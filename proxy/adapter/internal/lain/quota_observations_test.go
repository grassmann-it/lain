package lain

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/gin-gonic/gin"
	auth "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/auth"
	executor "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/executor"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/httptransport"
)

func quotaFixture(t *testing.T, baseURL string) (*Integration, context.Context, *auth.Auth) {
	t.Helper()
	i, ctx, a := dispatchFixture(t)
	route := i.Bindings.Routes["alias"]
	route.Provider = "groq"
	i.Bindings.Routes["alias"] = route
	c := ctx.Value("gin").(*gin.Context)
	c.Set("lain.selection", selection{Route: route, Alias: "alias", Allowed: map[string]bool{a.ID: true}})
	a.Provider = "openai-compatible-groq-openai-chat"
	a.Attributes = map[string]string{"compat_name": "groq-openai-chat", "upstream_protocol": "openai-chat", "base_url": baseURL, "api_key": "fixture-private-key"}
	return i, ctx, a
}

func quotaBatch(t *testing.T, q *quotaObservations) quotaObservationBatch {
	t.Helper()
	data, err := q.read()
	if err != nil {
		t.Fatal(err)
	}
	var batch quotaObservationBatch
	if err = json.Unmarshal(data, &batch); err != nil || batch.Version != 1 || batch.Observations == nil || len(data) > maxQuotaBytes {
		t.Fatalf("invalid batch: %v %s", err, data)
	}
	return batch
}

type quotaHTTPExecutor struct {
	fixtureExecutor
	url          string
	afterHeaders func()
}

func (f *quotaHTTPExecutor) Execute(ctx context.Context, _ *auth.Auth, _ executor.Request, _ executor.Options) (executor.Response, error) {
	req, _ := http.NewRequestWithContext(ctx, "POST", f.url+"/chat/completions", strings.NewReader(`{"model":"model","messages":[{"role":"user","content":"private prompt"}]}`))
	req.Header.Set("Authorization", "Bearer fixture-private-key")
	resp, err := httptransport.Decorate(ctx, &http.Client{}).Do(req)
	if err != nil {
		return executor.Response{}, err
	}
	defer resp.Body.Close()
	if f.afterHeaders != nil {
		f.afterHeaders()
	}
	body, err := io.ReadAll(resp.Body)
	return executor.Response{Payload: body}, err
}

func TestGroqCapturesRealHTTPHeadersBeforeReturnAndPersistsAcrossRestart(t *testing.T) {
	for _, status := range []int{200, 429, 403} {
		t.Run(fmt.Sprint(status), func(t *testing.T) {
			calls := 0
			server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				calls++
				if r.Header.Get("Authorization") != "Bearer fixture-private-key" {
					t.Error("auth lost")
				}
				w.Header().Set("x-ratelimit-limit-requests", "14400")
				w.Header().Set("x-ratelimit-remaining-requests", "14399")
				w.Header().Set("x-ratelimit-reset-requests", "24h0m0s")
				w.Header().Set("x-ratelimit-limit-tokens", "6000")
				w.Header().Set("x-ratelimit-remaining-tokens", "5990")
				w.Header().Set("x-ratelimit-reset-tokens", "7.66s")
				w.Header().Set("Authorization", "Bearer must-never-persist")
				w.Header().Set("Set-Cookie", "session=must-never-persist")
				w.WriteHeader(status)
				_, _ = io.WriteString(w, `{"choices":[],"private":"response payload must not persist"}`)
			}))
			defer server.Close()
			i, ctx, a := quotaFixture(t, server.URL)
			raw := &quotaHTTPExecutor{url: server.URL, afterHeaders: func() {
				if len(quotaBatch(t, i.Quotas).Observations) != 1 {
					t.Error("observation absent when RoundTrip returned")
				}
			}}
			if _, err := i.ExecutorDecorator(nil)(raw).Execute(ctx, a, executor.Request{Model: "model"}, executor.Options{}); err != nil {
				t.Fatal(err)
			}
			before, _ := i.Quotas.read()
			observation := quotaBatch(t, i.Quotas).Observations[0]
			if observation.AccountID != "stable-account" || observation.Provider != "groq" || observation.Model != "model" || observation.RequestID != ParentRequestIDFromContext(ctx) || observation.StatusCode != status || len(observation.Headers) != 6 {
				t.Fatalf("unproven metadata: %#v", observation)
			}
			for _, forbidden := range []string{"private", "prompt", "response payload", "Authorization", "Cookie", "credential-v2"} {
				if strings.Contains(string(before), forbidden) {
					t.Fatalf("non-quota data persisted: %s", forbidden)
				}
			}
			info, err := os.Stat(i.Quotas.path)
			if err != nil || info.Mode().Perm() != 0600 {
				t.Fatal("snapshot is not private", err)
			}
			path, bindings := i.Journal.file.Name(), i.Bindings
			if err = i.Journal.Close(); err != nil {
				t.Fatal(err)
			}
			reopened, err := OpenJournal(path)
			if err != nil {
				t.Fatal(err)
			}
			defer reopened.Close()
			restarted, err := New(reopened, bindings)
			if err != nil {
				t.Fatal(err)
			}
			after, err := restarted.Quotas.read()
			if err != nil || string(after) != string(before) || calls != 1 {
				t.Fatal("restart refreshed or lost observation", err, calls)
			}
		})
	}
}

