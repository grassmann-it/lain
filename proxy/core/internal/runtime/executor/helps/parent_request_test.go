package helps

import (
	"bytes"
	"context"
	"testing"

	cliproxyexecutor "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/executor"
	"github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/usage"
)

func TestUsageReporterCapturesLifecycleParentAcrossAttempts(t *testing.T) {
	parent := "11111111-2222-4333-8444-555555555555"
	ctx := usage.WithParentRequestID(context.Background(), parent)
	first := NewUsageReporter(ctx, "test", "model", nil)
	second := NewUsageReporter(ctx, "test", "model", nil)
	failure := first.buildRecord(usage.Detail{}, true)
	success := second.buildRecord(usage.Detail{InputTokens: 100, OutputTokens: 20, TotalTokens: 120}, false)
	if failure.RequestID == success.RequestID || failure.RequestID == parent || success.RequestID == parent {
		t.Fatal("retry execution UUID collapsed into lifecycle UUID")
	}
	if failure.ParentRequestID != parent || success.ParentRequestID != parent {
		t.Fatal("reporter parent missing")
	}
	extra, ok := second.buildAdditionalModelRecord("image-model", usage.Detail{TotalTokens: 3})
	if !ok || extra.RequestID == success.RequestID || extra.ParentRequestID != parent {
		t.Fatal("additional-model execution lost distinct ID or parent")
	}
	if first.buildRecord(usage.Detail{}, false).ParentRequestID != parent {
		t.Fatal("captured parent mutated")
	}
}

func TestWebSocketObserverCarriesExplicitParentWithoutPayloadChanges(t *testing.T) {
	parent := "11111111-2222-4333-8444-555555555555"
	ctx := usage.WithParentRequestID(context.Background(), parent)
	payload := []byte(`{"type":"response.output_text.delta","delta":"synthetic"}`)
	before := bytes.Clone(payload)
	called := false
	opts := cliproxyexecutor.Options{
		Metadata: map[string]any{"request_id": "original-observer-id", "parent_request_id": "spoofed-parent"},
		WebSocketResponseObserver: func(_ context.Context, ev cliproxyexecutor.WebSocketResponseEvent) {
			called = true
			if ev.RequestID != "original-observer-id" || ev.ParentRequestID != parent || !bytes.Equal(ev.Payload, before) {
				t.Fatal("WS observer rewrote ID/payload or read parent from free metadata")
			}
		},
	}
	EmitWebSocketResponseEvent(ctx, opts, nil, "codex", "model", payload)
	if !called || !bytes.Equal(payload, before) || opts.Metadata["parent_request_id"] != "spoofed-parent" {
		t.Fatal("observer failed or modified its input")
	}
}
