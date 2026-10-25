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
	"sort"
	"strings"
	"syscall"

	"github.com/google/uuid"
	"golang.org/x/sys/unix"
)

// A directory at the configured journal path is an intentional format gate:
// legacy OpenFile(O_RDWR) fails on it instead of replaying only an active tail.
// This format is created exclusively by the offline copy operation below.
const journalSegmentVersion = 1
const DefaultJournalSegmentBytes int64 = 64 << 20
const maxJournalSegmentBytes int64 = 1 << 30

type journalSegment struct {
	File   string `json:"file"`
	First  int64  `json:"first"`
	Last   int64  `json:"last"`
	Bytes  int64  `json:"bytes"`
	SHA256 string `json:"sha256"`
}
type journalSegmentManifest struct {
	Version     int              `json:"version"`
	SourceID    string           `json:"sourceId"`
	MaxBytes    int64            `json:"maxSegmentBytes"`
	Sealed      []journalSegment `json:"sealed"`
	Active      string           `json:"active"`
	ActiveFirst int64            `json:"activeFirst"`
}
type journalSegmentIndex struct {
	file                string
	first, count, bytes int64
	offsets             []int64
}
type journalSegments struct {
	path     string
	lock     *os.File
	manifest journalSegmentManifest
	sealed   []journalSegmentIndex
	active   journalSegmentIndex
	// Tests inject crashes/errors at actual publication boundaries. No runtime
	// environment switch or successful-result override exists.
	checkpoint func(string) error
}
type JournalArchiveReport struct {
	Version         int    `json:"version"`
	Format          string `json:"format"`
	SourceID        string `json:"sourceId"`
	Events          int64  `json:"events"`
	Bytes           int64  `json:"bytes"`
	SHA256          string `json:"sha256"`
	SealedSegments  int    `json:"sealedSegments"`
	MaxSegmentBytes int64  `json:"maxSegmentBytes,omitempty"`
	Destination     string `json:"destination,omitempty"`
}

