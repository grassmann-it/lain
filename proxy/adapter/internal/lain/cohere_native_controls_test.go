package lain

import (
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"strings"
	"testing"

	"github.com/gin-gonic/gin"
	executor "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/executor"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/translator"
	"github.com/tidwall/gjson"
)

// Match the native Codex capture's control envelope, including the absence of
// max_output_tokens. The real pinned SDK translator and final transport guard
// must retain both calls and pair their full-history outputs without a provider.
func TestCohereNativeResponsesDefaultsPreserveParallelToolHistory(t *testing.T) {
	for _, budget := range []string{"", `,"max_output_tokens":64`} {
		t.Run("budget="+budget, func(t *testing.T) {
			i, ctx, _ := dispatchFixture(t)
			raw := &cohereTranslatingExecutor{reply: `{"id":"chatcmpl_parallel","choices":[{"index":0,"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call_left","type":"function","function":{"name":"echo","arguments":"{\"value\":\"left\"}"}},{"id":"call_right","type":"function","function":{"name":"echo","arguments":"{\"value\":\"right\"}"}}]},"finish_reason":"tool_calls"}]}`}
			bound := i.ExecutorDecorator(nil)(raw)
			envelope := `"model":"model","tool_choice":"auto","parallel_tool_calls":true,"include":["reasoning.encrypted_content"],"client_metadata":{"fixture":"native"},"prompt_cache_key":"fixture-cache","reasoning":{"effort":"none"},"store":false,"stream":true,"tools":[{"type":"function","name":"echo","parameters":{"type":"object","properties":{"value":{"type":"string"}},"required":["value"]}}]` + budget
			original := []byte(`{` + envelope + `,"instructions":"fixture instruction","input":[{"role":"user","content":"echo left and right"}]}`)
			before := string(original)
			response, err := bound.Execute(ctx, cohereAuth(), executor.Request{Model: "model", Payload: original}, executor.Options{OriginalRequest: original, SourceFormat: translator.FormatOpenAIResponse, Stream: true})
			if err != nil || raw.executions != 1 || len(raw.sentBodies) != 1 {
				t.Fatal("native defaults failed before final dispatch", err, raw.executions, len(raw.sentBodies))
			}
			if string(original) != before {
				t.Fatal("caller payload mutated")
			}
			wire := raw.sentBodies[0]
			// The owned forwarder planned for qualification will send this Chat
			// body through the existing runtime's unchanged strict subset guard.
			if err := validateCohereInput(cohereAuth(), executor.Request{Payload: wire}, executor.Options{SourceFormat: translator.FormatOpenAI}); err != nil {
				t.Fatal("candidate wire cannot pass the existing Chat subset", err, string(wire))
			}
			for _, field := range []string{"tool_choice", "parallel_tool_calls", "store", "max_completion_tokens"} {
				if gjson.GetBytes(wire, field).Exists() {
					t.Fatal("unsupported control reached Cohere", field, string(wire))
				}
			}
			if gjson.GetBytes(wire, "messages.0.role").String() != "system" || gjson.GetBytes(wire, "messages.0.content").String() != "fixture instruction" || gjson.GetBytes(wire, "tools.0.function.name").String() != "echo" || gjson.GetBytes(wire, "reasoning_effort").String() != "none" || !gjson.GetBytes(wire, "stream").Bool() {
				t.Fatal("native request intent changed", string(wire))
			}
			if (budget == "" && gjson.GetBytes(wire, "max_tokens").Exists()) || (budget != "" && gjson.GetBytes(wire, "max_tokens").Int() != 64) {
				t.Fatal("output budget was invented or lost", string(wire))
			}
			calls := gjson.GetBytes(response.Payload, "output").Array()
			if len(calls) != 2 {
				t.Fatal("parallel response calls were lost", string(response.Payload))
			}
			for index, side := range []string{"left", "right"} {
				call := calls[index]
				if call.Get("type").String() != "function_call" || call.Get("call_id").String() != "call_"+side || call.Get("name").String() != "echo" || call.Get("arguments").String() != `{"value":"`+side+`"}` {
					t.Fatal("parallel call identity/arguments changed", call.Raw)
				}
			}
			continuation := []byte(`{` + envelope + `,"input":[{"role":"user","content":"echo left and right"},` + calls[0].Raw + `,` + calls[1].Raw + `,{"type":"function_call_output","call_id":"call_right","output":"right result"},{"type":"function_call_output","call_id":"call_left","output":"left result"}]}`)
			raw.reply = `{"id":"chatcmpl_done","choices":[{"index":0,"message":{"role":"assistant","content":"done"},"finish_reason":"stop"}]}`
			response, err = bound.Execute(ctx, cohereAuth(), executor.Request{Model: "model", Payload: continuation}, executor.Options{OriginalRequest: continuation, SourceFormat: translator.FormatOpenAIResponse, Stream: true})
			if err != nil || len(raw.sentBodies) != 2 || gjson.GetBytes(response.Payload, "output.0.content.0.text").String() != "done" {
				t.Fatal("parallel full-history continuation failed", err, string(response.Payload))
			}
			wire = raw.sentBodies[1]
			if gjson.GetBytes(wire, "messages.1.role").String() != "assistant" || len(gjson.GetBytes(wire, "messages.1.tool_calls").Array()) != 2 {
				t.Fatal("parallel calls were split or discarded", string(wire))
			}
			for index, side := range []string{"left", "right"} {
				call := gjson.GetBytes(wire, "messages.1.tool_calls").Array()[index]
				if call.Get("id").String() != "call_"+side || call.Get("function.arguments").String() != `{"value":"`+side+`"}` {
					t.Fatal("continuation call changed", call.Raw)
				}
			}
			results := map[string]string{}
			for _, message := range gjson.GetBytes(wire, "messages").Array() {
				if message.Get("role").String() == "tool" {
					results[message.Get("tool_call_id").String()] = message.Get("content").String()
				}
			}
			if len(results) != 2 || results["call_left"] != "left result" || results["call_right"] != "right result" {
				t.Fatal("out-of-order parallel results mispaired", string(wire))
			}
		})
	}
}

