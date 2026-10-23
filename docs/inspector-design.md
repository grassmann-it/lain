# Owned live inspector implementation

Implementation within the user's authorized [2026-10-24 expansion](expansion-2026-10-24.md).
The public DTOs and measurement definitions are in
[observability contracts](observability-contracts.md). This document records the
bounded Host implementation; it is not a release or live-provider acceptance claim.

## Dataflow and ownership

```text
own Core executor observations
  -> GET /lain/inspector/events (authenticated, non-destructive RAM replay)
  -> InspectorCollector (bounded reads, source/cursor validation)
  -> InspectorStore (sanitized transient event ring and descriptors)
  -> GET /api/v1/inspector and /api/v1/inspector/events
```

The Host's existing authentication/loopback middleware protects both public GET
endpoints. `AddInspector(managementToken)` registers the store and hosted reader;
`MapInspectorEndpoints()` maps the two routes. Program wiring belongs to the
integrator. The reader uses its own loopback HTTP client with redirects and
ambient proxies disabled, a 10-second timeout, and an 8 MiB response bound.
It requests 500 events per page and at most 16 pages per pass. Caught-up polling
is 250 ms, continued catch-up 10 ms, and failure retry 2 seconds. A semaphore
prevents overlapping passes. Only exception type is logged, on type changes.

This stream does not read the personal proxy, its old collector, its queue, or
its database. Durable usage remains the independent journal-to-SQLite path;
inspector events cannot acknowledge or alter it. The Host adds no event files,
payload export, provider request, tool execution or inference cancellation.

## Identity and grouping

The current source emits concrete attempt lifecycles. Each descriptor has a
stable ID formed from the source instance and attempt ID, plus the explicit
logical request ID. Multiple attempts can therefore belong to one request,
without claiming a separately observed request lifecycle. Tool names/call IDs
remain events within that request/attempt; they never create another chat.

Conversation, turn, worker, parent-worker/request, response-chain, harness,
profile and routing dimensions are accepted only with matching evidence from
`route_binding`, `harness_header`, `request_body`, `provider_response` or `core`.
Only bounded curated fields survive. Missing evidence stays null. Contradictory
known dimensions become unknown and remain conflicted for that descriptor;
later events cannot silently move it to another group. Stable account/route or
request conflicts reject the event and mark a gap. Actual model updates remain
separate from the requested model. Requested/effective reasoning fields do not
overwrite one another. No grouping uses time proximity, content similarity,
model prefixes or a historical worker's presumed liveness.

The adapter accepts the Core's top-level nullable `inputTokens`, `outputTokens`
and `totalTokens`; absent cache/reasoning components remain null. Reported zero
remains zero. Measurements require a bounded source name and finite nonnegative
durations; an impossible first-output duration beyond a known total is unknown.
No rate, duration or percentile is inferred from a paginated event sample.

## Public cursor and gaps

The public `sourceId` is a Host ring generation, not the Core instance ID. It
changes after a Host restart or a changed Core instance. Core sequences are
deduplicated before assigning Host cursors; deferred sanitized text can add a
display event, so the two cursor spaces intentionally differ.
Source identities that would change during control/format normalization are
rejected, so distinct upstream generations cannot collapse onto one identity.

Resume with a positive `after` requires `sourceId`. A mismatched source yields
`truncated=true`, `gapReason=source_changed` and the current retained tail.
A future cursor in the same generation, negative values, repeated scalar query
parameters and excessive limits are rejected. Filtering by `requestId` scans
the global cursor, including nonmatching events; an empty filtered page can
advance to the high watermark. A page is limited by record count and 4 MiB of
serialized event data. Readers do not own additional server-side queues.

Upstream unseen intervals, unsupported events, capacity loss and content expiry
are explicit gaps. `droppedEvents` counts Host-retained events removed or unseen
events omitted, not every upstream eviction of content the Host already read.
Gap flags are conservative and global: an identity filter cannot certify that a
capture gap was irrelevant. A completely evicted upstream page advances only
with an explicit gap. Failed/truncated intervals are not reconstructed from
durable usage. Source loss changes active descriptors to `unknown`, without a
fabricated completion timestamp. A restart retains these short-lived metadata
tombstones but clears content and old-generation cursors.