func segmentSizeValid(size int64) bool { return size >= 4096 && size <= maxJournalSegmentBytes }
func segmentName() string              { return "segment-" + uuid.NewString() + ".jsonl" }
func validSegmentName(name string) bool {
	return strings.HasPrefix(name, "segment-") && strings.HasSuffix(name, ".jsonl") && filepath.Base(name) == name && !strings.ContainsAny(name, `/\\`) && len(name) == len("segment-")+36+len(".jsonl")
}
func segmentFile(path string, writable bool) (*os.File, error) {
	flags := unix.O_RDONLY | unix.O_NOFOLLOW | unix.O_CLOEXEC | unix.O_NONBLOCK
	if writable {
		flags = unix.O_RDWR | unix.O_NOFOLLOW | unix.O_CLOEXEC | unix.O_NONBLOCK
	}
	fd, err := unix.Open(path, flags, 0)
	if err != nil {
		return nil, err
	}
	f := os.NewFile(uintptr(fd), path)
	info, err := f.Stat()
	if err != nil || !info.Mode().IsRegular() {
		f.Close()
		return nil, errors.New("journal segment must be a regular non-symlink file")
	}
	return f, nil
}
func validateSegmentManifest(m journalSegmentManifest) error {
	if m.Version != journalSegmentVersion {
		return errors.New("unsupported journal segment manifest version; use a compatible Core or an explicit legacy-flat export")
	}
	if _, err := uuid.Parse(m.SourceID); err != nil {
		return errors.New("invalid segment source identity")
	}
	if !segmentSizeValid(m.MaxBytes) || m.Sealed == nil {
		return errors.New("invalid segment limit or archive list")
	}
	next := int64(1)
	seen := map[string]bool{}
	for _, part := range m.Sealed {
		if !validSegmentName(part.File) || seen[part.File] || part.First != next || part.Last < part.First || part.Last == int64(^uint64(0)>>1) || part.Bytes <= 0 || part.Bytes > m.MaxBytes {
			return errors.New("noncontiguous or invalid sealed journal segment")
		}
		hash, err := hex.DecodeString(part.SHA256)
		if err != nil || len(hash) != sha256.Size || strings.ToLower(part.SHA256) != part.SHA256 {
			return errors.New("invalid sealed journal SHA256")
		}
		seen[part.File] = true
		next = part.Last + 1
	}
	if !validSegmentName(m.Active) || seen[m.Active] || m.ActiveFirst != next {
		return errors.New("invalid active journal boundary")
	}
	return nil
}
func readSegmentManifest(path string) (journalSegmentManifest, error) {
	var m journalSegmentManifest
	f, err := segmentFile(filepath.Join(path, "manifest.json"), false)
	if err != nil {
		return m, err
	}
	defer f.Close()
	decoder := json.NewDecoder(f)
	decoder.DisallowUnknownFields()
	if err = decoder.Decode(&m); err != nil {
		return m, err
	}
	var extra any
	if err = decoder.Decode(&extra); err != io.EOF {
		return m, errors.New("trailing journal manifest data")
	}
	return m, validateSegmentManifest(m)
}
func scanJournalSegment(f *os.File, source string, first, maxBytes int64) (journalSegmentIndex, string, error) {
	index := journalSegmentIndex{file: filepath.Base(f.Name()), first: first}
	info, err := f.Stat()
	if err != nil {
		return index, "", err
	}
	if info.Size() > maxBytes {
		return index, "", errors.New("journal segment exceeds configured byte limit")
	}
	hash := sha256.New()
	reader := bufio.NewReader(io.TeeReader(io.NewSectionReader(f, 0, info.Size()), hash))
	for {
		line, err := reader.ReadBytes('\n')
		if err == io.EOF && len(line) == 0 {
			break
		}
		if err != nil {
			return index, "", fmt.Errorf("journal segment %s incomplete at byte %d; retain the original and use offline recovery: %w", index.file, index.bytes, err)
		}
		var event Event
		if err = json.Unmarshal(line, &event); err != nil {
			return index, "", fmt.Errorf("invalid journal segment record: %w", err)
		}
		expected := first + index.count
		if expected < 1 || event.SourceID != source || event.Sequence != expected || event.EventID != fmt.Sprintf("%s:%d", source, expected) {
			return index, "", errors.New("journal segment identity/sequence mismatch")
		}
		if index.count%journalIndexStride == 0 {
			index.offsets = append(index.offsets, index.bytes)
		}
		index.count++
		index.bytes += int64(len(line))
	}
	return index, hex.EncodeToString(hash.Sum(nil)), nil
}
func openSegmentedJournal(path string, writable bool) (*Journal, error) {
	info, err := os.Lstat(path)
	if err != nil || !info.IsDir() {
		return nil, errors.New("segmented journal must be a non-symlink directory")
	}
	lock, err := segmentFile(filepath.Join(path, "owner.lock"), false)
	if err != nil {
		return nil, err
	}
	success := false
	defer func() {
		if !success {
			lock.Close()
		}
	}()
	if err = syscall.Flock(int(lock.Fd()), syscall.LOCK_EX|syscall.LOCK_NB); err != nil {
		return nil, fmt.Errorf("stop the owning Core before journal maintenance: %w", err)
	}
	m, err := readSegmentManifest(path)
	if err != nil {
		return nil, err
	}
	s := &journalSegments{path: path, lock: lock, manifest: m}
	j := &Journal{sourceID: m.SourceID, basePath: path, segments: s}
	for _, part := range m.Sealed {
		f, err := segmentFile(filepath.Join(path, part.File), false)
		if err != nil {
			return nil, err
		}
		index, hash, scanErr := scanJournalSegment(f, m.SourceID, part.First, m.MaxBytes)
		closeErr := f.Close()
		if scanErr != nil {
			return nil, scanErr
		}
		if closeErr != nil {
			return nil, closeErr
		}
		if hash != part.SHA256 || index.bytes != part.Bytes || index.count != part.Last-part.First+1 {
			return nil, errors.New("sealed journal hash/size/boundary mismatch; restore a matching complete backup")
		}
		s.sealed = append(s.sealed, index)
		j.count += index.count
		j.end += index.bytes
	}
	f, err := segmentFile(filepath.Join(path, m.Active), writable)
	if err != nil {
		return nil, err
	}
	index, _, err := scanJournalSegment(f, m.SourceID, m.ActiveFirst, m.MaxBytes)
	if err != nil {
		f.Close()
		return nil, err
	}
	s.active = index
	j.count += index.count
	j.end += index.bytes
	j.file = f
	j.syncFile = f.Sync
	success = true
	return j, nil
}
func (s *journalSegments) at(point string) error {
	if s.checkpoint != nil {
		return s.checkpoint(point)
	}
	return nil
}
func writeNewSegmentFile(path string, data io.Reader) error {
	f, err := os.OpenFile(path, os.O_WRONLY|os.O_CREATE|os.O_EXCL, 0600)
	if err != nil {
		return err
	}
	_, err = io.Copy(f, data)
	if err == nil {
		err = f.Sync()
	}
	return errors.Join(err, f.Close())
}
func publishSegmentManifest(path string, m journalSegmentManifest, checkpoint func(string) error) error {
	if err := validateSegmentManifest(m); err != nil {
		return err
	}
	at := func(point string) error {
		if checkpoint != nil {
			return checkpoint(point)
		}
		return nil
	}
	data, err := json.MarshalIndent(m, "", "  ")
	if err != nil {
		return err
	}
	temporary := filepath.Join(path, ".manifest-"+uuid.NewString())
	if err = at("before-manifest-write"); err != nil {
		return err
	}
	if err = writeNewSegmentFile(temporary, bytes.NewReader(append(data, '\n'))); err != nil {
		return err
	}
	if err = at("after-manifest-sync"); err != nil {
		return err
	}
	if err = os.Rename(temporary, filepath.Join(path, "manifest.json")); err != nil {
		return err
	}
	if err = at("after-manifest-rename"); err != nil {
		return err
	}
	if err = syncDirectory(path); err != nil {
		return err
	}
	return at("after-directory-sync")
}
func (j *Journal) prepareSegmentAppend(size int64) error {
	s := j.segments
	if size > s.manifest.MaxBytes {
		return errors.New("event exceeds journal segment byte limit; no bytes written")
	}
	if s.active.bytes+size <= s.manifest.MaxBytes {
		return nil
	}
	// Keep the old active inode forever. Either manifest generation can safely
	// survive a crash; new events are not appended until publication is durable.
	if err := j.syncFile(); err != nil {
		return err
	}
	index, hash, err := scanJournalSegment(j.file, j.sourceID, s.active.first, s.manifest.MaxBytes)
	if err != nil {
		return err
	}
	if index.bytes != s.active.bytes || index.count != s.active.count {
		return errors.New("active journal changed outside its owner")
	}
	next := segmentName()
	f, err := os.OpenFile(filepath.Join(s.path, next), os.O_RDWR|os.O_CREATE|os.O_EXCL, 0600)
	if err != nil {
		return err
	}
	adopted := false
	defer func() {
		if !adopted {
			f.Close()
		}
	}()
	if err = f.Sync(); err != nil {
		return err
	}
	if err = s.at("after-active-sync"); err != nil {
		return err
	}
	m := s.manifest
	m.Sealed = append(append([]journalSegment{}, m.Sealed...), journalSegment{File: s.active.file, First: s.active.first, Last: j.count, Bytes: s.active.bytes, SHA256: hash})
	m.Active = next
	m.ActiveFirst = j.count + 1
	if err = publishSegmentManifest(s.path, m, s.checkpoint); err != nil {
		return err
	}
	old := j.file
	j.file = f
	j.syncFile = f.Sync
	adopted = true
	s.manifest = m
	s.sealed = append(s.sealed, index)
	s.active = journalSegmentIndex{file: next, first: m.ActiveFirst}
	return old.Close()
}
func (j *Journal) readSegments(batch Batch, after int64, limit int) (Batch, error) {
	s := j.segments
	// Binary search the global ranges; at most one descriptor per sealed file
	// plus the open active file is retained, independent of archive length.
	position := sort.Search(len(s.sealed), func(n int) bool { return s.sealed[n].first+s.sealed[n].count > after+1 })
	for ; position <= len(s.sealed) && len(batch.Events) < limit; position++ {
		index := s.active
		f := j.file
		closeFile := false
		if position < len(s.sealed) {
			index = s.sealed[position]
			var err error
			f, err = segmentFile(filepath.Join(s.path, index.file), false)
			if err != nil {
				return batch, j.fail(err)
			}
			closeFile = true
			// A sealed archive must still match before any of its events leave
			// this boundary, including corruption occurring after startup.
			hash := sha256.New()
			n, err := io.Copy(hash, io.NewSectionReader(f, 0, s.manifest.MaxBytes+1))
			if err != nil || n != index.bytes || hex.EncodeToString(hash.Sum(nil)) != s.manifest.Sealed[position].SHA256 {
				f.Close()
				return batch, j.fail(errors.New("sealed journal changed after verification"))
			}
		}
		if index.count == 0 {
			if closeFile {
				f.Close()
			}
			continue
		}
		local := after - index.first + 1
		if local < 0 {
			local = 0
		}
		checkpoint := local / journalIndexStride
		start := index.offsets[checkpoint]
		reader := bufio.NewReader(io.NewSectionReader(f, start, index.bytes-start))
		var readErr error
		for n := checkpoint * journalIndexStride; n < index.count && len(batch.Events) < limit; n++ {
			var line []byte
			line, readErr = reader.ReadBytes('\n')
			if readErr != nil {
				break
			}
			if n < local {
				continue
			}
			var e Event
			readErr = json.Unmarshal(line, &e)
			if readErr != nil {
				break
			}
			expected := index.first + n
			if e.Sequence != expected || e.SourceID != j.sourceID || e.EventID != fmt.Sprintf("%s:%d", j.sourceID, expected) {
				readErr = errors.New("journal replay identity/sequence mismatch")
				break
			}
			batch.Events = append(batch.Events, e)
			batch.NextCursor = e.Sequence
		}
		if closeFile {
			readErr = errors.Join(readErr, f.Close())
		}
		if readErr != nil {
			return batch, j.fail(readErr)
		}
		after = batch.NextCursor
	}
	return batch, nil
}
func (j *Journal) segmentStorage(storage *JournalStorage) {
	s := j.segments
	storage.Format = "segmented-v1"
	storage.SealedSegments = len(s.sealed)
	storage.MaxSegmentBytes = s.manifest.MaxBytes
	storage.IndexEntries = len(s.active.offsets)
	storage.IndexCapacityBytes = cap(s.active.offsets) * 8
	for _, index := range s.sealed {
		storage.IndexEntries += len(index.offsets)
		storage.IndexCapacityBytes += cap(index.offsets) * 8
	}
	// OnDiskBytes includes retained unreferenced files after interrupted
	// publication; Bytes remains the exact logical event stream length.
	var diskBytes int64
	entries, err := os.ReadDir(s.path)
	if err != nil {
		storage.OnDiskBytes = nil
		return
	}
	for _, entry := range entries {
		info, err := entry.Info()
		if err != nil {
			storage.OnDiskBytes = nil
			return
		}
		if info.Mode().IsRegular() {
			diskBytes += info.Size()
		}
	}
	storage.OnDiskBytes = &diskBytes
}

