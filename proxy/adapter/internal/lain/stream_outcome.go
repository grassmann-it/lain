package lain

import (
	"bytes"
	"encoding/json"
	"net/http"

	"github.com/tidwall/gjson"
)

// Only fixed local descriptions leave this boundary. Provider error payloads may
// contain request content or credentials and are never copied into the journal.
type streamOutcomeError struct {
	missingTerminal bool
	classification  *upstreamErrorClassification
}

func (e *streamOutcomeError) Error() string {
	if e.missingTerminal {
		return "Responses stream closed before a terminal response event"
	}
	if e.classification != nil {
		return string(canonicalUpstreamErrorJSON(e.classification))
	}
	return "upstream stream returned a terminal error payload"
}

func (*streamOutcomeError) IsRequestScoped() bool { return true }

// Only a classified error exposes status/body interfaces. Generic streamOutcomeError
// must not acquire StatusCode=0, which would alter unknown-failure serialization.
type classifiedStreamOutcomeError struct{ *streamOutcomeError }

func (*classifiedStreamOutcomeError) StatusCode() int      { return http.StatusBadRequest }
func (*classifiedStreamOutcomeError) DirectResponse() bool { return true }
func (e *classifiedStreamOutcomeError) ResponseBody() []byte {
	return canonicalUpstreamErrorJSON(e.classification)
}

// The parameter allowlist is deliberately exact. Providers sometimes put user
// text, paths, credentials or another error object in this field; merely sizing
// it would still forward untrusted diagnostics.
var safeUpstreamErrorParams = map[string]bool{
	"input": true, "messages": true, "prompt": true, "tools": true,
	"tool_choice": true, "max_tokens": true, "max_output_tokens": true,
	"include": true, "reasoning.encrypted_content": true,
	"include.reasoning.encrypted_content": true, "previous_response_id": true,
}

type upstreamErrorDefinition struct{ errorType, message string }

var upstreamErrorDefinitions = map[string]upstreamErrorDefinition{
	"context_length_exceeded": {
		errorType: "invalid_request_error",
		message:   "The upstream model's context limit was exceeded. Shorten the conversation or reduce the requested output, then retry.",
	},
	"invalid_encrypted_content": {
		errorType: "invalid_request_error",
		message:   "The upstream provider rejected encrypted content in this request. Start a new request without the incompatible encrypted content, then retry.",
	},
}

type upstreamErrorClassification struct {
	code, param string
	hasParam    bool
	definition  upstreamErrorDefinition
}

type upstreamErrorDetail struct {
	Type    string `json:"type"`
	Code    string `json:"code"`
	Message string `json:"message"`
	Param   string `json:"param,omitempty"`
}

func canonicalUpstreamErrorJSON(classification *upstreamErrorClassification) []byte {
	if classification == nil {
		return nil
	}
	envelope := struct {
		Error upstreamErrorDetail `json:"error"`
	}{Error: upstreamErrorDetail{
		Type: classification.definition.errorType, Code: classification.code,
		Message: classification.definition.message,
	}}
	if classification.hasParam {
		envelope.Error.Param = classification.param
	}
	data, err := json.Marshal(envelope)
	if err != nil {
		// All fields come from the static allowlist, so this is unreachable. The
		// fallback still avoids ever substituting provider text.
		return []byte(`{"error":{"type":"invalid_request_error","code":"upstream_rejected","message":"The upstream provider rejected this request."}}`)
	}
	return data
}

func classifyUpstreamErrorDetail(raw []byte) *upstreamErrorClassification {
	fields, err := toolControlFields(raw, "code", "param")
	if err != nil {
		return nil
	}
	var code string
	if json.Unmarshal(fields["code"], &code) != nil {
		return nil
	}
	definition, known := upstreamErrorDefinitions[code]
	if !known {
		return nil
	}
	var param string
	hasParam := json.Unmarshal(fields["param"], &param) == nil && safeUpstreamErrorParams[param]
	return &upstreamErrorClassification{code: code, param: param, hasParam: hasParam, definition: definition}
}

