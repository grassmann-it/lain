# Core stabilization evidence and maintenance contract

These checks exercise the independent adapter with the two pinned upstream
patches. They use synthetic data and do not certify a provider or an installed
Core. No upstream source fork or additional patch is introduced.

## Admission and owning Host lifetime

Management bearer authentication is required for all three operations:

| Operation | Result |
| --- | --- |
| `POST /lain/drain` | Atomically closes new inference admission; returns `{"draining":true,"activeRequests":N}`. |
| `GET /lain/activity` | Returns the same two fields with the current drain state; `Cache-Control: no-store`. |
| `DELETE /lain/drain` | Reopens admission and returns its snapshot. |

The counter covers the entire HTTP handler, including retries and delivery of
the final stream tail. A finished executor alone does not decrement it. The
Host must publish drain, wait for zero, then stop the Core. If its bounded wait
expires, it removes drain and leaves the old runtime usable. New inference
requests during drain receive HTTP 503, code `runtime_draining`, and
`Retry-After: 1`. Management and the models list remain available. A signal is
not a graceful-stream guarantee.

The launcher flag `--parent-stdin` binds a child runtime to the owning Host's
private input pipe. EOF or a read failure closes admission then cancels service
execution. The Host must hold redirected stdin open throughout ownership.
Standalone launch retains its previous behavior unless this flag is selected.
Unit tests cover pipe lifetime; orphan cleanup under an actual Host crash still
requires the integrator's separate process qualification.

## Qualified tool boundaries

Only exact bound provider, upstream model, and actual protocol select a rule.
The additive binding field is `upstreamProtocol` (`openai` or `responses` for
these rules), checked against the credential's actual transport attribute.
Public aliases and client version headers do not select or bypass constraints.

Native protocol evidence comes from the selected executor's `RequestToFormat`;
the pinned native Claude/Codex executors have fixed registered implementations
and do not expose that optional method. Their exact executor identities provide
the corresponding native format. A legacy compatibility configuration without
`upstream_protocol` defaults to Chat only when its synthesized `provider_key`
matches the actual selected executor. Logical account/provider labels never
substitute for protocol evidence.

* `xiaomi-mimo`, `mimo-v2.6-pro`/`mimo-v2.6-flash`, Responses-to-Chat: native
  web search cannot traverse the qualified local translator. This is not a
  generic claim about MiMo provider capability or native Responses routes.
* `commandcode`, `stealth/glyph-cluster:free`, Chat transport: namespace and
  custom tools are rejected before translator normalization and again at send.
* `commandcode`, `reflection/beam:free`, Chat transport: at most 128 actual
  transmitted **top-level** tools. Original-array estimates and nested members
  do not become the transmitted cardinality. No tools are truncated.

The middleware retains original controls for the lifetime of the request. The
executor creates the durable account/request/attempt attribution before a new
compatibility preflight can fail. The final HTTP guard checks actual body,
model, protocol path, and tool controls before the delegated transport. Failed
attempts carry only explicit `unsupported_tool` or `tool_limit` categories.
The optional `errorCategory` field is omitted when absent; arbitrary status
codes/messages are not classified. Function names and function-schema data are
not interpreted as native tool types. No route or paid fallback is introduced.

The source matrix belongs to `tests/model-compatibility/fixtures`; its mirror
under adapter `testdata` is necessary because private prepared Go modules are
standalone. Run `python3 tests/core-stabilization/check-shared-fixture.py` after
integration to detect drift. `LAIN_COMPATIBILITY_FIXTURE` can explicitly select
the shared source for adapter tests.

## ST-04 bounded measurement and chosen compatible change

`measurements.json` records actual 1,000/10,000-event unit measurements. The
synthetic files occupy 654,679/6,576,682 bytes. At 10,000 events the previous
index held 10,000 entries with 98,304 bytes allocated for offsets; the sparse
index holds 40 entries with 512 bytes allocated. Total heap deltas contain GC
and cache noise and are not treated as a production RAM estimate.

