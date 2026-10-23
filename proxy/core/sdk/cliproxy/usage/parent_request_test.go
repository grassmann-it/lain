package usage

import (
	"context"
	"testing"
	"time"
)

const parentTestID = "11111111-2222-4333-8444-555555555555"
const childTestID = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee"

type parentCapturePlugin struct{ records chan Record }

func (p parentCapturePlugin) HandleUsage(_ context.Context, r Record) { p.records <- r }

func TestParentRequestContextIsTypedAndImmutable(t *testing.T) {
	base := WithTraceID(context.Background(), "00000000")
	// Neither the HTTP trace nor a string-keyed SDK/plugin value supplies a parent.
	base = context.WithValue(base, "parent_request_id", childTestID)
	if ParentRequestIDFromContext(base) != "" || ParentRequestIDFromContext(nil) != "" {
		t.Fatal("inferred a lifecycle parent")
	}
	outer := WithParentRequestID(base, parentTestID)
	inner := WithParentRequestID(outer, childTestID)
	if ParentRequestIDFromContext(outer) != parentTestID || ParentRequestIDFromContext(inner) != childTestID {
		t.Fatal("nested lifecycle mutated its parent")
	}
	canceled, cancel := context.WithCancel(outer)
	cancel()
	if ParentRequestIDFromContext(context.WithoutCancel(canceled)) != parentTestID {
		t.Fatal("asynchronous publication lost parent")
	}
	for _, invalid := range []string{"00000000", "secret", "prefix " + parentTestID} {
		if ParentRequestIDFromContext(WithParentRequestID(outer, invalid)) != "" {
			t.Fatal("invalid child context inherited or accepted a parent")
		}
	}
	if ParentRequestIDFromContext(WithParentRequestID(nil, parentTestID)) != parentTestID {
		t.Fatal("nil base context unsupported")
	}
}

func TestPublishPreservesDistinctAttemptsAndExplicitParent(t *testing.T) {
	m := NewManager(8)
	defer m.Stop()
	capture := parentCapturePlugin{make(chan Record, 8)}
	m.Register(capture)
	ctx := WithParentRequestID(context.Background(), parentTestID)
	ids := []string{"22222222-2222-4222-8222-222222222222", "33333333-3333-4333-8333-333333333333"}
	for i, id := range ids {
		m.Publish(ctx, Record{RequestID: id, Failed: i == 0})
	}
	for _, id := range ids {
		select {
		case r := <-capture.records:
			if r.RequestID != id || r.ParentRequestID != parentTestID {
				t.Fatal("attempt identity or parent was replaced")
			}
		case <-time.After(time.Second):
			t.Fatal("usage publication stalled")
		}
	}
	// A record captured in a child reporter must retain its birth parent even
	// if published later with the caller's different context.
	m.Publish(ctx, Record{RequestID: ids[0], ParentRequestID: childTestID})
	m.Publish(context.Background(), Record{RequestID: ids[1]})
	for _, want := range []string{childTestID, ""} {
		select {
		case r := <-capture.records:
			if r.ParentRequestID != want {
				t.Fatal("captured parent lost or missing parent fabricated")
			}
		case <-time.After(time.Second):
			t.Fatal("usage publication stalled")
		}
	}
}
