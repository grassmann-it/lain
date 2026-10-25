package lain

import (
	"bytes"
	"context"
	"encoding/json"
	"io"
	"net/http"

	auth "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/auth"
	executor "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/executor"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/usage"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/translator"
)

type providerInputError struct{ message string }

func (e *providerInputError) Error() string       { return e.message }
func (*providerInputError) StatusCode() int       { return http.StatusBadRequest }
func (*providerInputError) IsRequestScoped() bool { return true }

func cohereCompatibility(a *auth.Auth) bool {
	return a != nil && a.Provider == "openai-compatible-cohere-openai-chat" && a.Attributes["compat_name"] == "cohere-openai-chat" && a.Attributes["upstream_protocol"] == "openai-chat"
}

var cohereFields = map[string]bool{
	"model": true, "messages": true, "stream": true, "reasoning_effort": true,
	"response_format": true, "tools": true, "temperature": true,
	"max_tokens": true, "stop": true, "seed": true, "top_p": true,
	"frequency_penalty": true, "presence_penalty": true,
}

func cohereFieldError(field string) error {
	// The field list here is static; never echo arbitrary client field names or values.
	switch field {
	case "stream_options":
		return &providerInputError{"Cohere compatibility does not support explicit stream_options; streaming usage may be unavailable"}
	case "max_completion_tokens":
		return &providerInputError{"Cohere compatibility supports max_tokens; max_completion_tokens is not qualified"}
	case "reasoning_effort":
		return &providerInputError{"Cohere compatibility reasoning_effort must be none or high"}
	case "store":
		return &providerInputError{"Cohere supports only Responses store:false through the stateless Chat bridge"}
	case "previous_response_id", "conversation":
		return &providerInputError{"Cohere Chat requires complete input history; response IDs and stored conversations are not supported"}
	default:
		return &providerInputError{"request contains a field outside the supported Cohere compatibility subset"}
	}
}

func validateCohereInput(a *auth.Auth, req executor.Request, opts executor.Options) error {
	if !cohereCompatibility(a) {
		return nil
	}
	// Validate the caller's payload before upstream translators can normalize or
	// remove an unsupported explicit option. A final check follows translation.
	body := opts.OriginalRequest
	if len(body) == 0 {
		body = req.Payload
	}
	if opts.SourceFormat == translator.FormatOpenAI {
		var original map[string]json.RawMessage
		if json.Unmarshal(body, &original) != nil || original == nil {
			return &providerInputError{"invalid Cohere Chat request"}
		}
		names := make([]string, 0, len(original))
		for name := range original {
			if !cohereFields[name] {
				return cohereFieldError(name)
			}
			names = append(names, name)
		}
		if _, err := toolControlFields(body, names...); err != nil {
			return &providerInputError{"invalid or duplicate Cohere request fields"}
		}
	}
	responses := opts.SourceFormat == translator.FormatOpenAIResponse
	fields, err := toolControlFields(body, "stream_options", "max_completion_tokens", "reasoning_effort", "parallel_tool_calls", "store", "previous_response_id", "conversation", "metadata", "n", "audio", "documents", "connectors", "tool_choice")
	if err != nil {
		return &providerInputError{"invalid or duplicate Cohere request controls"}
	}
	for field, value := range fields {
		// The Responses storage control concerns retrieval of a generated response,
		// not provider logging/retention. This bridge has no stored response resource:
		// false maps to an ordinary stateless Chat request, never to a sent store field.
		if field == "store" && responses && bytes.Equal(bytes.TrimSpace(value), []byte("false")) {
			continue
		}
		if field != "reasoning_effort" {
			return cohereFieldError(field)
		}
		var effort string
		if json.Unmarshal(value, &effort) != nil || (effort != "none" && effort != "high") {
			return cohereFieldError(field)
		}
	}
	if err := validateCohereTools(body, responses); err != nil {
		return err
	}
	_, err = cohereRequestedEffort(req, opts)
	return err
}

