# Private local recording source, frame version 1 / control version 2

`RecordingSourceStore(directory, IRecordingPolicy, RecordingStoreOptions?, TimeProvider?)`
implements `IRecordingStore` and `IAsyncDisposable`. Construction never enables capture.
A new store initializes lazily on the first valid commit. Construction, health,
reads and clear on a disabled installation without an archive create no files.
Existing-archive recovery is one owned background task. The constructor acquires
the fresh source gate without blocking before starting it; health immediately
returns `recovering` until it completes. Reads, maintenance and the bounded
writer wait behind the same gate, so no unverified prefix is exposed. Existing
archive/open failures become fixed health codes rather than a Host startup
exception; a second writer is refused through `archive_unavailable`.
Only `SanitizedObservation` crosses this boundary. Redaction states must match the
current policy sanitizer; failed/omitted bodies cannot carry content. The writer
rechecks revision/stop generation after compression and before source publication.
The already admitted bounded write can finish at a hard-stop boundary. Enqueue
acceptance is volatile; only `RecordingCommitResult.Committed` is a durable receipt.
Cancellation stops waiting and does not undo an admitted commit.

The implementation currently supports Linux x64. Files are opened with
`O_NOFOLLOW|O_NONBLOCK`, checked as regular and owned by the current user using the
opened descriptor, then set to 0600. Directories are user-owned 0700. A nonblocking
exclusive `flock` prevents another writer. Paths are generated internally and
ancestors are checked for symbolic links. These protections do not defend against
a malicious process with the same user privileges replacing ancestor directories;
such a process can already read this unencrypted archive. Other platforms fail
closed rather than claiming equivalent permission semantics.

## Layout and commit ordering

- `control.json`: content-free authoritative generation, monotonic high watermark,
  replay invalidation and deletion tombstones. Atomically replaced and directory synced.
- `<generation UUID>/journal-<first cursor>.rec`: append-only frames. Each frame is
  REC1 magic, little-endian compressed length, SHA-256 of compressed bytes, and
  independent Brotli-compressed JSON metadata. No retained body is embedded here.
- `payloads/<cursor>.br`: one independent Brotli body per observed occurrence.
  Equal histories remain different occurrences and files; there is no CAS contract.
- `recovery/*.tail`: private retained uncertain incomplete append material.
- `writer.lock`: private exclusive-writer lock.
- `projection.db` and its SQLite sidecars: optional disposable index, owned by its
  projection consumer. No Usage files, migration registration or accounting gates change.

Each payload is compressed in bounded memory before its first private write,
flushed to disk, atomically renamed and its directory synced. The journal frame
is then appended and flushed to disk. Only afterwards is the cursor returned.
The control watermark may lag append durability on crash; verified source replay
repairs it. Rotating closes/flushed the prior segment and directory-syncs a new
segment before publishing its first frame. No provider callback performs this I/O.

The queue is bounded by count and UTF-8 body bytes plus a metadata allowance.
Metadata is serialized only on the writer and checked against the record bound.
An archive may retain at most `MaximumRecords` descriptors/identity keys. Replay
and payload hydration are capped by record size and `ReplayBatchSize`. The current policy's record, queue-byte, total-disk and retention limits are read
at admission, commit and maintenance rather than frozen at construction. Each is
bounded by its constructor ceiling. Existing source decoding continues to use the
constructor record ceiling, so a lower policy limit cannot turn valid retained
history into an apparent corrupt frame. Queue admission counts UTF-8 body bytes
plus a conservative 4096-byte metadata allowance; the writer checks the actual
serialized observation against the record limit.

Constructor defaults remain configurable experimental choices: 256 queued
records/16 MiB, 16 MiB records, 100,000 retained records, 5 GiB total archive,
64 MiB shared-volume headroom, 16 MiB segments and 100-record replay pages.
`RecordingStoreOptions.SuggestedProductionOptions(externalReservedBytes)` raises
only the hard ceilings to the policy service's supported range: 64 MiB records,
1 GiB queue, 1 TiB disk, and 3650-day retention. It does not enlarge the policy's
current default limits or enable capture.

`ExternalReservedBytes` defaults to zero. The source budget is
`max(0, min(policy.DiskBytes, options.MaximumDiskBytes) - ExternalReservedBytes)`.
It reserves part of the total recording budget for derived files kept outside
this source directory. For a 128 MiB index plus rollback journal and a private
1 MiB label file, the Host can reserve 261 MiB (260 MiB index workspace + 1 MiB
labels). The reservation is a conservative bound, not an additional allocation
or a report of actual external file sizes. Invalid negative reservations are
rejected at construction. Disk accounting includes every file
under this directory, including payloads, SQLite/WAL/SHM, recovery, temporary
and old generations. Compression/compaction reserve workspace before writes;
quota refusal returns a capture error. External writes to the same volume can
still exhaust shared space and affect baseline Usage.

## Recovery, inspection and deletion

On startup the entire bounded source is validated before committed reads. A
partial header/body at the final active tail is copied to a private synced
recovery file and the source is truncated to its verified prefix. A checksum
failure in a complete frame or an incomplete sealed segment is corruption, even
at the tail. The original damaged material is preserved and reads/commits stop
with `source_corrupt`; a missing/corrupt payload has the same explicit failure.
Replay after restart reports `recovered_commit_uncertain`: valid source bytes
cannot prove whether a prior caller received its commit receipt. Queue loss has
no exact recoverable count. Unsupported control versions are rejected intact.
A missing control file over an existing archive is rejected, never reinitialized.

