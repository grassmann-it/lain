package freeonly

import (
	"context"
	"errors"
	"io"
	"net/http"
	"strings"
	"testing"
)

type fakeTransport func(*http.Request) (*http.Response, error)

func (f fakeTransport) RoundTrip(req *http.Request) (*http.Response, error) { return f(req) }

const pinnedModel = "qwen/qwen3-coder:free"
const proof = `{"data":{"limit":0,"limit_remaining":0,"include_byok_in_limit":true,"is_management_key":false}}`

func outgoing(t *testing.T, body string) *http.Request {
	t.Helper()
	req, err := http.NewRequest(http.MethodPost, "https://openrouter.ai/api/v1/chat/completions", strings.NewReader(body))
	if err != nil {
		t.Fatal(err)
	}
	req.Header.Set("Authorization", "Bearer mock-private-key")
	return req
}

func TestFreshZeroCapAndFinalPayload(t *testing.T) {
	checks := 0
	client := &http.Client{Transport: fakeTransport(func(req *http.Request) (*http.Response, error) {
		checks++
		if req.Method != http.MethodGet || req.URL.String() != openRouterKeyURL || req.Header.Get("Authorization") != "Bearer mock-private-key" || req.Header.Get("Cache-Control") != "no-cache" {
			t.Fatal("proof did not use fixed read-only endpoint and same credential")
		}
		return &http.Response{StatusCode: 200, Header: http.Header{}, Body: io.NopCloser(strings.NewReader(proof))}, nil
	})}
	for count := 1; count <= 2; count++ {
		req := outgoing(t, `{"model":"qwen/qwen3-coder:free","messages":[{"role":"user","content":"hello"}],"stream":true,"tools":[{"type":"function","function":{"name":"read_file","parameters":{"type":"object"}}}]}`)
		if err := PrepareOpenRouterRequest(context.Background(), client, req, pinnedModel); err != nil {
			t.Fatal(err)
		}
		if checks != count {
			t.Fatal("key proof was cached")
		}
		payload, _ := io.ReadAll(req.Body)
		body, err := object(payload)
		if err != nil {
			t.Fatal(err)
		}
		provider := body["provider"].(map[string]any)
		if body["model"] != pinnedModel || provider["allow_fallbacks"] != false || provider["require_parameters"] != true {
			t.Fatal("routing pin absent")
		}
		prices := provider["max_price"].(map[string]any)
		for _, field := range []string{"prompt", "completion", "request", "image"} {
			if !zero(prices[field]) {
				t.Fatal("zero price category absent")
			}
		}
		if len(body["plugins"].([]any)) != 0 || req.ContentLength != int64(len(payload)) || req.Header.Get("Content-Type") != "application/json" {
			t.Fatal("body rewrite inconsistent")
		}
		if client.CheckRedirect(req, nil) != http.ErrUseLastResponse {
			t.Fatal("redirects not disabled for inference")
		}
	}
}

func TestUnsafeInputsNeverReachProofEndpoint(t *testing.T) {
	client := &http.Client{Transport: fakeTransport(func(*http.Request) (*http.Response, error) { t.Fatal("unsafe input reached network"); return nil, nil })}
	for _, payload := range []string{
		`{"model":"paid/model","messages":[]}`,
		`{"model":"qwen/qwen3-coder:free","models":["paid/model"],"messages":[]}`,
		`{"model":"qwen/qwen3-coder:free","route":"fallback","messages":[]}`,
		`{"model":"qwen/qwen3-coder:free","extra_body":{"model":"paid/model"},"messages":[]}`,
		`{"model":"qwen/qwen3-coder:free","provider":{"allow_fallbacks":true},"messages":[]}`,
		`{"model":"qwen/qwen3-coder:free","provider":{"max_price":{"prompt":1e-1000}},"messages":[]}`,
		`{"model":"qwen/qwen3-coder:free","provider":{"only":["paid"]},"messages":[]}`,
		`{"model":"qwen/qwen3-coder:free","plugins":[{"id":"web"}],"messages":[]}`,
		`{"model":"qwen/qwen3-coder:free","tools":[{"type":"openrouter:web_search"}],"messages":[]}`,
		`{"model":"qwen/qwen3-coder:free","tools":[{"type":"function","function":{"name":"openrouter:web_search"}}],"messages":[]}`,
		`{"model":"qwen/qwen3-coder:free","modalities":["audio"],"messages":[]}`,
		`{"model":"qwen/qwen3-coder:free","messages":[{"content":[{"type":"file","file":{"file_data":"private"}}]}]}`,
		`{"model":"paid/model","model":"qwen/qwen3-coder:free","messages":[]}`,
		`{"model":"qwen/qwen3-coder:free","messages":[]} {}`,
		`null`,
	} {
		t.Run(payload, func(t *testing.T) {
			if err := PrepareOpenRouterRequest(context.Background(), client, outgoing(t, payload), pinnedModel); !errors.Is(err, ErrRejected) {
				t.Fatal("unsafe input accepted")
			}
		})
	}
	for _, endpoint := range []string{"http://openrouter.ai/api/v1/chat/completions", "https://openrouter.ai.evil/api/v1/chat/completions", "https://openrouter.ai:444/api/v1/chat/completions", "https://openrouter.ai/api/v1/responses/compact", "https://openrouter.ai/api/v1/chat/completions?model=paid"} {
		req, _ := http.NewRequest(http.MethodPost, endpoint, strings.NewReader(`{"model":"qwen/qwen3-coder:free","messages":[]}`))
		req.Header.Set("Authorization", "Bearer mock-private-key")
		if PrepareOpenRouterRequest(context.Background(), client, req, pinnedModel) == nil {
			t.Fatal("unsafe endpoint accepted")
		}
	}
	req := outgoing(t, `{"model":"qwen/qwen3-coder:free","messages":[]}`)
	req.Header.Set("Cookie", "session=other")
	if PrepareOpenRouterRequest(context.Background(), client, req, pinnedModel) == nil {
		t.Fatal("alternate account header accepted")
	}
}

