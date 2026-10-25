package lain

import (
	"bytes"

	"github.com/tidwall/gjson"
)

// Only fixed local descriptions leave this boundary. Provider error payloads may
// contain request content or credentials and are never copied into the journal.
type streamOutcomeError struct{ missingTerminal bool }

func (e *streamOutcomeError) Error() string {
	if e.missingTerminal {
		return "Responses stream closed before a terminal response event"
	}
	return "upstream stream returned a terminal error payload"
}
func (*streamOutcomeError) IsRequestScoped() bool { return true }

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
