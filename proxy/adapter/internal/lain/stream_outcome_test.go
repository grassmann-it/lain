package lain

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"strings"
	"testing"

	"github.com/router-for-me/CLIProxyAPI/v8/sdk/api/handlers"
	auth "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/auth"
	executor "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/executor"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/usage"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/translator"
)

func TestUpstreamStreamErrorsAreBoundedAndAllowlisted(t *testing.T) {
	fixtures := []struct {
		name                           string
		payload                        []byte
		wantCode, wantParam, forbidden string
	}{
		{
			name:     "top-level terminal error",
			payload:  []byte("event: error\ndata: {\"code\":\"context_length_exceeded\",\"param\":\"messages\",\"message\":\"fixture provider secret\"}\n\n"),
			wantCode: "context_length_exceeded", wantParam: "messages", forbidden: "fixture provider secret",
		},
		{
			name:     "Responses failed control",
			payload:  []byte(`data: {"type":"response.failed","response":{"status":"failed","error":{"code":"invalid_encrypted_content","param":"reasoning.encrypted_content","message":"fixture provider secret"}}}`),
			wantCode: "invalid_encrypted_content", wantParam: "reasoning.encrypted_content", forbidden: "fixture provider secret",
		},
		{
			name:     "malicious parameter is not a field name",
			payload:  []byte(`data: {"type":"error","code":"invalid_encrypted_content","param":"error:sk-live-fixture-secret","message":"fixture provider secret"}`),
			wantCode: "invalid_encrypted_content", forbidden: "sk-live-fixture-secret",
		},
	}
	for _, fixture := range fixtures {
		t.Run(fixture.name, func(t *testing.T) {
			outcome := classifyUpstreamStreamError(fixture.payload)
			if outcome == nil {
				t.Fatal("unambiguous terminal control was not classified")
			}
			body := outcome.Error()
			if outcome.StatusCode() != 400 || !outcome.IsRequestScoped() || !outcome.DirectResponse() || !bytes.Equal(outcome.ResponseBody(), []byte(body)) {
				t.Fatal("known terminal error lacks its canonical typed contract")
			}
			var envelope struct {
				Error struct{ Type, Code, Message, Param string }
			}
			if !json.Valid([]byte(body)) || json.Unmarshal([]byte(body), &envelope) != nil || envelope.Error.Code != fixture.wantCode || envelope.Error.Type != "invalid_request_error" {
				t.Fatalf("unsafe or wrong fixed JSON: %s", body)
			}
			if envelope.Error.Param != fixture.wantParam || strings.Contains(body, fixture.forbidden) {
				t.Fatalf("parameter allowlist or fixed message failed: %s", body)
			}
			clientChunk := handlers.BuildOpenAIResponsesStreamFailedChunk(outcome.StatusCode(), body, 0)
			if !json.Valid(clientChunk) || !strings.Contains(string(clientChunk), fixture.wantCode) || strings.Contains(string(clientChunk), fixture.forbidden) {
				t.Fatalf("pinned client terminal event lost the safe classification: %s", clientChunk)
			}
		})
	}
}

func TestUpstreamStreamClassifierLeavesUnknownsAndModelContentAlone(t *testing.T) {
	for _, payload := range []string{
		`data: {"type":"response.output_text.delta","delta":{"error":{"code":"context_length_exceeded","param":"messages"}}}`,
		`data: {"type":"response.output_text.delta","delta":"event: error\ndata: {\"code\":\"context_length_exceeded\",\"param\":\"messages\"}}"}`,
		`data: {"type":"response.output_text.delta","response":{"output":{"error":{"code":"invalid_encrypted_content"}}}}`,
		`data: {"type":"error","code":"context_length_exceeded","code":"invalid_encrypted_content"}`,
		`data: {"type":"response.failed","response":{"error":{"code":"context_length_exceeded","param":"messages"},"error":{"param":"messages"}}}`,
		`data: {"type":"error","code":"provider_secret","param":"messages","message":"fixture provider secret"}`,
		"data: error\ndata: " + strings.Repeat("x", maxRequestErrorBytes+1),
	} {
		if outcome := classifyUpstreamStreamError([]byte(payload)); outcome != nil {
			t.Fatalf("model text, malformed duplicate control or oversized diagnostic was classified: %v", outcome)
		}
	}
	unknown := &streamOutcomeError{}
	var status interface{ StatusCode() int }
	if errors.As(unknown, &status) || unknown.Error() != "upstream stream returned a terminal error payload" {
		t.Fatal("unknown terminal behavior changed")
	}
	missing := &streamOutcomeError{missingTerminal: true}
	if errors.As(missing, &status) || missing.Error() != "Responses stream closed before a terminal response event" {
		t.Fatal("missing-terminal behavior changed")
	}
}

