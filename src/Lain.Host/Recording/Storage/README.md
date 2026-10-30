# Private local recording source, storage version 1

`RecordingSourceStore(directory, IRecordingPolicy, RecordingStoreOptions?, TimeProvider?)`
implements `IRecordingStore` and `IAsyncDisposable`. Construction never enables capture.
A new store initializes lazily on the first valid commit. Construction, health,
reads and clear on a disabled installation without an archive create no files.
Existing-archive open failures become `archive_unavailable` health, never a Host
startup exception; a second writer is refused through that same fixed code.
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
and payload hydration are capped by record size and `ReplayBatchSize`. Defaults
are configurable experimental choices, not performance acceptance results. Store
options default to 256 queued records/16 MiB, 16 MiB records, 100,000 retained
records, 5 GiB total archive, 64 MiB shared-volume headroom, 16 MiB segments and
100-record replay pages. The policy's limits should be passed into the store
options by the integrator when configured. Disk accounting includes every file
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
conflicts are never overwritten. Tombstones remain bounded by MaximumRecords;
exhaustion requires an explicit clear instead of dropping deletion decisions.

`ApplyRetentionAsync` must be called by the Host maintenance worker (and is safe
to invoke repeatedly). A whole capture expires when any retained body is older
than `ContentRetentionDays`, or any metadata exceeds `MetadataRetentionDays`.
Experimental defaults are seven and thirty days. This can delete newer records
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
