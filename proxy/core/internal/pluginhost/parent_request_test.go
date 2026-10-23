package pluginhost

import (
	"context"
	"encoding/json"
	"testing"

	coreusage "github.com/router-for-me/CLIProxyAPI/v7/sdk/cliproxy/usage"
	"github.com/router-for-me/CLIProxyAPI/v7/sdk/pluginapi"
)

func TestUsageAdapterAndRPCSerializeExactParentRequestID(t *testing.T) {
	parent := "11111111-2222-4333-8444-555555555555"
	ids := []string{"aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee", "12345678-1234-4234-8234-123456789abc"}
	var got []pluginapi.UsageRecord
	plugin := usagePluginFunc(func(_ context.Context, r pluginapi.UsageRecord) { got = append(got, r) })
	host := newHostWithRecords(capabilityRecord{id: "parent-test", plugin: pluginapi.Plugin{Capabilities: pluginapi.Capabilities{UsagePlugin: plugin}}})
	adapter := &usageAdapter{host: host, pluginID: "parent-test"}
	for _, id := range ids {
		adapter.HandleUsage(context.Background(), coreusage.Record{RequestID: id, ParentRequestID: parent})
	}
	if len(got) != 2 {
		t.Fatal("missing adapter records")
	}
	for i, r := range got {
		if r.RequestID != ids[i] || r.ParentRequestID != parent {
			t.Fatal("adapter collapsed attempts or dropped parent")
		}
		raw, err := json.Marshal(r)
		if err != nil {
			t.Fatal(err)
		}
		var fields map[string]json.RawMessage
		_ = json.Unmarshal(raw, &fields)
		if string(fields["parent_request_id"]) != `"`+parent+`"` || fields["ParentRequestID"] != nil {
			t.Fatal("usage RPC field is not exactly parent_request_id")
		}
	}
	raw, _ := json.Marshal(rpcWebSocketResponseEvent{WebSocketResponseEvent: pluginapi.WebSocketResponseEvent{ParentRequestID: parent}})
	var fields map[string]json.RawMessage
	_ = json.Unmarshal(raw, &fields)
	if string(fields["parent_request_id"]) != `"`+parent+`"` {
		t.Fatal("WS RPC parent field missing")
	}
}

func TestUsageAdapterPreservesCanceledUnknownMetadata(t *testing.T) {
	reported := false
	called := false
	plugin := usagePluginFunc(func(ctx context.Context, r pluginapi.UsageRecord) {
		called = true
		if ctx.Err() != nil || r.Outcome != "canceled" || r.UsageReported == nil || *r.UsageReported || !r.Failed || r.RequestID != "execution" || r.ParentRequestID != "parent" {
			t.Fatal("canceled adapter contract lost")
		}
		b, err := json.Marshal(r)
		if err != nil {
			t.Fatal(err)
		}
		var fields map[string]json.RawMessage
		_ = json.Unmarshal(b, &fields)
		if fields["Detail"] != nil {
			t.Fatal("unknown detail fabricated on RPC")
		}
	})
	host := newHostWithRecords(capabilityRecord{id: "cancel-test", plugin: pluginapi.Plugin{Capabilities: pluginapi.Capabilities{UsagePlugin: plugin}}})
	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	(&usageAdapter{host: host, pluginID: "cancel-test"}).HandleUsage(ctx, coreusage.Record{RequestID: "execution", ParentRequestID: "parent", Failed: true, Outcome: "canceled", UsageReported: &reported})
	if !called {
		t.Fatal("canceled context suppressed usage")
	}
}
