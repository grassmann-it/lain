package lain

import (
	"bytes"
	"encoding/json"
	"net/http/httptest"
	"reflect"
	"strings"
	"sync"
	"testing"
	"time"

	"github.com/gin-gonic/gin"
	"github.com/google/uuid"
)

func availabilitySnapshot(i *Integration, ids ...string) routerAvailabilitySnapshot {
	now := time.Now().UTC().Add(-time.Second)
	s := routerAvailabilitySnapshot{Version: 1, Generation: i.routerAvailability.generation, Sequence: 1, ObservedAt: now, ExpiresAt: now.Add(2 * time.Minute), Entries: []routerAvailabilityEntry{}}
	router := i.Bindings.Routers["auto"]
	for _, id := range ids {
		r := i.Bindings.Routes[id]
		s.Entries = append(s.Entries, routerAvailabilityEntry{RouterID: router.ID, ProfileID: router.ProfileID, RouteID: r.ID, MemberFingerprint: router.MemberFingerprints[r.ID], AccountID: r.Accounts[0], Provider: r.Provider, Model: r.Model, BlockedUntil: s.ExpiresAt, Reason: "quota_exhausted"})
	}
	return s
}
func availabilityPost(g *gin.Engine, s routerAvailabilitySnapshot, token string) *httptest.ResponseRecorder {
	raw, _ := json.Marshal(s)
	req := httptest.NewRequest("POST", "/lain/router-availability", bytes.NewReader(raw))
	req.Header.Set("Authorization", "Bearer "+token)
	w := httptest.NewRecorder()
	g.ServeHTTP(w, req)
	return w
}

func TestRouterAvailabilityAuthenticatedExactIdentityAndGeneration(t *testing.T) {
	i, _, raw, g := failoverFixture(t)
	s := availabilitySnapshot(i, "a", "b")
	if w := availabilityPost(g, s, "inference-token"); w.Code != 401 {
		t.Fatal(w.Code)
	}
	if w := availabilityPost(g, s, "management-token"); w.Code != 204 {
		t.Fatal(w.Code, w.Body.String())
	}
	w := failoverRequest(g, uuid.NewString(), `{"model":"auto","input":"hello"}`, "")
	if w.Code != 200 || !reflect.DeepEqual(raw.routes(), []string{"c"}) {
		t.Fatal("cached exhausted member dispatched", w.Code, raw.routes(), w.Body.String())
	}
	req := httptest.NewRequest("GET", "/lain/router-availability", nil)
	req.Header.Set("Authorization", "Bearer management-token")
	got := httptest.NewRecorder()
	g.ServeHTTP(got, req)
	var info struct {
		Version    int    `json:"schemaVersion"`
		Generation string `json:"generation"`
		Sequence   uint64 `json:"sequence"`
	}
	if json.Unmarshal(got.Body.Bytes(), &info) != nil || info.Version != 1 || info.Generation != s.Generation || info.Sequence != 1 {
		t.Fatal(got.Body.String())
	}
	restarted := newRouterAvailability()
	if restarted.generation == s.Generation {
		t.Fatal("generation survived restart")
	}
	s.Generation = restarted.generation
	s.Sequence = 2
	if w := availabilityPost(g, s, "management-token"); w.Code != 409 {
		t.Fatal("old process post accepted", w.Code)
	}
	if i.routerAvailability.snapshot.Sequence != 1 {
		t.Fatal("rejected post mutated state")
	}
}

