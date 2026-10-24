package lain

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"strings"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy"
	auth "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/auth"
	executor "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/executor"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/usage"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/config"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/httptransport"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/translator"
	"github.com/tidwall/gjson"
	"lain.local/proxy/internal/freeonly"
)

type parentRequestKey struct{}

func WithParentRequestID(ctx context.Context, id string) context.Context {
	return context.WithValue(ctx, parentRequestKey{}, id)
}
func ParentRequestIDFromContext(ctx context.Context) string {
	if id, _ := ctx.Value(parentRequestKey{}).(string); id != "" {
		return id
	}
	if c, ok := ctx.Value("gin").(*gin.Context); ok && c != nil {
		return c.GetString("lain.request_id")
	}
	return ""
}
func SnapshotWatcher(string, string, func(*config.Config)) (*cliproxy.WatcherWrapper, error) {
	return &cliproxy.WatcherWrapper{}, nil
}

type selector struct{ inner auth.RoundRobinSelector }

func NewSelector() auth.Selector { return &selector{} }
func (s *selector) AllowCandidate(ctx context.Context, a *auth.Auth) bool {
	selected, ok := routeSelection(ctx)
	return ok && a != nil && selected.Allowed[a.ID]
}

func (s *selector) Pick(ctx context.Context, provider, model string, opts executor.Options, candidates []*auth.Auth) (*auth.Auth, error) {
	selected, ok := routeSelection(ctx)
	if !ok {
		return nil, closed(errors.New("route selection missing"))
	}
	allowed := make([]*auth.Auth, 0, len(candidates))
	for _, candidate := range candidates {
		if candidate != nil && selected.Allowed[candidate.ID] {
			allowed = append(allowed, candidate)
		}
	}
	return s.inner.Pick(ctx, provider, model, opts, allowed)
}

type boundExecutor struct {
	inner       auth.ProviderExecutor
	integration *Integration
	writable    func(*auth.Auth) bool
}

func (i *Integration) ExecutorDecorator(writable func(*auth.Auth) bool) func(auth.ProviderExecutor) auth.ProviderExecutor {
	return func(inner auth.ProviderExecutor) auth.ProviderExecutor {
		return &boundExecutor{inner: inner, integration: i, writable: writable}
	}
}
func (e *boundExecutor) Identifier() string { return e.inner.Identifier() }
func (e *boundExecutor) begin(ctx context.Context, a *auth.Auth, req executor.Request, opts executor.Options) (context.Context, error) {
	ctx = usage.WithExecutionRequestID(ctx, uuid.NewString())
	dispatchCtx := ctx
	ctx = usage.WithDurableSink(ctx, func(_ context.Context, record usage.Record) error { return e.integration.Publish(dispatchCtx, record) })
	var err error
	ctx, err = e.integration.DispatchContext(ctx, a, req, opts)
	if err != nil {
		return ctx, err
	}
	ctx = httptransport.WithDecorator(ctx, func(next http.RoundTripper) http.RoundTripper { return &guardedTransport{next: next, auth: a} })
	return ctx, e.integration.Before(ctx, a, req, opts)
}
func (e *boundExecutor) Execute(ctx context.Context, a *auth.Auth, req executor.Request, opts executor.Options) (executor.Response, error) {
	ctx, err := e.begin(ctx, a, req, opts)
	if err != nil {
		return executor.Response{}, err
	}
	response, err := e.inner.Execute(ctx, a, req, opts)
	if err == nil {
		e.integration.Observe(ctx, response.Payload, false)
	}
	if end := e.integration.After(ctx, err); end != nil {
		return executor.Response{}, end
	}
	return response, err
}
func (e *boundExecutor) CountTokens(ctx context.Context, a *auth.Auth, req executor.Request, opts executor.Options) (executor.Response, error) {
	ctx, err := e.begin(ctx, a, req, opts)
	if err != nil {
		return executor.Response{}, err
	}
	response, err := e.inner.CountTokens(ctx, a, req, opts)
	if end := e.integration.After(ctx, err); end != nil {
		return executor.Response{}, end
	}
	return response, err
}

