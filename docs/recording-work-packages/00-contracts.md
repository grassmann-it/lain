# REC-00 · Integration contracts and executable shared fixtures

Owner: integrator. Status: prepared plan, not implementation authorization.
Sources: [package overview](README.md), [current plan](../training-logging-plan.md).

## Result

Publish one shared v1 recording contract so policy, observer, parser, store,
projection, API and Desktop can be implemented in parallel without incompatible
DTOs or invented identities. Preserve installed/runtime/source boundaries and
all existing routing/Usage/display guarantees.

## Exclusive future ownership

`src/Lain.Core/RecordingContracts.cs`, any shared
`proxy/adapter/internal/lain/recording_contracts.go`; proposed
`docs/contracts/recording.md`, `docs/recording-event-schema.json`,
`tests/recording-fixtures/**`; build/solution registration; existing Host Program,
Go admission/entry wiring and shared client/UI entry registration changes.
REC-04 exclusively owns all recording SQL migration content. No unrelated WIP.

## Tasks before dependent code

1. Re-read installed Core/Host/provenance and capture boundary code. Freeze a
   branch/worktree based on a verified baseline; don't pull patch5 behavior into
   an installed patch4 update invisibly. Keep the previously fixed Desktop.
2. Freeze these source/semantic/persistence/query seams (names below are proposed):

| Seam | Producer -> consumer | Required semantics |
| --- | --- | --- |
| `SourceObservation` / batch | REC-02 ->06/01 | Schema, sourceInstance+sequence+eventId, UTC/monotonic observation, boundary/protocol/kind, bounded body/metadata and explicit gaps; no auth objects or arbitrary header maps |
| `CapturePolicySnapshot` |01 ->02/06 | Independent recording revision, enabled/mode, all-model default, explicit scope, denial/stop generation; no inference config revision/Apply side effect |
| `SanitizedObservation` |01 ->04 | Retention-approved copy, redaction version/outcome/omission flags; unavailable body explicit. Type cannot masquerade as byte-original after transformation |
| `CommittedObservation` / range |04 ->03/05 | Stable source identity, journal location/hash/version and durable state; enqueue is not commit |
| semantic records |03 ->05 | Role/content parts, tool call/item identity, client-result vs execution evidence, source refs, nullable usage and finish/coverage state |
| projection query page |05 ->06 ->07 | Bounded stable cursor/snapshot semantics, conversation/branch/worker/request/attempt/run manifest; no full session payload on list |
| health/control |01/02/04 ->06 ->07 | enabled/mode/state, queue bytes/count, dropped/unknown loss, disk/retention, source/representation coverage, safe error codes |

3. Keep event/message/attempt/tool scopes distinct. Supplied native IDs carry
   source/evidence; local generated IDs never claim to be provider IDs. Unknown
   model revision/effective reasoning/tokens/billing/loaded skills remain null.
4. Define public local API/client/CLI surface together with REC-06. Proposed
   `/api/v1/recording/*` namespace has recording-only revisions; current profile
   revision and pending Apply remain unchanged. Explicit list/query caps and
   stable cursor error behavior must be executable in fixtures.
5. Establish mutation/stop semantics: atomic admission stop, queued discard,
   treatment of already-started bounded writes, visible stopping/off status and
   restart behavior. Don't promise instant physical erasure or no post-stop
   in-flight commit without qualifying that stronger guarantee.
6. Agree private handoff/source epoch reset, cursor continuation/eviction behavior,
   record size/memory accounting, unsupported-protocol representation and safe
   failure types. No protocol callback may mutate response/headers/auth/selection.
7. Define run/variant manifest with configured/reported/observed evidence and
   optional explicit skill/prompt hashes. No home-wide skill scan or prompt
   injection. Original observations immutable; later labels are derived metadata.

## Shared synthetic fixture pack

Create compact fixtures and expected projections; data fake and marked synthetic:

- >=3 turns; same conversation changes model; full-history resends and ambiguous
  duplicate text; fork with shared prefix; two workers with explicit parent evidence.
- Two same-name tools with different IDs, interleaved argument fragments, later
  client result, rejected/missing result, and unsupported local execution evidence.
- Retry/fallback with partial output, cancel/timeout, missing terminal, zero vs
  unknown usage, reported/effective reasoning mismatch and unknown moving alias.
- Compaction/resume/previous-response, conflicting/missing IDs, lost source epoch,
  dropped interval, oversize body and parser/redaction failure.
- A scalable200-model-call/100-tool-event case with bounded streaming generation;
  no need to write enormous fixture blobs or imply real shell commands ran.
- Two skill variants on an otherwise controlled run manifest; known configured
  revision vs actually observed instruction hash; no fabricated loaded-skill proof.
- Secret canaries for policy/normalizer/storage tests; no actual credentials.

## Ready gate and acceptance

Dependent workers can start real implementations only after shared source/event,
policy, source-commit, semantic and query fixtures compile and expected outcomes
are agreed. They can prepare independent tests/design earlier. Stop on missing
identity provenance, unsafe capture type or hidden encryption/training assumptions.

Return contract version, baseline/source pins, fixture paths/hashes, explicit
unsettled policy/resource numbers and integration seam owners. This is not a
migration, installed acceptance, actual200-call live run or source capture approval.
