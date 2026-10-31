# Capture coverage: recorder implementation and qualification

31 October 2026 · Candidate source implementation. **Not installed or enabled in
production.** `forwarded`, `parsed in synthetic fixtures`, `recorded`, and
`qualified end-to-end` are separate states. The durable Saved conversations view
is separate from the short-lived Inspector. Actual results and source/artifact
pins are in [execution evidence](recording-execution-2026-10-30.md).

## Evidence in this increment

- Real isolated candidate Host + candidate Core + local fake Chat provider:
  Responses ingress, nonstream and SSE, fragmented tool arguments, client-reported
  result continuation, model change, evidenced worker, nullable usage, secret
  screening before persistence, selected-source identity/paging, off, restart and
  archive clear. This is actual HTTP/process/storage behavior with synthetic data.
- Pure normalization/assembly fixtures cover Responses, Chat Completions,
  Anthropic and Gemini. These do not prove native live provider behavior.
- Go adapter unit/race suites cover callbacks, pressure/drop handling and existing
  inference behavior. .NET tests cover policy, source failure/recovery, projection,
  API/CLI and bounded UI. Native and load receipts are separately identified.
- Observed boundaries are client ingress and executor output. Final client-egress
  bytes, exact provider wire requests and actual local tool execution are not
  independently observed by this increment. No blanket full-protocol promise.

## Protocol/API matrix

| Client-facing path/protocol | Current lain admission | Existing partial evidence | Additional protocol acceptance boundary |
| --- | --- | --- | --- |
| POST `/v1/responses`, nonstream/SSE | Admitted | Durable attempts/usage, selected output and evidenced dimensions | First complete Codex/Responses path: request/response representations, tool deltas/results, completion/error/abort and repeat-history references |
| POST `/v1/responses/compact` | Admitted by adapter | Some request/attempt dimensions | Explicit compaction event and payload coverage; no claim that missing earlier context was reconstructed |
| POST `/v1/chat/completions`, nonstream/SSE | Admitted, route/policy dependent | Existing usage/output parsing | Independent parity/normalization suite; not automatically covered by Responses success |
| POST `/v1/messages`, Anthropic nonstream/SSE | Admitted, route/policy dependent | Existing Claude translation/output support | Content-block/tool IDs, signatures as opaque metadata, stop/error/cancellation fixtures and exact client-egress parity |
| Gemini Generate Content native URL | Not in public adapter admission allowlist | Underlying SDK has Gemini executors/formats | Parser fixtures may be added; real ingress recording unsupported until explicitly admitted and qualified, no new route merely for logging |
| Gemini Interactions native URL | Not in public adapter admission allowlist | SDK executor availability | Same distinction; actual coverage must be tested separately |
| Client-facing WebSocket upgrade | Inference admission accepts POST, not a generic GET upgrade | Some SDK/provider transports can use WS internally | Unsupported in initial recorder qualification unless explicit route/interface gate is added; no blanket WS promise |
| Provider-side HTTP/WS | Varies by executor | Existing executor/transport and usage observations | Identify each actually observed boundary; byte-original upstream and client output must not be conflated |

First delivery may qualify protocols in successive small changes; no "full Sprint1
protocol coverage" assertion until each admitted path's own gates pass. Unsupported
formats still produce safe metadata/completeness states, never invented messages.

## Client and event matrix

| Source/event | What proxy can establish | What remains absent/conditional |
| --- | --- | --- |
| Codex CLI | Its configured lain-bound requests; reported session/turn/worker IDs when supplied | Active per-thread/provider configuration and capture principal must be checked; shell execution/test outcomes are not proven |
| VS Code/Codex | Model requests actually sent through lain | Cached per-thread providers and app/plugin traffic may bypass it; name does not prove coverage |
| OpenCode | Future explicitly connected protocol requests | Not on PATH/standard state path in prior audit; no qualified client adapter |
| Other desktop/apps | Requests using the selected lain endpoint | No invisible interception of bypassing traffic |
| Tool requested | Provider-generated name/arguments/scoped IDs | Not an execution |
| Tool result supplied by client | Content of the submitted result with source attribution | Exitcode/command execution is client-reported unless trusted adapter evidence exists |
| Subworker/parent | Supplied IDs with supported evidence | Missing/conflicting parents remain unassigned; don't infer from text/timing |
| Retries/fallback | Actual attempt identity, route/model changes and available outcome | Failed attempt content not merged into successful attempt; unknown cause stays unknown |
| Approvals/patches/tests/acceptance | Future separate adapter events | Sprint2; no fake terminal verification in Sprint1 |
| Usage/reasoning/cache | Only genuinely reported fields, nullable with source/measurement semantics | No inferred zeros, hidden reasoning, true vendor billing or exact revision from a moving alias |
| Training eligibility | Separately recorded source permission/status | No inference from capture permission, model access or Open-Weight branding |

## Capture policy and representation coverage

- Disabled: no new collector content buffers or archive files. Existing accounting
  and Inspector keep their own unchanged policies.
- Enabled metadata-only: typed allowlisted facts, opaque/pseudonymous IDs, no full
  SDK record/header/body copy. Unavailable facts remain null.
- Enabled content-opt-in: all model/provider routes used within the authorized
  local scope by default, private unencrypted local files, explicit retention,
  known observation boundaries and redaction/completeness markers. Redaction
  failure omits content. No archive encryption/key-unlock workflow in current
  scope; no training-rights allowlist blocking ordinary local inspection.
- Raw body, normalized object, provider output, client-emitted output and derived
  final message use distinct representation/source tags. Unsupported/partial data
  is not a complete chat, and a redacted record is not byte-original raw data.

Consult the execution ledger for exact source/artifact pins and actual test evidence.
Synthetic examples must be labeled synthetic; a future 200-call/100-tool fixture
proves graph mechanics, not real execution or rights to train on production data.

## Skill-version comparison coverage

A saved run may carry user-configured or client-reported skill/prompt variant IDs
and hashes. This is not evidence of a skill actually loaded or followed. Preserve
`configured/reported/observed` provenance and unknown versions; don't scan/import
all skills or change prompts just to populate the fields. The first browser can
show available run manifests; automated replay/assessment is later.
