package executor

import (
	"context"
	"fmt"
	"net/http"
	"net/http/httptest"
	"testing"
	"time"

	"github.com/gorilla/websocket"
	"github.com/router-for-me/CLIProxyAPI/v7/internal/config"
	auth "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/auth"
	core "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/executor"
	usage "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/usage"
	translator "github.com/router-for-me/CLIProxyAPI/v7/sdk/translator"
	"github.com/tidwall/gjson"
)

func TestCodexWSExecutionOwnershipAndCanceledUnknownOnce(t *testing.T) {
	for _, duplex := range []bool{false, true} {
		t.Run(map[bool]string{false: "legacy", true: "duplex"}[duplex], func(t *testing.T) {
			capture := &codexResponseModelUsageCapture{alias: t.Name(), records: make(chan usage.Record, 16)}
			usage.RegisterNamedPlugin(t.Name(), capture)
			t.Cleanup(func() { usage.RegisterNamedPlugin(t.Name(), codexResponseModelNoopUsagePlugin{}) })
			done := make(chan struct{})
			upstream := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				defer close(done)
				conn, err := (&websocket.Upgrader{}).Upgrade(w, r, nil)
				if err != nil {
					t.Error(err)
					return
				}
				defer conn.Close()
				for i := 1; i <= 3; i++ {
					if i == 1 || !duplex {
						if _, _, err = conn.ReadMessage(); err != nil {
							t.Error(err)
							return
						}
					}
					created := fmt.Sprintf(`{"type":"response.created","response":{"id":"resp_%d","output":[]}}`, i)
					if err = conn.WriteMessage(websocket.TextMessage, []byte(created)); err != nil {
						t.Error(err)
						return
					}
					if i < 3 {
						completed := fmt.Sprintf(`{"type":"response.completed","response":{"id":"resp_%d","output":[],"usage":{"input_tokens":%d,"output_tokens":%d,"total_tokens":%d}}}`, i, 100*i, 20*i, 120*i)
						if err = conn.WriteMessage(websocket.TextMessage, []byte(completed)); err != nil {
							t.Error(err)
							return
						}
					}
				}
				_, _, _ = conn.ReadMessage() // silent third response remains pending until disconnect
			}))
			defer upstream.Close()
			parent := "12345678-1234-4234-8234-123456789abc"
			ctx, cancel := context.WithCancel(usage.WithParentRequestID(usage.WithRequestedModelAlias(context.Background(), t.Name()), parent))
			defer cancel()
			ctx = core.WithDownstreamWebsocket(ctx)
			if duplex {
				ctx = core.WithWebsocketInput(ctx, make(chan core.WebsocketInput))
			}
			cfg := &config.Config{Codex: config.CodexConfig{ResponseSteering: duplex}}
			exec := NewCodexWebsocketsExecutor(cfg)
			exec.store = &codexWebsocketSessionStore{sessions: make(map[string]*codexWebsocketSession)}
			candidate := &auth.Auth{ID: t.Name(), Provider: "codex", Attributes: map[string]string{"api_key": "synthetic", "base_url": upstream.URL, "websockets": "true"}}
			req := core.Request{Model: "gpt-6-astra", Payload: []byte(`{"model":"gpt-6-astra","input":[]}`)}
			opts := core.Options{SourceFormat: translator.FromString("codex"), Metadata: map[string]any{core.ExecutionSessionMetadataKey: t.Name()}}
			ids := map[string]string{}
			var result *core.StreamResult
			for i := 1; i <= 3; i++ {
				if i == 1 || !duplex {
					var err error
					result, err = exec.ExecuteStream(ctx, candidate, req, opts)
					if err != nil {
						t.Fatal(err)
					}
				}
				want := fmt.Sprintf("resp_%d", i)
				terminal := false
				for !terminal {
					select {
					case chunk, open := <-result.Chunks:
						if !open || chunk.Err != nil {
							t.Fatalf("response lost: open=%v err=%v", open, chunk.Err)
						}
						if chunk.ExecutionRequestID == "" || chunk.ExecutionRequestID == parent || chunk.ExecutionStartedAt.IsZero() {
							t.Fatal("execution ownership missing")
						}
						if id := gjson.GetBytes(chunk.Payload, "response.id").String(); id != "" {
							if id != want {
								t.Fatalf("out of order response: %s", id)
							}
							if ids[id] != "" && ids[id] != chunk.ExecutionRequestID {
								t.Fatal("execution UUID changed within response")
							}
							ids[id] = chunk.ExecutionRequestID
						}
						kind := gjson.GetBytes(chunk.Payload, "type").String()
						terminal = kind == "response.completed" || (i == 3 && kind == "response.created")
					case <-time.After(5 * time.Second):
						t.Fatal("missing response")
					}
				}
				if i < 3 {
					record := capture.await(t)
					if record.RequestID != ids[want] || record.ParentRequestID != parent || record.Failed || record.Detail.TotalTokens != int64(120*i) {
						t.Fatalf("usage mismatch: %+v", record)
					}
					if !duplex {
						for range result.Chunks {
						}
					}
				}
			}
			if ids["resp_1"] == ids["resp_2"] || ids["resp_2"] == ids["resp_3"] {
				t.Fatal("responses share execution UUID")
			}
			cancel()
			drained := make(chan struct{})
			go func() {
				for range result.Chunks {
				}
				close(drained)
			}()
			select {
			case <-drained:
			case <-time.After(3 * time.Second):
				t.Fatal("cancel did not terminate stream")
			}
			record := capture.await(t)
			if record.RequestID != ids["resp_3"] || record.ParentRequestID != parent || record.Outcome != "canceled" || record.UsageReported == nil || *record.UsageReported || record.Detail != (usage.Detail{}) || record.Fail != (usage.Failure{}) {
				t.Fatalf("missing canceled unknown: %+v", record)
			}
			usage.PublishRecord(ctx, usage.Record{Alias: t.Name(), RequestID: "sentinel"})
			if capture.await(t).RequestID != "sentinel" {
				t.Fatal("socket close duplicated/overwrote completed execution")
			}
			exec.CloseExecutionSession(t.Name())
			select {
			case <-done:
			case <-time.After(3 * time.Second):
				t.Fatal("upstream did not close")
			}
		})
	}
}

