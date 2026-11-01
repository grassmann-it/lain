# Live Inspector review — 27 October 2026

Source reviewed: `835d2e2260dfa3f1b072eb8fcc1541c33c6dd665` plus the existing
working tree. This is a bounded source review and design proposal. No build,
private-Xvfb/native UI run, installed-runtime test, provider request, configuration
change or capture-policy change was performed. Existing tests were read, not run.
ZCode is outside this task.

## Outcome

Make Live a readable activity view for checking whether an agent is broadly doing
the intended work: actual emitted text first, distinct tool calls next, technical
events on demand. The current renderer is still primarily an event-card viewer.
Several source-level behaviors can explain the reported empty/noisy experience;
adding another metadata expander alone would not address them.

The [workflow contract](contracts/live-inspector.md) is the requested outcome.
The [existing capture contract](observability-contracts.md#inspector-api-and-ephemeral-dataflow)
continues to constrain content and retention.

## Findings, ordered by effect on the user's task

| Priority | Source finding | User consequence / scope |
| --- | --- | --- |
| High | Core `captureBuffer.consume` waits for newline or finish, omits a line above 2,048 bytes, and redacts lines matching credential/opaque-string rules. Host additionally holds back 128 characters. | Live text can arrive late, only at completion, or be replaced by an omission. A prettier Desktop cannot recover uncaptured content. This buffering is intentional redaction behavior, not proof of a broken collector. |
| High | `RenderEvents` makes a card for every block, including lifecycle, metadata and text events with null text; those show a state such as `active`. | The readable text is surrounded by repeated empty/status cards. Routine metadata is not confined to the existing expanders. |
| High | `Observe` emits a `tool_delta` for argument fragments even when a fragment carries no tool name/call ID. Host discards tool text; Desktop only merges nonempty text blocks. | One real tool call can become many uninformative Tool cards. The renderer lacks a reliable complete tool-call model for all supported protocols. |
| Medium | Adjacent text is merged only when many attributes match and the immediately preceding block is text. Interleaved metadata or empty redaction events breaks adjacency. | A single answer can fragment into repeated Assistant cards. Merging across arbitrary intervening events would also be unsafe without semantic block boundaries. |
| Medium | Default inventory labels are `Conversation <short ID>` or `Request <short ID>` plus request/stream counts and models; worker identity is largely in expanded details/search. A group still displays one selected request at a time. | Hard to spot the active worker or follow work over tool-continuation requests. A conversation grouping is not a continuous transcript. |
| Medium | `RenderEvents` clears/recreates the entire tree and constructs complete technical JSON/copy controls for every constituent event even when collapsed. | More allocation/layout work on frequent updates; a full rebuild rather than incremental text growth. No measured performance regression is claimed here. |
| Medium | Barebones mainly hides timestamps and density alters spacing; every block still has the same event-card/details structure. | Presentation levels do not substantially reduce event noise in this view. |

### Concrete source paths

- `proxy/adapter/internal/lain/inspector.go`: `captureBuffer.consume` and
  `redact`; 2,048-byte line limit, line buffering and bounded RAM source.
- `proxy/adapter/internal/lain/observation.go`: `Observe`, `classifyOutput`;
  protocol decoding, tool-fragment observations, metadata before readable output.
- `src/Lain.Host/Inspector/InspectorRedaction.cs`: `InspectorTextRedactor.Push`;
  128-character holdback, protected split-secret handling and monotonic age.
- `src/Lain.Host/Inspector/InspectorStore.cs`: `Ingest` always adds the incoming
  event even when the redactor has not released text, and flushes tails at finish.
- `src/Lain.Desktop/Inspector/InspectorView.cs`: `RenderEvents` (around lines
  720–817), `RenderGroups`, `GroupKey`, `RenderSelection`, `ScheduleFollowOutput`.

Text already uses a selectable plain string, and known JSON protocol fields are
decoded with `gjson.String()` upstream. The review does not establish that raw
JSON string envelopes are generally displayed as answers. Do not recursively
unescape arbitrary output; literal JSON/code emitted by a model must stay literal.

## Existing behavior worth retaining

- Evidence-qualified conversation/parent/response linkage; conflicting or missing
  identities do not silently fabricate a conversation/worker relationship.
- Fixed side-by-side request inventory/output, including the agreed 200-pixel
  narrow inventory. Search/activity filters do not silently retarget the selection.
- Explicit follow toggle, upward scrolling disables follow, viewport anchoring
  when not following, and explicit detail expansion overrides across rerenders.
- Pause affects display, not inference; expiry still runs. Selected old requests
  are not automatically replaced with a different new request.
- Bounded source/Host/Desktop buffers, generation-aware cursors, explicit gap and
  outage states, and removal of transient content when the view detaches.

Follow behavior targets the latest text's end rather than expanded technical
metadata. It does not currently perform a persistent multi-request conversation
follow. These distinctions must survive a UI rewrite.

## Proposed default UI

Keep the existing split layout. Reduce the top bar to search, activity filter,
Pause and Follow. Put Clear, manual Refresh and technical-source details in an
accessible overflow/context menu. Keep connection health and any content gap as
one compact visible strip; routine byte/cursor/retention counts move into details.

Inventory rows should lead with the observed worker label when available,
otherwise the model/account, then an activity summary (`Writing`, `Tool call ·
Read`, `Waiting`, `Finished`, `Failed`). Show friendly labels only when actually
known; full IDs remain copyable. An explicit `Follow latest request in this
conversation` option may be offered later, separate from following text scroll.
It must not change request while the user is selecting text or reviewing history.

The main pane should render semantic blocks:

- **Message:** readable selectable output with line breaks and stable incremental
  updates, one compact header per actual output block.
- **Tool call · name:** one compact activity row per proven call identity. Show
  only observed name/ID and lifecycle facts; no invented tool success or output.
- **Reported reasoning:** separately labelled and optionally collapsed; never
  infer hidden reasoning from prose.
- **Errors/gaps/omissions:** visible compact markers where relevant. Routine start,
  finish and measurement events update request status or a collapsed event list.

Metadata belongs in one request-level Details section and optional per-block
Details accessible through the header/context menu. Construct technical JSON
lazily when opened, within the same retention limits. A normal successful
sanitization need not occupy a visible line on every message; actual removed or
truncated content must remain visibly marked.

This is a proposed presentation, not a new capture permission. The user cannot
check exact commands, diffs, tool arguments or results under today's output-only
policy. Say this plainly in tool detail/empty states instead of simulating a full
agent transcript. A future opt-in capture extension is a separate decision.

## Data/projection changes required

First extract a pure presentation projection from existing DTOs: content blocks,
request state and hidden technical events. Ignore routine metadata for visual
segmentation, but preserve its cursor/evidence and never merge across a known
tool/attempt/turn/source/gap boundary. Treat unknown boundaries conservatively.

For robust call/block assembly, qualify additive ephemeral feed fields such as
provider response/item/block identity, content-block/tool index, fragment type
and observed lifecycle. In Anthropic input-json deltas the current classifier
loses block-index association; some Responses/Chat fragments likewise omit an ID.
Maintain bounded parser-local association from explicit starts/indices, not
heuristics by nearby event position. Missing/conflicting linkage stays unknown.
Do not turn `tool_delta` into `tool completed`; provider tool-call emission is not
local tool execution or result receipt.

Early readable streaming requires a separate redaction review. Removing the
newline buffer or 128-character holdback outright would weaken split-secret
protection. A bounded incremental sanitizer with explicit omissions may be an
option, but its policy and tests must precede activation. Immediate safe UX:
show `Output observed; readable text is buffered` when that fact is known, and
distinguish no output, buffered output, redacted/omitted output and expired output.

## Bounded work packages and acceptance

| Package | Deliverable | Acceptance |
| --- | --- | --- |
| LIVE-1 | Pure semantic projection and quieter default Desktop | Lifecycle/null-text events create no fake message cards; continuous text stays readable; errors/gaps remain visible; metadata is accessible on demand; no capture-policy change. |
| LIVE-2 | Qualified tool/block identity mapping through Core→Host→client | One synthetic parallel two-tool stream produces two named calls despite fragmented IDs/arguments; attempts/sources cannot merge; unsupported linkage remains explicit. No arguments/results retained. |
| LIVE-3 | Capture-latency investigation and approved sanitizer improvement | Synthetic slow no-newline text, short final output, overlong line, split credential and Unicode cases establish observed delay/omission; no secret regression or fabricated completeness. Changes remain behind exact qualified policy if needed. |
| LIVE-4 | Native UI acceptance and bounded-render follow-up | Private Xvfb at 800×560 and 1120×780, long text, concurrent workers, pause/expiry, selection/copy, resize, follow-off anchor, gap/source restart; frequent updates remain bounded and do not repeatedly build hidden JSON. |

LIVE-1 can proceed independently with the existing DTOs. LIVE-2 and LIVE-3 affect
the ephemeral observation contract and need source-specific fixtures. No need to
rewrite durable Usage or persist full conversations for any of these packages.
Preserve one existing user-selected request unless a new explicit follow mode is
selected. Optional conversation follow needs its own tests for worker mixing and
request expiry; do not bundle it silently into scroll-follow.

The existing `tests/inspector` fixtures cover redaction, bounds, identity and
source behavior, and `tests/desktop/Program.cs:CheckInspector` covers selected
native-view scenarios. Some Desktop fixtures include `Fixture tool result.`
even though the production output-only Host policy excludes tool results; passing
that UI fixture would not prove real tool-result capture. Add cases representative
of production null-text/fragmented-tool noise and delayed sanitized text.

No external UI framework is necessary to make this change. The local source and
explicit user workflow provide a more concrete starting point than selecting a
new chat UI library. All acceptance cases above are proposed, not executed here.
