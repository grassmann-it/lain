package main

import (
	"errors"
	"io"
	"testing"
	"time"
)

func TestParentPipeLifetime(t *testing.T) {
	r, w := io.Pipe()
	defer r.Close()
	stopped := make(chan struct{})
	go monitorParent(r, func() { close(stopped) })
	// Successful writes prove the monitor is reading without ending ownership.
	if _, err := w.Write([]byte("bounded ignored parent data")); err != nil {
		t.Fatal(err)
	}
	select {
	case <-stopped:
		t.Fatal("live parent caused shutdown")
	default:
	}
	if err := w.Close(); err != nil {
		t.Fatal(err)
	}
	select {
	case <-stopped:
	case <-time.After(time.Second):
		t.Fatal("orphaned parent pipe did not trigger shutdown")
	}
}

type brokenParent struct{}

func (brokenParent) Read([]byte) (int, error) { return 0, errors.New("private fixture error") }
func TestParentPipeErrorStops(t *testing.T) {
	calls := 0
	monitorParent(brokenParent{}, func() { calls++ })
	if calls != 1 {
		t.Fatal(calls)
	}
}
