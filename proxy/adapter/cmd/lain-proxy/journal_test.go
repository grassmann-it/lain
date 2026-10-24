package main

import (
	"bytes"
	"encoding/json"
	"lain.local/proxy/internal/lain"
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
