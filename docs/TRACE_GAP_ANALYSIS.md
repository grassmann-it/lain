# Trace archive: Phase 0 gap analysis and architecture proposal


> Scope update, 30 October: the user removed archive encryption/key management
> and prioritizes human inspection and skill-version comparisons across all
> recorded models. Earlier mandatory-encryption proposals below are superseded.
> [The current Sprint1 plan](training-logging-plan.md) is authoritative for the planned scope;
> secret exclusion/private OS permissions and separate training/export rights remain.

29 October 2026 · **Review draft; no archive collector or content capture implemented.**
This document applies the user's implementation brief and subsequent architecture
sparring request. The independent Desktop responsiveness installation is separate;
it does not enable recording. Phase 1 implementation waits for review of this
analysis. No customer transcripts, credential values or payloads are included.

## Current planning instruction · 30 October 2026

The user's expanded training-logging brief is now consolidated in
[the focused audit](observability-audit.md), [coverage matrix](capture-coverage.md)
and [Sprint1 plan](training-logging-plan.md). The latest explicit steering is
**plan before building**. No recorder source/configuration/capture change follows
from these documents; numerical settings and key/identity choices are proposals.

## Latest sequencing decision · 30 October 2026

The user confirms recording first and a separate later assessment/anonymized export. The next requested increment is coherent persisted conversations with messages, tool calls/results and evidenced subworkers, efficient storage and viewing in lain UI. [Conversation-first packages](trace-research/conversation-first.md) supersede recommendations that blind review or a single short recording must be the initial product scope. [Optional decision notes](trace-research/decision-notes.md) remain a skill proposal, not a recovered-reasoning feature or an installed prompt change. No collector has been implemented/enabled by these documentation changes.

## Priority refinement · 30 October 2026

The user now prioritizes inspecting concrete API/tool-use behavior, blind model comparison and evidence-based system-instruction improvement before distillation. [The comparison proposal](trace-research/blind-comparison.md) scopes that use: actual tool schemas/arguments/results require separately consented content capture; metadata alone is insufficient. A reviewer projection can hide model identity while private provenance remains intact. This does not yet authorize starting recording or automatically editing live prompts.

## Intended outcome and fixed requirements

The user wants to understand the actual shape of AI API conversations, inspect
where agents took a good or bad turn, preserve evidence of real tool trajectories,
and eventually curate permitted material to distill/fine-tune an independently
chosen open-weight model such as an eligible Qwen release. Training now, automatic
traffic capture, cloud upload and recovery of hidden reasoning are out of scope.

Accepted from the brief: disabled-by-default collection, per-client/per-route
controls and a hard global stop; separate metadata and consented encrypted content
modes; immutable observations with separately versioned derivatives; unknown
training rights excluded by default; genuine vs synthetic execution distinguished;
independent holdouts before experiments; no archive failure may break inference.
This does not authorize extending Live's existing output-only capture policy.
The UI choices, sample limits and package sequence below remain recommendations.

## Runtime and source actually inspected

| Item | Observed state | Limit of the observation |
| --- | --- | --- |
| Installation | `~/.local/share/lain/app`; systemd user `lain.service`, Host PID 1649 owns Core PID 1832 | No service action or alternate proxy started for this audit |
| Host | `1.0.0+835d2e2260dfa3f1b072eb8fcc1541c33c6dd665`, Gateway, proxy ready | Desired revision 919 differs from applied 917; no pending changes applied |
| Installed Core | CLIProxyAPI **v8.0.41**, upstream `8289ec4c71ad914d0612c2776352b76ba3d355a6`, lain patch **4**, Go 1.27.2 | SHA-256 `191f4f17e4096558c32ebc5b28ec51341fadc6549f3269300af22800dfe6ce18` matches installed binary manifest |
| Checkout Core | Same upstream, lain patch **5** | Working-tree analysis is not installed proof; use installed `core-source-provenance/` for exact runtime hook review |
| Listener/config | `127.0.0.1:18417`; Host API `127.0.0.1:18418`; Core launched with private config/bindings/journal paths | Auth keys, headers and raw bindings were not displayed or copied |
| Accounts/models | 19 configured accounts, 457 configured model accesses | Desired inventory, not a claim that every route is applied or live-qualified |
| Provider families | Codex, Command Code, Z.ai, Kimi, OpenRouter, NVIDIA Build, Groq, MiMo, Cohere, Kilo, SambaNova, Gemini API, Cloudflare Workers AI, Upstage, Requesty, Synthetic, Fireworks | Provider family does not determine contractual output rights |
| Ordinary logging | Config has request-log=false, logging-to-file=false, usage-statistics-enabled=false; launcher enforces its own logging/plugin exclusions | Does **not** prove that every error logging path is off |
| Existing error artifacts | 10 `.log` files, approximately 9.5 MB under isolated Core auth/logs at inspection | Only names/count/size inspected in this phase; do not import these as a consented archive |
| Live feed | Live bounded snapshot, 10 reported Codex streams in a deliberately limited, incomplete snapshot | No content inspected; not an exhaustive client/session inventory |

