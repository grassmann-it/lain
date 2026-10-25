package lain

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"strings"
	"testing"

	"github.com/gin-gonic/gin"
	auth "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/auth"
	executor "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/executor"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/httptransport"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/translator"
	_ "github.com/router-for-me/CLIProxyAPI/v8/sdk/translator/builtin"
	"github.com/tidwall/gjson"
)

// Exercise the pinned SDK translator through the real adapter decorator and final
// transport guard. The synthetic transport never leaves the process.
type cohereTranslatingExecutor struct {
	fixtureExecutor
	sentBodies [][]byte
	reply      string
}

func (e *cohereTranslatingExecutor) Execute(ctx context.Context, _ *auth.Auth, req executor.Request, opts executor.Options) (executor.Response, error) {
	e.executions++
	wire := translator.TranslateRequest(opts.SourceFormat, translator.FormatOpenAI, req.Model, req.Payload, opts.Stream)
	client := httptransport.Decorate(ctx, &http.Client{Transport: fixtureTransport(func(r *http.Request) (*http.Response, error) {
		body, err := io.ReadAll(r.Body)
		if err != nil {
			return nil, err
		}
		e.sentBodies = append(e.sentBodies, body)
		return &http.Response{StatusCode: 200, Header: http.Header{}, Body: io.NopCloser(strings.NewReader(e.reply))}, nil
	})})
	r, err := http.NewRequestWithContext(ctx, "POST", "https://fixture.invalid/chat/completions", strings.NewReader(string(wire)))
	if err != nil {
		return executor.Response{}, err
	}
	response, err := client.Do(r)
	if err != nil {
		return executor.Response{}, err
	}
	defer response.Body.Close()
	body, err := io.ReadAll(response.Body)
	var state any
	return executor.Response{Payload: translator.TranslateNonStream(ctx, translator.FormatOpenAI, opts.SourceFormat, req.Model, req.Payload, wire, body, &state)}, err
}

