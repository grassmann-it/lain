# REC-05 — causal projection, saved-run queries and variant index

30 October 2026 · **Work-package plan only. No implementation, tests or runtime
changes are authorized by this document.** Follow the revised
[recording plan](../training-logging-plan.md), [REC-00 contracts](00-contracts.md)
and [package overview](README.md). Private local unencrypted recording is the
confirmed scope. Human inspection of saved conversations and skill/prompt
comparisons is primary; export, review grading and training are later consumers.

## Outcome and exclusive ownership

Project committed observations into a rebuildable SQLite view of conversations,
branches, turns, requests, attempts, messages, tool requests, supplied results,
workers and labeled comparison runs. Supply bounded read queries for the future
local API/browser. Model changes do not split an evidenced conversation, and
missing relationships remain visible rather than being guessed.

Proposed exclusive source ownership: `src/Lain.Host/Recording/Projection/**`,
including its recording read-query class in that subtree. REC-05 owns causal
projection, SQLite read/write transactions within the approved schema, query
models and bounded payload-resolution orchestration. **No SQL migrations are
owned here.** All new recording migrations, including index tables, belong to
[REC-04](04-storage.md) under `Recording/Storage/**`, requested through REC-00.
Do not modify existing Usage queries/schema or register new public routes.

REC-03 owns pure protocol normalization: incremental SSE/JSON/UTF-8 handling,
protocol roles/items/call fragments, terminal usage extraction and protocol
coverage. REC-05 consumes that versioned output with source provenance; it must
not grow a second protocol parser. Shared public DTOs/interfaces, common IDs,
API registration and fixture shapes are REC-00's responsibility. UI and SDK code
belong to their respective packages.

## Dependencies and parallel work

After REC-00 freezes contracts, start with its synthetic committed-source and
normalizer fixtures. No live recorder is needed for early projection/query work.
Real integration requires REC-03's normalized stream contract and REC-04's
committed replay, payload reader, generation/deletion notifications and schema.

Request through REC-00 before migrations are written:

- Unique source-event identity, source generation/sequence/cursor, observation and
  optional source occurrence time, committed checkpoint and projection versions.
- Scoped entity/external-ID tables, event occurrences, directed causal edges with
  evidence/conflict state, availability/gap/partial markers and payload references.
- Nullable reported usage measures and their exact provenance/basis, separate from
  authoritative existing accounting aggregates.
- Run/comparison/variant manifests and per-field provenance, version/hash evidence,
  manual labels and independently represented capture/retention/training states.
- Keyset-query indexes, atomic projection checkpoint, deletion filtering and
  rebuild-generation publication so partially rebuilt views are not shown as
  current complete state.

REC-04 remains the migration owner even when a needed index is discovered during
query work. Freeze initial schema requests in REC-00; negotiate later changes
there rather than editing another worker's migration or adding a second chain.

## Tasks

1. Define a deterministic causal projection over committed source observations
   plus REC-03 normalization. Preserve source-event/artifact locators and parser
   versions on derived rows; raw-retained versus normalized representations are
   separately addressable. Rebuild never edits source history.
2. Apply batches transactionally: validate source generation and source-event
   identity, upsert occurrences/entities/evidence idempotently, update related
   projections, and advance the committed-source checkpoint in the same SQLite
   transaction. No checkpoint can pass an event that was only queued in RAM.
3. Resolve explicit references when available, including late/out-of-order tool
   results, response continuations, workers and forks. Store unresolved references
   and conflicting assertions without inventing a parent, root or execution.
4. Build run/variant manifests with field-level provenance and optional comparison
   grouping. Manual run labels are useful first-class data, not proof that any
   skill was actually loaded or followed. Persist labels/changes via the REC-00
   authoritative annotation event contract, not SQLite-only writes lost on rebuild.
5. Supply paged query operations for saved-run lists, conversation/branch history,
   lazy worker/request/attempt expansion, content blocks, tool relations, source
   details, capture health and basic variant comparison. Read large content in
   bounded windows through REC-04's payload reader, not whole-archive hydration.
6. Support safe rebuild/version transition, source retention/deletion and index
   invalidation. Expose projection lag/failure/coverage separately from archive
   health, inference completion and task correctness.

## Causal and content rules

| Case | Projection rule |
| --- | --- |
| Explicit conversation, new model | Retain one conversation with distinct requested/reported model facts per attempt |
| Request retry or route failover | One proven logical request with separate attempts and explicit retry/fallback edges; preserve any partial output |
| Tool request without result | Requested/partial/unknown remains visible; no completed execution or successful result |
| Client supplies tool result | Label client-reported/submitted result; match only within proven call-ID scope, never assert local execution |
| Later executor evidence | Add distinct execution instance/outcome with source evidence; never reinterpret every submitted result as executed |
| Same-name parallel calls | Distinct scoped call IDs/occurrences, no join by name, text or time |
| Repeated full history | Preserve each observation occurrence; link to a prior message only with explicit evidence, not digest equality alone |
| Worker/branch relations absent/conflicting | Unassigned or conflicted entity, no fabricated tree/root; late evidence may refine the projection with provenance |
| Compaction/resume/previous-response reference | Preserve boundary/reference, indicate unavailable earlier context; do not reconstruct a complete hidden prefix |
| Source restart or capture gap | Preserve epoch/gap boundary and partial coverage; no text merge across uncertain boundaries |

Keep conversation, branch, turn, request, attempt, message/block, tool invocation
and execution identities distinct. Source sequences order only their source;
wall-clock proximity is not causality. If external IDs collide across clients or
source instances, scope them; do not rewrite original IDs or silently merge rows.

