# REC-02 — Passive Core observations and bounded local handoff

30 October 2026 · **Proposed work package; documentation only.** No observer,
spike, test, capture, runtime change or implementation authorization is claimed.
Entry point: [package register](README.md). Shared contract authority:
[REC-00](00-contracts.md). Current scope authority:
[recording plan](../training-logging-plan.md), with
[audited hooks](../observability-audit.md) and [coverage](../capture-coverage.md).

## Goal and scope

Provide one-way, bounded observations of explicitly enabled lain connections so
REC-03/REC-04 can produce persisted, inspectable conversations while the UI is
closed. Once content capture is explicitly enabled, include **all models actually
used in that enabled scope** by default. Optional filters may narrow it; unknown
training rights are not an all-model capture allowlist. Fresh installation remains
off; metadata-only and content are different consent modes.

Keep authentication, routing, provider payloads, HTTP writer behavior, retries,
terminal delivery and the fail-closed Usage journal unchanged. No dynamic plugin
activation, historical-log import, remote upload, replay, provider request, tool
execution adapter, grading or training belongs here. REC-02 does not implement
archive storage, retention, UI, shared policy decisions or protocol normalization.
Private unencrypted storage with Linux0600/0700 is the accepted direction; do not
add encryption, keyring or unlock workflows.

## Baseline and ownership

Installed Core at the audit: CLIProxyAPI8.0.41/lain-patch4, upstream
`8289ec4c71ad914d0612c2776352b76ba3d355a6`, binary SHA256
`191f4f17e4096558c32ebc5b28ec51341fadc6549f3269300af22800dfe6ce18`.
Checkout is patch5. Installed adapter provenance is under the installed package's
`core-source-provenance/`; audit findings are dated, not a claim of current live
readiness. Freeze REC-00's chosen source baseline before integration. A recorder
change must not silently deliver unrelated patch5 routing/compatibility work,
apply pending profile revisions or replace a live Core. Preserve foreign WIP and
the independently installed Desktop responsiveness fix.

Proposed exclusive implementation paths, **not changed by this package plan**:

- New `proxy/adapter/internal/lain/recording_*.go` and associated
  `recording_*_test.go`, excluding shared contracts/policy files allocated by REC-00.
- Prepare focused hook-wiring diffs for existing `proxy/adapter/internal/lain/http.go`,
  `integration.go`, `executor.go`, and `proxy/adapter/cmd/lain-proxy/main.go`.
  These existing files remain integrator-owned; submit rather than concurrently
  apply the diffs. They already contain unrelated work, which must be preserved.
- New synthetic fixtures under `proxy/adapter/internal/lain/testdata/recording/`.

REC-00/integrator owns shared Go/.NET DTOs, build/provenance files, any upstream
patch file and cross-package registration. REC-01 owns the agreed scope/redaction
policy; REC-03 owns Host normalization; REC-04 owns committed archive/payloads;
REC-05 owns projection/query code and REC-06 owns handoff collection/public API. Do not edit their paths. If new
SDK instrumentation is required, return a minimal proposed diff to the integrator
for separate allocation rather than concurrently editing `proxy/patches/` or the
Go module cache. Ownership changes go through the package register first.

## Dependencies and readiness

REC-00 must freeze an executable contract for: source epochs/sequences and gaps,
local/external request and attempt IDs, boundary/representation enums, scope and
policy revision, stop semantics, byte/count admission, redaction state, nullable
measurements and health. REC-01 must define the first secret-exclusion gate and
what may cross the private handoff. REC-06 must publish a collector stub using
REC-04's source-commit contract and the agreed bounded transfer/cursor behavior.

A synthetic hook spike can proceed independently after REC-00 supplies a minimal
stub. Full integration waits for those seams; do not invent a second DTO, infer
training permission from capture, or pick numeric queue/latency defaults silently.
Suggested limits in the main plan are measurement candidates, not user-approved
values. Resource and latency budgets must be recorded before pass/fail claims.

## Work sequence

