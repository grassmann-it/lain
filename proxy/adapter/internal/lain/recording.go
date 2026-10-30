package lain

// This feed is a transient private handoff. Only the Host's normalizer and
// redactor may turn its content into a durable recording. It is independent of
// the fail-closed Usage journal and of Inspector retention.
import (
	"bytes"
	"encoding/json"
	"errors"
	"strconv"
	"sync"
	"time"
	"unicode/utf8"

	"github.com/google/uuid"
)

const recordingSchemaVersion = 1
const recordingEventOverhead = 8192 // conservative bounded metadata accounting

type RecordingLimits struct {
	MaxEvents      int `json:"maxEvents"`
	MaxBytes       int `json:"maxBytes"`
	MaxBodyBytes   int `json:"maxBodyBytes"`
	MaxActive      int `json:"maxActive"`
	MaxActiveBytes int `json:"maxActiveBytes"`
	MaxPageBytes   int `json:"maxPageBytes"`
}

// RecordingPolicy is a recording-only snapshot; changing it never applies an
// inference profile. Only the explicit shared-connection scope is supported.
// Inference bearer tokens and User-Agent are deliberately not client identities.
type RecordingPolicy struct {
	SchemaVersion  int             `json:"schemaVersion"`
	Revision       int64           `json:"revision"`
	Enabled        bool            `json:"enabled"`
	Mode           string          `json:"mode"`
	Scope          string          `json:"scope"`
	RouteIDs       []string        `json:"routeIds,omitempty"`
	Models         []string        `json:"models,omitempty"`
	StopGeneration int64           `json:"stopGeneration"`
	CaptureRawSSE  bool            `json:"captureRawSse"`
	Limits         RecordingLimits `json:"limits"`
}

type RecordingContent struct {
	Body           string `json:"body"`
	Encoding       string `json:"encoding"`
	MediaType      string `json:"mediaType"`
	Representation string `json:"representation"`
}

type RecordingObservation struct {
	SchemaVersion         int               `json:"schemaVersion"`
	SourceEpoch           string            `json:"sourceEpoch"`
	Sequence              int64             `json:"sequence"`
	EventID               string            `json:"eventId"`
	ObservedAt            time.Time         `json:"observedAt"`
	MonotonicNanoseconds  int64             `json:"monotonicNanoseconds"`
	PolicyRevision        int64             `json:"policyRevision"`
	StopGeneration        int64             `json:"stopGeneration"`
	RequestID             string            `json:"requestId"`
	AttemptID             string            `json:"attemptId,omitempty"`
	Boundary              string            `json:"boundary"`
	Protocol              string            `json:"protocol"`
	Kind                  string            `json:"kind"`
	RequestedModel        string            `json:"requestedModel,omitempty"`
	Model                 string            `json:"model,omitempty"`
	RouteID               *string           `json:"routeId,omitempty"`
	AccountID             string            `json:"accountId,omitempty"`
	Provider              string            `json:"provider,omitempty"`
	Dimensions            *Dimensions       `json:"dimensions,omitempty"`
	Content               *RecordingContent `json:"-"`
	CaptureID             string            `json:"captureId"`
	BodyEncoding          string            `json:"bodyEncoding,omitempty"`
	Body                  []byte            `json:"body,omitempty"`
	Complete              bool              `json:"complete"`
	Truncated             bool              `json:"truncated"`
	ContentOmissionReason string            `json:"gapReason,omitempty"`
	ChunkCount            int64             `json:"chunkCount,omitempty"`
	ByteCount             int64             `json:"byteCount,omitempty"`
	Outcome               string            `json:"outcome,omitempty"`
	raw                   []byte
	stream                bool
	rawSSE                bool
	size                  int
}

