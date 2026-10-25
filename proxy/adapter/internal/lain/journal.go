// Package lain is the durable, private integration boundary around the pinned core.
package lain

import (
	"bufio"
	"encoding/json"
	"errors"
	"fmt"
	"github.com/google/uuid"
	"io"
	"os"
	"path/filepath"
	"sync"
	"syscall"
	"time"
)

type Event struct {
	SchemaVersion       int           `json:"schemaVersion"`
	SourceID            string        `json:"sourceId"`
	Sequence            int64         `json:"sequence"`
	EventID             string        `json:"eventId"`
	Kind                string        `json:"kind"`
	RequestID           string        `json:"requestId"`
	AttemptID           string        `json:"attemptId"`
	AccountID           string        `json:"accountId"`
	CredentialID        string        `json:"credentialId"`
	RouteID             *string       `json:"routeId"`
	ObservedAt          time.Time     `json:"observedAt"`
	Provider            string        `json:"provider"`
	RequestedModel      string        `json:"requestedModel"`
	Model               string        `json:"model"`
	Outcome             *string       `json:"outcome"`
	InputTokens         *int64        `json:"inputTokens"`
	OutputTokens        *int64        `json:"outputTokens"`
	TotalTokens         *int64        `json:"totalTokens"`
	CacheReadTokens     *int64        `json:"cacheReadTokens"`
	CacheCreationTokens *int64        `json:"cacheCreationTokens"`
	ReasoningTokens     *int64        `json:"reasoningTokens"`
	UsageReported       bool          `json:"usageReported"`
	StatusCode          *int          `json:"statusCode"`
	LatencyMs           *float64      `json:"latencyMs"`
	TTFTMs              *float64      `json:"ttftMs"`
	Dimensions          *Dimensions   `json:"dimensions,omitempty"`
	Measurements        *Measurements `json:"measurements,omitempty"`
	CompletionState     *string       `json:"completionState,omitempty"`
	ErrorCategory       *string       `json:"errorCategory,omitempty"`
}
type Batch struct {
	SourceID      string  `json:"sourceId"`
	Events        []Event `json:"events"`
	NextCursor    int64   `json:"nextCursor"`
	HighWatermark int64   `json:"highWatermark"`
}

type Journal struct {
	mu       sync.Mutex
	file     *os.File
	basePath string
	segments *journalSegments
	sourceID string
	offsets  []int64
	count    int64
	end      int64
	fault    error
	syncFile func() error
}

// Sparse in-memory checkpoints leave the existing file/source/cursor format
// unchanged, including rollback to binaries that index every event.
const journalIndexStride int64 = 256

type JournalStorage struct {
	SourceID                 string  `json:"sourceId"`
	Events                   int64   `json:"events"`
	Bytes                    int64   `json:"bytes"`
	IndexEntries             int     `json:"indexEntries"`
	IndexCapacityBytes       int     `json:"indexCapacityBytes"`
	IndexStride              int64   `json:"indexStride"`
	Healthy                  bool    `json:"healthy"`
	Format                   string  `json:"format"`
	SealedSegments           int     `json:"sealedSegments"`
	MaxSegmentBytes          int64   `json:"maxSegmentBytes,omitempty"`
	OnDiskBytes              *int64  `json:"onDiskBytes,omitempty"`
	FilesystemAvailableBytes *uint64 `json:"filesystemAvailableBytes,omitempty"`
}

func (j *Journal) Storage() JournalStorage {
	j.mu.Lock()
	defer j.mu.Unlock()
	storage := JournalStorage{SourceID: j.sourceID, Events: j.count, Bytes: j.end, IndexEntries: len(j.offsets), IndexCapacityBytes: cap(j.offsets) * 8, IndexStride: journalIndexStride, Healthy: j.fault == nil, Format: "flat"}
	if info, err := j.file.Stat(); err == nil {
		storage.OnDiskBytes = ptr(info.Size())
	}
	if j.segments != nil {
		j.segmentStorage(&storage)
	}
	var fs syscall.Statfs_t
	if syscall.Fstatfs(int(j.file.Fd()), &fs) == nil {
		storage.FilesystemAvailableBytes = ptr(fs.Bavail * uint64(fs.Bsize))
	}
	return storage
}