Source references below are checkout-relative unless stated otherwise. Installed
provenance includes the adapter source under `core-source-provenance/proxy/adapter/`.
`main.go`, `observation.go`, `inspector.go` and `dimensions.go` matched the checkout
byte-for-byte at inspection; `http.go`, `integration.go`, `executor.go` differ.
Installed patch-4 entry points are `http.go:20`, `integration.go:140/207/286`,
`executor.go:133/164`, and `cmd/lain-proxy/main.go:135`. Inspect those exact files
again before modifying or qualifying a runtime build.

## Clients and protocol coverage

Codex's current default provider is `lain`, Responses endpoint
`http://127.0.0.1:18417/v1`. Its config also contains direct Kimi and old CPA
providers on port 8317. Configuration alone cannot prove which endpoint each
existing/forked VS Code thread uses. Threads can retain their own provider state.
A native ChatGPT/plugin login and its connector traffic are separate from model
inference through lain. No such traffic is silently intercepted by this design.

`~/.codex/sessions` exists; no session content was read or imported. OpenCode was
not on PATH, and the checked standard OpenCode state path was absent. A checked
VS Code extension-storage path was absent; this does not establish that VS Code
has no other storage. OpenCode, ChatGPT Desktop and other clients require an
explicit installation/configuration audit when connected. Do not infer coverage
from a client brand or a common API key.

The admitted public inference paths in the current adapter are POST
`/v1/chat/completions`, `/v1/responses`, `/v1/responses/compact`, `/v1/messages`,
and GET `/v1/models`. Upstream executors support more protocols, including Gemini
native and WebSockets, but the lain HTTP admission layer does not expose every
upstream route. GET WebSocket upgrades are outside its inference POST allowlist.
Codex may use upstream WebSocket transport internally through an executor; that
is a different boundary from a client-facing WebSocket. No WebSocket or Gemini
native compatibility is claimed for a future collector until separately tested.

## Existing coverage and important gaps

| Existing component | Useful observations | Missing/unsafe assumptions for a training archive |
| --- | --- | --- |
| Durable usage journal (`internal/lain/journal.go`, Integration.Before/Publish/After) | Stable request/attempt/account/route, UTC, provider/model/alias, token nullability, status, measurement provenance and dimensions | Not a conversation store; no prompts, schemas, actual local execution or verified task outcomes. Its disk failures deliberately stop new inference; archive semantics must not replace that guarantee |
| RAM Inspector (`observation.go`, `inspector.go`; Host InspectorCollector/Store) | Selected returned output/tool identities, explicit omissions, bounded cursors/drop counters and source generations | Redacted, incomplete and transient. Core has line/buffer limits; Host filters again. Tool arguments/results and prompts excluded. Cannot reconstruct exact original bodies or full historic sessions |
| Protocol executor decorator (`executor.go`) | Selected account identity, individual execution, upstream result/chunks, completion/error/cancel boundary | Executor chunks may be translated; not proof of final client bytes, final provider request body, or received-by-client delivery. Existing terminal durability/normalization remains authoritative |
| SDK `WithServerOptions` + `api.WithMiddleware` | Verified in v8 SDK and actively used in adapter main | Entry/exit metadata and authenticated request context; generic response wrapping risks SSE flush, optional interfaces, cancellation and WebSocket hijacking |
| SDK `WithRequestLoggerFactory` | Present in v8 `sdk/api/options.go`; customization is possible | Signature accepts headers, request/response bodies, API errors and WS timelines. Generic full logger is **not** an allowlisted metadata observer or automatic safe archival source |
| SDK `usage.Plugin.HandleUsage` | Actual v8 interface exists; usage Record includes timing/usage/model identity | It also carries APIKey, response headers, failure body and other sensitive fields. Never serialize Record wholesale. Manager uses a slice-backed RAM queue, not bounded durable delivery; constructor buffer is not a hard backpressure guarantee |
| Client-side native transcripts | Potential explicit import source for another phase | Different permissions/schema; not proof of a proxy-visible request, real shell execution or training rights |

