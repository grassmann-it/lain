package lain

import (
	"bytes"
	"context"
	"errors"
	"strings"
	"testing"

	auth "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/auth"
	executor "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/executor"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/usage"
)

type streamFixture struct {
	fixtureExecutor
	chunks       []executor.StreamChunk
	ctx          context.Context
	beforeReturn func()
}

func (f *streamFixture) ExecuteStream(ctx context.Context, _ *auth.Auth, _ executor.Request, _ executor.Options) (*executor.StreamResult, error) {
	f.ctx = ctx
	if f.beforeReturn != nil {
		f.beforeReturn()
	}
	out := make(chan executor.StreamChunk, len(f.chunks))
	for _, chunk := range f.chunks {
		out <- chunk
	}
	close(out)
	return &executor.StreamResult{Chunks: out}, nil
}
func TestTerminalMarkerThenUpstreamErrorCannotEscapeAsSuccess(t *testing.T) {
	for _, kind := range []string{"response.completed", "response.incomplete", "message_stop"} {
		t.Run(kind, func(t *testing.T) {
			i, ctx, a := dispatchFixture(t)
			raw := &streamFixture{chunks: []executor.StreamChunk{{Payload: []byte(`data: {"type":"` + kind + `"}`)}, {Err: errors.New("upstream failed after marker")}}}
			result, err := i.ExecutorDecorator(nil)(raw).ExecuteStream(ctx, a, executor.Request{Model: "model"}, executor.Options{})
			if err != nil {
				t.Fatal(err)
			}
			errorsSeen := 0
			for chunk := range result.Chunks {
				if len(chunk.Payload) > 0 {
					t.Fatal("success marker escaped after terminal failure")
				}
				if chunk.Err != nil {
					errorsSeen++
				}
			}
			page, _ := i.Journal.Read(0, 10)
			if errorsSeen != 1 || len(page.Events) != 2 || *page.Events[1].Outcome != "failed" {
				t.Fatal("terminal failure was not durably preserved", errorsSeen, page)
			}
		})
	}
}
func TestTerminalBufferLimitsCancelAndDrain(t *testing.T) {
	for _, bytesLimit := range []bool{false, true} {
		i, ctx, a := dispatchFixture(t)
		raw := &streamFixture{}
		if bytesLimit {
			raw.chunks = []executor.StreamChunk{{Payload: append([]byte("data: [DONE]\n"), bytes.Repeat([]byte("x"), maxTerminalBytes)...)}}
		} else {
			raw.chunks = append(raw.chunks, executor.StreamChunk{Payload: []byte("data: [DONE]\n")})
			for n := 0; n < maxTerminalChunks; n++ {
				raw.chunks = append(raw.chunks, executor.StreamChunk{Payload: []byte("trailing")})
			}
		}
		result, err := i.ExecutorDecorator(nil)(raw).ExecuteStream(ctx, a, executor.Request{Model: "model"}, executor.Options{})
		if err != nil {
			t.Fatal(err)
		}
		seen := false
		for chunk := range result.Chunks {
			if len(chunk.Payload) > 0 {
				t.Fatal("overflowed terminal buffer released content")
			}
			seen = chunk.Err != nil && strings.Contains(chunk.Err.Error(), "buffer limit")
		}
		if !seen || raw.ctx.Err() == nil {
			t.Fatal("bounded terminal abort did not cancel provider")
		}
	}
}

func TestChatFinishIsHeldUntilDurableEnd(t *testing.T) {
	if !terminalChunk([]byte(`data: {"choices":[{"index":0,"finish_reason":"stop"}]}`)) {
		t.Fatal("Chat success marker escaped durability gate")
	}
	i, ctx, a := dispatchFixture(t)
	raw := &streamFixture{chunks: []executor.StreamChunk{{Payload: []byte(`data: {"choices":[{"index":0,"finish_reason":"stop"}]}`)}, {Err: errors.New("late failure")}}}
	result, err := i.ExecutorDecorator(nil)(raw).ExecuteStream(ctx, a, executor.Request{Model: "model"}, executor.Options{})
	if err != nil {
		t.Fatal(err)
	}
	for chunk := range result.Chunks {
		if len(chunk.Payload) > 0 || chunk.Err == nil {
			t.Fatal("Chat successful completion released before durable error")
		}
	}
}

func TestIncompleteCompletionCannotPrecedeTerminalJournalWrite(t *testing.T) {
	i, ctx, a := dispatchFixture(t)
	raw := &streamFixture{chunks: []executor.StreamChunk{{Payload: []byte(`data: {"type":"response.incomplete"}`)}}, beforeReturn: func() { i.Journal.syncFile = func() error { return errors.New("terminal disk failure") } }}
	result, err := i.ExecutorDecorator(nil)(raw).ExecuteStream(ctx, a, executor.Request{Model: "model"}, executor.Options{})
	if err != nil {
		t.Fatal(err)
	}
	errorsSeen := 0
	for chunk := range result.Chunks {
		if len(chunk.Payload) > 0 {
			t.Fatal("incomplete terminal escaped failed journal")
		}
		if chunk.Err != nil {
			errorsSeen++
		}
	}
	if errorsSeen != 1 || i.Journal.Health() == nil {
		t.Fatal("terminal journal error was lost")
	}
}

type cancelUsageFixture struct{ fixtureExecutor }

func (f *cancelUsageFixture) ExecuteStream(ctx context.Context, a *auth.Auth, _ executor.Request, _ executor.Options) (*executor.StreamResult, error) {
	out := make(chan executor.StreamChunk)
	go func() {
		defer close(out)
		out <- executor.StreamChunk{Payload: []byte("data: [DONE]\n")}
		for n := 0; n < maxTerminalChunks; n++ {
			out <- executor.StreamChunk{Payload: []byte("trailing")}
		}
		<-ctx.Done()
		usage.PublishRecord(ctx, usage.Record{RequestID: usage.ExecutionRequestIDFromContext(ctx), AuthID: a.ID, Failed: true})
	}()
	return &executor.StreamResult{Chunks: out}, nil
}
func TestLocalBufferAbortIsNotReportedAsClientCancellation(t *testing.T) {
	i, ctx, a := dispatchFixture(t)
	result, err := i.ExecutorDecorator(nil)(&cancelUsageFixture{}).ExecuteStream(ctx, a, executor.Request{Model: "model"}, executor.Options{})
	if err != nil {
		t.Fatal(err)
	}
	for range result.Chunks {
	}
	page, _ := i.Journal.Read(0, 10)
	if len(page.Events) != 3 || page.Events[1].Outcome == nil || *page.Events[1].Outcome != "failed" || page.Events[2].CompletionState == nil || *page.Events[2].CompletionState != "failed" {
		t.Fatal("local abort misclassified as client cancellation", page)
	}
}