type RecordingGap struct {
	FromSequence    int64  `json:"fromSequence"`
	ThroughSequence int64  `json:"throughSequence"`
	Reason          string `json:"reason"`
}
type RecordingBatch struct {
	SchemaVersion  int                    `json:"schemaVersion"`
	SourceEpoch    string                 `json:"sourceEpoch"`
	AfterSequence  int64                  `json:"afterSequence"`
	NextSequence   int64                  `json:"nextSequence"`
	OldestSequence int64                  `json:"oldestSequence"`
	LatestSequence int64                  `json:"latestSequence"`
	DroppedCount   int64                  `json:"droppedCount"`
	UnknownLoss    bool                   `json:"unknownLoss"`
	GapReason      string                 `json:"gapReason,omitempty"`
	Gap            *RecordingGap          `json:"gap,omitempty"`
	Events         []RecordingObservation `json:"observations"`
}
type RecordingStatus struct {
	SchemaVersion    int             `json:"schemaVersion"`
	SourceEpoch      string          `json:"sourceEpoch"`
	Enabled          bool            `json:"enabled"`
	Mode             string          `json:"mode"`
	PolicyRevision   int64           `json:"policyRevision"`
	StopGeneration   int64           `json:"stopGeneration"`
	State            string          `json:"state"`
	ErrorCode        string          `json:"errorCode,omitempty"`
	QueuedEvents     int             `json:"queuedEvents"`
	QueuedBytes      int             `json:"queuedBytes"`
	ActiveRequests   int             `json:"activeRequests"`
	ActiveAttempts   int             `json:"activeAttempts"`
	ActiveBytes      int             `json:"activeBytes"`
	DroppedEvents    int64           `json:"droppedEvents"`
	LatestSequence   int64           `json:"latestSequence"`
	UnknownCrashLoss bool            `json:"unknownCrashLoss"`
	Limits           RecordingLimits `json:"limits"`
}

type recordingRequest struct {
	protocol, model      string
	revision, generation int64
	content              bool
}
type recordingAttempt struct {
	observation RecordingObservation
	content     bool
	body        []byte
	omitted     string
}

type Recorder struct {
	mu                       sync.Mutex
	readGate                 chan struct{}
	httpGate                 chan struct{}
	epoch                    string
	started                  time.Time
	policy                   RecordingPolicy
	errorCode                string
	stopped                  bool
	closed                   bool
	ring                     []RecordingObservation
	head, count, queuedBytes int
	requests                 map[string]recordingRequest
	attempts                 map[string]*recordingAttempt
	activeBytes              int
	sequence, dropped        int64
}

func defaultRecordingLimits() RecordingLimits {
	return RecordingLimits{MaxEvents: 1024, MaxBytes: 16 << 20, MaxBodyBytes: 1 << 20, MaxActive: 64, MaxActiveBytes: 16 << 20, MaxPageBytes: 3 << 20}
}
func normalizeRecordingPolicy(p RecordingPolicy) (RecordingPolicy, error) {
	if p.SchemaVersion == 0 {
		p.SchemaVersion = recordingSchemaVersion
	}
	if p.SchemaVersion != recordingSchemaVersion || p.Revision < 0 || p.StopGeneration < 0 {
		return p, errors.New("invalid_recording_policy")
	}
	if p.Mode == "" {
		p.Mode = "metadata_only"
	}
	if p.Mode != "metadata_only" && p.Mode != "content" {
		return p, errors.New("invalid_recording_mode")
	}
	if p.Enabled && (p.Scope != "owned_gateway") {
		return p, errors.New("unsupported_recording_scope")
	}
	if len(p.Models) > 256 || len(p.RouteIDs) > 256 {
		return p, errors.New("invalid_recording_filter")
	}
	for _, route := range p.RouteIDs {
		if len(route) == 0 || len(route) > 192 {
			return p, errors.New("invalid_recording_filter")
		}
	}
	for _, model := range p.Models {
		if len(model) == 0 || len(model) > 192 {
			return p, errors.New("invalid_recording_filter")
		}
	}
	d := defaultRecordingLimits()
	if p.Limits.MaxEvents == 0 {
		p.Limits.MaxEvents = d.MaxEvents
	}
	if p.Limits.MaxBytes == 0 {
		p.Limits.MaxBytes = d.MaxBytes
	}
	if p.Limits.MaxBodyBytes == 0 {
		p.Limits.MaxBodyBytes = d.MaxBodyBytes
	}
	if p.Limits.MaxActive == 0 {
		p.Limits.MaxActive = d.MaxActive
	}
	if p.Limits.MaxActiveBytes == 0 {
		p.Limits.MaxActiveBytes = d.MaxActiveBytes
	}
	if p.Limits.MaxPageBytes == 0 {
		p.Limits.MaxPageBytes = d.MaxPageBytes
	}
	l := p.Limits
	if l.MaxEvents < 1 || l.MaxEvents > 65536 || l.MaxBytes < 4096 || l.MaxBytes > 256<<20 || l.MaxBodyBytes < 1 || l.MaxBodyBytes > 32<<20 || l.MaxActive < 1 || l.MaxActive > 1024 || l.MaxActiveBytes < l.MaxBodyBytes || l.MaxActiveBytes > 256<<20 || l.MaxPageBytes < 2*l.MaxBodyBytes+recordingEventOverhead || l.MaxPageBytes > 64<<20 || l.MaxBytes < 2*l.MaxBodyBytes+recordingEventOverhead {
		return p, errors.New("invalid_recording_limits")
	}
	p.Models = append([]string(nil), p.Models...)
	p.RouteIDs = append([]string(nil), p.RouteIDs...)
	return p, nil
}
func NewRecorder() *Recorder {
	return &Recorder{epoch: uuid.NewString(), started: time.Now(), readGate: make(chan struct{}, 1), httpGate: make(chan struct{}, 1), requests: map[string]recordingRequest{}, attempts: map[string]*recordingAttempt{}, policy: RecordingPolicy{SchemaVersion: 1, Mode: "metadata_only", Limits: defaultRecordingLimits()}}
}

