package usage

import (
	"context"
	"errors"
	"testing"
)

func TestDurableSinkPrecedesQueueAndFailureDoesNotEnqueue(t *testing.T) {
	original := DurableSink
	defer func() { DurableSink = original }()
	manager := NewManager(1)
	called := false
	DurableSink = func(_ context.Context, _ Record) error {
		called = true
		manager.mu.Lock()
		defer manager.mu.Unlock()
		if len(manager.queue) != 0 {
			t.Fatal("record queued before durability")
		}
		return errors.New("injected journal failure")
	}
	manager.Publish(context.Background(), Record{RequestID: "attempt", Provider: "fixture"})
	if !called {
		t.Fatal("durability gate not called")
	}
	manager.mu.Lock()
	defer manager.mu.Unlock()
	if len(manager.queue) != 0 {
		t.Fatal("failed durable record entered queue")
	}
}