Sparse checkpoints occur every 256 events. A page reads at most 255 preceding
records before its requested range. The file layout, source sidecar, sequence,
event IDs and cursor remain unchanged, and legacy readers can read every newer
record. Startup continues validating the complete file: the single bounded
runs measured roughly 34 ms before and 43 ms after at 10,000 events. This does
not establish either a startup speedup or a sustained-load regression.

Authenticated `GET /lain/journal/storage` reports `sourceId`, `events`, `bytes`
(the verified published prefix), `indexEntries`, `indexCapacityBytes`,
`indexStride`, `healthy`, and, when available, `onDiskBytes` and
`filesystemAvailableBytes`. The physical size can exceed the published prefix
after a failed write; health remains faulted until offline investigation.

### Explicit offline inspection and recovered copy

The owning Core must be stopped. Both commands require its exclusive inode
lock and never access providers or require runtime credentials:

```sh
lain-proxy journal-inspect --journal /absolute/events.jsonl
lain-proxy journal-recover-copy --journal /absolute/events.jsonl \
  --destination /absolute/new-recovery-directory
```

Inspection reports the source, verified event count/prefix bytes, original SHA-256,
uncertain tail bytes, and commitment uncertainty. Recovery writes a **new** private
directory containing exact verified-prefix `events.jsonl`, original
`events.jsonl.source`, and `inspection.json`; it publishes that directory with a
no-replace rename after file/directory synchronization. Existing destinations
are refused. The original, including torn bytes and source identity, remains
unchanged. Recovery does not automatically activate the copy.

A final record lacking its newline is uncertain even when its JSON is complete;
it remains in the original and is excluded from the copy. Complete malformed
records, source mismatch, or sequence gaps require investigation and fail
closed. On-disk complete records are replayable, but their presence alone cannot
prove whether a crashed writer acknowledged fsync to its caller. This limitation
is explicit rather than declaring ambiguous usage committed or lost.

Adoption requires a matching Host/SQLite state: never reset an existing collector
cursor or replay identity to hide a mismatch. A copied journal alone is **not** a
consistent backup of configuration, credential generations, journal and SQLite.
That complete backup/restore transaction remains a separate pending ST-04 stage.

### Segmentation and archive decision

Destructive live segmentation is deferred: old binaries only understand the flat
journal and would silently miss removed history. The compatible first step is
the sparse in-memory index plus explicit sealed whole-journal copies. No source
events are automatically removed. Full segmentation needs a versioned segment
manifest, contiguous global sequence/cursor lookup across sealed boundaries,
hash-checked segment publication, matching SQLite snapshot/replay guarantees,
and an explicit legacy-flat export before rollback. It also needs a separately
agreed retention policy before any deletion. These are not claimed complete by
the bounded memory improvement.

## Reproduction

Prepare the module using the repository's verified `scripts/prepare-core.py`
after the integrator refreshes its source manifest. In the prepared adapter:

```sh
GOTOOLCHAIN=local GOWORK=off go test ./internal/... ./cmd/lain-proxy -count=1
LAIN_JOURNAL_MEASURE=1 GOTOOLCHAIN=local GOWORK=off \
  go test ./internal/lain -run TestJournalBoundedGrowthMeasurement -count=1 -v
```

The tests cover admission, pipeline attribution, original/final controls, sparse
cursor boundaries and legacy bytes, unchanged corrupt originals, fresh recovery
destinations, source/sequence failures and writer exclusion. Existing sticky
fsync/disk-full injection tests remain. Real full-disk faults, abrupt process
death, complete backup/restore and sustained history growth require separate
runtime qualification before marking all of ST-04 complete.

The separate actual-process harness is prepared for the integrator's serialized
runtime slot. It accepts only an explicitly supplied freshly built Core:

```sh
python3 tests/core-stabilization/process_probe.py \
  --core /absolute/private-build/lain-proxy \
  --report /absolute/private-evidence/core-stabilization-process.json
```

Its assertions cover gated SSE/drain/reopen, original and final tool constraints,
native executor protocol scope, actual stdin EOF, abrupt synthetic owner death,
legacy-format checkpoint replay, startup refusal of torn tails and adoption of
a recovered copy into a temporary fixture. Only a passing report bound to the
binary and source-manifest hashes counts as executed process evidence.