func classifyUpstreamErrorObject(raw []byte, eventKind string) *upstreamErrorClassification {
	if len(raw) > maxRequestErrorBytes {
		return nil
	}
	fields, err := toolControlFields(raw, "type", "code", "param", "error", "response")
	if err != nil {
		return nil
	}
	terminalKind := map[string]bool{"error": true, "response.error": true, "response.failed": true}[eventKind]
	var rootType string
	_ = json.Unmarshal(fields["type"], &rootType)
	terminalKind = terminalKind || rootType == "error" || rootType == "response.error" || rootType == "response.failed"
	if detail, exists := fields["error"]; exists && string(bytes.TrimSpace(detail)) != "null" {
		return classifyUpstreamErrorDetail(detail)
	}
	if terminalKind {
		if direct := classifyUpstreamErrorDetail(raw); direct != nil {
			return direct
		}
	}
	response, exists := fields["response"]
	if !exists || string(bytes.TrimSpace(response)) == "null" {
		return nil
	}
	responseFields, err := toolControlFields(response, "error")
	if err != nil {
		return nil
	}
	if detail, exists := responseFields["error"]; exists && string(bytes.TrimSpace(detail)) != "null" {
		return classifyUpstreamErrorDetail(detail)
	}
	if terminalKind {
		return classifyUpstreamErrorDetail(raw)
	}
	return nil
}

// classifyUpstreamStreamError is intentionally separate from outcome detection.
// The executor first establishes that a protocol terminal failed, then may use
// this classifier to replace only an unambiguous allowlisted cause. Content in
// delta/output fields, prose and malformed or oversized diagnostics stay unknown.
func classifyUpstreamStreamError(payload []byte) *classifiedStreamOutcomeError {
	if len(payload) > maxRequestErrorBytes {
		return nil
	}
	eventKind := ""
	for _, line := range bytes.Split(payload, []byte("\n")) {
		line = bytes.TrimSpace(line)
		if len(line) == 0 {
			eventKind = ""
			continue
		}
		if bytes.HasPrefix(line, []byte("event:")) {
			eventKind = string(bytes.TrimSpace(bytes.TrimPrefix(line, []byte("event:"))))
			continue
		}
		if !bytes.HasPrefix(line, []byte("data:")) {
			continue
		}
		data := bytes.TrimSpace(bytes.TrimPrefix(line, []byte("data:")))
		if !gjson.ValidBytes(data) {
			continue
		}
		if root := gjson.ParseBytes(data); !root.IsObject() {
			continue
		}
		if classification := classifyUpstreamErrorObject(data, eventKind); classification != nil {
			return &classifiedStreamOutcomeError{&streamOutcomeError{classification: classification}}
		}
	}
	return nil
}

// Observe complete executor frames, using the same terminal/error vocabulary as
// the pinned Responses HTTP framer. A successful HTTP status is not a successful
// stream. Root-level errors are controls; error text inside a delta is content.
func responseStreamOutcome(payload []byte) (terminal, failed bool) {
	visitType := func(kind string) {
		switch kind {
		case "response.completed", "response.incomplete", "response.done":
			terminal = true
		case "response.failed", "response.error", "error":
			terminal, failed = true, true
		}
	}
	for _, line := range bytes.Split(payload, []byte("\n")) {
		line = bytes.TrimSpace(line)
		if bytes.HasPrefix(line, []byte("event:")) {
			visitType(string(bytes.TrimSpace(bytes.TrimPrefix(line, []byte("event:")))))
			continue
		}
		line = bytes.TrimSpace(bytes.TrimPrefix(line, []byte("data:")))
		if !gjson.ValidBytes(line) {
			continue
		}
		root := gjson.ParseBytes(line)
		if !root.IsObject() {
			continue
		}
		visitType(root.Get("type").String())
		for _, path := range []string{"error", "response.error"} {
			value := root.Get(path)
			if value.Exists() && value.Type != gjson.Null {
				terminal, failed = true, true
			}
		}
		if root.Get("response.status").String() == "failed" {
			terminal, failed = true, true
		}
	}
	return terminal, failed
}