func TestCodexDuplexCanceledCreateBeforeResponseCreated(t *testing.T) {
	capture := &codexResponseModelUsageCapture{alias: t.Name(), records: make(chan usage.Record, 8)}
	usage.RegisterNamedPlugin(t.Name(), capture)
	t.Cleanup(func() { usage.RegisterNamedPlugin(t.Name(), codexResponseModelNoopUsagePlugin{}) })
	submitted := make(chan struct{})
	done := make(chan struct{})
	upstream := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		defer close(done)
		conn, err := (&websocket.Upgrader{}).Upgrade(w, r, nil)
		if err != nil {
			t.Error(err)
			return
		}
		defer conn.Close()
		if _, _, err = conn.ReadMessage(); err != nil {
			t.Error(err)
			return
		}
		for _, frame := range []string{`{"type":"response.created","response":{"id":"resp_first","output":[]}}`, `{"type":"response.completed","response":{"id":"resp_first","output":[],"usage":{"total_tokens":120,"input_tokens":100,"output_tokens":20}}}`} {
			if err = conn.WriteMessage(websocket.TextMessage, []byte(frame)); err != nil {
				t.Error(err)
				return
			}
		}
		if _, _, err = conn.ReadMessage(); err != nil {
			t.Error(err)
			return
		}
		close(submitted)
		_, _, _ = conn.ReadMessage()
	}))
	defer upstream.Close()
	ctx, cancel := context.WithCancel(usage.WithRequestedModelAlias(context.Background(), t.Name()))
	defer cancel()
	input := make(chan core.WebsocketInput, 1)
	ctx = core.WithWebsocketInput(core.WithDownstreamWebsocket(ctx), input)
	exec := NewCodexWebsocketsExecutor(&config.Config{Codex: config.CodexConfig{ResponseSteering: true}})
	exec.store = &codexWebsocketSessionStore{sessions: make(map[string]*codexWebsocketSession)}
	candidate := &auth.Auth{ID: t.Name(), Provider: "codex", Attributes: map[string]string{"api_key": "synthetic", "base_url": upstream.URL, "websockets": "true"}}
	result, err := exec.ExecuteStream(ctx, candidate, core.Request{Model: "gpt-6-astra", Payload: []byte(`{"model":"gpt-6-astra","input":[]}`)}, core.Options{SourceFormat: translator.FromString("codex"), Metadata: map[string]any{core.ExecutionSessionMetadataKey: t.Name()}})
	if err != nil {
		t.Fatal(err)
	}
	for {
		select {
		case chunk, open := <-result.Chunks:
			if !open || chunk.Err != nil {
				t.Fatal("initial response lost")
			}
			if gjson.GetBytes(chunk.Payload, "type").String() == "response.completed" {
				goto completed
			}
		case <-time.After(5 * time.Second):
			t.Fatal("initial response stalled")
		}
	}
completed:
	first := capture.await(t)
	input <- core.WebsocketInput{Payload: []byte(`{"type":"response.create","input":[]}`)}
	select {
	case <-submitted:
	case <-time.After(3 * time.Second):
		t.Fatal("second create never submitted")
	}
	cancel()
	for range result.Chunks {
	}
	pending := capture.await(t)
	if pending.RequestID == first.RequestID || pending.Outcome != "canceled" || pending.UsageReported == nil || *pending.UsageReported || pending.Detail != (usage.Detail{}) {
		t.Fatalf("pending reporter lost or success overwritten: %+v", pending)
	}
	usage.PublishRecord(ctx, usage.Record{Alias: t.Name(), RequestID: "sentinel"})
	if capture.await(t).RequestID != "sentinel" {
		t.Fatal("duplicate terminal records")
	}
	select {
	case <-done:
	case <-time.After(3 * time.Second):
		t.Fatal("upstream did not close")
	}
}
