package lain

import (
	"bytes"
	"context"
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/http/httptest"
	"reflect"
	"strings"
	"sync"
	"testing"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/api/handlers"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/api/handlers/openai"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy"
	auth "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/auth"
	executor "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/executor"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/config"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/httptransport"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/translator"
	"github.com/tidwall/sjson"
	"lain.local/proxy/internal/freeonly"
)

type routerTestError struct {
	status int
	body   string
}

func (e *routerTestError) Error() string   { return e.body }
func (e *routerTestError) StatusCode() int { return e.status }
func routerTestQuota() error {
	return &routerTestError{429, `{"error":{"type":"exceeded_current_quota_error"}}`}
}

type routerTestExecutor struct {
	fixtureExecutor
	identifier string
	mu         sync.Mutex
	calls      []string
	execute    func(context.Context, *auth.Auth, executor.Request) (executor.Response, error)
	stream     func(context.Context, *auth.Auth, executor.Request) (*executor.StreamResult, error)
}

func (e *routerTestExecutor) ForAPIKey() auth.ProviderExecutor { return e }
func (e *routerTestExecutor) Identifier() string {
	if e.identifier != "" {
		return e.identifier
	}
	return "fixture"
}
func (e *routerTestExecutor) record(ctx context.Context) {
	selected, _ := routeSelection(ctx)
	e.mu.Lock()
	e.calls = append(e.calls, selected.Route.ID)
	e.mu.Unlock()
}
func (e *routerTestExecutor) routes() []string {
	e.mu.Lock()
	defer e.mu.Unlock()
	return append([]string(nil), e.calls...)
}
func (e *routerTestExecutor) Execute(ctx context.Context, a *auth.Auth, req executor.Request, _ executor.Options) (executor.Response, error) {
	e.record(ctx)
	if e.execute != nil {
		return e.execute(ctx, a, req)
	}
	return executor.Response{Payload: []byte(`{"id":"resp","object":"response","status":"completed","output":[]}`)}, nil
}
func (e *routerTestExecutor) ExecuteStream(ctx context.Context, a *auth.Auth, req executor.Request, _ executor.Options) (*executor.StreamResult, error) {
	e.record(ctx)
	if e.stream != nil {
		return e.stream(ctx, a, req)
	}
	return nil, routerTestQuota()
}
func (e *routerTestExecutor) RequestToFormat(executor.Request, executor.Options) translator.Format {
	return translator.FormatOpenAIResponse
}

func failoverFixture(t *testing.T) (*Integration, *auth.Manager, *routerTestExecutor, *gin.Engine) {
	t.Helper()
	b := Bindings{Accounts: map[string]string{}, Routes: map[string]Route{}, Routers: map[string]CollectionRouter{}}
	router := CollectionRouter{ID: "router", ProfileID: "profile", Selection: "ordered_sticky_v1", CostPolicy: "configured", RouteIDs: []string{"a", "b", "c"}, MemberFingerprints: map[string]string{}}
	credentials := []*auth.Auth{}
	for n, id := range router.RouteIDs {
		credential := &auth.Auth{ID: uuid.NewString(), Provider: "fixture", Attributes: map[string]string{"upstream_protocol": "openai-responses"}}
		credentials = append(credentials, credential)
		b.Accounts["account-"+id] = credential.ID
		model := "model-" + uuid.NewString()
		b.Routes[id] = Route{ID: id, Model: model, Provider: "fixture", Accounts: []string{"account-" + id}, Selection: "strict", UpstreamProtocol: "responses"}
		router.MemberFingerprints[id] = strings.Repeat(fmt.Sprintf("%x", n+1), 64)
		cliproxy.GlobalModelRegistry().RegisterClient(credential.ID, "fixture", []*cliproxy.ModelInfo{{ID: model, Object: "model", OwnedBy: "fixture"}})
		t.Cleanup(func() { cliproxy.GlobalModelRegistry().UnregisterClient(credential.ID) })
	}
	b.Routers["auto"] = router
	i, err := New(testJournal(t), b)
	if err != nil {
		t.Fatal(err)
	}
	manager := auth.NewManager(nil, NewSelector(), nil)
	if err := manager.SetExecutorDecorator(i.ExecutorDecorator(nil)); err != nil {
		t.Fatal(err)
	}
	for _, a := range credentials {
		if _, err := manager.Register(auth.WithSkipPersist(context.Background()), a); err != nil {
			t.Fatal(err)
		}
	}
	raw := &routerTestExecutor{}
	manager.RegisterExecutor(raw)
	return i, manager, raw, failoverEngine(i, manager)
}
func failoverEngine(i *Integration, manager *auth.Manager) *gin.Engine {
	g := gin.New()
	g.Use(i.Middleware("management-token", manager, "inference-token"))
	h := openai.NewOpenAIResponsesAPIHandler(handlers.NewBaseAPIHandlers(&config.SDKConfig{}, manager))
	g.POST("/v1/responses", h.Responses)
	return g
}
func failoverRequest(g *gin.Engine, thread, body, account string) *httptest.ResponseRecorder {
	req := httptest.NewRequest("POST", "/v1/responses", strings.NewReader(body))
	req.Header.Set("Authorization", "Bearer inference-token")
	req.Header.Set("Thread-Id", thread)
	if account != "" {
		req.Header.Set("X-Lain-Account-ID", account)
	}
	w := httptest.NewRecorder()
	g.ServeHTTP(w, req)
	return w
}