The brief's v6 pkg.go.dev link is historical, not the runtime API contract.
Source checked: v8.0.41 `sdk/cliproxy/builder.go`, `sdk/api/options.go`,
`sdk/cliproxy/usage/manager.go` and `docs/sdk-usage.md` in the local module cache.
An SDK hook's existence does not establish complete stream/session capture.

### Existing error logging needs a separate decision

Upstream `internal/api/middleware/request_logging.go` installs an error-only
wrapper when regular logging is disabled. Its `response_writer.go:296` can force
logging for actionable API errors. `RequestLogger` accepts full headers/bodies.
This explains why request-log=false alone is an insufficient privacy assertion.
The existence of old files does not prove their exact content or creation time.
Do not turn this logger on, ingest old error files, or delete them as part of
Phase 0. Before claiming that the new archive is the sole content store, qualify
and explicitly decide the separate legacy/error-artifact policy. A new metadata
observer must not reuse an unsanitized logger path or a secrets-bearing Record.

## Proposed integration: independent archive beside existing accounting

```text
Client -> existing authentication/admission -> existing dispatch/executor -> Provider
                 | nonblocking typed observation                 |
                 +-> policy + bounded allowlist projection <-----+
                           | TryEnqueue; drop on full
                           v
                 independent bounded queue -> archive worker
                           |                         |
                 metadata JSONL segments      SQLite catalog
                           |
                 separately consented encrypted content objects (later)

Existing durable Usage journal/ack/checkpoint path remains unchanged.
Harness -> authenticated client-event API -> reported/executed evidence (later).
```

Recommended hook assignments, not yet implementation approval:

1. **Admission/Middleware:** after existing auth/routing validation, associate a
   real client principal, public endpoint enum and request ID. Extract a bounded
   set of declared scalar parameters from already-bounded parsing. No second
   unrestricted body read, auth-header copy or full HTTP logger.
2. **Integration.Before:** resolved route/account/provider/attempt and causal IDs.
   Enqueue a separate allowlisted DTO without retaining `ctx`, auth objects,
   request payloads or references to provider headers. Never do archive I/O or
   acquire its writer lock while holding the active-attempt mutex.
3. **Executor start/first output/finish:** use existing monotonic measurement
   foundations, respecting what they measure. First generated output at an
   executor is not network TTFT or proof of arrival at the client. Preserve the
   existing held terminal suffix and durable-accounting failure behavior.
4. **Usage publication:** whitelist genuinely reported nullable values and their
   source. A reported zero differs from unknown. Reasoning, cached, billed tokens,
   price snapshot and resolved model version remain null if unavailable. No
   proportional invention of cost, no catalog marketing name as model version.
5. **Client/tool events:** a separately authenticated, bounded ingestion contract,
   with execution/task/check IDs, exit code, verifier artifact hashes and claimed
   producer. Tool.requested, client-reported result and harness-observed execution
   are separate evidence classes. Client-supplied `human_verified=true` cannot
   grant reviewer authority.

Start with metadata only. Do not add prompt/content fields to the existing
Inspector or its policy ID. Content capture later needs named boundaries:
`client_ingress`, `executor_output`, `provider_egress`, `client_egress`. Record
which one was actually captured. Final provider transport includes auth; capture
body-only before/after known transforms, never whole credential-bearing requests.
Do not label reformatted/redacted JSON as byte-identical original wire data.

### Opt-in, identity and stop semantics

`training_archive.enabled=false` is the master default; no writer, new disk
files or content buffers in the disabled path. Suggested scopes are off,
metadata, and separately consented encrypted-content. A global hard stop wins
over all route/client policy; a denial must not be promoted by another override.
Content requires the client **and** concrete route to be approved. Pooled or
rerouted dispatches re-evaluate their actual source; no inherited paid/provider
fallback or automatic training permission.

Current shared inference-token authentication does not establish distinct client
principals. User-Agent/session labels alone are spoofable. Per-client guarantees
therefore need separately registered principals/tokens or an explicitly isolated
connection before enabling them. This is an implementation prerequisite, not
permission to replace auth in this audit.

The hard stop gates new observations atomically and causes the worker to discard
pending capture according to the reviewed stop policy. It must not wait for disk,
cancel inference or imply erasure of already durable records. Queue drain vs
immediate discard, retention duration and disk budget remain explicit decisions.
A filesystem-full/security fault disables only the archive, reports content-free
health state and leaves existing accounting/routing guarantees intact.

### Privacy and storage proposal

- Metadata is a newly constructed allowlist with bounded enum/numeric/opaque-ID
  fields. HMAC pseudonyms for client/session identities; key managed outside the
  dataset. Do not serialize arbitrary URLs, route names, headers, exception text,
  file paths, tool descriptions or user-provided labels. Declared tool names also
  need a bounded approved-name policy or pseudonyms, not blind inclusion.