func OpenJournal(path string) (*Journal, error) {
	if info, err := os.Lstat(path); err == nil && info.IsDir() {
		return openSegmentedJournal(path, true)
	}
	if err := os.MkdirAll(filepath.Dir(path), 0700); err != nil {
		return nil, err
	}
	f, err := os.OpenFile(path, os.O_RDWR|os.O_CREATE, 0600)
	if err != nil {
		return nil, err
	}
	success := false
	defer func() {
		if !success {
			_ = f.Close()
		}
	}()
	if err = syscall.Flock(int(f.Fd()), syscall.LOCK_EX|syscall.LOCK_NB); err != nil {
		return nil, fmt.Errorf("journal already owned: %w", err)
	}
	if err = f.Chmod(0600); err != nil {
		return nil, err
	}
	j := &Journal{file: f, basePath: path, syncFile: f.Sync}
	sourcePath := path + ".source"
	source, err := os.ReadFile(sourcePath)
	if errors.Is(err, os.ErrNotExist) {
		info, e := f.Stat()
		if e != nil {
			return nil, e
		}
		if info.Size() != 0 {
			return nil, errors.New("nonempty journal has no source identity")
		}
		source = []byte(uuid.NewString())
		sf, e := os.OpenFile(sourcePath, os.O_WRONLY|os.O_CREATE|os.O_EXCL, 0600)
		if e != nil {
			return nil, e
		}
		_, e = sf.Write(source)
		if e == nil {
			e = sf.Sync()
		}
		closeErr := sf.Close()
		if e != nil {
			return nil, e
		}
		if closeErr != nil {
			return nil, closeErr
		}
	} else if err != nil {
		return nil, err
	}
	if _, err = uuid.Parse(string(source)); err != nil {
		return nil, errors.New("invalid journal source identity")
	}
	j.sourceID = string(source)
	dir, err := os.Open(filepath.Dir(path))
	if err != nil {
		return nil, err
	}
	err = dir.Sync()
	_ = dir.Close()
	if err != nil {
		return nil, err
	}
	reader := bufio.NewReader(f)
	for {
		line, e := reader.ReadBytes('\n')
		if e == io.EOF && len(line) == 0 {
			break
		}
		// A torn append is never silently discarded: operator recovery is needed.
		if e != nil {
			return nil, fmt.Errorf("journal incomplete at byte %d: %w", j.end, e)
		}
		var event Event
		if err = json.Unmarshal(line, &event); err != nil {
			return nil, fmt.Errorf("journal invalid at byte %d: %w", j.end, err)
		}
		expected := j.count + 1
		if event.SourceID != j.sourceID || event.Sequence != expected || event.EventID != fmt.Sprintf("%s:%d", j.sourceID, expected) {
			return nil, errors.New("journal identity/sequence mismatch")
		}
		if j.count%journalIndexStride == 0 {
			j.offsets = append(j.offsets, j.end)
		}
		j.count++
		j.end += int64(len(line))
	}
	success = true
	return j, nil
}
func (j *Journal) fail(err error) error {
	if j.fault == nil {
		j.fault = fmt.Errorf("durable journal unavailable: %w", err)
	}
	return j.fault
}
func (j *Journal) Health() error { j.mu.Lock(); defer j.mu.Unlock(); return j.fault }
func (j *Journal) Append(event Event) error {
	j.mu.Lock()
	defer j.mu.Unlock()
	if j.fault != nil {
		return j.fault
	}
	event.SchemaVersion = 1
	event.SourceID = j.sourceID
	event.Sequence = j.count + 1
	event.EventID = fmt.Sprintf("%s:%d", j.sourceID, event.Sequence)
	if event.ObservedAt.IsZero() {
		event.ObservedAt = time.Now().UTC()
	}
	data, err := json.Marshal(event)
	if err != nil {
		return j.fail(err)
	}
	data = append(data, '\n')
	writeAt := j.end
	if j.segments != nil {
		if err = j.prepareSegmentAppend(int64(len(data))); err != nil {
			return j.fail(err)
		}
		writeAt = j.segments.active.bytes
	}
	n, err := j.file.WriteAt(data, writeAt)
	if err != nil {
		return j.fail(err)
	}
	if n != len(data) {
		return j.fail(io.ErrShortWrite)
	}
	if err = j.syncFile(); err != nil {
		return j.fail(err)
	}
	if j.segments != nil {
		active := &j.segments.active
		if active.count%journalIndexStride == 0 {
			active.offsets = append(active.offsets, active.bytes)
		}
		active.count++
		active.bytes += int64(len(data))
	} else if j.count%journalIndexStride == 0 {
		j.offsets = append(j.offsets, j.end)
	}
	j.count++
	j.end += int64(len(data))
	return nil
}
func (j *Journal) Read(after int64, limit int) (Batch, error) {
	j.mu.Lock()
	defer j.mu.Unlock()
	b := Batch{SourceID: j.sourceID, Events: []Event{}, NextCursor: after, HighWatermark: j.count}
	if after < 0 || after > b.HighWatermark || limit < 1 || limit > 1000 {
		return b, errors.New("invalid cursor or limit")
	}
	if after == b.HighWatermark {
		return b, nil
	}
	if j.segments != nil {
		return j.readSegments(b, after, limit)
	}
	checkpoint := after / journalIndexStride
	start := j.offsets[checkpoint]
	reader := bufio.NewReader(io.NewSectionReader(j.file, start, j.end-start))
	for index := checkpoint * journalIndexStride; index < b.HighWatermark && len(b.Events) < limit; index++ {
		data, err := reader.ReadBytes('\n')
		if err != nil {
			return b, err
		}
		if index < after {
			continue
		}
		var e Event
		if err := json.Unmarshal(data, &e); err != nil {
			return b, err
		}
		b.Events = append(b.Events, e)
		b.NextCursor = e.Sequence
	}
	return b, nil
}
func (j *Journal) Close() error {
	j.mu.Lock()
	defer j.mu.Unlock()
	err := j.file.Close()
	if j.segments != nil {
		err = errors.Join(err, j.segments.lock.Close())
	}
	return err
}