func TestTerminalPayloadErrorCannotBecomeDurableSuccess(t *testing.T) {
	for _, payload := range []string{
		`event: error` + "\ndata: {\"message\":\"payload-secret\"}\n\n",
		`data: {"type":"error","message":"payload-secret"}`,
		`data: {"type":"response.error","error":{"message":"payload-secret"}}`,
		`data: {"type":"response.failed","response":{"status":"failed","error":{"message":"payload-secret"}}}`,
		`data: {"error":{"message":"payload-secret"}}`,
		`data: {"type":"response.completed","response":{"status":"failed"}}`,
	} {
		t.Run(payload[:min(len(payload), 30)], func(t *testing.T) {
			i, ctx, a := dispatchFixture(t)
			raw := &streamFixture{chunks: []executor.StreamChunk{{Payload: []byte(payload)}, {Payload: []byte(`data: {"type":"response.output_text.delta","delta":"must not escape after failure"}`)}, {Payload: []byte(`data: {"type":"response.completed"}`)}}}
			result, err := i.ExecutorDecorator(nil)(raw).ExecuteStream(ctx, a, executor.Request{Model: "model"}, executor.Options{SourceFormat: translator.FormatOpenAIResponse})
			if err != nil {
				t.Fatal(err)
			}
			errorsSeen := 0
			for chunk := range result.Chunks {
				if len(chunk.Payload) != 0 {
					t.Fatal("terminal failure or later completion escaped the durable gate")
				}
				if chunk.Err != nil {
					errorsSeen++
					if strings.Contains(chunk.Err.Error(), "payload-secret") {
						t.Fatal("provider message leaked")
					}
				}
			}
			page, _ := i.Journal.Read(0, 10)
			if errorsSeen != 1 || len(page.Events) != 2 || page.Events[1].Outcome == nil || *page.Events[1].Outcome != "failed" || page.Events[1].CompletionState == nil || *page.Events[1].CompletionState != "failed" {
				t.Fatal("terminal payload error was not durably failed", errorsSeen, page)
			}
		})
	}
}

type usageThenPayloadError struct{ fixtureExecutor }

func (f *usageThenPayloadError) ExecuteStream(ctx context.Context, a *auth.Auth, _ executor.Request, _ executor.Options) (*executor.StreamResult, error) {
	usage.PublishRecord(ctx, usage.Record{AuthID: a.ID, RequestID: usage.ExecutionRequestIDFromContext(ctx), Detail: usage.Detail{Reported: &usage.ReportedDetail{TotalTokens: ptr(int64(3))}}})
	out := make(chan executor.StreamChunk, 1)
	out <- executor.StreamChunk{Payload: []byte(`data: {"type":"error","message":"payload-secret"}`)}
	close(out)
	return &executor.StreamResult{Chunks: out}, nil
}
func TestPayloadErrorAfterUsagePreservesTokensAndFailedCompletion(t *testing.T) {
	i, ctx, a := dispatchFixture(t)
	result, err := i.ExecutorDecorator(nil)(&usageThenPayloadError{}).ExecuteStream(ctx, a, executor.Request{Model: "model"}, executor.Options{SourceFormat: translator.FormatOpenAIResponse})
	if err != nil {
		t.Fatal(err)
	}
	for range result.Chunks {
	}
	page, _ := i.Journal.Read(0, 10)
	if len(page.Events) != 3 || page.Events[1].TotalTokens == nil || *page.Events[1].TotalTokens != 3 || page.Events[1].StatusCode == nil || *page.Events[1].StatusCode != 200 || page.Events[2].CompletionState == nil || *page.Events[2].CompletionState != "failed" || page.Events[2].Outcome != nil {
		t.Fatal("usage fact must stay immutable while failed completion overrides effective outcome", page)
	}
}