func TestGroqCaptureRequiresExactDispatchIdentityAndFinalModel(t *testing.T) {
	for _, scenario := range []string{"wrong-auth", "wrong-provider", "wrong-route-provider", "wrong-model", "wrong-request", "no-attempt", "no-identity", "wrong-path", "wrong-origin", "transport-error"} {
		t.Run(scenario, func(t *testing.T) {
			i, ctx, a := quotaFixture(t, "http://fixture.invalid")
			if scenario != "no-attempt" && scenario != "no-identity" {
				if err := i.Before(ctx, a, executor.Request{Model: "model"}, executor.Options{}); err != nil {
					t.Fatal(err)
				}
			}
			model, endpoint := "model", "http://fixture.invalid/chat/completions"
			switch scenario {
			case "wrong-auth":
				a.ID = "outside-account"
			case "wrong-provider":
				a.Provider = "openai-compatible-other-openai-chat"
			case "wrong-route-provider":
				c := ctx.Value("gin").(*gin.Context)
				selected, _ := routeSelection(ctx)
				selected.Route.Provider = "other"
				c.Set("lain.selection", selected)
			case "wrong-model":
				model = "other-model"
			case "wrong-request":
				ctx = WithParentRequestID(ctx, "different-request")
			case "no-identity":
				ctx = context.Background()
			case "wrong-path":
				endpoint = "http://fixture.invalid/models"
			case "wrong-origin":
				endpoint = "http://other.invalid/chat/completions"
			}
			req, _ := http.NewRequestWithContext(ctx, "POST", endpoint, strings.NewReader(`{"model":"`+model+`"}`))
			tr := &guardedTransport{auth: a, integration: i, next: fixtureTransport(func(*http.Request) (*http.Response, error) {
				if scenario == "transport-error" {
					return nil, errors.New("fixture network error")
				}
				return &http.Response{StatusCode: 200, Header: http.Header{"X-Ratelimit-Limit-Tokens": []string{"6000"}}, Body: io.NopCloser(strings.NewReader(`{}`))}, nil
			})}
			resp, _ := tr.RoundTrip(req)
			if resp != nil {
				resp.Body.Close()
			}
			if len(quotaBatch(t, i.Quotas).Observations) != 0 {
				t.Fatal("unproven response attributed to account")
			}
		})
	}
}

func TestGroqMissingPartialDuplicateAndStaleHeaders(t *testing.T) {
	i, ctx, a := quotaFixture(t, "http://fixture.invalid")
	if err := i.Before(ctx, a, executor.Request{Model: "model"}, executor.Options{}); err != nil {
		t.Fatal(err)
	}
	request, _ := http.NewRequestWithContext(ctx, "POST", "http://fixture.invalid/chat/completions", strings.NewReader(`{"model":"model"}`))
	full := http.Header{"X-Ratelimit-Limit-Tokens": []string{"6000"}, "X-Ratelimit-Remaining-Tokens": []string{"5900"}, "X-Ratelimit-Reset-Tokens": []string{"7s"}}
	i.observeQuotaResponse(request, a, &http.Response{StatusCode: 200, Header: full})
	old := quotaBatch(t, i.Quotas).Observations[0]
	for _, empty := range []http.Header{nil, {"X-Other": []string{"123"}}, {"X-Ratelimit-Limit-Tokens": []string{"Bearer secret"}}, {"X-Ratelimit-Reset-Tokens": []string{strings.Repeat("1", 101)}}} {
		i.observeQuotaResponse(request, a, &http.Response{StatusCode: 200, Header: empty})
		if !quotaBatch(t, i.Quotas).Observations[0].ObservedAt.Equal(old.ObservedAt) {
			t.Fatal("empty response refreshed old quota")
		}
	}
	stale := old
	stale.ObservedAt = old.ObservedAt.Add(-time.Hour)
	stale.Headers = map[string][]string{"x-ratelimit-limit-tokens": {"1"}}
	i.Quotas.record(stale)
	if !quotaBatch(t, i.Quotas).Observations[0].ObservedAt.Equal(old.ObservedAt) {
		t.Fatal("stale record replaced latest")
	}
	partial := old
	partial.ObservedAt = old.ObservedAt.Add(time.Millisecond)
	partial.Headers = captureQuotaHeaders(http.Header{"X-Ratelimit-Limit-Requests": []string{"14400", "10000"}, "x-ratelimit-limit-requests": []string{"9000"}})
	i.Quotas.record(partial)
	current := quotaBatch(t, i.Quotas).Observations[0]
	if len(current.Headers) != 1 || len(current.Headers["x-ratelimit-limit-requests"]) != 3 || current.Headers["x-ratelimit-limit-tokens"] != nil {
		t.Fatal("partial headers merged or duplicates concealed", current)
	}
}