1. **Static SDK bridge spike.** Keep `cfg.Plugins.Enabled=false`. Use public
   `api.WithRouterConfigurator` to reach
   `BaseAPIHandler.SetPluginHost(PluginInterceptorHost)` with a small static
   observer, rather than loading the dynamic plugin runtime. Return zero-value
   modification DTOs: nil headers, no body/path override, no terminate/drop.
   Implement exact request/stream detector methods and
   `StreamChunkPayloadIncludesRequestBody=false`,
   `StreamChunkPayloadIncludesHistory=false`; unknown hosts otherwise get costly
   per-chunk prompt/history copies. Record source pin and trace call order under
   synthetic nonstream/SSE/WS, disabled, fault, restart and config-reload cases.
2. **Admission and identity.** Reuse the bounded original client body before model
   rewrite in `http.go`, but admit recording only after authorization and enabled
   scope validation. Resolve the actual registered capture principal through
   REC-01; User-Agent/shared inference keys are not per-client proof. Associate
   declared conversation/turn/worker evidence without guessing identities. Bind
   each real attempt to its route/model/account separately, including failover.
3. **Observe only qualified boundaries.** Build a per-protocol/transport matrix
   below; use the existing executor decorator and final HTTP transport where
   appropriate. Tag a missing boundary as unavailable. Keep late terminal suffixes,
   cancellation, response validation and journal gates in their existing order.
4. **Bounded Core transfer.** Introduce a separately bounded recorder buffer and
   authenticated local pull/handoff, with epoch/sequence/cursor validation,
   non-destructive reads, duplicate-safe IDs, byte caps and explicit loss markers.
   Do not drain, acknowledge or reuse Usage or ephemeral Inspector queues. No
   second unbounded queue, task per chunk, retained HTTP context/auth object,
   payload serialization to logs, or disk I/O in the provider callback.
5. **Pressure, stop and teardown.** Scope is evaluated with the policy revision
   specified by REC-00. Global stop atomically denies new observations and follows
   the agreed pending-content discard policy without canceling inference. Consumer
   outage, capacity, oversize, sanitizer fault and shutdown produce content-free
   health/gap facts. Bound all in-flight copies as well as queued bytes. Queue
   acceptance is not durability; only REC-04's commit establishes durable history.
6. **Handoff and qualification.** Supply REC-03/04 synthetic pages and a fake
   consumer. Qualification uses isolated paths/listeners and mock upstreams only;
   no live listener replacement, provider invocation, replay or client switch.

## SDK limits and honest boundary labels

| Observation | Minimal integration and evidence limit |
| --- | --- |
| `client_ingress` | Already-bounded body saved before alias/JSON rewrite (`internal/lain/http.go:308`). Never archive auth headers or unauthenticated body content. |
| `normalized` request | Executor or after-auth hook, explicitly pre-executor translation. `sdk/cliproxy/executor/types.go:101` documents this limit. |
| `provider_request` | Actual final HTTP request body at the transport send boundary, after known transforms. `internal/lain/executor.go:460` is the current RoundTrip seam. The after-auth hook is insufficient. |
| `provider_response` | HTTP body reads before downstream diagnostic replacement; preserve error/short-read coverage. Application bytes may already be decompressed. Optional native Codex WS response observer is event evidence only. |
| `client_egress` | A separately qualified additive send-side observation at the existing write/flush path. Handler interception happens before validation/write and cannot establish delivery. |
| Handler response/chunks | Useful normalized output with its own source tag, including post-hook validation omissions; never relabel as actual client egress. |

Hook sources are pinned SDK `sdk/api/options.go:28`,
`sdk/api/handlers/handlers.go:470`, `handlers_interceptors.go:21/35/281`,
`handlers_stream.go:233/245`, and `internal/api/server_reload.go:201`. Reload can
replace a statically set handler host; prove lifecycle rather than assuming it.
Interceptor RequestID, HTTP TraceID and lain request/attempt IDs are distinct
unless explicit correlation establishes a relation.

Do not add a ResponseWriter wrapper to satisfy an egress checkbox. A minimal
send-side SDK change, if needed, requires integrator allocation and separate
qualification; an observation after a short write records only bytes accepted by
that writer and the write error, not proof of client receipt. Preserve Flush/
FlushError, status/header semantics, cancellation and terminal accounting order.

