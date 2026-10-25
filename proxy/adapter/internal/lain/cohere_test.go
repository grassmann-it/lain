package lain

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"strings"
	"testing"

	auth "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/auth"
	executor "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/executor"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/usage"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/translator"
	"github.com/tidwall/gjson"
)

func cohereAuth() *auth.Auth {
	return &auth.Auth{ID: "credential-v2", Provider: "openai-compatible-cohere-openai-chat", Attributes: map[string]string{"compat_name": "cohere-openai-chat", "upstream_protocol": "openai-chat"}}
}

func TestCohereRejectsUnsupportedExplicitIntentBeforeTranslation(t *testing.T) {
	for _, control := range []string{`"stream_options":{"include_usage":true}`, `"stream_options":{"include_usage":false}`, `"max_completion_tokens":64`, `"reasoning_effort":"low"`, `"reasoning_effort":null`, `"parallel_tool_calls":true`, `"tool_choice":"required"`, `"metadata":{"purpose":"fixture"}`, `"n":2`, `"logprobs":true`, `"documents":["fixture"]`, `"max_tokens":64,"max_tokens":32`} {
		t.Run(control, func(t *testing.T) {
			i, ctx, _ := dispatchFixture(t)
			raw := &fixtureExecutor{}
			original := []byte(`{"model":"model","messages":[],` + control + `}`)
			_, err := i.ExecutorDecorator(nil)(raw).Execute(ctx, cohereAuth(), executor.Request{Model: "model", Payload: []byte(`{"model":"model","messages":[]}`)}, executor.Options{OriginalRequest: original, SourceFormat: translator.FormatOpenAI})
			var status interface{ StatusCode() int }
			if err == nil || !errors.As(err, &status) || status.StatusCode() != 400 || raw.executions != 0 {
				t.Fatal("unsupported option lost before validation", err, raw.executions)
			}
			if i.Journal.Health() != nil {
				t.Fatal("input validation poisoned journal")
			}
		})
	}
}

func TestCohereFinalBoundaryRemovesOnlyInjectedUsageAndPreservesIntent(t *testing.T) {
	for _, effort := range []string{"none", "high"} {
		original := `{"model":"command-a-plus-05-2026","messages":[{"role":"tool","tool_call_id":"call_1","content":"result"}],"stream":true,"max_tokens":64,"reasoning_effort":"` + effort + `","tools":[{"type":"function","function":{"name":"echo","parameters":{"type":"object"}}}]}`
		if err := validateCohereInput(cohereAuth(), executor.Request{Payload: []byte(original)}, executor.Options{SourceFormat: translator.FormatOpenAI}); err != nil {
			t.Fatal(err)
		}
		var expected map[string]json.RawMessage
		_ = json.Unmarshal([]byte(original), &expected)
		payload := strings.TrimSuffix(original, "}") + `,"stream_options":{"include_usage":true}}`
		calls := 0
		tr := &guardedTransport{auth: cohereAuth(), cohereValidated: true, next: fixtureTransport(func(req *http.Request) (*http.Response, error) {
			calls++
			data, _ := io.ReadAll(req.Body)
			var got map[string]json.RawMessage
			_ = json.Unmarshal(data, &got)
			if len(got) != len(expected) || got["stream_options"] != nil || gjson.GetBytes(data, "max_tokens").Int() != 64 {
				t.Fatal("invalid final Cohere fields", string(data))
			}
			for key, value := range expected {
				if string(got[key]) != string(value) {
					t.Fatalf("user intent changed for %s", key)
				}
			}
			copy, err := req.GetBody()
			if err != nil {
				t.Fatal(err)
			}
			replay, _ := io.ReadAll(copy)
			copy.Close()
			if string(replay) != string(data) || req.ContentLength != int64(len(data)) {
				t.Fatal("normalized body replay/length changed")
			}
			return &http.Response{StatusCode: 200, Header: http.Header{}, Body: io.NopCloser(strings.NewReader(`{}`))}, nil
		})}
		req, _ := http.NewRequestWithContext(context.Background(), "POST", "http://fixture.invalid/chat/completions", strings.NewReader(payload))
		response, err := tr.RoundTrip(req)
		if err != nil || calls != 1 {
			t.Fatal(err, calls)
		}
		response.Body.Close()
	}
}

func TestCohereFinalBoundaryRejectsUnqualifiedInjectedOptions(t *testing.T) {
	for _, control := range []string{`"stream_options":{"include_usage":false}`, `"stream_options":{"include_usage":true,"extra":true}`, `"max_completion_tokens":64`, `"parallel_tool_calls":true`, `"reasoning_effort":"medium"`} {
		calls := 0
		tr := &guardedTransport{auth: cohereAuth(), cohereValidated: true, next: fixtureTransport(func(*http.Request) (*http.Response, error) { calls++; return nil, errors.New("must not dispatch") })}
		req, _ := http.NewRequest("POST", "http://fixture.invalid/chat/completions", strings.NewReader(`{"model":"model","messages":[],`+control+`}`))
		if _, err := tr.RoundTrip(req); err == nil || calls != 0 {
			t.Fatal("unqualified final control dispatched", control, err)
		}
	}
}

