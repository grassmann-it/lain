package lain

import (
	"bytes"
	"encoding/json"
	"io"
	"net/http"
	"sync"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
)

const maxRouterAvailabilityBytes = 1 << 20

type routerAvailabilityEntry struct {
	RouterID          string    `json:"routerId"`
	ProfileID         string    `json:"profileId"`
	RouteID           string    `json:"routeId"`
	MemberFingerprint string    `json:"memberFingerprint"`
	AccountID         string    `json:"accountId"`
	Provider          string    `json:"provider"`
	Model             string    `json:"model"`
	BlockedUntil      time.Time `json:"blockedUntil"`
	Reason            string    `json:"reason"`
}
type routerAvailabilitySnapshot struct {
	Version    int                       `json:"schemaVersion"`
	Generation string                    `json:"generation"`
	Sequence   uint64                    `json:"sequence"`
	ObservedAt time.Time                 `json:"observedAt"`
	ExpiresAt  time.Time                 `json:"expiresAt"`
	Entries    []routerAvailabilityEntry `json:"entries"`
}
type routerAvailability struct {
	mu         sync.RWMutex
	generation string
	snapshot   routerAvailabilitySnapshot
	rejections map[routerRejectionKey]time.Time
}

func newRouterAvailability() *routerAvailability {
	return &routerAvailability{generation: uuid.NewString()}
}

// Memory-only and fenced by a fresh core generation. Restart, reset, expiry and
// semantic mismatches all restore unknown availability, never an inferred quota.
func (a *routerAvailability) blocked(router CollectionRouter, route Route, now time.Time) bool {
	if a == nil {
		return false
	}
	a.mu.RLock()
	defer a.mu.RUnlock()
	s := a.snapshot
	if now.Before(s.ObservedAt) || !now.Before(s.ExpiresAt) {
		return false
	}
	for _, e := range s.Entries {
		if e.RouterID == router.ID && e.ProfileID == router.ProfileID && e.RouteID == route.ID &&
			e.MemberFingerprint == router.MemberFingerprints[route.ID] && len(route.Accounts) == 1 && e.AccountID == route.Accounts[0] &&
			e.Provider == route.Provider && e.Model == route.Model && now.Before(e.BlockedUntil) {
			return true
		}
	}
	return false
}
func (i *Integration) handleRouterAvailability(c *gin.Context) {
	a := i.routerAvailability
	c.Header("Cache-Control", "no-store")
	if c.Request.Method == http.MethodGet {
		a.mu.RLock()
		sequence := a.snapshot.Sequence
		a.mu.RUnlock()
		c.AbortWithStatusJSON(200, gin.H{"schemaVersion": 1, "generation": a.generation, "sequence": sequence})
		return
	}
	if c.Request.Method != http.MethodPost {
		c.AbortWithStatus(405)
		return
	}
	raw, err := io.ReadAll(io.LimitReader(c.Request.Body, maxRouterAvailabilityBytes+1))
	if len(raw) > maxRouterAvailabilityBytes {
		c.AbortWithStatus(413)
		return
	}
	if err != nil {
		c.AbortWithStatus(400)
		return
	}
	var s routerAvailabilitySnapshot
	decoder := json.NewDecoder(bytes.NewReader(raw))
	decoder.DisallowUnknownFields()
	if decoder.Decode(&s) != nil || decoder.Decode(new(any)) != io.EOF {
		c.AbortWithStatus(400)
		return
	}
	// Check the process fence before any mutation, including resets.
	if s.Generation != a.generation {
		c.AbortWithStatus(409)
		return
	}
	now := time.Now()
	if s.Version != 1 || s.Entries == nil || len(s.Entries) > maxRouterPins || s.ObservedAt.IsZero() ||
		s.ObservedAt.After(now) || !s.ExpiresAt.After(s.ObservedAt) || s.ExpiresAt.Sub(s.ObservedAt) > 5*time.Minute {
		c.AbortWithStatus(400)
		return
	}
	seen := map[string]bool{}
	for _, e := range s.Entries {
		key := e.RouterID + "\n" + e.ProfileID + "\n" + e.RouteID
		if seen[key] || safeDimension(e.RouterID) == "" || safeDimension(e.ProfileID) == "" || safeDimension(e.RouteID) == "" ||
			safeDimension(e.AccountID) == "" || len(e.Provider) > 256 || e.Model == "" || len(e.Model) > 1024 ||
			!validRouterFingerprint(e.MemberFingerprint) || !e.BlockedUntil.After(s.ObservedAt) || e.BlockedUntil.After(s.ExpiresAt) ||
			(e.Reason != "quota_exhausted" && e.Reason != "rate_limited") {
			c.AbortWithStatus(400)
			return
		}
		seen[key] = true
	}
	a.mu.Lock()
	// Do not let a delayed publisher overwrite a newer observation.
	if a.snapshot.Sequence == ^uint64(0) || s.Sequence != a.snapshot.Sequence+1 || s.ObservedAt.Before(a.snapshot.ObservedAt) {
		a.mu.Unlock()
		c.AbortWithStatus(409)
		return
	}
	a.snapshot = s
	a.mu.Unlock()
	c.AbortWithStatus(204)
}