func TestRouterQuotaFailoverFullReplayAndAttemptIdentities(t *testing.T) {
	i, _, raw, g := failoverFixture(t)
	body := " {\n\"model\":\"auto\",\"input\":\"" + strings.Repeat("Grüße🐈", 16384) + "\",\"instructions\":\"keep every control\",\"store\":false,\"tools\":[{\"type\":\"function\",\"name\":\"echo\",\"parameters\":{\"type\":\"object\"}}],\"parallel_tool_calls\":false} "
	raw.execute = func(ctx context.Context, _ *auth.Auth, req executor.Request) (executor.Response, error) {
		selected, _ := routeSelection(ctx)
		want, _ := sjson.SetBytes([]byte(body), "model", selected.Route.Model)
		if !bytes.Equal(want, req.Payload) {
			t.Error("replay changed caller history or controls")
		}
		if selected.Route.ID == "a" {
			return executor.Response{}, routerTestQuota()
		}
		return executor.Response{Payload: []byte(`{"status":"completed","output":[]}`)}, nil
	}
	w := failoverRequest(g, uuid.NewString(), body, "")
	if w.Code != 200 || !reflect.DeepEqual(raw.routes(), []string{"a", "b"}) {
		t.Fatalf("status=%d calls=%v body=%s", w.Code, raw.routes(), w.Body.String())
	}
	page, err := i.Journal.Read(0, 100)
	if err != nil {
		t.Fatal(err)
	}
	starts := []Event{}
	finishes := []Event{}
	for _, event := range page.Events {
		if event.Kind == "attempt_started" {
			starts = append(starts, event)
		}
		if event.Kind == "usage" {
			finishes = append(finishes, event)
		}
	}
	if len(starts) != 2 || len(finishes) != 2 || starts[0].AttemptID == starts[1].AttemptID || starts[0].RequestID != starts[1].RequestID || starts[0].RequestID == "" {
		t.Fatal("dispatch journal identities", page)
	}
	for n, id := range []string{"a", "b"} {
		if starts[n].RequestedModel != "auto" || starts[n].RouteID == nil || *starts[n].RouteID != id || starts[n].AccountID != "account-"+id {
			t.Fatal(starts)
		}
	}
	if finishes[0].Outcome == nil || *finishes[0].Outcome != "failed" || finishes[1].Outcome == nil || *finishes[1].Outcome != "success" {
		t.Fatal(finishes)
	}
}

func TestRouterQuotaAllExhaustedAndCooldown(t *testing.T) {
	_, _, raw, g := failoverFixture(t)
	raw.execute = func(context.Context, *auth.Auth, executor.Request) (executor.Response, error) {
		return executor.Response{}, routerTestQuota()
	}
	body := `{"model":"auto","input":"hello"}`
	w := failoverRequest(g, uuid.NewString(), body, "")
	if w.Code != 429 || !strings.Contains(w.Body.String(), "router_quota_exhausted") || !reflect.DeepEqual(raw.routes(), []string{"a", "b", "c"}) {
		t.Fatal(w.Code, w.Body.String(), raw.routes())
	}
	w = failoverRequest(g, uuid.NewString(), body, "")
	if w.Code != 429 || len(raw.routes()) != 3 {
		t.Fatal("cooldown burned another call", w.Code, raw.routes())
	}
}

