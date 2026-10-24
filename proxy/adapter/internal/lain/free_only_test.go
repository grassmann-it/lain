package lain

import (
	"context"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/gin-gonic/gin"
	"lain.local/proxy/internal/freeonly"

	auth "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/auth"
	executor "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/executor"
)

func qualifiedFreeRoute() Route {
	return Route{ID: "route", Model: "qwen/qwen3-coder:free", Accounts: []string{"stable-account"}, Selection: "strict", Provider: "openrouter", CostPolicy: "free_only", PricingClass: "free", FreeOnlyEnforcement: freeonly.OpenRouterZeroCapV1}
}

func freeDispatchFixture(t *testing.T) (*Integration, context.Context, *auth.Auth) {
	i, ctx, a := dispatchFixture(t)
	route := qualifiedFreeRoute()
	i.Bindings.Routes["alias"] = route
	ctx.Value("gin").(*gin.Context).Set("lain.selection", selection{Route: route, Alias: "alias", Allowed: map[string]bool{a.ID: true}})
	a.Provider = "openai-compatible-openrouter-openai-chat"
	a.Attributes = map[string]string{"compat_name": "openrouter-openai-chat", "upstream_protocol": "openai-chat", "base_url": "https://openrouter.ai/api/v1", "api_key": "synthetic-never-transmitted"}
	return i, ctx, a
}

func TestFreeOnlyStartupRequiresExactQualifiedBinding(t *testing.T) {
	route := qualifiedFreeRoute()
	bindings := Bindings{Accounts: map[string]string{"stable-account": "credential-v2"}, Routes: map[string]Route{"alias": route}}
	if _, err := New(testJournal(t), bindings); err != nil {
		t.Fatal(err)
	}
	for _, mutate := range []func(*Route){
		func(r *Route) { r.Provider = "poe" }, func(r *Route) { r.PricingClass = "unknown" },
		func(r *Route) { r.FreeOnlyEnforcement = "" }, func(r *Route) { r.FreeOnlyEnforcement = "claimed-model-pin" },
		func(r *Route) { r.Model = "openrouter/free" }, func(r *Route) { r.Model = "arbitrary/model:free" },
	} {
		bad := route
		mutate(&bad)
		bindings.Routes["alias"] = bad
		if _, err := New(testJournal(t), bindings); err == nil {
			t.Fatal("unqualified binding accepted", bad)
		}
	}
}

func TestFreeOnlyContextIsRequiredAndKeepsRequestIdentity(t *testing.T) {
	i, ctx, a := freeDispatchFixture(t)
	req := executor.Request{Model: qualifiedFreeRoute().Model}
	if err := i.Before(ctx, a, req, executor.Options{}); err == nil {
		t.Fatal("unmarked dispatch accepted")
	}
	batch, _ := i.Journal.Read(0, 100)
	if len(batch.Events) != 0 {
		t.Fatal("rejected route recorded provider attempt")
	}
	prepared, err := i.DispatchContext(ctx, a, req, executor.Options{})
	if err != nil {
		t.Fatal(err)
	}
	if model, ok := freeonly.OpenRouterPin(prepared); !ok || model != req.Model {
		t.Fatal("trusted pin absent")
	}
	if err := i.Before(prepared, a, req, executor.Options{}); err != nil {
		t.Fatal(err)
	}
	if err := i.After(prepared, nil); err != nil {
		t.Fatal(err)
	}
	batch, _ = i.Journal.Read(0, 100)
	if len(batch.Events) != 2 || batch.Events[0].RequestID != batch.Events[1].RequestID || batch.Events[0].AttemptID != batch.Events[1].AttemptID {
		t.Fatal(batch)
	}
}

func TestFreeOnlyWrongAuthOrTranslatedModelNeverGetsPin(t *testing.T) {
	for _, mutate := range []func(*auth.Auth, *executor.Request){
		func(a *auth.Auth, _ *executor.Request) { a.Provider = "openrouter" },
		func(a *auth.Auth, _ *executor.Request) { a.Attributes["compat_name"] = "another-provider" },
		func(a *auth.Auth, _ *executor.Request) { a.Attributes["upstream_protocol"] = "openai-responses" },
		func(a *auth.Auth, _ *executor.Request) { delete(a.Attributes, "upstream_protocol") },
		func(a *auth.Auth, _ *executor.Request) {
			a.Attributes["base_url"] = "https://openrouter.ai.evil/api/v1"
		},
		func(a *auth.Auth, _ *executor.Request) { a.Attributes["base_url"] = "http://openrouter.ai/api/v1" },
		func(_ *auth.Auth, r *executor.Request) { r.Model = "paid/model" },
	} {
		i, ctx, a := freeDispatchFixture(t)
		req := executor.Request{Model: qualifiedFreeRoute().Model}
		mutate(a, &req)
		prepared, err := i.DispatchContext(ctx, a, req, executor.Options{})
		if err == nil {
			t.Fatal("unqualified credential transport accepted")
		}
		if _, ok := freeonly.OpenRouterPin(prepared); ok {
			t.Fatal("rejected route got trusted pin")
		}
		batch, _ := i.Journal.Read(0, 100)
		if len(batch.Events) != 0 {
			t.Fatal("rejected route recorded attempt")
		}
	}
}

func TestFreeOnlyHTTPValidatesOriginalBodyBeforeModelRewrite(t *testing.T) {
	gin.SetMode(gin.TestMode)
	i, _, _ := freeDispatchFixture(t)
	g := gin.New()
	g.Use(i.Middleware("management", auth.NewManager(nil, nil, nil), "inference"))
	called := 0
	for _, path := range []string{"/v1/chat/completions", "/v1/responses", "/v1/responses/compact", "/v1/messages"} {
		g.POST(path, func(c *gin.Context) { called++; c.Status(200) })
	}
	for _, test := range []struct {
		path, body string
		want       int
	}{
		{"/v1/chat/completions", `{"model":"alias","messages":[{"role":"user","content":"hello"}]}`, 200},
		{"/v1/responses", `{"model":"alias","input":"hello"}`, 200},
		{"/v1/responses/compact", `{"model":"alias","input":"hello"}`, 400},
		{"/v1/messages", `{"model":"alias","messages":[]}`, 400},
		{"/v1/chat/completions", `{"model":"alias","models":["paid/model"]}`, 400},
		{"/v1/chat/completions", `{"model":"alias","provider":{"allow_fallbacks":true}}`, 400},
		{"/v1/chat/completions", `{"model":"paid/model","model":"alias"}`, 400},
		{"/v1/chat/completions", `{"model":"alias","plugins":[{"id":"web"}]}`, 400},
	} {
		req := httptest.NewRequest("POST", test.path, strings.NewReader(test.body))
		req.Header.Set("Authorization", "Bearer inference")
		rec := httptest.NewRecorder()
		g.ServeHTTP(rec, req)
		if rec.Code != test.want {
			t.Fatal(test.path, test.body, rec.Code, rec.Body.String())
		}
	}
	if called != 2 {
		t.Fatal("unsafe request reached handler", called)
	}
}
