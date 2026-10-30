# REC-03 — Incremental protocol, message and tool normalization

30 October 2026 · **Proposed work package; documentation only.** No normalizer,
fixture test, recorder or migration has been implemented by this document.
Entry point: [package register](README.md); shared schema/fixture authority:
[REC-00](00-contracts.md); current outcome:
[recording plan](../training-logging-plan.md). Peer input:
[REC-02 observer](02-observer.md). Storage/commit ownership is REC-04;
causal projection and queries are REC-05, public API/collector REC-06.

## Goal and accepted scope

Turn admitted, source-linked observations into readable persisted conversation
projections: messages/parts, requested tools and client-reported results, real
attempts/retries/model changes, evidenced branches/workers and available run/skill/
prompt variant provenance. A human must be able to inspect what was actually
available and where coverage ends. Model switches do not split a conversation by
default; all models used in enabled capture scope are eligible for ordinary local
inspection. Missing training rights do not suppress this projection.

The first implementation includes coherent Codex/Responses conversations with
minimal browsing through REC-05/06 and the browser owner. Other admitted protocols
need their own parity gates; unsupported records retain safe coverage evidence.
A proxy-emitted tool request is not a local execution; a submitted exit code is
client-reported. Genuine execution/approval/test adapters, scoring, replay,
anonymized export, training, hidden-reasoning recovery and automatic prompt/skill
changes are later work. No encryption/keyring workflow: approved persisted data
is private unencrypted content under REC-04's OS0600/0700 contract.

## Baseline and proposed exclusive ownership

Installed audit baseline is CLIProxyAPI8.0.41/lain-patch4 at upstream
`8289ec4c71ad914d0612c2776352b76ba3d355a6`; binary SHA256
`191f4f17e4096558c32ebc5b28ec51341fadc6549f3269300af22800dfe6ce18`.
Checkout is patch5. REC-00 freezes the implementation baseline; source parsing
success is not installed capture evidence. Preserve existing WIP, accounting and
Inspector policies and installed Desktop performance behavior.

Proposed exclusive implementation paths, **not changed in this planning task**:

- New `src/Lain.Host/Recording/Normalization/` for pure protocol readers,
  bounded stream assembly, semantic normalization and derived provenance handling.
  Durable cross-request causal graph/index/query logic belongs exclusively to REC-05.
- New `tests/recording-normalization/` for its synthetic fixture runner and tests.

REC-00 owns common Core/client/Go DTOs, shared fixtures and existing entry-point
wiring. REC-02 owns new Core capture modules and submits wiring diffs to REC-00. REC-01 owns secret/scope policy. REC-04 owns archive source,
commit/recovery and migrations; REC-05 owns causal index/query code, REC-06 the
collector/public API. Shared Host registration remains integrator-owned. REC-07 owns Desktop; REC-06 owns CLI/typed client. Project/solution references
and shared service registration are integrator changes, not parallel package
edits. New shared code or ownership expansion returns to the register first.
Do not edit `InspectorView.cs`, existing Inspector normalization or Go parser
files to make this package's fixture tests pass.

## Dependencies and stable seam

After REC-00 freezes executable contracts and synthetic source fixtures, pure
normalization can proceed against an in-memory observation stream independently
of REC-02 and REC-04 implementation. Integration requires REC-02's source/boundary/
gap contract, REC-01's screened-content result, REC-04's committed source reader
and REC-05's derived-change input stub.

Consume only typed, admitted records with source epoch, sequence/event identity,
request/attempt linkage, boundary/protocol/representation, policy revision,
redaction/completeness and allowed payload references. The parser has no access
to raw auth contexts, SDK headers, ordinary logs, another client's archive or
network. It must not create its own authoritative journal or SQLite tables.
Publish idempotent derived changes keyed by source record identity plus projector
version, with explicit source references. REC-05 commits derived rows/checkpoints
after REC-04 source durability under REC-00's transaction ordering. Parser output alone never means durable.

Required contract decisions before full integration: canonical versus alternative
representations of one observation; per-client/source identity namespace; item/
call fragmentation IDs; completeness and unknown crash intervals; parser-size/
expansion/state limits; variant-manifest attachment and provenance; supported
protocol matrix. Do not silently choose semantics when the contract is absent.
Suggested resource numbers in the main plan remain proposals until settled.