// Terminal payloads are held only until the durable end boundary. Explicit limits
// prevent malformed post-terminal streams from becoming an unbounded payload store.
const maxTerminalBytes = 8 << 20
const maxTerminalChunks = 256

func (e *boundExecutor) ExecuteStream(ctx context.Context, a *auth.Auth, req executor.Request, opts executor.Options) (*executor.StreamResult, error) {
	ctx, err := e.begin(ctx, a, req, opts)
	if err != nil {
		return nil, err
	}
	providerCtx, cancelProvider := context.WithCancel(ctx)
	result, err := e.inner.ExecuteStream(providerCtx, a, req, opts)
	if err != nil || result == nil {
		cancelProvider()
		if err == nil {
			err = closed(errors.New("upstream returned no stream"))
		}
		if end := e.integration.After(ctx, err); end != nil {
			return nil, end
		}
		return result, err
	}
	chunks := make(chan executor.StreamChunk)
	go func() {
		defer close(chunks)
		defer cancelProvider()
		var terminal error
		canceled, overflow := false, false
		heldBytes := 0
		var completion []executor.StreamChunk
		for chunk := range result.Chunks {
			if chunk.Err != nil {
				if terminal == nil {
					terminal = chunk.Err
				}
				continue
			}
			if canceled || overflow {
				continue
			}
			e.integration.Observe(ctx, chunk.Payload, true)
			if len(completion) > 0 || terminalChunk(chunk.Payload) {
				if len(completion) >= maxTerminalChunks || len(chunk.Payload) > maxTerminalBytes-heldBytes {
					terminal = closed(errors.New("terminal stream buffer limit exceeded"))
					completion = nil
					overflow = true
					cancelProvider()
					continue
				}
				heldBytes += len(chunk.Payload)
				completion = append(completion, chunk)
				continue
			}
			select {
			case chunks <- chunk:
			case <-ctx.Done():
				terminal = ctx.Err()
				canceled = true
				cancelProvider()
			}
		}
		if end := e.integration.After(ctx, terminal); end != nil {
			terminal = end
		}
		if terminal != nil {
			if !canceled {
				select {
				case chunks <- executor.StreamChunk{Err: terminal}:
				case <-ctx.Done():
				}
			}
			return
		}
		for _, chunk := range completion {
			select {
			case chunks <- chunk:
			case <-ctx.Done():
				return
			}
		}
	}()
	return &executor.StreamResult{Headers: result.Headers, Chunks: chunks}, nil
}
func terminalChunk(payload []byte) bool {
	for _, line := range bytes.Split(payload, []byte("\n")) {
		line = bytes.TrimSpace(bytes.TrimPrefix(bytes.TrimSpace(line), []byte("data:")))
		if bytes.Equal(line, []byte("[DONE]")) {
			return true
		}
		switch gjson.GetBytes(line, "type").String() {
		case "response.completed", "response.done", "response.incomplete", "response.failed", "message_stop":
			return true
		}
	}
	// Preserve the existing durability gate for clients that complete on a Chat
	// finish_reason without reading [DONE]. Subsequent choices remain bounded.
	for _, line := range bytes.Split(payload, []byte("\n")) {
		line = bytes.TrimSpace(bytes.TrimPrefix(bytes.TrimSpace(line), []byte("data:")))
		done := false
		gjson.GetBytes(line, "choices").ForEach(func(_, choice gjson.Result) bool {
			reason := choice.Get("finish_reason")
			done = reason.Exists() && reason.Type != gjson.Null
			return !done
		})
		if done {
			return true
		}
	}
	return false
}
func (e *boundExecutor) Refresh(ctx context.Context, a *auth.Auth) (*auth.Auth, error) {
	if e.writable == nil || !e.writable(a) {
		return a, nil
	}
	return e.inner.Refresh(ctx, a)
}
func (e *boundExecutor) ShouldPrepareRequestAuth(a *auth.Auth) bool {
	p, ok := e.inner.(auth.RequestAuthPreparer)
	return ok && e.writable != nil && e.writable(a) && p.ShouldPrepareRequestAuth(a)
}
func (e *boundExecutor) PrepareRequestAuth(ctx context.Context, a *auth.Auth) (*auth.Auth, error) {
	if !e.ShouldPrepareRequestAuth(a) {
		return a, nil
	}
	return e.inner.(auth.RequestAuthPreparer).PrepareRequestAuth(ctx, a)
}
func (e *boundExecutor) ForAPIKey() auth.ProviderExecutor {
	if p, ok := e.inner.(auth.APIKeyConfigExecutor); ok {
		return &boundExecutor{inner: p.ForAPIKey(), integration: e.integration, writable: e.writable}
	}
	return e
}
func (e *boundExecutor) PrepareRequest(req *http.Request, a *auth.Auth) error {
	if p, ok := e.inner.(auth.RequestPreparer); ok {
		return p.PrepareRequest(req, a)
	}
	return nil
}
func (e *boundExecutor) HttpRequest(ctx context.Context, a *auth.Auth, req *http.Request) (*http.Response, error) {
	return nil, closed(errors.New("unbound HTTP execution is disabled"))
}
func (e *boundExecutor) CloseExecutionSession(id string) {
	if p, ok := e.inner.(auth.ExecutionSessionCloser); ok {
		p.CloseExecutionSession(id)
	}
}
func (e *boundExecutor) CloseAuthExecutionSessions(id string) {
	if p, ok := e.inner.(auth.AuthExecutionSessionCloser); ok {
		p.CloseAuthExecutionSessions(id)
	}
}
func (e *boundExecutor) SharesExecutionSessionsWith(other auth.ProviderExecutor) bool {
	if wrapped, ok := other.(*boundExecutor); ok {
		other = wrapped.inner
	}
	if p, ok := e.inner.(auth.ExecutionSessionSharer); ok {
		return p.SharesExecutionSessionsWith(other)
	}
	return e.inner == other
}
func (e *boundExecutor) RequestToFormat(req executor.Request, opts executor.Options) translator.Format {
	if p, ok := e.inner.(interface {
		RequestToFormat(executor.Request, executor.Options) translator.Format
	}); ok {
		return p.RequestToFormat(req, opts)
	}
	return ""
}

