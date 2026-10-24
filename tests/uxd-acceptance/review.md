# UXD-09 visual and interaction evidence

The final targeted correction run passed **8/8 cases and 141 assertions** on
24 October 2026. The latest broad run remains **42/45 cases passed**: its three
Compact Profiles failures were fixed and then covered by that targeted run.
All 45 cases were not rerun on the corrected source. This is scoped synthetic UX
evidence, not blanket visual acceptance or release qualification.

The checked-in [evidence summary](evidence-summary.json) records exact checkout,
desktop assembly, evidence and log hashes; historical failures; all 14 reference
mappings; hashes of the specifically reviewed PNGs; and remaining limits.
Immutable run evidence retains its automatic `visualReview: REQUIRED` values.
Manual observations are separate and do not rewrite the artifacts.

## Executed runs

Every run used owned private Xvfb, Xauthority and an isolated synthetic fixture.
No user display/session bus, real host, provider, credential, external browser
or installed application was used. Builds/runtime were serialized with the
integrator. The final build passed in 11.84 seconds with no errors and 11 CS0649
warnings for unused variation fields in the shared synthetic fixture.

| Run / artifact directory | Source identity | Actual result |
| --- | --- | --- |
| [pass1](../../artifacts/uxd-acceptance-20261024-pass1/) | `87c9b5b` | 37/45 cases passed, 658 passed assertions, 95 image records / 98 PNGs. Six real Usage construction failures; two harness sequencing failures in native typing and Live selection. |
| [pass2](../../artifacts/uxd-acceptance-20261024-pass2/) | `f2f1e88`; dirty harness edits later committed as `bdc4b88` | 14/14 filtered cases, 215 assertions, 31 image records / 34 PNGs. Functional pass still missed unreadable narrow Usage rows; manual PNG inspection found the issue. |
| [final broad (F)](../../artifacts/uxd-acceptance-20261024-final/) | Product base `9319397`, harness checkout `a174c77` | 42/45 cases, 736 passed assertions, 106 image records / 111 PNGs. Only `matrix-Profiles-{Barebones,Simple,Advanced}-Compact` failed at 800 px. This run remains **FAIL**. |
| [profiles-final (P)](../../artifacts/uxd-acceptance-20261024-profiles-final/) | Product base `9319397` + `f5c0fac` (local `0c38932`), fixture `422564f`, harness checkout `5efe8da` | 8/8 cases, 141 assertions, 19 image records / 21 PNGs. Six Profiles level/density combinations at both sizes, Simple/Normal Usage at both sizes, and profile/dialog/native Back passed. |

Image records count page captures; PNG totals also include native X11 root
captures of menus/dialogs. Each directory has verified `SHA256SUMS` for PNGs,
`evidence.json` and `run.log`. Ignored artifacts must accompany a machine transfer.
The final filter was `matrix-Profiles,matrix-Usage-Simple-Normal,profile-viewing`.

## Independently discovered defects and corrections

| Finding | Correction and actual verification |
| --- | --- |
| Usage attached the same header Grid twice: `AttachedToLogicalTreeCore called for Grid but control has no logical parent`. | Owner fix `7a38075`; rendered rows verified in pass2 and final. |
| Usage at 800 px showed only a table header and border pixels despite a passing intersection check. | Fully visible first-account-name assertion replaced the weak check. Owner fix `70a940a`; final and P show the complete first row, P wide shows all three rows. |
| Models used the previous width: missing wide inspector, stale narrow inspector squeezing names. | Owner fix `4d976f0` uses actual arranged width. Final assertions and reviewed 1120 / Advanced-Compact 800 PNGs confirm correct pane state. |
| Pending Connect claimed `Connection saved.`, Kilo retained Codex success, and narrow Cancel was below the fold. Chooser/model labels were centered awkwardly. | Owner fix `ec2a618`; final assertions and chooser/waiting/Kilo/Models PNGs confirm clear notices, reachable Cancel and left-aligned compact rows. |
| Profiles Compact at 800 px retained two panes after earlier fixes read stale outer `Window.Bounds`. | Owner fix `f5c0fac` uses the measured `root.SizeChanged` width. P passes all six variants; all three Compact 800 PNGs were directly inspected and show one inventory pane. Selected detail, owned dialog, focus return and native Back also pass. |

Evidence setup corrections remain distinct from product defects. Native typing
originally delivered `code` in a burst; spaced actual native keys now produce
required `codex`. Live explicitly returns to the narrow inventory before pointer
selection. Lower Accounts now shows the entire Z.ai card before comparison.
Fixture `422564f` aligns Usage summary account scopes with breakdown rows;
P Usage captures supersede the older inconsistent no-activity chips.

## All 14 final reference mappings

All 14 final UX-Pilot PNGs were opened directly from the final manifest under
`/home/tomka/Desktop/lain/artifacts/uxpilot-20261024/`; discarded iterations were
excluded. The app is 1120×780 inside the 1440×1024 export. Outer black margins
are not application whitespace. Public PNG URLs remain in the
[versioned reference record](../../docs/uxpilot-design-results-2026-10-24.md).

F and P below refer to the linked final-broad and profiles-final artifact
directories above. Every listed product image was manually inspected. Repetitive
matrix PNGs are not all visually accepted. JSON contains exact image hashes.

