package lain

import (
	"bytes"
	"context"
	"encoding/json"
	"io"
	"os"
	"time"
)

// OpenRecording starts one independent control-file reader. Missing or invalid
// configuration always means recording off and never a failure of inference
// startup. It never creates directories, a configuration file, or a disk spool.
// The file contains CapturePolicySnapshot plus optional sourceLimits. Shared
// archive limits are translated into source bounds; inference configuration is
// never read or written by this worker.
func OpenRecording(path string) (*Recorder, func()) {
	r := NewRecorder()
	if path == "" {
		return r, r.Close
	}
	ctx, cancel := context.WithCancel(context.Background())
	done := make(chan struct{})
	reload := func(previous []byte) []byte {
		data, code := readRecordingConfiguration(path)
		if code != "" {
			r.configFault(code)
			return nil
		}
		if bytes.Equal(data, previous) {
			return previous
		}
		policy, err := decodeRecordingConfiguration(data)
		if err != nil {
			r.configFault("invalid_recording_configuration")
			return data
		}
		_ = r.Configure(policy)
		return data
	}
	previous := reload(nil)
	go func() {
		defer close(done)
		ticker := time.NewTicker(250 * time.Millisecond)
		defer ticker.Stop()
		for {
			select {
			case <-ctx.Done():
				return
			case <-ticker.C:
				previous = reload(previous)
			}
		}
	}()
	return r, func() { cancel(); <-done; r.Close() }
}

func readRecordingConfiguration(path string) ([]byte, string) {
	before, err := os.Lstat(path)
	if os.IsNotExist(err) {
		return []byte(`{"enabled":false,"mode":"metadata_only","scope":"owned_gateway"}`), ""
	}
	if err != nil {
		return nil, "recording_configuration_unavailable"
	}
	if !before.Mode().IsRegular() || before.Mode().Perm()&0077 != 0 || before.Size() > 64<<10 {
		return nil, "invalid_recording_configuration"
	}
	f, err := os.Open(path)
	if err != nil {
		return nil, "recording_configuration_unavailable"
	}
	defer f.Close()
	actual, err := f.Stat()
	if err != nil || !os.SameFile(before, actual) || !actual.Mode().IsRegular() {
		return nil, "invalid_recording_configuration"
	}
	body, err := io.ReadAll(io.LimitReader(f, 64<<10+1))
	if err != nil {
		return nil, "recording_configuration_unavailable"
	}
	if len(body) > 64<<10 {
		return nil, "invalid_recording_configuration"
	}
	return body, ""
}

func decodeRecordingConfiguration(data []byte) (RecordingPolicy, error) {
	var p RecordingPolicy
	if err := json.Unmarshal(data, &p); err != nil {
		return p, err
	}
	var envelope struct {
		Limits struct {
			MaxRecordBytes int `json:"maxRecordBytes"`
			QueueBytes     int `json:"queueBytes"`
		} `json:"limits"`
		SourceLimits *RecordingLimits `json:"sourceLimits"`
	}
	if err := json.Unmarshal(data, &envelope); err != nil {
		return p, err
	}
	if envelope.SourceLimits != nil {
		p.Limits = *envelope.SourceLimits
	} else {
		// Source limits remain individually configurable. The shared policy also
		// bounds this transient feed, so it cannot capture beyond archive admission.
		if envelope.Limits.MaxRecordBytes > 0 {
			p.Limits.MaxBodyBytes = envelope.Limits.MaxRecordBytes
			p.Limits.MaxPageBytes = 2*envelope.Limits.MaxRecordBytes + recordingEventOverhead
			p.Limits.MaxActiveBytes = 4 * envelope.Limits.MaxRecordBytes
		}
		if envelope.Limits.QueueBytes > 0 {
			p.Limits.MaxBytes = envelope.Limits.QueueBytes
		}
	}
	return normalizeRecordingPolicy(p)
}
