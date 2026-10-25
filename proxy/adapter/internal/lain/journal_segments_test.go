package lain

import (
	"bytes"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"reflect"
	"strings"
	"sync"
	"syscall"
	"testing"
	"time"
)

func archiveFixture(t *testing.T, count int, maxBytes int64) (string, string, []byte) {
	t.Helper()
	root := t.TempDir()
	original := filepath.Join(root, "original.jsonl")
	j, err := OpenJournal(original)
	if err != nil {
		t.Fatal(err)
	}
	for n := 0; n < count; n++ {
		if err = j.Append(Event{Kind: "usage", TotalTokens: ptr(int64(n + 1)), UsageReported: true}); err != nil {
			t.Fatal(err)
		}
	}
	if err = j.Close(); err != nil {
		t.Fatal(err)
	}
	data, err := os.ReadFile(original)
	if err != nil {
		t.Fatal(err)
	}
	// Unknown future fields and legal whitespace must survive the roundtrip.
	data = bytes.ReplaceAll(data, []byte("\"schemaVersion\":1"), []byte("\"futureField\": {\"preserve\":true}, \"schemaVersion\":1"))
	if err = os.WriteFile(original, data, 0600); err != nil {
		t.Fatal(err)
	}
	destination := filepath.Join(root, "segmented")
	report, err := SegmentJournalCopy(original, destination, maxBytes)
	if err != nil {
		t.Fatal(err)
	}
	if report.Events != int64(count) || report.Bytes != int64(len(data)) || report.Destination != filepath.Join(destination, "events.jsonl") {
		t.Fatal(report)
	}
	return original, report.Destination, data
}
func archiveReadAll(t *testing.T, j *Journal, limit int) []Event {
	t.Helper()
	var events []Event
	cursor := int64(0)
	for {
		page, err := j.Read(cursor, limit)
		if err != nil {
			t.Fatal(err)
		}
		events = append(events, page.Events...)
		if page.NextCursor == page.HighWatermark {
			break
		}
		if page.NextCursor <= cursor {
			t.Fatal("cursor did not advance")
		}
		cursor = page.NextCursor
	}
	return events
}
func mustOpenArchive(t *testing.T, path string) *Journal {
	t.Helper()
	j, err := OpenJournal(path)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { j.Close() })
	return j
}
func largeArchiveEvent() Event { return Event{Kind: strings.Repeat("x", 2300)} }

