# Usage persistence checks

Run `dotnet test tests/usage/Lain.Usage.Tests.csproj`. The suite uses temporary
SQLite databases and an in-memory HTTP handler; it does not contact a provider,
the existing proxy, or its queue. One test starts this test assembly as a helper
process, commits a dispatch, stages uncommitted data and cursor changes, and kills
the helper before reopening the database. No subprocess survives test cleanup.

These are synthetic contract tests, not backend/provider qualification.

For a complete sanitized journal envelope from the isolated core, run
`dotnet run --project tests/usage/Lain.Usage.Tests.csproj -- --ingest-journal envelope.json /tmp/owned-usage-check.db`.
This helper ingests/reopens/replays the unchanged journal ten times and emits
only aggregate counts. It deliberately rejects subset fixtures with sequence gaps.

Observed normal-phase integration on 2026-10-23: the qualification runner's
complete 400-event envelope from core SHA-256
`683c610548b185669c1a638fb27dba6eb239ef7c08f1b84f58f36f44655d82ad`
passed unchanged replay/reopen ten times. Result: cursor 400, complete true,
200 requests, 200 attempts, 0 errors, 2 accounts, 2200 input / 1400 output /
3600 total tokens; cached and reasoning remained null. This confirms the normal
fake-upstream journal-to-SQLite path for that core build. It does not qualify
other scenarios, newer core builds, or real providers; backend qualification
remained open because of a separately observed streaming-order defect.

The same unchanged-envelope replay/reopen check was repeated against schema 2
with the final synthetic qualification build SHA-256
`8edf8abf666e3c15f1a04c20c841a969d35229f09e7de5265755ab704cf9dfc0`:
400 events again produced exactly the counts above, with complete capture.
The source-transition fix passed all 13 tests after a non-incremental rebuild,
including empty and populated replacement journals, v1 migration, transaction
rollback, and process-kill recovery. Real-provider qualification is separate.

## Host integration

The host references `Microsoft.Data.Sqlite` version **10.0.12** and embeds
`Usage/Migrations/*.sql`. Register one `SqliteUsageStore(databasePath)` as both
itself and `IUsageStore`. Register a `JournalUsageCollector` hosted service with
that store, a logger, and an `HttpClient` whose base address is the owned core's
root URL (including trailing slash). The caller configures any management
authentication and timeout. Do not point it at the existing proxy.

The collector probes journal source identity, resumes its durable cursor, and
polls `GET lain/events?after=N&limit=1000`. Events and their cursor share a FULL
synchronous SQLite transaction; there is no destructive acknowledgement call.
The journal must remain immutable, contiguous, and replayable. A source reset,
gap, malformed event, changed duplicate, or collector error cannot advance the
cursor. The background worker marks failed collection as incomplete and retries.

## Query semantics

- Default interval: the last 30 days. Maximum interval: 366 days. Intervals use
  inclusive `From`, exclusive `To`, and attempt start time. Pagination is limited
  to 500 attempts. Account and model groups are limited to 1000 each; truncation
  makes `Complete` false. Filtering and aggregation execute in SQLite.
- Daily buckets use the requested system time-zone identifier, including DST;
  timestamps in filters are explicit instants. Late terminal events update the
  original attempt's day. Terminal model is the actually observed model.
- Requests count distinct source/request identities; attempts count dispatches.
  Failed, canceled, or HTTP >=400 attempts count as errors. A pending dispatch
  remains pending after restart and carries null token quantities.
- Each aggregate token component is null if any included attempt lacks that
  component. Provider totals are never rebuilt by adding cache/reasoning subsets.
  `UnknownUsageAttempts` counts attempts without reported usage or a known total.
  A completely captured terminal event may still have unknown token quantities.
- `Cursor` sums committed per-source cursors. `ObservedAt` is the oldest source's
  last successful ingestion time (Unix epoch before first ingestion). `Complete`
  also requires no backlog, collection fault, or matching pending attempt. It
  means capture coverage at that snapshot, not that provider quantities are known.
- Schema version 2 preserves the active journal source and permanent coverage-gap
  intervals. Replacing its UUID between clean polls records the uncertain interval
  from the previous successful observation to the first observation of the new
  source, in the same transaction as events and cursor. Every overlapping query
  reports incomplete capture, including account/model/status-filtered queries.
  Normal restart and replay with an unchanged UUID create no gap. A v1 database
  migrates without changing usage or cursor; if it already contains multiple
  sources, its untraceable historical transitions are conservatively incomplete.

Only sanitized event metadata, token counts, timing, stable identities and
deduplication digests are stored. No credential content, HTTP headers, prompts or
response bodies are persisted by this store.


The archive qualification also uses the fresh test assembly's
`--collect-journal http://127.0.0.1:<private-port>/ envelope.json /absolute/fixture.db`
mode. It calls the actual Host `JournalUsageCollector` against the synthetic Core
with the fixture-only management token, reopening/polling SQLite three times.
Only explicit private loopback URLs are accepted; product ports are refused.
`tests/core-stabilization/archive_gate.py` coordinates source/cursor/sum comparisons,
real segment files, sparse strides, matching full-tree fixture restore and flat
export. Supplying this mode is not evidence that the gate was executed.