func TestCohereNativeToolIntentRejectsUnsupportedValuesBeforeTranslation(t *testing.T) {
	for _, control := range []string{
		`"tool_choice":"none"`, `"tool_choice":"required"`, `"tool_choice":{"type":"function","name":"echo"}`,
		`"tool_choice":{"type":"function","function":{"name":"echo"}}`, `"tool_choice":{"type":"allowed_tools","mode":"auto","tools":[]}`,
		`"tool_choice":null`, `"tool_choice":true`, `"tool_choice":"AUTO"`, `"tool_choice":"auto","tool_choice":"auto"`,
		`"parallel_tool_calls":false`, `"parallel_tool_calls":null`, `"parallel_tool_calls":"true"`, `"parallel_tool_calls":1`,
		`"parallel_tool_calls":true,"parallel_tool_calls":true`, `"parallel_tool_calls":true,"parallel_tool_calls":false`,
	} {
		t.Run(control, func(t *testing.T) {
			for _, source := range []string{"original", "handler", "preserved"} {
				i, ctx, _ := dispatchFixture(t)
				raw := &fixtureExecutor{}
				bad := []byte(`{"model":"model",` + control + `}`)
				good := []byte(`{"model":"model","store":false,"tool_choice":"auto","parallel_tool_calls":true}`)
				payload, original := good, bad
				if source == "handler" {
					payload, original = bad, good
				}
				if source == "preserved" {
					ctx.Value("gin").(*gin.Context).Set("lain.original_request", bad)
					original = good
				}
				_, err := i.ExecutorDecorator(nil)(raw).Execute(ctx, cohereAuth(), executor.Request{Model: "model", Payload: payload}, executor.Options{OriginalRequest: original, SourceFormat: translator.FormatOpenAIResponse})
				var status interface{ StatusCode() int }
				if !errors.As(err, &status) || status.StatusCode() != 400 || raw.executions != 0 {
					t.Fatal("explicit tool intent disappeared before rejection", source, err, raw.executions)
				}
				page, _ := i.Journal.Read(0, 10)
				if len(page.Events) != 2 || page.Events[1].CompletionState == nil || *page.Events[1].CompletionState != "failed" || i.Journal.Health() != nil {
					t.Fatal("tool control rejection lost durable attribution", page)
				}
			}
		})
	}
}

