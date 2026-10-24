package lain

import (
	"bytes"
	"crypto/subtle"
	"encoding/json"
	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy"
	auth "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/auth"
	"io"
	"lain.local/proxy/internal/freeonly"
	"sort"
	"strconv"
	"strings"
	"time"
)

// Middleware is installed before core routes. Only the documented inference surface is exposed.
func (i *Integration) Middleware(managementToken string, manager *auth.Manager, inferenceTokens ...string) gin.HandlerFunc {
	return func(c *gin.Context) {
		path := c.Request.URL.Path
		c.Set("lain.request_id", uuid.NewString())
		if strings.HasPrefix(path, "/lain/") {
			received := strings.TrimPrefix(c.GetHeader("Authorization"), "Bearer ")
			if !strings.HasPrefix(c.GetHeader("Authorization"), "Bearer ") || subtle.ConstantTimeCompare([]byte(received), []byte(managementToken)) != 1 {
				c.AbortWithStatus(401)
				return
			}
			if i.Login != nil && i.Login.Handle(c) {
				return
			}
			switch path {
			case "/lain/quota/observations":
				if c.Request.Method != "GET" {
					c.AbortWithStatus(405)
					return
				}
				data, err := i.Quotas.read()
				c.Header("Cache-Control", "no-store")
				if err != nil {
					c.AbortWithStatusJSON(503, gin.H{"error": "passive quota observations unavailable"})
					return
				}
				c.Data(200, "application/json", data)
				c.Abort()
			case "/lain/inspector/events":
				if c.Request.Method != "GET" {
					c.AbortWithStatus(405)
					return
				}
				after, err := strconv.ParseInt(c.DefaultQuery("after", "0"), 10, 64)
				if err != nil {
					c.AbortWithStatus(400)
					return
				}
				limit, err := strconv.Atoi(c.DefaultQuery("limit", "1000"))
				if err != nil {
					c.AbortWithStatus(400)
					return
				}
				i.mu.Lock()
				i.pruneCapture(time.Now())
				i.mu.Unlock()
				batch, err := i.Inspector.Read(after, limit)
				if err != nil {
					c.AbortWithStatusJSON(400, gin.H{"error": err.Error()})
					return
				}
				c.Header("Cache-Control", "no-store")
				c.AbortWithStatusJSON(200, batch)
			case "/lain/events":
				if c.Request.Method != "GET" {
					c.AbortWithStatus(405)
					return
				}
				after, err := strconv.ParseInt(c.DefaultQuery("after", "0"), 10, 64)
				if err != nil {
					c.AbortWithStatus(400)
					return
				}
				limit, err := strconv.Atoi(c.DefaultQuery("limit", "1000"))
				if err != nil {
					c.AbortWithStatus(400)
					return
				}
				batch, err := i.Journal.Read(after, limit)
				if err != nil {
					c.AbortWithStatusJSON(400, gin.H{"error": err.Error()})
					return
				}
				c.AbortWithStatusJSON(200, batch)
			case "/lain/accounts":
				if c.Request.Method != "GET" {
					c.AbortWithStatus(405)
					return
				}
				accounts := []gin.H{}
				for _, a := range manager.List() {
					accounts = append(accounts, gin.H{"credentialId": a.ID, "provider": a.Provider, "disabled": a.Disabled})
				}
				c.AbortWithStatusJSON(200, accounts)
			case "/lain/health":
				ready := i.Journal.Health() == nil
				loaded := map[string]*auth.Auth{}
				for _, a := range manager.List() {
					loaded[a.ID] = a
				}
				for _, route := range i.Bindings.Routes {
					for _, id := range route.Accounts {
						a := loaded[i.Credential(route, id)]
						if a == nil || a.Disabled {
							ready = false
							continue
						}
						supported := false
						for _, m := range cliproxy.GlobalModelRegistry().GetModelsForClient(a.ID) {
							if m.ID == route.Model {
								supported = true
							}
						}
						if !supported {
							ready = false
						}
					}
				}
				status := 200
				if !ready {
					status = 503
				}
				c.AbortWithStatusJSON(status, gin.H{"ready": ready, "journalHealthy": i.Journal.Health() == nil, "version": "8.0.41-lain-adapter.1"})
			default:
				c.AbortWithStatus(404)
			}
			return
		}
		if path == "/v1/models" && c.Request.Method == "GET" {
			authorized := false
			received := strings.TrimPrefix(c.GetHeader("Authorization"), "Bearer ")
			for _, token := range inferenceTokens {
				if subtle.ConstantTimeCompare([]byte(received), []byte(token)) == 1 {
					authorized = true
				}
			}
			if !authorized {
				c.AbortWithStatus(401)
				return
			}
			aliases := make([]string, 0, len(i.Bindings.Routes))
			for alias, route := range i.Bindings.Routes {
				if route.Visible != nil && !*route.Visible {
					continue
				}
				aliases = append(aliases, alias)
			}
			sort.Strings(aliases)
			models := []gin.H{}
			for _, alias := range aliases {
				models = append(models, gin.H{"id": alias, "object": "model", "owned_by": "lain"})
			}
			c.AbortWithStatusJSON(200, gin.H{"object": "list", "data": models})
			return
		}
		if c.Request.Method != "POST" || (path != "/v1/chat/completions" && path != "/v1/responses" && path != "/v1/responses/compact" && path != "/v1/messages") {
			c.AbortWithStatus(404)
			return
		}
		if err := i.Journal.Health(); err != nil {
			c.AbortWithStatusJSON(503, gin.H{"error": "durable journal unavailable"})
			return
		}
		// Limit memory at this integration boundary; larger requests get an explicit rejection.
		body, err := io.ReadAll(io.LimitReader(c.Request.Body, 32*1024*1024+1))
		if err != nil || len(body) > 32*1024*1024 {
			c.AbortWithStatus(413)
			return
		}
		var payload map[string]json.RawMessage
		if json.Unmarshal(body, &payload) != nil {
			c.AbortWithStatus(400)
			return
		}
		if nativeImageToolRequested(body) {
			c.AbortWithStatusJSON(400, gin.H{"error": "native image_generation tools are outside the qualified lain usage contract"})
			return
		}
		var alias string
		if json.Unmarshal(payload["model"], &alias) != nil {
			c.AbortWithStatusJSON(400, gin.H{"error": "model route required"})
			return
		}
		route, ok := i.Bindings.Routes[alias]
		if !ok {
			c.AbortWithStatusJSON(400, gin.H{"error": "unknown model route"})
			return
		}
		if err := validateCostPolicy(route); err != nil {
			c.AbortWithStatusJSON(503, gin.H{"error": "route cost policy unavailable"})
			return
		}
		if route.CostPolicy == "free_only" {
			if path != "/v1/chat/completions" && path != "/v1/responses" {
				c.AbortWithStatusJSON(400, gin.H{"error": "free_only protocol is not qualified"})
				return
			}
			if err := freeonly.ValidateOpenRouterInput(body, alias, route.Model); err != nil {
				c.AbortWithStatusJSON(400, gin.H{"error": "free_only request controls are not qualified"})
				return
			}
		}
		allowed := map[string]bool{}
		for _, account := range route.Accounts {
			allowed[i.Credential(route, account)] = true
		}
		pin := c.GetHeader("X-Lain-Account-ID")
		if pin != "" {
			credential := i.Credential(route, pin)
			if !allowed[credential] {
				c.AbortWithStatusJSON(403, gin.H{"error": "account is outside route membership"})
				return
			}
			allowed = map[string]bool{credential: true}
			c.Set("lain.pinned_auth_id", credential)
		} else if route.Selection == "strict" {
			c.Set("lain.pinned_auth_id", i.Credential(route, route.Accounts[0]))
		}
		c.Set("lain.allowed_auth_ids", allowed)
		c.Set("lain.selection", selection{Route: route, Alias: alias, Allowed: allowed})
		// Do not forward internal selection metadata to an upstream provider.
		c.Request.Header.Del("X-Lain-Account-ID")
		c.Request.Header.Del("X-Lain-Selection")
		payload["model"], _ = json.Marshal(route.Model)
		body, _ = json.Marshal(payload)
		c.Request.Body = io.NopCloser(bytes.NewReader(body))
		c.Request.ContentLength = int64(len(body))
		c.Next()
	}
}
