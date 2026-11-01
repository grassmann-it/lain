package lain

import (
	"bytes"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"sync"
	"testing"
	"time"
)

func recordingTestPolicy() RecordingPolicy {
	return RecordingPolicy{Revision: 1, Enabled: true, Mode: "content", Scope: "owned_gateway", StopGeneration: 1}
}
func recordingTestRecorder(t *testing.T) *Recorder {
	t.Helper()
	r := NewRecorder()
	if err := r.Configure(recordingTestPolicy()); err != nil {
		t.Fatal(err)
	}
	return r
}
func recordingTestAttempt(r *Recorder, request, attempt string) {
	r.Admit(request, "responses", "any-model", "route", []byte(`{"model":"any-model","input":"hello"}`))
	r.BeginAttempt(Event{RequestID: request, AttemptID: attempt, RouteID: ptr("route"), AccountID: "account", Provider: "provider", RequestedModel: "any-model", Model: "actual-model"})
}
func recordingReadAll(t *testing.T, r *Recorder) RecordingBatch {
	t.Helper()
	b, err := r.Read("", 0, 1000)
	if err != nil {
		t.Fatal(err)
	}
	return b
}
func TestRecordingDisabledHasNoContentBuffers(t *testing.T) {
	r := NewRecorder()
	r.Admit("r", "responses", "m", "route", []byte("private"))
	r.BeginAttempt(Event{RequestID: "r", AttemptID: "a"})
	r.ObserveOutput("a", []byte("private"), true)
	r.FinishAttempt("a", "success")
	if r.ring != nil || len(r.requests) != 0 || len(r.attempts) != 0 || r.Status().QueuedBytes != 0 {
		t.Fatal("disabled recorder retained content or request state")
	}
}
func TestRecordingFullRequestResponseAndIdentity(t *testing.T) {
	r := recordingTestRecorder(t)
	recordingTestAttempt(r, "request", "attempt")
	r.ObserveOutput("attempt", []byte(`{"output":[{"role":"assistant","content":[{"type":"output_text","text":"hello"}]}]}`), false)
	r.FinishAttempt("attempt", "success")
	r.CompleteRequest("request")
	b := recordingReadAll(t, r)
	if len(b.Events) != 3 {
		t.Fatalf("events=%d", len(b.Events))
	}
	if b.Events[0].Boundary != "client_ingress" || b.Events[0].BodyEncoding != "utf8_json" || !bytes.Contains(b.Events[0].Body, []byte(`"input":"hello"`)) {
		t.Fatal("original client request unavailable")
	}
	o := b.Events[2]
	if o.Boundary != "executor_output" || o.AttemptID != "attempt" || o.Model != "actual-model" || o.PolicyRevision != 1 || o.StopGeneration != 1 || o.CaptureID != "request" || !o.Complete || o.ChunkCount != 1 {
		t.Fatalf("incorrect source metadata: %+v", o)
	}
	for n, e := range b.Events {
		if e.Sequence != int64(n+1) || e.SourceEpoch != b.SourceEpoch || e.EventID == "" {
			t.Fatal("unstable identity")
		}
	}
	encoded, err := json.Marshal(b)
	if err != nil {
		t.Fatal(err)
	}
	var wire map[string]any
	if err = json.Unmarshal(encoded, &wire); err != nil {
		t.Fatal(err)
	}
	events := wire["observations"].([]any)
	if _, ok := events[0].(map[string]any)["body"].(string); !ok {
		t.Fatal("body must be base64 for .NET byte[] contract")
	}
	b.Events[0].Body[0] = '!'
	again := recordingReadAll(t, r)
	if again.Events[0].Body[0] != '{' {
		t.Fatal("read mutated retained observation")
	}
	if r.Status().ActiveBytes != 0 || r.Status().ActiveRequests != 0 || r.Status().ActiveAttempts != 0 {
		t.Fatal("completed request retained assembly")
	}
}
func TestRecordingSourceRingEvictionEpochAndNonDestructiveRead(t *testing.T) {
	r := NewRecorder()
	p := recordingTestPolicy()
	p.Limits.MaxEvents = 2
	if err := r.Configure(p); err != nil {
		t.Fatal(err)
	}
	for n := 0; n < 5; n++ {
		id := fmt.Sprint(n)
		r.Admit(id, "chat", "model", "route", []byte(`{}`))
		r.CompleteRequest(id)
	}
	b := recordingReadAll(t, r)
	if len(b.Events) != 2 || b.Gap == nil || b.Gap.FromSequence != 1 || b.Gap.ThroughSequence != 3 || b.NextSequence != 5 || b.DroppedCount != 3 {
		t.Fatalf("unexpected gap/page: %+v", b)
	}
	again := recordingReadAll(t, r)
	if again.Events[0].EventID != b.Events[0].EventID {
		t.Fatal("pull drained source")
	}
	if _, err := r.Read("wrong-epoch", 0, 100); err == nil {
		t.Fatal("accepted another source epoch")
	}
	if _, err := r.Read(b.SourceEpoch, 6, 100); err == nil {
		t.Fatal("accepted future cursor")
	}
	r.Disable()
	after := recordingReadAll(t, r)
	if len(after.Events) != 0 || after.Gap == nil || after.NextSequence != 5 {
		t.Fatal("stop did not discard pending source content")
	}
	if err := r.Configure(p); err != nil {
		t.Fatal(err)
	}
	if r.Status().Enabled {
		t.Fatal("same policy re-enabled management stop")
	}
	p.Revision++
	p.StopGeneration++
	if err := r.Configure(p); err != nil {
		t.Fatal(err)
	}
	if !r.Status().Enabled {
		t.Fatal("new explicit policy did not enable")
	}
}
func TestRecordingStreamTerminalExtractionAndNoRawDefault(t *testing.T) {
	r := recordingTestRecorder(t)
	recordingTestAttempt(r, "r", "a")
	r.ObserveOutput("a", []byte("data: {\"type\":\"response.output_text.delta\",\"delta\":\"sk-secretfragment\"}\n\n"), true)
	r.ObserveOutput("a", []byte("data: {\"type\":\"response.completed\",\"response\":{\"id\":\"response1\",\"output\":[]}}\n\n"), true)
	r.FinishAttempt("a", "success")
	b := recordingReadAll(t, r)
	last := b.Events[len(b.Events)-1]
	if string(last.Body) != `{"id":"response1","output":[]}` || last.BodyEncoding != "utf8_json" || last.ChunkCount != 2 {
		t.Fatalf("bad assembled response: %+v", last)
	}
	for _, e := range b.Events {
		if e.Kind == "chunk_metadata" && len(e.Body) != 0 {
			t.Fatal("raw delta leaked into chunk observation")
		}
	}
	r.CompleteRequest("r")
	recordingTestAttempt(r, "r2", "a2")
	r.ObserveOutput("a2", []byte("data: {\"choices\":[{\"delta\":{\"content\":\"secret-fragment\"}}]}\n\n"), true)
	r.FinishAttempt("a2", "success")
	b = recordingReadAll(t, r)
	last = b.Events[len(b.Events)-1]
	if last.Body != nil || last.ContentOmissionReason != "stream_body_unavailable" || last.Complete {
		t.Fatal("unassembled stream was exposed as complete JSON")
	}
}
func TestRecordingInvalidUTF8AndBodyOversizeAreGaps(t *testing.T) {
	r := recordingTestRecorder(t)
	r.Admit("invalid", "responses", "m", "route", []byte{0xff})
	b := recordingReadAll(t, r)
	if b.Events[0].Body != nil || b.Events[0].ContentOmissionReason != "invalid_utf8" || b.Events[0].Complete {
		t.Fatal("UTF-8 replaced or corruption hidden")
	}
	r.Admit("large", "responses", "m", "route", make([]byte, r.Status().Limits.MaxBodyBytes+1))
	b = recordingReadAll(t, r)
	last := b.Events[len(b.Events)-1]
	if last.Body != nil || last.ContentOmissionReason != "body_limit" || last.Complete {
		t.Fatal("oversize was not omitted")
	}
}
func TestRecordingMetadataScopeAndAssemblyPressure(t *testing.T) {
	r := NewRecorder()
	p := recordingTestPolicy()
	p.Mode = "metadata_only"
	p.RouteIDs = []string{"wanted"}
	if err := r.Configure(p); err != nil {
		t.Fatal(err)
	}
	r.Admit("r", "responses", "any", "other", []byte("private"))
	if r.Status().QueuedEvents != 0 {
		t.Fatal("route filter ignored")
	}
	r.Admit("r", "responses", "any", "wanted", []byte("private"))
	b := recordingReadAll(t, r)
	if b.Events[0].Body != nil || b.Events[0].ContentOmissionReason != "metadata_only" {
		t.Fatal("metadata-only captured content")
	}
	p = recordingTestPolicy()
	p.Revision = 2
	p.StopGeneration = 2
	p.Limits.MaxBodyBytes = 4096
	p.Limits.MaxActiveBytes = 4096
	if err := r.Configure(p); err != nil {
		t.Fatal(err)
	}
	recordingTestAttempt(r, "r1", "a1")
	recordingTestAttempt(r, "r2", "a2")
	r.ObserveOutput("a1", []byte(`{"x":"hello"}`), false)
	r.ObserveOutput("a2", []byte(`{"x":"world"}`), false)
	if r.Status().ActiveBytes > 4096 {
		t.Fatal("assembly bytes exceeded bound")
	}
	r.FinishAttempt("a1", "success")
	r.FinishAttempt("a2", "success")
	b = recordingReadAll(t, r)
	last := b.Events[len(b.Events)-1]
	if last.ContentOmissionReason != "assembly_capacity" || last.Body != nil {
		t.Fatal("capacity omission hidden")
	}
}
func TestRecordingConfigMissingInvalidAndSharedPolicy(t *testing.T) {
	path := filepath.Join(t.TempDir(), "recording.json")
	r, closeRecorder := OpenRecording(path)
	closeRecorder()
	if r.Status().Enabled {
		t.Fatal("missing file enabled capture")
	}
	if err := os.WriteFile(path, []byte("not json"), 0600); err != nil {
		t.Fatal(err)
	}
	r, closeRecorder = OpenRecording(path)
	if r.Status().Enabled || r.Status().ErrorCode == "" {
		t.Fatal("invalid config was not diagnosed")
	}
	closeRecorder()
	p, err := decodeRecordingConfiguration([]byte(`{"revision":4,"enabled":true,"mode":"content","scope":"owned_gateway","stopGeneration":5,"limits":{"maxRecordBytes":16777216,"queueBytes":67108864,"diskBytes":5368709120}}`))
	if err != nil {
		t.Fatal(err)
	}
	if p.Limits.MaxBodyBytes != 16<<20 || p.Limits.MaxBytes != 64<<20 {
		t.Fatal("shared limits were not respected")
	}
	if err := os.Chmod(path, 0644); err != nil {
		t.Fatal(err)
	}
	if _, code := readRecordingConfiguration(path); code != "invalid_recording_configuration" {
		t.Fatal("public configuration accepted")
	}
}
func TestRecordingStopConcurrentCallbacksAndBoundedSource(t *testing.T) {
	r := recordingTestRecorder(t)
	var wg sync.WaitGroup
	for w := 0; w < 8; w++ {
		wg.Add(1)
		go func(w int) {
			defer wg.Done()
			for n := 0; n < 100; n++ {
				id := fmt.Sprintf("%d-%d", w, n)
				recordingTestAttempt(r, id, id)
				r.ObserveOutput(id, []byte(`{"output":[]}`), false)
				r.FinishAttempt(id, "success")
				r.CompleteRequest(id)
			}
		}(w)
	}
	r.Disable()
	wg.Wait()
	s := r.Status()
	if s.Enabled || s.QueuedBytes != 0 || s.ActiveBytes != 0 || s.ActiveRequests != 0 || s.ActiveAttempts != 0 {
		t.Fatal("stop retained active/pending observations")
	}
}
func TestRecordingAuthenticationGateDoesNotInventClientIdentity(t *testing.T) {
	for _, h := range []string{"", "shared-secret", "Basic shared-secret", "Bearer ", "Bearer wrong"} {
		if RecordingInferenceAuthorized(h, []string{"shared-secret", ""}) {
			t.Fatalf("authorized invalid bearer %q", h)
		}
	}
	if !RecordingInferenceAuthorized("Bearer shared-secret", []string{"other", "shared-secret"}) {
		t.Fatal("valid configured bearer rejected")
	}
	if NewRecorder().WantsAdmission("model", "route") {
		t.Fatal("disabled path requests dimensions work")
	}
}

