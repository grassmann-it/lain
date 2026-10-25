# Stream outcome and Cohere Responses boundary

Source baseline `0805e13`. Diagnosis uses only sanitized local probe summaries
and pinned source; no provider request, key, installed configuration or service
was accessed by this worker. Root owns the live 14-account qualification.

## False successful stream completion

The sanitized Cloudflare summary reports HTTP 200, zero output characters and
terminal SSE error, while the durable attempt had successful completion. HTTP
status alone cannot establish successful protocol completion.

The bound executor previously recognized only `StreamChunk.Err` as failure.
It could therefore durably finish successfully when the payload itself carried
`error`/`response.error`/`response.failed`, or when a Responses stream closed
without any protocol terminal. The pinned Responses HTTP framer generates an
error on that latter EOF **after** the bound executor's old success write.
This explains a client-visible error paired with successful completion; the
sanitized probe does not identify which particular upstream condition caused EOF.

The fix recognizes the same Responses terminal/error vocabulary at the durable
executor boundary, including non-null root `error`/`response.error`. Error text
inside an ordinary delta stays content. Fixed local descriptions replace raw
provider messages. Payload failure suppresses subsequent content/completion;
Responses EOF without a terminal becomes failed completion before returning.
Chat/Claude streams do not acquire a guessed Responses-only terminal requirement.

Early usage facts remain immutable. If the SDK already reported success/200 and
tokens, a later failure produces `attempt_finished.completionState=failed` rather
than rewriting that usage fact or inventing token/status data. SQLite effective
outcome/error aggregation uses completion precedence. The raw historical state
and HTTP 200 may remain visible separately and must not be called final success.
Without early usage the terminal usage/completion is written failed directly.

Root's later controlled delta on the same saved Cloudflare Gemma 4 route
completed successfully when the output budget increased from 512 to 2048:
421 visible characters, 1752 reported total tokens (1664 output), 31.89 seconds.
The smaller budget had produced reasoning-only output and reached the
budget/EOF boundary without the SDK's required terminal event. This narrows the
observed trigger to that budget/termination path; it is not a general Cloudflare
connection, account or model-access failure. The source fix is still required:
a client-visible terminal error must never remain an effective successful
attempt, even when a larger explicit budget lets the same route finish. This
delta was performed by Root; this worker made no additional provider request.

## Cohere original intent and attribution

The qualified Cohere compatibility subset deliberately rejects an explicit
`store` field, including `false`; it is not silently stripped. Root's first
Responses probe included `store:false`. The original local validation ran before
`Before`, explaining the missing durable attempt. It now follows durable start
and immediately records failed completion/400, still without executing upstream.

After Root deliberately omitted store, the remaining explicit
`reasoning.effort=none` exposed another contract mismatch. The pinned SDK's
configured API-key thinking metadata can default to `[low,medium,high]` without
zero/none support (`api_key_model_capabilities.go`); thinking validation maps
ModeNone to the lowest supported level, and the OpenAI applier emits that level.
The final Cohere gate correctly rejects low: the documented compatibility API
allows only none/high. Root's separate native Chat request without explicit
reasoning succeeded, narrowing this issue to the Responses intent path rather
than establishing an account/model access failure.

The fix validates nested Responses effort as none/high, rejects unsupported,
null, duplicate or conflicting effort, and carries only that explicit validated
intent to the final transport. If generic translation changed or removed it,
the final request restores exactly none/high. No original effort means no
invented effort. The parameter subset is unchanged; store and unsupported levels
remain rejected. Final-wire metadata also preserves the actual sent effort so
an early SDK reporter's low value cannot replace it.

## Synthetic regression

`proxy/adapter/internal/lain/stream_outcome_test.go` covers payload errors,
post-error output suppression, missing/empty/reasoning-only Responses EOF,
ordinary text mentioning errors, valid completion, early immutable usage with
failed completion, and durable Cohere store rejection. Cohere tests add nested
none/high final-wire preservation, unsupported/conflicting effort rejection and
correct final-wire reasoning telemetry.

The pinned upstream source and patch set are unchanged. Adapter source hashes
are regenerated in `proxy/source-manifest.json`. A new Core binary and Root's
explicit installed/runtime qualification remain separate from source tests.

Local result: **PASS**, complete `./internal/lain` package, `-count=1` and
`-timeout=90s`, using the verified temporary prepared v8.0.41 source. Reported
package test duration: 0.675 seconds. Build/test ran serially in a capped user
scope (MemoryHigh 1536M, MemoryMax 2G, SwapMax 256M, CPU 150%, GOMAXPROCS 2,
GOMEMLIMIT 1GiB, nice 10, ionice 7, outer timeout 120 seconds) with external
module fetching disabled. No installation or real provider regression is claimed.

Cleanup check: the two worker-owned temporary adapter trees contained no retained
`.test`, object, archive or Core binary build outputs after the successful Go
test. Prepared source/manifests and qualification evidence are retained. No
shared upstream snapshot, toolchain/module/build cache or installed application
was deleted or changed. No further build, provider call or deployment occurred.
