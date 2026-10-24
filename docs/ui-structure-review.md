# UI structure · independent UX review

Review date: 24 October 2026. **Accepted for the reviewed synthetic desktop/UI
scope; no remaining blocking UX finding.** The final implementation removes the
Overview/Accounts duplication, preserves the required quota information, and
makes the common Accounts, Profiles and Usage tasks visible earlier.

## Basis and limits

The reviewer read `docs/ui-structure-implementation.md`, the relevant accepted
sparring requirements and current source in the integrator’s root checkout,
including the initially uncommitted shell/workspace changes. Product source was
frozen at `99c93fe`; the final test source is `a66cb2a`. The review-only worktree
starts at `4f5352a`; only this document was changed here.

Final evidence: `/tmp/lain-structure-ui-07/evidence.json` reports **PASS, 571
checks and 104 screenshots**, on private Xvfb at 1120 × 780 and 800 × 560. The
reviewer independently inspected 25 actual PNGs from that run, covering every
main destination, the three information presets, normal and compact account
layouts, a 35-account inventory, pending status with a long profile name, empty
onboarding, offline service state and unknown Usage measurements. Earlier runs
03–06 informed the findings and corrections; they do not replace final evidence.

The recorded production Desktop assembly is
`tests/desktop/bin/Debug/net10.0/Lain.Desktop.dll`, SHA-256
`CC16B9858130FC393D28BBDB5068D2CB2183A6E8DCB5BDC26A5215796FBC3FF9`.
This is an in-memory HTTP fixture run using production UI controls/theme. Routed
button events exercise handlers, not OS pointer hit-testing. The reviewer ran no
builds, tests, provider calls, services or user-desktop interaction. Installed
package integrity, native launcher behavior and real data retention are separate
release checks; this review does not claim they are established by these images.
The later monitor-mode proposal is outside this UI-structure acceptance.

## Findings resolved before acceptance

| Finding | Final evidence and outcome |
| --- | --- |
| Accounts initially spent most of the small viewport on its heading and controls. | `3f006b4` puts discovery beside Add account and search beside the filter toggle. The first tile starts at y=181 instead of y=301 at 800 × 560; its quota block and Show details action fit. Active filters remain explicit. |
| Repeated profile state text pushed frequent actions below the small viewport. | `8e55327` consolidates applied/pending state and puts Apply to proxy, Set up Codex client and Edit selection immediately below the selected profile heading. `profiles-800x560.png` shows all three actions, even with a save notice present. |
| Usage Simple initially presented only a filter form at 800 × 560. | `32bfd99` places applied scope and whole-window totals before detailed filters. `usage-800x560.png` now shows Requests, Attempts, Historical errors and Total tokens together before the form. |
| A long applied-profile name could hide pending changes in the footer. | Pending state now precedes the profile name and the status control uses the available width. `pending-long-profile-800x560.png` visibly retains Changes pending while the long name is trimmed. The evidence contains geometry/font-based checks at both sizes. |
| Live and Usage preset changes could discard manually chosen disclosure state. | `d1640b3` and `54cc92d` preserve explicit local overrides. Final checks cover manually opened and closed Live details, Usage overrides, untouched preset defaults and subsequent updates. Live override storage is pruned with retained events. |
| Usage attempt → Models navigation could filter out the evidenced route. | `155110f` prioritizes the concrete route identity. The final run completes this context link and the return-context checks. |

Run 03’s Live resize/follow-output failure is superseded by `75fa762` and the
final viewport checks. Run 04’s gap assertion ran before the paused refresh
interval; a bounded semantic wait now observes the real gap state. Run 06’s
unknown-profile assertion still expected punctuation from the old label; the
screenshot already showed the correct unknown state, and the final semantic
check passes. None of those historical failures is an unresolved product claim.

## Accepted UX behavior

- **One inventory, five destinations.** Accounts starts the application; Models,
  Profiles, Usage and Live stay directly accessible. Providers is discovery
  within Accounts, useful without a credential. No second full account inventory
  or pseudo-discovery model catalog remains.
- **Account detail ownership is coherent.** A closed tile carries every reported
  window, including balance-only windows beside percentages, rest amounts and
  reset information. Clicking reveals account actions and context links. Stale
  exhaustion, overdue resets, unsupported/unknown quota and shared budgets stay
  distinguishable. Neither presets nor ordering invent combined capacity.
- **The first screen is quieter without concealing state.** Discovery has
  secondary weight; Add account is primary. Search and explicit filter controls
  remain available. Pins, grouping and stable ordering work with the 35-account
  fixture; polling does not sort by changing available capacity. Compact images
  retain readable separate windows and show no overlapping account tiles.
- **Profile selection is not application.** The selected profile and the host’s
  applied identity are separately named. Pending saved settings remain visible.
  Apply to proxy names the global operation and its confirmation explains the
  effect on every client; Set up Codex client is a separate export flow. Missing
  applied metadata stays unknown instead of being inferred from local selection.
- **Models distinguishes client identity from provider model and account binding.**
  Menu visibility, enabled state and configured spending policy remain visible.
  Existing fallback selections and paid-fallback consent survive unrelated edits;
  failed policy checks do not claim verified access or a fulfilled spending cap.
- **Usage has one principal breakdown.** Whole-range totals, applied filters and
  coverage remain explicit. Historical error counts and completion-based
  breakdown errors are not silently equated. Performance values retain measured
  versus eligible counts; zero coverage remains unknown, not zero performance.
  Provider quotas are not presented as local Usage history or known costs.
- **Live output is usable at both supported sizes.** The native output scroller
  exposes actual content without requiring a second outer scroll. A single
  request does not need a redundant request picker. Filter-empty, source-empty,
  disconnected, cleared and retention-gap states are distinguishable. Pause
  display does not imply stopping inference; provenance and bounded retention
  remain visible.
- **Presentation controls affect presentation.** All five destinations and the
  same functions remain reachable. Barebones retains required quota/state data;
  Advanced adds local detail. Explicit detail choices survive preset switches.
  Density is independently persisted by area. Final assertions cover focused
  draft inputs, reload, contextual Back, pins/order/grouping persistence and
  absence of host-configuration mutations from view changes.
- **Branding is consistent.** The supplied cat appears alongside lain, with the
  accepted Local AI Gateway wording. The established mint/dark surfaces and
  fonts remain. The five navigation controls and footer stay legible at 800 px.

## Non-blocking refinements

Models’ active context and its Clear model context button could share one row,
reducing unused vertical space in narrow filtered views. The Profiles selector
list could stay top-aligned when Advanced makes its neighboring detail card
longer. These are optional layout refinements; the inspected views retain access
to their content and actions, and neither changes the acceptance above.