func TestQuotaSnapshotBoundsAndFailureAreIndependentFromJournal(t *testing.T) {
	q := openQuotaObservations(filepath.Join(t.TempDir(), "quotas.json"))
	// Populate in memory to exercise both hard bounds without 600 disk syncs.
	for n := 0; n < 600; n++ {
		headers := map[string][]string{}
		for _, name := range groqQuotaHeaders {
			value := strings.Repeat("1", 100)
			if strings.Contains(name, "reset") {
				value = strings.Repeat("1", 99) + "s"
			}
			headers[name] = []string{value, value, value, value}
		}
		o := quotaObservation{AccountID: fmt.Sprint("account-", n), Provider: "groq", Model: "model", RequestID: fmt.Sprint("request-", n), ObservedAt: time.Now().Add(-time.Duration(n) * time.Second), StatusCode: 200, Headers: headers}
		q.latest[quotaKey{o.AccountID, o.Model}] = o
	}
	batch := quotaBatch(t, q)
	if len(batch.Observations) >= 512 || len(batch.Observations) == 0 || batch.Observations[0].AccountID != "account-0" {
		t.Fatal("byte bound did not evict oldest", len(batch.Observations))
	}
	i, ctx, a := quotaFixture(t, "http://fixture.invalid")
	i.Quotas.path = filepath.Join(t.TempDir(), "absent-directory", "quotas.json")
	if err := i.Before(ctx, a, executor.Request{Model: "model"}, executor.Options{}); err != nil {
		t.Fatal(err)
	}
	req, _ := http.NewRequestWithContext(ctx, "POST", "http://fixture.invalid/chat/completions", strings.NewReader(`{"model":"model"}`))
	tr := &guardedTransport{auth: a, integration: i, next: fixtureTransport(func(*http.Request) (*http.Response, error) {
		return &http.Response{StatusCode: 200, Header: http.Header{"X-Ratelimit-Limit-Tokens": []string{"6000"}}, Body: io.NopCloser(strings.NewReader(`{"choices":[]}`))}, nil
	})}
	resp, err := tr.RoundTrip(req)
	if err != nil || resp == nil {
		t.Fatal("optional quota failure broke response", err)
	}
	resp.Body.Close()
	if _, err = i.Quotas.read(); err == nil {
		t.Fatal("quota persistence failure hidden")
	}
	if err = i.After(ctx, nil); err != nil || i.Journal.Health() != nil {
		t.Fatal("quota failure poisoned durable inference", err)
	}
	page, _ := i.Journal.Read(0, 10)
	if len(page.Events) != 2 || *page.Events[1].Outcome != "success" || page.Events[1].TotalTokens != nil {
		t.Fatal("inference durability or unknown usage changed", page)
	}
	for _, invalid := range []string{`{"version":2,"observations":[]}`, strings.Repeat("x", maxQuotaBytes+1), `{"version":1,"observations":[{}]}`} {
		path := filepath.Join(t.TempDir(), "invalid.json")
		if err = os.WriteFile(path, []byte(invalid), 0600); err != nil {
			t.Fatal(err)
		}
		if _, err = openQuotaObservations(path).read(); err == nil {
			t.Fatal("corrupt snapshot accepted")
		}
	}
}

func TestQuotaEndpointUsesManagementAuthAndNeverDispatches(t *testing.T) {
	i, _, _ := quotaFixture(t, "http://unused.invalid")
	gin.SetMode(gin.TestMode)
	g := gin.New()
	g.Use(i.Middleware("management-token", auth.NewManager(nil, nil, nil), "inference-token"))
	for _, test := range []struct {
		method, token string
		status        int
	}{{"GET", "inference-token", 401}, {"GET", "", 401}, {"POST", "management-token", 405}, {"GET", "management-token", 200}} {
		r := httptest.NewRequest(test.method, "/lain/quota/observations", nil)
		r.Header.Set("Authorization", "Bearer "+test.token)
		w := httptest.NewRecorder()
		g.ServeHTTP(w, r)
		if w.Code != test.status {
			t.Fatal(w.Code, w.Body.String())
		}
		if w.Code == 200 && (w.Body.String() != `{"version":1,"observations":[]}` || w.Header().Get("Cache-Control") != "no-store") {
			t.Fatal("invalid empty quota source", w.Body.String())
		}
	}
	page, _ := i.Journal.Read(0, 10)
	if len(page.Events) != 0 {
		t.Fatal("quota polling created inference")
	}
}
