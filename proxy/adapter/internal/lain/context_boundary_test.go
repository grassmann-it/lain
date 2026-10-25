package lain

import (
	"bytes"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"strconv"
	"strings"
	"testing"
	"unicode/utf8"

	"github.com/router-for-me/CLIProxyAPI/v8/sdk/api/handlers"
)

type contextBoundaryBody struct {
	io.Reader
	read, closes int
}

func (b *contextBoundaryBody) Read(p []byte) (int, error) {
	n, err := b.Reader.Read(p)
	b.read += n
	return n, err
}
func (b *contextBoundaryBody) Close() error { b.closes++; return nil }

func TestContextBoundaryFinalTransportKeepsEntireRequestAndBoundsKnownError(t *testing.T) {
	// A large conversation and complete tool continuation are opaque request
	// content. A diagnostic byte limit must never become a context/token guess.
	body := `{"model":"fixture-model","messages":[{"role":"user","content":"` + strings.Repeat("Grüße🐈", 8192) + `"},{"role":"assistant","tool_calls":[{"id":"call-1","type":"function","function":{"name":"echo","arguments":"{}"}}]},{"role":"tool","tool_call_id":"call-1","content":"complete tool result"}],"tools":[{"type":"function","function":{"name":"echo","parameters":{"type":"object"}}}],"max_tokens":4096,"stream":true}`
	for _, status := range []int{400, 413, 422} {
		t.Run(strconv.Itoa(status), func(t *testing.T) {
			calls := 0
			upstream, _ := json.Marshal(map[string]any{"error": map[string]any{"code": "context_length_exceeded", "param": "messages", "message": strings.Repeat("provider detail 🐈", 2048), "debug": "must not reach client"}})
			source := &contextBoundaryBody{Reader: bytes.NewReader(upstream)}
			transport := &guardedTransport{next: fixtureTransport(func(req *http.Request) (*http.Response, error) {
				calls++
				outgoing, err := io.ReadAll(req.Body)
				if err != nil || string(outgoing) != body || req.ContentLength != int64(len(body)) {
					t.Fatal("request content, tools, output budget or stream flag changed")
				}
				return &http.Response{StatusCode: status, Body: source, ContentLength: int64(len(upstream)), Header: http.Header{"Content-Type": {"application/json"}, "Content-Length": {strconv.Itoa(len(upstream))}, "Content-Encoding": {"identity"}, "Transfer-Encoding": {"chunked"}, "Trailer": {"Digest"}, "Digest": {"stale"}, "Etag": {"stale"}}, TransferEncoding: []string{"chunked"}, Trailer: http.Header{"Digest": {"stale"}}}, nil
			})}
			req, _ := http.NewRequest(http.MethodPost, "https://fixture.invalid/v1/chat/completions", strings.NewReader(body))
			response, err := transport.RoundTrip(req)
			if err != nil || response == nil || calls != 1 {
				t.Fatalf("single original request failed: %v, calls=%d", err, calls)
			}
			data, err := io.ReadAll(response.Body)
			_ = response.Body.Close()
			if err != nil || response.StatusCode != status || source.closes != 1 || len(data) > 1024 || !utf8.Valid(data) || !json.Valid(data) {
				t.Fatalf("invalid bounded response: status=%d length=%d closes=%d err=%v", response.StatusCode, len(data), source.closes, err)
			}
			var envelope struct{ Error map[string]any }
			_ = json.Unmarshal(data, &envelope)
			if envelope.Error["code"] != "context_length_exceeded" || envelope.Error["param"] != "messages" || !strings.Contains(envelope.Error["message"].(string), "Shorten the conversation") || strings.Contains(string(data), "provider detail") || strings.Contains(string(data), "debug") {
				t.Fatal("context explanation/code/parameter lost or provider details exposed")
			}
			if response.ContentLength != int64(len(data)) || response.Header.Get("Content-Length") != strconv.Itoa(len(data)) || response.Header.Get("Content-Type") != "application/json" || response.Header.Get("Content-Encoding") != "" || response.Header.Get("Transfer-Encoding") != "" || response.Header.Get("Digest") != "" || response.Header.Get("ETag") != "" || len(response.TransferEncoding) != 0 || response.Trailer != nil {
				t.Fatal("replacement retained stale response framing or representation metadata")
			}
			// Exercise the same pinned SDK serializer used by nonstream clients.
			client := handlers.BuildErrorResponseBody(status, string(data))
			if len(client) > 1024 || !bytes.Equal(client, data) {
				t.Fatal("client serializer expanded or changed bounded context error")
			}
		})
	}
}

