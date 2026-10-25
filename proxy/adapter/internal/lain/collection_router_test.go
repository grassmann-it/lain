package lain

import (
	"context"
	"errors"
	"io"
	"net/http"
	"net/http/httptest"
	"os"
	"strings"
	"sync"
	"testing"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy"
	auth "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/auth"
	executor "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/executor"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/usage"
)

func routerFixture(t *testing.T) (*Integration, *auth.Manager, CollectionRouter) {
	t.Helper()
	credentialID := uuid.NewString()
	router := CollectionRouter{ID: "router", ProfileID: "profile", Selection: "ordered_sticky_v1", CostPolicy: "configured",
		RouteIDs: []string{"missing", "ready"}, MemberFingerprints: map[string]string{"missing": strings.Repeat("a", 64), "ready": strings.Repeat("b", 64)}}
	bindings := Bindings{Accounts: map[string]string{"missing-account": "absent", "ready-account": credentialID},
		Routes: map[string]Route{
			"missing-alias": {ID: "missing", Model: "model", Accounts: []string{"missing-account"}, Selection: "strict", UpstreamProtocol: "openai"},
			"ready-alias":   {ID: "ready", Model: "model", Accounts: []string{"ready-account"}, Selection: "strict", UpstreamProtocol: "openai"}},
		Routers: map[string]CollectionRouter{"auto": router}}
	integration, err := New(testJournal(t), bindings)
	if err != nil {
		t.Fatal(err)
	}
	if integration.routerPinError != nil {
		t.Fatal(integration.routerPinError)
	}
	manager := auth.NewManager(nil, NewSelector(), nil)
	credential := &auth.Auth{ID: credentialID, Provider: "fixture"}
	if _, err := manager.Register(auth.WithSkipPersist(context.Background()), credential); err != nil {
		t.Fatal(err)
	}
	registry := cliproxy.GlobalModelRegistry()
	registry.RegisterClient(credentialID, "fixture", []*cliproxy.ModelInfo{{ID: "model", Object: "model"}})
	t.Cleanup(func() { registry.UnregisterClient(credentialID) })
	return integration, manager, router
}

func TestCollectionRouterOrderedPinnedAndNoReassignment(t *testing.T) {
	i, manager, router := routerFixture(t)
	headers := http.Header{"Thread-Id": []string{uuid.NewString()}}
	body := []byte(`{"model":"auto","input":"hello","store":false}`)
	route, err := i.routerRoute(router, "auto", body, headers, manager)
	if err != nil || route.ID != "ready" {
		t.Fatalf("ordered availability: %#v %v", route, err)
	}
	// Restart reopens the exact durable pin; order changes never move it.
	i.routerPins, err = openRouterPins(i.Journal, i.Bindings.Routers)
	if err != nil {
		t.Fatal(err)
	}
	router.RouteIDs = []string{"ready", "missing"}
	continuation := []byte(`{"model":"auto","input":[{"type":"function_call_output","call_id":"call_1","output":"kept opaque"}],"store":false}`)
	route, err = i.routerRoute(router, "auto", continuation, headers, manager)
	if err != nil || route.ID != "ready" {
		t.Fatal("continuation lost pin", err)
	}
	router.MemberFingerprints["ready"] = strings.Repeat("c", 64)
	if _, err = i.routerRoute(router, "auto", continuation, headers, manager); err == nil {
		t.Fatal("changed member silently reselected")
	}
	headers.Set("Thread-Id", uuid.NewString())
	if _, err = i.routerRoute(router, "auto", continuation, headers, manager); err == nil {
		t.Fatal("unpinned continuation accepted")
	}
}

func TestCollectionRouterAuthBeforePinAndSingleDispatch(t *testing.T) {
	i, manager, _ := routerFixture(t)
	before, err := os.ReadFile(i.routerPins.path)
	if err != nil {
		t.Fatal(err)
	}
	g := gin.New()
	g.Use(i.Middleware("management-token", manager, "inference-token"))
	calls := 0
	g.POST("/v1/responses", func(c *gin.Context) {
		calls++
		selected := c.MustGet("lain.selection").(selection)
		if selected.Route.ID != "ready" || selected.Alias != "auto" {
			t.Error("wire/real identity lost")
		}
		// A started output and terminal error must never invoke another member.
		c.Writer.WriteHeader(200)
		_, _ = c.Writer.WriteString("data: partial\n\n")
		c.Abort()
	})
	for _, token := range []string{"management-token", "wrong-token", ""} {
		req := httptest.NewRequest("POST", "/v1/responses", strings.NewReader(`{"model":"auto","input":"hello"}`))
		req.Header.Set("Authorization", "Bearer "+token)
		req.Header.Set("Thread-Id", uuid.NewString())
		w := httptest.NewRecorder()
		g.ServeHTTP(w, req)
		if w.Code != 401 {
			t.Fatal("unauthorized router", w.Code)
		}
	}
	after, _ := os.ReadFile(i.routerPins.path)
	if string(before) != string(after) || calls != 0 {
		t.Fatal("unauthorized request changed pins")
	}
	req := httptest.NewRequest("POST", "/v1/responses", strings.NewReader(`{"model":"auto","input":"hello"}`))
	req.Header.Set("Authorization", "Bearer inference-token")
	req.Header.Set("Thread-Id", uuid.NewString())
	g.ServeHTTP(httptest.NewRecorder(), req)
	if calls != 1 {
		t.Fatal("router must dispatch exactly once", calls)
	}
}

