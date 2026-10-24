package lain

import (
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"regexp"
	"sort"
	"strings"
	"sync"
	"time"

	auth "github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/auth"
	"github.com/router-for-me/CLIProxyAPI/v8/sdk/cliproxy/usage"
	"github.com/tidwall/gjson"
)

const maxQuotaObservations = 512
const maxQuotaBytes = 1 << 20

var groqQuotaHeaders = []string{
	"x-ratelimit-limit-requests", "x-ratelimit-limit-tokens",
	"x-ratelimit-remaining-requests", "x-ratelimit-remaining-tokens",
	"x-ratelimit-reset-requests", "x-ratelimit-reset-tokens",
}
var quotaNumber = regexp.MustCompile(`^[0-9]{1,100}$`)
var quotaDuration = regexp.MustCompile(`^(?:[0-9]+(?:\.[0-9]+)?(?:ms|s|m|h|d))+$`)

type quotaObservation struct {
	AccountID  string              `json:"accountId"`
	Provider   string              `json:"provider"`
	Model      string              `json:"model"`
	RequestID  string              `json:"requestId"`
	ObservedAt time.Time           `json:"observedAt"`
	StatusCode int                 `json:"statusCode"`
	Headers    map[string][]string `json:"headers"`
}
type quotaObservationBatch struct {
	Version      int                `json:"version"`
	Observations []quotaObservation `json:"observations"`
}
type quotaKey struct{ account, model string }

// This optional snapshot has its own failure state. It can never poison the
// durable usage journal or convert otherwise successful inference to failure.
type quotaObservations struct {
	mu     sync.Mutex
	path   string
	latest map[quotaKey]quotaObservation
	fault  error
}

func openQuotaObservations(path string) *quotaObservations {
	q := &quotaObservations{path: path, latest: map[quotaKey]quotaObservation{}}
	f, err := os.Open(path)
	if errors.Is(err, os.ErrNotExist) {
		return q
	}
	if err != nil {
		q.fault = err
		return q
	}
	defer f.Close()
	data, err := io.ReadAll(io.LimitReader(f, maxQuotaBytes+1))
	if err != nil || len(data) > maxQuotaBytes {
		q.fault = errors.New("invalid quota snapshot size")
		return q
	}
	var batch quotaObservationBatch
	if json.Unmarshal(data, &batch) != nil || batch.Version != 1 || batch.Observations == nil || len(batch.Observations) > maxQuotaObservations {
		q.fault = errors.New("invalid quota snapshot")
		return q
	}
	for _, observation := range batch.Observations {
		if !validQuotaObservation(observation) {
			q.fault = errors.New("invalid quota observation")
			q.latest = map[quotaKey]quotaObservation{}
			return q
		}
		key := quotaKey{observation.AccountID, observation.Model}
		if previous, ok := q.latest[key]; !ok || observation.ObservedAt.After(previous.ObservedAt) {
			q.latest[key] = observation
		}
	}
	return q
}

func validQuotaObservation(o quotaObservation) bool {
	if o.Provider != "groq" || safeDimension(o.AccountID) == "" || safeDimension(o.Model) == "" || safeDimension(o.RequestID) == "" || o.ObservedAt.IsZero() || o.ObservedAt.After(time.Now().Add(time.Minute)) || o.StatusCode < 100 || o.StatusCode > 599 || len(o.Headers) == 0 || len(o.Headers) > len(groqQuotaHeaders) {
		return false
	}
	for name, values := range o.Headers {
		known := false
		for _, allowed := range groqQuotaHeaders {
			if name == allowed {
				known = true
				break
			}
		}
		if !known || len(values) == 0 || len(values) > 4 {
			return false
		}
		for _, value := range values {
			if !validQuotaHeader(name, value) {
				return false
			}
		}
	}
	return true
}

func validQuotaHeader(name, value string) bool {
	if len(value) == 0 || len(value) > 100 {
		return false
	}
	if strings.HasPrefix(name, "x-ratelimit-reset-") {
		return quotaDuration.MatchString(value)
	}
	return quotaNumber.MatchString(value)
}

func captureQuotaHeaders(headers http.Header) map[string][]string {
	result := map[string][]string{}
	for _, allowed := range groqQuotaHeaders {
		var values []string
		valid := true
		for name, items := range headers {
			if !strings.EqualFold(name, allowed) {
				continue
			}
			for _, value := range items {
				value = strings.TrimSpace(value)
				if !validQuotaHeader(allowed, value) || len(values) == 4 {
					valid = false
					break
				}
				values = append(values, value)
			}
			if !valid {
				break
			}
		}
		// Keep duplicate values as duplicates. The Host rejects ambiguity rather
		// than guessing which limit/reset is current.
		if valid && len(values) > 0 {
			result[allowed] = values
		}
	}
	return result
}