func TestCohereResponsesStoreFalseUsesStatelessChatAndCompleteFunctionHistory(t *testing.T) {
	i, ctx, _ := dispatchFixture(t)
	raw := &cohereTranslatingExecutor{reply: `{"id":"chatcmpl_fixture","choices":[{"index":0,"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call_1","type":"function","function":{"name":"echo","arguments":"{\"value\":\"safe\"}"}}]},"finish_reason":"tool_calls"}]}`}
	bound := i.ExecutorDecorator(nil)(raw)
	original := []byte(`{"model":"model","store":false,"instructions":"fixture instruction","max_output_tokens":64,"reasoning":{"effort":"none"},"text":{"format":{"type":"json_object"}},"input":[{"role":"user","content":"fixture"}],"tools":[{"type":"function","name":"echo","parameters":{"type":"object","properties":{"value":{"type":"string"}}}}]}`)
	response, err := bound.Execute(ctx, cohereAuth(), executor.Request{Model: "model", Payload: original}, executor.Options{OriginalRequest: original, SourceFormat: translator.FormatOpenAIResponse})
	if err != nil || raw.executions != 1 || len(raw.sentBodies) != 1 {
		t.Fatal("stateless request failed", err, raw.executions, len(raw.sentBodies))
	}
	wire := raw.sentBodies[0]
	if gjson.GetBytes(wire, "store").Exists() || gjson.GetBytes(wire, "input").Exists() || gjson.GetBytes(wire, "messages.0.role").String() != "system" || gjson.GetBytes(wire, "messages.0.content").String() != "fixture instruction" || gjson.GetBytes(wire, "messages.1.content").String() != "fixture" || gjson.GetBytes(wire, "max_tokens").Int() != 64 || gjson.GetBytes(wire, "reasoning_effort").String() != "none" || gjson.GetBytes(wire, "response_format.type").String() != "json_object" || gjson.GetBytes(wire, "tools.0.function.name").String() != "echo" {
		t.Fatal("stateless mapping lost explicit supported intent", string(wire))
	}
	call := gjson.GetBytes(response.Payload, "output.0")
	if call.Get("type").String() != "function_call" || call.Get("call_id").String() != "call_1" || call.Get("name").String() != "echo" || call.Get("arguments").String() != `{"value":"safe"}` {
		t.Fatal("function response was not preserved", string(response.Payload))
	}
	// Full client-owned history carries the original call as well as its output;
	// there is no response-ID lookup or invented assistant/tool pairing.
	continuation := []byte(`{"model":"model","store":false,"input":[{"role":"user","content":"fixture"},` + call.Raw + `,{"type":"function_call_output","call_id":"call_1","output":"result-safe"}],"tools":[{"type":"function","name":"echo","parameters":{"type":"object"}}]}`)
	raw.reply = `{"id":"chatcmpl_done","choices":[{"index":0,"message":{"role":"assistant","content":"done"},"finish_reason":"stop"}]}`
	_, err = bound.Execute(ctx, cohereAuth(), executor.Request{Model: "model", Payload: continuation}, executor.Options{OriginalRequest: continuation, SourceFormat: translator.FormatOpenAIResponse})
	if err != nil || len(raw.sentBodies) != 2 {
		t.Fatal("full-history continuation failed", err)
	}
	wire = raw.sentBodies[1]
	if gjson.GetBytes(wire, "store").Exists() || gjson.GetBytes(wire, "previous_response_id").Exists() || gjson.GetBytes(wire, "messages.1.role").String() != "assistant" || gjson.GetBytes(wire, "messages.1.tool_calls.0.id").String() != "call_1" || gjson.GetBytes(wire, "messages.1.tool_calls.0.function.arguments").String() != `{"value":"safe"}` || gjson.GetBytes(wire, "messages.2.role").String() != "tool" || gjson.GetBytes(wire, "messages.2.tool_call_id").String() != "call_1" || gjson.GetBytes(wire, "messages.2.content").String() != "result-safe" {
		t.Fatal("function continuation was changed", string(wire))
	}
}

func TestCohereResponsesRejectsStoredStateBeforeTranslation(t *testing.T) {
	for _, control := range []string{`"store":true`, `"store":null`, `"store":"false"`, `"store":0`, `"store":false,"store":false`, `"previous_response_id":"resp_prior"`, `"previous_response_id":null`, `"conversation":"conv_prior"`, `"input":[{"type":"item_reference","id":"item_prior"}]`} {
		t.Run(control, func(t *testing.T) {
			i, ctx, _ := dispatchFixture(t)
			raw := &fixtureExecutor{}
			original := []byte(`{"model":"model",` + control + `}`)
			_, err := i.ExecutorDecorator(nil)(raw).Execute(ctx, cohereAuth(), executor.Request{Model: "model", Payload: original}, executor.Options{OriginalRequest: original, SourceFormat: translator.FormatOpenAIResponse})
			var status interface{ StatusCode() int }
			if !errors.As(err, &status) || status.StatusCode() != 400 || raw.executions != 0 {
				t.Fatal("stored state reached translator/provider", err, raw.executions)
			}
			page, _ := i.Journal.Read(0, 10)
			if len(page.Events) != 2 || page.Events[1].CompletionState == nil || *page.Events[1].CompletionState != "failed" {
				t.Fatal("state rejection was not durably attributed", page)
			}
		})
	}
	// The narrow Responses mapping never broadens the native Chat field subset.
	for _, control := range []string{`"store":false`, `"store":true`, `"extra":false`} {
		body := []byte(`{"model":"model","messages":[],` + control + `}`)
		if validateCohereInput(cohereAuth(), executor.Request{Payload: body}, executor.Options{SourceFormat: translator.FormatOpenAI}) == nil {
			t.Fatal("Chat subset was broadened", control)
		}
	}
}

