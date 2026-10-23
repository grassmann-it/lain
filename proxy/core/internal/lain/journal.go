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
	SchemaVersion       int       `json:"schemaVersion"`
	SourceID            string    `json:"sourceId"`
	Sequence            int64     `json:"sequence"`
	EventID             string    `json:"eventId"`
	Kind                string    `json:"kind"`
	RequestID           string    `json:"requestId"`
	AttemptID           string    `json:"attemptId"`
	AccountID           string    `json:"accountId"`
	CredentialID        string    `json:"credentialId"`
	RouteID             *string   `json:"routeId"`
	ObservedAt          time.Time `json:"observedAt"`
	Provider            string    `json:"provider"`
	RequestedModel      string    `json:"requestedModel"`
	Model               string    `json:"model"`
	Outcome             *string   `json:"outcome"`
	InputTokens         *int64    `json:"inputTokens"`
	OutputTokens        *int64    `json:"outputTokens"`
	TotalTokens         *int64    `json:"totalTokens"`
	CacheReadTokens     *int64    `json:"cacheReadTokens"`
	CacheCreationTokens *int64    `json:"cacheCreationTokens"`
	ReasoningTokens     *int64    `json:"reasoningTokens"`
	UsageReported       bool      `json:"usageReported"`
	StatusCode          *int      `json:"statusCode"`
	LatencyMs           *float64  `json:"latencyMs"`
	TTFTMs              *float64  `json:"ttftMs"`
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
	sourceID string
	offsets  []int64
	end      int64
	fault    error
	syncFile func() error
}

func OpenJournal(path string) (*Journal, error) {
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
	j := &Journal{file: f, syncFile: f.Sync}
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
		expected := int64(len(j.offsets)) + 1
		if event.SourceID != j.sourceID || event.Sequence != expected || event.EventID != fmt.Sprintf("%s:%d", j.sourceID, expected) {
			return nil, errors.New("journal identity/sequence mismatch")
		}
		j.offsets = append(j.offsets, j.end)
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
	event.Sequence = int64(len(j.offsets)) + 1
	event.EventID = fmt.Sprintf("%s:%d", j.sourceID, event.Sequence)
	if event.ObservedAt.IsZero() {
		event.ObservedAt = time.Now().UTC()
	}
	data, err := json.Marshal(event)
	if err != nil {
		return j.fail(err)
	}
	data = append(data, '\n')
	n, err := j.file.WriteAt(data, j.end)
	if err != nil {
		return j.fail(err)
	}
	if n != len(data) {
		return j.fail(io.ErrShortWrite)
	}
	if err = j.syncFile(); err != nil {
		return j.fail(err)
	}
	j.offsets = append(j.offsets, j.end)
	j.end += int64(len(data))
	return nil
}
func (j *Journal) Read(after int64, limit int) (Batch, error) {
	j.mu.Lock()
	defer j.mu.Unlock()
	b := Batch{SourceID: j.sourceID, Events: []Event{}, NextCursor: after, HighWatermark: int64(len(j.offsets))}
	if after < 0 || after > b.HighWatermark || limit < 1 || limit > 1000 {
		return b, errors.New("invalid cursor or limit")
	}
	for index := after; index < b.HighWatermark && len(b.Events) < limit; index++ {
		start := j.offsets[index]
		end := j.end
		if index+1 < b.HighWatermark {
			end = j.offsets[index+1]
		}
		data := make([]byte, end-start)
		if _, err := j.file.ReadAt(data, start); err != nil {
			return b, err
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
func (j *Journal) Close() error { j.mu.Lock(); defer j.mu.Unlock(); return j.file.Close() }
