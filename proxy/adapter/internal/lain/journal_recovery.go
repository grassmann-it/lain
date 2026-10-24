package lain

import (
	"bufio"
	"bytes"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"syscall"

	"github.com/google/uuid"
	"golang.org/x/sys/unix"
)

type JournalInspection struct {
	SourceID            string `json:"sourceId"`
	Events              int64  `json:"events"`
	OriginalBytes       int64  `json:"originalBytes"`
	VerifiedPrefixBytes int64  `json:"verifiedPrefixBytes"`
	UncertainTailBytes  int64  `json:"uncertainTailBytes"`
	OriginalSHA256      string `json:"originalSha256"`
	TailState           string `json:"tailState"`
	CommitCertainty     string `json:"commitCertainty"`
}

// Maintenance owns the same inode lock as the runtime and never modifies the
// original journal, its permissions, or its source identity.
func openJournalMaintenance(path string) (*os.File, error) {
	if !filepath.IsAbs(path) {
		return nil, errors.New("journal path must be absolute")
	}
	f, err := os.Open(path)
	if err != nil {
		return nil, err
	}
	info, err := f.Stat()
	if err != nil || !info.Mode().IsRegular() {
		f.Close()
		return nil, errors.New("journal must be a regular file")
	}
	if err = syscall.Flock(int(f.Fd()), syscall.LOCK_EX|syscall.LOCK_NB); err != nil {
		f.Close()
		return nil, fmt.Errorf("stop the owning Core before journal maintenance: %w", err)
	}
	return f, nil
}

func inspectJournalFile(f *os.File, path string) (JournalInspection, []byte, error) {
	r := JournalInspection{TailState: "none", CommitCertainty: "complete records are replayable; on-disk presence alone does not prove a prior successful fsync acknowledgement"}
	source, err := os.ReadFile(path + ".source")
	if err != nil {
		return r, nil, err
	}
	if _, err = uuid.Parse(string(source)); err != nil {
		return r, nil, errors.New("invalid journal source identity")
	}
	r.SourceID = string(source)
	hash := sha256.New()
	reader := bufio.NewReader(io.TeeReader(f, hash))
	for {
		line, err := reader.ReadBytes('\n')
		r.OriginalBytes += int64(len(line))
		if err == io.EOF {
			if len(line) > 0 {
				r.TailState = "incomplete_final_record"
				r.UncertainTailBytes = int64(len(line))
			}
			break
		}
		if err != nil {
			return r, nil, err
		}
		var event Event
		if err = json.Unmarshal(line, &event); err != nil {
			return r, nil, fmt.Errorf("invalid complete journal record at byte %d; copy recovery refuses interior corruption", r.VerifiedPrefixBytes)
		}
		expected := r.Events + 1
		if event.SourceID != r.SourceID || event.Sequence != expected || event.EventID != fmt.Sprintf("%s:%d", r.SourceID, expected) {
			return r, nil, errors.New("journal identity/sequence mismatch; copy recovery refuses interior corruption")
		}
		r.Events++
		r.VerifiedPrefixBytes += int64(len(line))
	}
	r.OriginalSHA256 = hex.EncodeToString(hash.Sum(nil))
	return r, source, nil
}

func InspectJournal(path string) (JournalInspection, error) {
	f, err := openJournalMaintenance(path)
	if err != nil {
		return JournalInspection{}, err
	}
	defer f.Close()
	report, _, err := inspectJournalFile(f, path)
	return report, err
}

// RecoverJournalCopy publishes a new private directory with a verified prefix,
// unchanged source identity and an inspection report. The original (including
// any uncertain trailing bytes) is retained. No repair is attempted for a bad
// complete record, unknown source, or sequence gap. No automatic adoption occurs.
func RecoverJournalCopy(path, destination string) (JournalInspection, error) {
	if !filepath.IsAbs(destination) {
		return JournalInspection{}, errors.New("recovery destination must be an absolute new directory")
	}
	if _, err := os.Lstat(destination); !errors.Is(err, os.ErrNotExist) {
		return JournalInspection{}, errors.New("recovery destination already exists or is inaccessible")
	}
	f, err := openJournalMaintenance(path)
	if err != nil {
		return JournalInspection{}, err
	}
	defer f.Close()
	report, source, err := inspectJournalFile(f, path)
	if err != nil {
		return report, err
	}
	parent := filepath.Dir(destination)
	stage, err := os.MkdirTemp(parent, ".lain-journal-copy-")
	if err != nil {
		return report, err
	}
	defer os.RemoveAll(stage)
	write := func(name string, reader io.Reader) error {
		out, err := os.OpenFile(filepath.Join(stage, name), os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0600)
		if err != nil {
			return err
		}
		_, err = io.Copy(out, reader)
		if err == nil {
			err = out.Sync()
		}
		end := out.Close()
		if err == nil {
			err = end
		}
		return err
	}
	if err = write("events.jsonl", io.NewSectionReader(f, 0, report.VerifiedPrefixBytes)); err != nil {
		return report, err
	}
	if err = write("events.jsonl.source", bytes.NewReader(source)); err != nil {
		return report, err
	}
	data, err := json.MarshalIndent(report, "", "  ")
	if err != nil {
		return report, err
	}
	if err = write("inspection.json", bytes.NewReader(append(data, '\n'))); err != nil {
		return report, err
	}
	if err = syncDirectory(stage); err != nil {
		return report, err
	}
	// Linux no-replace rename avoids overwriting even a concurrently created
	// empty destination directory. This is an explicitly Linux maintenance tool.
	if err = unix.Renameat2(unix.AT_FDCWD, stage, unix.AT_FDCWD, destination, unix.RENAME_NOREPLACE); err != nil {
		return report, err
	}
	if err = syncDirectory(parent); err != nil {
		return report, err
	}
	return report, nil
}

func syncDirectory(path string) error {
	f, err := os.Open(path)
	if err != nil {
		return err
	}
	defer f.Close()
	return f.Sync()
}