## Bounds and lifetime

The default Host caps, exposed in every snapshot, are 256 descriptors, 8,192
events, 32 MiB serialized event data globally, 1 MiB per descriptor, 256 KiB per
event, a 120-second content lifetime and 60-second terminal metadata lifetime.
Lower limits are supported for synthetic testing; exceeding the ceilings is
rejected. Active descriptors are preferred: completed/unknown retained metadata
is evicted first, and excess new active streams are dropped with coverage loss.

Retention uses monotonic time from Host ingestion, including pending redaction
buffers. New deltas and completion flushes preserve the original receipt age of
held characters. An emitted chunk uses its oldest contributing receipt; newer
characters in the remaining holdback keep their own age. Event expiry therefore
checks receipt age independently of cursor order. Expiry is evaluated on reads
and collection passes, including failed passes. Terminal descriptor removal
also removes its content, so content can
expire earlier than the maximum. There is no secure-erasure or exact-GC-time
claim. A pull-only viewer cannot make inference wait, retain an unbounded pause
backlog or allocate an extra unbounded event queue.

The Core independently retains up to 4,096 events/4 MiB for 900 seconds, with
2 KiB display fields. Its previous history may already be older when the Host
first reads it; the 120-second Host retention begins at ingestion. Closing a
viewer is not a request to erase the independent Core buffer. Both lifetimes
are bounded, and neither is a durable content archive.

## Redacted output policy

`redacted_output_only_v1` permits inert text and provider-visible reasoning
output, plus tool names/IDs. It excludes request prompts, raw frames, HTTP
headers, credentials, tool arguments/results and arbitrary error text. Unknown
JSON properties are never forwarded. Tool event text is always removed, even
when a source claims that it is already safe. Public text events explicitly say
`sanitized` or `removed`; status-only events may have null redaction state.

The Core first assembles bounded display lines. The Host applies an additional
bounded sanitizer: credential-shaped patterns and credential-key markers are
redacted; control/format characters are removed. A 128-character holdback
handles markers split across deltas and is flushed at observed completion.
Credential continuations are dropped until their delimiter; authorization
lines, oversized chunks and gaps discard through the next newline. This may
delay short display output and remove benign text. It never changes the Core's
first-output timing measurements. Metadata has its own length/identity checks;
credential checks run after control/format normalization.
Pattern sanitization cannot identify arbitrary secrets written as ordinary
prose; it is a lossy display transform, not a confidentiality classifier. No raw
fallback is returned on a sanitizer failure.

## Review and execution evidence

The previous TUI's `docs/live-protocol.md`, `clients.py` and `state.py` informed
the separation of request/attempt identity, explicit unknown states, bounded
replay and non-advancing synthetic viewer gaps. Its collector and known replay
workaround were not reused. The simpler HTTP pull transport is an implementation
choice for this first integration; SSE is not required or claimed.

The integrator ran the focused synthetic suite and independent Host boundary
suite on 2026-10-24. The Host implementation is `d94a7cf`, integrated as
`3b63bcb`; the independent boundary suite is `963154d`, integrated as `a41432c`.
Both builds finished with zero warnings and errors:

- `tests/inspector/Inspector.Tests.csproj`: 27 assertions passed; log
  `/tmp/lain-expansion-tests/Inspector.Tests-integrated.log`.
- `tests/inspection/host/InspectorIntegration.csproj`: 80 assertions passed,
  fake HTTP only; log
  `/tmp/lain-expansion-tests/InspectorIntegration-integrated.log`.

These cover provenance/conflicts, grouping, zero/null, source generations,
filtered cursor progression, cardinality/byte/monotonic-age bounds, collector
envelope failures and split credential/tool-content removal. The follow-up
normalization, slow-trickle, delayed-flush and mixed-age regression cases were
added after those runs; their execution awaits the serialized integration slot.
No worker-side build or service run is claimed for that follow-up. A passing
unit suite alone does not establish provider parser coverage, a working packaged
inspector or real UI grouping; those need integrated own-core and private-Xvfb
evidence.
