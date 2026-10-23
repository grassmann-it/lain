package openai

import (
	"context"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/gorilla/websocket"
)

func TestWebsocketInputDetectsCloseWhileResponseIsStalled(t *testing.T) {
	for _, normal := range []bool{false, true} {
		t.Run(map[bool]string{false: "aborted", true: "normal"}[normal], func(t *testing.T) {
			done := make(chan error, 1)
			server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				conn, err := (&websocket.Upgrader{}).Upgrade(w, r, nil)
				if err != nil {
					done <- err
					return
				}
				defer conn.Close()
				ctx, cancel := context.WithCancelCause(r.Context())
				defer cancel(context.Canceled)
				input := readResponsesWebsocketInput(ctx, cancel, conn)
				first := <-input
				if string(first.Payload) != "first" {
					t.Error("input order changed")
				}
				// Simulate a response waiting on upstream; nobody reads input here.
				<-ctx.Done()
				done <- context.Cause(ctx)
			}))
			defer server.Close()
			client, _, err := websocket.DefaultDialer.Dial("ws"+strings.TrimPrefix(server.URL, "http"), nil)
			if err != nil {
				t.Fatal(err)
			}
			defer client.Close()
			if err = client.WriteMessage(websocket.TextMessage, []byte("first")); err != nil {
				t.Fatal(err)
			}
			if normal {
				if err = client.WriteControl(websocket.CloseMessage, websocket.FormatCloseMessage(websocket.CloseNormalClosure, ""), time.Now().Add(time.Second)); err != nil {
					t.Fatal(err)
				}
			} else {
				_ = client.Close()
			}
			select {
			case cause := <-done:
				if cause == nil || cause == context.Canceled {
					t.Fatal("disconnect reason lost")
				}
			case <-time.After(3 * time.Second):
				t.Fatal("stalled response hid downstream disconnect")
			}
		})
	}
}
