package lain

import (
	"context"
	"errors"
	"net/http"
	"testing"

	"github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy"
	auth "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/auth"
	executor "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/executor"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/usage"
)

type fixtureExecutor struct {
	executions, refreshes, preparations int
	during                              func(context.Context)
}

func (f *fixtureExecutor) Identifier() string { return "fixture" }
func (f *fixtureExecutor) Execute(ctx context.Context, a *auth.Auth, _ executor.Request, _ executor.Options) (executor.Response, error) {
	f.executions++
	if f.during != nil {
		f.during(ctx)
	}
	usage.PublishRecord(ctx, usage.Record{AuthID: a.ID, RequestID: usage.ExecutionRequestIDFromContext(ctx), Detail: usage.Detail{Reported: &usage.ReportedDetail{TotalTokens: ptr(int64(0))}}})
	return executor.Response{Payload: []byte(`{"choices":[]}`)}, nil
}
func (f *fixtureExecutor) ExecuteStream(context.Context, *auth.Auth, executor.Request, executor.Options) (*executor.StreamResult, error) {
	return nil, errors.New("unused fixture")
}
func (f *fixtureExecutor) CountTokens(ctx context.Context, a *auth.Auth, r executor.Request, o executor.Options) (executor.Response, error) {
	return f.Execute(ctx, a, r, o)
}
func (f *fixtureExecutor) Refresh(_ context.Context, a *auth.Auth) (*auth.Auth, error) {
	f.refreshes++
	return a, nil
}
func (f *fixtureExecutor) HttpRequest(context.Context, *auth.Auth, *http.Request) (*http.Response, error) {
	return nil, errors.New("unused fixture")
}
func (f *fixtureExecutor) ShouldPrepareRequestAuth(*auth.Auth) bool { return true }
func (f *fixtureExecutor) PrepareRequestAuth(_ context.Context, a *auth.Auth) (*auth.Auth, error) {
	f.preparations++
	return a, nil
}
func (f *fixtureExecutor) ForAPIKey() auth.ProviderExecutor { return f }

func TestEveryExecutorRegistrationAndAPIKeyViewIsBound(t *testing.T) {
	i, ctx, a := dispatchFixture(t)
	manager := auth.NewManager(nil, NewSelector(), nil)
	if err := manager.SetExecutorDecorator(i.ExecutorDecorator(func(*auth.Auth) bool { return false })); err != nil {
		t.Fatal(err)
	}
	for n := 0; n < 2; n++ {
		raw := &fixtureExecutor{during: func(context.Context) {
			page, err := i.Journal.Read(0, 100)
			if err != nil || len(page.Events) != n*3+1 || page.Events[len(page.Events)-1].Kind != "attempt_started" {
				t.Fatalf("dispatch without durable start: %#v %v", page, err)
			}
		}}
		manager.RegisterExecutor(raw)
		bound, _ := manager.Executor("fixture")
		bound = bound.(auth.APIKeyConfigExecutor).ForAPIKey()
		if _, err := bound.Execute(ctx, a, executor.Request{Model: "model"}, executor.Options{}); err != nil {
			t.Fatal(err)
		}
		if _, err := bound.Refresh(ctx, a); err != nil {
			t.Fatal(err)
		}
		if bound.(auth.RequestAuthPreparer).ShouldPrepareRequestAuth(a) {
			t.Fatal("external credential preparation allowed")
		}
		if _, err := bound.(auth.RequestAuthPreparer).PrepareRequestAuth(ctx, a); err != nil {
			t.Fatal(err)
		}
		if raw.refreshes != 0 || raw.preparations != 0 {
			t.Fatal("external lifecycle reached provider")
		}
	}
	if err := manager.SetExecutorDecorator(nil); err == nil {
		t.Fatal("late decorator replacement allowed")
	}
	page, _ := i.Journal.Read(0, 100)
	if len(page.Events) != 6 || page.Events[1].TotalTokens == nil || *page.Events[1].TotalTokens != 0 || page.Events[1].InputTokens != nil {
		t.Fatal("presence or completion contract lost", page)
	}
}
func TestOwnedExecutorLifecycleAndStrictSelection(t *testing.T) {
	i, ctx, a := dispatchFixture(t)
	raw := &fixtureExecutor{}
	bound := i.ExecutorDecorator(func(candidate *auth.Auth) bool { return candidate.ID == a.ID })(raw)
	if _, err := bound.Refresh(ctx, a); err != nil {
		t.Fatal(err)
	}
	if _, err := bound.(auth.RequestAuthPreparer).PrepareRequestAuth(ctx, a); err != nil {
		t.Fatal(err)
	}
	if raw.refreshes != 1 || raw.preparations != 1 {
		t.Fatal("owned lifecycle not delegated")
	}
	chosen, err := NewSelector().Pick(ctx, "fixture", "model", executor.Options{}, []*auth.Auth{{ID: "outside", Provider: "fixture"}, a})
	if err != nil || chosen.ID != a.ID {
		t.Fatal("binding selection failed", chosen, err)
	}
	if _, err := NewSelector().Pick(ctx, "fixture", "model", executor.Options{}, []*auth.Auth{{ID: "outside", Provider: "fixture"}}); err == nil {
		t.Fatal("strict route escaped its membership")
	}
}

func TestBoundLowerPriorityCredentialRemainsSelectable(t *testing.T) {
	i, ctx, a := dispatchFixture(t)
	manager := auth.NewManager(nil, NewSelector(), nil)
	if err := manager.SetExecutorDecorator(i.ExecutorDecorator(func(*auth.Auth) bool { return false })); err != nil {
		t.Fatal(err)
	}
	manager.RegisterExecutor(&fixtureExecutor{})
	registry := cliproxy.GlobalModelRegistry()
	for _, credential := range []*auth.Auth{a, {ID: "outside-high-priority", Provider: "fixture", Attributes: map[string]string{"priority": "100"}}} {
		if _, err := manager.Register(auth.WithSkipPersist(context.Background()), credential); err != nil {
			t.Fatal(err)
		}
		registry.RegisterClient(credential.ID, "fixture", []*cliproxy.ModelInfo{{ID: "model", Object: "model"}})
		defer registry.UnregisterClient(credential.ID)
	}
	chosen, err := manager.SelectAuth(ctx, "fixture", "model", executor.Options{})
	if err != nil || chosen == nil || chosen.ID != a.ID {
		t.Fatalf("outside priority hid bound account: %v %#v", err, chosen)
	}
}
func (f *fixtureExecutor) SupportsApplyPatch() bool                         { return true }
func (f *fixtureExecutor) SupportsNativeToolSearch(*auth.Auth, string) bool { return true }
func TestExecutorCapabilitiesSurviveWrapping(t *testing.T) {
	i, _, a := dispatchFixture(t)
	bound := i.ExecutorDecorator(nil)(&fixtureExecutor{})
	for _, executor := range []auth.ProviderExecutor{bound, bound.(auth.APIKeyConfigExecutor).ForAPIKey()} {
		if !executor.(auth.ApplyPatchSupport).SupportsApplyPatch() || !executor.(auth.NativeToolSearchSupport).SupportsNativeToolSearch(a, "model") {
			t.Fatal("upstream capability hidden by adapter")
		}
	}
}