func TestJournalSegmentsExactLegacyExportAndCrossBoundaryReplay(t *testing.T) {
	original, archive, originalBytes := archiveFixture(t, 41, 4096)
	flat := mustOpenArchive(t, original)
	j := mustOpenArchive(t, archive)
	if len(j.segments.sealed) < 3 {
		t.Fatal("fixture did not create real archive boundaries")
	}
	if !reflect.DeepEqual(archiveReadAll(t, flat, 7), archiveReadAll(t, j, 13)) {
		t.Fatal("segmentation changed events, source, identities or sums")
	}
	for after := int64(0); after <= 41; after++ {
		for _, limit := range []int{1, 5, 1000} {
			want, _ := flat.Read(after, limit)
			got, err := j.Read(after, limit)
			if err != nil || !reflect.DeepEqual(want, got) {
				t.Fatalf("cursor %d limit %d: %v %#v %#v", after, limit, err, want, got)
			}
		}
	}
	for _, part := range j.segments.manifest.Sealed {
		if part.Bytes > 4096 {
			t.Fatal("segment exceeds limit")
		}
	}
	storage := j.Storage()
	if storage.Format != "segmented-v1" || storage.SealedSegments < 3 || storage.Bytes != int64(len(originalBytes)) || storage.OnDiskBytes == nil || *storage.OnDiskBytes < storage.Bytes {
		t.Fatal(storage)
	}
	if _, err := OpenJournal(archive); err == nil {
		t.Fatal("second runtime writer accepted")
	}
	if _, err := ExportJournalFlat(archive, filepath.Join(t.TempDir(), "busy")); err == nil {
		t.Fatal("live export accepted")
	}
	j.Close()
	destination := filepath.Join(t.TempDir(), "legacy")
	report, err := ExportJournalFlat(archive, destination)
	if err != nil {
		t.Fatal(err)
	}
	got, _ := os.ReadFile(report.Destination)
	source, _ := os.ReadFile(original + ".source")
	exportedSource, _ := os.ReadFile(report.Destination + ".source")
	if !bytes.Equal(got, originalBytes) || !bytes.Equal(source, exportedSource) {
		t.Fatal("legacy export changed exact bytes/source")
	}
	hash := sha256.Sum256(originalBytes)
	if report.SHA256 != hex.EncodeToString(hash[:]) {
		t.Fatal(report)
	}
	reopened := mustOpenArchive(t, report.Destination)
	if !reflect.DeepEqual(archiveReadAll(t, flat, 17), archiveReadAll(t, reopened, 17)) {
		t.Fatal("legacy reopen differs")
	}
	if err = reopened.Append(Event{Kind: "after_rollback"}); err != nil {
		t.Fatal(err)
	}
	if tail, err := reopened.Read(41, 1); err != nil || tail.NextCursor != 42 || tail.SourceID != string(source) {
		t.Fatal(tail, err)
	}
	unchanged, _ := os.ReadFile(original)
	if !bytes.Equal(unchanged, originalBytes) {
		t.Fatal("copy/export modified original")
	}
	// This is the actual legacy opening syscall, not a simulated format flag.
	if f, err := os.OpenFile(archive, os.O_RDWR|os.O_CREATE, 0600); err == nil {
		f.Close()
		t.Fatal("legacy binary could open segmented path as flat journal")
	}
}
func TestJournalSegmentsRotationRestartAndQuotaSidecar(t *testing.T) {
	_, archive, _ := archiveFixture(t, 6, 4096)
	j := mustOpenArchive(t, archive)
	i, err := New(j, Bindings{})
	if err != nil {
		t.Fatal(err)
	}
	observation := quotaObservation{AccountID: "a", Provider: "groq", Model: "m", RequestID: "r", ObservedAt: time.Now().UTC(), StatusCode: 200, Headers: map[string][]string{"x-ratelimit-limit-tokens": {"6000"}}}
	i.Quotas.record(observation)
	before := quotaBatch(t, i.Quotas)
	firstActive := j.file.Name()
	for n := 0; n < 3; n++ {
		if err = j.Append(largeArchiveEvent()); err != nil {
			t.Fatal(err)
		}
	}
	if j.file.Name() == firstActive || i.Quotas.path != archive+".quota-observations.json" {
		t.Fatal("rotation or stable sidecar failed")
	}
	want := archiveReadAll(t, j, 2)
	source := j.sourceID
	j.Close()
	j = mustOpenArchive(t, archive)
	if j.count != 9 || j.sourceID != source || !reflect.DeepEqual(want, archiveReadAll(t, j, 4)) {
		t.Fatal("restart lost acknowledged data")
	}
	i, err = New(j, Bindings{})
	if err != nil {
		t.Fatal(err)
	}
	if !reflect.DeepEqual(before, quotaBatch(t, i.Quotas)) {
		t.Fatal("quota observations lost across rotation/restart")
	}
	j.Close()
	out, err := ExportJournalFlat(archive, filepath.Join(t.TempDir(), "rollback"))
	if err != nil {
		t.Fatal(err)
	}
	flat := mustOpenArchive(t, out.Destination)
	if !reflect.DeepEqual(want, archiveReadAll(t, flat, 3)) {
		t.Fatal("rollback omitted live rotated events")
	}
}
func TestJournalSegmentsConcurrentAppendAndReplay(t *testing.T) {
	_, archive, _ := archiveFixture(t, 4, 4096)
	j := mustOpenArchive(t, archive)
	var wg sync.WaitGroup
	for n := 0; n < 32; n++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			if err := j.Append(Event{Kind: "concurrent"}); err != nil {
				t.Error(err)
			}
			if _, err := j.Read(0, 1000); err != nil {
				t.Error(err)
			}
		}()
	}
	wg.Wait()
	events := archiveReadAll(t, j, 7)
	if len(events) != 36 {
		t.Fatal(len(events))
	}
	for n, e := range events {
		if e.Sequence != int64(n+1) || e.EventID != fmt.Sprintf("%s:%d", j.sourceID, n+1) {
			t.Fatal(e)
		}
	}
}
func TestJournalSegmentsPublicationFailuresRetainAcknowledgedPrefix(t *testing.T) {
	for _, point := range []string{"after-active-sync", "before-manifest-write", "after-manifest-sync", "after-manifest-rename", "after-directory-sync"} {
		t.Run(point, func(t *testing.T) {
			_, archive, _ := archiveFixture(t, 0, 4096)
			j := mustOpenArchive(t, archive)
			if err := j.Append(largeArchiveEvent()); err != nil {
				t.Fatal(err)
			}
			want := archiveReadAll(t, j, 1000)
			j.segments.checkpoint = func(at string) error {
				if at == point {
					return syscall.ENOSPC
				}
				return nil
			}
			if err := j.Append(largeArchiveEvent()); !errors.Is(err, syscall.ENOSPC) {
				t.Fatal("fault boundary not exercised", err)
			}
			if j.Health() == nil || j.Append(Event{}) == nil || j.count != 1 {
				t.Fatal("write error not sticky or made failed event visible")
			}
			j.Close()
			entriesBefore, _ := os.ReadDir(archive)
			j = mustOpenArchive(t, archive)
			if !reflect.DeepEqual(want, archiveReadAll(t, j, 2)) {
				t.Fatal("publication failure lost acknowledged prefix")
			}
			entriesAfter, _ := os.ReadDir(archive)
			if len(entriesAfter) != len(entriesBefore) {
				t.Fatal("open deleted or adopted orphan files")
			}
			if err := j.Append(largeArchiveEvent()); err != nil {
				t.Fatal(err)
			}
			if j.count != 2 {
				t.Fatal("restart did not recover writer")
			}
		})
	}
}
func TestJournalSegmentsAbruptPublicationCrash(t *testing.T) {
	if path := os.Getenv("LAIN_TEST_ARCHIVE_CRASH_PATH"); path != "" {
		j, err := OpenJournal(path)
		if err != nil {
			t.Fatal(err)
		}
		point := os.Getenv("LAIN_TEST_ARCHIVE_CRASH_POINT")
		j.segments.checkpoint = func(at string) error {
			if at == point {
				os.Exit(91)
			}
			return nil
		}
		if err = j.Append(largeArchiveEvent()); err != nil {
			t.Fatal(err)
		}
		t.Fatal("crash point was not reached")
	}
	for _, point := range []string{"after-active-sync", "after-manifest-sync", "after-manifest-rename", "after-directory-sync"} {
		t.Run(point, func(t *testing.T) {
			_, archive, _ := archiveFixture(t, 0, 4096)
			j := mustOpenArchive(t, archive)
			if err := j.Append(largeArchiveEvent()); err != nil {
				t.Fatal(err)
			}
			want := archiveReadAll(t, j, 1)
			j.Close()
			cmd := exec.Command(os.Args[0], "-test.run=^TestJournalSegmentsAbruptPublicationCrash$")
			cmd.Env = append(os.Environ(), "LAIN_TEST_ARCHIVE_CRASH_PATH="+archive, "LAIN_TEST_ARCHIVE_CRASH_POINT="+point)
			output, err := cmd.CombinedOutput()
			var exit *exec.ExitError
			if !errors.As(err, &exit) || exit.ExitCode() != 91 {
				t.Fatalf("child did not crash at publication: %v %s", err, output)
			}
			j = mustOpenArchive(t, archive)
			if !reflect.DeepEqual(want, archiveReadAll(t, j, 1)) {
				t.Fatal("crash changed confirmed prefix")
			}
			if err = j.Append(largeArchiveEvent()); err != nil {
				t.Fatal(err)
			}
			if page, err := j.Read(1, 1); err != nil || page.NextCursor != 2 {
				t.Fatal(page, err)
			}
		})
	}
}
func TestJournalSegmentsRejectCorruptionAndTornActive(t *testing.T) {
	for _, change := range []string{"version", "gap", "source", "hash", "size", "missing", "symlink", "duplicate", "active-gap", "torn-active", "oversize-active", "changed-content", "trailing-manifest"} {
		t.Run(change, func(t *testing.T) {
			_, archive, _ := archiveFixture(t, 12, 4096)
			m, err := readSegmentManifest(archive)
			if err != nil {
				t.Fatal(err)
			}
			file := filepath.Join(archive, m.Sealed[0].File)
			switch change {
			case "version":
				m.Version++
			case "gap":
				m.Sealed[0].First++
			case "source":
				m.SourceID = "00000000-0000-4000-8000-000000000000"
			case "hash":
				m.Sealed[0].SHA256 = strings.Repeat("0", 64)
			case "size":
				m.Sealed[0].Bytes--
			case "missing":
				if err = os.Remove(file); err != nil {
					t.Fatal(err)
				}
			case "symlink":
				data, _ := os.ReadFile(file)
				target := filepath.Join(t.TempDir(), "external")
				if err = os.WriteFile(target, data, 0600); err != nil {
					t.Fatal(err)
				}
				if err = os.Remove(file); err != nil {
					t.Fatal(err)
				}
				if err = os.Symlink(target, file); err != nil {
					t.Fatal(err)
				}
			case "duplicate":
				m.Active = m.Sealed[0].File
			case "active-gap":
				m.ActiveFirst++
			case "torn-active":
				if err = os.WriteFile(filepath.Join(archive, m.Active), []byte(`{"incomplete":`), 0600); err != nil {
					t.Fatal(err)
				}
			case "oversize-active":
				if err = os.WriteFile(filepath.Join(archive, m.Active), bytes.Repeat([]byte("x"), 4097), 0600); err != nil {
					t.Fatal(err)
				}
			case "changed-content":
				data, _ := os.ReadFile(file)
				data = bytes.Replace(data, []byte(`"totalTokens":1`), []byte(`"totalTokens":9`), 1)
				if err = os.WriteFile(file, data, 0600); err != nil {
					t.Fatal(err)
				}
			}
			data, _ := json.Marshal(m)
			if change == "trailing-manifest" {
				data = append(data, []byte(" {}")...)
			}
			if err = os.WriteFile(filepath.Join(archive, "manifest.json"), data, 0600); err != nil {
				t.Fatal(err)
			}
			if opened, err := OpenJournal(archive); err == nil {
				opened.Close()
				t.Fatal("corrupt archive opened")
			}
			if _, err := ExportJournalFlat(archive, filepath.Join(t.TempDir(), "flat")); err == nil {
				t.Fatal("corruption exported")
			}
		})
	}
}
func TestJournalSegmentsDetectPostOpenArchiveCorruption(t *testing.T) {
	_, archive, _ := archiveFixture(t, 12, 4096)
	j := mustOpenArchive(t, archive)
	file := filepath.Join(archive, j.segments.manifest.Sealed[0].File)
	data, _ := os.ReadFile(file)
	changed := bytes.Replace(data, []byte(`"totalTokens":1`), []byte(`"totalTokens":9`), 1)
	if bytes.Equal(data, changed) {
		t.Fatal("corruption fixture ineffective")
	}
	if err := os.WriteFile(file, changed, 0600); err != nil {
		t.Fatal(err)
	}
	if batch, err := j.Read(0, 1000); err == nil || len(batch.Events) != 0 || j.Health() == nil {
		t.Fatal("changed sums replayed", batch, err)
	}
}
func TestJournalSegmentsOfflineWriterExclusionAndNoReplaceRace(t *testing.T) {
	original, archive, _ := archiveFixture(t, 2, 4096)
	flat := mustOpenArchive(t, original)
	if _, err := SegmentJournalCopy(original, filepath.Join(t.TempDir(), "busy"), 4096); err == nil {
		t.Fatal("live segmentation accepted")
	}
	flat.Close()
	for _, operation := range []string{"segment", "export"} {
		t.Run(operation, func(t *testing.T) {
			destination := filepath.Join(t.TempDir(), "race")
			checkpoint := func(point string) error {
				if point == "before-destination-rename" {
					return os.Mkdir(destination, 0700)
				}
				return nil
			}
			var err error
			if operation == "segment" {
				_, err = segmentJournalCopy(original, destination, 4096, checkpoint)
			} else {
				_, err = exportJournalFlat(archive, destination, checkpoint)
			}
			if err == nil {
				t.Fatal("concurrent destination overwritten")
			}
			entries, _ := os.ReadDir(destination)
			if len(entries) != 0 {
				t.Fatal("race winner changed")
			}
		})
	}
	if _, err := ExportJournalFlat(archive, filepath.Join(archive, "inside")); err == nil {
		t.Fatal("export staged inside original")
	}
	if _, err := SegmentJournalCopy(original, filepath.Join(t.TempDir(), "oversmall"), 4095); err == nil {
		t.Fatal("bad cap accepted")
	}
}
func TestJournalSegmentsOversizedEventAndSyncFailureVisibility(t *testing.T) {
	for _, mode := range []string{"oversized", "sync"} {
		t.Run(mode, func(t *testing.T) {
			_, archive, _ := archiveFixture(t, 3, 4096)
			j := mustOpenArchive(t, archive)
			before := j.count
			event := Event{Kind: strings.Repeat("x", 5000)}
			if mode == "sync" {
				event = Event{Kind: "uncertain"}
				j.syncFile = func() error { return syscall.ENOSPC }
			}
			if err := j.Append(event); err == nil {
				t.Fatal("expected failure")
			}
			if j.count != before || j.Health() == nil {
				t.Fatal("failed append visible")
			}
			if mode == "oversized" {
				info, err := j.file.Stat()
				if err != nil || info.Size() != 0 {
					t.Fatal("oversized event wrote bytes", err)
				}
			}
		})
	}
}

