# Recording query and causal fixture qualification

```
dotnet run --project tests/recording-projection/Recording.Projection.Tests.csproj --disable-build-servers -p:UseSharedCompilation=false -m:1
```

`RecordingConversationProjection(IRecordingStore, IRecordingNormalizer, limits)`
is a read facade over committed retained source. It persists no second authority
and has no schema migration. `ApplyAsync` is consequently an inert notification;
queries replay the current journal. Restart/rebuild reconstruct the same view and
source deletion is reflected on the next read. The store owner provides a future
SQLite schema; the current implementation deliberately makes no index/checkpoint
transaction claims.

Every query replays pages of 32 source records through a fresh normalizer and
retains bounded summaries or one selected detail page. Defaults cap source work
at 100,000 records, summaries at 16,384, results at 100/page, selected payload at
16 MiB and manifests at 256. Source reading inflates only one store page at a
time, though this first query facade must scan the retained source to discover
all summaries. These limits and O(retained source) list cost are visible initial
limitations; they do not establish Desktop responsiveness at production scale.

Cursors bind source high-watermark, retained-from cursor, record count and query
scope. Any append/deletion invalidates prior cursors with `stale_cursor`.
Concurrent source mutation is checked again before results are exposed. Query
work truncation, retained-prefix gaps, unavailable content and detail byte limits
remain explicit. A future stable-generation SQLite index can relax append
invalidation without changing source identity semantics.

Conversation IDs namespace native conversation IDs by trusted CaptureId. Unknown
native identities produce a source-epoch/request observation group. CaptureId
must be stable across one trusted recording scope, not reassigned to RequestId.
Worker/parent dimensions require the existing curated source evidence. Fork
parent IDs additionally require `parentConversationId` dimension evidence and
`metadata.parent_conversation_id` in the retained body. Conflicting summary
worker/parent evidence becomes unknown. Tool relations use explicit call IDs in
proven conversation/worker scope; multiple candidate requests stay ambiguous,
missing evidence stays unresolved and client results never become executions.

The fixture workload includes 200 calls and 100 tool items, model changes, two
workers, bounded pages, manifests, source deletion and cancellation. It is
synthetic source/query evidence, not a live inference or trusted tool-execution
run. Training-unknown status does not gate browsing; no training/export/replay,
automatic grades, instruction scan or filesystem prompt intervention exists.

Additional checks cover explicit fork-parent resolution, conflict-to-unknown
worker dimensions, cursor filter binding and complete conversation counts when
filtering on one model in a multi-model conversation.

## SQLite production index

`RecordingIndexedProjection(store, normalizer, databasePath, schemaSql, limits)`
is the production implementation. Pass `RecordingSchema.Sql` from the storage
owner, and place the index outside the source archive (for example
`recording/index.sqlite`). It uses the existing recording schema without a
second migration chain. The earlier `RecordingConversationProjection` remains a
source-replay fallback and pure query fixture implementation.

SQLite stores normalized metadata, at most 120 characters of an approved title,
source occurrence/evidence locators, supplied causal edges, summaries and explicit
run manifests. It does not duplicate message text, tool arguments or results.
Lists read only indexed summaries; selected detail pages hydrate only exact source
cursors from the index. A durable snapshot prevents repeated startup/list replay;
new source records are applied transactionally with the projection checkpoint.
Rebuild/retention invalidation clears and recreates the index in one transaction,
using SQLite secure_delete plus VACUUM to remove deleted preview bytes. A source
change during a query/rebuild rejects the snapshot.

The index uses 0600 files/0700 directory, DELETE journals, memory temporary tables,
a 512 MiB page cap and the same source/query work limits. Index quota accounting
is independent from the source archive quota; the integrator must include both
when presenting total recording disk use. Changed conversations use the bounded metadata cache described below; cold cache
misses hydrate indexed metadata only. No live latency/RSS acceptance follows from
these synthetic tests.

The test project accepts `-p:RecordingSchemaSource=/path/to/RecordingSchema.cs`
while integrating separate worktrees; the default is the committed storage-owner
file in this checkout. This only links the authoritative schema for fixtures and
does not copy or modify migration content.

Conversation summaries now use a metadata-only LRU cache capped at
`MaxConversations`. A cold conversation is rehydrated once from indexed metadata
before its new record is inserted. Each later append updates its cached state
and SQLite summary row directly; no accumulated conversation history is rescanned.
Cache entries contain counts, IDs, model/route sets, pending-state metadata and a
bounded title, never message/tool payloads. Eviction reloads only the affected
conversation on its next append. Any aborted transaction, source deletion,
explicit rebuild or projector version change clears the cache. Cold replay orders
metadata by its durable source cursor so rebuild and incremental append agree.
`SummaryMetadataScans`, `SummaryMetadataRowsRead`, `SummaryCacheEvictions` and
`CachedSummaryCount` expose content-free qualification counters. Fixtures include
150 appends to a 200-record conversation with zero additional metadata history
scans, one cold reload after restart, rollback/retry, rebuild/deletion and LRU
capacity behavior. Persisting a summary still serializes its distinct model list;
its cost follows summary size rather than accumulated message/record history.
