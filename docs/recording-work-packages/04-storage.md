# REC-04 — recording source storage, recovery and retention

30 October 2026 · **Work-package plan only. No implementation, tests or runtime
changes are authorized by this document.** The current authority is
[training-logging-plan.md](../training-logging-plan.md). Shared contracts and
integration ownership belong to [REC-00](00-contracts.md); package ordering belongs
to the [package overview](README.md). Earlier mandatory archive-encryption
recommendations are superseded: this package uses private, unencrypted local
storage, with no keyring, Vault, key management or cloud sink.

## Outcome and exclusive ownership

After explicit recording opt-in, persist available permitted observations across
all models used through the enabled capture scope. Recording survives UI closure
and service restarts. Human inspection and skill/prompt run comparisons are the
first consumers. Training permission `unknown_not_approved` does not prevent
storage or inspection; a specific capture/retention exclusion still applies.

Proposed exclusive source ownership: `src/Lain.Host/Recording/Storage/**`.
REC-04 owns the source journal, payload files, storage manifests, recovery,
retention, archive filesystem protections and **all new recording SQL migrations,
including REC-05's index tables**. Do not modify the existing Usage schema,
journal, acknowledgement gates or migration chain. New recording SQL migrations
live under this package's subtree; the integrator wires registration through
REC-00. REC-05 requests schema changes through REC-00 and does not add migrations.

Shared DTOs/interfaces, capture configuration, API routes and common fixture
contracts are owned by REC-00. SQLite projection code belongs to REC-05. File
paths, return shapes and type names below describe intended responsibilities;
they are not permission to create shared definitions outside the owned subtree.

## Dependencies and contracts to freeze first

REC-04 can proceed in parallel with observer/normalizer/projection packages after
REC-00 freezes:

- Versioned observation envelope, source-instance/sequence identity, request and
  attempt references, observation boundaries, partial/omission/gap states and
  bounded payload handles. No arbitrary contexts, headers or auth objects.
- The validated post-exclusion input boundary: secret exclusion runs before
  persistence, and a failed exclusion drops content with an explicit omission.
  Original content means retained observed content after declared exclusions;
  it does not imply an untouched full HTTP wire capture.
- Nonblocking ingestion result versus durable commit receipt, replay/checkpoint
  cursor and generation, source health, hard-stop generation, and payload reader
  contracts. Payload acceptance never equals durability.
- REC-05 table/index requests: unique source-event key, projection checkpoint,
  projection/normalizer versions, causal entities/evidence, run/variant bindings,
  deletion visibility and paged-query indexes. Agree a schema before independent
  workers build incompatible index layouts.
- Retention/deletion unit, logical deletion visibility, physical purge completion,
  cursor reset after compaction, backup scope and compatibility behavior.

These are integration prerequisites. Concrete compression settings, file naming,
SQLite index implementation and bounded frame sizes are routine owner choices
within the agreed budgets, recorded with their reasons.

## Tasks

1. Define one authoritative retained source stream with independently validated,
   versioned records/segments and an exclusively owned writer. Store large
   content in scoped payload files; records carry references, lengths, digest,
   media/encoding, boundary and completeness. Preserve every logical occurrence,
   even when the same full context is sent again.
2. Use bounded independent compression units with checksums and decoding/output
   limits. Checksums detect accidental corruption; they do not establish hostile
   tamper resistance. Keep unsupported/new fields in the retained source contract
   where safe, without silently promoting an unknown schema into a known event.
   Cross-record CAS/dedup is deferred unless REC-00 explicitly adds its object,
   reference and deletion contract. Compression alone does not require CAS.
3. Implement blob-before-reference durability and journal-before-index ordering.
   Publish a committed watermark only after source records and required payloads
   are durable. Provide bounded replay and payload reads to REC-05; readers never
   observe a reference as committed while its required object is absent.
4. Recover the last valid committed source generation, verify frames and payload
   references, isolate uncertain tail material, and report damage. A valid but
   previously unacknowledged tail is commit-uncertain, not proof of prior receipt.
   Never reinterpret a sealed interior corruption as a harmless torn append.
5. Create separate recording SQLite migrations from the REC-00-approved schema.
   Make the index disposable/rebuildable; preserve unsupported newer versions
   rather than silently down-migrating. Back up or rebuild the recording index
   as the documented migration requires, independently of Usage.
6. Implement bounded disk/working-space accounting and retention, including
   payloads, source segments, SQLite/WAL/SHM, temporary compression/compaction
   output, retained recovery tails and any locally managed backups. Reserve
   headroom on the shared volume and stop capture before archive exhaustion.
7. Implement explicit retention/source deletion with REC-05 invalidation and a
   local restore policy. Purge affected payloads and derived material, respecting
   other live references. Make progress/failure observable; logical hiding is
   not reported as completed physical removal.
8. Document private local archive layout, compatibility, restore/recovery steps,
   inspection limitations and metrics that contain no content or secret values.

## Crash and replay ordering

For a record referencing a new payload, the intended order is: create a private
payload temporary file exclusively; write/validate/compress within limits;
synchronize it; atomically publish the payload and synchronize its directory;
append the complete journal frame; synchronize the source journal; expose its
committed cursor; let REC-05 commit projection rows and checkpoint together.
Metadata-only events have no payload step. Rotation must publish a recoverable
manifest without losing the prior committed stream. Required fsyncs happen on
the independent writer, never on the inference callback.