// Only declaration locations are inspected; function schemas and tool results
// are data. Match the pinned Responses-to-Chat translator, including additional_tools.
func validateCohereTools(body []byte, responses bool) error {
	fields, err := toolControlFields(body, "tools", "input")
	if err != nil {
		return &providerInputError{"invalid or duplicate Cohere tool controls"}
	}
	if err := validateCohereToolArray(fields["tools"], responses, 0); err != nil {
		return err
	}
	if !responses || len(fields["input"]) == 0 {
		return nil
	}
	var input []json.RawMessage
	if json.Unmarshal(fields["input"], &input) != nil {
		return nil // A string input is handled by the translator.
	}
	for _, item := range input {
		controls, err := toolControlFields(item, "type", "tools")
		if err != nil {
			return &providerInputError{"invalid or duplicate Cohere input controls"}
		}
		var kind string
		if raw := controls["type"]; len(raw) > 0 && json.Unmarshal(raw, &kind) != nil {
			return &providerInputError{"invalid Cohere input type"}
		}
		if kind == "item_reference" {
			return cohereFieldError("previous_response_id")
		}
		if kind == "additional_tools" {
			if err := validateCohereToolArray(controls["tools"], true, 0); err != nil {
				return err
			}
		}
	}
	return nil
}

func validateCohereToolArray(raw []byte, responses bool, depth int) error {
	if len(raw) == 0 || bytes.Equal(bytes.TrimSpace(raw), []byte("null")) {
		return nil
	}
	if depth > 64 {
		return &providerInputError{"tool nesting exceeds the qualified request limit"}
	}
	var tools []json.RawMessage
	if json.Unmarshal(raw, &tools) != nil {
		return &providerInputError{"invalid Cohere tools array"}
	}
	for _, tool := range tools {
		fields, err := toolControlFields(tool, "type", "tools", "environment", "format")
		if err != nil {
			return &providerInputError{"invalid or duplicate Cohere tool declaration"}
		}
		var kind string
		if json.Unmarshal(fields["type"], &kind) != nil {
			return &providerInputError{"invalid Cohere tool type"}
		}
		if nativeSearchToolKind(kind) {
			return &compatibilityError{"native_search_unsupported", "unsupported_tool", "the qualified Cohere Responses-to-Chat bridge cannot carry native web search; use an explicit function tool or another qualified route"}
		}
		switch {
		case kind == "function":
		case responses && kind == "custom":
			if raw := fields["format"]; len(raw) > 0 {
				format, err := toolControlFields(raw, "type")
				var mode string
				if err != nil || json.Unmarshal(format["type"], &mode) != nil || mode != "text" {
					return &compatibilityError{"chat_bridge_tool_unsupported", "unsupported_tool", "the qualified Cohere Chat bridge cannot carry a constrained custom tool format"}
				}
			}
		case responses && kind == "namespace" && depth == 0:
			if err := validateCohereToolArray(fields["tools"], true, depth+1); err != nil {
				return err
			}
		case responses && kind == "shell":
			environment, err := toolControlFields(fields["environment"], "type")
			var mode string
			if err != nil || json.Unmarshal(environment["type"], &mode) != nil || mode != "local" || depth != 0 {
				return &compatibilityError{"chat_bridge_tool_unsupported", "unsupported_tool", "the qualified Cohere Chat bridge cannot carry this native tool"}
			}
		default:
			return &compatibilityError{"chat_bridge_tool_unsupported", "unsupported_tool", "the qualified Cohere Chat bridge cannot carry this native tool"}
		}
	}
	return nil
}

