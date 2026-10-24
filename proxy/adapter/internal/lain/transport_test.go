package lain

import (
	"context"
	auth "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/auth"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/httptransport"
	"github.com/tidwall/gjson"
	"io"
	"lain.local/proxy/internal/freeonly"
	"net/http"
	"strings"
	"testing"
)

type fixtureTransport func(*http.Request) (*http.Response, error)

func (f fixtureTransport) RoundTrip(req *http.Request) (*http.Response, error) { return f(req) }
func TestFinalFreeOnlyDecoratorBoundary(t *testing.T) {
	for _, scenario := range []string{"allowed", "wrong-pin", "wrong-model", "responses", "redirect", "same-endpoint-redirect"} {
		t.Run(scenario, func(t *testing.T) {
			calls := 0
			next := fixtureTransport(func(req *http.Request) (*http.Response, error) {
				calls++
				if req.URL.Path == "/api/v1/key" {
					return &http.Response{StatusCode: 200, Header: http.Header{}, Body: io.NopCloser(strings.NewReader(`{"data":{"limit":0,"limit_remaining":0,"include_byok_in_limit":true,"is_management_key":false}}`))}, nil
				}
				if calls != 2 {
					t.Fatal("inference reached before one fresh balance proof")
				}
				if scenario == "same-endpoint-redirect" {
					return &http.Response{StatusCode: 307, Header: http.Header{"Location": []string{req.URL.String()}}, Body: io.NopCloser(strings.NewReader(""))}, nil
				}
				if scenario == "redirect" {
					return &http.Response{StatusCode: 302, Header: http.Header{"Location": []string{"https://other.invalid/charged"}}, Body: io.NopCloser(strings.NewReader(""))}, nil
				}
				body, _ := io.ReadAll(req.Body)
				if gjson.GetBytes(body, "provider.allow_fallbacks").Bool() || gjson.GetBytes(body, "provider.max_price.request").Int() != 0 || gjson.GetBytes(body, "model").String() != "qwen/qwen3-coder:free" {
					t.Fatal("final outgoing body is not pinned")
				}
				return &http.Response{StatusCode: 200, Header: http.Header{}, Body: io.NopCloser(strings.NewReader(`{}`))}, nil
			})
			original := &http.Client{Transport: next}
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
			decorated := httptransport.Decorate(httptransport.WithDecorator(ctx, func(next http.RoundTripper) http.RoundTripper {
				return &guardedTransport{next: next, auth: &auth.Auth{}}
			}), original)
			req, _ := http.NewRequestWithContext(ctx, http.MethodPost, "https://openrouter.ai/api/v1/"+path, strings.NewReader(`{"model":"`+model+`","messages":[{"role":"user","content":"fixture"}],"stream":true}`))
			req.Header.Set("Authorization", "Bearer fixture-key")
			result, err := decorated.Do(req)
			if scenario == "allowed" {
				if err != nil || calls != 2 {
					t.Fatal("allowed request failed", err, calls)
				}
				result.Body.Close()
			} else if scenario == "redirect" || scenario == "same-endpoint-redirect" {
				if err == nil || calls != 2 {
					t.Fatal("redirect reached an unqualified transport", err, calls)
				}
			} else if err == nil || calls != 0 {
				t.Fatal("invalid intent reached the network", err, calls)
			}
			if original.CheckRedirect != nil {
				t.Fatal("shared client policy mutated")
			}
		})
	}
}
func TestProviderSessionAndGeminiBearerFinalHeaders(t *testing.T) {
	for _, session := range []string{"", "fixture-session", "bad session"} {
		calls := 0
		next := fixtureTransport(func(req *http.Request) (*http.Response, error) {
			calls++
			if req.Header.Get("x-goog-api-key") != "" || req.Header.Get("Authorization") != "Bearer fixture-key" {
				t.Fatal("Gemini bearer mode was not applied at send")
			}
			return &http.Response{StatusCode: 200, Header: http.Header{}, Body: io.NopCloser(strings.NewReader(`{}`))}, nil
		})
		tr := &guardedTransport{next: next, auth: &auth.Auth{Attributes: map[string]string{"auth_header": "bearer", "api_key": "fixture-key", "header:x-opencode-session": "$CPA-SESSION-ID"}}}
		req, _ := http.NewRequest(http.MethodPost, "http://fixture.invalid/model", nil)
		req.Header.Set("x-goog-api-key", "fixture-key")
		req.Header.Set("x-opencode-session", session)
		result, err := tr.RoundTrip(req)
		if session == "fixture-session" {
			if err != nil || calls != 1 {
				t.Fatal(err, calls)
			}
			result.Body.Close()
		} else if err == nil || calls != 0 {
			t.Fatal("invalid mandatory session dispatched")
		}
	}
}

func TestNativeImageToolsRejectedAtFinalBoundaryWithoutAffectingFunctions(t *testing.T) {
	for _, test := range []struct {
		body   string
		denied bool
	}{
		{`{"tools":[{"type":"image_generation"}]}`, true},
		{`{"tools":[{"type":"function","name":"x"}],"tools":[{"type":"image_generation"}]}`, true},
		{`{"tools":[{"type":"image_generation"}],"tools":[{"type":"function","name":"x"}]}`, true},
		{`{"tools":[{"type":"function","type":"image_generation"}]}`, true},
		{`{"tools":[{"type":"image_generation","type":"function"}]}`, true},
		{`{"tool_choice":{"type":"function","type":"image_generation"}}`, true},
		{`{"tool_choice":{"type":"image_generation"}}`, true},
		{`{"tool_choice":"image_generation"}`, true},
		{`{"tools":[{"type":"function","name":"image_generation"}]}`, false},
		{`{"tools":[{"type":"function","function":{"name":"generate_image"}}],"input":[{"type":"input_image","image_url":"fixture"}]}`, false},
	} {
		calls := 0
		next := fixtureTransport(func(req *http.Request) (*http.Response, error) {
			calls++
			body, _ := io.ReadAll(req.Body)
			if string(body) != test.body {
				t.Fatal("original body consumed or changed")
			}
			return &http.Response{StatusCode: 200, Header: http.Header{}, Body: io.NopCloser(strings.NewReader(`{}`))}, nil
		})
		req, _ := http.NewRequest(http.MethodPost, "http://fixture.invalid/responses", strings.NewReader(test.body))
		tr := &guardedTransport{next: next, auth: &auth.Auth{}}
		response, err := tr.RoundTrip(req)
		if test.denied {
			if err == nil || calls != 0 {
				t.Fatal("native image tool reached provider", test.body)
			}
		} else {
			if err != nil || calls != 1 {
				t.Fatal("client function or vision input rejected", err)
			}
			response.Body.Close()
		}
	}
}