| Interruption | Required outcome |
| --- | --- |
| Before payload publication | No committed event; private incomplete temporary file is quarantined/removed by the documented recovery rule |
| Payload published, journal absent | Orphan object cannot become an invented observation; bounded reachability cleanup applies |
| Torn active journal tail | Recover verified committed prefix; retain/report uncertain tail and incomplete capture interval |
| Journal durable, SQLite absent/stale | Replay from the committed source; one logical row per source-event identity |
| SQLite transaction interrupted | Event rows and checkpoint roll back together or commit together; replay remains idempotent |
| Same event identity, different bytes | Integrity/source conflict, never silent replacement or a second counted observation |
| Missing payload or sealed interior corruption | Affected interval explicitly unavailable; no fabricated content or false complete state |

Expose the durable source boundary separately from the highest volatile/enqueued
observation. A crashed Core/Host can lose queued content and exact drop counts;
mark an unknown loss interval rather than asserting zero loss.

## Retention and deletion contract

Append-only means observations are not edited while retained. It does not forbid
removal. REC-00 must select retention units and live-delete cursor behavior before
implementation. Prefer whole recording units where practical; arbitrary per-event
purge may require survivor compaction and additional working space.

Deletion first makes affected records unavailable to new reads/projections through
an authoritative content-free control state, then coordinates purge and index
cleanup. Physical removal of source metadata requires a new retained generation
or equivalent agreed source-store mechanism. Preserve surviving identities and
record bytes where their contract requires it. Publish the new generation
atomically, invalidate old cursors explicitly, and never fall back to an old
generation that resurrects deleted content. A crash at each publication/purge
boundary must have a documented resumable outcome. Do not report completed purge
while old payloads, index text/WAL or managed recovery files still retain it.

A future shared payload/CAS object remains until no retained occurrence references
it; deleting one repeated message cannot delete another run's content. Conversely,
dedup cannot retain an otherwise-deleted object forever through stale references.
Restore reapplies later deletion decisions before content becomes viewable; scope
and retention of local backups must be explicit. No automatic historical import,
offsite backup or indefinite retention is introduced.

## Permissions, bounds and operating behavior

Use user-owned `0700` directories and `0600` files on Linux, including database,
WAL/SHM, lock, manifest, temporary, recovery and exported maintenance files. Set
permissions explicitly and verify results; do not rely only on inherited umask.
Protect against symlink traversal, arbitrary paths in recorded data, unsafe file
types and a second writer. Define platform equivalents through REC-00 rather
than claiming Linux mode bits protect every platform. There is no encryption
promise: other processes with the same user privileges may read retained data.

Do not persist Authorization/Cookies/API keys/OAuth tokens/passwords/private keys
or known secret files. No raw fallback after redaction failure. No body snippets
in normal logs, metrics, exceptions or recovery diagnostics. Capture remains off
on new installations; storage creation does not enable it. A hard stop denies
new observations atomically and discards pending uncommitted content under the
REC-00 generation contract; retained committed data follows retention.

Bound queue count/bytes, payload/frame sizes, compression expansion, active files,
open readers, cleanup work, replay batches and compaction working space. Archive
failure degrades/stops capture while inference continues. Never poison or bypass
the existing fail-closed Usage journal. Separate directories do not protect against
a globally full filesystem; report that baseline limitation honestly.

Content seven days / metadata thirty days, a total five-GiB budget and a sixteen-
MiB experimental queue are current **recommendations only**, not approved defaults.
Do not embed those values as accepted settings. REC-00 settles configurable bounds
and measurements before acceptance claims.

## Acceptance and handoff

Planned synthetic acceptance, not executed by preparing this package:

- Restart and replay retain exact committed occurrences, boundary/completeness
  flags and all known source references. UI absence does not stop recording.
- Crash injection at payload, journal, rotation and projection boundaries gives
  the outcomes above; duplicate replay does not duplicate counts or rows.
- Corrupt sealed bytes, truncated tails, absent payloads, conflicting duplicate
  IDs and unsupported schema versions remain explicit and recoverable within the
  stated contract; originals are not silently overwritten.
- Slow/full/failing archive storage, queue pressure, oversize content and cleanup
  pressure stay bounded and do not newly fail inference or weaken Usage gates.
- Delete one of two identical occurrences; delete their final reference; delete
  during projection/read/compaction; crash and restore after deletion. Survivors
  remain readable, deleted material does not reappear, pending purge is visible.
- Canary secrets are absent from source files, payloads, index/temp/WAL and ordinary
  logs. Permission and symlink/race cases cover every generated file class.
- Recording migrations/rebuilds preserve source history and do not touch Usage,
  accounts, routes, profiles, credentials or pending runtime revisions.

Return owned paths/diff, storage and migration versions, actual evidence once
implementation is authorized, crash matrix, quota/retention accounting, restore
procedure, compatibility/rollback limits and requests for REC-00. No runtime
activation, installation, genuine transcript import or test execution belongs to
this planning assignment.