- Content and artifacts are sensitive even with apparent secrets removed. No
  OAuth/API keys, passwords, cookies or credential URLs may enter the archive in
  any mode. Encryption is not an exception to this rule. Sanitizer uncertainty
  may omit/quarantine an archive record; it must not fail the provider request.
  Exact byte-original capture and guaranteed removal of secrets cannot both be
  claimed for a transformed record; name the retained representation honestly.
- Bounded queue in both event count **and bytes**, bounded in-flight work, no
  goroutine per chunk and no retention of full HTTP contexts. Never run a scanner,
  AI judge or formatter synchronously on the inference path.
- Local append-only versioned JSONL segments with record lengths/hash/checksum,
  stable event IDs, UTC + monotonic durations and source epochs. One worker owns
  rotation/fsync and crash-tail recovery. Queue acceptance is not durability;
  bounded batch fsync can lose accepted events on crash. Report an unknown-loss
  interval where the exact count is unknowable rather than inventing zero loss.
- Separate SQLite catalog and migrations, not the Host's accounting tables.
  Rebuild catalog from validated committed JSONL segments; idempotent index by
  event ID and transactionally store catalog checkpoint. Corrupt/truncated tails
  become visible gaps; no alteration of routing or Usage journal acknowledgments.
- Deletion obligations override perpetual immutability. Separate immutable payload
  objects, versioned derivations and a deletion lineage; remove affected content,
  derivatives, caches and managed exports/backups, preserve only a permissible
  minimal tombstone. Cryptographic erasure requires key/backup design and restore
  tests. Deleting a dataset cannot promise removal from already trained weights.

## Proposed schema and artifact separation

A causal graph is authoritative; a readable chat is a derived projection. The
following is a synthetic proposed event, not an actual schema/migration contract:

```json
{
  "schema_version": 1,
  "event_id": "synthetic-event-001",
  "event_type": "tool.requested",
  "trace_id": "synthetic-trace-001",
  "span_id": "synthetic-attempt-002",
  "parent_span_id": "synthetic-request-001",
  "source_epoch": "synthetic-capture-001",
  "request_id": "synthetic-request-001",
  "session_id": null,
  "client_id": "hmac:synthetic-client",
  "observed_at": "2026-10-29T12:00:00Z",
  "observation_boundary": "executor_output",
  "observation_kind": "model_requested",
  "tool_call_id": "synthetic-call-001",
  "actual_execution_observed": false,
  "synthetic": true,
  "completeness": "partial",
  "payload_ref": null,
  "training_permission": "unknown_not_approved",
  "source_license": "unknown",
  "policy_revision": "synthetic-policy-001"
}
```

Extend the metadata envelope with endpoint/API enum, requested alias vs resolved
model/provider/account pseudonym, attempt ordinal, parameters actually present,
stream/finish/error classifications, observation-based latency fields, nullable
usage/cost and source IDs. Separate raw-observation object, normalized-message
projection, sanitized derivative, AI suggestion, scoped human review, export
manifest and holdout membership. Preserve hashes and transformation/version links.
Opaque continuation signatures are transport artifacts, never natural-language
reasoning targets; prefer digest/omission at metadata level.

Canonical event vocabulary follows the brief (session/request/model/tool/test/
correction/review events). `tool.executed` and `test.result` require their actual
source/evidence. Missing results, denied tools, disconnected streams and fake
runs remain explicitly missing/failed/synthetic. A corrected answer is a derived
candidate until reviewed; verification scope can be narrower than an entire chat.

Each source/derivative carries separate collection/retention, inspection,
evaluation, external-review, training and export eligibility with purpose,
provider/model/service, contract/license evidence, reviewer, date and expiry.
Unknown or disallowed dependencies exclude export for that purpose. Customer data,
repo/tool licenses, model weight license and hosted-service output rights are
independent. Gemini must not silently supply targets for a competing model.
Select a concrete Qwen release/template/license later, not a family-wide promise.

Freeze leakage groups before rewriting examples or teacher synthesis: incident,
conversation branches/retries/corrections, repository lineage, near duplicates and
time. Choose holdout fraction from eligible sample diversity; do not pretend that
random message splits measure unseen-repository or future-time generalization.

## Small next increments and review gates