func TestContextBoundarySmallUnrelatedErrorsAreByteExact(t *testing.T) {
	for _, body := range []string{
		"  {\"error\":{\"code\":\"invalid_parameter\",\"message\":\"context_length_exceeded\"}}\n",
		`{"error":{"code":"context_window_exceeded"}}`,
		`{"error":{"code":"context_length_exceeded","code":"other"}}`,
		`{"error":{"code":"context_length_exceeded"},"error":{"code":"other"}}`,
		`{"error":{"code":"context_length_exceeded"}} trailing`,
		`{"error":{"message":"Your input is too long"}}`,
		"not JSON\xff",
	} {
		source := &contextBoundaryBody{Reader: strings.NewReader(body)}
		response := &http.Response{StatusCode: 400, Body: source, Header: http.Header{"Content-Type": {"text/plain"}}, ContentLength: -1}
		boundRequestErrorResponse(response)
		if source.closes != 0 {
			t.Fatal("passthrough original closed before caller finished")
		}
		actual, err := io.ReadAll(response.Body)
		_ = response.Body.Close()
		if err != nil || string(actual) != body || source.closes != 1 || response.ContentLength != -1 || response.Header.Get("Content-Type") != "text/plain" {
			t.Fatal("unrelated or ambiguous diagnostic changed")
		}
	}
}

func TestContextBoundaryOversizeIsGenericAndReadIsBounded(t *testing.T) {
	for _, status := range []int{400, 413, 422} {
		// Include the known code up front: incomplete JSON beyond the read limit
		// is deliberately never enough evidence to claim a context cause.
		body := `{"error":{"code":"context_length_exceeded","param":"messages","message":"` + strings.Repeat("x", 4*maxRequestErrorBytes) + `"}}`
		source := &contextBoundaryBody{Reader: strings.NewReader(body)}
		response := &http.Response{StatusCode: status, Body: source}
		boundRequestErrorResponse(response)
		data, err := io.ReadAll(response.Body)
		_ = response.Body.Close()
		if err != nil || source.read != maxRequestErrorBytes+1 || source.closes != 1 || response.StatusCode != status || len(data) > 1024 || !json.Valid(data) || !strings.Contains(string(data), `"code":"error_details_too_large"`) || strings.Contains(string(data), "context_length_exceeded") || strings.Contains(string(data), "param") {
			t.Fatalf("unbounded read or invented context classification: read=%d closes=%d", source.read, source.closes)
		}
	}
}

type contextBoundaryReadError struct{ err error }

func (r contextBoundaryReadError) Read([]byte) (int, error) { return 0, r.err }

func TestContextBoundaryReadErrorAndNonRequestStatusesRemainIntact(t *testing.T) {
	readErr := errors.New("fixture read failure")
	source := &contextBoundaryBody{Reader: io.MultiReader(strings.NewReader("partial"), contextBoundaryReadError{readErr})}
	response := &http.Response{StatusCode: 400, Body: source}
	boundRequestErrorResponse(response)
	data, err := io.ReadAll(response.Body)
	_ = response.Body.Close()
	if string(data) != "partial" || !errors.Is(err, readErr) || source.closes != 1 {
		t.Fatal("error-body read failure hidden or bytes/close lost")
	}
	for _, status := range []int{200, 401, 429, 500} {
		source := &contextBoundaryBody{Reader: strings.NewReader(strings.Repeat("x", 2*maxRequestErrorBytes))}
		response := &http.Response{StatusCode: status, Body: source}
		boundRequestErrorResponse(response)
		if source.read != 0 || response.Body != source || source.closes != 0 {
			t.Fatal("success/stream/auth/rate/server response inspected or changed")
		}
		_ = response.Body.Close()
	}
	boundRequestErrorResponse(nil)
	boundRequestErrorResponse(&http.Response{StatusCode: 400})
}