Keep observation boundaries distinct: client ingress, normalized request, provider
request/response and emitted client output are not interchangeable. REC-03's
normalized content is not exact byte-original wire data. Unknown/unsupported
parts remain locatable and visibly unsupported when retained, not silently
omitted from completeness. Redaction/size/pressure omission is distinct from a
protocol parse failure. Do not infer hidden reasoning, credentials or unseen tool
results from adjacent text.

Usage fields remain nullable and source-qualified. Early usage and actual stream
completion can differ; source-reported usage success is not task correctness.
Do not derive vendor cost, rank “best skill” by token totals, or replace existing
Usage accounting with this best-effort archive projection. Aggregates must state
their population, missing values and capture coverage.

## Skill/prompt manifests and comparison queries

Index `comparisonId`, `runId`, `variantId` and available skill ID/revision/hash,
instruction hashes, harness version, requested/reported model, explicit reasoning,
tool-schema hash and starting task/context/repository reference. Unknown stays
null with an evidence/availability state; a field has no invented default source.

Distinguish per-field claims such as user-selected label, configured artifact,
client-reported loaded artifact and actually observed payload. A content hash
identifies available bytes, not that a skill was loaded, followed, or caused a
result. Record such evidence at its actual boundary; don't promote “observed
payload hash” into a loaded-skill claim if the content's origin is unknown.
No recursive scan of skill directories, repository snapshots, prompt files or
other applications is authorized. Only supplied/observed artifacts enter a
manifest. Unknown training rights remain `unknown_not_approved` and **do not
block ordinary local recording, history queries or variant labels**. A specific
retention prohibition still prevents displaying material that should not exist.

Allow side-by-side run discovery and simple reported measures, with differing
model/task/context/harness/budget visible. Exploratory comparisons may contain
uncontrolled differences; do not claim they establish causal skill quality.
No AI judge, automatic grades, synthetic repairs, replay or external evaluation
request is part of this package. Later training/export queries must apply their
own admission checks instead of retroactively gating the inspection browser.

## Query, privacy and resource behavior

Use stable keyset pagination bound to a projection generation and an explicit
read snapshot/high-watermark where needed. Specify reset/stale-cursor responses
through REC-00 when rebuild/retention invalidates a cursor. Filtering may yield
no rows while scan position advances; it must not imply no capture gaps.
Queries expose availability and lag rather than pretending an empty or stale
projection means no source history. Unknown schema/event kinds remain visible
as unsupported coverage where their envelope is safe.

Bound page size, content window bytes, traversal depth, query time/work, pending
unresolved references, active assemblies and in-memory caches. Resolve workers,
attempts and payloads lazily; do not load whole conversations or all branches to
render a list. Late relations can be stored/indexed on disk rather than retained
in an unbounded RAM join map. Use REC-04-provided database/payload boundaries and
coordinate read transactions so they do not indefinitely prevent retention/WAL
cleanup. Cancellation closes readers and content handles.

All endpoints remain authenticated and local through the owning API package.
Archive files are private `0700`/`0600` via REC-04. SQLite is unencrypted, so avoid
unnecessary duplicate content/FTS previews; payload references and bounded derived
fields support browsing. If text indexing is later required, count its data,
WAL/temp and deletion behavior explicitly. Never log payloads, SQL parameter
content, private manifest contents or redaction failures. Treat recorded content
and labels as data, never as policy/instructions to query executors or collectors.

## Recovery, duplicates and deletion acceptance

Planned synthetic acceptance, not executed by preparing this package:

- Shared REC-00 graph covers at least 200 API calls/100 tools, a root plus two
  workers, explicit fork, resume/compaction, same-name parallel tools, retries
  with partial output, model switch and ambiguous/unassigned relationships.
- Rebuild and incremental replay yield equivalent projected identities/relations
  and reported measures. Replay duplicates do not double messages, token fields,
  tool results, variant bindings or comparison counts.
- Same event ID with different content reports conflict; same bytes in different
  occurrences remain distinct. Same external call ID in separate proven scopes
  cannot cross-link results. Late tool/worker references resolve only when evidence
  permits; missing evidence stays unknown after rebuild.
- Crash after source commit/before projection, during row writes and before/after
  checkpoint publication produces consistent rollback/replay. Projection or
  normalizer upgrade rebuilds a separate generation and publishes it coherently.
- Drop/gap/redaction/unsupported data survives rebuild in coverage state. A final
  failure/cancellation cannot become success merely because usage arrived earlier.
- Delete while a read page, late reference, projection batch or rebuild is active.
  New results hide deleted data, old payload resolution reports unavailable, and
  neither replay nor stale index rows resurrect it. Related surviving occurrences
  remain readable; dangling edges are unavailable/unresolved, not guessed joins.
- Variant fixtures distinguish selected/configured/reported/observed from verified
  loaded/followed claims. Missing skill data remains unknown; no file scan fills
  it. Training-unknown runs remain viewable and comparable.
- Long bodies, many workers, delayed consumers and canceled queries remain bounded;
  list pages do not hydrate all payloads. Query latency/RSS and reader/WAL pressure
  are measured when authorized, with actual limits/coverage reported.
- No mutation of source, routing/auth, existing Usage aggregates, client settings
  or runtime revisions arises from reading, filtering, rebuilding or comparing.

Return owned paths/diff, REC-00 schema/index requests, projection version and
rebuild/rollback behavior, query contract/limits, fixture mapping and actual
verification evidence only after implementation is authorized. No code, test
execution, runtime changes or new workers belong to this planning assignment.
