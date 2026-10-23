package executor

import (
	"context"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/router-for-me/CLIProxyAPI/v7/internal/config"
	cliproxyauth "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/auth"
	cliproxyexecutor "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/executor"
	sdktranslator "github.com/router-for-me/CLIProxyAPI/v7/sdk/translator"
	"github.com/tidwall/gjson"
)

// This is a synthetic protocol fixture, not a NVIDIA/StepFun access test.
// It exercises the existing compatibility executor against documented stream
// shapes and keeps the separate Step Plan path observable.
func TestOpenAICompatExecutorNvidiaStepDocumentedStreamFixture(t *testing.T) {
	for _, route := range []struct{ provider, model, path string }{
		{"nvidia-build", "stepfun-ai/step-5-preview", "/v1"},
		{"stepfun-payg", "step-5-preview", "/v1"},
		{"stepfun-plan", "step-5-preview", "/step_plan/v1"},
	} {
		t.Run(route.provider, func(t *testing.T) {
			var body []byte
			var path, authorization string
			server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				body, _ = io.ReadAll(r.Body)
				path, authorization = r.URL.Path, r.Header.Get("Authorization")
				w.Header().Set("Content-Type", "text/event-stream")
				_, _ = io.WriteString(w, "data: "+`{"id":"fixture","object":"chat.completion.chunk","choices":[{"index":0,"delta":{"role":"assistant","reasoning_content":"fixture reasoning"},"finish_reason":null}]}`+"\n\n")
				_, _ = io.WriteString(w, "data: "+`{"id":"fixture","object":"chat.completion.chunk","choices":[{"index":0,"delta":{"content":"checking","tool_calls":[{"index":0,"id":"call_next","type":"function","function":{"name":"inspect","arguments":""}}]},"finish_reason":null}]}`+"\n\n")
				_, _ = io.WriteString(w, "data: "+`{"id":"fixture","object":"chat.completion.chunk","choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"path\":\"fixture\"}"}}]},"finish_reason":"tool_calls"}]}`+"\n\n")
				_, _ = io.WriteString(w, "data: [DONE]\n\n")
			}))
			defer server.Close()

			exec := NewOpenAICompatExecutor(route.provider, &config.Config{})
			auth := &cliproxyauth.Auth{Provider: route.provider, Attributes: map[string]string{
				"base_url": server.URL + route.path, "api_key": "synthetic-fixture-key",
			}}
			payload := []byte(`{"model":"` + route.model + `","stream":true,"max_tokens":4096,"messages":[{"role":"assistant","content":null,"reasoning_content":"prior reasoning","tool_calls":[{"id":"call_prior","type":"function","function":{"name":"inspect","arguments":"{}"}}]},{"role":"tool","tool_call_id":"call_prior","content":"fixture result"}],"tools":[{"type":"function","function":{"name":"inspect","parameters":{"type":"object"}}}],"tool_choice":"auto"}`)
			result, err := exec.ExecuteStream(context.Background(), auth, cliproxyexecutor.Request{Model: route.model, Payload: payload}, cliproxyexecutor.Options{
				SourceFormat: sdktranslator.FormatOpenAI, ResponseFormat: sdktranslator.FormatOpenAI, Stream: true,
			})
			if err != nil {
				t.Fatal(err)
			}
			var stream strings.Builder
			for chunk := range result.Chunks {
				if chunk.Err != nil {
					t.Fatal(chunk.Err)
				}
				stream.Write(chunk.Payload)
			}
			if path != route.path+"/chat/completions" || authorization != "Bearer synthetic-fixture-key" {
				t.Fatalf("wrong fixed protocol path or authentication: path=%q", path)
			}
			for field, want := range map[string]string{"model": route.model, "messages.0.reasoning_content": "prior reasoning", "messages.0.tool_calls.0.id": "call_prior", "messages.1.tool_call_id": "call_prior", "tools.0.function.name": "inspect", "tool_choice": "auto"} {
				if got := gjson.GetBytes(body, field).String(); got != want {
					t.Fatalf("%s = %q, want %q", field, got, want)
				}
			}
			for _, want := range []string{"fixture reasoning", "checking", "call_next", "inspect", `\"path\":\"fixture\"`, `"finish_reason":"tool_calls"`} {
				if !strings.Contains(stream.String(), want) {
					t.Fatalf("documented stream field missing: %q", want)
				}
			}
			if strings.Contains(stream.String(), "synthetic-fixture-key") {
				t.Fatal("credential exposed in protocol output")
			}
		})
	}
}
