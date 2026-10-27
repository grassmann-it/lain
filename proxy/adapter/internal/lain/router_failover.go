package lain

import (
	"bytes"
	"context"
	"crypto/sha256"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"strconv"
	"sync"
	"time"

	"github.com/gin-gonic/gin"
	auth "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/auth"
	"github.com/tidwall/sjson"
)

// One actual provider invocation per member, even if the SDK's retry policy
// requests another execution. This state belongs to one isolated handler call.
type routerDispatch struct {
	mu                                                   sync.Mutex
	claimed, finished, quota, output, accountQuota, sent bool
	retryAfter                                           time.Duration
	rejectionKey                                         routerRejectionKey
	haveRejectionKey                                     bool
}

func routerDispatchFor(ctx interface{ Value(any) any }) *routerDispatch {
	if c, ok := ctx.Value("gin").(*gin.Context); ok && c != nil {
		value, _ := c.Get("lain.router_dispatch")
		state, _ := value.(*routerDispatch)
		return state
	}
	return nil
}
func (s *routerDispatch) claim() error {
	if s == nil {
		return nil
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.claimed {
		return closed(errors.New("router member already dispatched"))
	}
	s.claimed = true
	return nil
}
func routerCanceled(ctx context.Context) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	if c, ok := ctx.Value("gin").(*gin.Context); ok && c != nil && c.Request != nil {
		return c.Request.Context().Err()
	}
	return nil
}
func claimRouterSend(ctx context.Context) error {
	s := routerDispatchFor(ctx)
	if s == nil {
		return nil
	}
	if err := routerCanceled(ctx); err != nil {
		return err
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.sent {
		return closed(errors.New("router member inference already sent"))
	}
	s.sent = true
	return nil
}
func observeRouterQuotaPayload(ctx context.Context, payload []byte) {
	s := routerDispatchFor(ctx)
	if s == nil {
		return
	}
	for _, line := range bytes.Split(payload, []byte("\n")) {
		line = bytes.TrimSpace(bytes.TrimPrefix(bytes.TrimSpace(line), []byte("data:")))
		if routerQuotaPayload(line) {
			s.noteQuota(0, routerAccountQuotaPayload(line))
		}
	}
}

func (s *routerDispatch) finish(err error) {
	if s == nil {
		return
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	s.finished = true
	s.quota = s.quota || routerQuotaError(err)
	if err != nil {
		s.accountQuota = s.accountQuota || routerAccountQuotaPayload([]byte(err.Error()))
	}
	// A successful response must not inherit a transient transport rejection.
	if err == nil || errors.Is(err, context.Canceled) || errors.Is(err, context.DeadlineExceeded) {
		s.quota = false
	}
}
func (s *routerDispatch) retryable() bool {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.claimed && s.finished && s.quota && !s.output
}

func (s *routerDispatch) markOutput(payload []byte) {
	if s == nil || len(payload) == 0 {
		return
	}
	s.mu.Lock()
	s.output = true
	s.mu.Unlock()
}
func (s *routerDispatch) noteQuota(delay time.Duration, accountQuota bool) {
	if s == nil {
		return
	}
	s.mu.Lock()
	s.quota = true
	s.retryAfter = delay
	s.accountQuota = s.accountQuota || accountQuota
	s.mu.Unlock()
}

// Runtime rejection cooldowns are bound to the actual credential material in
// RAM (only a digest is retained). Replacement/refresh cannot inherit one.
type routerRejectionKey struct {
	account, credential, model string
	generation                 [32]byte
}

func routerRejectionIdentity(route Route, a *auth.Auth) (routerRejectionKey, bool) {
	if a == nil || len(route.Accounts) != 1 {
		return routerRejectionKey{}, false
	}
	raw, err := json.Marshal(struct {
		Attributes map[string]string
		Metadata   map[string]any
	}{a.Attributes, a.Metadata})
	if err != nil {
		return routerRejectionKey{}, false
	}
	return routerRejectionKey{account: route.Accounts[0], credential: a.ID, model: route.Model, generation: sha256.Sum256(raw)}, true
}
func (i *Integration) routerMemberBlocked(router CollectionRouter, route Route, manager *auth.Manager) bool {
	now := time.Now()
	if i.routerAvailability.blocked(router, route, now) {
		return true
	}
	a := routerCredential(i, route, manager)
	key, ok := routerRejectionIdentity(route, a)
	if !ok {
		return false
	}
	i.routerAvailability.mu.RLock()
	until := i.routerAvailability.rejections[key]
	key.model = ""
	accountUntil := i.routerAvailability.rejections[key]
	i.routerAvailability.mu.RUnlock()
	return now.Before(until) || now.Before(accountUntil)
}
func routerCredential(i *Integration, route Route, manager *auth.Manager) *auth.Auth {
	if len(route.Accounts) != 1 {
		return nil
	}
	for _, a := range manager.List() {
		if a.ID == i.Credential(route, route.Accounts[0]) {
			return a
		}
	}
	return nil
}
func (i *Integration) coolRouterAccount(state *routerDispatch) {
	state.mu.Lock()
	key, ok := state.rejectionKey, state.haveRejectionKey
	delay := state.retryAfter
	if state.accountQuota {
		key.model = ""
	}
	state.mu.Unlock()
	if !ok {
		return
	}
	if delay <= 0 {
		delay = 30 * time.Second
	}
	if delay > 5*time.Minute {
		delay = 5 * time.Minute
	}
	now := time.Now()
	a := i.routerAvailability
	a.mu.Lock()
	defer a.mu.Unlock()
	if a.rejections == nil {
		a.rejections = map[routerRejectionKey]time.Time{}
	}
	for old, until := range a.rejections {
		if !now.Before(until) {
			delete(a.rejections, old)
		}
	}
	if len(a.rejections) < maxRouterPins {
		a.rejections[key] = now.Add(delay)
	}
}

// Observe only actual rejected inference responses, before SDK error sanitizing.
// Preserve every byte for the provider parser; never inspect successful streams.
func observeRouterQuotaResponse(ctx context.Context, response *http.Response) {
	state := routerDispatchFor(ctx)
	if state == nil || response == nil || response.StatusCode < 400 {
		return
	}
	quota := response.StatusCode == http.StatusTooManyRequests
	accountQuota := false
	if response.Body != nil {
		original := response.Body
		raw, err := io.ReadAll(io.LimitReader(original, maxRouterErrorBytes+1))
		response.Body = &requestErrorReplayBody{prefix: bytes.NewReader(raw), original: original, readErr: err}
		quota = quota || err == nil && len(raw) <= maxRouterErrorBytes && routerQuotaPayload(raw)
		accountQuota = err == nil && len(raw) <= maxRouterErrorBytes && routerAccountQuotaPayload(raw)
	}
	if quota {
		delay := time.Duration(0)
		value := response.Header.Get("Retry-After")
		if seconds, err := strconv.ParseInt(value, 10, 32); err == nil && seconds > 0 {
			delay = time.Duration(seconds) * time.Second
		} else if reset, err := http.ParseTime(value); err == nil {
			delay = time.Until(reset)
		}
		state.noteQuota(delay, accountQuota)
	}
}

var errRouterExhausted = errors.New("all eligible router members have exhausted quota or are unavailable")

// Deliberately no prose matching: authentication, billing, context limits and
// arbitrary provider failures cannot be mistaken for safe quota rejection.
func routerQuotaPayload(raw []byte) bool        { quota, _ := routerQuotaCodes(raw); return quota }
func routerAccountQuotaPayload(raw []byte) bool { _, account := routerQuotaCodes(raw); return account }
func routerQuotaCodes(raw []byte) (bool, bool) {
	// Reject duplicate controls at every inspected level; prose and malformed JSON
	// never provide evidence for a non-429 quota classification.
	fields, err := toolControlFields(raw, "code", "type", "status", "error", "response")
	if err != nil {
		return false, false
	}
	objects := []map[string]json.RawMessage{fields}
	if response, exists := fields["response"]; exists {
		responseFields, err := toolControlFields(response, "error")
		if err != nil {
			return false, false
		}
		if detail, exists := responseFields["error"]; exists && string(detail) != "null" {
			object, err := toolControlFields(detail, "code", "type", "status")
			if err != nil {
				return false, false
			}
			objects = append(objects, object)
		}
	}
	if detail, exists := fields["error"]; exists && string(detail) != "null" {
		object, err := toolControlFields(detail, "code", "type", "status")
		if err != nil {
			return false, false
		}
		objects = append(objects, object)
	}
	quota, account := false, false
	for _, object := range objects {
		for _, field := range []string{"code", "type", "status"} {
			var code string
			if json.Unmarshal(object[field], &code) != nil {
				continue
			}
			switch code {
			case "exceeded_current_quota_error", "insufficient_quota", "usage_limit_reached":
				quota, account = true, true
			case "rate_limit_exceeded", "rate_limit_error", "quota_exceeded", "quota_exhausted", "RESOURCE_EXHAUSTED":
				quota = true
			case "context_length_exceeded", "invalid_api_key", "authentication_error", "permission_denied":
				return false, false
			}
		}
	}
	return quota, account
}

// Stop the SDK's retries inside this member. The outer router owns the only
// bounded replay loop; preserve original error identity/status for its journal.
type routerAttemptError struct{ error }

func (e *routerAttemptError) Unwrap() error       { return e.error }
func (*routerAttemptError) IsRequestScoped() bool { return true }
func (e *routerAttemptError) StatusCode() int {
	var status interface{ StatusCode() int }
	if errors.As(e.error, &status) {
		return status.StatusCode()
	}
	return http.StatusBadGateway
}
func stopRouterRetry(ctx context.Context, err error) error {
	if err != nil && routerDispatchFor(ctx) != nil {
		return &routerAttemptError{err}
	}
	return err
}

func routerQuotaError(err error) bool {
	if err == nil {
		return false
	}
	var status interface{ StatusCode() int }
	if errors.As(err, &status) && status.StatusCode() == http.StatusTooManyRequests {
		return true
	}
	return routerQuotaPayload([]byte(err.Error()))
}

// Buffer only failed HTTP responses. Every success byte (including keepalives
// and tool events), explicit successful header commit and flush commits forever.
// The original caller body is separately bounded by the existing 32 MiB gate.
const maxRouterErrorBytes = 64 << 10

type routerResponseWriter struct {
	gin.ResponseWriter
	header       http.Header
	status, size int
	committed    bool
	pending      bytes.Buffer
}

func newRouterResponseWriter(w gin.ResponseWriter) *routerResponseWriter {
	return &routerResponseWriter{ResponseWriter: w, header: w.Header().Clone(), status: 200, size: -1}
}
func (w *routerResponseWriter) Header() http.Header { return w.header }
func (w *routerResponseWriter) Status() int         { return w.status }
func (w *routerResponseWriter) Size() int           { return w.size }
func (w *routerResponseWriter) Written() bool       { return w.size >= 0 }
func (w *routerResponseWriter) WriteHeader(code int) {
	if !w.Written() && code > 0 {
		w.status = code
	}
}
func (w *routerResponseWriter) WriteHeaderNow() {
	if w.size < 0 {
		w.size = 0
	}
	if w.status < 400 {
		w.commit()
	}
}
func (w *routerResponseWriter) commit() {
	if w.committed {
		return
	}
	w.committed = true
	target := w.ResponseWriter.Header()
	for name := range target {
		delete(target, name)
	}
	for name, values := range w.header {
		target[name] = append([]string(nil), values...)
	}
	w.ResponseWriter.WriteHeader(w.status)
	w.ResponseWriter.WriteHeaderNow()
	if w.pending.Len() > 0 {
		_, _ = w.ResponseWriter.Write(w.pending.Bytes())
		w.pending.Reset()
	}
}
func (w *routerResponseWriter) Write(data []byte) (int, error) {
	if w.size < 0 {
		w.size = 0
	}
	if !w.committed && w.status >= 400 && len(data) <= maxRouterErrorBytes-w.pending.Len() {
		n, err := w.pending.Write(data)
		w.size += n
		return n, err
	}
	w.commit()
	n, err := w.ResponseWriter.Write(data)
	w.size += n
	return n, err
}
func (w *routerResponseWriter) WriteString(data string) (int, error) { return w.Write([]byte(data)) }
func (w *routerResponseWriter) Flush()                               { w.commit(); w.ResponseWriter.Flush() }
func (w *routerResponseWriter) FlushError() error {
	w.commit()
	if f, ok := w.ResponseWriter.(interface{ FlushError() error }); ok {
		return f.FlushError()
	}
	w.ResponseWriter.Flush()
	return nil
}

func routerPinFor(router CollectionRouter, route Route) routerPin {
	return routerPin{RouteID: route.ID, AccountID: route.Accounts[0], Fingerprint: router.MemberFingerprints[route.ID]}
}
func (i *Integration) nextRouterRoute(router CollectionRouter, current Route, alias string, body []byte, headers http.Header, manager *auth.Manager, exhaustedAccounts map[string]bool) (Route, error) {
	byID, eligible := i.routerCandidates(router, alias, body, headers, manager)
	after := false
	for _, id := range router.RouteIDs {
		if id == current.ID {
			after = true
			continue
		}
		if after && (current.CostPolicy != "free_only" || byID[id].CostPolicy == "free_only") && !exhaustedAccounts[byID[id].Accounts[0]] && eligible(byID[id]) == nil && !i.routerMemberBlocked(router, byID[id], manager) {
			return byID[id], nil
		}
	}
	return Route{}, errRouterExhausted
}

// Existing threads can migrate only with an explicit transcript, never a lone
// user turn or tool result. Fullness remains a caller contract: no prompt history
// is stored here to reconstruct or prove omitted ordinary messages.
func routerTranscriptReplay(body []byte) bool {
	fields, err := toolControlFields(body, "input")
	if err != nil {
		return false
	}
	var items []struct {
		Type   string `json:"type"`
		Role   string `json:"role"`
		CallID string `json:"call_id"`
	}
	if json.Unmarshal(fields["input"], &items) != nil {
		return false
	}
	user, assistant := false, false
	calls := map[string]bool{}
	for _, item := range items {
		switch {
		case item.Type == "function_call":
			if item.CallID == "" || calls[item.CallID] {
				return false
			}
			calls[item.CallID] = true
			assistant = true
		case item.Type == "function_call_output":
			if !calls[item.CallID] {
				return false
			}
			delete(calls, item.CallID)
		case item.Type != "" && item.Type != "message":
			return false
		case item.Role == "user":
			user = true
		case item.Role == "assistant":
			assistant = true
		case item.Role == "system" || item.Role == "developer":
		default:
			return false
		}
	}
	return user && assistant && len(calls) == 0
}

func (i *Integration) serveCollectionRouter(c *gin.Context, router CollectionRouter, alias string, original []byte, manager *auth.Manager) {
	// Keep selection constraints before stripping internal headers downstream.
	headers := c.Request.Header.Clone()
	thread, _, _ := routerRequest(original, headers)
	i.routerPins.mu.Lock()
	persisted, pinErr := i.routerPins.read()
	i.routerPins.mu.Unlock()
	if pinErr != nil {
		c.AbortWithStatusJSON(409, gin.H{"error": "persistent router state unavailable; explicit recovery required"})
		return
	}
	existing := false
	for _, pin := range persisted.Pins {
		if pin.RouterID == router.ID && pin.ProfileID == router.ProfileID && pin.ThreadID == thread {
			existing = true
			break
		}
	}
	replaySafe := !existing || routerTranscriptReplay(original)
	route, err := i.routerRoute(router, alias, original, headers, manager)
	if err != nil {
		if errors.Is(err, errRouterExhausted) {
			c.AbortWithStatusJSON(429, gin.H{"error": gin.H{"code": "router_quota_exhausted", "message": err.Error()}})
			return
		}
		c.AbortWithStatusJSON(409, gin.H{"error": gin.H{"code": "router_unavailable", "message": err.Error()}})
		return
	}
	downstream := c.Handler()
	template := c.Copy()
	clientWriter := c.Writer
	first := true
	exhaustedAccounts := map[string]bool{}
	for n := 0; n < len(router.RouteIDs); n++ {
		if c.Request.Context().Err() != nil {
			c.Abort()
			return
		}
		if !i.routerMemberBlocked(router, route, manager) {
			// Start from the preserved caller bytes every time, changing only model.
			replay, replayErr := sjson.SetBytes(original, "model", route.Model)
			if replayErr != nil {
				c.AbortWithStatus(400)
				return
			}
			call := c
			if !first {
				call = template.Copy()
			}
			call.Request = template.Request.Clone(template.Request.Context())
			call.Request.Body = io.NopCloser(bytes.NewReader(replay))
			call.Request.ContentLength = int64(len(replay))
			call.Request.Header.Del("X-Lain-Account-ID")
			call.Request.Header.Del("X-Lain-Selection")
			allowed := map[string]bool{i.Credential(route, route.Accounts[0]): true}
			call.Set("lain.pinned_auth_id", i.Credential(route, route.Accounts[0]))
			call.Set("lain.allowed_auth_ids", allowed)
			call.Set("lain.selection", selection{Route: route, Alias: alias, Allowed: allowed})
			call.Set("lain.original_request", original)
			state := &routerDispatch{}
			call.Set("lain.router_dispatch", state)
			writer := newRouterResponseWriter(clientWriter)
			call.Writer = writer
			if first {
				c.Next()
				for _, key := range []string{"userApiKey", "accessProvider", "accessMetadata"} {
					if value, exists := c.Get(key); exists {
						template.Set(key, value)
					}
				}
				template.Request = template.Request.WithContext(c.Request.Context())
			} else {
				downstream(call)
			}
			first = false
			c.Writer = clientWriter
			if writer.committed || writer.status < 400 || !state.retryable() || c.Request.Context().Err() != nil || i.Journal.Health() != nil {
				writer.commit()
				c.Abort()
				return
			}
			state.mu.Lock()
			if state.accountQuota {
				exhaustedAccounts[route.Accounts[0]] = true
			}
			state.mu.Unlock()
			i.coolRouterAccount(state)
		}
		if !replaySafe {
			c.Writer = clientWriter
			c.AbortWithStatusJSON(409, gin.H{"error": gin.H{"code": "router_replay_required", "message": "quota migration supports replayable full message/function history with paired calls and results; opaque reasoning/native/custom records cannot migrate; start a new thread"}})
			return
		}
		next, nextErr := i.nextRouterRoute(router, route, alias, original, headers, manager, exhaustedAccounts)
		if nextErr != nil {
			c.Writer = clientWriter
			c.AbortWithStatusJSON(429, gin.H{"error": gin.H{"code": "router_quota_exhausted", "message": nextErr.Error()}})
			return
		}
		if c.Request.Context().Err() != nil {
			c.Abort()
			return
		}
		if err := i.routerPins.replace(router, thread, routerPinFor(router, route), routerPinFor(router, next)); err != nil {
			c.Writer = clientWriter
			c.AbortWithStatusJSON(409, gin.H{"error": gin.H{"code": "router_pin_conflict", "message": err.Error()}})
			return
		}
		route = next
	}
	c.AbortWithStatusJSON(429, gin.H{"error": gin.H{"code": "router_quota_exhausted", "message": "router member budget exhausted"}})
}