func TestRouterNonQuotaDoesNotRetry(t *testing.T) {
	for _, tc := range []struct {
		status int
		body   string
	}{{400, `{"error":{"code":"context_length_exceeded"}}`}, {401, `{"error":{"code":"invalid_api_key"}}`}, {403, `{"error":{"code":"permission_denied"}}`}, {500, `{"error":{"message":"quota exceeded"}}`}, {400, `{"error":{"code":"invalid_parameter","message":"insufficient_quota"}}`}} {
		t.Run(fmt.Sprint(tc.status, tc.body), func(t *testing.T) {
			_, _, raw, g := failoverFixture(t)
			raw.execute = func(context.Context, *auth.Auth, executor.Request) (executor.Response, error) {
				return executor.Response{}, &routerTestError{tc.status, tc.body}
			}
			w := failoverRequest(g, uuid.NewString(), `{"model":"auto","input":"hello"}`, "")
			if w.Code != tc.status || !reflect.DeepEqual(raw.routes(), []string{"a"}) {
				t.Fatal(w.Code, w.Body.String(), raw.routes())
			}
		})
	}
}

func TestRouterKimiNon429QuotaAndAccountAliases(t *testing.T) {
	i, manager, raw, g := failoverFixture(t)
	// An adjacent alias shares the exact logical account and credential.
	b := i.Bindings.Routes["b"]
	b.Accounts = i.Bindings.Routes["a"].Accounts
	i.Bindings.Routes["b"] = b
	a := routerCredential(i, i.Bindings.Routes["a"], manager)
	cliproxy.GlobalModelRegistry().RegisterClient(a.ID, "fixture", []*cliproxy.ModelInfo{{ID: i.Bindings.Routes["a"].Model, Object: "model"}, {ID: b.Model, Object: "model"}})
	raw.execute = func(ctx context.Context, _ *auth.Auth, _ executor.Request) (executor.Response, error) {
		selected, _ := routeSelection(ctx)
		if selected.Route.ID == "a" {
			return executor.Response{}, &routerTestError{400, `{"error":{"type":"exceeded_current_quota_error"}}`}
		}
		return executor.Response{Payload: []byte(`{"output":[]}`)}, nil
	}
	w := failoverRequest(g, uuid.NewString(), `{"model":"auto","input":"hello"}`, "")
	if w.Code != 200 || !reflect.DeepEqual(raw.routes(), []string{"a", "c"}) {
		t.Fatal("account quota burned alias", w.Code, raw.routes(), w.Body.String())
	}
}

func TestRouterGeneric429KeepsIndependentModelWindow(t *testing.T) {
	i, manager, raw, g := failoverFixture(t)
	b := i.Bindings.Routes["b"]
	b.Accounts = i.Bindings.Routes["a"].Accounts
	i.Bindings.Routes["b"] = b
	a := routerCredential(i, i.Bindings.Routes["a"], manager)
	cliproxy.GlobalModelRegistry().RegisterClient(a.ID, "fixture", []*cliproxy.ModelInfo{{ID: i.Bindings.Routes["a"].Model, Object: "model"}, {ID: b.Model, Object: "model"}})
	raw.execute = func(ctx context.Context, _ *auth.Auth, _ executor.Request) (executor.Response, error) {
		selected, _ := routeSelection(ctx)
		if selected.Route.ID == "a" {
			return executor.Response{}, &routerTestError{429, `{"error":{"type":"rate_limit_error"}}`}
		}
		return executor.Response{Payload: []byte(`{"output":[]}`)}, nil
	}
	w := failoverRequest(g, uuid.NewString(), `{"model":"auto","input":"hello"}`, "")
	if w.Code != 200 || !reflect.DeepEqual(raw.routes(), []string{"a", "b"}) {
		t.Fatal("generic rate error suppressed independent model", w.Code, raw.routes(), w.Body.String())
	}
}

