package lain

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"strings"

	"github.com/gin-gonic/gin"
	auth "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/auth"
	executor "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/executor"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/usage"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/translator"
)

// These are qualified constraints, not default capabilities. Match the logical
// bound provider, exact upstream model and actual transport; public aliases,
// provider substrings and similarly named models never select a rule.
type compatibilityPolicy struct {
	model                   string
	mimoBridge, glyph, beam bool
}

func compatibilityPolicyForRoute(route Route, responses bool) compatibilityPolicy {
	p := compatibilityPolicy{model: route.Model}
	if route.UpstreamProtocol != "openai" {
		return p
	}
	p.mimoBridge = responses && route.Provider == "xiaomi-mimo" && (route.Model == "mimo-v2.6-pro" || route.Model == "mimo-v2.6-flash")
	p.glyph = route.Provider == "commandcode" && route.Model == "stealth/glyph-cluster:free"
	p.beam = route.Provider == "commandcode" && route.Model == "reflection/beam:free"
	return p
}

type compatibilityError struct{ code, category, message string }

func (e *compatibilityError) Error() string       { return e.code + ": " + e.message }
func (*compatibilityError) StatusCode() int       { return http.StatusBadRequest }
func (*compatibilityError) IsRequestScoped() bool { return true }

func compatibilityErrorCategory(err error) string {
	var e *compatibilityError
	if errors.As(err, &e) && (e.category == "unsupported_tool" || e.category == "tool_limit") {
		return e.category
	}
	return ""
}

func compatibilityForDispatch(ctx context.Context, a *auth.Auth, req executor.Request, opts executor.Options, actualFormat translator.Format) (compatibilityPolicy, error) {
	selected, ok := routeSelection(ctx)
	if !ok || a == nil || !selected.Allowed[a.ID] {
		return compatibilityPolicy{}, closed(errors.New("compatibility dispatch identity missing"))
	}
	route := selected.Route
	p := compatibilityPolicy{model: route.Model}
	switch {
	case route.Provider == "xiaomi-mimo" && (route.Model == "mimo-v2.6-pro" || route.Model == "mimo-v2.6-flash"):
		p.mimoBridge = true
	case route.Provider == "commandcode" && route.Model == "stealth/glyph-cluster:free":
		p.glyph = true
	case route.Provider == "commandcode" && route.Model == "reflection/beam:free":
		p.beam = true
	default:
		return compatibilityPolicy{}, nil
	}
	if req.Model != route.Model {
		return compatibilityPolicy{}, closed(errors.New("compatibility dispatch model differs from bound model"))
	}
	protocol := ""
	switch actualFormat {
	case translator.FormatOpenAI:
		protocol = "openai"
	case translator.FormatOpenAIResponse, translator.FormatCodex:
		protocol = "responses"
	case translator.FormatClaude:
		protocol = "anthropic"
	case translator.FormatGemini:
		protocol = "gemini"
	}
	attributeProtocol := ""
	switch a.Attributes["upstream_protocol"] {
	case "openai-chat":
		attributeProtocol = "openai"
	case "openai-responses":
		attributeProtocol = "responses"
	}
	if protocol != "" && attributeProtocol != "" && protocol != attributeProtocol {
		return compatibilityPolicy{}, closed(errors.New("compatibility executor differs from credential protocol"))
	}
	if protocol == "" {
		protocol = attributeProtocol
	}
	if route.UpstreamProtocol != "" && protocol != route.UpstreamProtocol {
		return compatibilityPolicy{}, closed(errors.New("compatibility transport differs from bound protocol"))
	}
	// The known limitations were qualified on this Chat transport. A native
	// Responses route does not inherit the local Responses-to-Chat limitation.
	if protocol != "openai" {
		return compatibilityPolicy{}, nil
	}
	if p.mimoBridge {
		responses := opts.SourceFormat == translator.FormatOpenAIResponse
		if c, ok := ctx.Value("gin").(*gin.Context); ok && c != nil && c.Request != nil {
			responses = c.Request.URL.Path == "/v1/responses" || c.Request.URL.Path == "/v1/responses/compact"
		}
		if !responses {
			return compatibilityPolicy{}, nil
		}
	}
	return p, nil
}

func originalCompatibilityRequest(ctx context.Context, req executor.Request, opts executor.Options) []byte {
	if c, ok := ctx.Value("gin").(*gin.Context); ok && c != nil {
		if body, ok := c.Get("lain.original_request"); ok {
			if raw, ok := body.([]byte); ok {
				return raw
			}
		}
	}
	if len(opts.OriginalRequest) > 0 {
		return opts.OriginalRequest
	}
	return req.Payload
}