// Configure is called by the independent configuration reader, never by a
// provider callback. Every policy change discards pending content and assemblies.
func (r *Recorder) Configure(p RecordingPolicy) error {
	p, err := normalizeRecordingPolicy(p)
	if err != nil {
		r.configFault("invalid_recording_configuration")
		return err
	}
	r.mu.Lock()
	defer r.mu.Unlock()
	if r.closed {
		return errors.New("recording_closed")
	}
	if p.Revision < r.policy.Revision || p.StopGeneration < r.policy.StopGeneration {
		r.disableLocked("stale_recording_configuration")
		return errors.New("stale_recording_configuration")
	}
	// A management stop is sticky until a newer policy revision or generation.
	if r.stopped && p.Enabled && p.Revision <= r.policy.Revision && p.StopGeneration <= r.policy.StopGeneration {
		return nil
	}
	r.clearLocked()
	r.policy, r.errorCode, r.stopped = p, "", false
	if p.Enabled {
		r.ring = make([]RecordingObservation, p.Limits.MaxEvents)
	}
	return nil
}
func (r *Recorder) configFault(code string) { r.mu.Lock(); defer r.mu.Unlock(); r.disableLocked(code) }
func (r *Recorder) clearLocked() {
	r.dropped += int64(r.count)
	r.ring = nil
	r.head = 0
	r.count = 0
	r.queuedBytes = 0
	r.activeBytes = 0
	r.requests = map[string]recordingRequest{}
	r.attempts = map[string]*recordingAttempt{}
}
func (r *Recorder) disableLocked(code string) {
	r.clearLocked()
	r.policy.Enabled = false
	r.errorCode = code
	r.stopped = true
}
func (r *Recorder) Disable() {
	if r == nil {
		return
	}
	r.mu.Lock()
	defer r.mu.Unlock()
	r.disableLocked("")
}
func (r *Recorder) Close() {
	if r == nil {
		return
	}
	r.mu.Lock()
	defer r.mu.Unlock()
	r.disableLocked("")
	r.closed = true
}
func (r *Recorder) acceptsModel(model string) bool {
	if len(r.policy.Models) == 0 {
		return true
	}
	for _, m := range r.policy.Models {
		if m == model {
			return true
		}
	}
	return false
}
func (r *Recorder) acceptsRoute(routeID string) bool {
	if len(r.policy.RouteIDs) == 0 {
		return true
	}
	for _, id := range r.policy.RouteIDs {
		if id == routeID {
			return true
		}
	}
	return false
}
func (r *Recorder) stamp(o RecordingObservation) RecordingObservation {
	r.sequence++
	now := time.Now()
	o.SchemaVersion = 1
	o.CaptureID = o.RequestID
	o.Complete = o.ContentOmissionReason == ""
	o.Truncated = o.ContentOmissionReason == "body_limit" || o.ContentOmissionReason == "assembly_capacity"
	o.SourceEpoch = r.epoch
	o.Sequence = r.sequence
	o.EventID = r.epoch + ":" + strconv.FormatInt(r.sequence, 10)
	o.ObservedAt = now.UTC()
	o.MonotonicNanoseconds = now.Sub(r.started).Nanoseconds()
	return o
}
func (r *Recorder) enqueue(o RecordingObservation) {
	o = r.stamp(o)
	o.size = recordingEventOverhead + 2*cap(o.raw)
	for r.count > 0 && (r.count == len(r.ring) || r.queuedBytes+o.size > r.policy.Limits.MaxBytes) {
		old := r.ring[r.head]
		r.queuedBytes -= old.size
		r.ring[r.head] = RecordingObservation{}
		r.head = (r.head + 1) % len(r.ring)
		r.count--
		r.dropped++
	}
	if len(r.ring) == 0 || o.size > r.policy.Limits.MaxBytes {
		r.dropped++
		return
	}
	r.ring[(r.head+r.count)%len(r.ring)] = o
	r.count++
	r.queuedBytes += o.size
}