| Frame / final ID | Reviewed product captures | Observation |
| --- | --- | --- |
| 1 Accounts overview · `Rf8aun5CqK1jJDDEsy1R` | F `accounts-simple-normal-1120x780.png` | Multiwindow closed summaries and reserved inspector retain the original cat and dark/mint appearance. |
| 2 Lower inventory before · `e15bU4GJVZiLJqr1AD7h` | F `accounts-lower-before.png` | Entire Z.ai card visible before selection. |
| 3 Adjacent Z.ai · `ZFjD7XKb3sOKSBNNikNQ` | F `accounts-lower-adjacent.png` | Inspector does not cover the account; assertions preserve every card rectangle, list scroll and both scroll anchors through Settings/Back. |
| 4 Provider chooser · `gzASp44CCY795jSZqF7i` | F `connect-provider-chooser-wide.png`, `connect-provider-chooser-narrow.png` | Compact left-aligned provider rows and reachable actions at both sizes. |
| 5 Codex start · `VqOo9QWKoHTuPl2QFBA3` | F `connect-codex-start-wide.png` | Provider-specific browser start separated from waiting; launcher is synthetic. |
| 6 Codex waiting · `k0kGywZOHwQqiJP6M9dc` | F `connect-codex-waiting-narrow.png` | Reopen/Cancel visible, no premature saved notice. Reopen reuses the same operation. |
| 7 Kilo key · `wQVdJZc3s1aOOHiSyLRP` | F `connect-kilo-key-narrow.png` | Name/key without compulsory plan selector; previous success cleared. Successful synthetic handoff clears private input. |
| 8 Connected · `XRRl0bWu3u4yabFGoOVB` | F `connect-codex-result-wide.png` | Bounded completion distinguishes connection and unverified observations; Done/model choice is separate from Apply/export. |
| 9 Models · `XjprbmkRcPW8vOyOPOEr` | F `models-simple-normal-1120x780.png`, `models-advanced-compact-800x560.png` | Compact rows and direct Edit; wide inspector, narrow inventory without stale pane. All 45 Edit actions remain reachable in assertions. |
| 10 Profiles · `tUQuAL5HzEst2g0t69B1` | P `profiles-selected-daily-wide.png`, `profiles-selected-daily-narrow.png` | Viewing Daily and Applied Workspace 2 distinct. Wide model context and separate Export/Client setup; narrow detail has Back. |
| 11 Apply review · `qCI0uheft2XiFWKSBJmQ` | P `profiles-apply-dialog-narrow-x11.png` | Actual private X11 root shows owned review, global scope, delta/restart and visible actions. Native Escape restores trigger focus without applying. |
| 12 Usage · `VFZv56jHJJ2zSdBAQhNG` | P `usage-simple-normal-1120x780.png`, `usage-simple-normal-800x560.png` | Three rows wide, full first row narrow. Totals/scopes agree: 128 requests, 132 attempts, four historical errors, 81,986 tokens. Z.ai/MiMo have no observed activity. |
| 13 Live · `t41xl53MKsTLKcXbKOmb` | F `live-simple-normal-1120x780.png`, `live-selected-output-narrow.png` | Request/output hierarchy, local picker, narrow Back and ephemeral retention visible. Native selection shows output; Pause/Clear cause no host writes. |
| 14 Settings · `O63jRSAvsTNvo54maqIS` | F `settings-simple-normal-1120x780.png` | Three groups, native checkboxes, local density and tray explanation; exactly one footer information selector. |

Additional inspected evidence is hashed in JSON: F
`native-account-context-menu-x11.png`, `usage-partial-known-subtotal-narrow.png`;
P `profiles-{barebones,simple,advanced}-compact-800x560.png` and
`profiles-narrow-return.png`. Partial Usage displays a known 65,000 subtotal,
unknown full total and 110/132 token coverage independently from journal coverage.

Native controls and responsive task pages adapt the design. The reserved wide
Accounts inspector reduces inventory width; narrow details replace inventory
and return to it. These observations do not accept every typography, contrast
or uncaptured state.

## Executed interaction scope

The broad run executes six pages × three information levels × two densities ×
two sizes (1120×780 and 800×560), plus nine focused cases. Assertions cover
shell/footer navigation, every reported closed quota-window kind, 45 model Edit
actions, account geometry/scroll restoration, Connect start/wait/key/result,
profile selection versus Apply/export, partial/unknown Usage, narrow Live output,
local preferences, and Monitor with/without history versus offline Gateway.
Information level preserves window size and does not create host capabilities.

Native XTEST on the owned display establishes:

- Pointer focus and exact `codex` typing, Ctrl+A/Backspace, Tab reaching account
  Details, Enter opening it and Escape restoring its trigger.
- Right-click, visible menu and Shift+F10 exposing identical account commands;
  actual native menu captured from the X11 root.
- Pointer opening owned Apply review, native Escape and trigger focus return at
  both sizes, native narrow Back restoring focused Daily inventory. Preview does
  not apply or export.
- Pointer selecting a narrow Live request after explicit Back, with output
  visible in the initial viewport.

Most matrix navigation and other focused actions use routed/control events.
The native subset does not establish complete keyboard coverage.

## Explicit remaining qualifications

- Full screenreader/accessibility, every editor's keyboard traversal, complete
  modal focus containment, clipboard, compositor/window-manager and installed
  tray behavior remain unqualified.
- The central suite demonstrates expert-editor access, not a field-by-field
  retention/policy round trip. Dedicated Profiles, Connect and Settings suites
  remain separate evidence.
- Stale/deleted command targets, complete unsupported/failure/late-completion
  Connect flows, Apply conflict/recovery/install and backend/installer behavior
  are not established by this central visual run.
- Usage/Live partial, error and restart captures alone do not prove complete
  polling, draft, filter, selection-preservation or retention semantics.
- The older broad `tests/desktop` suite retains historical Expander-specific
  account assumptions. Its corrected navigation contract does not mean the old
  suite was rerun or passed.
- No real provider/login/entitlement, inference or host mutation was tested.
  Complete UX and release acceptance require the separate qualifications above.