func TestRouterSSECreatedOrToolEventStopsFailover(t *testing.T) {
	for _, payload := range []string{`{"type":"response.created","response":{"id":"resp"}}`, `{"type":"response.output_item.added","item":{"type":"function_call","call_id":"c","name":"echo","arguments":"{}"}}`} {
		t.Run(payload, func(t *testing.T) {
			i, _, raw, g := failoverFixture(t)
			raw.stream = func(context.Context, *auth.Auth, executor.Request) (*executor.StreamResult, error) {
				chunks := make(chan executor.StreamChunk, 2)
				chunks <- executor.StreamChunk{Payload: []byte("data: " + payload + "\n\n")}
				chunks <- executor.StreamChunk{Err: routerTestQuota()}
				close(chunks)
				return &executor.StreamResult{Chunks: chunks}, nil
			}
			thread := uuid.NewString()
			w := failoverRequest(g, thread, `{"model":"auto","input":"hello","stream":true}`, "")
			if w.Code != 200 || !reflect.DeepEqual(raw.routes(), []string{"a"}) {
				t.Fatal(w.Code, w.Body.String(), raw.routes())
			}
			state, err := i.routerPins.read()
			if err != nil || len(state.Pins) != 1 || state.Pins[0].RouteID != "a" {
				t.Fatal("visible output moved pin", state, err)
			}
		})
	}
}

func TestRouterPinnedContinuationMigrationRestartAndCAS(t *testing.T) {
	i, manager, raw, g := failoverFixture(t)
	thread := uuid.NewString()
	if w := failoverRequest(g, thread, `{"model":"auto","input":"hello"}`, ""); w.Code != 200 {
		t.Fatal(w.Code, w.Body.String())
	}
	continuation := `{"model":"auto","input":[{"role":"user","content":"hello"},{"type":"function_call","call_id":"c","name":"echo","arguments":"{}"},{"type":"function_call_output","call_id":"c","output":"all history"}],"store":false}`
	raw.execute = func(ctx context.Context, _ *auth.Auth, req executor.Request) (executor.Response, error) {
		s, _ := routeSelection(ctx)
		want, _ := sjson.SetBytes([]byte(continuation), "model", s.Route.Model)
		if !bytes.Equal(want, req.Payload) {
			t.Error("continuation changed")
		}
		if s.Route.ID == "a" {
			return executor.Response{}, routerTestQuota()
		}
		return executor.Response{Payload: []byte(`{"output":[]}`)}, nil
	}
	w := failoverRequest(g, thread, continuation, "")
	if w.Code != 200 {
		t.Fatal(w.Code, w.Body.String())
	}
	restarted, err := New(i.Journal, i.Bindings)
	if err != nil || restarted.routerPinError != nil {
		t.Fatal(err, restarted.routerPinError)
	}
	router := i.Bindings.Routers["auto"]
	if err := restarted.routerPins.replace(router, thread, routerPinFor(router, i.Bindings.Routes["a"]), routerPinFor(router, i.Bindings.Routes["c"])); err == nil {
		t.Fatal("stale CAS moved migrated pin")
	}
	route, err := restarted.routerRoute(router, "auto", []byte(continuation), http.Header{"Thread-Id": {thread}}, manager)
	if err != nil || route.ID != "b" {
		t.Fatal("restart pin lost", route, err)
	}
}

func TestRouterConcurrentFirstRequestsAndCanceledWaiter(t *testing.T) {
	i, _, raw, g := failoverFixture(t)
	thread := uuid.NewString()
	entered := make(chan struct{})
	release := make(chan struct{})
	raw.execute = func(ctx context.Context, _ *auth.Auth, _ executor.Request) (executor.Response, error) {
		s, _ := routeSelection(ctx)
		if s.Route.ID == "a" {
			close(entered)
			<-release
			return executor.Response{}, routerTestQuota()
		}
		return executor.Response{Payload: []byte(`{"output":[]}`)}, nil
	}
	body := `{"model":"auto","input":"hello"}`
	results := make(chan *httptest.ResponseRecorder, 2)
	go func() { results <- failoverRequest(g, thread, body, "") }()
	<-entered
	canceled, cancel := context.WithCancel(context.Background())
	cancel()
	if _, err := i.routerPins.acquire(canceled, i.Bindings.Routers["auto"], thread); !errors.Is(err, context.Canceled) {
		t.Fatal(err)
	}
	go func() { results <- failoverRequest(g, thread, body, "") }()
	close(release)
	for n := 0; n < 2; n++ {
		w := <-results
		if w.Code != 200 {
			t.Fatal(w.Code, w.Body.String())
		}
	}
	if !reflect.DeepEqual(raw.routes(), []string{"a", "b", "b"}) {
		t.Fatal("concurrent divergent dispatch", raw.routes())
	}
}

