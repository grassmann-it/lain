# Trace research: readable and API views

Phase 0 proposal · 29 October 2026. Read-only source review of the working tree;
no implementation, recording, provider call, runtime change or new qualification.
The existing WIP and installed/runtime state are not altered by this document.

## Intended result and recommendation

Allow a person to understand a deliberately recorded AI API interaction, inspect
what crossed a named API boundary, and later identify useful or harmful decision
points. The small first implementation should record **one explicitly selected
client and one concrete route for one short session**, with readable messages and
API records linked by stable source references. A separate capture contract is
needed before implementation: the current Live Inspector deliberately excludes
prompts, tool arguments/results and persistence.

This proposal assumes local, single-user review; supported UTF-8 JSON/SSE bodies;
one qualified protocol/client path first; and no automatic execution, grading or
training export. Those are scope recommendations, not newly accepted decisions.
Rights/consent for later model training are a separate per-source decision; a
recording permission alone must not become a training approval.

## What the present source actually observes

- `proxy/adapter/internal/lain/http.go:215` reads bounded incoming application
  bodies. At `:308` it preserves the original body for the active request; at
  `:314` it rewrites the model and reserializes JSON before the handler continues.
  Therefore `req.Payload` or a reserialized request is not evidence of the exact
  original incoming bytes.
- `proxy/adapter/internal/lain/integration.go:175` inspects request headers and an
  original/fallback payload to derive curated identity dimensions. At `:185` it
  creates an attempt record; it does not persist the request body.
- `proxy/adapter/internal/lain/executor.go:154` and `:253` pass executor response
  payloads/chunks to `Observe`, before forwarding them to the downstream handler.
  These are executor-boundary application observations. They do not prove the
  exact upstream provider body or the exact bytes delivered to a client.
- `proxy/adapter/internal/lain/observation.go:16` accepts only payloads up to
  1 MiB, recognizes complete JSON or `data:` lines, extracts known output fields,
  and emits display deltas. At `:67` tool arguments become an omission marker;
  `:113` onward handles several protocol shapes. The parser does not preserve
  every wire field, framing detail, or stream-block identity.
- `src/Lain.Host/Inspector/InspectorStore.cs:160` applies another display
  transformation. At `:172`, tool content and arbitrary metadata text are
  explicitly excluded. Existing event details therefore mean **Inspector event
  JSON**, not raw API JSON.
- `docs/observability-contracts.md:192` describes independent RAM Inspector and
  durable usage metadata paths. Its `:244` content policy excludes prompts,
  headers, tool arguments/results and unfiltered errors. A capture archive cannot
  silently reuse those endpoints or make their old policy label mean more.

The current inspector's “Reasoning” channel means only reasoning text or a
summary actually returned by the API. Do not infer internal thoughts, decrypt
opaque reasoning items, or present omitted reasoning as recoverable.

## Make every API boundary explicit

A future record needs `capture_id`, record sequence, boundary, direction,
protocol, request ID, attempt ID when applicable, a body reference, observed
length, retained length and a completeness state. Record wall-clock time for
navigation and monotonic ordering within the capture. Keep policy version,
transformations and omission reasons beside the record. Content must not enter
ordinary logs or the durable usage journal.

| Boundary label | Evidence it can establish | Important limit |
| --- | --- | --- |
| Client request received | Body bytes accepted before model/handler rewriting | Full history may repeat earlier messages; does not prove how the client constructed them |
| Executor input/output | Payload handed to/from an executor | May already have been normalized or translated; must not be labelled provider wire |
| Provider request sent / response read | Application body at final HTTP transport boundary, with an explicit transport-observation contract | Not TLS, HTTP/2 frames, packet boundaries, or necessarily original compressed bytes |
| Client response written | Application payload written by the local response writer | Write success alone is not proof the client received or consumed it |

For a first capture, prefer client request received plus client response written:
that pair explains the client's API conversation with the fewest provider-specific
claims. If the question is specifically what a provider received after translation,
add the final transport boundary as a separately qualified increment. Executor
observations can help relate attempts, but cannot substitute for either boundary.
The UI must display unavailable boundaries as unavailable.

The existing final transport is a useful future investigation point:
`proxy/adapter/internal/lain/executor.go:406` normalizes/validates requests, changes
auth headers, and eventually calls `t.next.RoundTrip` at `:460`. Incoming response
bodies can subsequently be rewritten at `:469` by
`proxy/adapter/internal/lain/context_boundary.go:18`. Any eventual provider-body
capture has to be placed and labelled relative to those operations. The HTTP
client can also transparently decompress bodies; byte fidelity must be specified
at that application layer rather than claiming packet capture. Never store
Authorization, cookies, API keys or arbitrary request headers as “raw evidence”.

A raw-body copy is byte-faithful only when complete and unmodified. A redacted or
truncated body is an explicitly transformed API record. A decoded, pretty JSON
view is another presentation with the original retained body as its source.
For the first MVP, call the tab **API records**, state the boundary in every
record, and expose “Body as retained” and “Pretty JSON” separately. Do not promise
an unrestricted raw transcript while also promising automatic secret removal.

## Readable view and tool evidence

Keep the present request navigation, with a selected capture and a two-tab view:
**Messages** and **API records**. A persistent strip shows recording state,
selected scope, completeness/gaps and retention. Switching views does not start,
stop or expand recording.

Messages decode only known protocol fields. Display actual role, ordered content
blocks, assistant text, tool call, tool result, reported reasoning/summary, error
and capture status. Unknown structures remain visible as unsupported API records;
no blank “successful” message is invented. Merge deltas only with a proven
request/attempt/output-block identity. A call ID alone does not establish all
block boundaries. Use protocol stream indices/item IDs where the protocol
provides them, and retain the reference to the contributing record span.

