package lain

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"io"
	"os"
	"path/filepath"
	"sync"
	"syscall"
)

const maxRouterPins = 4096
const maxRouterPinBytes = 4 << 20

type routerPin struct {
	RouterID    string `json:"routerId"`
	ProfileID   string `json:"profileId"`
	ThreadID    string `json:"threadId"`
	RouteID     string `json:"routeId"`
	AccountID   string `json:"accountId"`
	Fingerprint string `json:"bindingFingerprint"`
}
type routerPinFile struct {
	Version  int         `json:"schemaVersion"`
	SourceID string      `json:"sourceId"`
	Pins     []routerPin `json:"pins"`
}

// The existing journal process lock owns this sidecar as well. No prompt, key,
// response or tool data is retained; pins never expire. Movement requires a
// serialized pre-output quota transition.
type routerPins struct {
	mu             sync.Mutex
	flightsMu      sync.Mutex
	flights        map[string]chan struct{}
	path, sourceID string
}

func openRouterPins(j *Journal, routers map[string]CollectionRouter) (*routerPins, error) {
	p := &routerPins{path: j.basePath + ".router-pins.json", sourceID: j.sourceID}
	if _, err := os.Lstat(p.path); err == nil {
		_, err = p.read()
		return p, err
	} else if !errors.Is(err, os.ErrNotExist) {
		return nil, err
	}
	marker := p.path + ".initialized"
	if _, err := os.Lstat(marker); !errors.Is(err, os.ErrNotExist) {
		return nil, errors.New("router pin state missing; explicit recovery required")
	}
	// Upgrading an existing individual-route runtime is allowed. Never rebuild
	// pins from partial usage history after this router has actually dispatched.
	for after := int64(0); ; {
		batch, err := j.Read(after, 1000)
		if err != nil {
			return nil, err
		}
		for _, event := range batch.Events {
			if _, found := routers[event.RequestedModel]; found {
				return nil, errors.New("router history exists without pin state")
			}
		}
		if batch.NextCursor >= batch.HighWatermark {
			break
		}
		if batch.NextCursor <= after {
			return nil, errors.New("cannot validate router history")
		}
		after = batch.NextCursor
	}
	f, err := os.OpenFile(marker, os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0600)
	if err != nil {
		return nil, err
	}
	_, err = f.WriteString(j.sourceID)
	if err == nil {
		err = f.Sync()
	}
	closeErr := f.Close()
	if err == nil {
		err = closeErr
	}
	if err != nil {
		return nil, err
	}
	if err = syncRouterDir(marker); err != nil {
		return nil, err
	}
	if err = p.write(routerPinFile{Version: 1, SourceID: j.sourceID, Pins: []routerPin{}}); err != nil {
		return nil, err
	}
	return p, nil
}

func (p *routerPins) read() (routerPinFile, error) {
	var state routerPinFile
	info, err := os.Lstat(p.path)
	if err != nil {
		return state, err
	}
	if !info.Mode().IsRegular() || info.Mode().Perm()&0077 != 0 || info.Size() > maxRouterPinBytes {
		return state, errors.New("unsafe router pin file")
	}
	f, err := os.OpenFile(p.path, os.O_RDONLY|syscall.O_NOFOLLOW, 0)
	if err != nil {
		return state, err
	}
	defer f.Close()
	raw, err := io.ReadAll(io.LimitReader(f, maxRouterPinBytes+1))
	if err != nil {
		return state, err
	}
	if len(raw) > maxRouterPinBytes {
		return state, errors.New("router pin capacity exceeded")
	}
	decoder := json.NewDecoder(bytes.NewReader(raw))
	decoder.DisallowUnknownFields()
	if err = decoder.Decode(&state); err != nil {
		return state, err
	}
	if decoder.Decode(new(any)) != io.EOF || state.Version != 1 || state.SourceID != p.sourceID || len(state.Pins) > maxRouterPins {
		return state, errors.New("invalid router pin state")
	}
	seen := map[string]bool{}
	for _, pin := range state.Pins {
		key := pin.RouterID + "\n" + pin.ProfileID + "\n" + pin.ThreadID
		if seen[key] || safeDimension(pin.RouterID) == "" || safeDimension(pin.ProfileID) == "" || !validRouterThread(pin.ThreadID) ||
			safeDimension(pin.RouteID) == "" || safeDimension(pin.AccountID) == "" || !validRouterFingerprint(pin.Fingerprint) {
			return state, errors.New("invalid router pin identity")
		}
		seen[key] = true
	}
	return state, nil
}

