# UXD-09 visual and interaction evidence

Review started 24 October 2026 against source baseline `ec68062`. The initial
reference review below is preserved; integrated runtime evidence follows here.
The supplied stabilization evidence contains 26 assertion descriptions but no
assembly hash or source commit. Its images establish the visible captured state,
not the current checkout.

## Integrated runs and independently discovered defects

All execution used the runner-owned private Xvfb and in-memory fixture. No
user display, session bus, real host, provider, browser or credential was used.

| Run | Product commit / assembly SHA-256 | Executed result |
| --- | --- | --- |
| `artifacts/uxd-acceptance-20261024-pass1` | `87c9b5bd29a6e2fa98052138637e4957df893533`; `99C2B52094EA9BE3B82A19204FC7374E840740DDBE59EA497F8EB7808A0FBC5E` | 45 cases, 658 passed assertions, 95 image records. Six Usage variants failed on a real Avalonia logical-parent exception; native typing and Live selection had fixture sequencing errors. |
| `artifacts/uxd-acceptance-20261024-pass2` | `f2f1e885dd63d7f2a490022fbb7af3e1555f9edd` (same product as source plus Usage `7a38075`); `0008D5FD61AF45694212BDDBED29E4AAAFA0542168299EE91BD568B47114790A` | Focused 14 cases, 215 assertions, 31 image records passed. This does **not** mean visual acceptance: narrow Usage still had no readable result row. Harness edits were uncommitted during this diagnostic run and are recorded in `bdc4b88`. |

Native pointer/keyboard evidence in pass2 establishes typing the full `codex`,
Ctrl+A/Backspace, Tab to Details, Enter to open, Escape with trigger focus return,
and right-click/visible-menu/Shift+F10 parity. Each key now waits for actual UI
processing. The first burst-input result was `code`; the expected text was not
weakened. Live now explicitly returns to its narrow inventory before native
pointer selection. Native Apply pointer → owned dialog → Escape → trigger focus
already passed in run1, with actual X11 root images showing the dialog.

Independent image review found defects that passing geometry checks missed:

| Finding | Evidence and action |
| --- | --- |
| Usage row construction exception | Run1 shows `AttachedToLogicalTreeCore called for Grid but control has no logical parent`; no rows. Owner fixed double attachment in `7a38075`; run2 confirms rendered rows. |
| Usage800 has no readable first row | Run2 `usage-simple-normal-800x560.png` shows only table header and a few border pixels. The former intersection assertion was insufficient. Changed to fully visible first account-name text; source owner supplies `70a940a`. |
| Models responsive state follows previous width | Run1 wide Simple lacks its reserved pane while `models-advanced-compact-800x560.png` retains a 320px empty inspector and squeezes names. Owner confirmed root resize callback used stale outer bounds; source correction pending retest. |
| Profiles800 normal/compact breakpoint | Run1 still shows two columns. Source `60c9c15` handles 760px normal content; compact content is772px and needs a density-independent boundary. Both densities now have explicit one-pane assertions. |
| Pending Connect claims success | Run1 `connect-codex-waiting-narrow.png` says `Connection saved.` while authorization is waiting. Next Kilo form retains the prior Codex success notice. Owner `ec2a618` clears/gates notices and keeps Cancel above optional guidance; retest pending. |
| Chooser and model labels misaligned | Run1 chooser has90px fullwidth cards with tiny centered provider buttons; model names centered under left-aligned header. `ec2a618` supplies compact left-aligned rows/labels; retest pending. |

Accounts before/after selection kept every measured rectangle and both scroll
anchors across Settings/Back. Their initial lower-reference setup revealed only
Z.ai's title at the fold; the next capture explicitly scrolls to inventory bottom
and requires the entire Z.ai card before comparison. This is an evidence setup
correction, not a product movement bug.

Reviewed native product images show correct separate Viewed Daily/Applied
Workspace2, visible distinct Export/Client setup, full wide profile rows,
readable modal actions, three calm Settings groups and one bottom-right
information selector. Simple/Normal wide and narrow frames plus targeted states
were inspected at full size. Some repetitive matrix variants remain only
automatically checked; they are not individually marked visually passed.

All 14 final UX-Pilot PNGs were opened directly from the final manifest under
`/home/tomka/Desktop/lain/artifacts/uxpilot-20261024/`. Discarded generator
iterations were excluded. The app is 1120×780 within the 1440×1024 export;
black outer margins are not application whitespace.

## Final reference coverage