func TestJournalSegmentsRecoverTornActiveIntoExactFlatPrefix(t *testing.T) {
	_, archive, original := archiveFixture(t, 12, 4096)
	j := mustOpenArchive(t, archive)
	if err := j.Append(Event{Kind: "after-sealed", TotalTokens: ptr(int64(17))}); err != nil {
		t.Fatal(err)
	}
	completeActive, err := os.ReadFile(j.file.Name())
	if err != nil {
		t.Fatal(err)
	}
	active := j.file.Name()
	expected := archiveReadAll(t, j, 1000)
	j.Close()
	tail := []byte(`{"sequence":14,"uncertain":`)
	if err = os.WriteFile(active, append(append([]byte{}, completeActive...), tail...), 0600); err != nil {
		t.Fatal(err)
	}
	if opened, err := OpenJournal(archive); err == nil {
		opened.Close()
		t.Fatal("torn active opened")
	}
	destination := filepath.Join(t.TempDir(), "recovered")
	report, err := RecoverSegmentedJournalCopy(archive, destination)
	if err != nil {
		t.Fatal(err)
	}
	if report.Events != 13 || report.UncertainTailBytes != int64(len(tail)) || report.TailState != "incomplete_final_record" {
		t.Fatal(report)
	}
	bytesBefore := append(append(append([]byte{}, original...), completeActive...), tail...)
	hash := sha256.Sum256(bytesBefore)
	if report.OriginalSHA256 != hex.EncodeToString(hash[:]) || report.OriginalBytes != int64(len(bytesBefore)) {
		t.Fatal("recovery report lost original archive bytes", report)
	}
	out := filepath.Join(destination, "events.jsonl")
	got, err := os.ReadFile(out)
	if err != nil || !bytes.Equal(got, bytesBefore[:len(bytesBefore)-len(tail)]) {
		t.Fatal("recovery did not preserve exact complete prefix", err)
	}
	recovered := mustOpenArchive(t, out)
	if !reflect.DeepEqual(expected, archiveReadAll(t, recovered, 5)) {
		t.Fatal("recovery changed identity or sums")
	}
	if err = recovered.Append(Event{Kind: "resumed"}); err != nil {
		t.Fatal(err)
	}
	if page, err := recovered.Read(13, 1); err != nil || page.NextCursor != 14 {
		t.Fatal(page, err)
	}
	unchanged, _ := os.ReadFile(active)
	if !bytes.HasSuffix(unchanged, tail) {
		t.Fatal("original uncertain tail removed")
	}
	m, err := readSegmentManifest(archive)
	if err != nil {
		t.Fatal(err)
	}
	bad := filepath.Join(archive, m.Sealed[0].File)
	data, _ := os.ReadFile(bad)
	data[1] = '!'
	if err = os.WriteFile(bad, data, 0600); err != nil {
		t.Fatal(err)
	}
	if _, err = RecoverSegmentedJournalCopy(archive, filepath.Join(t.TempDir(), "bad")); err == nil {
		t.Fatal("interior corruption recovered")
	}
}