func TestRouterAccountPinAndCapabilityRemainOnFallback(t *testing.T) {
	t.Run("account", func(t *testing.T) {
		_, _, raw, g := failoverFixture(t)
		raw.execute = func(context.Context, *auth.Auth, executor.Request) (executor.Response, error) {
			return executor.Response{}, routerTestQuota()
		}
		w := failoverRequest(g, uuid.NewString(), `{"model":"auto","input":"hello"}`, "account-a")
		if w.Code != 429 || !reflect.DeepEqual(raw.routes(), []string{"a"}) {
			t.Fatal(w.Code, raw.routes())
		}
	})
	t.Run("capability", func(t *testing.T) {
		i, manager, raw, g := failoverFixture(t)
		b := i.Bindings.Routes["b"]
		b.Provider = "xiaomi-mimo"
		b.Model = "mimo-v2.6-pro"
		b.UpstreamProtocol = "openai"
		i.Bindings.Routes["b"] = b
		a := routerCredential(i, b, manager)
		cliproxy.GlobalModelRegistry().RegisterClient(a.ID, "fixture", []*cliproxy.ModelInfo{{ID: b.Model, Object: "model"}})
		raw.execute = func(ctx context.Context, _ *auth.Auth, _ executor.Request) (executor.Response, error) {
			s, _ := routeSelection(ctx)
			if s.Route.ID == "a" {
				return executor.Response{}, routerTestQuota()
			}
			return executor.Response{Payload: []byte(`{"output":[]}`)}, nil
		}
		w := failoverRequest(g, uuid.NewString(), `{"model":"auto","input":"hello","tools":[{"type":"web_search"}]}`, "")
		if w.Code != 200 || !reflect.DeepEqual(raw.routes(), []string{"a", "c"}) {
			t.Fatal(w.Code, w.Body.String(), raw.routes())
		}
	})
}

func TestRouterQuotaTransportClassificationPreservesBodyAndRetryHint(t *testing.T) {
	for _, code := range []int{400, 429} {
		ctx := context.Background()
		c := &gin.Context{}
		state := &routerDispatch{}
		c.Set("lain.router_dispatch", state)
		ctx = context.WithValue(ctx, "gin", c)
		source := &contextBoundaryBody{Reader: strings.NewReader(`{"error":{"type":"exceeded_current_quota_error"}}`)}
		response := &http.Response{StatusCode: code, Header: http.Header{"Retry-After": {"9999"}}, Body: source}
		observeRouterQuotaResponse(ctx, response)
		body, err := io.ReadAll(response.Body)
		_ = response.Body.Close()
		if err != nil || source.closes != 1 || string(body) != `{"error":{"type":"exceeded_current_quota_error"}}` || !state.quota || !state.accountQuota || state.retryAfter != 9999*time.Second {
			t.Fatal("quota evidence/body", state, err)
		}
	}
}

func TestRouterStreamingQuotaBeforeOutputRetries(t *testing.T) {
	_, _, raw, g := failoverFixture(t)
	raw.stream = func(ctx context.Context, _ *auth.Auth, _ executor.Request) (*executor.StreamResult, error) {
		s, _ := routeSelection(ctx)
		if s.Route.ID == "a" {
			return nil, routerTestQuota()
		}
		chunks := make(chan executor.StreamChunk, 1)
		chunks <- executor.StreamChunk{Payload: []byte("data: {\"type\":\"response.completed\",\"response\":{\"id\":\"resp\",\"status\":\"completed\",\"output\":[]}}\n\n")}
		close(chunks)
		return &executor.StreamResult{Chunks: chunks}, nil
	}
	w := failoverRequest(g, uuid.NewString(), `{"model":"auto","input":"hello","stream":true}`, "")
	if w.Code != 200 || !reflect.DeepEqual(raw.routes(), []string{"a", "b"}) || !strings.Contains(w.Body.String(), "response.completed") {
		t.Fatal(w.Code, w.Body.String(), raw.routes())
	}
}