// WantsAdmission lets hook wiring avoid computing dimensions or copying content
// when recording is disabled. Admit still repeats the check under its own lock.
func (r *Recorder) WantsAdmission(model, routeID string) bool {
	if r == nil {
		return false
	}
	r.mu.Lock()
	defer r.mu.Unlock()
	return r.policy.Enabled && !r.closed && r.acceptsModel(model) && r.acceptsRoute(routeID)
}

// Admit must be invoked only after inference authorization and route admission,
// with the original body before the model is rewritten. It stores no headers.
func (r *Recorder) Admit(requestID, protocol, model, routeID string, body []byte, dimensions ...*Dimensions) {
	if r == nil {
		return
	}
	r.mu.Lock()
	defer r.mu.Unlock()
	if !r.policy.Enabled || r.closed || !r.acceptsModel(model) || !r.acceptsRoute(routeID) {
		return
	}
	if len(requestID) > 192 || len(protocol) > 64 || len(model) > 192 || len(routeID) > 192 || requestID == "" {
		return
	}
	if len(r.requests) >= r.policy.Limits.MaxActive {
		r.enqueue(RecordingObservation{PolicyRevision: r.policy.Revision, StopGeneration: r.policy.StopGeneration, RequestID: requestID, Boundary: "client_ingress", Protocol: protocol, Kind: "gap", ContentOmissionReason: "active_capacity"})
		return
	}
	request := recordingRequest{protocol: protocol, model: model, revision: r.policy.Revision, generation: r.policy.StopGeneration, content: r.policy.Mode == "content"}
	r.requests[requestID] = request
	o := RecordingObservation{PolicyRevision: request.revision, StopGeneration: request.generation, RequestID: requestID, Boundary: "client_ingress", Protocol: protocol, Kind: "request", RequestedModel: model, ByteCount: int64(len(body))}
	if routeID != "" {
		o.RouteID = ptr(routeID)
	}
	if len(dimensions) > 0 {
		o.Dimensions = copyDimensions(dimensions[0])
	}
	if request.content && len(body) <= r.policy.Limits.MaxBodyBytes {
		o.raw = bytes.Clone(body)
	} else if request.content {
		o.ContentOmissionReason = "body_limit"
	} else {
		o.ContentOmissionReason = "metadata_only"
	}
	r.enqueue(o)
}
func (r *Recorder) CompleteRequest(requestID string) {
	if r == nil {
		return
	}
	r.mu.Lock()
	defer r.mu.Unlock()
	delete(r.requests, requestID)
}

// BeginAttempt receives a content-free, allowlisted Usage identity fact. In
// particular it never receives auth.Auth, usage.Record or a header map.
func (r *Recorder) BeginAttempt(e Event) {
	if r == nil {
		return
	}
	r.mu.Lock()
	defer r.mu.Unlock()
	request, ok := r.requests[e.RequestID]
	if len(e.RequestedModel) > 192 || len(e.Model) > 192 || len(e.AccountID) > 192 || len(e.Provider) > 192 || (e.RouteID != nil && len(*e.RouteID) > 192) {
		return
	}
	if !ok || !r.policy.Enabled || (e.RouteID != nil && !r.acceptsRoute(*e.RouteID)) || e.AttemptID == "" || len(e.AttemptID) > 192 {
		return
	}
	if len(r.attempts) >= r.policy.Limits.MaxActive {
		r.enqueue(RecordingObservation{PolicyRevision: request.revision, StopGeneration: request.generation, RequestID: e.RequestID, AttemptID: e.AttemptID, Boundary: "executor_output", Protocol: request.protocol, Kind: "gap", ContentOmissionReason: "active_capacity"})
		return
	}
	// All metadata comes from the existing validated dispatch and dimensions gate.
	o := RecordingObservation{PolicyRevision: request.revision, StopGeneration: request.generation, RequestID: e.RequestID, AttemptID: e.AttemptID, Boundary: "executor_output", Protocol: request.protocol, Kind: "attempt_started", RequestedModel: e.RequestedModel, Model: e.Model, AccountID: e.AccountID, Provider: e.Provider, Dimensions: copyDimensions(e.Dimensions)}
	if e.RouteID != nil {
		o.RouteID = ptr(*e.RouteID)
	}
	r.attempts[e.AttemptID] = &recordingAttempt{observation: o, content: request.content}
	r.enqueue(o)
}

