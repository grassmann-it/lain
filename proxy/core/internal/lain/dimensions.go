package lain

import (
	"net/http"
	"regexp"
	"strings"
	"time"

	"github.com/tidwall/gjson"
)

// Dimensions contain source-backed facts, never affinity or content-derived IDs.
// New optional fields must stay omitted when unknown: old journal replay digests
// depend on preserving the original serialized shape.
type Dimensions struct {
	ModelFamily              string              `json:"modelFamily,omitempty"`
	Manufacturer             string              `json:"manufacturer,omitempty"`
	InferenceProvider        string              `json:"inferenceProvider,omitempty"`
	Gateway                  string              `json:"gateway,omitempty"`
	BillingAccountID         string              `json:"billingAccountId,omitempty"`
	ReasoningEffort          string              `json:"reasoningEffort,omitempty"`
	Mode                     string              `json:"mode,omitempty"`
	ConversationID           string              `json:"conversationId,omitempty"`
	TurnID                   string              `json:"turnId,omitempty"`
	WorkerID                 string              `json:"workerId,omitempty"`
	ParentWorkerID           string              `json:"parentWorkerId,omitempty"`
	ParentRequestID          string              `json:"parentRequestId,omitempty"`
	ToolCallID               string              `json:"toolCallId,omitempty"`
	Harness                  string              `json:"harness,omitempty"`
	ProfileID                string              `json:"profileId,omitempty"`
	ResponseID               string              `json:"responseId,omitempty"`
	PreviousResponseID       string              `json:"previousResponseId,omitempty"`
	EffectiveReasoningEffort string              `json:"effectiveReasoningEffort,omitempty"`
	Evidence                 []DimensionEvidence `json:"evidence,omitempty"`
}

type DimensionEvidence struct {
	Field  string `json:"field"`
	Source string `json:"source"`
	Key    string `json:"key,omitempty"`
}

type Measurements struct {
	FirstOutputLatencyMs *float64   `json:"firstOutputLatencyMs,omitempty"`
	TotalDurationMs      *float64   `json:"totalDurationMs,omitempty"`
	FirstOutputAt        *time.Time `json:"firstOutputAt,omitempty"`
	FinishedAt           *time.Time `json:"finishedAt,omitempty"`
	Source               string     `json:"source"`
}

var dimensionValue = regexp.MustCompile(`^[A-Za-z0-9][A-Za-z0-9_.:/@+() -]{0,191}$`)
var credentialLike = regexp.MustCompile(`(?i)(bearer[ :]|sk-|nvapi-|AIza|gh[pousr]_|xox[baprs]-|eyJ[A-Za-z0-9_-]+\.|api[ _-]?key|access[ _-]?token|refresh[ _-]?token|client[ _-]?secret|secret[ _-]?key|private[ _-]?key|password|authorization)`)
var legacyClaudeSession = regexp.MustCompile(`^user_[A-Za-z0-9-]+_account_[A-Za-z0-9-]*_session_([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})$`)

func safeDimension(value string) string {
	if value != strings.TrimSpace(value) || !dimensionValue.MatchString(value) || credentialLike.MatchString(value) {
		return ""
	}
	return value
}

func (d *Dimensions) set(field string, target *string, value, source, key string) bool {
	value = safeDimension(value)
	if value == "" || *target != "" {
		return false
	}
	*target = value
	d.Evidence = append(d.Evidence, DimensionEvidence{Field: field, Source: source, Key: key})
	return true
}

func copyDimensions(d *Dimensions) *Dimensions {
	if d == nil {
		return nil
	}
	out := *d
	out.Evidence = append([]DimensionEvidence(nil), d.Evidence...)
	return &out
}