## Work sequence

1. **Deterministic reducer and lineage.** Keep original source records immutable
   during their retained life. Normalize using an explicit parser version and
   record-span/JSON-path/item-index links. Distinguish raw body as retained,
   normalized object, provider output and client-emitted output. Alternative
   observations of the same message are related representations, not several
   fabricated assistant messages. If relation is unproven, show separate request
   observations rather than content-based deduplication.
2. **Incremental protocol assembly.** Responses first: roles/content parts,
   text, function/custom tool items where supported, item/call/output/content
   indices, argument deltas, result input, terminal usage and explicit abort/error/
   compaction. Handle split UTF-8, JSON and SSE frame boundaries without recursive
   unescaping. Parse actual protocol frames, not `bytes.Split` of arbitrary chunks.
   Ignore keepalives as message content. Subsequent Chat/Anthropic gates need
   equivalent explicit call/block state; Gemini fixtures alone do not establish
   admitted ingress or live coverage.
3. **Causal identity.** Keep conversation, branch, turn, request, attempt, message,
   content item and worker distinct. Namespace external IDs by trusted client/
   harness/source, retaining local IDs separately. Reuse the existing dimension
   evidence meaning, including conflict-to-unknown behavior. A new source epoch
   does not prove a new external conversation; a matching unscoped ID across
   clients does not prove continuity.
4. **Tools and worker edges.** Assemble interleaved tool calls by explicit scoped
   IDs/index state; match later submitted results without guessing by name/time.
   Preserve pending/incomplete/denied/missing status and multiple result items.
   Keep model-requested, client-reported and provider-reported results separate;
   `executed` requires a later trusted execution adapter. Link workers only from
   supported supplied parent/child evidence. A branch/fork is not automatically a
   worker, nor a `spawn_agent` name proof of child traffic actually observed.
5. **Repeated context, resume and alternatives.** Each full-history resend remains
   its own request occurrence and source evidence even if REC-04 shares payload
   bytes. A readable projection may suppress a proven repeated source item while
   retaining occurrence links; identical text alone is not proof. Partial retry
   output cannot become the successful attempt's answer. Preserve model changes,
   response-chain/state references, missing prefixes, fork divergence and
   compaction as explicit coverage boundaries.
6. **Measurements and run variants.** Keep reported nullable usage/cache/reasoning
   components, requested versus effective controls, actual versus requested model
   and measurement sources. Unknown remains null and explicit zero remains zero;
   no token, billing, success or exact revision inference from a marketing alias.
   Attach a run/variant manifest from REC-00/04 with skill/prompt hashes and
   configured/reported/observed evidence. Do not scan all skill directories, load
   hidden files, alter prompts or claim a configured hash proves instructions were
   actually loaded or followed. Model self-report notes remain annotations.
7. **Bounded recovery and handoff.** Limit active parsers, frames, argument/string
   assembly, nesting/expansion, reference resolution and working
   memory. Expire/close partial assembly with explicit gaps; process only selected
   payload pages. A malformed record must not exhaust the Host or poison unrelated
   conversations. REC-04 exposes committed-source replay; REC-05 owns atomic
   semantic projection checkpoints;
   reducer replay is deterministic and idempotent across duplicate delivery.

## Actual source constraints

The current Core `internal/lain/observation.go:67` discards tool arguments; its
Anthropic `input_json_delta` handling at `:143` emits no stable call identity.
Host `InspectorStore.cs:172` removes tool text again. These ephemeral outputs
cannot provide complete conversation normalization, so consume REC-02's separate
screened capture contract rather than reading the Inspector ring or Usage DB.

`internal/lain/dimensions.go:136` recognizes explicit Codex `thread_spawn` plus
child/parent IDs as worker evidence; plain thread ID means conversation.
`:204` deliberately avoids promoting multiple tool results to one `toolCallId`.
Retain these semantics but parse each payload item independently. SDK interceptor
RequestID, HTTP trace ID and lain attempt ID are not interchangeable identifiers.

SDK after-auth observes before executor translation. Handler response interception
precedes validation and actual writes; HTTP transport does not capture WS frames.
Therefore a normalizer cannot upgrade `normalized` to `provider_request` or
`client_egress`, repair missing WS coverage, or infer successful delivery because
it saw a terminal response item. Each claimed source boundary must carry REC-02's
actual qualification. First qualified Responses does not imply all admitted API
paths passed; all-model enabled scope and per-protocol coverage are separate axes.

