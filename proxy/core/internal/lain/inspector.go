package lain

import (
	"encoding/json"
	"errors"
	"regexp"
	"strings"
	"sync"
	"time"
	"unicode"

	"github.com/google/uuid"
)

const inspectorTextLimit = 2048

type InspectorEvent struct {
	Sequence       int64         `json:"sequence"`
	ObservedAt     time.Time     `json:"observedAt"`
	Kind           string        `json:"kind"`
	Scope          string        `json:"scope"`
	RequestID      string        `json:"requestId"`
	AttemptID      string        `json:"attemptId"`
	AccountID      string        `json:"accountId,omitempty"`
	RouteID        *string       `json:"routeId,omitempty"`
	Provider       string        `json:"provider,omitempty"`
	RequestedModel string        `json:"requestedModel,omitempty"`
	Model          string        `json:"model,omitempty"`
	Dimensions     *Dimensions   `json:"dimensions,omitempty"`
	Measurements   *Measurements `json:"measurements,omitempty"`
	Outcome        *string       `json:"outcome,omitempty"`
	InputTokens    *int64        `json:"inputTokens,omitempty"`
	OutputTokens   *int64        `json:"outputTokens,omitempty"`
	TotalTokens    *int64        `json:"totalTokens,omitempty"`
	StatusCode     *int          `json:"statusCode,omitempty"`
	Text           string        `json:"text,omitempty"`
	ToolName       string        `json:"toolName,omitempty"`
	ToolCallID     string        `json:"toolCallId,omitempty"`
	Truncated      bool          `json:"truncated,omitempty"`
}

type InspectorBatch struct {
	InstanceID     string           `json:"instanceId"`
	EarliestCursor int64            `json:"earliestCursor"`
	NextCursor     int64            `json:"nextCursor"`
	HighWatermark  int64            `json:"highWatermark"`
	DroppedCount   int64            `json:"droppedCount"`
	Events         []InspectorEvent `json:"events"`
}

// Inspector has no persistence or destructive dequeue. It is independent of the
// journal: losing display data never acknowledges or discards usage records.
type Inspector struct {
	mu                  sync.Mutex
	instanceID          string
	events              []InspectorEvent
	sizes               []int
	received            []time.Time
	bytes               int
	sequence, dropped   int64
	maxEvents, maxBytes int
	ttl                 time.Duration
	now                 func() time.Time
}

func newInspector() *Inspector {
	return &Inspector{instanceID: uuid.NewString(), maxEvents: 4096, maxBytes: 4 * 1024 * 1024, ttl: 15 * time.Minute, now: time.Now}
}

func (s *Inspector) prune(now time.Time) {
	n := 0
	for n < len(s.events) && (len(s.events)-n > s.maxEvents || s.bytes > s.maxBytes || now.Sub(s.received[n]) >= s.ttl) {
		s.bytes -= s.sizes[n]
		s.events[n] = InspectorEvent{} // release payload references promptly
		n++
	}
	if n > 0 {
		s.dropped += int64(n)
		s.events = append([]InspectorEvent(nil), s.events[n:]...)
		s.sizes = append([]int(nil), s.sizes[n:]...)
		s.received = append([]time.Time(nil), s.received[n:]...)
	}
}

func (s *Inspector) append(e InspectorEvent) {
	if s == nil {
		return
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	s.sequence++
	e.Sequence = s.sequence
	e.Dimensions = copyDimensions(e.Dimensions)
	if e.Measurements != nil {
		m := *e.Measurements
		e.Measurements = &m
	}
	data, err := json.Marshal(e)
	if err != nil {
		s.dropped++
		return
	}
	s.events = append(s.events, e)
	s.sizes = append(s.sizes, len(data))
	now := s.now()
	s.received = append(s.received, now)
	s.bytes += len(data)
	s.prune(now)
}

func (s *Inspector) Read(after int64, limit int) (InspectorBatch, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.prune(s.now())
	b := InspectorBatch{InstanceID: s.instanceID, EarliestCursor: s.sequence + 1, NextCursor: after, HighWatermark: s.sequence, DroppedCount: s.dropped, Events: []InspectorEvent{}}
	if len(s.events) > 0 {
		b.EarliestCursor = s.events[0].Sequence
	}
	if after < 0 || limit < 1 || limit > 1000 {
		return b, errors.New("invalid cursor or limit")
	}
	// Return the new instance identity even when a previous process's cursor is
	// beyond this instance's high watermark. Consumers compare identity first.
	if after > s.sequence {
		b.NextCursor = 0
		return b, nil
	}
	for _, e := range s.events {
		if e.Sequence <= after {
			continue
		}
		e.Dimensions = copyDimensions(e.Dimensions)
		b.Events = append(b.Events, e)
		b.NextCursor = e.Sequence
		if len(b.Events) == limit {
			break
		}
	}
	return b, nil
}

func liveEvent(e Event, kind string) InspectorEvent {
	return InspectorEvent{ObservedAt: e.ObservedAt, Kind: kind, Scope: "attempt", RequestID: e.RequestID, AttemptID: e.AttemptID,
		AccountID: e.AccountID, RouteID: e.RouteID, Provider: e.Provider, RequestedModel: e.RequestedModel, Model: e.Model,
		Dimensions: e.Dimensions, Measurements: e.Measurements, Outcome: e.Outcome, InputTokens: e.InputTokens,
		OutputTokens: e.OutputTokens, TotalTokens: e.TotalTokens, StatusCode: e.StatusCode}
}

var opaqueSecret = regexp.MustCompile(`[A-Za-z0-9_+/=-]{24,}`)

// Content stays in separate bounded RAM, never Event. Buffering complete lines
// before redaction prevents a secret split across transport chunks escaping a
// per-chunk regexp. Tool arguments, prompts, headers and raw error bodies are not
// captured. An overlong line is omitted in full rather than emitted piecemeal.
type captureBuffer struct {
	pending      string
	dropping     bool
	suppressNext bool
}

func (b *captureBuffer) consume(value string, finish bool) []string {
	var lines []string
	for len(value) > 0 {
		n := strings.IndexByte(value, '\n')
		part, newline := value, n >= 0
		if newline {
			part, value = value[:n+1], value[n+1:]
		} else {
			value = ""
		}
		if !b.dropping {
			if len(b.pending)+len(part) > inspectorTextLimit {
				b.pending = ""
				b.dropping = true
				lines = append(lines, "[content omitted: line limit]\n")
			} else {
				b.pending += part
			}
		}
		if newline {
			if !b.dropping {
				lines = append(lines, b.redact(b.pending))
			}
			b.pending, b.dropping = "", false
		}
	}
	if finish && b.pending != "" {
		lines = append(lines, b.redact(b.pending))
		b.pending = ""
	}
	return lines
}

func (b *captureBuffer) redact(line string) string {
	// Match the exact display form. Removing invisible characters only after
	// matching can turn a concealed marker into a visible, unredacted credential.
	line = strings.Map(func(r rune) rune {
		if r == '\n' || r == '\t' {
			return r
		}
		if unicode.IsControl(r) || unicode.Is(unicode.Cf, r) {
			return -1
		}
		return r
	}, line)
	marker := credentialLike.MatchString(line)
	if marker || b.suppressNext || opaqueSecret.MatchString(line) {
		b.suppressNext = marker
		return "[redacted sensitive content]\n"
	}
	b.suppressNext = false
	return line
}