`PurgeCaptureAsync` uses whole capture IDs as the deletion unit. Tombstones are
flushed before new reads can return deleted observations. Survivors are copied
into a synced new generation; control atomically names it; old generations,
unreferenced payloads, recovery tails, and the disposable projection are removed.
`PurgePending` stays authoritative until all removals complete. Restart resumes
pending purge and never falls back to a deleted generation. `ClearAsync` has an
additional durable clear flag so it can erase even a damaged source and resume
a crash at any deletion boundary. Clear also rejects older enqueued work.

The projection consumer must close its database before purge. Rebuild/query code
must use the source's logical deletion visibility and must never open an old
index as authority. Any externally held deleted file handles are outside the
archive's physical-removal promise; close readers before reporting purge to a user.
Survivors receive a new monotonic cursor range while preserving source identity,
observation bytes and payload identity. Old nonzero cursors up to the deletion
watermark get `cursor_invalidated` and must restart from zero. Source identity
conflicts are never overwritten. Capture deletion decisions use fixed 64-character
SHA-256 tombstones, bounded by the smaller of MaximumRecords and 100,000. They
remain durable after physical purge; no reclamation silently re-enables replay.
The 8 MiB control limit accommodates all 100,000 hashes and the small manifest.
A new deletion beyond capacity is rejected before changing visible/control state
with `recording_deletion_capacity`; capture of new occurrences stops with
`deletion_capacity` until explicit clear. Existing committed repeats and reads
remain available. Exhaustion does not itself set `purge_pending`.

`ApplyRetentionAsync` must be called by the Host maintenance worker (and is safe
to invoke repeatedly). A whole capture expires when any retained body is older
than `ContentRetentionDays`, or any metadata exceeds `MetadataRetentionDays`.
Current policy values are bounded by the constructor's retention ceilings; defaults
are seven and thirty days. This can delete newer records
in that same capture; the whole-capture deletion unit is intentional. Automatic
backup/import/offsite restore is absent. For manual restore: stop/disable capture,
close source and index, restore only a compatible private archive, and preserve
and reapply the newest control/deletion decisions *before* serving reads. A stale
archive without the later tombstones cannot prove that restoration is safe and
must not be exposed as current. Never restore Usage with the recording archive.

`Migrations/001_recording_projection.sql` and `RecordingSchema.Sql` provide schema
version 1 for a later SQLite projection. The current source works independently.
A future projection migration must check `user_version`, refuse newer versions
unchanged, and rebuild a disposable index from the source. Do not attach this
schema to the Usage migration chain. Checksums detect accidental corruption,
not adversarial authenticity. Normal health/error codes contain no body snippets.

## Resource accounting and replay work

Commit and health do not recursively walk the archive. A tracked file-size map
is populated during recovery, then updated for each owned append, atomic
replacement, temporary file, truncation and deletion. An atomic replacement
reserves the complete new temporary file alongside its existing destination.
Failed writes retain conservative accounting and trigger a cold reconciliation;
quota checks reject writes while accounting is invalid. The four optional
`projection.db`/WAL/SHM/journal paths are sampled directly with constant work,
so an in-directory SQLite consumer remains counted. Mount discovery is cached;
shared-volume free space is still sampled before writes.

`ApplyRetentionAsync` performs the scheduled full reconciliation for externally
added/changed archive files. It must remain background maintenance, never part
of an inference or UI callback. Recovery and maintenance scans are bounded by
the configured retained-record/disk archive bounds. Existing-archive recovery
runs on its own background task, keeping constructor/Host startup and recovery
health polling responsive. A large archive still takes time to validate; reads
and commits remain unavailable until full validation finishes. Scheduled
maintenance reconciliation remains synchronous background maintenance.

Visible records have a cached ordered reference list, and capture tombstones
use a hash set. Health count/first cursor are constant work. Replay binary
searches that visible list, then opens/decompresses only the requested page,
checking its aggregate byte budget before the next payload is hydrated. The
extra list and tracked-file map are bounded by managed archive file/record
counts. `Diagnostics` exposes content-free counters (`DiskReconciliations`,
`DirectoryEntriesScanned`, `ReplaySearchSteps`, `PayloadReads`) without doing I/O.
Synthetic resource tests assert repeated health performs no scan/hydration,
commits avoid rescanning, late-cursor reads seek logarithmically, and accounting
matches actual retained file lengths after maintenance, purge and clear.

Recovery cancellation is checked between filesystem entries, journal frames,
compaction records, and 8192-byte decompression chunks. `DisposeAsync` cancels
recovery and observes recovery/writer/cleanup failures. It waits up to five
seconds for cleanup; a filesystem syscall itself cannot safely be force-aborted,
so an overlong operation retains owned background cleanup rather than turning
shutdown into an unlimited wait or closing a descriptor still being used.
Queued uncommitted work is rejected if disposal cancels unfinished recovery.
The internal pre-recovery delay hook exists only for deterministic startup/
cancellation qualification and is absent from public construction.

Control version 2 replaces variable-length raw capture-ID tombstones with fixed
hashes. A version-1 control is read and migrated before further control writes;
its deletion decisions are preserved. Frame and payload formats stay at version 1.
Older readers reject version 2 rather than ignore a deletion field and allow
resurrection. Rollback therefore requires a compatible reader; never replace the
latest control file with an older backup. The 8 MiB input bound also applies to
legacy controls, and unsupported newer control versions remain unchanged.

Compaction reserves the exact encoded remapped metadata frame lengths in a
bounded first pass, plus the next control publication's temporary file and
64 KiB working headroom. Existing compressed payloads remain in place and are
not charged again at their uncompressed OriginalBytes size. The second pass
encodes/writes one frame at a time, avoiding an archive-sized in-memory buffer.