func TestRouterCancellationDoesNotDispatchFallback(t *testing.T) {
	i, _, raw, g := failoverFixture(t)
	ctx, cancel := context.WithCancel(context.Background())
	raw.execute = func(context.Context, *auth.Auth, executor.Request) (executor.Response, error) {
		cancel()
		return executor.Response{}, routerTestQuota()
	}
	req := httptest.NewRequest("POST", "/v1/responses", strings.NewReader(`{"model":"auto","input":"hello"}`)).WithContext(ctx)
	req.Header.Set("Authorization", "Bearer inference-token")
	req.Header.Set("Thread-Id", uuid.NewString())
	w := httptest.NewRecorder()
	g.ServeHTTP(w, req)
	if !reflect.DeepEqual(raw.routes(), []string{"a"}) {
		t.Fatal("canceled fallback", raw.routes())
	}
	state, err := i.routerPins.read()
	if err != nil || len(state.Pins) != 1 || state.Pins[0].RouteID != "a" {
		t.Fatal(state, err)
	}
}

func TestRouterErrorBufferBoundFlushAndBodyLimit(t *testing.T) {
	for _, action := range []string{"small", "overflow", "flush", "success"} {
		t.Run(action, func(t *testing.T) {
			recorder := httptest.NewRecorder()
			c, _ := gin.CreateTestContext(recorder)
			w := newRouterResponseWriter(c.Writer)
			w.WriteHeader(429)
			switch action {
			case "small":
				_, _ = w.WriteString("quota")
				if recorder.Body.Len() != 0 || w.committed {
					t.Fatal("error exposed before decision")
				}
			case "overflow":
				_, _ = w.Write(make([]byte, maxRouterErrorBytes+1))
			case "flush":
				w.Flush()
			case "success":
				w.WriteHeader(200)
				_, _ = w.WriteString("data: tool event\n\n")
			}
			if action != "small" && !w.committed {
				t.Fatal("output boundary not committed")
			}
			w.commit()
		})
	}
	_, _, raw, g := failoverFixture(t)
	w := failoverRequest(g, uuid.NewString(), strings.Repeat("x", 32*1024*1024+1), "")
	if w.Code != 413 || len(raw.routes()) != 0 {
		t.Fatal("unbounded replay request", w.Code, raw.routes())
	}
}

func TestRouterQuotaCodesRejectAmbiguity(t *testing.T) {
	for _, body := range []string{`{"error":{"code":"insufficient_quota","code":"invalid_api_key"}}`, `{"error":{"code":"insufficient_quota"},"error":{"code":"invalid_api_key"}}`, `{"error":{"code":"insufficient_quota","type":"authentication_error"}}`, `{"error":{"message":"quota_exceeded"}}`, `{"error":{"code":"quota_exceeded"}} trailing`} {
		if routerQuotaPayload([]byte(body)) {
			t.Fatal("ambiguous quota evidence", body)
		}
	}
}