func TestOtherProviderStreamingUsageAndFieldsRemainUntouched(t *testing.T) {
	body := `{"model":"model","max_completion_tokens":64,"stream_options":{"include_usage":true},"reasoning_effort":"medium","parallel_tool_calls":true}`
	a := cohereAuth()
	a.Provider, a.Attributes["compat_name"] = "openai-compatible-other-openai-chat", "other-openai-chat"
	tr := &guardedTransport{auth: a, next: fixtureTransport(func(req *http.Request) (*http.Response, error) {
		data, _ := io.ReadAll(req.Body)
		if string(data) != body {
			t.Fatal("Cohere normalization leaked to another provider")
		}
		return &http.Response{StatusCode: 200, Body: io.NopCloser(strings.NewReader(`{}`))}, nil
	})}
	req, _ := http.NewRequest("POST", "http://fixture.invalid/chat/completions", strings.NewReader(body))
	response, err := tr.RoundTrip(req)
	if err != nil {
		t.Fatal(err)
	}
	response.Body.Close()
}

func TestCohereResponsesPreservesOnlyExplicitSupportedEffort(t *testing.T) {
	for _, effort := range []string{"none", "high"} {
		original := []byte(`{"model":"model","input":"fixture","reasoning":{"effort":"` + effort + `"}}`)
		opts := executor.Options{OriginalRequest: original, SourceFormat: translator.FormatOpenAIResponse}
		if err := validateCohereInput(cohereAuth(), executor.Request{Payload: original}, opts); err != nil {
			t.Fatal(err)
		}
		requested, err := cohereRequestedEffort(executor.Request{Payload: original}, opts)
		if err != nil || requested != effort {
			t.Fatal("supported nested intent lost", requested, err)
		}
		calls := 0
		tr := &guardedTransport{auth: cohereAuth(), cohereValidated: true, cohereReasoningEffort: requested, next: fixtureTransport(func(req *http.Request) (*http.Response, error) {
			calls++
			body, _ := io.ReadAll(req.Body)
			if gjson.GetBytes(body, "reasoning_effort").String() != effort {
				t.Fatal("generic translator changed explicit supported effort")
			}
			return &http.Response{StatusCode: 200, Header: http.Header{}, Body: io.NopCloser(strings.NewReader(`{}`))}, nil
		})}
		// Reproduce the generic thinking layer's low output without borrowing
		// live provider behavior; only explicit validated none/high may restore it.
		req, _ := http.NewRequest("POST", "http://fixture.invalid/chat/completions", strings.NewReader(`{"model":"model","messages":[],"reasoning_effort":"low"}`))
		response, err := tr.RoundTrip(req)
		if err != nil || calls != 1 {
			t.Fatal("supported original effort was rejected after translation", err, calls)
		}
		response.Body.Close()
	}
	for _, original := range []string{
		`{"reasoning":{"effort":"low"}}`, `{"reasoning":{"effort":"medium"}}`, `{"reasoning":{"effort":null}}`,
		`{"reasoning":{"effort":"none","effort":"high"}}`, `{"reasoning_effort":"high","reasoning":{"effort":"none"}}`,
	} {
		if err := validateCohereInput(cohereAuth(), executor.Request{Payload: []byte(original)}, executor.Options{SourceFormat: translator.FormatOpenAIResponse}); err == nil {
			t.Fatal("unsupported or ambiguous nested effort was accepted")
		}
	}
}

func TestCohereFinalEffortTelemetryDoesNotReportTranslatorLow(t *testing.T) {
	i, ctx, a := dispatchFixture(t)
	if err := i.Before(ctx, a, executor.Request{Model: "model"}, executor.Options{}); err != nil {
		t.Fatal(err)
	}
	tr := &guardedTransport{integration: i, auth: cohereAuth(), cohereValidated: true, cohereReasoningEffort: "none", next: fixtureTransport(func(*http.Request) (*http.Response, error) {
		if err := i.Publish(ctx, usage.Record{AuthID: a.ID, RequestID: usage.ExecutionRequestIDFromContext(ctx), ReasoningEffort: "low"}); err != nil {
			t.Fatal(err)
		}
		return &http.Response{StatusCode: 200, Header: http.Header{}, Body: io.NopCloser(strings.NewReader(`{}`))}, nil
	})}
	req, _ := http.NewRequestWithContext(ctx, "POST", "http://fixture.invalid/chat/completions", strings.NewReader(`{"model":"model","messages":[],"reasoning_effort":"low"}`))
	response, err := tr.RoundTrip(req)
	if err != nil {
		t.Fatal(err)
	}
	response.Body.Close()
	if err := i.After(ctx, nil); err != nil {
		t.Fatal(err)
	}
	page, _ := i.Journal.Read(0, 10)
	if len(page.Events) != 3 || page.Events[1].Dimensions == nil || page.Events[1].Dimensions.EffectiveReasoningEffort != "none" {
		t.Fatal("final wire reasoning metadata was replaced by pre-normalization reporter", page)
	}
}
