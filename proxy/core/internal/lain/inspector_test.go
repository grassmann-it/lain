package lain

import (
	"context"
	"encoding/json"
	"net/http"
	"strings"
	"testing"
	"time"

	executor "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/executor"
	"github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/usage"
	"github.com/tidwall/gjson"
)

func TestInspectorReplayBoundsExpiryAndRestart(t *testing.T) {
	now := time.Now()
	s := newInspector()
	s.maxEvents = 2
	s.now = func() time.Time { return now }
	for n := 0; n < 3; n++ {
		s.append(InspectorEvent{ObservedAt: now, Kind: "metadata"})
	}
	one, err := s.Read(0, 1)
	if err != nil || one.EarliestCursor != 2 || one.NextCursor != 2 || one.HighWatermark != 3 || one.DroppedCount != 1 {
		t.Fatal(one, err)
	}
	replay, _ := s.Read(0, 1)
	if replay.Events[0].Sequence != one.Events[0].Sequence {
		t.Fatal("read was destructive")
	}
	two, _ := s.Read(2, 1)
	if two.NextCursor != 3 || len(two.Events) != 1 {
		t.Fatal(two)
	}
	now = now.Add(16 * time.Minute)
	expired, _ := s.Read(2, 1)
	if len(expired.Events) != 0 || expired.NextCursor != 2 || expired.EarliestCursor != 4 || expired.DroppedCount != 3 {
		t.Fatal(expired)
	}
	restart, _ := newInspector().Read(200, 1)
	if restart.InstanceID == one.InstanceID || restart.NextCursor != 0 || len(restart.Events) != 0 {
		t.Fatal(restart)
	}
	s = newInspector()
	s.maxBytes = 128
	s.append(InspectorEvent{ObservedAt: time.Now(), Text: strings.Repeat("x", 200)})
	if s.bytes > 128 || len(s.events) != 0 {
		t.Fatal("byte cap not enforced")
	}
}

func TestCaptureRedactsAcrossChunksAndBoundsUnterminatedLines(t *testing.T) {
	var b captureBuffer
	if lines := b.consume("safe text\nAuthor", false); len(lines) != 1 || lines[0] != "safe text\n" {
		t.Fatal(lines)
	}
	if lines := b.consume("ization: Be", false); len(lines) != 0 {
		t.Fatal(lines)
	}
	lines := b.consume("arer sk-secret-fixture-abcdefghijk\n", false)
	if len(lines) != 1 || strings.Contains(lines[0], "fixture") || !strings.Contains(lines[0], "redacted") {
		t.Fatal(lines)
	}
	b = captureBuffer{}
	for _, part := range []string{"sk-", "0123456789", "abcdefghijk", "lmno\n"} {
		for _, line := range b.consume(part, false) {
			if strings.Contains(line, "sk-") || strings.Contains(line, "012345") {
				t.Fatal("split secret escaped", line)
			}
		}
	}
	b = captureBuffer{}
	lines = b.consume(strings.Repeat("x", inspectorTextLimit+1), false)
	if len(b.pending) != 0 || !b.dropping || len(lines) != 1 {
		t.Fatal("unbounded pending content")
	}
	if got := b.consume("tail\nOK", true); len(got) != 1 || got[0] != "OK" {
		t.Fatal(got)
	}
	b = captureBuffer{}
	if got := b.consume("a\x1b[31mb\u202ec", true); len(got) != 1 || strings.ContainsAny(got[0], "\x1b\u202e") {
		t.Fatal(got)
	}
}