| Frame / final ID | Supplied product PNG | Independent result / next evidence |
| --- | --- | --- |
| 1 `Rf8aun5CqK1jJDDEsy1R` | `accounts-simple-1120x780.png` | Reserved adjacent inspector visible. Repetitive technical source rows and account-level empty-window badge: UXR-01. |
| 2 `e15bU4GJVZiLJqr1AD7h` | No lower Z.ai before image | New lower-before/after captures must use identical fixture and scroll offset. |
| 3 `ZFjD7XKb3sOKSBNNikNQ` | `account-details-1120x780.png`, `account-details-800x560.png` | Wide adjacent inspector and separate narrow task page visible; this pair does not prove lower-inventory geometry or keyboard focus. |
| 4 `gzASp44CCY795jSZqF7i` | None | Provider-first chooser, search/cancel and native initial focus pending. |
| 5 `VqOo9QWKoHTuPl2QFBA3` | None | Codex initial state pending; real sign-in is outside this fixture audit. |
| 6 `k0kGywZOHwQqiJP6M9dc` | None | Waiting/reopen/cancel, late completion and focus pending. |
| 7 `wQVdJZc3s1aOOHiSyLRP` | None | Kilo name/key without compulsory plan/model picker pending. |
| 8 `XRRl0bWu3u4yabFGoOVB` | `connect-result-1120x780.png` | Honest unverified quota/inference, but loose full-page completion differs materially from bounded card: UXR-04. |
| 9 `XjprbmkRcPW8vOyOPOEr` | `models-1120x780.png`, `models-800x560.png` | Tall cards show about 4½ wide/2¼ narrow rows; compact table/detail pending: UXR-02. |
| 10 `tUQuAL5HzEst2g0t69B1` | `profiles-1120x780.png` | One-profile/one-model fixture insufficient; tiny centered model pill and hidden maintenance actions: UXR-03. |
| 11 `qCI0uheft2XiFWKSBJmQ` | `apply-preview-800x560.png` | Unknown baseline/global restart visible. Full-page review differs from proposed owned dialog; narrow ownership/focus pending. |
| 12 `VFZv56jHJJ2zSdBAQhNG` | None | First results, partial totals/coverage, local details and narrow layout pending. |
| 13 `t41xl53MKsTLKcXbKOmb` | None | List/output, native selection, Pause/Clear semantics and narrow Back pending. |
| 14 `O63jRSAvsTNvo54maqIS` | None | Three groups, native checkbox readability, tray explanation and narrow layout pending. |

The [14-frame result record](../../docs/uxpilot-design-results-2026-10-24.md)
preserves all public image URLs. Initial product PNGs are in
[`artifacts/desktop-connect-stabilization-20261024/`](../../artifacts/desktop-connect-stabilization-20261024/).
Ignored local artifacts must accompany a transfer to another machine.

## Findings communicated to owners

| ID | Visible issue | Owner / next verification |
| --- | --- | --- |
| UXR-01 | Simple repeats source IDs, timestamps and balances. Empty free credits produce a red account-identity badge despite remaining independent windows. | UXD-01/02: preserve all windows, disclose provenance locally, scope empty status to its window; recheck new Simple images. |
| UXR-02 | Models uses high bordered cards plus permanent filter chrome, reducing visible rows substantially. | UXD-03/04: compact native rows, local filters, direct Edit, narrow detail and long-name evidence. |
| UXR-03 | Model links lack context; Export/Client setup are hidden together under detail. | UXD-05: compact contextual rows, distinct visible maintenance actions and 3-profile fixture. |
| UXR-04 | Completion uses a full-page success strip plus loose connection lines instead of one focused group. | UXD-03: bounded result with honest observed quota versus stored credential and optional Models. |
| UXR-05 | Supplied evidence uses routed events/focus assertions without native input. | UXD-09: owned Xvfb XTEST, actual menu/dialog screenshots; no screenreader claim. |

Original cat, dark green/black palette, mint actions and native controls are
visible in the reviewed product captures. This limited observation does not
accept all typography/contrast or uncaptured states.

## Expert-field retention checklist

| Existing family | Required reachable place / evidence |
| --- | --- |
| Every quota window, balance, unknown value, reset/freshness | Closed summary, with no invented denominator. |
| Identity, generation/purposes, source/owner/provenance, precise timestamps | Account detail and local technical disclosure; exact field audit after integration. |
| Alias/upstream/provider/account/catalog route, enabled/visible | Direct model Edit; long-name 800×560 case. |
| Model/profile policy, paid-fallback permission, ordered fallback routes | Full policy editor and meaningful preservation round trip. |
| Viewed versus applied profile/revision, default and model set | Profile inventory/detail; selection issues neither Apply nor export. |
| Global delta, affected accounts/clients, unknown baseline, revision conflict/recovery | Real synthetic preview endpoint, owned review, refreshed reconfirmation. |
| Usage attribution, token components/subtotals/coverage, time zone/exclusive end | Local filter/group/attempt disclosures; null distinct from measured zero. |
| Live evidenced identities, linked picker, event/time/error/source/retention | Local request/event detail; unproven identities remain independent. |
| Information level, density, runtime intent, tray/notifications | One footer mode selector; local preferences; separate explicit runtime action. |

## Not yet executed

- Six pages × three levels × two densities × two viewports, followed by
  independent PNG review.
- Lower account geometry, independent list/detail scroll, Settings/Back,
  narrow focus return, stale/deleted menu targets.
- Connect chooser/start/wait/key/result, unsupported/failure/late completion,
  draft retention and private input clearing.
- Full policy fields, command parity, unknown baseline, stale Apply,
  failure/recovery and model/contextual Back links.
- Usage partial/null/zero, draft/polling, initial rows and drillthrough/Back.
- Live native output selection, filtered selection, polling/source/retention,
  Pause/Clear and narrow Back.
- Monitor with/without history; offline configured Gateway versus no setup;
  information mode must not create capabilities or resize the window.
- Native Tab/Enter/Escape, visible focus, pointer hit-testing, menu parity,
  modal focus containment/return. Complete accessibility remains unqualified.

No real provider/login, installed tray or release-ready claim is made. UXD-09
remains open until required cases have evidence or explicit bounded limitations.