## Privacy and safe failure

Normalization is downstream of REC-01/02's first exclusion gate. Revalidate allowed
payload references and redaction status before materializing a projection. It is
not acceptable to persist a raw record first and sanitize only the readable view.
REC-04 receives only permitted content/metadata; temporary parser buffers never
spill to disk or become debug/error output. Error DTOs contain safe classifications
and source IDs rather than exception text containing offending bodies. Injected
instructions inside a message remain inert text and cannot alter scope, policy,
variant authority, filesystem paths or service behavior.

Do not hash omitted credentials/private keys/secret files into an index as a
substitute for exclusion. Variant hashes cover authorized instruction material
under the shared policy, not arbitrary secret files. Unsupported opaque reasoning
or signatures are omitted/digested only if REC-00/01 permits that representation;
no decrypting or reconstructing hidden thoughts. Legitimately retained redacted
conversation text may be plaintext in private approved storage. Sanitizer failure
omits affected content with explicit source coverage; no raw fallback.

## Observable acceptance — planned, not run

| Case | Required observable outcome |
| --- | --- |
| Multi-turn root and two workers, fork and model switch | Stable scoped graph and ordered parts; only proven edges; lazy-load-friendly references; no merge of unrelated simultaneous requests |
| Parallel same-name tools, fragmented arguments and later results | Exact supported argument assembly, distinct calls, submitted results correctly linked, source paths/spans retained; execution remains unproven |
| Retry with partial output, error, canceled stream, terminal before failed write | Distinct attempts and incomplete/delivery states; failed output never relabeled as successful answer |
| History resends and identical text | Every request occurrence retained; physical storage sharing does not collapse logical events; any readable suppression has an explicit identity basis |
| Stateful previous response, resume, compaction, unknown prefix | Known links retained, missing context visible; no fabricated complete history |
| UTF-8/SSE/JSON splits, escaped text, keepalives, huge/invalid frames | Correct one-time decoding and item boundaries; inert literal content; bounded omission/recovery on invalid or oversized input |
| Gaps, source restart, duplicate/out-of-order delivery, corrupt record | Deterministic idempotent projection or explicit gap/rejection; no silent cursor repair or duplicate messages |
| Usage/model/reasoning variants | Exact null versus zero, requested versus reported/effective and source preserved; no invented cost, model revision, reasoning or outcome |
| Skill/prompt manifests | Configured/reported/observed distinguished; missing version remains unknown; no automatic filesystem scan or prompt intervention |
| Secret/injection canaries and unsupported content | No credential content in output/index/temp/log paths; unsupported representation not called complete; injection does not execute or mutate policy |

Include REC-00's proposed synthetic200-call/100-tool graph as a workload, not an
approved resource limit or real execution proof. Measure bounded state, worst-case
frame/argument expansion, per-page memory and latency with the package's agreed
budgets, including many small worker/tool items and repeated long histories. Pure
projector tests are separate from REC-02 transport parity and REC-04 restart and REC-07 UI
qualification. One parser test passing cannot establish a captured real client.

## Handoff evidence and return conditions

Return owned diff/source pin, supported protocol/item matrix, versioned projection
schema usage, source-to-derived example links, synthetic fixture IDs, exact tests
and results, deterministic replay/duplicate proof, nullability/conflict evidence,
resource measurements, and integration examples for REC-05/browser queries.
Supply missing/unsupported case results as well as successful cases. Register
shared-fixture additions with REC-00; do not mutate a common fixture to hide a
transport/parser disagreement. No customer conversations, credentials, provider
calls or replay requests are needed for package evidence.

Return to REC-00/integrator if the shared identity/representation contract is
missing or contradictory; a needed source field was never observed; a client
cannot be safely distinguished; parser evidence disagrees with transport framing;
a source gap would require guessed joins; run manifest authority is ambiguous;
secret exclusion would require unsafe raw fallback; or measured assembly exceeds
the agreed budget. Return to REC-02 for missing observable fields, REC-04 for
source transaction/migration requests and REC-05 for projection/query requirements. Do not silently change capture, storage schema,
accounting, protocol admission, UI behavior or SDK hooks from this package.