HTTP RoundTripper does not see WS frames after upgrade. Native Codex WS response
hooks exist; complete provider WS request/response coverage is a separate gate.
Client-facing WS and Gemini-native paths remain outside current public admission.
Responses, compact, Chat Completions and Anthropic require their own gates; an
all-model scope is not proof of every protocol's capture coverage. Do not change
routes/transport preferences merely to improve recorder coverage.

## Secret boundary and no unintended plaintext spill

Approved conversation content will ultimately be stored unencrypted by REC-04.
That decision does not permit credentials or unredacted secret-bearing bodies in
handoff files, overflow spools, logs, SQLite, temporary/debug artifacts or crash
reports. Copy only admitted bounded body fragments and allowlisted metadata;
never enqueue an entire SDK usage Record, auth object, header map or context.

REC-00/01 must specify the first exclusion gate. Recommended implementation seam:
bounded transient Core RAM, then an independent bounded preprocessing worker
before any secret-bearing content could be serialized into the handoff or spilled
onto disk; Host revalidates before normalization/persistence. Do not perform
extensive scanning or compression on inference callbacks. If first-gate semantics
are unresolved, synthetic spike work may proceed but real-content integration is
not ready. A private handoff uses authentication, restrictive permissions and no
filesystem spool fallback. Unsupported/malformed or insufficiently screened
content is omitted with a reason; metadata may continue. Do not claim that
pattern matching can discover every unmarked secret in ordinary prose.

## Observable acceptance — planned, not run

- Baseline/off/on/observer-fault mock parity for each claimed path: identical
  outbound/inbound bytes, headers/status, model/account selection, retry counts,
  stream flush/cancel semantics and durable Usage ordering. Nil versus allocated
  empty interceptor headers, post-hook SSE validation, zero-length/short writes,
  write/flush errors, keepalives and withheld terminal suffixes are explicit cases.
- Static bridge stays passive with plugins disabled; detector/copy policy tests
  show no per-chunk full prompt/history copies. Startup/restart/reload either
  retain the bridge or expose a diagnosed unavailable state, never silently lose
  coverage. A hook error cannot log the observed body or alter inference.
- Separate request/attempt IDs survive retry, failover and simultaneous clients;
  one request does not collapse several attempts. Unknown fields remain null,
  reported zeros remain zero, conflicts and unsupported transports remain visible.
- Disabled collector allocates no new content buffers or archive files. Enabled
  all-model scope does not use a training/model allowlist; optional deny/narrowing
  and client scope are honored for every concrete attempt.
- Queue count/bytes, active assembly, capture-copy memory and local response-page
  size stay bounded under 1/4/8 synthetic concurrency. Slow/absent consumer,
  stop races, oversize frames and metadata-only mode expose known gaps or unknown
  crash loss without failing new inference through an archive-specific path.
- Canary credentials fragmented across input/SSE boundaries are absent from all
  transfer serialization, logs, overflow/temp artifacts and downstream fixtures.
  Legitimately retained redacted text is allowed plaintext in approved storage;
  neither raw-fallback capture nor a confidentiality guarantee is invented.

## Return evidence and return conditions

Return owned diff and exact baseline/SDK pins; hook call-order table; passive DTO
assertions; synthetic fixture IDs; actual test commands/results; off/on/fault
bytes/headers/accounting comparison; latency/RSS/copy/queue measurements with
agreed budget; supported/unavailable boundary matrix; policy-stop/loss behavior;
and a short integration note for REC-03/04. No test result is inferred from the
existing Inspector responsiveness benchmark. Build through the shared serial
verification lane; native UI checks belong to the browser/qualification owners.

Return to REC-00/integrator before dependent implementation if sourcebaseline,
shared identity/stop contract, first exclusion gate or per-client principal is
unresolved; a hook mutates behavior; host replacement loses observations; a new
SDK patch is needed; unsupported transport is required for an acceptance claim;
or measured overhead exceeds the agreed budget. Preserve failed evidence and
coverage limits; do not enable plugins, relax policy, alter routing or add a disk
spool to make the package appear complete.
