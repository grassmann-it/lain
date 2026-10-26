package lain

import (
	"context"
	"reflect"
	"testing"
	"time"

	auth "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/auth"
	executor "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/executor"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/usage"
)

// Deliberately never closes its channel in response to cancellation. No producer
// goroutine is needed: the test owns sends and releases the channel in cleanup.
type openCancelStreamFixture struct {
	fixtureExecutor
	input    chan executor.StreamChunk
	ctx      context.Context
	reported bool
}

func (f *openCancelStreamFixture) ExecuteStream(ctx context.Context, a *auth.Auth, _ executor.Request, _ executor.Options) (*executor.StreamResult, error) {
	f.ctx = ctx
	if f.reported {
		usage.PublishRecord(ctx, usage.Record{AuthID: a.ID, RequestID: usage.ExecutionRequestIDFromContext(ctx), Detail: usage.Detail{Reported: &usage.ReportedDetail{TotalTokens: ptr(int64(3))}}})
	}
	return &executor.StreamResult{Chunks: f.input}, nil
}

func TestExecuteStreamCancelDoesNotWaitForInnerClose(t *testing.T) {
	for _, reported := range []bool{false, true} {
		for _, stage := range []string{"silent", "after-output", "held-terminal", "blocked-output"} {
			name := stage + "/unknown-usage"
			if reported {
				name = stage + "/reported-usage"
			}
			t.Run(name, func(t *testing.T) {
				i, parent, a := dispatchFixture(t)
				ctx, cancel := context.WithCancel(parent)
				defer cancel()
				raw := &openCancelStreamFixture{input: make(chan executor.StreamChunk), reported: reported}
				result, err := i.ExecutorDecorator(nil)(raw).ExecuteStream(ctx, a, executor.Request{Model: "model"}, executor.Options{})
				if err != nil {
					t.Fatal(err)
				}
				// Also releases the old implementation after a failing timeout, so
				// the regression test never leaves a wrapper using a closed journal.
				defer func() {
					cancel()
					close(raw.input)
					timer := time.NewTimer(time.Second)
					defer timer.Stop()
					for {
						select {
						case _, ok := <-result.Chunks:
							if !ok {
								return
							}
						case <-timer.C:
							t.Error("wrapper did not exit during cleanup")
							return
						}
					}
				}()
				if stage != "silent" {
					payload := []byte(`data: {"choices":[{"delta":{"content":"fixture"}}]}`)
					if stage == "held-terminal" {
						payload = []byte("data: [DONE]\n")
					}
					select {
					case raw.input <- executor.StreamChunk{Payload: payload}:
					case <-time.After(time.Second):
						t.Fatal("wrapper did not receive fixture chunk")
					}
					if stage == "after-output" {
						select {
						case chunk := <-result.Chunks:
							if chunk.Err != nil || string(chunk.Payload) != string(payload) {
								t.Fatal("first output changed", chunk)
							}
						case <-time.After(time.Second):
							t.Fatal("wrapper did not forward first output")
						}
					}
				}
				before, err := i.Journal.Read(0, 10)
				if err != nil {
					t.Fatal(err)
				}
				cancel()
				// Do not consume output until finalization: a canceled wrapper must
				// neither wait for the producer nor require a downstream reader.
				deadline := time.After(time.Second)
				ticker := time.NewTicker(time.Millisecond)
				defer ticker.Stop()
				for {
					i.mu.Lock()
					active := len(i.active)
					i.mu.Unlock()
					if active == 0 {
						break
					}
					select {
					case <-ticker.C:
					case <-deadline:
						t.Fatal("cancellation left attempt active while inner channel remained open")
					}
				}
				select {
				case chunk, ok := <-result.Chunks:
					if ok {
						t.Fatal("payload or terminal error escaped cancellation", chunk)
					}
				case <-time.After(time.Second):
					t.Fatal("cancellation did not close downstream channel")
				}
				if raw.ctx.Err() != context.Canceled {
					t.Fatal("provider context was not canceled")
				}
				page, err := i.Journal.Read(0, 10)
				if err != nil || i.Journal.Health() != nil {
					t.Fatal("cancellation faulted journal", err)
				}
				expected := 2
				if reported {
					expected = 3
				}
				if len(page.Events) != expected || !reflect.DeepEqual(page.Events[:len(before.Events)], before.Events) {
					t.Fatal("cancellation duplicated or rewrote durable facts", page)
				}
				final := page.Events[len(page.Events)-1]
				start := page.Events[0]
				if final.CompletionState == nil || *final.CompletionState != "canceled" || final.SourceID != start.SourceID || final.AttemptID != start.AttemptID || final.RequestID != start.RequestID || final.AccountID != start.AccountID || final.CredentialID != start.CredentialID || !reflect.DeepEqual(final.RouteID, start.RouteID) {
					t.Fatal("canceled completion lost state or identity", page)
				}
				if reported {
					if page.Events[1].TotalTokens == nil || *page.Events[1].TotalTokens != 3 || final.Kind != "attempt_finished" || final.Outcome != nil || final.TotalTokens != nil {
						t.Fatal("reported usage was not preserved with one metadata completion", page)
					}
				} else if final.Kind != "usage" || final.Outcome == nil || *final.Outcome != "canceled" || final.UsageReported || final.TotalTokens != nil || final.InputTokens != nil || final.OutputTokens != nil {
					t.Fatal("unknown cancellation invented usage", page)
				}
				// A producer publishing after cancellation must not resurrect the
				// closed attempt, rewrite consumption, or fault the journal.
				usage.PublishRecord(raw.ctx, usage.Record{AuthID: a.ID, RequestID: usage.ExecutionRequestIDFromContext(raw.ctx), Detail: usage.Detail{Reported: &usage.ReportedDetail{TotalTokens: ptr(int64(99))}}})
				after, err := i.Journal.Read(0, 10)
				if err != nil || !reflect.DeepEqual(page, after) || i.Journal.Health() != nil {
					t.Fatal("late provider usage changed finalized attempt", after, err)
				}
			})
		}
	}
}
