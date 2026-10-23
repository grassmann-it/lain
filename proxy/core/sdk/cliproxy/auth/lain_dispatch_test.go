package auth

import (
	"context"
	"errors"
	executor "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/executor"
	"github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/usage"
	"testing"
)

type lainStreamFixture struct {
	ProviderExecutor
	called bool
}

func (e *lainStreamFixture) ExecuteStream(context.Context, *Auth, executor.Request, executor.Options) (*executor.StreamResult, error) {
	e.called = true
	c := make(chan executor.StreamChunk, 2)
	c <- executor.StreamChunk{Payload: []byte("data: {\"choices\":[{\"delta\":{\"content\":\"token\"}}]}\n\n")}
	c <- executor.StreamChunk{Payload: []byte("data: [DONE]\n\n")}
	close(c)
	return &executor.StreamResult{Chunks: c}, nil
}
func TestLainStreamDoesNotAcknowledgeCompletionBeforeDurability(t *testing.T) {
	before, after := LainBeforeDispatch, LainAfterDispatch
	defer func() { LainBeforeDispatch = before; LainAfterDispatch = after }()
	LainBeforeDispatch = func(context.Context, *Auth, executor.Request, executor.Options) error { return nil }
	LainAfterDispatch = func(context.Context, error) error { return errors.New("disk sync failed") }
	e := &lainStreamFixture{}
	result, err := lainExecuteStream(e, context.Background(), &Auth{}, executor.Request{}, executor.Options{})
	if err != nil {
		t.Fatal(err)
	}
	count := 0
	failed := false
	for chunk := range result.Chunks {
		count++
		if lainTerminalChunk(chunk.Payload) {
			t.Fatal("completion marker escaped durability gate")
		}
		if chunk.Err != nil {
			failed = true
		}
	}
	if !e.called || !failed || count != 2 {
		t.Fatal(e.called, failed, count)
	}
}
func TestLainBeforeDispatchFailureDoesNotCallProvider(t *testing.T) {
	original := LainBeforeDispatch
	defer func() { LainBeforeDispatch = original }()
	LainBeforeDispatch = func(context.Context, *Auth, executor.Request, executor.Options) error {
		return errors.New("cannot persist attempt")
	}
	e := &lainStreamFixture{}
	if _, err := lainExecuteStream(e, context.Background(), &Auth{}, executor.Request{}, executor.Options{}); err == nil || e.called {
		t.Fatal("provider called without durable attempt")
	}
}

func TestLainObservationUsesExactAttemptAndPrecedesFinalization(t *testing.T) {
	before, after, observe := LainBeforeDispatch, LainAfterDispatch, LainObserveOutput
	defer func() { LainBeforeDispatch, LainAfterDispatch, LainObserveOutput = before, after, observe }()
	attemptID := ""
	observed, finished := 0, false
	LainBeforeDispatch = func(ctx context.Context, _ *Auth, _ executor.Request, _ executor.Options) error {
		attemptID = usage.ExecutionRequestIDFromContext(ctx)
		if attemptID == "" || usage.ParentRequestIDFromContext(ctx) != "handler-request" {
			t.Error("missing exact dispatch identity")
		}
		return nil
	}
	LainObserveOutput = func(ctx context.Context, _ []byte, stream bool) {
		if finished || !stream || usage.ExecutionRequestIDFromContext(ctx) != attemptID {
			t.Error("observation identity/order changed")
		}
		observed++
	}
	LainAfterDispatch = func(context.Context, error) error { finished = true; return nil }
	ctx := usage.WithParentRequestID(context.Background(), "handler-request")
	result, err := lainExecuteStream(&lainStreamFixture{}, ctx, &Auth{}, executor.Request{}, executor.Options{})
	if err != nil {
		t.Fatal(err)
	}
	for range result.Chunks {
	}
	if observed != 2 || !finished {
		t.Fatal(observed, finished)
	}
}

type lainRefreshFixture struct {
	ProviderExecutor
	calls int
}

func (e *lainRefreshFixture) Identifier() string { return "fixture" }
func (e *lainRefreshFixture) Refresh(_ context.Context, a *Auth) (*Auth, error) {
	e.calls++
	a.Metadata = map[string]any{"access_token": "fresh"}
	return a, nil
}
func TestLainRefreshOwnershipGateProtectsExternalCredentials(t *testing.T) {
	readonly, writable := LainReadOnlyCredentials, LainCredentialWritable
	defer func() { LainReadOnlyCredentials = readonly; LainCredentialWritable = writable }()
	LainReadOnlyCredentials = true
	LainCredentialWritable = func(a *Auth) bool { return a != nil && a.ID == "owned/own.json" && !a.Disabled }
	manager := NewManager(nil, nil, nil)
	e := &lainRefreshFixture{}
	manager.RegisterExecutor(e)
	for _, id := range []string{"external.json", "owned/own.json"} {
		if _, err := manager.Register(context.Background(), &Auth{ID: id, Provider: "fixture", Metadata: map[string]any{"access_token": "old", "refresh_token": "secret"}}); err != nil {
			t.Fatal(err)
		}
	}
	if _, err := manager.refreshAuthForRequest(context.Background(), "external.json", ""); err == nil || e.calls != 0 {
		t.Fatal("external refresh attempted")
	}
	if _, err := manager.refreshAuthForRequest(context.Background(), "owned/own.json", ""); err != nil || e.calls != 1 {
		t.Fatal("owned refresh unavailable", err, e.calls)
	}
}