func TestRouterPinConcurrencyMissingAndCorruptState(t *testing.T) {
	i, _, router := routerFixture(t)
	thread := uuid.NewString()
	chosen := 0
	var wg sync.WaitGroup
	for n := 0; n < 20; n++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			_, err := i.routerPins.resolve(router, thread, false, func() (routerPin, error) {
				chosen++
				return routerPin{RouteID: "ready", AccountID: "ready-account", Fingerprint: strings.Repeat("b", 64)}, nil
			}, func(routerPin) error { return nil })
			if err != nil {
				t.Error(err)
			}
		}()
	}
	wg.Wait()
	if chosen != 1 {
		t.Fatal("concurrent first requests selected more than once", chosen)
	}
	if err := os.Remove(i.routerPins.path); err != nil {
		t.Fatal(err)
	}
	if _, err := openRouterPins(i.Journal, i.Bindings.Routers); err == nil {
		t.Fatal("missing pins were recreated")
	}
	if err := os.WriteFile(i.routerPins.path, []byte(`{"schemaVersion":1,"sourceId":"wrong","pins":[]}`), 0600); err != nil {
		t.Fatal(err)
	}
	if _, err := openRouterPins(i.Journal, i.Bindings.Routers); err == nil {
		t.Fatal("foreign source pins accepted")
	}
}

func TestCollectionRouterRejectsFreeUnknownPoolAndStoredState(t *testing.T) {
	i, _, router := routerFixture(t)
	router.CostPolicy = "free_only"
	i.Bindings.Routers["auto"] = router
	if validateRouters(i.Bindings) == nil {
		t.Fatal("configured or unknown-priced route accepted as free")
	}
	headers := http.Header{"Thread-Id": []string{uuid.NewString()}}
	for _, body := range []string{`{"input":"hello","store":true}`, `{"input":"hello","previous_response_id":null}`, `{"input":[{"type":"item_reference","id":"prior"}]}`} {
		if _, _, err := routerRequest([]byte(body), headers); err == nil {
			t.Fatal("stored state accepted", body)
		}
	}
	_, err := i.routerPins.resolve(router, uuid.NewString(), false, func() (routerPin, error) { return routerPin{}, errors.New("no candidate") }, func(routerPin) error { return nil })
	if err == nil {
		t.Fatal("no candidate did not fail closed")
	}
}

func TestRouterFinalModelAndJournalRetainRealIdentity(t *testing.T) {
	i, manager, _ := routerFixture(t)
	credential := manager.List()[0]
	c := &gin.Context{}
	c.Set("lain.selection", selection{Route: i.Bindings.Routes["ready-alias"], Alias: "auto", Allowed: map[string]bool{credential.ID: true}})
	ctx := context.WithValue(context.Background(), "gin", c)
	ctx = WithParentRequestID(ctx, "request-router")
	ctx = usage.WithExecutionRequestID(ctx, "attempt-router")
	for _, model := range []string{"model", "different-model"} {
		req := httptest.NewRequest("POST", "https://fixture.invalid/v1/chat/completions", strings.NewReader(`{"model":"`+model+`"}`)).WithContext(ctx)
		// The actual SDK creates replayable final outgoing requests.
		req.GetBody = func() (io.ReadCloser, error) {
			return io.NopCloser(strings.NewReader(`{"model":"` + model + `"}`)), nil
		}
		err := i.validateRouterFinal(req)
		if (err == nil) != (model == "model") {
			t.Fatal("final model pin mismatch", model, err)
		}
	}
	if err := i.Before(ctx, credential, executor.Request{Model: "model"}, executor.Options{}); err != nil {
		t.Fatal(err)
	}
	if err := i.After(ctx, nil); err != nil {
		t.Fatal(err)
	}
	batch, err := i.Journal.Read(0, 1000)
	if err != nil {
		t.Fatal(err)
	}
	for _, event := range batch.Events {
		if event.RequestID != "request-router" || event.AttemptID != "attempt-router" || event.RequestedModel != "auto" || event.RouteID == nil || *event.RouteID != "ready" || event.AccountID != "ready-account" {
			t.Fatalf("router rewrote durable real identities: %#v", event)
		}
	}
}

func TestRouterPinFailureDoesNotChangeIndividualRoutes(t *testing.T) {
	i, manager, _ := routerFixture(t)
	if err := os.WriteFile(i.routerPins.path, []byte("broken fixture"), 0600); err != nil {
		t.Fatal(err)
	}
	withRouters, err := New(i.Journal, i.Bindings)
	if err != nil || withRouters.routerPinError == nil {
		t.Fatal("corrupt router pins should only disable routers", err)
	}
	noRouters := i.Bindings
	noRouters.Routers = nil
	direct, err := New(i.Journal, noRouters)
	if err != nil || direct.routerPins != nil || direct.routerPinError != nil {
		t.Fatal("manual runtime inspected unrelated pin state", err)
	}
	for _, integration := range []*Integration{withRouters, direct} {
		g := gin.New()
		g.Use(integration.Middleware("management-token", manager, "inference-token"))
		g.POST("/v1/responses", func(c *gin.Context) { c.Status(204) })
		req := httptest.NewRequest("POST", "/v1/responses", strings.NewReader(`{"model":"ready-alias","input":"hello"}`))
		req.Header.Set("Authorization", "Bearer inference-token")
		w := httptest.NewRecorder()
		g.ServeHTTP(w, req)
		if w.Code != 204 {
			t.Fatal("manual alias was blocked by router state", w.Code)
		}
	}
}
