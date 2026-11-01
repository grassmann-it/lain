package lain

// Inference observations are strictly best effort. No callback waits for the
// recorder mutex, the configuration worker, an HTTP reader or another callback.
// Failed cleanup has bounded storage and is serviced by the existing control
// worker or the next status/read call; no callback creates a goroutine.
type recordingCleanup struct{ requestID, attemptID string }

func (r *Recorder) tryInferenceLock() bool {
	if !r.captureEnabled.Load() {
		return false
	}
	if r.cleanupOverflow.Load() || !r.mu.TryLock() {
		r.contentionDrops.Add(1)
		return false
	}
	return true
}
func (r *Recorder) markAttemptLost(id string) {
	if a, ok := r.attemptIndex.Load(id); ok {
		a.(*recordingAttempt).lost.Store(true)
	}
}
func (r *Recorder) deferCleanup(c recordingCleanup) {
	// IDs originate from generated admission/dispatch IDs. Do not retain arbitrary
	// caller strings even when a standalone caller bypasses that qualification.
	if !r.captureEnabled.Load() || len(c.requestID) > 192 || len(c.attemptID) > 192 {
		return
	}
	select {
	case r.cleanup <- c:
	default:
		r.cleanupOverflow.Store(true)
	}
}
func (r *Recorder) discardAttemptLocked(id, reason string) {
	a := r.attempts[id]
	if a == nil {
		return
	}
	delete(r.attempts, id)
	r.attemptIndex.Delete(id)
	r.activeBytes -= cap(a.body)
	if !r.policy.Enabled {
		return
	}
	o := a.observation
	o.Kind = "gap"
	o.ContentOmissionReason = reason
	o.raw = nil
	r.enqueue(o)
}
func (r *Recorder) completeRequestLocked(id string) {
	delete(r.requests, id)
	for attempt, a := range r.attempts {
		if a.observation.RequestID == id {
			r.discardAttemptLocked(attempt, "observer_cleanup_gap")
		}
	}
}
func (r *Recorder) maintainLocked() {
	if r.cleanupOverflow.Swap(false) {
		// Saturation loses individual cleanup identities. Drop all bounded live
		// captures rather than leaking slots or representing partial content as full.
		for id := range r.attempts {
			r.discardAttemptLocked(id, "observer_cleanup_overflow")
		}
		clear(r.requests)
	}
	for n := 0; n < cap(r.cleanup); n++ {
		select {
		case c := <-r.cleanup:
			if c.requestID != "" {
				r.completeRequestLocked(c.requestID)
			}
			if c.attemptID != "" {
				r.discardAttemptLocked(c.attemptID, "observer_contention")
			}
		default:
			return
		}
	}
}
func (r *Recorder) maintain() { r.mu.Lock(); defer r.mu.Unlock(); r.maintainLocked() }

func copyRecordingTokens(t *RecordingTokenCounts) *RecordingTokenCounts {
	if t == nil {
		return nil
	}
	c := *t
	for _, p := range []**int64{&c.Input, &c.Output, &c.Cached, &c.Reasoning, &c.Total, &c.CacheCreation} {
		if *p != nil {
			*p = ptr(**p)
		}
	}
	return &c
}

// ObserveUsage receives only the existing content-free dispatch Event after its
// durable Usage append. It never receives a SDK usage.Record or auth object.
func (r *Recorder) ObserveUsage(e Event) {
	if r == nil {
		return
	}
	if !r.tryInferenceLock() {
		return
	}
	defer r.mu.Unlock()
	a := r.attempts[e.AttemptID]
	if a == nil || !r.policy.Enabled || a.observation.RequestID != e.RequestID {
		return
	}
	t := copyRecordingTokens(a.observation.Tokens)
	if t == nil {
		t = &RecordingTokenCounts{}
	}
	known := false
	for _, field := range []struct {
		source *int64
		target **int64
	}{
		{e.InputTokens, &t.Input}, {e.OutputTokens, &t.Output}, {e.CacheReadTokens, &t.Cached},
		{e.ReasoningTokens, &t.Reasoning}, {e.TotalTokens, &t.Total}, {e.CacheCreationTokens, &t.CacheCreation},
	} {
		if field.source != nil {
			*field.target = ptr(*field.source)
			known = true
		}
	}
	if known {
		a.observation.Tokens = t
	}
	statusKnown := e.StatusCode != nil && *e.StatusCode >= 100 && *e.StatusCode <= 599
	if statusKnown {
		a.observation.StatusCode = ptr(*e.StatusCode)
	}
	if !known && !statusKnown {
		return
	}
	o := a.observation
	o.Kind = "usage"
	o.ChunkCount = 0
	o.ByteCount = 0
	o.raw = nil
	r.enqueue(o)
}