func TestRecordingHeldLockNeverBlocksInferenceCallbacks(t *testing.T) {
	r := recordingTestRecorder(t)
	recordingTestAttempt(r, "request", "attempt")
	r.ObserveOutput("attempt", []byte(`{"before":"partial"}`), true)
	r.mu.Lock()
	done := make(chan struct{})
	go func() {
		defer close(done)
		_ = r.WantsAdmission("model", "route")
		r.Admit("other", "responses", "model", "route", []byte(`{}`))
		r.BeginAttempt(Event{RequestID: "other", AttemptID: "other"})
		r.ObserveOutput("attempt", []byte(`{"missing":"part"}`), true)
		r.ObserveUsage(Event{RequestID: "request", AttemptID: "attempt", OutputTokens: ptr(int64(0))})
		r.FinishAttempt("attempt", "success")
		r.CompleteRequest("request")
	}()
	select {
	case <-done:
	case <-time.After(time.Second):
		r.mu.Unlock()
		<-done
		t.Fatal("callback blocked waiting for recorder lock")
	}
	r.mu.Unlock()
	status := r.Status() // drains deferred cleanup on the control path
	if status.ActiveRequests != 0 || status.ActiveAttempts != 0 || status.ActiveBytes != 0 {
		t.Fatalf("cleanup leaked capacity: %+v", status)
	}
	if status.ContentionDrops != 7 || status.DroppedEvents < 7 {
		t.Fatalf("contention loss not counted: %+v", status)
	}
	b := recordingReadAll(t, r)
	if b.DroppedCount < status.ContentionDrops {
		t.Fatal("batch dropped count regressed")
	}
	last := b.Events[len(b.Events)-1]
	if last.Kind != "gap" || last.Body != nil || last.ContentOmissionReason == "" || last.Complete {
		t.Fatalf("missed finish did not publish a content-free gap: %+v", last)
	}
}