func syncRouterDir(path string) error {
	dir, err := os.Open(filepath.Dir(path))
	if err != nil {
		return err
	}
	defer dir.Close()
	return dir.Sync()
}
func (p *routerPins) write(state routerPinFile) error {
	raw, err := json.Marshal(state)
	if err != nil {
		return err
	}
	if len(raw) > maxRouterPinBytes {
		return errors.New("router pin capacity reached")
	}
	f, err := os.CreateTemp(filepath.Dir(p.path), ".router-pins-*")
	if err != nil {
		return err
	}
	name := f.Name()
	defer os.Remove(name)
	if _, err = f.Write(raw); err == nil {
		err = f.Sync()
	}
	closeErr := f.Close()
	if err == nil {
		err = closeErr
	}
	if err != nil {
		return err
	}
	if err = os.Rename(name, p.path); err != nil {
		return err
	}
	return syncRouterDir(p.path)
}

func (p *routerPins) resolve(router CollectionRouter, thread string, continuation bool, choose func() (routerPin, error), validate func(routerPin) error) (routerPin, error) {
	p.mu.Lock()
	defer p.mu.Unlock()
	state, err := p.read()
	if err != nil {
		return routerPin{}, err
	}
	for _, pin := range state.Pins {
		if pin.RouterID == router.ID && pin.ProfileID == router.ProfileID && pin.ThreadID == thread {
			return pin, validate(pin)
		}
	}
	if continuation {
		return routerPin{}, errors.New("router conversation has no persistent pin; start a new thread")
	}
	if len(state.Pins) >= maxRouterPins {
		return routerPin{}, errors.New("router pin capacity reached")
	}
	pin, err := choose()
	if err != nil {
		return pin, err
	}
	pin.RouterID, pin.ProfileID, pin.ThreadID = router.ID, router.ProfileID, thread
	state.Pins = append(state.Pins, pin)
	if err = p.write(state); err != nil {
		return routerPin{}, err
	}
	return pin, nil
}

// acquire serializes a complete request for this conversation, including output.
// The bounded durable table also bounds active keys; canceled waiters never dispatch.
func (p *routerPins) acquire(ctx context.Context, router CollectionRouter, thread string) (func(), error) {
	key := router.ID + "\n" + router.ProfileID + "\n" + thread
	for {
		if err := ctx.Err(); err != nil {
			return nil, err
		}
		p.flightsMu.Lock()
		if p.flights == nil {
			p.flights = map[string]chan struct{}{}
		}
		if wait, exists := p.flights[key]; exists {
			p.flightsMu.Unlock()
			select {
			case <-ctx.Done():
				return nil, ctx.Err()
			case <-wait:
			}
			continue
		}
		if len(p.flights) >= maxRouterPins {
			p.flightsMu.Unlock()
			return nil, errors.New("router concurrency capacity reached")
		}
		done := make(chan struct{})
		p.flights[key] = done
		p.flightsMu.Unlock()
		return func() { p.flightsMu.Lock(); delete(p.flights, key); close(done); p.flightsMu.Unlock() }, nil
	}
}

// replace is a durable compare-and-swap; a stale request cannot move a newer pin.
func (p *routerPins) replace(router CollectionRouter, thread string, expected, next routerPin) error {
	p.mu.Lock()
	defer p.mu.Unlock()
	state, err := p.read()
	if err != nil {
		return err
	}
	for n, pin := range state.Pins {
		if pin.RouterID != router.ID || pin.ProfileID != router.ProfileID || pin.ThreadID != thread {
			continue
		}
		if pin.RouteID != expected.RouteID || pin.AccountID != expected.AccountID || pin.Fingerprint != expected.Fingerprint {
			return errors.New("router pin changed concurrently")
		}
		next.RouterID, next.ProfileID, next.ThreadID = router.ID, router.ProfileID, thread
		state.Pins[n] = next
		return p.write(state)
	}
	return errors.New("router pin missing during transition")
}
