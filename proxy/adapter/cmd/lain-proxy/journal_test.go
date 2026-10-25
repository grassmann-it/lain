package main

import (
	"bytes"
	"encoding/json"
	"lain.local/proxy/internal/lain"
	"os"
	"path/filepath"
	"testing"
)

func TestOfflineJournalCommandsDoNotRequireRuntimeCredentials(t *testing.T) {
	path := filepath.Join(t.TempDir(), "events.jsonl")
	j, err := lain.OpenJournal(path)
	if err != nil {
		t.Fatal(err)
	}
	if err = j.Append(lain.Event{Kind: "usage"}); err != nil {
		t.Fatal(err)
	}
	_ = j.Close()
	var output bytes.Buffer
	if err = runJournalMaintenance([]string{"journal-inspect", "--journal", path}, &output); err != nil {
		t.Fatal(err)
	}
	var report lain.JournalInspection
	if json.Unmarshal(output.Bytes(), &report) != nil || report.Events != 1 || report.UncertainTailBytes != 0 {
		t.Fatal(output.String())
	}
	output.Reset()
	if err = runJournalMaintenance([]string{"journal-recover-copy", "--journal", path, "--destination", filepath.Join(t.TempDir(), "copy")}, &output); err != nil {
		t.Fatal(err)
	}
	if err = runJournalMaintenance([]string{"journal-recover-copy", "--journal", path}, &output); err == nil {
		t.Fatal("implicit destination accepted")
	}
}

func TestOfflineArchiveCommandsPreserveBytesAndRejectWrongFlags(t *testing.T) {
	root := t.TempDir()
	path := filepath.Join(root, "original.jsonl")
	j, err := lain.OpenJournal(path)
	if err != nil {
		t.Fatal(err)
	}
	for n := 0; n < 12; n++ {
		if err = j.Append(lain.Event{Kind: "usage"}); err != nil {
			t.Fatal(err)
		}
	}
	if err = j.Close(); err != nil {
		t.Fatal(err)
	}
	original, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	var output bytes.Buffer
	destination := filepath.Join(root, "archive")
	if err = runJournalMaintenance([]string{"journal-segment-copy", "--journal", path, "--destination", destination, "--max-segment-bytes", "4096"}, &output); err != nil {
		t.Fatal(err)
	}
	var report lain.JournalArchiveReport
	if err = json.Unmarshal(output.Bytes(), &report); err != nil || report.Events != 12 || report.SealedSegments < 2 {
		t.Fatal(report, err)
	}
	archive := report.Destination
	for _, command := range []string{"journal-segment-copy", "journal-export-flat"} {
		if !isJournalMaintenanceCommand(command) {
			t.Fatal(command, "not reachable before credential flags")
		}
	}
	output.Reset()
	if err = runJournalMaintenance([]string{"journal-inspect", "--journal", archive}, &output); err != nil {
		t.Fatal(err)
	}
	output.Reset()
	if err = runJournalMaintenance([]string{"journal-export-flat", "--journal", archive, "--destination", filepath.Join(root, "rollback")}, &output); err != nil {
		t.Fatal(err)
	}
	if err = json.Unmarshal(output.Bytes(), &report); err != nil {
		t.Fatal(err)
	}
	got, err := os.ReadFile(report.Destination)
	if err != nil || !bytes.Equal(original, got) {
		t.Fatal("CLI export changed bytes", err)
	}
	for _, args := range [][]string{
		{"journal-segment-copy", "--journal", path},
		{"journal-export-flat", "--journal", archive},
		{"journal-inspect", "--journal", archive, "--max-segment-bytes", "4096"},
		{"journal-export-flat", "--journal", archive, "--destination", filepath.Join(root, "other"), "--max-segment-bytes", "4096"},
		{"journal-segment-copy", "--journal", path, "--destination", destination},
	} {
		if err = runJournalMaintenance(args, &output); err == nil {
			t.Fatal("invalid command accepted", args)
		}
	}
}
