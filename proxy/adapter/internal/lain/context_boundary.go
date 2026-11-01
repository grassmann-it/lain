package lain

import (
	"bytes"
	"io"
	"net/http"
	"strconv"
)

// This is an error-diagnostic byte limit, never a model context/token limit.
const maxRequestErrorBytes = 64 << 10

// boundRequestErrorResponse limits rejected-request diagnostics before executors
// materialize them. Only an exact, unambiguous upstream code establishes a
// actionable failure; oversized diagnostics receive a generic explanation instead.
// Request bodies, token budgets, tools and retry/routing policy are untouched.
func boundRequestErrorResponse(response *http.Response) {
	if response == nil || response.Body == nil || (response.StatusCode != http.StatusBadRequest && response.StatusCode != http.StatusRequestEntityTooLarge && response.StatusCode != http.StatusUnprocessableEntity) {
		return
	}
	original := response.Body
	data, readErr := io.ReadAll(io.LimitReader(original, maxRequestErrorBytes+1))
	var replacement []byte
	if len(data) > maxRequestErrorBytes {
		replacement = []byte(`{"error":{"type":"upstream_rejected","code":"error_details_too_large","message":"The upstream provider rejected this request; its error details exceeded the diagnostic size limit."}}`)
	} else if readErr == nil {
		// HTTP and stream failures share one strict, bounded classifier. A known
		// code is sufficient for the fixed cause; a parameter is copied only when
		// its complete value is on the exact diagnostic-field allowlist.
		if classification := classifyUpstreamErrorObject(data, ""); classification != nil {
			replacement = canonicalUpstreamErrorJSON(classification)
		}
	}
	if replacement == nil {
		response.Body = &requestErrorReplayBody{prefix: bytes.NewReader(data), original: original, readErr: readErr}
		return
	}
	// Do not drain a potentially unlimited error body. Closing it releases the
	// upstream request; the executor owns only our independent replacement body.
	_ = original.Close()
	response.Body = io.NopCloser(bytes.NewReader(replacement))
	response.ContentLength = int64(len(replacement))
	response.TransferEncoding = nil
	response.Trailer = nil
	response.Uncompressed = false
	if response.Header == nil {
		response.Header = make(http.Header)
	}
	for _, name := range []string{"Content-Encoding", "Transfer-Encoding", "Trailer", "Content-MD5", "Digest", "ETag"} {
		response.Header.Del(name)
	}
	response.Header.Set("Content-Type", "application/json")
	response.Header.Set("Content-Length", strconv.Itoa(len(replacement)))
}

// Preserve small unrelated errors byte for byte, including a read failure and
// the original body's Close ownership. No provider body is retained elsewhere.
type requestErrorReplayBody struct {
	prefix   *bytes.Reader
	original io.ReadCloser
	readErr  error
}

func (b *requestErrorReplayBody) Read(p []byte) (int, error) {
	if b.prefix.Len() > 0 {
		return b.prefix.Read(p)
	}
	if b.readErr != nil {
		err := b.readErr
		b.readErr = nil
		return 0, err
	}
	return b.original.Read(p)
}

func (b *requestErrorReplayBody) Close() error { return b.original.Close() }