// Preserve only validated explicit caller intent. The generic Responses-to-Chat
// thinking path can normalize none to low; low is not a supported Cohere level.
func cohereRequestedEffort(req executor.Request, opts executor.Options) (string, error) {
	body := opts.OriginalRequest
	if len(body) == 0 {
		body = req.Payload
	}
	fields, err := toolControlFields(body, "reasoning_effort", "reasoning")
	if err != nil {
		return "", &providerInputError{"invalid or duplicate Cohere reasoning controls"}
	}
	effort := ""
	if value, ok := fields["reasoning_effort"]; ok {
		if json.Unmarshal(value, &effort) != nil || (effort != "none" && effort != "high") {
			return "", cohereFieldError("reasoning_effort")
		}
	}
	if value, ok := fields["reasoning"]; ok {
		nested, err := toolControlFields(value, "effort")
		if err != nil {
			return "", &providerInputError{"invalid or duplicate Cohere reasoning controls"}
		}
		if value, present := nested["effort"]; present {
			var requested string
			if json.Unmarshal(value, &requested) != nil || (requested != "none" && requested != "high") || (effort != "" && effort != requested) {
				return "", cohereFieldError("reasoning_effort")
			}
			effort = requested
		}
	}
	return effort, nil
}

func (t *guardedTransport) normalizeCohereRequest(req *http.Request) error {
	if !cohereCompatibility(t.auth) {
		return nil
	}
	if !t.cohereValidated || req.GetBody == nil {
		return &providerInputError{"Cohere request controls could not be validated"}
	}
	body, err := req.GetBody()
	if err != nil {
		return err
	}
	data, err := io.ReadAll(io.LimitReader(body, 32*1024*1024+1))
	_ = body.Close()
	if err != nil {
		return err
	}
	if len(data) > 32*1024*1024 {
		return &providerInputError{"Cohere request exceeds qualified body limit"}
	}
	var fields map[string]json.RawMessage
	if json.Unmarshal(data, &fields) != nil || fields == nil {
		return &providerInputError{"invalid Cohere request"}
	}
	if value, exists := fields["stream_options"]; exists {
		// Only the Core's known automatic usage request may be removed. Any
		// explicit stream_options was rejected before translation above.
		var options map[string]json.RawMessage
		var includeUsage bool
		if json.Unmarshal(value, &options) != nil || len(options) != 1 || json.Unmarshal(options["include_usage"], &includeUsage) != nil || !includeUsage {
			return cohereFieldError("stream_options")
		}
		delete(fields, "stream_options")
	}
	if t.cohereReasoningEffort != "" {
		if t.cohereReasoningEffort != "none" && t.cohereReasoningEffort != "high" {
			return cohereFieldError("reasoning_effort")
		}
		fields["reasoning_effort"], _ = json.Marshal(t.cohereReasoningEffort)
	}
	for field, value := range fields {
		if !cohereFields[field] {
			return cohereFieldError(field)
		}
		if field == "reasoning_effort" {
			var effort string
			if json.Unmarshal(value, &effort) != nil || (effort != "none" && effort != "high") {
				return cohereFieldError(field)
			}
		}
	}
	if err := validateCohereTools(data, false); err != nil {
		return err
	}
	data, err = json.Marshal(fields)
	if err != nil {
		return err
	}
	_ = req.Body.Close()
	req.Body = io.NopCloser(bytes.NewReader(data))
	req.ContentLength = int64(len(data))
	req.GetBody = func() (io.ReadCloser, error) { return io.NopCloser(bytes.NewReader(data)), nil }
	return nil
}

// Final wire evidence wins over the generic reporter's pre-normalization effort.
func (i *Integration) observeCohereFinalEffort(ctx context.Context, effort string) {
	if effort != "none" && effort != "high" {
		return
	}
	i.mu.Lock()
	defer i.mu.Unlock()
	a := i.active[usage.ExecutionRequestIDFromContext(ctx)]
	if a == nil {
		return
	}
	if a.event.Dimensions == nil {
		a.event.Dimensions = &Dimensions{}
	}
	a.event.Dimensions.set("effectiveReasoningEffort", &a.event.Dimensions.EffectiveReasoningEffort, effort, "core", "final_request.reasoning_effort")
}
