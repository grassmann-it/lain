# Synthetic context boundary

The final transport keeps the complete outbound request byte for byte. It never
estimates tokens, truncates conversation/tools, changes output budgets or chooses
another model. An upstream rejection is the context-limit evidence.

For upstream HTTP 400, 413 and 422, the adapter reads at most **65,537 bytes** of
error diagnostics. An unambiguous JSON `error.code: context_length_exceeded`
within 64 KiB becomes a small actionable OpenAI error envelope, preserving status,
exact code and string `param` values up to 128 UTF-8 bytes. Provider messages,
debug fields and other details are replaced. No other code or wording is inferred
to mean context exhaustion. Oversized diagnostics receive the generic
`upstream_rejected` / `error_details_too_large` envelope with the same HTTP status;
their cause and parameter remain unknown. This is deliberately a diagnostic byte
limit, independent of any model's context or token limit.

Small unrelated/ambiguous bodies retain their exact bytes, response headers and
original Close/read-error behavior. Replacement closes the original body without
draining it, resets ContentLength/encoding/trailer metadata, and owns an independent
JSON body. Success streams, authentication errors, rate limits and server errors
remain outside this diagnostic normalization. Streaming failures after a successful
HTTP response remain governed by the existing stream outcome path.

Focused adapter checks (run in the prepared adapter against the pinned patched
upstream, using the serialized test slot):

```sh
GOTOOLCHAIN=local GOWORK=off GOPROXY=off GOSUMDB=off GOFLAGS= GOMAXPROCS=2 \
  /home/tomka/.local/share/model-usage/toolchain/go/bin/go test -mod=readonly -p=1 \
  ./internal/lain -run '^TestContextBoundary' -count=1
```

The standalone Core probe reuses the private `tests/backend/qualify.py` process
runner, with an owned loopback provider, temporary state and synthetic keys. It
checks Chat and Responses-to-Chat, stream intent on/off, all three statuses,
complete conversation/tool continuation/schema and output budget, one upstream
request despite a configured retry budget, oversized diagnostics and an unrelated
small error. It accepts an explicitly supplied freshly built Core, never discovers
or operates installed services:

```sh
python3 tests/model-compatibility/context_boundary_probe.py \
  --core /absolute/path/to/qualified/lain-proxy \
  --report /absolute/path/to/context-boundary.json
```

These checks are synthetic. They do not establish provider/model context sizes,
tokenizer correctness, real-provider behavior or compatibility of every client
version. The direct adapter fixture establishes exact final request bytes; protocol
translation in the Core probe is checked for complete semantic payload preservation.
Real Codex execution is not part of this probe. Results must be recorded separately;
the commands above are not a claim that either check has run.
