package lain

import (
	"bytes"
	"encoding/json"
	"io"
	"net/http"

	auth "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/auth"
	executor "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/executor"
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
	fields, err := toolControlFields(body, "stream_options", "max_completion_tokens", "reasoning_effort", "parallel_tool_calls", "store", "metadata", "n", "audio", "documents", "connectors", "tool_choice")
	if err != nil {
		return &providerInputError{"invalid or duplicate Cohere request controls"}
	}
	for field, value := range fields {
		if field != "reasoning_effort" {
			return cohereFieldError(field)
		}
		var effort string
		if json.Unmarshal(value, &effort) != nil || (effort != "none" && effort != "high") {
			return cohereFieldError(field)
		}
	}
	return nil
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