// These commands publish new sibling directories atomically and never adopt,
// replace, truncate or delete the source. Holding its runtime lock throughout
// makes the output a closed, consistent journal snapshot.
func SegmentJournalCopy(path, destination string, maxBytes int64) (JournalArchiveReport, error) {
	return segmentJournalCopy(path, destination, maxBytes, nil)
}
func segmentJournalCopy(path, destination string, maxBytes int64, checkpoint func(string) error) (JournalArchiveReport, error) {
	var report JournalArchiveReport
	if !segmentSizeValid(maxBytes) {
		return report, errors.New("max segment bytes must be between 4096 and 1073741824")
	}
	if err := validateArchiveDestination(path, destination); err != nil {
		return report, err
	}
	stage, err := newJournalArchiveStage(destination)
	if err != nil {
		return report, err
	}
	defer os.RemoveAll(stage)
	f, err := openJournalMaintenance(path)
	if err != nil {
		return report, err
	}
	defer f.Close()
	inspection, _, err := inspectJournalFile(f, path)
	if err != nil {
		return report, err
	}
	if inspection.UncertainTailBytes != 0 {
		return report, errors.New("segment copy refuses an uncertain tail; recover a new flat copy first and retain the original")
	}
	archive := filepath.Join(stage, "events.jsonl")
	if err = os.Mkdir(archive, 0700); err != nil {
		return report, err
	}
	if err = writeNewSegmentFile(filepath.Join(archive, "owner.lock"), strings.NewReader("")); err != nil {
		return report, err
	}
	m := journalSegmentManifest{Version: journalSegmentVersion, SourceID: inspection.SourceID, MaxBytes: maxBytes, Sealed: []journalSegment{}, ActiveFirst: 1}
	reader := bufio.NewReader(io.NewSectionReader(f, 0, inspection.OriginalBytes))
	var part *os.File
	var partBytes, first, last int64
	first = 1
	hash := sha256.New()
	seal := func() error {
		if part == nil {
			return nil
		}
		err := part.Sync()
		err = errors.Join(err, part.Close())
		part = nil
		if err != nil {
			return err
		}
		m.Sealed = append(m.Sealed, journalSegment{File: m.Active, First: first, Last: last, Bytes: partBytes, SHA256: hex.EncodeToString(hash.Sum(nil))})
		first = last + 1
		partBytes = 0
		hash.Reset()
		return nil
	}
	defer func() {
		if part != nil {
			part.Close()
		}
	}()
	for {
		line, readErr := reader.ReadBytes('\n')
		if readErr == io.EOF && len(line) == 0 {
			break
		}
		if readErr != nil {
			return report, readErr
		}
		if int64(len(line)) > maxBytes {
			return report, errors.New("existing event exceeds requested segment byte limit; choose a larger limit")
		}
		if part != nil && partBytes+int64(len(line)) > maxBytes {
			if err = seal(); err != nil {
				return report, err
			}
		}
		if part == nil {
			m.Active = segmentName()
			part, err = os.OpenFile(filepath.Join(archive, m.Active), os.O_WRONLY|os.O_CREATE|os.O_EXCL, 0600)
			if err != nil {
				return report, err
			}
		}
		n, err := part.Write(line)
		if err != nil {
			return report, err
		}
		if n != len(line) {
			return report, io.ErrShortWrite
		}
		hash.Write(line)
		partBytes += int64(n)
		last++
	}
	if err = seal(); err != nil {
		return report, err
	}
	m.Active = segmentName()
	m.ActiveFirst = inspection.Events + 1
	if err = writeNewSegmentFile(filepath.Join(archive, m.Active), strings.NewReader("")); err != nil {
		return report, err
	}
	if err = publishSegmentManifest(archive, m, checkpoint); err != nil {
		return report, err
	}
	verified, err := openSegmentedJournal(archive, false)
	if err != nil {
		return report, err
	}
	report, err = verified.archiveReport()
	err = errors.Join(err, verified.Close())
	if err != nil {
		return report, err
	}
	if report.SHA256 != inspection.OriginalSHA256 || report.Events != inspection.Events || report.Bytes != inspection.OriginalBytes {
		return report, errors.New("segmented copy does not match original event bytes")
	}
	report.Destination = filepath.Join(destination, "events.jsonl")
	if err = writeArchiveReport(stage, report); err != nil {
		return report, err
	}
	if err = publishJournalArchive(stage, destination, checkpoint); err != nil {
		return report, err
	}
	return report, nil
}
func (j *Journal) archiveReport() (JournalArchiveReport, error) {
	s := j.segments
	r := JournalArchiveReport{Version: 1, Format: "segmented-v1", SourceID: j.sourceID, Events: j.count, Bytes: j.end, SealedSegments: len(s.sealed), MaxSegmentBytes: s.manifest.MaxBytes}
	hash := sha256.New()
	if err := j.copySegmentBytes(hash); err != nil {
		return r, err
	}
	r.SHA256 = hex.EncodeToString(hash.Sum(nil))
	return r, nil
}
func (j *Journal) copySegmentBytes(output io.Writer) error {
	s := j.segments
	for n, index := range s.sealed {
		f, err := segmentFile(filepath.Join(s.path, index.file), false)
		if err != nil {
			return err
		}
		hash := sha256.New()
		written, err := io.Copy(io.MultiWriter(output, hash), io.NewSectionReader(f, 0, s.manifest.MaxBytes+1))
		err = errors.Join(err, f.Close())
		if err != nil {
			return err
		}
		if written != index.bytes || hex.EncodeToString(hash.Sum(nil)) != s.manifest.Sealed[n].SHA256 {
			return errors.New("sealed journal changed during export")
		}
	}
	_, err := io.Copy(output, io.NewSectionReader(j.file, 0, s.active.bytes))
	return err
}
func InspectSegmentedJournal(path string) (JournalArchiveReport, error) {
	if !filepath.IsAbs(path) {
		return JournalArchiveReport{}, errors.New("journal path must be absolute")
	}
	j, err := openSegmentedJournal(path, false)
	if err != nil {
		return JournalArchiveReport{}, err
	}
	defer j.Close()
	return j.archiveReport()
}
func ExportJournalFlat(path, destination string) (JournalArchiveReport, error) {
	return exportJournalFlat(path, destination, nil)
}
func exportJournalFlat(path, destination string, checkpoint func(string) error) (JournalArchiveReport, error) {
	var report JournalArchiveReport
	if !filepath.IsAbs(path) {
		return report, errors.New("journal path must be absolute")
	}
	if err := validateArchiveDestination(path, destination); err != nil {
		return report, err
	}
	stage, err := newJournalArchiveStage(destination)
	if err != nil {
		return report, err
	}
	defer os.RemoveAll(stage)
	j, err := openSegmentedJournal(path, false)
	if err != nil {
		return report, err
	}
	defer j.Close()
	report, err = j.archiveReport()
	if err != nil {
		return report, err
	}
	out, err := os.OpenFile(filepath.Join(stage, "events.jsonl"), os.O_WRONLY|os.O_CREATE|os.O_EXCL, 0600)
	if err != nil {
		return report, err
	}
	err = j.copySegmentBytes(out)
	if err == nil {
		err = out.Sync()
	}
	err = errors.Join(err, out.Close())
	if err != nil {
		return report, err
	}
	if err = writeNewSegmentFile(filepath.Join(stage, "events.jsonl.source"), strings.NewReader(j.sourceID)); err != nil {
		return report, err
	}
	// Independently parse and hash the actual flat output, preserving unknown
	// fields and whitespace by copying record bytes rather than reserializing.
	check, err := InspectJournal(filepath.Join(stage, "events.jsonl"))
	if err != nil {
		return report, err
	}
	if check.OriginalSHA256 != report.SHA256 || check.Events != report.Events || check.SourceID != report.SourceID || check.UncertainTailBytes != 0 {
		return report, errors.New("legacy-flat export verification failed")
	}
	report.Format = "flat"
	report.Destination = filepath.Join(destination, "events.jsonl")
	if err = writeArchiveReport(stage, report); err != nil {
		return report, err
	}
	if err = publishJournalArchive(stage, destination, checkpoint); err != nil {
		return report, err
	}
	return report, nil
}
func newJournalArchiveStage(destination string) (string, error) {
	if !filepath.IsAbs(destination) {
		return "", errors.New("destination must be an absolute new directory")
	}
	if _, err := os.Lstat(destination); !errors.Is(err, os.ErrNotExist) {
		return "", errors.New("destination already exists or is inaccessible")
	}
	return os.MkdirTemp(filepath.Dir(destination), ".lain-journal-archive-")
}
func writeArchiveReport(stage string, report JournalArchiveReport) error {
	data, err := json.MarshalIndent(report, "", "  ")
	if err != nil {
		return err
	}
	return writeNewSegmentFile(filepath.Join(stage, "archive.json"), bytes.NewReader(append(data, '\n')))
}
func publishJournalArchive(stage, destination string, checkpoint func(string) error) error {
	if err := syncDirectory(stage); err != nil {
		return err
	}
	if checkpoint != nil {
		if err := checkpoint("before-destination-rename"); err != nil {
			return err
		}
	}
	if err := unix.Renameat2(unix.AT_FDCWD, stage, unix.AT_FDCWD, destination, unix.RENAME_NOREPLACE); err != nil {
		return err
	}
	return syncDirectory(filepath.Dir(destination))
}

