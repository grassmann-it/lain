package lain

import (
	"bytes"
	"encoding/json"
	"io"
	"net/http"
	"strconv"
)

// This is an error-diagnostic byte limit, never a model context/token limit.
const maxRequestErrorBytes = 64 << 10

// boundRequestErrorResponse limits rejected-request diagnostics before executors
// materialize them. Only an exact, unambiguous upstream code establishes a
// context failure; oversized diagnostics receive a generic explanation instead.
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
		fields, err := toolControlFields(data, "error")
		if err == nil {
			detail, err := toolControlFields(fields["error"], "code", "param")
			var code string
			if err == nil && json.Unmarshal(detail["code"], &code) == nil && code == "context_length_exceeded" {
				payload := map[string]any{
					"type":    "invalid_request_error",
					"code":    code,
					"message": "The upstream model's context limit was exceeded. Shorten the conversation or reduce the requested output, then retry.",
				}
				var param string
				// A parameter is diagnostic metadata, not arbitrary provider text.
				// Keep normal field names exactly; omit nonstrings/oversized values.
				paramJSON := bytes.TrimSpace(detail["param"])
				if len(paramJSON) > 0 && paramJSON[0] == '"' && json.Unmarshal(paramJSON, &param) == nil && len(param) <= 128 {
					payload["param"] = param
				}
				replacement, _ = json.Marshal(map[string]any{"error": payload})
			}
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