Each readable block can reveal its API record and JSON path or stream event/item
reference. Changing parser versions should be able to rederive a projection from
the retained record rather than rewriting evidence. Parse JSON strings once
according to the protocol; never recursively unescape arbitrary model prose.
Plain selectable text is adequate for the first MVP; do not execute HTML, scripts,
commands, links or tool payloads displayed in a transcript.

A gateway normally observes a requested tool call and, on a later request, a
client-submitted tool result. That is useful evidence, but not direct evidence
that the shell/browser/file operation actually ran. Show these distinct facts:

| Observation | Honest UI state |
| --- | --- |
| Model emits tool name/arguments | Tool requested |
| Client submits a matching result | Result reported by client |
| API response reports a provider-hosted tool result | Result reported by provider |
| A trusted harness supplies a separately linked execution event | Execution observed by that harness |
| Stream ends/cancels without a result | Result not observed |

Link by explicit call/item IDs scoped to the conversation/request/protocol; never
by tool name, text similarity or time proximity. Parallel tools and retries stay
separate. A result reporting `exit_code: 0` is still a result's claim unless a
harness execution source establishes it. Tool arguments/results often contain
files, credentials or third-party data; receiving tool metadata is not permission
to record those payloads. No harness connector or execution-event capture is
proposed for the first increment.

Repeated full-history requests must remain separate API records. A readable
conversation may present a repeated source item once only if protocol IDs or an
explicit verified relation prove it, with links to every occurrence. Otherwise
keep request snapshots separate. Compaction, Responses state/previous-response
IDs, missing tool results, and unsupported multimodal bodies are visible coverage
limits, not permission to reconstruct a complete conversation.

## Selective recording and bounded operation

Use explicit start/stop recording controls, independent of “Pause display”,
“Clear view”, route selection and training approval. Recommended initial consent
is short-lived, local, and scoped to the intersection of a registered client
identity and a fixed route. Freeze that effective policy on admission to each
request and show its capture/session ID. Reject an unidentifiable client for a
client-scoped policy rather than accepting any request with the same User-Agent.

Current source accepts inference tokens but does not establish a per-client
principal: see `proxy/adapter/internal/lain/http.go:20`, token comparisons at
`:240`, and the current Codex inference token endpoint at
`src/Lain.Host/Program.cs:397`. A label such as “Codex” from headers is display
metadata, not sufficient consent isolation. A future per-client credential
registration or an explicitly isolated listener/client connection is a required
prerequisite for claiming client-specific recording. Never put the credential
itself into the archive.

For v1, choose a direct route; defer collections/failover selection. Otherwise
consent must cover the concrete selected route/provider for every attempt, with
unknown or unapproved fallback omitted and explicitly marked. A UI route filter
must never become a capture authorization rule implicitly.

Proposed starting limits, to be validated with synthetic fixtures: one recording,
15-minute maximum session, 64 MiB total retained bodies, 4 MiB per message/body,
128-record or 1 MiB bounded writer queue, and an explicit next-day expiry.
These are design placeholders, not measured requirements. Capture admission and
copying are bounded; disk/queue failure marks a gap and ends recording without
blocking, cancelling or replaying inference. Requests already in flight use the
frozen start policy; stopping records an exact cutoff, leaving their captured
suffix incomplete rather than pretending completion. New requests cannot join a
stopped session. This is best-effort observation; a complete-audit guarantee
would require a different failure policy.

The UI reads indexed pages rather than loading an archive into the existing RAM
Inspector. Preserve the recent performance work:
`src/Lain.Desktop/Inspector/InspectorView.cs:24` limits the live display to 500
events/262,144 characters; `:914` renders an 8,192-character reading window in
4,096-character sections; `:970` exposes only 25 source-event details at a time.
Apply bounded visible text and bounded metadata pages to API JSON too. Pretty
printing an entire multi-megabyte body into a collapsed control would reintroduce
the same problem. Perform bounded parsing/index work off the UI thread, preserve
selection/scroll and provide cancellable loading. “Copy complete retained body”
must state the retained byte count and all omissions; it must not imply full
source retention. Huge copies need their own bounded policy.

The native synthetic performance evidence is in
`docs/live-inspector-performance-2026-10-29.md`: 363 ms maximum measured input
heartbeat in that particular long contiguous response test, 123 checks, source
WIP only. It is not a guarantee for a new archive view, 500 independent cards,
or concurrent live streams. A new renderer needs new evidence.

## Small implementation increment and acceptance gates

1. Define the separate capture policy, identity prerequisite and record envelope;
   create only synthetic JSON/SSE fixtures for one chosen client/protocol path.
   Agree exact boundary and body-secret policy before collecting real data.
2. Add a separately bounded opt-in recorder for that one direct route/session.
   Preserve original observations, derive messages with source references, and
   retain unsupported frames/omission markers as evidence. No training export.
3. Add a local paged capture viewer with Messages/API records, stable navigation,
   explicit completeness, independent stop/display pause, and deletion/expiry.
4. Qualify split JSON/SSE boundaries, UTF-8/surrogates, long lines, huge arguments,
   multiple parallel tools, retries/cancellation, repeated history, stateful or
   compacted input, unsupported content, disk full/slow writer, policy revocation,
   and client isolation with synthetic data. Compare inference bytes and outcomes
   with recording on/off; document recorder-side resource and latency budgets.
5. Only then propose a deliberate small real capture with a separately specified
   client, route/provider and content policy. Good/bad annotations and candidate
   training selection follow later and keep source provenance and rights status.

The first deliverable worth reviewing is a faithful selected API exchange and a
legible linked projection. An agent evaluation system, hidden-reasoning capture,
automatic dataset builder, tool executor and general packet recorder are not
required to establish that result.