func requestDimensions(route Route, account *Dimensions, headers http.Header, payload []byte) *Dimensions {
	d := &Dimensions{}
	// Binding fields are explicit catalog/account facts. Never guess from a model
	// alias or core transport adapter name, which may actually be a gateway.
	if r := route.Dimensions; r != nil {
		for _, f := range []struct {
			name  string
			dst   *string
			value string
		}{
			{"modelFamily", &d.ModelFamily, r.ModelFamily}, {"manufacturer", &d.Manufacturer, r.Manufacturer},
			{"inferenceProvider", &d.InferenceProvider, r.InferenceProvider}, {"gateway", &d.Gateway, r.Gateway},
			{"mode", &d.Mode, r.Mode}, {"profileId", &d.ProfileID, r.ProfileID},
		} {
			d.set(f.name, f.dst, f.value, "route_binding", "dimensions."+f.name)
		}
	}
	if account != nil {
		d.set("billingAccountId", &d.BillingAccountID, account.BillingAccountID, "route_binding", "accountDimensions.billingAccountId")
	}
	for _, f := range []struct {
		name string
		dst  *string
		keys []string
	}{
		{"conversationId", &d.ConversationID, []string{"X-Claude-Code-Session-Id", "Thread-Id", "Thread_id"}},
		{"workerId", &d.WorkerID, []string{"X-Claude-Code-Agent-Id"}},
		{"parentWorkerId", &d.ParentWorkerID, []string{"X-Claude-Code-Parent-Agent-Id"}},
	} {
		for _, key := range f.keys {
			if d.set(f.name, f.dst, headers.Get(key), "harness_header", key) {
				break
			}
		}
	}
	turnHeader := headers.Get("X-Codex-Turn-Metadata")
	if len(turnHeader) <= 8192 && gjson.Valid(turnHeader) {
		root := gjson.Parse(turnHeader)
		for _, f := range []struct {
			name string
			dst  *string
			keys []string
		}{
			{"conversationId", &d.ConversationID, []string{"thread_id"}},
			{"turnId", &d.TurnID, []string{"turn_id"}},
			{"workerId", &d.WorkerID, []string{"agent_id"}},
			{"parentWorkerId", &d.ParentWorkerID, []string{"parent_agent_id"}},
		} {
			for _, key := range f.keys {
				v := root.Get(key)
				if v.Type == gjson.String {
					d.set(f.name, f.dst, v.String(), "harness_header", "X-Codex-Turn-Metadata."+key)
				}
			}
		}
		// A thread ID by itself is only a conversation. The explicit Codex
		// thread_spawn marker plus child/parent IDs proves a worker relationship.
		if root.Get("subagent_kind").String() == "thread_spawn" && d.WorkerID == "" && d.ParentWorkerID == "" {
			child, parent := root.Get("thread_id"), root.Get("parent_thread_id")
			if child.Type == gjson.String && parent.Type == gjson.String && safeDimension(child.String()) != "" && safeDimension(parent.String()) != "" {
				d.set("workerId", &d.WorkerID, child.String(), "harness_header", "X-Codex-Turn-Metadata.thread_id")
				d.set("parentWorkerId", &d.ParentWorkerID, parent.String(), "harness_header", "X-Codex-Turn-Metadata.parent_thread_id")
			}
		}
	}
	root := gjson.ParseBytes(payload)
	// Current Claude metadata can encode explicit identities in a JSON string.
	// Extract only these fields, never the surrounding user/account/device IDs.
	userID := root.Get("metadata.user_id")
	if userID.Type == gjson.String && len(userID.String()) <= 8192 {
		if raw := userID.String(); gjson.Valid(raw) && gjson.Parse(raw).IsObject() {
			meta := gjson.Parse(raw)
			for _, f := range []struct {
				name string
				dst  *string
				keys []string
			}{
				{"conversationId", &d.ConversationID, []string{"session_id"}},
				{"workerId", &d.WorkerID, []string{"agent_id", "subagent_id"}},
				{"parentWorkerId", &d.ParentWorkerID, []string{"parent_agent_id"}},
			} {
				for _, key := range f.keys {
					v := meta.Get(key)
					if v.Type == gjson.String {
						d.set(f.name, f.dst, v.String(), "request_body", "metadata.user_id."+key)
					}
				}
			}
		} else if match := legacyClaudeSession.FindStringSubmatch(raw); len(match) == 2 {
			d.set("conversationId", &d.ConversationID, match[1], "request_body", "metadata.user_id._session_")
		}
	}
	for _, f := range []struct {
		name string
		dst  *string
		keys []string
	}{
		{"conversationId", &d.ConversationID, []string{"thread_id", "metadata.thread_id", "metadata.conversation_id"}},
		{"turnId", &d.TurnID, []string{"turn_id", "metadata.turn_id"}},
		{"workerId", &d.WorkerID, []string{"metadata.agent_id", "metadata.subagent_id"}},
		{"parentWorkerId", &d.ParentWorkerID, []string{"metadata.parent_agent_id"}},
		{"parentRequestId", &d.ParentRequestID, []string{"metadata.parent_request_id"}},
		{"previousResponseId", &d.PreviousResponseID, []string{"previous_response_id"}},
		{"reasoningEffort", &d.ReasoningEffort, []string{"reasoning.effort", "reasoning_effort"}},
	} {
		for _, key := range f.keys {
			v := root.Get(key)
			if v.Type == gjson.String {
				d.set(f.name, f.dst, v.String(), "request_body", key)
			}
		}
	}
	// Prefer an explicit thread (including body/turn metadata) over a broader
	// harness session, which can own more than one thread.
	for _, key := range []string{"Session-Id", "Session_id"} {
		d.set("conversationId", &d.ConversationID, headers.Get(key), "harness_header", key)
	}
	if len(turnHeader) <= 8192 && gjson.Valid(turnHeader) {
		v := gjson.Get(turnHeader, "session_id")
		if v.Type == gjson.String {
			d.set("conversationId", &d.ConversationID, v.String(), "harness_header", "X-Codex-Turn-Metadata.session_id")
		}
	}
	// A single explicit tool result can associate this request with its call. A
	// request with multiple results has no honest single toolCallId dimension.
	toolID, toolKey, count := "", "", 0
	acceptTool := func(v gjson.Result, key string) {
		if v.Type == gjson.String && safeDimension(v.String()) != "" {
			toolID, toolKey, count = v.String(), key, count+1
		}
	}
	root.Get("input").ForEach(func(_, item gjson.Result) bool {
		if item.Get("type").String() == "function_call_output" {
			acceptTool(item.Get("call_id"), "input[].call_id")
		}
		return true
	})
	root.Get("messages").ForEach(func(_, item gjson.Result) bool {
		if item.Get("role").String() == "tool" {
			acceptTool(item.Get("tool_call_id"), "messages[].tool_call_id")
		}
		item.Get("content").ForEach(func(_, block gjson.Result) bool {
			if block.Get("type").String() == "tool_result" {
				acceptTool(block.Get("tool_use_id"), "messages[].content[].tool_use_id")
			}
			return true
		})
		return true
	})
	if count == 1 {
		d.set("toolCallId", &d.ToolCallID, toolID, "request_body", toolKey)
	}
	if len(d.Evidence) == 0 {
		return nil
	}
	return d
}