func TestCohereNativeToolsCannotDisappearBeforeDispatch(t *testing.T) {
	for _, declaration := range []string{`{"type":"web_search"}`, `{"type":"web_search_preview"}`, `{"type":"web_search_preview_2025_03_11"}`, `{"type":"file_search"}`, `{"type":"computer_use_preview"}`, `{"type":"future_native_tool"}`, `{"type":"shell","environment":{"type":"container"}}`, `{"type":"custom","name":"constrained","format":{"type":"grammar","definition":"fixture"}}`} {
		for _, envelope := range []string{`{"tools":[` + declaration + `]}`, `{"tools":[{"type":"namespace","name":"ns","tools":[` + declaration + `]}]}`, `{"input":[{"type":"additional_tools","tools":[` + declaration + `]}]}`} {
			i, ctx, _ := dispatchFixture(t)
			raw := &fixtureExecutor{}
			// An earlier handler rewrite cannot hide native intent from validation.
			ctx.Value("gin").(*gin.Context).Set("lain.original_request", []byte(envelope))
			_, err := i.ExecutorDecorator(nil)(raw).Execute(ctx, cohereAuth(), executor.Request{Model: "model", Payload: []byte(`{"tools":[]}`)}, executor.Options{SourceFormat: translator.FormatOpenAIResponse})
			if compatibilityErrorCategory(err) != "unsupported_tool" || raw.executions != 0 {
				t.Fatal("native tool disappeared before rejection", envelope, err)
			}
		}
	}
	// Tool names/schema strings are data, not declarations of native capabilities.
	for _, body := range []string{`{"tools":[{"type":"function","name":"web_search","parameters":{"type":"object","properties":{"type":{"const":"web_search"}}}}]}`, `{"tools":[{"type":"namespace","name":"ns","tools":[{"type":"function","name":"echo"}]}]}`, `{"tools":[{"type":"custom","name":"apply_patch"}]}`} {
		if err := validateCohereInput(cohereAuth(), executor.Request{Payload: []byte(body)}, executor.Options{SourceFormat: translator.FormatOpenAIResponse}); err != nil {
			t.Fatal("supported translator declaration rejected", body, err)
		}
	}
	for _, final := range []string{`{"model":"model","messages":[],"store":false}`, `{"model":"model","messages":[],"tools":[{"type":"web_search"}]}`} {
		calls := 0
		tr := &guardedTransport{auth: cohereAuth(), cohereValidated: true, next: fixtureTransport(func(*http.Request) (*http.Response, error) { calls++; return nil, nil })}
		req, _ := http.NewRequest("POST", "https://fixture.invalid/chat/completions", strings.NewReader(final))
		if _, err := tr.RoundTrip(req); err == nil || calls != 0 {
			t.Fatal("unsupported final override reached provider", final)
		}
	}
	for _, changed := range []string{`{"store":true}`, `{"previous_response_id":"resp_prior"}`, `{"tools":[{"type":"web_search"}]}`} {
		i, ctx, _ := dispatchFixture(t)
		raw := &fixtureExecutor{}
		_, err := i.ExecutorDecorator(nil)(raw).Execute(ctx, cohereAuth(), executor.Request{Model: "model", Payload: []byte(changed)}, executor.Options{OriginalRequest: []byte(`{"store":false,"input":"fixture"}`), SourceFormat: translator.FormatOpenAIResponse})
		if err == nil || raw.executions != 0 {
			t.Fatal("handler payload introduced unsupported intent after original validation", changed, err)
		}
	}
}

func TestCohereStoreFalseValidationDoesNotMutateInput(t *testing.T) {
	body := []byte(`{"model":"model","store":false,"input":"fixture"}`)
	before := string(body)
	if err := validateCohereInput(cohereAuth(), executor.Request{Payload: body}, executor.Options{SourceFormat: translator.FormatOpenAIResponse}); err != nil || string(body) != before || !json.Valid(body) {
		t.Fatal("validation must preserve caller payload", err)
	}
}