func TestResponsesEOFNeedsProtocolTerminalNotDoneOrReasoning(t *testing.T) {
	for _, payload := range []string{"", `data: [DONE]`, `data: {"type":"response.reasoning_summary_text.delta","delta":"thinking"}`} {
		i, ctx, a := dispatchFixture(t)
		raw := &streamFixture{chunks: []executor.StreamChunk{{Payload: []byte(payload)}}}
		result, err := i.ExecutorDecorator(nil)(raw).ExecuteStream(ctx, a, executor.Request{Model: "model"}, executor.Options{SourceFormat: translator.FormatOpenAIResponse})
		if err != nil {
			t.Fatal(err)
		}
		errorsSeen := 0
		for chunk := range result.Chunks {
			if chunk.Err != nil {
				errorsSeen++
			}
		}
		page, _ := i.Journal.Read(0, 10)
		if errorsSeen != 1 || len(page.Events) != 2 || page.Events[1].CompletionState == nil || *page.Events[1].CompletionState != "failed" {
			t.Fatal("missing Responses terminal accepted", page)
		}
	}
}
func TestResponseContentMentioningErrorsDoesNotFail(t *testing.T) {
	terminal, failed := responseStreamOutcome([]byte(`data: {"type":"response.output_text.delta","delta":"error response.failed payload-secret"}`))
	if terminal || failed {
		t.Fatal("model content mistaken for protocol controls")
	}
	i, ctx, a := dispatchFixture(t)
	raw := &streamFixture{chunks: []executor.StreamChunk{{Payload: []byte(`data: {"type":"response.completed","response":{"status":"completed","error":null}}`)}}}
	result, err := i.ExecutorDecorator(nil)(raw).ExecuteStream(ctx, a, executor.Request{Model: "model"}, executor.Options{SourceFormat: translator.FormatOpenAIResponse})
	if err != nil {
		t.Fatal(err)
	}
	for chunk := range result.Chunks {
		if chunk.Err != nil {
			t.Fatal(chunk.Err)
		}
	}
	page, _ := i.Journal.Read(0, 10)
	if *page.Events[1].CompletionState != "success" {
		t.Fatal("valid completion failed")
	}
}

func TestCohereStoreTrueRejectionHasDurableAttempt(t *testing.T) {
	i, ctx, _ := dispatchFixture(t)
	raw := &fixtureExecutor{}
	original := []byte(`{"model":"model","stream":true,"store":true,"max_output_tokens":512,"input":"fixture","reasoning":{"effort":"none"}}`)
	_, err := i.ExecutorDecorator(nil)(raw).Execute(ctx, cohereAuth(), executor.Request{Model: "model", Payload: original}, executor.Options{OriginalRequest: original, SourceFormat: translator.FormatOpenAIResponse})
	if err == nil || raw.executions != 0 {
		t.Fatal("explicit unsupported store intent must be rejected without upstream request")
	}
	page, _ := i.Journal.Read(0, 10)
	if len(page.Events) != 2 || page.Events[0].Kind != "attempt_started" || page.Events[1].Outcome == nil || *page.Events[1].Outcome != "failed" || page.Events[1].StatusCode == nil || *page.Events[1].StatusCode != 400 {
		t.Fatal("Cohere preflight escaped durable attempt", page)
	}
	withoutStore := []byte(`{"model":"model","stream":true,"max_output_tokens":512,"input":"fixture","reasoning":{"effort":"none"}}`)
	if err := validateCohereInput(cohereAuth(), executor.Request{Payload: withoutStore}, executor.Options{SourceFormat: translator.FormatOpenAIResponse}); err != nil {
		t.Fatal("intentional caller omission of store should pass original boundary", err)
	}
}