type guardedTransport struct {
	next http.RoundTripper
	auth *auth.Auth
}

func (t *guardedTransport) RoundTrip(req *http.Request) (*http.Response, error) {
	if err := validateFinalTools(req); err != nil {
		return nil, err
	}
	if t.auth != nil && t.auth.Attributes["auth_header"] == "bearer" {
		req.Header.Del("x-goog-api-key")
		req.Header.Set("Authorization", "Bearer "+t.auth.Attributes["api_key"])
	}
	for name, value := range t.auth.Attributes {
		if strings.EqualFold(name, "header:x-opencode-session") && value == "$CPA-SESSION-ID" {
			session := req.Header.Get("x-opencode-session")
			if session == "" || len(session) > 256 {
				return nil, closed(errors.New("provider requires a stable conversation session identifier"))
			}
			for _, r := range session {
				if r < 0x21 || r > 0x7e {
					return nil, closed(errors.New("invalid provider conversation session identifier"))
				}
			}
		}
	}
	if pin, ok := freeonly.OpenRouterPin(req.Context()); ok {
		client := &http.Client{Transport: t.next, CheckRedirect: func(*http.Request, []*http.Request) error { return http.ErrUseLastResponse }}
		if err := freeonly.PrepareOpenRouterRequest(req.Context(), client, req, pin); err != nil {
			return nil, err
		}
	}
	response, err := t.next.RoundTrip(req)
	if _, pinned := freeonly.OpenRouterPin(req.Context()); pinned && err == nil && response != nil && response.StatusCode >= 300 && response.StatusCode < 400 {
		if response.Body != nil {
			_ = response.Body.Close()
		}
		return nil, closed(errors.New("free_only inference redirects are disabled"))
	}
	return response, err
}

