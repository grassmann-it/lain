package executor

import (
	"context"
	"io"
	"net/http"
	"strings"
	"testing"

	"github.com/router-for-me/CLIProxyAPI/v7/internal/freeonly"
	"github.com/tidwall/gjson"
)

type lainGuardTransport func(*http.Request) (*http.Response, error)

func (f lainGuardTransport) RoundTrip(req *http.Request) (*http.Response, error) { return f(req) }

func TestLainFinalFreeOnlyBoundary(t *testing.T) {
	for _, scenario := range []string{"allowed", "wrong-pin", "wrong-model", "responses", "redirect"} {
		t.Run(scenario, func(t *testing.T) {
			calls := 0
			tracked := 0
			client := &http.Client{Transport: lainGuardTransport(func(req *http.Request) (*http.Response, error) {
				calls++
				if req.URL.Path == "/api/v1/key" {
					return &http.Response{StatusCode: 200, Header: http.Header{}, Body: io.NopCloser(strings.NewReader(`{"data":{"limit":0,"limit_remaining":0,"include_byok_in_limit":true,"is_management_key":false}}`))}, nil
				}
				if scenario == "redirect" {
					return &http.Response{StatusCode: 302, Header: http.Header{"Location": []string{"https://other.invalid/charged"}}, Body: io.NopCloser(strings.NewReader(""))}, nil
				}
				body, _ := io.ReadAll(req.Body)
				if gjson.GetBytes(body, "provider.allow_fallbacks").Bool() || gjson.GetBytes(body, "provider.max_price.request").Int() != 0 || gjson.GetBytes(body, "model").String() != "qwen/qwen3-coder:free" {
					t.Fatal("final body not pinned")
				}
				return &http.Response{StatusCode: 200, Header: http.Header{}, Body: io.NopCloser(strings.NewReader(`{}`))}, nil
			})}
			pin := "qwen/qwen3-coder:free"
			model := pin
			path := "chat/completions"
			if scenario == "wrong-pin" {
				pin = "unqualified"
			}
			if scenario == "wrong-model" {
				model = "paid-model"
			}
			if scenario == "responses" {
				path = "responses"
			}
			ctx := freeonly.WithOpenRouterPin(context.Background(), pin)
			request, _ := http.NewRequestWithContext(ctx, http.MethodPost, "https://openrouter.ai/api/v1/"+path, strings.NewReader(`{"model":"`+model+`","messages":[{"role":"user","content":"fixture"}],"stream":true}`))
			request.Header.Set("Authorization", "Bearer fixture-key")
			request.Header.Set("Cache-Control", "no-cache")
			result, err := doOpenAICompatRequest(ctx, client, request, func(inference *http.Client) *http.Client {
				tracked++
				if calls != 1 {
					t.Fatal("inference instrumentation began before fresh proof completed")
				}
				return inference
			})
			if scenario == "allowed" || scenario == "redirect" {
				if err != nil {
					t.Fatal(err)
				}
				defer result.Body.Close()
				if tracked != 1 {
					t.Fatal("inference tracking absent or repeated")
				}
				if calls != 2 {
					t.Fatalf("expected fresh proof and inference only; got %d", calls)
				}
			} else if err == nil || calls != 0 || tracked != 0 {
				t.Fatal("invalid intent reached network")
			}
			if client.CheckRedirect != nil {
				t.Fatal("shared client was mutated")
			}
		})
	}
}