| Increment | Deliverable | Gate and demonstration |
| --- | --- | --- |
| TRACE-0 (this work) | Runtime/source gap analysis and three independent proposals | Review hook boundaries, first protocol/client, identity, data policy and fault semantics |
| TRACE-1 recommendation | Offline synthetic causal schema + small readable/API viewer | No traffic capture. One fake tool request, client result, genuine-vs-synthetic verifier distinction, retry/failure/correction and export eligibility |
| TRACE-2 / brief Phase 1 | Disabled-by-default metadata collector, its own migration/catalog, health and rollback docs | No bodies; verified client identity; demonstrate response equivalence and fail-open under pressure |
| TRACE-3 / brief Phase 2 | Explicit selected-session encrypted content recording + local viewer/redaction | Separate approval of capture boundary/retention/key management and provider rights; opt-in demo only |
| TRACE-4 / brief Phases 3–4 | Offline review/curation, permission closure, dataset registry and sandbox evaluation | No indiscriminate dataset downloads or unapproved cloud judge; original vs corrected needs evidence/rubric |
| TRACE-5 / brief Phase 5 | Benchmark and eligible model/adapters experiment | Later training authorization, eligible corpus, independent holdout and tested sandbox |

TRACE-1 is a recommended sequencing addition for the user's understanding goal;
it does not silently replace the brief's metadata-first rollout. A metadata-only
collector cannot show raw chats, so do not market that first phase as achieving
content inspection. Third-party dataset candidates in the brief remain research
items; their labels/licenses and row-level lineage were not verified in this audit.

## Tests required before a collector can ship

Use fake providers and synthetic canary secrets, never replay private traffic for
qualification. Test the exact installed baseline and then exact proposed binary:

1. Disabled/enabled metadata mode produces equivalent nonstreaming bodies,
   statuses/headers and streaming payload/order/flush behavior; no extra inference
   or retry. SSE split frames, UTF-8, `[DONE]`, Responses terminal failures,
   client abort, slow consumer, 429/retry and protocol translation cases.
2. Prove optional `Flusher`, `Hijacker` and other writer interfaces unchanged;
   qualify WebSocket upgrade/frame/close handling separately if admitted at all.
3. Concurrency and chosen-client/route isolation, spoofed IDs, pooled dispatch,
   config change and hard-stop races. No content fields in metadata projections.
4. Canary secrets in headers, query/path URLs, bodies, tool names, errors and
   oversized malformed inputs. Public metrics/errors contain only fixed safe
   codes and counters; no debug bypass or full SDK Record dumps.
5. Disk full, slow/fsync-failing disk, queue saturation, corrupt records, crash
   tail, shutdown timeout and catalog rebuild. Assert archive drops/gaps and no
   added provider failure; separately verify existing durable Usage still fails
   closed on its own journal fault.
6. Benchmark p50/p95/p99 request/TTFT/stream gaps, CPU/RSS, allocations, bounded
   queue bytes and writer/disk behavior under identical synthetic load, disabled
   vs enabled metadata and faulted collector. Suggested acceptance budget:
   no statistically material increase, a reviewed absolute/relative p99 bound,
   and no unbounded resource growth. Numerical limits must be agreed from the
   baseline; no performance result is claimed by this proposal.
7. Content phase: scoped consent, expiry/stop, redaction attacks, encryption key
   failure, quarantine, rights revocation, deletion propagation, restored backup
   policy and exact UI reading/copy/navigation tests with bounded rendering.
8. Synthetic end-to-end demo: request -> tool requested -> separately attributed
   execution/result -> failed test -> retry -> correction -> scoped review ->
   sanitized example -> permitted SFT/DPO/eval manifests. Never label a fake test
   or teacher vote as real verification. No actual training is needed.

Rollback in the metadata implementation should first hard-disable archive
observation and its worker without changing routes, then revert only its feature
wiring/config. Preserve or explicitly dispose of archive data by the reviewed
retention policy; never delete Usage, revoke accounts, or clear the existing queue.
A concrete CLI/config reference, executable JSON Schema and migrations belong to
that implementation after this gate, not to a silent Phase-0 runtime change.

## Independent reviews and open decisions

- [Raw/message view and exact capture boundaries](trace-research/raw-view.md).
- [Causality, review, splits and deletion design](trace-research/dataset-design.md).
- [Dated rights research and its evidence limits](trace-research/rights-research.md).

Before implementing genuine capture, decide: first named boundary/protocol;
trusted per-client identity; selected-session vs continuous scoped collection;
content/key/retention budget; pending queue treatment on kill; legacy error-log
policy; and the first explicitly eligible source. None requires starting model
training now. Recommendation: first a completely synthetic local specimen so the
user can inspect an actual-looking API exchange and mark a decision point, then
qualify the metadata observer and a deliberately selected content-capture path.
