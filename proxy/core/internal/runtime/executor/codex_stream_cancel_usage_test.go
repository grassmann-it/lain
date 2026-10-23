package executor

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"net/http/httptest"
	"testing"
	"time"

	coreusage "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/usage"
)

// Exercise the actual HTTP async worker, not just its reporter helper. In the
// terminal case, stop reading downstream after the HTTP handoff so that terminal
// usage is published before a canceled output send runs the worker's defer.
func TestCodexHTTPAsyncCancelUsage(t *testing.T) {
	for _, completed := range []bool{false, true} {
		t.Run(fmt.Sprintf("terminal_usage_%t", completed), func(t *testing.T) {
			alias := t.Name()
			capture := &codexResponseModelUsageCapture{alias: alias, records: make(chan coreusage.Record, 8)}
			coreusage.RegisterNamedPlugin(t.Name(), capture)
			t.Cleanup(func() { coreusage.RegisterNamedPlugin(t.Name(), codexResponseModelNoopUsagePlugin{}) })
			release := make(chan struct{})
			server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				w.Header().Set("Content-Type", "text/event-stream")
				if !completed {
					fmt.Fprintf(w, "data: %s\n\n", codexCreatedEvent)
				}
				w.(http.Flusher).Flush()
				select {
				case <-release:
				case <-r.Context().Done():
					return
				}
				if completed {
					fmt.Fprintf(w, "data: %s\n\n", codexCompletedEventBody)
					w.(http.Flusher).Flush()
				}
				<-r.Context().Done()
			}))
			defer server.Close()
			parent := "11111111-2222-4333-8444-555555555555"
			ctx, cancel := context.WithCancel(coreusage.WithParentRequestID(coreusage.WithRequestedModelAlias(context.Background(), alias), parent))
			defer cancel()
			auth := codexTestAuth(server.URL)
			authBefore, _ := json.Marshal(auth)
			req, opts := codexTestRequest()
			payloadBefore := bytes.Clone(req.Payload)
			result, err := NewCodexExecutor(codexBufferingConfig(false)).ExecuteStream(ctx, auth, req, opts)
			if err != nil {
				t.Fatal(err)
			}
			if !completed {
				select {
				case chunk, ok := <-result.Chunks:
					if !ok || chunk.Err != nil || len(chunk.Payload) == 0 {
						t.Fatal("missing post-handoff payload")
					}
				case <-time.After(5 * time.Second):
					t.Fatal("stream did not hand off")
				}
			}

			var record coreusage.Record
			if completed {
				close(release)
				record = capture.await(t) // completed usage exists while output is blocked
				cancel()
			} else {
				cancel() // scanner read/output loop must exit, reporting metadata only
			}
			done := make(chan struct{})
			go func() {
				for range result.Chunks {
				}
				close(done)
			}()
			select {
			case <-done:
			case <-time.After(5 * time.Second):
				t.Fatal("cancel changed stream termination")
			}
			if !completed {
				record = capture.await(t)
			}
			if record.RequestID == "" || record.RequestID == parent || record.ParentRequestID != parent {
				t.Fatal("execution UUID/parent missing or collapsed")
			}
			if completed {
				if record.Failed || record.Outcome == "canceled" || (record.UsageReported != nil && !*record.UsageReported) || record.Detail.TotalTokens != 2 {
					t.Fatalf("late cancel replaced terminal usage: %+v", record)
				}
			} else {
				if !record.Failed || record.Outcome != "canceled" || record.UsageReported == nil || *record.UsageReported {
					t.Fatalf("missing canceled/unknown contract: %+v", record)
				}
				if record.Fail != (coreusage.Failure{}) || record.Detail != (coreusage.Detail{}) {
					t.Fatal("cancel fabricated failure or normalized usage")
				}
			}
			// The dispatch queue is FIFO. A sentinel proves there is no second record
			// after the worker has exited, without a timing-based absence assertion.
			const sentinel = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee"
			coreusage.PublishRecord(ctx, coreusage.Record{RequestID: sentinel, Alias: alias})
			if next := capture.await(t); next.RequestID != sentinel {
				t.Fatal("duplicate completion/cancellation record")
			}
			authAfter, _ := json.Marshal(auth)
			if !bytes.Equal(authBefore, authAfter) || !bytes.Equal(payloadBefore, req.Payload) {
				t.Fatal("observer changed credential or model payload")
			}
		})
	}
}
