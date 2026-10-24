# UXD-07 / UXD-14 Live acceptance handoff

The production change is confined to `src/Lain.Desktop/Inspector/InspectorView.cs`.
The shared shell must give this retained instance a finite viewport. Presentation
updates call `SetPresentation` in place; actual navigation away detaches it.

Use the existing six-stream synthetic source in `tests/desktop/Fixture.cs` (one
evidenced conversation, independent unsupported claim, error, conflicting retry
parents), with the UXD-09 harness. No private Host, provider, user screen or
installation is needed. At 1120×780 the list and output sit alongside each other.
The confirmed `LIVE-NARROW-A` contract also keeps both panes visible at 800×560:
a permanent 200px left inventory and output in the remaining column. Selecting a
request changes only the right detail. There is no `Back to live requests`
replacement page or selector-only narrow layout. Column definitions and controls
stay mounted across resize, polling and presentation changes.

Focused source-only delta, 25 October 2026, baseline `7a6b88c`: the empty state now
explains that current-source Live output is transient and retention-bound, while
historical request records belong to Usage → Attempts. A restart does not replay
historical output into Live. No Hostcollector, retention, cursor, source-reset,
mutation or provider behavior changes accompany this presentation change.
Structured cases for UXD-22 are in `narrow-a-cases.json`; these are acceptance
inputs, not recorded passes.

Focused cases for the central private-Xvfb harness:

- Poll reordered snapshots: retain native group buttons, chosen RequestId and
  inventory scroll; newly arrived identity evidence can move its group without
  selecting another request. Grouping still requires supported matching evidence.
- Keep the search field focused across polls and Simple/Advanced/Barebones changes.
  A hidden selection is retained but its output is not presented as a filter hit.
  Clearing the filter reveals it again; no remote inference action occurs.
- Choose a request within the evidenced conversation. Reasoning, tool output and
  assistant chunks stay with that request. Every merged source chunk has its own
  complete metadata and explicit copy action inside Event details.
- Manually expand/collapse Event details or Complete stream attributes, switch
  presentation, then poll: local choices persist. Technical fields remain locally
  available at every level. Raw reported claims and unsupported provenance are
  distinguished from identities actually used for grouping; conflicting retries
  have an explicit warning.
- Disable Follow output or scroll upward, append a new event, then change density:
  preserve the visible event anchor and offset. Explicitly checking Follow output
  resumes following. Keyboard focus within output defers new rendering, while TTL
  still expires and bounds still trim content.
- Pause display: automatic fetches stop, inference/source do not stop, and local
  monotonic TTL keeps running. Refresh fetches once while paused. A retention gap
  on Resume remains visible; no completeness guarantee is invented.
- Clear displayed events advances no cursor backwards, never replays pre-clear
  data, and writes no configuration or persistent Usage. Source gaps, local
  500-event/262144-character trimming and local TTL expiry are separate notices.
- Metadata retention expiry does not silently select the first remaining request;
  the selected request is marked as no longer described. Already received text
  retains only its original receipt TTL, with no invented current status.
- A lost source reports unknown active state. A new source clears all output and
  relationships. Actual detach stops polling and deletes all buffered content;
  only the source/request ID and pixel offset survive as navigation anchors. Back
  reloads currently retained events from that same source, never a second history.
- In both window sizes, assert a finite output viewport (at least 64px where the
  shell provides its agreed minimum), visible Pause/Refresh/Clear/Follow and simultaneous left inventory/right output,
  no outer page scrolling, and selectable/wrapped long output. Inspect screenshots.

No builds or runtime evidence are claimed for the 25 October source delta by this handoff. Root owns the combined
Desktop build; UXD-09 owns central fixtures and private-Xvfb screenshots.