func TestRouterFreeOnlyFallbackRetainsFreshZeroCapAndStopsOnPaidProof(t *testing.T) {
	for _, tc := range []struct{ paid, mixed bool }{{false, false}, {true, false}, {false, true}} {
		paid, mixed := tc.paid, tc.mixed
		t.Run(fmt.Sprint(paid, mixed), func(t *testing.T) {
			i, manager, raw, _ := failoverFixture(t)
			provider := "openai-compatible-openrouter-openai-chat"
			raw.identifier = provider
			manager.RegisterExecutor(raw)
			router := i.Bindings.Routers["auto"]
			router.CostPolicy = "free_only"
			if mixed {
				router.CostPolicy = "configured"
			}
			i.Bindings.Routers["auto"] = router
			for _, id := range router.RouteIDs {
				route := i.Bindings.Routes[id]
				route.Model = "qwen/qwen3-coder:free"
				route.Provider = "openrouter"
				route.UpstreamProtocol = "openai"
				route.CostPolicy = "free_only"
				route.PricingClass = "free"
				route.FreeOnlyEnforcement = freeonly.OpenRouterZeroCapV1
				if mixed && id != "a" {
					route.CostPolicy = "configured"
				}
				i.Bindings.Routes[id] = route
				a := routerCredential(i, route, manager)
				a.Provider = provider
				a.Attributes = map[string]string{"compat_name": "openrouter-openai-chat", "upstream_protocol": "openai-chat", "base_url": "https://openrouter.ai/api/v1", "api_key": "fixture-" + id}
				if _, err := manager.Update(auth.WithSkipPersist(context.Background()), a); err != nil {
					t.Fatal(err)
				}
				cliproxy.GlobalModelRegistry().RegisterClient(a.ID, provider, []*cliproxy.ModelInfo{{ID: route.Model, Object: "model"}})
			}
			if err := validateRouters(i.Bindings); err != nil {
				t.Fatal(err)
			}
			proofs, sent := 0, []string{}
			raw.execute = func(ctx context.Context, a *auth.Auth, req executor.Request) (executor.Response, error) {
				selected, _ := routeSelection(ctx)
				if pin, ok := freeonly.OpenRouterPin(ctx); !ok || pin != selected.Route.Model {
					t.Error("free_only execution pin missing")
				}
				client := httptransport.Decorate(ctx, &http.Client{Transport: fixtureTransport(func(r *http.Request) (*http.Response, error) {
					if r.Method == "GET" {
						proofs++
						proof := `{"data":{"limit":0,"limit_remaining":0,"include_byok_in_limit":true,"is_management_key":false}}`
						if paid && selected.Route.ID == "b" {
							proof = `{"data":{"limit":10,"limit_remaining":10,"include_byok_in_limit":true,"is_management_key":false}}`
						}
						return &http.Response{StatusCode: 200, Header: http.Header{}, Body: io.NopCloser(strings.NewReader(proof))}, nil
					}
					sent = append(sent, selected.Route.ID)
					body, _ := io.ReadAll(r.Body)
					if !strings.Contains(string(body), `"allow_fallbacks":false`) || !strings.Contains(string(body), `"max_price"`) {
						t.Error("cost guard missing", string(body))
					}
					return &http.Response{StatusCode: 200, Header: http.Header{}, Body: io.NopCloser(strings.NewReader(`{}`))}, nil
				})})
				wire, _ := http.NewRequestWithContext(ctx, "POST", "https://openrouter.ai/api/v1/chat/completions", strings.NewReader(`{"model":"`+req.Model+`","messages":[{"role":"user","content":"hello"}]}`))
				wire.Header.Set("Authorization", "Bearer "+a.Attributes["api_key"])
				response, err := client.Do(wire)
				if response != nil {
					_ = response.Body.Close()
				}
				if err != nil {
					return executor.Response{}, err
				}
				if selected.Route.ID == "a" {
					return executor.Response{}, routerTestQuota()
				}
				return executor.Response{Payload: []byte(`{"output":[]}`)}, nil
			}
			w := failoverRequest(failoverEngine(i, manager), uuid.NewString(), `{"model":"auto","input":"hello"}`, "")
			if mixed {
				if proofs != 1 || w.Code != 429 || !reflect.DeepEqual(raw.routes(), []string{"a"}) || !reflect.DeepEqual(sent, []string{"a"}) {
					t.Fatal("free-only floor weakened", proofs, w.Code, raw.routes(), sent)
				}
				return
			}
			if proofs != 2 || !reflect.DeepEqual(raw.routes(), []string{"a", "b"}) {
				t.Fatal("fresh proof count or extra member", proofs, raw.routes(), w.Code, w.Body.String())
			}
			if paid {
				if w.Code == 200 || !reflect.DeepEqual(sent, []string{"a"}) {
					t.Fatal("paid proof allowed fallback", w.Code, sent)
				}
			} else if w.Code != 200 || !reflect.DeepEqual(sent, []string{"a", "b"}) {
				t.Fatal(w.Code, sent, w.Body.String())
			}
		})
	}
}

func TestRouterSDKRetriesAndDuplicateTransportSendAreBounded(t *testing.T) {
	_, manager, raw, g := failoverFixture(t)
	manager.SetRetryConfig(5, time.Millisecond, 10)
	raw.execute = func(context.Context, *auth.Auth, executor.Request) (executor.Response, error) {
		return executor.Response{}, routerTestQuota()
	}
	w := failoverRequest(g, uuid.NewString(), `{"model":"auto","input":"hello"}`, "")
	if w.Code != 429 || !reflect.DeepEqual(raw.routes(), []string{"a", "b", "c"}) {
		t.Fatal("SDK duplicated member", w.Code, raw.routes())
	}
	c := &gin.Context{}
	c.Set("lain.router_dispatch", &routerDispatch{})
	ctx := context.WithValue(context.Background(), "gin", c)
	if err := claimRouterSend(ctx); err != nil {
		t.Fatal(err)
	}
	if err := claimRouterSend(ctx); err == nil {
		t.Fatal("duplicate internal HTTP inference was allowed")
	}
}