func (p compatibilityPolicy) active() bool { return p.mimoBridge || p.glyph || p.beam }

func (p compatibilityPolicy) validate(body []byte, final bool) error {
	if !p.active() {
		return nil
	}
	fields, err := toolControlFields(body, "model", "tools", "tool_choice")
	if err != nil {
		return &providerInputError{"invalid or duplicate compatibility request controls"}
	}
	if final {
		var model string
		if json.Unmarshal(fields["model"], &model) != nil || model != p.model {
			return closed(errors.New("final model differs from bound compatibility model"))
		}
	}
	var tools []json.RawMessage
	if raw := fields["tools"]; len(raw) > 0 && string(raw) != "null" {
		if json.Unmarshal(raw, &tools) != nil {
			return &providerInputError{"invalid compatibility tools array"}
		}
	}
	// Count the actual transmitted top-level array, never recursively add
	// namespace members or truncate the caller's tools to fit a limit.
	if final && p.beam && len(tools) > 128 {
		return &compatibilityError{"tool_limit_exceeded", "tool_limit", "this bound model accepts at most 128 transmitted top-level tools"}
	}
	if p.beam {
		return nil
	}
	for _, tool := range tools {
		if err := p.validateTool(tool, 0); err != nil {
			return err
		}
	}
	if choice := fields["tool_choice"]; len(choice) > 0 && string(choice) != "null" {
		var kind string
		if json.Unmarshal(choice, &kind) == nil {
			return p.validateKind(kind)
		}
		return p.validateTool(choice, 0)
	}
	return nil
}

func (p compatibilityPolicy) validateTool(tool []byte, depth int) error {
	if depth > 64 {
		return &providerInputError{"tool nesting exceeds the qualified request limit"}
	}
	fields, err := toolControlFields(tool, "type", "tools")
	if err != nil {
		return &providerInputError{"invalid or duplicate compatibility tool controls"}
	}
	var kind string
	if raw := fields["type"]; len(raw) > 0 && json.Unmarshal(raw, &kind) != nil {
		return &providerInputError{"invalid compatibility tool type"}
	}
	if err := p.validateKind(kind); err != nil {
		return err
	}
	// Descend only through tool declarations, never function schemas or text.
	if kind == "namespace" {
		var children []json.RawMessage
		if json.Unmarshal(fields["tools"], &children) != nil {
			return &providerInputError{"invalid namespace tools array"}
		}
		for _, child := range children {
			if err := p.validateTool(child, depth+1); err != nil {
				return err
			}
		}
	}
	return nil
}

func (p compatibilityPolicy) validateKind(kind string) error {
	if p.mimoBridge && nativeSearchToolKind(kind) {
		return &compatibilityError{"native_search_unsupported", "unsupported_tool", "the qualified MiMo Responses-to-Chat translator cannot carry native web search; use an explicit function tool or another qualified route"}
	}
	if p.glyph && kind == "namespace" {
		return &compatibilityError{"namespace_tools_unsupported", "unsupported_tool", "this bound Glyph model does not accept namespace tools"}
	}
	if p.glyph && kind == "custom" {
		return &compatibilityError{"custom_tools_unsupported", "unsupported_tool", "this bound Glyph model does not accept custom tools"}
	}
	return nil
}

func nativeSearchToolKind(kind string) bool {
	return kind == "web_search" || kind == "web_search_preview" || strings.HasPrefix(kind, "web_search_preview_")
}

func (p compatibilityPolicy) validateFinal(req *http.Request) error {
	if !p.active() {
		return nil
	}
	if req.Method != http.MethodPost || !strings.HasSuffix(req.URL.Path, "/chat/completions") {
		return closed(errors.New("final transport differs from bound compatibility protocol"))
	}
	if req.GetBody == nil {
		return closed(errors.New("final compatibility request cannot be validated"))
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
		return closed(errors.New("final compatibility request exceeds qualified body limit"))
	}
	return p.validate(data, true)
}

func (i *Integration) recordCompatibilityFailure(ctx context.Context, err error) {
	category := compatibilityErrorCategory(err)
	if category == "" {
		return
	}
	i.mu.Lock()
	defer i.mu.Unlock()
	if a := i.active[usage.ExecutionRequestIDFromContext(ctx)]; a != nil {
		a.event.ErrorCategory = ptr(category)
	}
}
