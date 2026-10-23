package auth

import (
	"context"
	"errors"
	executor "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/executor"
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
