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