func (e *boundExecutor) SupportsApplyPatch() bool {
	p, ok := e.inner.(auth.ApplyPatchSupport)
	return ok && p.SupportsApplyPatch()
}
func (e *boundExecutor) SupportsNativeToolSearch(a *auth.Auth, model string) bool {
	p, ok := e.inner.(auth.NativeToolSearchSupport)
	return ok && p.SupportsNativeToolSearch(a, model)
}

type nativeToolPolicyError struct{}

func (*nativeToolPolicyError) Error() string {
	return "native image_generation tools are outside the qualified lain usage contract"
}
func (*nativeToolPolicyError) StatusCode() int       { return http.StatusBadRequest }
func (*nativeToolPolicyError) IsRequestScoped() bool { return true }
func nativeImageToolRequested(body []byte) bool {
	controls, err := toolControlFields(body, "tools", "tool_choice")
	if err != nil {
		return true
	}
	if choice := controls["tool_choice"]; len(choice) > 0 {
		var text string
		if json.Unmarshal(choice, &text) == nil {
			if text == "image_generation" {
				return true
			}
		} else {
			fields, err := toolControlFields(choice, "type")
			if err != nil {
				return true
			}
			if json.Unmarshal(fields["type"], &text) == nil && text == "image_generation" {
				return true
			}
		}
	}
	if tools := controls["tools"]; len(tools) > 0 && string(tools) != "null" {
		var items []json.RawMessage
		if json.Unmarshal(tools, &items) != nil {
			return true
		}
		for _, tool := range items {
			fields, err := toolControlFields(tool, "type")
			if err != nil {
				return true
			}
			var kind string
			if json.Unmarshal(fields["type"], &kind) == nil && kind == "image_generation" {
				return true
			}
		}
	}
	return false
}

// Inspect all occurrences of policy controls; duplicate-key JSON cannot choose
// a different native tool under another parser's first/last-key semantics.
func toolControlFields(body []byte, names ...string) (map[string]json.RawMessage, error) {
	decoder := json.NewDecoder(bytes.NewReader(body))
	token, err := decoder.Token()
	if err != nil || token != json.Delim('{') {
		return nil, errors.New("tool controls require a JSON object")
	}
	fields := map[string]json.RawMessage{}
	wanted := map[string]bool{}
	for _, name := range names {
		wanted[name] = true
	}
	for decoder.More() {
		token, err = decoder.Token()
		if err != nil {
			return nil, err
		}
		key, ok := token.(string)
		if !ok {
			return nil, errors.New("invalid tool control key")
		}
		var value json.RawMessage
		if err = decoder.Decode(&value); err != nil {
			return nil, err
		}
		if wanted[key] {
			if _, exists := fields[key]; exists {
				return nil, errors.New("duplicate tool control")
			}
			fields[key] = value
		}
	}
	token, err = decoder.Token()
	if err != nil || token != json.Delim('}') {
		return nil, errors.New("invalid tool controls")
	}
	if _, err = decoder.Token(); err != io.EOF {
		return nil, errors.New("trailing tool controls")
	}
	return fields, nil
}
func validateFinalTools(req *http.Request) error {
	if req.Body == nil || req.Body == http.NoBody {
		return nil
	}
	// Inspect a replay copy; never consume or retain the executor's original body.
	if req.GetBody == nil {
		return closed(errors.New("final request body cannot be validated"))
	}
	body, err := req.GetBody()
	if err != nil {
		return closed(err)
	}
	defer body.Close()
	data, err := io.ReadAll(io.LimitReader(body, 32*1024*1024+1))
	if err != nil {
		return closed(err)
	}
	if len(data) > 32*1024*1024 {
		return closed(errors.New("final request exceeds qualified body limit"))
	}
	if nativeImageToolRequested(data) {
		return &nativeToolPolicyError{}
	}
	return nil
}