func TestRouterQuotaFailureEventBeforeOutputRetries(t *testing.T) {
	_, _, raw, g := failoverFixture(t)
	raw.stream = func(ctx context.Context, _ *auth.Auth, _ executor.Request) (*executor.StreamResult, error) {
		selected, _ := routeSelection(ctx)
		chunks := make(chan executor.StreamChunk, 1)
		payload := `{"type":"response.completed","response":{"status":"completed","output":[]}}`
		if selected.Route.ID == "a" {
			payload = `{"type":"response.failed","response":{"status":"failed","error":{"type":"exceeded_current_quota_error"}}}`
		}
		chunks <- executor.StreamChunk{Payload: []byte("data: " + payload + "\n\n")}
		close(chunks)
		return &executor.StreamResult{Chunks: chunks}, nil
	}
	w := failoverRequest(g, uuid.NewString(), `{"model":"auto","input":"hello","stream":true}`, "")
	if w.Code != 200 || !reflect.DeepEqual(raw.routes(), []string{"a", "b"}) {
		t.Fatal(w.Code, w.Body.String(), raw.routes())
	}
}

func TestRouterPinnedFragmentsCannotMigrateAfterQuota(t *testing.T) {
	for _, body := range []string{`{"model":"auto","input":"only latest user turn"}`, `{"model":"auto","input":[{"type":"function_call_output","call_id":"missing","output":"partial history"}]}`, `{"model":"auto","input":[{"role":"user","content":"prior"},{"role":"assistant","content":"prior"},{"type":"function_call_output","call_id":"missing","output":"unpaired"}]}`, `{"model":"auto","input":[{"role":"user","content":"apply the patch"},{"type":"reasoning","summary":[{"type":"summary_text","text":"preserve opaque history"}]},{"type":"custom_tool_call","call_id":"patch-1","name":"apply_patch","input":"*** Begin Patch\n*** End Patch"},{"type":"custom_tool_call_output","call_id":"patch-1","output":"applied"},{"role":"user","content":"continue"}]}`} {
		t.Run(body, func(t *testing.T) {
			i, _, raw, g := failoverFixture(t)
			thread := uuid.NewString()
			if w := failoverRequest(g, thread, `{"model":"auto","input":"hello"}`, ""); w.Code != 200 {
				t.Fatal(w.Code)
			}
			raw.execute = func(context.Context, *auth.Auth, executor.Request) (executor.Response, error) {
				return executor.Response{}, routerTestQuota()
			}
			w := failoverRequest(g, thread, body, "")
			if w.Code != 409 || !strings.Contains(w.Body.String(), "router_replay_required") || !reflect.DeepEqual(raw.routes(), []string{"a", "a"}) {
				t.Fatal(w.Code, w.Body.String(), raw.routes())
			}
			state, err := i.routerPins.read()
			if err != nil || state.Pins[0].RouteID != "a" {
				t.Fatal("fragment moved pin", state, err)
			}
		})
	}
}

func TestRouterCooldownCannotSuppressRefreshedCredential(t *testing.T) {
	i, manager, raw, g := failoverFixture(t)
	raw.execute = func(ctx context.Context, a *auth.Auth, _ executor.Request) (executor.Response, error) {
		selected, _ := routeSelection(ctx)
		if selected.Route.ID == "a" {
			// A refresh may finish after old material was sent but before routing sees
			// its rejection. Only the credential used by that attempt may cool down.
			updated := a.Clone()
			updated.Attributes["api_key"] = "rotated-fixture-value"
			if _, err := manager.Update(auth.WithSkipPersist(context.Background()), updated); err != nil {
				t.Error(err)
			}
			return executor.Response{}, routerTestQuota()
		}
		return executor.Response{Payload: []byte(`{"output":[]}`)}, nil
	}
	w := failoverRequest(g, uuid.NewString(), `{"model":"auto","input":"hello"}`, "")
	if w.Code != 200 {
		t.Fatal(w.Code, w.Body.String())
	}
	if i.routerMemberBlocked(i.Bindings.Routers["auto"], i.Bindings.Routes["a"], manager) {
		t.Fatal("old-key rejection suppressed refreshed credential")
	}
}
