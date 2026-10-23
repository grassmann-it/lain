# Desktop redesign review

Review owner: independent UI/accessibility acceptance worker. Date: 2026-10-23.

## Scope and decision

**Pending integrated visual verification.** Source review and reference comparison
alone do not approve the replacement desktop. The old desktop's functional
evidence remains useful but does not establish acceptance of this redesign.

This review covers visual direction, information density, interaction semantics,
keyboard accessibility and the 800 × 560 minimum window. GUI evidence must come
from a private Xvfb display. No native desktop, real credentials, real account
mutations or service changes are part of this review.

## Reference and baseline inspected

The requested reference is [MeowChan](https://github.com/tfgrass/MeowChan), inspected
from the local read-only checkout at `/home/tomka/Desktop/oChan`:

- `docs/screenshots/v0.9.0-advanced.png`: compact table, one detail pane, clearly
  separated toolbar and footer, visible selected/focused cell.
- `docs/screenshots/v0.9.0-settings.png`: aligned form rows, thin dividers, concise
  explanatory text, compact controls and restrained primary action.
- `Desktop/WorkspaceStyles.axaml` and `App.axaml`: Semi/Ursa control templates;
  graphite surfaces, blue primary accent, 13 px text, 20 px page headings,
  approximately 30–32 px controls and 5 px corner radii.
- `docs/screenshots/README.md`: reference screenshot provenance and Xvfb capture
  conditions. These are reference images, not lain test evidence.

The rejected lain baseline was inspected at source checkpoint `5fc1439` and in
`/tmp/lain-w8-overview.png` plus
`/tmp/lain-installed-gui-check/reopened-usage.png`. It used a teal/navy palette,
large brand and headings, oversized padded cards, and broad margins. Its minimum
window was 880 × 640. Those screenshots are historical comparison material only;
this reviewer did not run the native desktop.

## Acceptance criteria

| Area | Required observable result |
| --- | --- |
| Direction | Neutral graphite workspace, restrained blue accent, small page titles and ordinary desktop controls; the former teal dashboard appearance is absent. |
| Density | Account, route, profile and usage data use aligned rows or compact sections. Common comparison tasks do not require reading one oversized card per item. |
| Hierarchy | Page title, local actions, table/section headings, values and helper text remain distinguishable. Primary and destructive actions are visually differentiated. |
| Small window | At 800 × 560 navigation and page actions remain reachable. Labels and controls do not overlap or run outside the page. Scrolling is discoverable and preserves content access. |
| Long/unknown data | Long identities truncate with a way to read the full value. Unknown quota/token data stays explicitly unknown; stale snapshots do not appear current. |
| State semantics | Ready, stale, offline, unavailable and failed states are expressed in text, with consistent supporting color. Color alone never carries the result. |
| Keyboard | Every interactive control is reachable and visibly focused. Lists remain usable with keyboard selection. Passive polling does not discard focus or pending edits. |
| Accessible labels | Inputs have an accessible name or label relationship. Icon-only actions have specific names. Essential information is not available only on pointer hover. |
| Retained behavior | Account setup, routes, profile operations, usage queries, login cancellation and relevant error states still work against isolated fixtures. |
| Provenance | Captures identify the integrated source, window size, fixture and private Xvfb environment. Routed click-handler tests are distinguished from physical input tests. |

## Findings sent to implementation

| ID | Finding | Status |
| --- | --- | --- |
| UI-01 | The baseline shell's ten-second refresh replaced whole pages and detached keyboard focus. | Source fix `376a0b4`: compare semantic snapshots; defer passive replacement while the page owns focus. Runtime verification pending. |
| UI-02 | Usage filter labels lacked accessible names/label relationships in the first redesign source. | Source fix `f016b94`: six named filters. Integrated verification pending. |
| UI-03 | Usage attempt rows were rebuilt every five seconds, preserving expansion but detaching the focused toggle. | Source fix `f016b94`: restore focused attempt by ID. Runtime verification pending. |
| UI-04 | Exact daily chart values were exposed only through pointer tooltips. | Source fix `f016b94`: persistent keyboard-reachable Daily totals expander. Integrated verification pending. |
| UI-05 | Route filters lacked accessible labels in the first redesign source. | Source fix `e6f4d28`: named filters and row actions. Integrated verification pending. |
| UI-06 | A fixed 780 px route table minimum put row actions outside the small window's content viewport. | Source fix `e6f4d28`: flexible compact columns, provider in identity cell at narrow widths. Render verification pending. |
| UI-07 | Profile destination label named a layout container; search/composite selections lacked explicit accessible names; selecting profiles rebuilt the focused selector. | Source fix `7bf09ee`: named controls and stable selector instances with only detail replacement. Runtime verification pending. |

The theme foundation's final shared title is 22 px, with 13 px body text, 32 px
controls and 5 px control radii. Those values are consistent with the requested
direction without requiring a pixel-for-pixel reference copy.

Calculated WCAG contrast ratios from the theme source are 10.21:1 or better for
primary text and 5.05:1 or better for muted text across normal, raised and selected
surfaces. The dark label on the blue primary button is 6.73:1. Semantic success,
warning and danger colors are at least 4.61:1 on those surfaces. These calculations
do not establish rendered focus visibility, disabled-state appearance or full
accessibility conformance.

## Integrated render evidence

Not yet available. Final review must record inspected screenshot paths, source
checkpoint, test report and any remaining limitations before changing the
decision above.
