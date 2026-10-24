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
Unit tests cover pipe lifetime. The adapter3 process report additionally proves
actual Core shutdown after stdin EOF and abrupt death of a synthetic owning
process, including exit code 0, released listener and reaping. Host application
publication/recovery remains a separate integration contract.

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
fsync/disk-full injection tests remain. Abrupt synthetic Core death and replay
also pass the process regressions. The bounded real filesystem exhaustion
qualification below complements those injections. Complete Host backup/restore
and sustained production growth still require separate qualification before
marking all of ST-04 complete.

The separate actual-process harness runs in the integrator's serialized runtime
slot. It accepts only an explicitly supplied freshly built Core:

```sh
unshare -Urn sh -c 'ip link set lo up && exec python3 \
  tests/core-stabilization/process_probe.py \
  --core /absolute/private-build/lain-proxy \
  --report /absolute/private-evidence/core-stabilization-process.json'
```

Its assertions cover gated SSE/drain/reopen, original and final tool constraints,
native executor protocol scope, actual stdin EOF, abrupt synthetic owner death,
legacy-format checkpoint replay, startup refusal of torn tails and adoption of
a recovered copy into a temporary fixture. Only a passing report bound to the
binary and source-manifest hashes counts as executed process evidence.

The harness refuses a network namespace with non-loopback interfaces. The pinned
upstream SDK starts a public npm client-version check even without that provider's
account; the private namespace gives it no external network route. This preserves
the two existing upstream patches and does not mistake the SDK's incidental
metadata lookup for an authorized live-provider qualification.

## Executed adapter3 qualification

[The checked-in evidence](evidence/adapter3/process.json) belongs to Core SHA-256
`39c09202ca7c38c636cfb949abec3b71c6c9843dd2651cbc8bf44944c13c0629`
and source-manifest SHA-256
`4441b3bfb5caf5be7c5ab954303c25eac7be2bbd4a3833e4a496cd755d257750`.
The fresh source preparation and source verifier pass. There are 66 passing
top-level Go tests (135 test/subtest pass events), with one intentional opt-in
measurement skip. The actual process checks all pass:

| Suite | Passing checks |
| --- | ---: |
| New drain/dispatch/native-protocol/parent/journal scenarios | 6 |
| Existing backend transport, tools, replay and durability regressions | 15 |
| Independent authentication and Core crash/restart | 2 |
| Existing native image-tool exclusion and final override guard | 2 |
| Independent Inspector correlations, tools, cancellation and restart | 15 |

All 40 final process checks ran with loopback as their only network interface.
The parent checks exposed a normal canceled service context being returned as
exit code 1; the own launcher now maps only its expected lifetime cancellation
to success. Rebuilt source and artifact hashes above include that fix. Actual
old-binary rollback and full Host backup/restore remain explicitly unclaimed.

`python3 tests/core-stabilization/check-evidence.py --core /absolute/lain-proxy`
checks that the current manifest, qualification, reports and supplied binary
belong together. Omitting `--core` checks the repository evidence chain only.

## Executed actual Host + Core and filesystem qualification

[The joint evidence](evidence/host-core/qualification.json) selects three actually
passing restored-Gateway cases from the retained raw reports. Every case used
the qualified Core above and the same compiled Host SHA-256
`24f7d47fcf19f674952ea3fd5f57c1c9997c7a70802475462b564dcbb2ec41c8`.
The Host reported build revision `6104a5c`; the initial report's manually supplied
`a890903` label describes the builder-reported product baseline, not the observed
build stamp. All Host DLL, dependency/runtime manifests and `Lain.Core.dll`
hashes are retained and agree across runs.

The cases cover an unusable legacy saved configuration, an interrupted Apply,
and a syntactically valid saved Core configuration with a restored owned grant.
The old synthetic grant and ownership registry remain unchanged in quarantine;
the saved runtime registers no old grant and rejects its old inference alias.
Actual Core login Begin/Poll/Cancel works in the separate management-only runtime
with empty bindings and journal. Active configuration, bindings, proof, journal
and source identity remain byte-identical before explicit Apply. Saving a fresh
synthetic account/route/profile does not activate it; explicit Apply replaces the
bootstrap and publishes the normal ready runtime.

The interrupted case also exercises the actual Host's gated SSE drain and
20-second deadline. The timeout currently returns HTTP 500, preserves the old
Core PID, applied revision and exact configuration/proof bytes, reopens
admission and preserves the admitted stream tail. Explicit retry succeeds.
Normal Host shutdown returns exit 0. Both normal shutdown and abrupt Host
SIGKILL release the listener and leave no Core orphan; the final lifecycle run
also checks unchanged journal/configuration/proof bytes after both exits.

The first run failed an extra assertion requiring Core exit 0 after Host SIGKILL;
its raw failure remains in `initial.json` and is not counted as passing. A scoped
diagnostic repeat recorded Core exit 0; the final repeat recorded SIGPIPE (-13).
Host-owned stdout/stderr pipes are redirected, so abrupt owner death is not
claimed to have identical clean-exit semantics to the standalone stdin-EOF test.
The qualification requires bounded process exit, released listener, and preserved
durable state, and retains the actual observed exit/signal.

[The filesystem report](evidence/host-core/filesystem.json) records actual kernel
ENOSPC (errno 28) on a private **1 MiB tmpfs** in its own mount/network namespaces.
The Core refuses dispatch with HTTP 503 before an upstream attempt, leaves
confirmed history unchanged, and stays faulted after only the filler is removed.
Explicit restart preserves the original source/cursor and resumes append;
an explicitly recovered copy replays identically. Only the bounded fixture is
filled; no host filesystem or existing service is touched.

Reproduce in the serialized runtime slot with explicit retained binaries:

```sh
unshare -Urn sh -c 'ip link set lo up && exec python3 \
  tests/core-stabilization/host_core_probe.py \
  --host /absolute/build/Lain.Host.dll --host-source-commit BUILD_REVISION \
  --core /absolute/private-build/lain-proxy --dotnet /usr/bin/dotnet \
  --report /absolute/private-evidence/host-core.json'
python3 tests/core-stabilization/filesystem_probe.py \
  --core /absolute/private-build/lain-proxy \
  --report /absolute/private-evidence/filesystem.json
```

The filesystem launcher creates its own private user, mount and network
namespaces. The Host harness requires the reference qualification environment
supported by this build, uses temporary state and ephemeral ports, and disables
quota/refresh work for these synthetic fixtures. Both require loopback-only
networking. No OAuth callback/token exchange or live provider is exercised;
the saved grant is synthetic and expired. Full configuration/credentials/journal/
SQLite backup and restore, actual old-binary rollback, production device failures
and sustained growth remain unqualified.

`check-evidence.py` also validates the new raw-report hashes and selected cases.
Pass `--host /absolute/retained/Lain.Host.dll` to check all retained Host build
inputs, in addition to the optional `--core` artifact check.