func TestJournalSegmentsSparseCheckpointsWithinSealedAndActiveSegments(t *testing.T) {
	original, archive, _ := archiveFixture(t, 620, 1<<20)
	flat := mustOpenArchive(t, original)
	j := mustOpenArchive(t, archive)
	if len(j.segments.sealed) != 1 || len(j.segments.sealed[0].offsets) < 3 {
		t.Fatal("fixture did not cross two sparse strides inside a real segment")
	}
	for _, after := range []int64{0, 254, 255, 256, 257, 510, 511, 512, 513, 619, 620} {
		want, _ := flat.Read(after, 17)
		got, err := j.Read(after, 17)
		if err != nil || !reflect.DeepEqual(want, got) {
			t.Fatal(after, err, want, got)
		}
	}
	for n := 0; n < 530; n++ {
		if err := j.Append(Event{Kind: "post-copy"}); err != nil {
			t.Fatal(err)
		}
	}
	if len(j.segments.active.offsets) < 3 {
		t.Fatal("active sparse stride was not exercised")
	}
	for _, after := range []int64{620 + 254, 620 + 255, 620 + 256, 620 + 257, 620 + 510, 620 + 511, 620 + 512} {
		page, err := j.Read(after, 13)
		if err != nil || page.NextCursor != after+13 || page.Events[0].Sequence != after+1 {
			t.Fatal(after, page, err)
		}
	}
	// Force a real rotation after those active checkpoints without changing
	// the manifest cap or manufacturing fake concatenated archive files.
	if err := j.Append(Event{Kind: strings.Repeat("z", 900000)}); err != nil {
		t.Fatal(err)
	}
	if len(j.segments.sealed) < 2 {
		t.Fatal("large event failed to rotate prior checkpointed active")
	}
	before := archiveReadAll(t, j, 193)
	j.Close()
	j = mustOpenArchive(t, archive)
	if !reflect.DeepEqual(before, archiveReadAll(t, j, 257)) {
		t.Fatal("sparse segmented restart replay changed")
	}
	j.Close()
	report, err := ExportJournalFlat(archive, filepath.Join(t.TempDir(), "rollback"))
	if err != nil {
		t.Fatal(err)
	}
	exported := mustOpenArchive(t, report.Destination)
	if !reflect.DeepEqual(before, archiveReadAll(t, exported, 257)) {
		t.Fatal("sparse segmented flat rollback changed")
	}
}