func TestZeroCapProofFailsClosed(t *testing.T) {
	for _, body := range []string{
		`{"data":{"limit":null,"limit_remaining":null,"include_byok_in_limit":true,"is_management_key":false}}`,
		`{"data":{"limit":1e-1000,"limit_remaining":0,"include_byok_in_limit":true,"is_management_key":false}}`,
		`{"data":{"limit":"0","limit_remaining":0,"include_byok_in_limit":true,"is_management_key":false}}`,
		`{"data":{"limit":0,"limit_remaining":1,"include_byok_in_limit":true,"is_management_key":false}}`,
		`{"data":{"limit":0,"limit_remaining":0,"include_byok_in_limit":false,"is_management_key":false}}`,
		`{"data":{"limit":0,"limit_remaining":0,"include_byok_in_limit":true,"is_management_key":true}}`,
		`{"data":{"limit":0,"limit":100,"limit_remaining":0,"include_byok_in_limit":true,"is_management_key":false}}`,
		`{}`, `not json`,
	} {
		client := &http.Client{Transport: fakeTransport(func(req *http.Request) (*http.Response, error) {
			return &http.Response{StatusCode: 200, Header: http.Header{}, Body: io.NopCloser(strings.NewReader(body))}, nil
		})}
		if PrepareOpenRouterRequest(context.Background(), client, outgoing(t, `{"model":"qwen/qwen3-coder:free","messages":[]}`), pinnedModel) == nil {
			t.Fatalf("unqualified cap accepted: %s", body)
		}
	}
	for _, status := range []int{301, 401, 429, 500} {
		client := &http.Client{Transport: fakeTransport(func(req *http.Request) (*http.Response, error) {
			return &http.Response{StatusCode: status, Header: http.Header{}, Body: io.NopCloser(strings.NewReader("mock-private-key"))}, nil
		})}
		err := PrepareOpenRouterRequest(context.Background(), client, outgoing(t, `{"model":"qwen/qwen3-coder:free","messages":[]}`), pinnedModel)
		if err == nil || strings.Contains(err.Error(), "mock-private-key") {
			t.Fatal("HTTP failure did not fail closed safely")
		}
	}
	client := &http.Client{Transport: fakeTransport(func(req *http.Request) (*http.Response, error) {
		return &http.Response{StatusCode: 200, Header: http.Header{"Age": []string{"100"}}, Body: io.NopCloser(strings.NewReader(proof))}, nil
	})}
	if PrepareOpenRouterRequest(context.Background(), client, outgoing(t, `{"model":"qwen/qwen3-coder:free","messages":[]}`), pinnedModel) == nil {
		t.Fatal("cached proof accepted")
	}
	client.Transport = fakeTransport(func(req *http.Request) (*http.Response, error) {
		return nil, errors.New("private request URL and mock-private-key")
	})
	if err := PrepareOpenRouterRequest(context.Background(), client, outgoing(t, `{"model":"qwen/qwen3-coder:free","messages":[]}`), pinnedModel); err == nil || strings.Contains(err.Error(), "mock-private-key") {
		t.Fatal("transport diagnostic leaked")
	}
}

func TestInboundAndContextPin(t *testing.T) {
	if ValidateOpenRouterInput([]byte(`{"model":"worker","input":"hello","tools":[{"type":"function","name":"read_file","parameters":{}}]}`), "worker", pinnedModel) != nil {
		t.Fatal("Responses input prerequisite rejected")
	}
	if ValidateOpenRouterInput([]byte(`{"model":"worker","models":["paid/model"]}`), "worker", pinnedModel) == nil {
		t.Fatal("inbound model fallback accepted")
	}
	if ValidateOpenRouterInput([]byte(`{"model":"worker","model":"worker"}`), "worker", pinnedModel) == nil {
		t.Fatal("duplicate alias accepted")
	}
	if _, ok := OpenRouterPin(context.Background()); ok {
		t.Fatal("unmarked context qualified")
	}
	if model, ok := OpenRouterPin(WithOpenRouterPin(context.Background(), "openrouter/free")); !ok || model != "openrouter/free" {
		t.Fatal("malformed pin intent disappeared instead of reaching final guard")
	}
	if model, ok := OpenRouterPin(WithOpenRouterPin(context.Background(), pinnedModel)); !ok || model != pinnedModel {
		t.Fatal("trusted pin missing")
	}
}