func TestDimensionsRequireExplicitEvidenceAndRejectInferredIdentities(t *testing.T) {
	headers := http.Header{}
	headers.Set("Session-Id", "session")
	headers.Set("Thread-Id", "thread")
	headers.Set("X-Codex-Turn-Metadata", `{"turn_id":"turn","agent_id":"worker","parent_agent_id":"parent"}`)
	headers.Set("Authorization", "Bearer sk-secret")
	d := requestDimensions(Route{Dimensions: &Dimensions{ModelFamily: "family", Manufacturer: "maker"}}, nil, headers, []byte(`{"prompt_cache_key":"not-a-conversation","previous_response_id":"resp_previous","input":[{"type":"function_call_output","call_id":"call_one","output":"secret tool payload"}]}`))
	if d.ConversationID != "thread" || d.TurnID != "turn" || d.WorkerID != "worker" || d.ParentWorkerID != "parent" || d.ToolCallID != "call_one" || d.PreviousResponseID != "resp_previous" {
		t.Fatal(d)
	}
	wire, _ := json.Marshal(d)
	if strings.Contains(string(wire), "secret") || strings.Contains(string(wire), "not-a-conversation") || d.BillingAccountID != "" {
		t.Fatal(string(wire))
	}
	for _, name := range []string{"conversationId", "turnId", "workerId", "parentWorkerId", "toolCallId", "previousResponseId", "modelFamily", "manufacturer"} {
		found := false
		for _, e := range d.Evidence {
			if e.Field == name && e.Source != "" && e.Key != "" {
				found = true
			}
		}
		if !found {
			t.Fatal("missing evidence", name)
		}
	}
	if got := requestDimensions(Route{}, nil, nil, []byte(`{"prompt_cache_key":"cache","metadata":{"user_id":"user","canonical_session_id":"inferred"}}`)); got != nil {
		t.Fatal(got)
	}
	if got := requestDimensions(Route{}, nil, nil, []byte(`{"previous_response_id":"Bearer sk-secret"}`)); got != nil {
		t.Fatal(got)
	}
	if got := requestDimensions(Route{}, nil, nil, []byte(`{"input":[{"type":"function_call_output","call_id":"a"},{"type":"function_call_output","call_id":"b"}]}`)); got != nil {
		t.Fatal("multiple tool IDs collapsed", got)
	}
	claude := requestDimensions(Route{}, nil, nil, []byte(`{"metadata":{"user_id":"{\"session_id\":\"session\",\"agent_id\":\"worker\",\"parent_agent_id\":\"parent\",\"account_uuid\":\"never-copy-account\"}"}}`))
	if claude == nil || claude.ConversationID != "session" || claude.WorkerID != "worker" || claude.ParentWorkerID != "parent" {
		t.Fatal(claude)
	}
	encoded, _ := json.Marshal(claude)
	if strings.Contains(string(encoded), "never-copy-account") {
		t.Fatal(string(encoded))
	}
	legacy := requestDimensions(Route{}, nil, nil, []byte(`{"metadata":{"user_id":"user_device_account_account_session_12345678-abcd-1234-abcd-123456789abc"}}`))
	if legacy == nil || legacy.ConversationID != "12345678-abcd-1234-abcd-123456789abc" {
		t.Fatal(legacy)
	}
	for _, marker := range []string{"", `,"subagent_kind":"thread_spawn"`} {
		h := http.Header{}
		h.Set("Session-Id", "broader-session")
		h.Set("X-Codex-Turn-Metadata", `{"thread_id":"child","parent_thread_id":"parent"`+marker+`}`)
		codex := requestDimensions(Route{}, nil, h, nil)
		if codex.ConversationID != "child" {
			t.Fatal("session masked explicit thread", codex)
		}
		if marker == "" && (codex.WorkerID != "" || codex.ParentWorkerID != "") {
			t.Fatal("ordinary thread fork became worker", codex)
		}
		if marker != "" && (codex.WorkerID != "child" || codex.ParentWorkerID != "parent") {
			t.Fatal("explicit worker relationship lost", codex)
		}
	}
}

func TestOutputClassificationExcludesTransportAndUsageEvents(t *testing.T) {
	for _, raw := range []string{
		`{"type":"response.created","response":{"id":"resp_a"}}`, `{"type":"ping"}`, `{"choices":[{"delta":{"role":"assistant"}}]}`,
		`{"usage":{"output_tokens":3}}`, `{"type":"response.completed","response":{"output":[{"content":[{"type":"output_text","text":"already delivered"}]}]}}`,
	} {
		if got := classifyOutput(gjson.Parse(raw), true); len(got) != 0 {
			t.Fatal(raw, got)
		}
	}
	for _, raw := range []string{
		`{"type":"response.output_text.delta","delta":"text"}`, `{"type":"response.reasoning_summary_text.delta","delta":"reason"}`,
		`{"choices":[{"delta":{"reasoning_content":"reason"}}]}`, `{"choices":[{"delta":{"tool_calls":[{"id":"call","function":{"name":"test"}}]}}]}`,
		`{"type":"content_block_delta","delta":{"type":"thinking_delta","thinking":"reason"}}`, `{"type":"content_block_start","content_block":{"type":"tool_use","id":"call","name":"test"}}`,
		`{"candidates":[{"content":{"parts":[{"text":"reason","thought":true}]}}]}`,
	} {
		if got := classifyOutput(gjson.Parse(raw), true); len(got) != 1 {
			t.Fatal(raw, got)
		}
	}
}

