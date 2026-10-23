package executor

import (
	"context"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/router-for-me/CLIProxyAPI/v7/internal/config"
	"github.com/router-for-me/CLIProxyAPI/v7/internal/util"
	cliproxyauth "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/auth"
	cliproxyexecutor "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/executor"
	sdktranslator "github.com/router-for-me/CLIProxyAPI/v7/sdk/translator"
	"github.com/tidwall/gjson"
)

// Tests exercise real executors with loopback fake providers; no vendor access is claimed.
func TestLainNativeResponsesTransport(t *testing.T) {
	for _, stream := range []bool{false, true} {
		t.Run(map[bool]string{false: "json", true: "sse"}[stream], func(t *testing.T) {
			var captured []byte
			server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				captured, _ = io.ReadAll(r.Body)
				if r.URL.Path != "/inference/go/openai/v1/responses" || r.Header.Get("Authorization") != "Bearer fixture-key" || r.Header.Get("X-Opencode-Session") != "fixture-session" || r.Header.Get("User-Agent") != "lain/1.0" {
					t.Errorf("incorrect native Responses path/headers")
				}
				if stream {
					w.Header().Set("Content-Type", "text/event-stream")
					io.WriteString(w, "event: response.output_text.delta\ndata: {\"type\":\"response.output_text.delta\",\"delta\":\"fixture\",\"output_index\":0,\"content_index\":0}\n\n")
					io.WriteString(w, "event: response.completed\ndata: {\"type\":\"response.completed\",\"response\":{\"id\":\"resp_fixture\",\"status\":\"completed\",\"output\":[],\"usage\":{\"input_tokens\":2,\"output_tokens\":1,\"total_tokens\":3}}}\n\n")
				} else {
					w.Header().Set("Content-Type", "application/json")
					io.WriteString(w, `{"id":"resp_fixture","object":"response","status":"completed","output":[],"usage":{"input_tokens":2,"output_tokens":1,"total_tokens":3}}`)
				}
			}))
			defer server.Close()
			auth := &cliproxyauth.Auth{Provider: "opencode-go-openai-responses", Attributes: map[string]string{"base_url": server.URL + "/inference/go/openai/v1", "api_key": "fixture-key", "upstream_protocol": "openai-responses", "header:User-Agent": "lain/1.0", "header:x-opencode-session": "$CPA-SESSION-ID"}}
			payload := []byte(`{"model":"gpt-6-luna","previous_response_id":"resp_prior","input":[{"type":"function_call_output","call_id":"call_prior","output":"fixture tool result"}],"tools":[{"type":"function","name":"inspect","parameters":{"type":"object"}}]}`)
			request := cliproxyexecutor.Request{Model: "gpt-6-luna", Payload: payload}
			options := cliproxyexecutor.Options{SourceFormat: sdktranslator.FormatOpenAIResponse, ResponseFormat: sdktranslator.FormatOpenAIResponse, Stream: stream}
			ctx := util.WithSessionID(context.Background(), "fixture-session")
			executor := NewOpenAICompatExecutor(auth.Provider, &config.Config{})
			if stream {
				result, err := executor.ExecuteStream(ctx, auth, request, options)
				if err != nil {
					t.Fatal(err)
				}
				var output strings.Builder
				for chunk := range result.Chunks {
					if chunk.Err != nil {
						t.Fatal(chunk.Err)
					}
					output.Write(chunk.Payload)
				}
				if !strings.Contains(output.String(), "response.completed") {
					t.Fatal("missing native terminal event")
				}
			} else {
				result, err := executor.Execute(ctx, auth, request, options)
				if err != nil {
					t.Fatal(err)
				}
				if gjson.GetBytes(result.Payload, "id").String() != "resp_fixture" {
					t.Fatal("native response lost")
				}
			}
			for key, want := range map[string]string{"previous_response_id": "resp_prior", "input.0.call_id": "call_prior", "tools.0.name": "inspect"} {
				if gjson.GetBytes(captured, key).String() != want {
					t.Errorf("native continuation field %s changed", key)
				}
			}
			if gjson.GetBytes(captured, "stream_options").Exists() {
				t.Fatal("Chat-only stream_options sent to Responses")
			}
		})
	}
}

func TestLainGatewayGeminiBearerTransport(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path != "/inference/go/google/v1beta/models/gemini-fixture:generateContent" {
			t.Errorf("wrong native Gemini path: %s", r.URL.Path)
		}
		if r.Header.Get("Authorization") != "Bearer fixture-key" || r.Header.Get("x-goog-api-key") != "" || r.Header.Get("x-opencode-session") != "fixture-session" {
			t.Error("incorrect gateway Gemini auth/session")
		}
		w.Header().Set("Content-Type", "application/json")
		io.WriteString(w, `{"candidates":[{"content":{"role":"model","parts":[{"text":"fixture"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":1,"candidatesTokenCount":1,"totalTokenCount":2}}`)
	}))
	defer server.Close()
	auth := &cliproxyauth.Auth{Provider: "gemini", Attributes: map[string]string{"base_url": server.URL + "/inference/go/google", "api_key": "fixture-key", "auth_header": "bearer", "header:x-opencode-session": "$CPA-SESSION-ID"}}
	result, err := NewGeminiExecutor(&config.Config{}).Execute(util.WithSessionID(context.Background(), "fixture-session"), auth, cliproxyexecutor.Request{Model: "gemini-fixture", Payload: []byte(`{"contents":[{"role":"user","parts":[{"text":"fixture"}]}]}`)}, cliproxyexecutor.Options{SourceFormat: sdktranslator.FormatGemini, ResponseFormat: sdktranslator.FormatGemini})
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(string(result.Payload), "fixture") {
		t.Fatal("missing Gemini content")
	}
}

func TestLainGatewayAnthropicTransport(t *testing.T) {
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path != "/inference/go/anthropic/v1/messages" {
			t.Errorf("wrong native Anthropic path: %s", r.URL.Path)
		}
		if r.Header.Get("Authorization") != "Bearer fixture-key" || r.Header.Get("x-api-key") != "" || r.Header.Get("x-opencode-session") != "fixture-session" {
			t.Error("incorrect gateway Anthropic auth/session")
		}
		w.Header().Set("Content-Type", "application/json")
		io.WriteString(w, `{"id":"msg_fixture","type":"message","role":"assistant","model":"claude-fixture","content":[{"type":"text","text":"fixture"}],"stop_reason":"end_turn","usage":{"input_tokens":1,"output_tokens":1}}`)
	}))
	defer server.Close()
	auth := &cliproxyauth.Auth{Provider: "claude", Attributes: map[string]string{"base_url": server.URL + "/inference/go/anthropic", "api_key": "fixture-key", "header:x-opencode-session": "$CPA-SESSION-ID", "header:User-Agent": "lain/1.0"}}
	result, err := NewClaudeExecutor(&config.Config{}).Execute(util.WithSessionID(context.Background(), "fixture-session"), auth, cliproxyexecutor.Request{Model: "claude-fixture", Payload: []byte(`{"model":"claude-fixture","max_tokens":16,"messages":[{"role":"user","content":"fixture"}]}`)}, cliproxyexecutor.Options{SourceFormat: sdktranslator.FormatClaude, ResponseFormat: sdktranslator.FormatClaude})
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(string(result.Payload), "fixture") {
		t.Fatal("missing Anthropic content")
	}
}