// Reject destinations inside the input, including aliases through parent
// symlinks, so even staging cannot modify an original archive directory.
func validateArchiveDestination(source, destination string) error {
	if !filepath.IsAbs(source) || !filepath.IsAbs(destination) {
		return errors.New("journal and destination must be absolute")
	}
	resolved, err := filepath.EvalSymlinks(source)
	if err != nil {
		return err
	}
	parent, err := filepath.EvalSymlinks(filepath.Dir(destination))
	if err != nil {
		return err
	}
	target := filepath.Join(parent, filepath.Base(destination))
	if target == resolved || strings.HasPrefix(target, resolved+string(filepath.Separator)) {
		return errors.New("destination must be outside the original journal")
	}
	return nil
}

// RecoverSegmentedJournalCopy is the same explicit uncertain-tail policy as
// flat recovery, but verifies every sealed hash before exporting a flat prefix.
// A malformed complete record, missing archive or sequence gap is never repaired.
func RecoverSegmentedJournalCopy(path, destination string) (JournalInspection, error) {
	report := JournalInspection{TailState: "none", CommitCertainty: "complete records are replayable; on-disk presence alone does not prove a prior successful fsync acknowledgement"}
	if err := validateArchiveDestination(path, destination); err != nil {
		return report, err
	}
	stage, err := newJournalArchiveStage(destination)
	if err != nil {
		return report, err
	}
	defer os.RemoveAll(stage)
	lock, err := segmentFile(filepath.Join(path, "owner.lock"), false)
	if err != nil {
		return report, err
	}
	defer lock.Close()
	if err = syscall.Flock(int(lock.Fd()), syscall.LOCK_EX|syscall.LOCK_NB); err != nil {
		return report, fmt.Errorf("stop the owning Core before journal maintenance: %w", err)
	}
	m, err := readSegmentManifest(path)
	if err != nil {
		return report, err
	}
	report.SourceID = m.SourceID
	out, err := os.OpenFile(filepath.Join(stage, "events.jsonl"), os.O_WRONLY|os.O_CREATE|os.O_EXCL, 0600)
	if err != nil {
		return report, err
	}
	defer out.Close()
	originalHash, prefixHash := sha256.New(), sha256.New()
	for _, part := range m.Sealed {
		f, err := segmentFile(filepath.Join(path, part.File), false)
		if err != nil {
			return report, err
		}
		index, hash, err := scanJournalSegment(f, m.SourceID, part.First, m.MaxBytes)
		if err != nil {
			f.Close()
			return report, err
		}
		if hash != part.SHA256 || index.bytes != part.Bytes || index.count != part.Last-part.First+1 {
			f.Close()
			return report, errors.New("sealed journal integrity failure; prefix recovery refused")
		}
		copiedHash := sha256.New()
		_, err = io.Copy(io.MultiWriter(out, originalHash, prefixHash, copiedHash), io.NewSectionReader(f, 0, part.Bytes))
		err = errors.Join(err, f.Close())
		if err != nil {
			return report, err
		}
		if hex.EncodeToString(copiedHash.Sum(nil)) != part.SHA256 {
			return report, errors.New("sealed journal changed during recovery")
		}
		report.Events += index.count
		report.OriginalBytes += index.bytes
		report.VerifiedPrefixBytes += index.bytes
	}
	active, err := segmentFile(filepath.Join(path, m.Active), false)
	if err != nil {
		return report, err
	}
	defer active.Close()
	info, err := active.Stat()
	if err != nil {
		return report, err
	}
	if info.Size() > m.MaxBytes {
		return report, errors.New("active journal exceeds byte limit; recovery refused")
	}
	reader := bufio.NewReader(io.TeeReader(io.NewSectionReader(active, 0, info.Size()), originalHash))
	for {
		line, readErr := reader.ReadBytes('\n')
		report.OriginalBytes += int64(len(line))
		if readErr == io.EOF {
			if len(line) > 0 {
				report.TailState = "incomplete_final_record"
				report.UncertainTailBytes = int64(len(line))
			}
			break
		}
		if readErr != nil {
			return report, readErr
		}
		var event Event
		if err = json.Unmarshal(line, &event); err != nil {
			return report, errors.New("invalid complete active record; prefix recovery refused")
		}
		expected := report.Events + 1
		if event.SourceID != m.SourceID || event.Sequence != expected || event.EventID != fmt.Sprintf("%s:%d", m.SourceID, expected) {
			return report, errors.New("active journal identity/sequence mismatch; prefix recovery refused")
		}
		if n, err := out.Write(line); err != nil {
			return report, err
		} else if n != len(line) {
			return report, io.ErrShortWrite
		}
		prefixHash.Write(line)
		report.Events++
		report.VerifiedPrefixBytes += int64(len(line))
	}
	if err = out.Sync(); err != nil {
		return report, err
	}
	if err = out.Close(); err != nil {
		return report, err
	}
	if err = writeNewSegmentFile(filepath.Join(stage, "events.jsonl.source"), strings.NewReader(m.SourceID)); err != nil {
		return report, err
	}
	check, err := InspectJournal(filepath.Join(stage, "events.jsonl"))
	if err != nil {
		return report, err
	}
	if check.OriginalSHA256 != hex.EncodeToString(prefixHash.Sum(nil)) || check.Events != report.Events || check.OriginalBytes != report.VerifiedPrefixBytes {
		return report, errors.New("recovered flat prefix verification failed")
	}
	report.OriginalSHA256 = hex.EncodeToString(originalHash.Sum(nil))
	data, err := json.MarshalIndent(report, "", "  ")
	if err != nil {
		return report, err
	}
	if err = writeNewSegmentFile(filepath.Join(stage, "inspection.json"), bytes.NewReader(append(data, '\n'))); err != nil {
		return report, err
	}
	if err = publishJournalArchive(stage, destination, nil); err != nil {
		return report, err
	}
	return report, nil
}
