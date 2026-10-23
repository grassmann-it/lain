# Desktop redesign review

Review owner: independent UI/accessibility acceptance worker. Date: 2026-10-23.

## Scope and decision

**Visual direction accepted for integrated source `f5f7c74`.** All 30 actual
captures from the successful private-Xvfb run were inspected, covering 15 scenes
at 1120 × 780 and 800 × 560. The graphite Semi/Ursa workspace, compact lists and
controls, restrained blue accent and persistent bottom navigation address the
requested MeowChan direction. No blocking overlap or horizontal content loss was
found in these fixtures. The run passed all 63 functional assertions.

This decision closes the visual-direction review for that source checkpoint. It
does not claim full keyboard/screen-reader conformance or qualify a subsequently
packaged/installed build. Those limits are recorded below. The old desktop's
functional evidence is not used as proof of the redesign.

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
| UI-01 | The baseline shell's ten-second refresh replaced whole pages and detached keyboard focus. | Resolved: source fix `376a0b4`; selected account row/list identity and focus survive passive polling in the final run; explicit refresh displays changed configuration. |
| UI-02 | Usage filter labels lacked accessible names/label relationships in the first redesign source. | Resolved by source inspection: `f016b94` names all six filters. Runtime input focus and pending-filter retention also pass. |
| UI-03 | Usage attempt rows were rebuilt every five seconds, preserving expansion but detaching the focused toggle. | Resolved within tested scope: `f016b94` restores focused attempt by ID; focus survives polling in the final run. Focus-ring appearance is not established by that assertion. |
| UI-04 | Exact daily chart values were exposed only through pointer tooltips. | Resolved: `f016b94` adds a persistent Daily totals expander; the expanded table and exact totals were visually inspected. |
| UI-05 | Route filters lacked accessible labels in the first redesign source. | Resolved by source inspection: `e6f4d28` names filters and row actions. |
| UI-06 | A fixed 780 px route table minimum put row actions outside the small window's content viewport. | Resolved: `e6f4d28` adds flexible compact columns; Edit/Remove remain visible in the actual 800 × 560 capture. |
| UI-07 | Profile destination label named a layout container; search/composite selections lacked explicit accessible names; selecting profiles rebuilt the focused selector. | Resolved by source inspection: `7bf09ee` names the actual controls and retains selector instances; final profile save/apply/export flows pass. |
| UI-08 | The first actual Accounts render had approximately 58 px rows, pushing useful detail down the page. | Resolved: `3b2b80e` reduces rows to 44 px while retaining Semi templates; all five fixture accounts now fit the narrow initial viewport. |
| UI-09 | Shared removal confirmation called routes/profiles a connection and lacked destructive styling. | Source fix `c6bf084` uses accurate generic removal copy and the danger class; removal is not included in the final fixture interaction assertions. |

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

Evidence directory: `/tmp/lain-ui-evidence-4`. The final `evidence.json` reports
`PASS`, `privateXvfb: true`, display `:2`, 63 assertions and 30 PNGs. The executed
production desktop assembly was
`/home/tomka/Desktop/lain/tests/desktop/bin/Debug/net10.0/Lain.Desktop.dll`, SHA-256
`91EFBE8E2B29F4BD399175586FE20C63DC2645D593A78C1010BF61635941F5F2`.
The integrator identified source commit
`f5f7c74841daa1d7790d11543919595322d11761` for this run.

Every scene below was inspected in both `-1120x780.png` and `-800x560.png` forms:

| Scene filename prefix | Visual result |
| --- | --- |
| `overview` | Compact hierarchy and status values; no former oversized branding or teal/navy cards. Smaller viewport scrolls vertically. |
| `accounts` | Aligned compact rows, readable selection and credential state; unknown quota remains explicit. Five fixture rows fit at minimum width. |
| `login` | Clear waiting state, readable primary action, collapsed manual-link section and separate cancellation. Lower controls require vertical scrolling at 800 × 560. |
| `routes` | Dense comparison table. Narrow mode puts provider below the model and keeps row actions visible. |
| `route-editor` | Two usable field/binding columns without overlap; smaller viewport scrolls to lower options/actions. |
| `profile-editor` | Aligned fields, searchable selection and readable route rows. The route list and page have separate bounded vertical scroll areas. |
| `profiles` | Compact selector and single detail panel; primary management actions fit in the narrow capture. |
| `codex-export` | Export path and launch command remain readable; long command wraps at minimum width. |
| `usage` | Filters wrap into two rows on the narrow window; totals switch to four columns; metadata and unknown semantics remain explicit. |
| `usage-details` | Expanded Daily totals table shows exact values at full width; narrow capture shows its heading and columns with remaining content below the viewport. |
| `usage-null` | Unknown token values are em dashes, not fabricated zeroes; unknown-attempt counts remain visible in tables. |
| `usage-error` | Captured scrolled data remains laid out correctly; the error banner is outside the captured viewport, so its appearance is not visually verified here. |
| `setup` | Concise ordered sections and consistent controls; lower steps are reached by vertical scrolling on the small window. |
| `offline` | Persistent unavailable notice, last-known configuration wording and amber footer indicator communicate stale/offline state. |
| `reopened-usage` | Reopened view retains the same layout and obtains fresh fixture data. |

The final runtime assertions cover account addition/disable/import, revisioned
route/profile saves, profile apply, fixture Codex export, login completion and
cancellation, unknown usage, API failure/recovery, offline state, polling focus
retention, detach cancellation and close/reopen behavior. Requests matched the
in-memory fixture allowlist; no real credentials, host, provider or browser
authorization was used. The first Xvfb attempt failed to bind its display socket;
two subsequent preliminary runs stopped on harness focus/label expectations.
Only the successful final run is counted as functional acceptance evidence.

## Limits of this acceptance

- Button tests raise routed Click events; they do not prove physical pointer
  hit-testing, complete Tab order, OS shortcuts, screen-reader output, native
  dialogs, clipboard integration or browser authorization.
- Runtime focus assertions establish focused-control retention, not a complete
  focus-ring audit. The focused attempt toggle is below the viewport in the
  `usage-details` captures; the `usage-error` banner is likewise above its
  captured scroll position. Neither appearance is claimed as visually verified.
- Quota rendering was inspected with unknown values. Populated multi-window,
  progress, stale/error quota fixtures and extreme-length account names are not
  covered by this capture set. Source review is not substituted for those renders.
- Minimum-height views use vertical scrolling; all form actions are not expected
  to remain simultaneously visible. The captures show the intended scroll
  boundaries, not a guarantee of physical wheel/keyboard scrolling behavior.
- The fixture assembly is a Debug source build. Packaged/installed parity and
  read-only real-host usage verification remain the integrator's separate gate.