func TestRecordingMissedOutputCannotMasqueradeAsCompleteResponse(t *testing.T) {
	r := recordingTestRecorder(t)
	recordingTestAttempt(r, "r", "a")
	r.ObserveOutput("a", []byte(`{"output":[]}`), false)
	r.mu.Lock()
	r.ObserveOutput("a", []byte(`{"secret_fragment":"missing"}`), false)
	r.mu.Unlock()
	r.FinishAttempt("a", "success")
	r.CompleteRequest("r")
	b := recordingReadAll(t, r)
	last := b.Events[len(b.Events)-1]
	if last.Body != nil || last.ContentOmissionReason != "observer_contention" || last.Complete {
		t.Fatal("partially observed body was exposed as complete")
	}
}

func TestRecordingCleanupOverflowAndConcurrentDropsRemainBounded(t *testing.T) {
	r := recordingTestRecorder(t)
	for n := 0; n < 16; n++ {
		id := fmt.Sprint(n)
		recordingTestAttempt(r, id, id)
		r.ObserveOutput(id, []byte(`{"output":[]}`), false)
	}
	r.mu.Lock()
	var wg sync.WaitGroup
	const workers, iterations = 8, 200
	for w := 0; w < workers; w++ {
		wg.Add(1)
		go func(w int) {
			defer wg.Done()
			for n := 0; n < iterations; n++ {
				id := fmt.Sprint(n % 16)
				r.ObserveOutput(id, []byte("missed"), true)
				r.FinishAttempt(id, "success")
				r.CompleteRequest(id)
			}
		}(w)
	}
	done := make(chan struct{})
	go func() { wg.Wait(); close(done) }()
	select {
	case <-done:
	case <-time.After(time.Second):
		r.mu.Unlock()
		<-done
		t.Fatal("cleanup producers blocked")
	}
	if len(r.cleanup) > cap(r.cleanup) || !r.cleanupOverflow.Load() {
		r.mu.Unlock()
		t.Fatal("cleanup did not use bounded overflow path")
	}
	r.mu.Unlock()
	s := r.Status()
	if s.ActiveRequests != 0 || s.ActiveAttempts != 0 || s.ActiveBytes != 0 {
		t.Fatalf("overflow leaked active capacity: %+v", s)
	}
	if s.ContentionDrops != workers*iterations*3 || s.DroppedEvents < s.ContentionDrops {
		t.Fatal("racing increments lost dropped-count updates")
	}
	b := recordingReadAll(t, r)
	if b.DroppedCount < s.DroppedEvents {
		t.Fatal("batch dropped count regressed")
	}
	recordingTestAttempt(r, "new", "new")
	r.ObserveOutput("new", []byte(`{"output":[]}`), false)
	r.FinishAttempt("new", "success")
	r.CompleteRequest("new")
	if r.Status().ActiveAttempts != 0 || r.Status().ActiveRequests != 0 {
		t.Fatal("capture did not recover capacity after maintenance")
	}
}