func TestMeasuredCompletionFollowsEarlyUsageAndKeepsJournalMetadataOnly(t *testing.T) {
	i, ctx, auth := dispatchFixture(t)
	if err := i.Before(ctx, auth, executor.Request{Model: "model"}, executor.Options{}); err != nil {
		t.Fatal(err)
	}
	id := usage.ExecutionRequestIDFromContext(ctx)
	i.active[id].started = time.Now().Add(-time.Second)
	i.Observe(ctx, []byte("data: {\"choices\":[{\"delta\":{\"role\":\"assistant\"}}]}\n\n"), true)
	if !i.active[id].firstOutput.IsZero() {
		t.Fatal("role counted as output")
	}
	i.Observe(ctx, []byte("data: {\"id\":\"resp_fixture\",\"object\":\"chat.completion.chunk\",\"choices\":[{\"delta\":{\"content\":\"PRIVATE_OUTPUT\"}}]}\n\n"), true)
	if i.active[id].firstOutput.IsZero() {
		t.Fatal("output missed")
	}
	r := usage.Record{RequestID: id, ParentRequestID: usage.ParentRequestIDFromContext(ctx), AuthID: auth.ID, ReasoningEffort: "medium", Detail: usage.Detail{Reported: &usage.ReportedTokens{OutputTokens: ptr(int64(7))}}}
	if err := i.Publish(ctx, r); err != nil {
		t.Fatal(err)
	}
	early, _ := i.Journal.Read(0, 100)
	if len(early.Events) != 2 || early.Events[1].Measurements.TotalDurationMs != nil {
		t.Fatal("early usage invented full duration")
	}
	if err := i.After(ctx, context.Canceled); err != nil {
		t.Fatal(err)
	}
	page, _ := i.Journal.Read(0, 100)
	last := page.Events[2]
	if last.Dimensions.EffectiveReasoningEffort != "medium" || last.Dimensions.ReasoningEffort != "" {
		t.Fatal("effective level mixed with requested level", last.Dimensions)
	}
	if last.Kind != "attempt_finished" || last.Outcome != nil || last.OutputTokens != nil || last.UsageReported || last.CompletionState == nil || *last.CompletionState != "canceled" {
		t.Fatal(last)
	}
	if last.Measurements.TotalDurationMs == nil || last.Measurements.FirstOutputLatencyMs == nil || *last.Measurements.TotalDurationMs < *last.Measurements.FirstOutputLatencyMs {
		t.Fatal(last.Measurements)
	}
	wire, _ := json.Marshal(page)
	if strings.Contains(string(wire), "PRIVATE_OUTPUT") || strings.Contains(string(wire), "choices") {
		t.Fatal("content leaked to journal", string(wire))
	}
	live, _ := i.Inspector.Read(0, 100)
	end := live.Events[len(live.Events)-1]
	if end.Kind != "attempt_finished" || *end.Outcome != "canceled" || *end.OutputTokens != 7 {
		t.Fatal(end)
	}
	if len(i.active) != 0 || i.captureSlots != 0 {
		t.Fatal("attempt capture not released")
	}
}

func TestOptionalJournalFieldsPreserveLegacySerialization(t *testing.T) {
	wire, err := json.Marshal(Event{})
	if err != nil {
		t.Fatal(err)
	}
	for _, field := range []string{"dimensions", "measurements", "completionState"} {
		if strings.Contains(string(wire), field) {
			t.Fatal("legacy digest shape changed", string(wire))
		}
	}
}

func TestPendingCaptureHasGlobalCapacityAndExpiry(t *testing.T) {
	i, ctx, auth := dispatchFixture(t)
	i.captureSlots = 256 // other active captures have exhausted the global bound
	if err := i.Before(ctx, auth, executor.Request{Model: "model"}, executor.Options{}); err != nil {
		t.Fatal(err)
	}
	id := usage.ExecutionRequestIDFromContext(ctx)
	if i.active[id].content != nil {
		t.Fatal("capture exceeded global capacity")
	}
	i.Observe(ctx, []byte(`{"choices":[{"delta":{"content":"still measured"}}]}`), true)
	if i.active[id].firstOutput.IsZero() {
		t.Fatal("content limit disabled measurements")
	}
	if err := i.After(ctx, nil); err != nil {
		t.Fatal(err)
	}
	i.captureSlots = 0
	if err := i.Before(ctx, auth, executor.Request{Model: "model"}, executor.Options{}); err != nil {
		t.Fatal(err)
	}
	i.Observe(ctx, []byte(`{"choices":[{"delta":{"content":"expired pending text"}}]}`), true)
	i.active[id].captureExpires = time.Now().Add(-time.Second)
	if err := i.After(ctx, nil); err != nil {
		t.Fatal(err)
	}
	live, _ := i.Inspector.Read(0, 100)
	for _, e := range live.Events {
		if strings.Contains(e.Text, "expired pending") {
			t.Fatal("expired partial line revived", e)
		}
	}
	if i.captureSlots != 0 {
		t.Fatal("expired capture slot not released")
	}
}

func TestFreeOnlyDescriptorCannotBypassRuntimeEnforcement(t *testing.T) {
	for _, policy := range []string{"free_only", "unknown"} {
		b := Bindings{Accounts: map[string]string{"a": "credential"}, Routes: map[string]Route{"route": {ID: "route", Model: "model", Accounts: []string{"a"}, Selection: "strict", CostPolicy: policy, PricingClass: "free", FreeOnlyEnforcement: "claimed-model-pin"}}}
		if _, err := New(testJournal(t), b); err == nil {
			t.Fatal("unenforced policy accepted", policy)
		}
	}
}
