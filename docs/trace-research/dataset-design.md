# Optional trace archive and dataset design — Phase 0 proposal


> Scope update, 30 October: the user removed archive encryption/key management
> and prioritizes human inspection and skill-version comparisons across all
> recorded models. Earlier mandatory-encryption proposals below are superseded.
> [The current Sprint1 plan](../training-logging-plan.md) is authoritative for the planned scope;
> secret exclusion/private OS permissions and separate training/export rights remain.

29 October 2026. Architecture review only. This document proposes a separate,
optional archive for protocol inspection, review of successful and unsuccessful
branches, and eventual training/evaluation on permitted material. It does not
approve or implement capture, ingest genuine transcripts, change the Inspector,
change the usage journal, or make an inference request. All example identifiers
and sequences below are synthetic. Schema names and package boundaries are
recommendations, not accepted public API contracts.

## Existing boundaries and their consequences

The current [observability contract](../observability-contracts.md#inspector-api-and-ephemeral-dataflow)
keeps bounded output in volatile Core/Host rings. Its `redacted_output_only_v1`
policy excludes prompts, arbitrary headers, tool arguments/results and unfiltered
errors. Sanitized display text is lossy and is not guaranteed free of private
information. The [Live Inspector contract](../contracts/live-inspector.md#content-boundary)
explicitly forbids durable raw conversations as a side effect of improving the
screen. That feed cannot supply an exact request, exact API response, or proof
of actual tool execution. Persisting it would be a different, partial product,
and still requires an explicit retention/policy extension.

The [usage contract](../service-contracts.md#request-execution-attempt-and-event)
counts logical incoming requests separately from upstream attempts. Repeated
payload text or nearby timestamps do not establish a conversation. Existing
`(sourceId, requestId)` and `(sourceId, attemptId)` references, where available,
should be reused as links, without changing journal serialization or digests.
Correlation evidence records an assertion, not authenticated authority.

The [Core integration](../../proxy/adapter/internal/lain/integration.go) appends
`attempt_started` before dispatch and appends final usage/completion before
ordinary success is released. [Journal.Append](../../proxy/adapter/internal/lain/journal.go)
latches write/fsync faults. The [proxy contract](../../proxy/README.md#why-the-official-release-binary-and-unmodified-sdk-are-insufficient)
requires synchronous durable usage before the RAM queue and gates terminal stream
markers. **An optional archive failure must never disable, bypass, defer or
replace these existing fail-closed boundaries.**

The [segmented journal](../journal-segments.md#format-und-crashvertrag) is an
explicitly adopted accounting format with no deletion/retention policy. Its
no-loss and byte-preserving migration guarantees do not apply automatically to
this proposed archive. Reusing journal files for content would entangle retention,
privacy, accounting recovery and replay; a separate store is preferred.

## Minimal causal model

Keep observations as events and causal relations as explicit edges. A flattened
chat transcript is a derived view, not the source of truth. Stable internal IDs
are opaque and source-scoped; original protocol IDs retain their original scope.
No synthetic global clock order is presented as proof of causality.

| Entity | Minimum purpose |
| --- | --- |
| Capture session | Capture source/instance, schema and policy versions, start/stop state, selected scope, retention and bounded-loss status |
| Request / attempt | Logical incoming request versus concrete execution attempt; requested model/route separate from observed actual model/provider/account |
| Message / content block | Explicit role/channel, author/source, block index and protocol linkage; no role or hidden reasoning inferred from prose |
| Tool invocation | A model-requested action with protocol call ID, tool name and argument artifact; distinct from any actual execution |
| Tool execution | One observed execution instance, possibly one of several retries; executor-reported start/end and result artifacts |
| Review / correction | A claim about an observed branch, with author kind, evidence, verification and separate replacement material |
| Artifact / transformation | Immutable captured bytes or a separately versioned derived representation, with provenance, rights and deletion lineage |
| Dataset manifest | Exact eligible artifact/sample versions, permission snapshot, split assignment and exporter/template/tokenizer versions |

An event envelope should contain `schemaVersion`, `eventId`, `sourceId`,
`sourceInstanceId`, `sourceSequence`, `observedAt`, optional `occurredAt`,
`kind`, scoped entity references, `evidence`, `payloadRef`, and capture status.
`observedAt` is collection time; `occurredAt` is a source assertion. A sequence
orders only its own source. Causal edges carry `from`, `to`, `relation`, and
`evidence` (`observed`, `harness_asserted`, `human_verified`, or
`ai_suggested` plus source locator/version). Evidence types must not grant
execution or dataset permissions.

Use distinct event kinds, for example:

| Event | What it establishes |
| --- | --- |
| `tool_requested` | The model emitted a tool request; complete arguments versus partial streamed arguments remain explicit |
| `tool_execution_started` | An instrumented executor reported starting one execution; a proxy alone usually cannot observe this |
| `tool_execution_finished` | Executor-reported completion/outcome; command exit zero is separate from task correctness |
| `tool_result_submitted` | The client submitted a tool result to an API; it does not by itself prove execution or authenticity |
| `tool_request_rejected` | A request was denied or malformed before execution, if actually observed |
| `api_attempt_started/finished` | Transport/model lifecycle, independent of a requested tool's lifecycle |
| `review_added`, `correction_proposed`, `correction_verified` | A later evaluation or improvement; no rewriting of what originally happened |
| `capture_gap`, `payload_omitted`, `capture_stopped` | Known loss, exclusion or truncation; absence is never successful completion |

A tool request can have no execution, multiple attempted executions, one result
submitted repeatedly, or several results with conflicting evidence. Keep all
observations, flag ambiguity, and do not silently choose a match. Matching a
call ID is useful only within a proven conversation/protocol scope. Replayed
message history must refer back to known content when proven; equal content
hashes alone do not prove it is the same occurrence.

A synthetic path may be:

```text
request R1 -> API attempt A1 -> tool request T1
                                |
                                +-> execution E1 -> result X1
                                +-> execution E2 -> result X2 (retry of E1)
client submits X2 -> continuation R2 -> assistant message M2
human review V1 rejects M2 -> correction C1 -> verification V2
```

Without executor evidence, retain only `T1 -> submitted result X2` as a client
assertion and leave E1/E2 unknown. Correction C1 is a separate candidate tied to
V1, not a claim that it was the historical assistant output.

Retries retain their logical request relation but get distinct attempt IDs;
`retry_of` and `fallback_of` state known reasons and prior visible-output state.
Conversation branches get explicit `fork_from` message/event references when
provided. A branch abandoned by the user is not automatically wrong. Parallel
workers have separate identity/parent relations only where evidenced. Missing
branch boundaries, source restarts and stream gaps stay visible in all views.

## Protocol and artifact fidelity

Record the observation boundary, for example `client_ingress`,
`provider_egress`, `provider_ingress`, `client_egress`, or `harness_executor`.
Pre-translation intent is not the request actually sent upstream. API response
bytes received from a provider are not necessarily the bytes delivered to the
client; held completion, cancellation and translation can change them. Any future
transport research must name exactly which boundary it can observe.

For each permitted artifact retain media type, byte length, digest, encoding,
stream/chunk position, completeness, content class, observation boundary and
provenance. Preserve bytes before JSON normalization where exactness is required.
Parsed JSON, concatenated deltas, rendered messages and Qwen chat-template tokens
are separate derived artifacts with parser/template versions. Streaming chunk
boundaries, partial JSON and unsuccessful terminal states cannot be silently
repaired. Exact payload equality applies only to the bytes actually retained.

Do not interpret “raw API” as collecting credentials or every HTTP header. A
future body-only source may be byte-exact for that body, while authentication,
cookies, signed URLs, sensitive headers and configured exclusions are omitted.
Its manifest must declare the omissions; it is not a complete wire dump. If
redaction occurs before storage, call the stored object sanitized capture, not
raw original. Do not retain hidden/internal model reasoning or infer it; only
explicitly returned, permitted output channels are eligible even for review.

Separate artifact provenance from review status:

- `captured_source`: captured bytes, immutable while retained; completeness and
  permission remain independent. “Source” does not mean correct or safe.
- `sanitized_derivative`: transformation ID/version, input references, rule set,
  output digest and omissions. Secrets/private data can exist inside prose,
  code, images and tool output; an automatic scan is not sufficient approval.
- `derived_view`: parsed/assembled/extracted material with exact input versions.
- `ai_suggestion`: machine-proposed labels, repairs, summaries or sample choices,
  with model/prompt version and input lineage; no self-certification.
- `human_review`: reviewer identity/time, rubric version, evidence and scope of
  verification. A human-verified label does not convert an edited sample into an
  original observation or establish rights that the reviewer does not hold.

## Permission and dataset admission

Collection, retention, local review, automated review, SFT, preference training,
evaluation, export and redistribution are separate purposes. Store each as
`allow`, `deny` or `unknown`, with actor/authority, material/scope, terms or grant
reference/version, effective time, expiry/revocation and review evidence. Missing
or unknown permission excludes the affected purpose. Ownership of an account or
a request is not enough to infer rights to provider output, third-party code,
customer material or tool results. Provider/model terms must be checked for the
specific route and use; this document makes no provider-specific legal claim.

An AI curator may propose labels and transformations within its permitted input
scope; sending restricted content to another model/provider is a separate use.
Do not route originals to an external reviewer merely because later local Qwen
training is desired. A redaction transformation does not automatically cure
underlying licensing, confidentiality or privacy restrictions.

Dataset admission requires permitted purpose, source lineage, approved sanitation,
quality review and split assignment. Record the resulting effective restriction
as the intersection of all dependencies, including copied/replayed context,
repository snippets and tool results. Revocation or deletion of a dependency
invalidates derived manifests and exports; subsequent builds must exclude them.
Keep an export/release registry so affected copies and training runs are known.
Deletion of archive records does not erase already trained weights or prove that
an uncontrolled external copy was removed; withdrawal/retraining obligations
need an explicit policy before any training or publication.

SFT exports should include selected, verified assistant targets and necessary
permitted context, with explicit loss masks. User/system/tool content is context,
not implicitly assistant training targets. Partial arguments, malformed tool
syntax and failed branches remain review evidence unless a deliberate negative
or repair dataset is specified. Preference pairs share a verified common context,
identify the chosen/rejected paths and review rationale, and mark synthetic
corrections as synthetic. Pairing unrelated “good” and “bad” sessions creates a
misleading training signal. Qwen format conversion belongs at export and must be
versioned against the eventual selected model/template; no model family choice
changes the provenance or permission tests.

## Holdouts and evaluation

Split before AI rewriting, augmentation, sample extraction or preference pairing.
Group all branches/retries/corrections of one incident/task family, conversation,
repository lineage and copied context into one connected leakage group. Exact
and approximate duplicates are checks for leakage, not evidence of shared
conversation identity. Record manual group merges and uncertainty. If a grouping
rule connects most data, report the reduced independent sample count instead of
quietly splitting the group to reach a desired percentage.

Use three named evaluations with different claims: a grouped in-domain holdout,
an unseen-repository holdout, and a future-time holdout. A time cutoff must use
source event time where reliable; import time cannot make old incidents new.
Exclude groups spanning the cutoff from the claimed future set, or freeze their
entire group on the non-test side and document the loss. Repository forks,
cherry-picked fixes, shared incidents and repeated benchmark prompts can cross
nominal repository/time boundaries and require lineage checks.

Do not describe a grouped random split as proving unseen-repository or future
performance. Lock the evaluation manifest and targets before model selection,
prompt iteration or automatic correction; repeated tuning against it turns it
into development data. Keep final holdout labels/answers unavailable to curators
that generate training material. Percentages and minimum useful sample counts
remain an experiment decision; report incident/repository counts and coverage
besides token/sample totals.

## Independent storage and failure behavior

Recommendation after a future implementation approval: a separate archive writer,
separate health state, separate content objects and rebuildable index. No content
in usage SQLite/journal, normal logs, event evidence keys, metrics or crash logs.
The archive cannot block dispatch or successful terminal delivery. Reading a
slow archive or reviewing a dataset cannot hold the usage journal mutex.

A nonblocking bounded metadata queue may contain fixed-size envelopes and bounded
payload handles only. A “handle” retaining an arbitrarily large request buffer is
still content memory and must count toward a separate total byte budget. Reserve
capacity before copying, cap per-event/per-stream/global bytes and active streams,
and drop archive capture on capacity failure. Do not spool unbounded originals,
block a provider stream on disk, or retry forever. Choose concrete caps, maximum
capture duration, disk quota/free-space reserve and overflow policy at the later
design gate. A separately mounted/limited archive reduces disk contention; a
process alone does not isolate filesystem exhaustion from accounting storage.

Expose archive states such as off, capturing, degraded and stopped plus bounded
reason codes, enqueue/drop counters, committed watermark and last successful
flush. Never publish raw payload snippets as errors. If exact drop counts cannot
survive a crash, report an unknown loss interval rather than inventing a zero.
Overflow reports need their own bounded counters; enqueueing another gap event
into a full queue cannot guarantee the loss is recorded.

Queue acceptance, write completion and durable commit are distinct. Recommend
batched fsync off the inference path, with an explicit maximum flush interval and
queue/byte caps. On crash, queued and unsynced data may be lost; the amount is not
necessarily bounded by the flush interval alone because storage can stall. Mark
an unclean source interval incomplete on recovery and verify committed chunks.
A complete but unacknowledged tail has commit uncertainty. If exact no-loss capture
is later required, fail-open nonblocking capture cannot promise it: that is a
new product tradeoff requiring blocking or an independently durable source.

Use append-only observation history as a semantic rule while content is retained,
not as a promise of physically undeletable storage. Store sensitive payloads in
separate scoped objects, with revisions/tombstones in a minimal non-content index.
Retention/deletion removes payloads, derivatives and registered exports, invalidates
manifests, and later compacts metadata where identifying information remains.
Hash values themselves may be sensitive linkage and need access/retention policy.
Encryption/key deletion can assist where appropriately scoped, but must include
backups and does not prove deletion from every medium. Backup TTL, recovery
reapplication of tombstones, shared-object references, legal holds and deletion
receipts must be designed before real ingestion. Do not copy the accounting
journal's “retain all confirmed events” rule into this archive.

## Smallest reviewable work packages

| Package | Concrete result and acceptance | Dependency / approval boundary |
| --- | --- | --- |
| D0 — contract decision | Decide first capture scope, source boundaries, retention/loss tolerance and use permissions; retain the explicit policy delta against Inspector | Current Phase 0; documentation only |
| D1 — offline schema prototype | Synthetic event/artifact/rights fixtures and a causal viewer/export sketch; show denied tool, client-asserted result, retry, branch, correction, gap and revocation | Separate implementation approval; no collector, genuine data or runtime/client changes |
| D2 — permission/review pipeline | Offline versioned sanitation, review records, dependency closure, split manifests and exclusion report using synthetic fixtures | D1; no training or external curator by default |
| D3 — archive capture experiment | One explicitly selected source; capped memory/disk; independent failure injection; prove existing journal start/terminal failures still close inference and archive failures do not | Fresh explicit capture/runtime approval; source and retention decisions complete |
| D4 — reviewed pilot corpus | Small explicitly selected and permitted material; review gaps/rights/sanitation; deletion/restore drill; reproducible training/eval export | Separate genuine-ingest and purpose approval; no blanket archive migration |
| D5 — model experiment | Selected Qwen/template, locked evaluation, run registry and measurable baseline comparison | Separate training approval and sufficient eligible independent groups |

D1/D2 are intentionally offline: causality and admission errors should be found
before capture is connected. A production viewer, universal client patch,
automatic branch grading and automatic training are unnecessary for the first
reviewable result.

## Decisions still open

1. **First source and fidelity:** proxy body boundaries are easiest for API
   comparison, while harness/executor events are required to establish executed
   tool outcomes. Recommend one proxy source first, explicitly incomplete about
   execution, then a separately scoped executor source. An offline importer is a
   possible first experiment only with explicit ingestion approval.
2. **Original retention:** restricted short-lived originals plus reviewed
   derivatives support debugging; sanitation before persistence reduces retained
   sensitive material but forfeits exact original reproduction. Do not silently
   pick both claims. Numeric TTLs/storage caps remain unchosen.
3. **Loss contract:** recommend bounded best-effort capture with visible gaps.
   Guaranteed durable complete conversations conflict with fail-open nonblocking
   inference. Batched fsync versus per-object fsync changes latency/storage work,
   not whether enqueue equals durable retention.
4. **Review workflow:** local manual review first versus explicitly permitted AI
   curator; later choice needs cost/provider/input permissions. Automatic quality
   suggestions remain proposals until scoped human verification.
5. **Training objective:** imitation of verified successes, repair examples or
   preference learning require different selection and loss rules. Preserve the
   source graph now; choose a first objective after eligible material is known.
6. **Retention/deletion jurisdiction and downstream obligations:** determine
   operational policy before D4; append-only history cannot be used to refuse
   a required deletion or conceal that released weights are hard to retract.

## Evidence and limits

Read against the current working tree, which contains unrelated WIP; no claim
that these files match the installed binary. Reviewed: repository `AGENTS.md`,
`.architecture-sparring/config.json`, its index and relevant retry/fallback note,
[observability contracts](../observability-contracts.md),
[shared contracts](../service-contracts.md),
[Live Inspector content contract](../contracts/live-inspector.md),
[journal segments](../journal-segments.md),
[Core DTOs](../../src/Lain.Core/ObservabilityContracts.cs),
[journal append](../../proxy/adapter/internal/lain/journal.go),
[start/completion integration](../../proxy/adapter/internal/lain/integration.go)
and [proxy integration contract](../../proxy/README.md). These sources establish
present architecture boundaries, not new capture consent or current provider
training rights. No source/runtime/client files or real trace data were changed
or ingested. No tests were run for this documentation-only proposal.
