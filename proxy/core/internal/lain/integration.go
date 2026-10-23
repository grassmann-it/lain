package lain

import (
	"context"
	"errors"
	"fmt"
	"github.com/gin-gonic/gin"
	auth "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/auth"
	executor "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/executor"
	"github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/usage"
	"net/http"
	"sync"
	"time"
)

type Route struct {
	Dimensions          *Dimensions       `json:"dimensions,omitempty"`
	CostPolicy          string            `json:"costPolicy,omitempty"`
	PricingClass        string            `json:"pricingClass,omitempty"`
	FreeOnlyEnforcement string            `json:"freeOnlyEnforcement,omitempty"`
	Provider            string            `json:"provider,omitempty"`
	Credentials         map[string]string `json:"credentials,omitempty"`
	Visible             *bool             `json:"visible,omitempty"`
	ID                  string            `json:"id"`
	Model               string            `json:"model"`
	Accounts            []string          `json:"accounts"`
	Selection           string            `json:"selection"`
}
type Bindings struct {
	AccountDimensions map[string]*Dimensions `json:"accountDimensions,omitempty"`
	Accounts          map[string]string      `json:"accounts"`
	Routes            map[string]Route       `json:"routes"`
}
type selection struct {
	Route   Route
	Alias   string
	Allowed map[string]bool
}
type attempt struct {
	event          Event
	reported       bool
	usage          *Event
	started        time.Time
	firstOutput    time.Time
	content        map[string]*captureBuffer
	captureExpires time.Time
}
type Integration struct {
	Login        *LoginManager
	Journal      *Journal
	Bindings     Bindings
	Inspector    *Inspector
	mu           sync.Mutex
	active       map[string]*attempt
	captureSlots int
}
type dispatchError struct{ message string }

