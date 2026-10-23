# Usage persistence checks

Run `dotnet test tests/usage/Lain.Usage.Tests.csproj`. The suite uses temporary
SQLite databases and an in-memory HTTP handler; it does not contact a provider,
the existing proxy, or its queue. One test starts this test assembly as a helper
process, commits a dispatch, stages uncommitted data and cursor changes, and kills
the helper before reopening the database. No subprocess survives test cleanup.

These are synthetic contract tests, not backend/provider qualification.

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

Only sanitized event metadata, token counts, timing, stable identities and
deduplication digests are stored. No credential content, HTTP headers, prompts or
response bodies are persisted by this store.