func TestRecordingUsagePreservesNullableZeroAndNoCredentialFields(t *testing.T) {
	r := recordingTestRecorder(t)
	recordingTestAttempt(r, "request", "attempt")
	input, zero, total, status := int64(12), int64(0), int64(12), 200
	e := Event{RequestID: "request", AttemptID: "attempt", CredentialID: "CREDENTIAL_MUST_NOT_LEAVE_EVENT", InputTokens: &input, OutputTokens: &zero, TotalTokens: &total, StatusCode: &status}
	r.ObserveUsage(e)
	// Source owns its nullable measurements after the callback returns.
	input = 999
	zero = 999
	total = 999
	status = 500
	r.ObserveUsage(Event{RequestID: "wrong", AttemptID: "attempt", OutputTokens: ptr(int64(66))})
	r.ObserveOutput("attempt", []byte(`{"output":[]}`), false)
	r.FinishAttempt("attempt", "success")
	r.CompleteRequest("request")
	b := recordingReadAll(t, r)
	o := b.Events[len(b.Events)-1]
	if o.Tokens == nil || o.Tokens.Input == nil || *o.Tokens.Input != 12 || o.Tokens.Output == nil || *o.Tokens.Output != 0 || o.Tokens.Total == nil || *o.Tokens.Total != 12 || o.Tokens.Cached != nil || o.Tokens.CacheCreation != nil || o.Tokens.Reasoning != nil || o.StatusCode == nil || *o.StatusCode != 200 {
		t.Fatalf("nullable tokens/status corrupted: %+v", o)
	}
	encoded, err := json.Marshal(b)
	if err != nil {
		t.Fatal(err)
	}
	if bytes.Contains(encoded, []byte("CREDENTIAL_MUST_NOT_LEAVE_EVENT")) || bytes.Contains(encoded, []byte("credentialId")) {
		t.Fatal("usage copied credential fields")
	}
	*o.Tokens.Input = 111
	*o.StatusCode = 599
	again := recordingReadAll(t, r)
	last := again.Events[len(again.Events)-1]
	if *last.Tokens.Input != 12 || *last.StatusCode != 200 {
		t.Fatal("read mutated retained nullable fields")
	}
}