// ObserveOutput does bounded copying only. Protocol parsing, UTF-8 validation,
// configuration I/O and JSON serialization happen outside the inference path.
func (r *Recorder) ObserveOutput(attemptID string, payload []byte, stream bool) {
	if r == nil {
		return
	}
	r.mu.Lock()
	defer r.mu.Unlock()
	a := r.attempts[attemptID]
	if a == nil || !r.policy.Enabled {
		return
	}
	a.observation.ChunkCount++
	a.observation.ByteCount += int64(len(payload))
	a.observation.stream = stream
	if stream {
		chunk := a.observation
		chunk.Kind = "chunk_metadata"
		chunk.ByteCount = int64(len(payload))
		r.enqueue(chunk)
	}
	if !a.content || a.omitted != "" {
		return
	}
	wanted := len(a.body) + len(payload)
	if wanted > r.policy.Limits.MaxBodyBytes {
		r.activeBytes -= cap(a.body)
		a.body = nil
		a.omitted = "body_limit"
		return
	}
	if wanted > cap(a.body) {
		capacity := cap(a.body) * 2
		if capacity < 4096 {
			capacity = 4096
		}
		if capacity < wanted {
			capacity = wanted
		}
		if capacity > r.policy.Limits.MaxBodyBytes {
			capacity = r.policy.Limits.MaxBodyBytes
		}
		if r.activeBytes+capacity > r.policy.Limits.MaxActiveBytes {
			r.activeBytes -= cap(a.body)
			a.body = nil
			a.omitted = "assembly_capacity"
			return
		}
		// Include the old allocation in peak accounting while allocating the new one.
		next := make([]byte, len(a.body), capacity)
		copy(next, a.body)
		r.activeBytes += capacity - cap(a.body)
		a.body = next
	}
	a.body = append(a.body, payload...)
}
func (r *Recorder) FinishAttempt(attemptID, outcome string) {
	if r == nil {
		return
	}
	r.mu.Lock()
	defer r.mu.Unlock()
	a := r.attempts[attemptID]
	if a == nil {
		return
	}
	delete(r.attempts, attemptID)
	r.activeBytes -= cap(a.body)
	o := a.observation
	o.Kind = "response"
	switch outcome {
	case "success", "failed", "canceled":
		o.Outcome = outcome
	default:
		o.Outcome = "unknown"
	}
	o.raw = a.body
	o.rawSSE = r.policy.CaptureRawSSE
	o.ContentOmissionReason = a.omitted
	if !a.content {
		o.ContentOmissionReason = "metadata_only"
	} else if len(o.raw) == 0 && o.ContentOmissionReason == "" {
		o.ContentOmissionReason = "body_unavailable"
	}
	r.enqueue(o)
}
func (r *Recorder) Status() RecordingStatus {
	if r == nil {
		return RecordingStatus{SchemaVersion: 1, Mode: "metadata_only", State: "off", UnknownCrashLoss: true}
	}
	r.mu.Lock()
	defer r.mu.Unlock()
	state := "off"
	if r.policy.Enabled {
		state = "on"
	}
	if r.errorCode != "" {
		state = "unavailable"
	}
	return RecordingStatus{SchemaVersion: 1, SourceEpoch: r.epoch, Enabled: r.policy.Enabled, Mode: r.policy.Mode, PolicyRevision: r.policy.Revision, StopGeneration: r.policy.StopGeneration, State: state, ErrorCode: r.errorCode, QueuedEvents: r.count, QueuedBytes: r.queuedBytes, ActiveRequests: len(r.requests), ActiveAttempts: len(r.attempts), ActiveBytes: r.activeBytes, DroppedEvents: r.dropped, LatestSequence: r.sequence, UnknownCrashLoss: true, Limits: r.policy.Limits}
}

var ErrRecordingReadBusy = errors.New("recording_read_busy")

