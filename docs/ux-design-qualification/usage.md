# UXD-06 native Usage hand-off

Source basis `ec68062`; final reference frame 12 `VFZv56jHJJ2zSdBAQhNG` inspected
from its referenced PNG. Implementation modifies only `UsageView.cs` and
`UsageView.Analysis.cs`. No build/runtime/screenshot qualification performed by
this worker; root owns the serialized integrated build and UXD-09 acceptance.

The page now begins with its applied range, core account/model inputs, explicit
Apply and preset controls. Precise dates/zone/status/dimensions stay mounted behind
More filters. The main cards and single Account breakdown precede supplementary
content. Widths at least 1000px within Usage put daily/performance in a 320px
secondary column; narrower views stack it after results. Tables scroll horizontally
when necessary instead of clipping columns. There are no fixed page heights.

The shell must provide a bounded stretch viewport and disable its outer vertical
scroll for Usage. Usage owns `Usage results scroll`. Polling keeps its offset;
Back captures/restores local scroll, focused automation target, draft text,
applied query, group/attempt disclosure and local drilldown history. Group filters
now operate on the displayed server query instead of committing unrelated drafts.
The local Previous result action returns to the prior query and draft. Plain
Apply remains an explicit request to apply all draft filters.

Full token totals remain null when measurement is missing. Main and group rows
can display a known reported subtotal, visibly marked partial; the separate
measurement line reports measured/eligible attempts using `UsageDisplayFacts`.
Zero samples remain unknown. Cache creation is included in local component details,
and no cache/reasoning values are added to totals. Journal completeness is a
separate badge. A regressing journal cursor or observation time warns that source
continuity is unconfirmed. Failed refreshes identify retained data as last known;
analysis failure/truncation remains visible at the actual breakdown.

Historical-error cards and daily values remain distinct from completion-error
group rows. Both percentages use attempts. Daily/performance labels name the
actual applied account/model/filter scope; merely opening a group leaves their
scope unchanged. P50/P95 and sample/eligible counts remain together. Account,
model and route context links remain; attempt disclosure state now includes source
identity so same attempt IDs in different journals cannot share expansion state.

Reference differences are deliberate and reviewable: group details use the
existing native row disclosure rather than a second focused-group inspector.
There is one breakdown. No Export-client-results command is invented without a
backend/client export contract. Secondary telemetry remains the full applied
scope, and the stored daily chart keeps unknown full totals as unknown rather than
silently plotting partial values as complete usage.

## Stable automation and fixture hand-off

- `Usage results scroll`: bounded local ScrollViewer.
- `Usage additional filters`: opens/closes the existing `Filters & range`
  Expander; textbox automation names remain unchanged. Closed precision inputs
  are intentionally not visible or focusable. Tests must open this first.
- `Usage applied context`, `Usage journal coverage`, `Usage token measurement
  coverage`, `Usage unapplied filters`: separate context and coverage facts.
- `Compare by`, `Group order`: existing selectors; initial grouping is now Account.
- `Analysis group {dimension} {friendly name or key}` and existing group/attempt
  action automation names remain; `Drill into {dimension} {key}` is the direct arrow.
- `Back to previous usage result`: local applied-query drilldown return.
- `Performance details` remains the stable expander automation name; its visible
  heading now includes the actual scope instead of a fixed English heading.

`tests/ux-usage/fixtures/results.json` records the synthetic reference counts and
120/132 first-output measurement with P50 240ms/P95 510ms. Only total tokens are
fully measured there; absent component values are explicitly unknown.
`partial.json` separately specifies a complete journal with 65000 known reported
tokens measured for 110/132 attempts and a null full total. These are data inputs
for central fixtures, not executed test evidence.

UXD-09 should verify first real breakdown row visibility at 1120×780 and 800×560,
partial/full/null totals, complete versus incomplete journal, invalid date drafts,
polling with focused edits, local drilldown Back and Account/Model navigation Back,
mode/density changes, source/cursor regression, incomplete analysis and offline
last-known values. Captures must use the integrated production assembly on a
private display. No provider or user-screen interaction is needed.