func TestRouterAvailabilityMismatchStaleResetAndPinnedMigration(t *testing.T) {
	for _, field := range []string{"router", "profile", "route", "fingerprint", "account", "provider", "model", "expired"} {
		t.Run(field, func(t *testing.T) {
			i, _, raw, g := failoverFixture(t)
			s := availabilitySnapshot(i, "a")
			e := &s.Entries[0]
			switch field {
			case "router":
				e.RouterID = "other"
			case "profile":
				e.ProfileID = "other"
			case "route":
				e.RouteID = "other"
			case "fingerprint":
				e.MemberFingerprint = strings.Repeat("f", 64)
			case "account":
				e.AccountID = "other"
			case "provider":
				e.Provider = "other"
			case "model":
				e.Model = "other"
			case "expired":
				s.ObservedAt = time.Now().Add(-4 * time.Minute)
				s.ExpiresAt = time.Now().Add(-time.Minute)
				e.BlockedUntil = s.ExpiresAt
			}
			if w := availabilityPost(g, s, "management-token"); w.Code != 204 {
				t.Fatal(w.Code)
			}
			w := failoverRequest(g, uuid.NewString(), `{"model":"auto","input":"hello"}`, "")
			if w.Code != 200 || !reflect.DeepEqual(raw.routes(), []string{"a"}) {
				t.Fatal("mismatch blocked new binding", w.Code, raw.routes())
			}
		})
	}
	i, _, raw, g := failoverFixture(t)
	thread := uuid.NewString()
	body := `{"model":"auto","input":"hello"}`
	if w := failoverRequest(g, thread, body, ""); w.Code != 200 {
		t.Fatal(w.Code)
	}
	s := availabilitySnapshot(i, "a")
	if w := availabilityPost(g, s, "management-token"); w.Code != 204 {
		t.Fatal(w.Code)
	}
	if w := failoverRequest(g, thread, `{"model":"auto","input":[{"role":"user","content":"hello"},{"role":"assistant","content":"prior full response"},{"role":"user","content":"next"}]}`, ""); w.Code != 200 {
		t.Fatal(w.Code, w.Body.String())
	}
	if !reflect.DeepEqual(raw.routes(), []string{"a", "b"}) {
		t.Fatal("pinned exhausted member was called", raw.routes())
	}
	s.Entries = []routerAvailabilityEntry{}
	s.Sequence = 2
	s.ObservedAt = time.Now().UTC()
	if w := availabilityPost(g, s, "management-token"); w.Code != 204 {
		t.Fatal(w.Code)
	}
	if w := failoverRequest(g, uuid.NewString(), body, ""); w.Code != 200 {
		t.Fatal(w.Code)
	}
	if !reflect.DeepEqual(raw.routes(), []string{"a", "b", "a"}) {
		t.Fatal("reset failed", raw.routes())
	}
}

func TestRouterAvailabilityBoundsSequenceCASAndAllBlocked(t *testing.T) {
	for _, mutate := range []func(*routerAvailabilitySnapshot){func(s *routerAvailabilitySnapshot) { s.Version = 2 }, func(s *routerAvailabilitySnapshot) { s.ExpiresAt = s.ObservedAt.Add(5*time.Minute + time.Nanosecond) }, func(s *routerAvailabilitySnapshot) { s.ObservedAt = time.Now().Add(time.Minute) }, func(s *routerAvailabilitySnapshot) { s.Entries[0].BlockedUntil = s.ExpiresAt.Add(time.Second) }, func(s *routerAvailabilitySnapshot) { s.Entries[0].Reason = "unknown" }, func(s *routerAvailabilitySnapshot) { s.Entries = append(s.Entries, s.Entries[0]) }, func(s *routerAvailabilitySnapshot) { s.Entries = nil }} {
		i, _, _, g := failoverFixture(t)
		s := availabilitySnapshot(i, "a")
		mutate(&s)
		if w := availabilityPost(g, s, "management-token"); w.Code != 400 {
			t.Fatal("invalid snapshot accepted", w.Code)
		}
		if i.routerAvailability.snapshot.Sequence != 0 {
			t.Fatal("invalid snapshot mutated state")
		}
	}
	i, _, raw, g := failoverFixture(t)
	s := availabilitySnapshot(i, "a", "b", "c")
	var wg sync.WaitGroup
	codes := make(chan int, 2)
	for n := 0; n < 2; n++ {
		wg.Add(1)
		go func() { defer wg.Done(); codes <- availabilityPost(g, s, "management-token").Code }()
	}
	wg.Wait()
	close(codes)
	counts := map[int]int{}
	for code := range codes {
		counts[code]++
	}
	if counts[204] != 1 || counts[409] != 1 {
		t.Fatal("sequence CAS", counts)
	}
	w := failoverRequest(g, uuid.NewString(), `{"model":"auto","input":"hello"}`, "")
	if w.Code != 429 || len(raw.routes()) != 0 {
		t.Fatal("all-blocked snapshot dispatched", w.Code, raw.routes())
	}
	req := httptest.NewRequest("POST", "/lain/router-availability", strings.NewReader(strings.Repeat("x", maxRouterAvailabilityBytes+1)))
	req.Header.Set("Authorization", "Bearer management-token")
	w = httptest.NewRecorder()
	g.ServeHTTP(w, req)
	if w.Code != 413 {
		t.Fatal("unbounded snapshot", w.Code)
	}
}
