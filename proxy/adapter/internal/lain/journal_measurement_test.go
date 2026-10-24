package lain

import (
	"bufio"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"runtime"
	"testing"
	"time"
)

// Explicit opt-in keeps a bounded measurement separate from routine tests.
// Generation uses synthetic events and never accesses an installed runtime.
func TestJournalBoundedGrowthMeasurement(t *testing.T) {
	if os.Getenv("LAIN_JOURNAL_MEASURE") != "1" {
		t.Skip("set LAIN_JOURNAL_MEASURE=1 for the bounded synthetic measurement")
	}
	for _, count := range []int{1000, 10000} {
		t.Run(fmt.Sprint(count), func(t *testing.T) {
			path := filepath.Join(t.TempDir(), "events.jsonl")
			j, err := OpenJournal(path)
			if err != nil {
				t.Fatal(err)
			}
			source := j.sourceID
			_ = j.Close()
			f, err := os.OpenFile(path, os.O_WRONLY|os.O_TRUNC, 0600)
			if err != nil {
				t.Fatal(err)
			}
			w := bufio.NewWriter(f)
			for n := 1; n <= count; n++ {
				e := Event{SchemaVersion: 1, SourceID: source, Sequence: int64(n), EventID: fmt.Sprintf("%s:%d", source, n), Kind: "usage", RequestID: "synthetic-request", AttemptID: fmt.Sprintf("synthetic-attempt-%d", n), AccountID: "synthetic-account", CredentialID: "synthetic-credential", RouteID: ptr("synthetic-route"), ObservedAt: time.Date(2026, 10, 24, 0, 0, 0, 0, time.UTC), Provider: "synthetic", RequestedModel: "synthetic-route", Model: "synthetic-model", Outcome: ptr("success"), InputTokens: ptr(int64(123)), OutputTokens: ptr(int64(45)), TotalTokens: ptr(int64(168)), UsageReported: true, StatusCode: ptr(200)}
				data, _ := json.Marshal(e)
				if _, err = w.Write(append(data, '\n')); err != nil {
					t.Fatal(err)
				}
			}
			if err = w.Flush(); err != nil {
				t.Fatal(err)
			}
			if err = f.Sync(); err != nil {
				t.Fatal(err)
			}
			if err = f.Close(); err != nil {
				t.Fatal(err)
			}
			runtime.GC()
			var before, after runtime.MemStats
			runtime.ReadMemStats(&before)
			start := time.Now()
			j, err = OpenJournal(path)
			startup := time.Since(start)
			if err != nil {
				t.Fatal(err)
			}
			defer j.Close()
			start = time.Now()
			batch, err := j.Read(int64(count-10), 10)
			read := time.Since(start)
			if err != nil || len(batch.Events) != 10 || batch.NextCursor != int64(count) {
				t.Fatal(batch, err)
			}
			runtime.GC()
			runtime.ReadMemStats(&after)
			data, _ := json.Marshal(map[string]any{"events": count, "fileBytes": j.end, "indexEntries": len(j.offsets), "indexCapacityBytes": cap(j.offsets) * 8, "heapDeltaBytes": int64(after.HeapAlloc) - int64(before.HeapAlloc), "startupMilliseconds": float64(startup) / float64(time.Millisecond), "tailReadMilliseconds": float64(read) / float64(time.Millisecond), "qualification": "bounded_synthetic_cached_filesystem_not_long_term_load"})
			t.Log(string(data))
			runtime.KeepAlive(j)
		})
	}
}