func (q *quotaObservations) encoded() []byte {
	batch := quotaObservationBatch{Version: 1, Observations: make([]quotaObservation, 0, len(q.latest))}
	for _, observation := range q.latest {
		batch.Observations = append(batch.Observations, observation)
	}
	sort.Slice(batch.Observations, func(a, b int) bool {
		x, y := batch.Observations[a], batch.Observations[b]
		if !x.ObservedAt.Equal(y.ObservedAt) {
			return x.ObservedAt.After(y.ObservedAt)
		}
		if x.AccountID != y.AccountID {
			return x.AccountID < y.AccountID
		}
		return x.Model < y.Model
	})
	for {
		data, _ := json.Marshal(batch)
		if len(batch.Observations) <= maxQuotaObservations && len(data) <= maxQuotaBytes {
			return data
		}
		last := batch.Observations[len(batch.Observations)-1]
		delete(q.latest, quotaKey{last.AccountID, last.Model})
		batch.Observations = batch.Observations[:len(batch.Observations)-1]
	}
}

func (q *quotaObservations) read() ([]byte, error) {
	q.mu.Lock()
	defer q.mu.Unlock()
	if q.fault != nil {
		return nil, q.fault
	}
	return q.encoded(), nil
}

func (q *quotaObservations) record(o quotaObservation) {
	if !validQuotaObservation(o) {
		return
	}
	q.mu.Lock()
	defer q.mu.Unlock()
	key := quotaKey{o.AccountID, o.Model}
	if old, ok := q.latest[key]; ok && !o.ObservedAt.After(old.ObservedAt) {
		return
	}
	q.latest[key] = o
	// Replace, never merge: an absent old header must not inherit a fresh time.
	q.fault = writeQuotaSnapshot(q.path, q.encoded())
}

func writeQuotaSnapshot(path string, data []byte) error {
	f, err := os.CreateTemp(filepath.Dir(path), ".quota-observations-*")
	if err != nil {
		return err
	}
	name := f.Name()
	defer os.Remove(name)
	if _, err = f.Write(data); err == nil {
		err = f.Sync()
	}
	closeErr := f.Close()
	if err != nil {
		return err
	}
	if closeErr != nil {
		return closeErr
	}
	if err = os.Rename(name, path); err != nil {
		return err
	}
	dir, err := os.Open(filepath.Dir(path))
	if err != nil {
		return err
	}
	defer dir.Close()
	return dir.Sync()
}

func (i *Integration) observeQuotaResponse(req *http.Request, a *auth.Auth, response *http.Response) {
	// Provider identity comes from the selected credential and trusted binding,
	// never a caller-supplied header, upstream body, or model name heuristic.
	if i.Quotas == nil || a == nil || a.Provider != "openai-compatible-groq-openai-chat" || a.Attributes["compat_name"] != "groq-openai-chat" || a.Attributes["upstream_protocol"] != "openai-chat" || req.Method != http.MethodPost || req.GetBody == nil {
		return
	}
	selected, ok := routeSelection(req.Context())
	if !ok || selected.Route.Provider != "groq" || !selected.Allowed[a.ID] {
		return
	}
	base, err := url.Parse(strings.TrimSuffix(a.Attributes["base_url"], "/") + "/chat/completions")
	if err != nil || req.URL.Scheme != base.Scheme || req.URL.Host != base.Host || req.URL.Path != base.Path || req.URL.RawQuery != "" {
		return
	}
	headers := captureQuotaHeaders(response.Header)
	if len(headers) == 0 {
		return
	}
	body, err := req.GetBody()
	if err != nil {
		return
	}
	data, err := io.ReadAll(io.LimitReader(body, 32*1024*1024+1))
	_ = body.Close()
	if err != nil || len(data) > 32*1024*1024 || !gjson.ValidBytes(data) {
		return
	}
	model := gjson.GetBytes(data, "model")
	if model.Type != gjson.String || model.String() != selected.Route.Model {
		return
	}
	i.mu.Lock()
	current := i.active[usage.ExecutionRequestIDFromContext(req.Context())]
	var observation quotaObservation
	if current != nil {
		e := current.event
		if e.Provider == "groq" && e.CredentialID == a.ID && e.RequestID == ParentRequestIDFromContext(req.Context()) && e.Model == model.String() && i.Credential(selected.Route, e.AccountID) == a.ID {
			observation = quotaObservation{AccountID: e.AccountID, Provider: "groq", Model: e.Model, RequestID: e.RequestID, ObservedAt: time.Now().UTC(), StatusCode: response.StatusCode, Headers: headers}
		}
	}
	i.mu.Unlock()
	i.Quotas.record(observation)
}