// Read is non-destructive. The consumer commits independently and advances its
// cursor only after durable storage. There is one bounded in-flight read page.
func (r *Recorder) Read(epoch string, after int64, limit int) (RecordingBatch, error) {
	if r == nil {
		return RecordingBatch{}, errors.New("recording_unavailable")
	}
	select {
	case r.readGate <- struct{}{}:
		defer func() { <-r.readGate }()
	default:
		return RecordingBatch{}, ErrRecordingReadBusy
	}
	r.mu.Lock()
	if epoch != "" && epoch != r.epoch {
		r.mu.Unlock()
		return RecordingBatch{}, errors.New("recording_epoch_changed")
	}
	if after < 0 || after > r.sequence || limit < 1 || limit > 1000 {
		r.mu.Unlock()
		return RecordingBatch{}, errors.New("invalid_recording_cursor")
	}
	oldest := r.sequence + 1
	if r.count > 0 {
		oldest = r.ring[r.head].Sequence
	}
	batch := RecordingBatch{SchemaVersion: 1, SourceEpoch: r.epoch, AfterSequence: after, NextSequence: after, OldestSequence: oldest, LatestSequence: r.sequence, Events: []RecordingObservation{}, DroppedCount: r.dropped, UnknownLoss: true}
	if after+1 < oldest {
		batch.GapReason = "source_evicted_or_discarded"
		batch.Gap = &RecordingGap{FromSequence: after + 1, ThroughSequence: oldest - 1, Reason: "source_evicted_or_discarded"}
		batch.NextSequence = oldest - 1
	}
	total := 0
	for n := 0; n < r.count && len(batch.Events) < limit; n++ {
		o := r.ring[(r.head+n)%len(r.ring)]
		if o.Sequence <= after {
			continue
		}
		if total+o.size > r.policy.Limits.MaxPageBytes {
			break
		}
		total += o.size
		batch.Events = append(batch.Events, o)
		batch.NextSequence = o.Sequence
	}
	r.mu.Unlock()
	for n := range batch.Events {
		batch.Events[n].Dimensions = copyDimensions(batch.Events[n].Dimensions)
		if batch.Events[n].RouteID != nil {
			batch.Events[n].RouteID = ptr(*batch.Events[n].RouteID)
		}
		materializeRecordingContent(&batch.Events[n])
	}
	return batch, nil
}

// A raw stream is not durable content. By default only a terminal complete
// Responses object is exposed. Other streams retain counts with explicit body
// unavailability. Raw SSE requires separate explicit policy consent.
func materializeRecordingContent(o *RecordingObservation) {
	raw := o.raw
	o.raw = nil
	if len(raw) == 0 {
		return
	}
	defer func() {
		if o.ContentOmissionReason != "" {
			o.Complete = false
		}
	}()
	if o.stream && !o.rawSSE {
		var complete json.RawMessage
		for remaining := raw; len(remaining) > 0; {
			line, rest, found := bytes.Cut(remaining, []byte("\n"))
			remaining = rest
			if !found {
				remaining = nil
			}
			line = bytes.TrimSpace(line)
			line = bytes.TrimSpace(bytes.TrimPrefix(line, []byte("data:")))
			var event struct {
				Type     string          `json:"type"`
				Response json.RawMessage `json:"response"`
			}
			if json.Unmarshal(line, &event) == nil && (event.Type == "response.completed" || event.Type == "response.done" || event.Type == "response.incomplete" || event.Type == "response.failed") && len(event.Response) > 0 && !bytes.Equal(event.Response, []byte("null")) {
				complete = event.Response
			}
		}
		if len(complete) == 0 {
			o.ContentOmissionReason = "stream_body_unavailable"
			return
		}
		raw = complete
	}
	if !utf8.Valid(raw) {
		o.ContentOmissionReason = "invalid_utf8"
		o.Kind = "gap"
		return
	}
	media, representation := "application/json", "executor_response"
	if o.Boundary == "client_ingress" {
		representation = "original_client_request"
	} else if o.stream && o.rawSSE {
		media = "text/event-stream"
		representation = "assembled_executor_stream"
	} else if o.stream {
		representation = "terminal_response_object"
	}
	o.Content = &RecordingContent{Encoding: "utf-8", MediaType: media, Representation: representation}
	o.BodyEncoding = "utf8_json"
	if media == "text/event-stream" {
		o.BodyEncoding = "utf8_sse"
	}
	o.Body = bytes.Clone(raw)
}
