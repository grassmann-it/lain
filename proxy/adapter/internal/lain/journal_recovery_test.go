package lain

import (
	"bytes"
	"encoding/json"
	"os"
	"path/filepath"
	"testing"
)

func TestSparseJournalPreservesLegacyBytesAndEveryCursor(t *testing.T) {
	path := filepath.Join(t.TempDir(), "events.jsonl")
	j, err := OpenJournal(path)
	if err != nil {
		t.Fatal(err)
	}
	for n := 0; n < 800; n++ {
		if err = j.Append(Event{Kind: "usage", AccountID: "fixture"}); err != nil {
			t.Fatal(err)
		}
	}
	before, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	storage := j.Storage()
	if storage.Events != 800 || storage.IndexEntries != 4 || storage.IndexStride != 256 || storage.Bytes != int64(len(before)) {
		t.Fatal(storage)
	}
	for _, after := range []int64{0, 1, 254, 255, 256, 257, 510, 511, 512, 767, 799, 800} {
		batch, err := j.Read(after, 19)
		if err != nil {
			t.Fatal(err)
		}
		for k, event := range batch.Events {
			if event.Sequence != after+int64(k)+1 || event.SourceID != storage.SourceID {
				t.Fatal("cursor changed", after, batch)
			}
		}
		if batch.HighWatermark != 800 {
			t.Fatal(batch)
		}
	}
	_ = j.Close()
	j, err = OpenJournal(path)
	if err != nil {
		t.Fatal(err)
	}
	defer j.Close()
	if err = j.Append(Event{Kind: "usage"}); err != nil {
		t.Fatal(err)
	}
	after, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	if !bytes.HasPrefix(after, before) {
		t.Fatal("legacy history rewritten")
	}
	// The legacy newline reader can still consume the entire newer file and
	// sees unchanged contiguous IDs. No segment manifest is needed to roll back.
	for k, line := range bytes.Split(bytes.TrimSuffix(after, []byte{'\n'}), []byte{'\n'}) {
		var event Event
		if json.Unmarshal(line, &event) != nil || event.Sequence != int64(k+1) || event.SourceID != storage.SourceID {
			t.Fatal("legacy reader cannot replay", k)
		}
	}
}

func TestRecoverCopyPreservesOriginalAndSourceAndCursor(t *testing.T) {
	for _, tail := range []string{"", `{"sequence":3`, `{"sequence":3,"unacknowledged":"complete JSON without newline"}`} {
		t.Run(tail, func(t *testing.T) {
			base := t.TempDir()
			path := filepath.Join(base, "events.jsonl")
			j, err := OpenJournal(path)
			if err != nil {
				t.Fatal(err)
			}
			for n := 0; n < 2; n++ {
				if err = j.Append(Event{Kind: "usage", AccountID: "fixture"}); err != nil {
					t.Fatal(err)
				}
			}
			if _, err = InspectJournal(path); err == nil {
				t.Fatal("inspection bypassed live writer lock")
			}
			if _, err = RecoverJournalCopy(path, filepath.Join(base, "live-copy")); err == nil {
				t.Fatal("recovery bypassed live writer lock")
			}
			prefix, _ := os.ReadFile(path)
			source, _ := os.ReadFile(path + ".source")
			_ = j.Close()
			original := append(append([]byte{}, prefix...), tail...)
			if err = os.WriteFile(path, original, 0600); err != nil {
				t.Fatal(err)
			}
			dest := filepath.Join(base, "recovered")
			report, err := RecoverJournalCopy(path, dest)
			if err != nil {
				t.Fatal(err)
			}
			if report.Events != 2 || report.VerifiedPrefixBytes != int64(len(prefix)) || report.UncertainTailBytes != int64(len(tail)) || report.SourceID != string(source) || len(report.OriginalSHA256) != 64 || report.CommitCertainty == "" {
				t.Fatal(report)
			}
			unchanged, _ := os.ReadFile(path)
			unchangedSource, _ := os.ReadFile(path + ".source")
			if !bytes.Equal(unchanged, original) || !bytes.Equal(unchangedSource, source) {
				t.Fatal("original changed")
			}
			copyBytes, _ := os.ReadFile(filepath.Join(dest, "events.jsonl"))
			if !bytes.Equal(copyBytes, prefix) {
				t.Fatal("copy did not preserve exact valid bytes")
			}
			reopened, err := OpenJournal(filepath.Join(dest, "events.jsonl"))
			if err != nil {
				t.Fatal(err)
			}
			defer reopened.Close()
			page, err := reopened.Read(1, 1)
			if err != nil || page.NextCursor != 2 || page.HighWatermark != 2 || page.SourceID != string(source) {
				t.Fatal(page, err)
			}
			if err = reopened.Append(Event{Kind: "usage"}); err != nil {
				t.Fatal(err)
			}
			next, _ := reopened.Read(2, 1)
			if next.NextCursor != 3 {
				t.Fatal(next)
			}
			if _, err = RecoverJournalCopy(path, dest); err == nil {
				t.Fatal("existing recovery overwritten")
			}
			info, _ := os.Stat(dest)
			if info.Mode().Perm() != 0700 {
				t.Fatal(info.Mode())
			}
		})
	}
}

func TestCopyRecoveryRefusesInteriorCorruptionAndUnknownSource(t *testing.T) {
	for _, bad := range []string{"{}\n", "not json\n", "missing-source"} {
		t.Run(bad, func(t *testing.T) {
			base := t.TempDir()
			path := filepath.Join(base, "events.jsonl")
			j, err := OpenJournal(path)
			if err != nil {
				t.Fatal(err)
			}
			_ = j.Close()
			if bad == "missing-source" {
				_ = os.Remove(path + ".source")
			} else {
				_ = os.WriteFile(path, []byte(bad), 0600)
			}
			destination := filepath.Join(base, "refused")
			if _, err = RecoverJournalCopy(path, destination); err == nil {
				t.Fatal("ambiguous history recovered")
			}
			if _, err = os.Lstat(destination); !os.IsNotExist(err) {
				t.Fatal("failed recovery published destination", err)
			}
		})
	}
}