func TestCohereFinalNativeToolDefaultsNormalizeWithReplay(t *testing.T) {
	for _, control := range []string{`"tool_choice":"auto"`, `"parallel_tool_calls":true`, `"tool_choice":"auto","parallel_tool_calls":true`} {
		calls := 0
		tr := &guardedTransport{auth: cohereAuth(), cohereValidated: true, next: fixtureTransport(func(req *http.Request) (*http.Response, error) {
			calls++
			data, _ := io.ReadAll(req.Body)
			var fields map[string]json.RawMessage
			if json.Unmarshal(data, &fields) != nil || len(fields) != 3 || gjson.GetBytes(data, "model").String() != "model" || gjson.GetBytes(data, "tools.0.function.name").String() != "echo" {
				t.Fatal("only documented defaults may normalize", string(data))
			}
			replay, err := req.GetBody()
			if err != nil {
				t.Fatal(err)
			}
			copy, _ := io.ReadAll(replay)
			replay.Close()
			if string(copy) != string(data) || req.ContentLength != int64(len(data)) {
				t.Fatal("normalized replay/length changed")
			}
			return &http.Response{StatusCode: 200, Header: http.Header{}, Body: io.NopCloser(strings.NewReader(`{}`))}, nil
		})}
		req, _ := http.NewRequest("POST", "https://fixture.invalid/chat/completions", strings.NewReader(`{"model":"model","messages":[],"tools":[{"type":"function","function":{"name":"echo","parameters":{"type":"object"}}}],`+control+`}`))
		response, err := tr.RoundTrip(req)
		if err != nil || calls != 1 {
			t.Fatal("default-equivalent controls failed final validation", control, err, calls)
		}
		response.Body.Close()
	}
}

func TestCohereFinalNativeToolControlsRejectOverridesAndDuplicates(t *testing.T) {
	for _, control := range []string{
		`"tool_choice":"none"`, `"tool_choice":"required"`, `"tool_choice":{"type":"function","function":{"name":"echo"}}`,
		`"tool_choice":"auto","tool_choice":"required"`, `"tool_choice":"auto","tool_choice":"auto"`, `"tool_choice":null`,
		`"parallel_tool_calls":false`, `"parallel_tool_calls":null`, `"parallel_tool_calls":"true"`,
		`"parallel_tool_calls":false,"parallel_tool_calls":true`, `"parallel_tool_calls":true,"parallel_tool_calls":true`,
		`"max_tokens":64,"max_tokens":32`, `"stream_options":{"include_usage":false},"stream_options":{"include_usage":true}`,
	} {
		calls := 0
		tr := &guardedTransport{auth: cohereAuth(), cohereValidated: true, next: fixtureTransport(func(*http.Request) (*http.Response, error) {
			calls++
			return nil, errors.New("must not dispatch")
		})}
		req, _ := http.NewRequest("POST", "https://fixture.invalid/chat/completions", strings.NewReader(`{"model":"model","messages":[],`+control+`}`))
		if _, err := tr.RoundTrip(req); err == nil || calls != 0 {
			t.Fatal("final tool intent changed or duplicates collapsed", control, err, calls)
		}
	}
}

func TestCohereNativeDefaultsDoNotBroadenChatSubsetOrOtherProviders(t *testing.T) {
	for _, control := range []string{`"tool_choice":"auto"`, `"parallel_tool_calls":true`} {
		body := []byte(`{"model":"model","messages":[],` + control + `}`)
		if err := validateCohereInput(cohereAuth(), executor.Request{Payload: body}, executor.Options{SourceFormat: translator.FormatOpenAI}); err == nil {
			t.Fatal("native Chat subset broadened", control)
		}
	}
	body := `{"model":"model","messages":[],"tool_choice":"auto","parallel_tool_calls":true}`
	a := cohereAuth()
	a.Attributes["compat_name"] = "other-openai-chat"
	tr := &guardedTransport{auth: a, next: fixtureTransport(func(req *http.Request) (*http.Response, error) {
		data, _ := io.ReadAll(req.Body)
		if string(data) != body {
			t.Fatal("Cohere default mapping leaked to another provider")
		}
		return &http.Response{StatusCode: 200, Header: http.Header{}, Body: io.NopCloser(strings.NewReader(`{}`))}, nil
	})}
	req, _ := http.NewRequest("POST", "https://fixture.invalid/chat/completions", strings.NewReader(body))
	response, err := tr.RoundTrip(req)
	if err != nil {
		t.Fatal(err)
	}
	response.Body.Close()
}