func (e *dispatchError) Error() string         { return e.message }
func (e *dispatchError) StatusCode() int       { return 503 }
func (e *dispatchError) IsRequestScoped() bool { return true }
func closed(err error) error {
	if err == nil {
		return nil
	}
	return &dispatchError{err.Error()}
}
func New(j *Journal, b Bindings) (*Integration, error) {
	seen := map[string]string{}
	for account, credential := range b.Accounts {
		if account == "" || credential == "" || seen[credential] != "" {
			return nil, errors.New("account bindings must be unique and nonempty")
		}
		seen[credential] = account
	}
	for alias, r := range b.Routes {
		if r.CostPolicy != "" && r.CostPolicy != "configured" && r.CostPolicy != "free_only" {
			return nil, errors.New("unknown route cost policy")
		}
		if r.CostPolicy == "free_only" {
			return nil, errors.New("free_only route has no qualified runtime enforcement")
		}
		if alias == "" || r.ID == "" || r.Model == "" || len(r.Accounts) == 0 {
			return nil, errors.New("incomplete route")
		}
		if r.Selection != "strict" && r.Selection != "pool" {
			return nil, errors.New("route selection must be strict or pool")
		}
		if r.Selection == "strict" && len(r.Accounts) != 1 {
			return nil, errors.New("strict route requires one account")
		}
		members := map[string]bool{}
		for _, id := range r.Accounts {
			members[id] = true
		}
		for account, credential := range r.Credentials {
			if !members[account] || credential == "" || (seen[credential] != "" && seen[credential] != account) {
				return nil, errors.New("route credential identity conflicts with account membership")
			}
			seen[credential] = account
		}
		for _, id := range r.Accounts {
			if b.Accounts[id] == "" {
				return nil, errors.New("route refers to unknown account")
			}
		}
	}
	return &Integration{Journal: j, Bindings: b, Inspector: newInspector(), active: map[string]*attempt{}}, nil
}
func (i *Integration) Install() {
	auth.LainBeforeDispatch = i.Before
	auth.LainAfterDispatch = i.After
	auth.LainObserveOutput = i.Observe
	auth.LainReadOnlyCredentials = true
	usage.DurableSink = i.Publish
}
func routeSelection(ctx context.Context) (selection, bool) {
	if c, ok := ctx.Value("gin").(*gin.Context); ok && c != nil {
		v, ok := c.Get("lain.selection")
		if ok {
			s, ok := v.(selection)
			return s, ok
		}
	}
	return selection{}, false
}
func (i *Integration) Before(ctx context.Context, a *auth.Auth, req executor.Request, opts executor.Options) error {
	if err := i.Journal.Health(); err != nil {
		return closed(err)
	}
	selected, ok := routeSelection(ctx)
	if !ok || a == nil || !selected.Allowed[a.ID] {
		return closed(errors.New("dispatch identity is outside the selected route"))
	}
	// Preserve the public alias for upstream payload rules after route model translation.
	if opts.Metadata != nil {
		opts.Metadata[executor.RequestedModelMetadataKey] = selected.Alias
	}
	accountID := ""
	for _, id := range selected.Route.Accounts {
		if i.Credential(selected.Route, id) == a.ID {
			accountID = id
			break
		}
	}
	requestID := usage.ParentRequestIDFromContext(ctx)
	attemptID := usage.ExecutionRequestIDFromContext(ctx)
	if accountID == "" || requestID == "" || attemptID == "" {
		return closed(errors.New("dispatch identity missing"))
	}
	provider := a.Provider
	if selected.Route.Provider != "" {
		provider = selected.Route.Provider
	}
	var headers http.Header
	if c, ok := ctx.Value("gin").(*gin.Context); ok && c != nil && c.Request != nil {
		headers = c.Request.Header
	}
	payload := opts.OriginalRequest
	if len(payload) == 0 {
		payload = req.Payload
	}
	dimensions := requestDimensions(selected.Route, i.Bindings.AccountDimensions[accountID], headers, payload)
	started := time.Now()
	event := Event{Kind: "attempt_started", RequestID: requestID, AttemptID: attemptID, AccountID: accountID, CredentialID: a.ID, RouteID: &selected.Route.ID, ObservedAt: started.UTC(), Provider: provider, RequestedModel: selected.Alias, Model: req.Model, Dimensions: dimensions}
	i.mu.Lock()
	defer i.mu.Unlock()
	i.pruneCapture(started)
	if err := i.Journal.Append(event); err != nil {
		return closed(err)
	}
	current := &attempt{event: event, started: started}
	if i.captureSlots < 256 {
		current.content = map[string]*captureBuffer{}
		current.captureExpires = started.Add(15 * time.Minute)
		i.captureSlots++
	}
	i.active[attemptID] = current
	i.Inspector.append(liveEvent(event, "attempt_started"))
	if current.content == nil {
		omitted := liveEvent(event, "metadata")
		omitted.Text, omitted.Truncated = "[content omitted: capture capacity]", true
		i.Inspector.append(omitted)
	}
	return nil
}
func ptr[T any](v T) *T { return &v }
func (i *Integration) Publish(ctx context.Context, r usage.Record) error {
	id := usage.ExecutionRequestIDFromContext(ctx)
	if id == "" {
		return nil
	} // Non-dispatch maintenance records are not inference attempts.
	i.mu.Lock()
	defer i.mu.Unlock()
	a := i.active[id]
	if a == nil {
		return closed(errors.New("usage has no registered attempt"))
	}
	e := a.event
	e.Dimensions = copyDimensions(a.event.Dimensions)
	e.Kind = "usage"
	e.ObservedAt = time.Now().UTC()
	e.Outcome = ptr("success")
	if r.Failed {
		e.Outcome = ptr("failed")
	}
	if r.Outcome == "canceled" || errors.Is(ctx.Err(), context.Canceled) {
		e.Outcome = ptr("canceled")
	}
	if known := r.Detail.Reported; known != nil && (r.UsageReported == nil || *r.UsageReported) {
		e.InputTokens = known.InputTokens
		e.OutputTokens = known.OutputTokens
		e.TotalTokens = known.TotalTokens
		e.CacheReadTokens = known.CacheReadTokens
		e.CacheCreationTokens = known.CacheCreationTokens
		e.ReasoningTokens = known.ReasoningTokens
		e.UsageReported = e.InputTokens != nil || e.OutputTokens != nil || e.TotalTokens != nil || e.CacheReadTokens != nil || e.CacheCreationTokens != nil || e.ReasoningTokens != nil
	}
	if r.Fail.StatusCode >= 100 && r.Fail.StatusCode <= 599 {
		e.StatusCode = ptr(r.Fail.StatusCode)
	} else if !r.Failed {
		e.StatusCode = ptr(200)
	}
	if r.Latency > 0 {
		e.LatencyMs = ptr(float64(r.Latency) / float64(time.Millisecond))
	}
	if r.TTFT > 0 {
		e.TTFTMs = ptr(float64(r.TTFT) / float64(time.Millisecond))
	}
	if r.ResponseModel != "" {
		e.Model = r.ResponseModel
	} else if r.Model != "" {
		e.Model = r.Model
	}
	if r.RequestID != id || r.ParentRequestID != e.RequestID || r.AuthID != e.CredentialID {
		return closed(i.fault(fmt.Errorf("usage identity mismatch for registered attempt")))
	}
	if a.reported {
		return nil
	}
	if safeDimension(r.ReasoningEffort) != "" {
		if e.Dimensions == nil {
			e.Dimensions = &Dimensions{}
		}
		e.Dimensions.set("effectiveReasoningEffort", &e.Dimensions.EffectiveReasoningEffort, r.ReasoningEffort, "core", "usage.reasoningEffort")
	}
	if !a.firstOutput.IsZero() {
		e.Measurements = a.measurements(time.Time{})
	}
	if err := i.Journal.Append(e); err != nil {
		return closed(err)
	}
	a.reported = true
	a.usage = &e
	a.event.Model = e.Model
	a.event.Dimensions = copyDimensions(e.Dimensions)
	return nil
}
func (i *Integration) fault(err error) error {
	i.Journal.mu.Lock()
	defer i.Journal.mu.Unlock()
	return i.Journal.fail(err)
}
func (i *Integration) After(ctx context.Context, resultErr error) error {
	finished := time.Now()
	i.mu.Lock()
	defer i.mu.Unlock()
	i.pruneCapture(finished)
	id := usage.ExecutionRequestIDFromContext(ctx)
	a := i.active[id]
	if a == nil {
		return closed(i.Journal.Health())
	}
	defer func() {
		if a.content != nil {
			i.captureSlots--
		}
		delete(i.active, id)
	}()
	e := a.event
	e.Kind = "usage"
	e.ObservedAt = finished.UTC()
	e.Measurements = a.measurements(finished)
	e.Outcome = ptr("success")
	if a.usage != nil && a.usage.Outcome != nil {
		e.Outcome = a.usage.Outcome
	}
	if resultErr != nil {
		e.Outcome = ptr("failed")
		var status interface{ StatusCode() int }
		if errors.As(resultErr, &status) {
			e.StatusCode = ptr(status.StatusCode())
		}
	}
	if errors.Is(resultErr, context.Canceled) || errors.Is(ctx.Err(), context.Canceled) {
		e.Outcome = ptr("canceled")
	}
	e.CompletionState = e.Outcome
	final := liveEvent(e, "attempt_finished")
	if a.usage != nil {
		final.InputTokens, final.OutputTokens, final.TotalTokens, final.StatusCode = a.usage.InputTokens, a.usage.OutputTokens, a.usage.TotalTokens, a.usage.StatusCode
	}
	if a.reported {
		// The early durable usage fact stays immutable. Completion is a separate
		// metadata-only fact, and can report cancellation after usage arrived.
		e.Kind, e.Outcome, e.StatusCode = "attempt_finished", nil, nil
	}
	if err := i.Journal.Append(e); err != nil {
		final.Outcome = ptr("failed")
		i.Inspector.append(final)
		return closed(err)
	}
	for kind, buffer := range a.content {
		for _, text := range buffer.consume("", true) {
			line := liveEvent(a.event, kind)
			line.ObservedAt = finished.UTC()
			line.Text = text
			i.Inspector.append(line)
		}
	}
	i.Inspector.append(final)
	return nil
}

// Called with i.mu held. As with ring retention, expired content is purged on
// observation/read. Never flush a stale partial line into a new retention window.
func (i *Integration) pruneCapture(now time.Time) {
	for _, a := range i.active {
		if a.content != nil && !now.Before(a.captureExpires) {
			a.content = nil
			i.captureSlots--
		}
	}
}

// Credential resolves a transport-specific credential without changing the logical account.
func (i *Integration) Credential(route Route, account string) string {
	if credential := route.Credentials[account]; credential != "" {
		return credential
	}
	return i.Bindings.Accounts[account]
}
