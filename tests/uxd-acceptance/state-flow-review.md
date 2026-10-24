# UXD-22 state-flow acceptance

Source prepared against c3db87e and UXD-17–21 deltas, 2026-10-25.
**Intermediate native runs executed; final required cases remain open. No overall acceptance PASS.**
The integrator owns the serialized, 2-GiB-constrained build/runtime slot.

Chosen variants: Draft B (direct navigation, persistent return/discard notice),
Simple Live A (direct Live destination). The unchosen alternatives are history.
The original cat, Mint controls, Accounts master/detail, long quota bars,
Models/Usage tables, fixed Apply review B and narrow Live list A remain baselines.

| Contract | Prepared evidence | Remaining qualification |
| --- | --- | --- |
| UXD-17 | `native-state-draft` in both sizes: native Enabled edit, Profiles, persistent banner, return/focus, changed revision, disabled Save, explicit discard, mutation absence | Native information-level/density changes are prepared; actual scroll/non-dismissal must be observed |
| UXD-18 | `native-state-first-snapshot-{data,empty,error}` in both sizes; held HTTP then supplied response. `native-state-account-refresh`: native A refresh then B selection, delayed A error, values retained, request identity | Native late-success A merge and stale B ignore are now prepared; owner smoke verifies all known windows |
| UXD-19 | `native-state-model-menu` in both sizes: pointer, right click, Shift+F10, Escape/focus, same command set, no mutations. Owner `--model-identity-smoke`: captured command ID/revision races, deleted ID draft, references, filter clear, exact Save | Native no-match/reset prepared; vanished selection/revision delivery while menu open remains structurally covered |
| UXD-20 Connect | `native-state-connect`: Monitor/Core prerequisites before login, deliberate Gateway review, no mode mutation | Gateway Core unavailable and provider-source return now prepared; direct preflight guard/source fixture lists 11 cases |
| UXD-20 Setup | `native-state-setup`: native invalid-parent correction, Prepared without destination files, explicit install into own test folder, ordinary isolated Codex sentinel unchanged, no Apply | Native Browse/Escape/focus and invalid executable now prepared; Cancel and target change remain to qualify |
| UXD-21 | `native-state-apply`: fixed review, Cancel/focus, stale diff disabled, refresh/new confirmation, commit response loss plus status failure, later status read without retry | Native late preview target change/double click, stale/incomplete status and exact list/detail/shell texts are now prepared |

All new native cases run separately at 1120×780 and 800×560, Simple/Normal.
The inherited matrix covers Barebones/Simple/Advanced × Normal/Compact layouts.
Its Simple navigation and former modal Apply expectation are updated to the
explicitly selected behavior. Existing prior reports are historical, not reruns.

Requests include ordered method/path/body/configuration revision/confirmed active
identity in `evidence.json`. Every screenshot remains `visualReview: REQUIRED`.
The harness never interprets a screenshot as accessibility qualification.
Root X11 images capture popup surfaces; page images use Avalonia rendering.
XTEST is real native event injection but is not a human or screenreader audit.

The private runner creates Xvfb, Xauthority, its own dbus-daemon, private HOME,
XDG and LAIN_HOME. There is no user-display or inherited-session fallback.
The synthetic executable is never executed; export and install are in-memory
HTTP plus the existing private artifact installer. No provider, inference,
credential discovery, normal Codex configuration or real service is used.

Each completed run must add commit/assembly hash, source/WIP provenance,
filter, fixture, exact input method and viewport; classify structure/native/
image evidence separately. PNGs must be opened and reviewed by the test owner.
Open mandatory cells prevent an overall completion claim.

Follow-up adds raw XTEST input trace. First-snapshot cases have native X11 images
but do not claim pointer/keyboard input unless recorded. `LAIN_TEST_SCALE=1.25`
sets supported Avalonia global scaling; images record actual `RenderScaling`.
A separate targeted scaled run is required before claiming enlarged UI coverage.

## Intermediate runtime evidence

`state-flow-evidence.json` records exact manifests, assembly hashes, historical
results and individually opened PNG hashes. R1: 18/24 functional cases, 374
assertions, 64 page images plus private X11 captures. R2 targeted corrections:
7/8 cases, 199 assertions, 24 images. These are separate runs, not a combined
blanket pass. R2 confirmed native menu entry parity/filter reset, Connect
prerequisites and retained A while B edit is blocked/B save succeeds. The one
remaining R2 failure was a fixture selector expecting an inventory Edit button
after narrow navigation correctly retained selected detail; source corrected,
rerun pending.

Image inspection found two real product issues: empty programmatic model popup
(fixed by owner `2f51a69`) and stale saved/proxy metadata after status recovery
(fixed by owner `77a6134`, final runtime still pending). An earlier assertion
requiring disabled stale Save was corrected to a native Save attempt proving
local rejection and retained input. Setup/installer, Browse/Escape, account
late-success/error A→B, preview target switching, unknown/old/incomplete status
and confirmed runtime_busy rejection all executed in both sizes in R1.

The external build project lives in the private SSD qualification cache because
legacy test locks contain no RID target. It retains the original assembly name,
source Compile includes and resource name, references the original desktop
project, and creates only an external lock. Repository locks remain unchanged.
Two production builds succeeded after the owner's Avalonia12 menu API fix.
The separate owner18/19 structural harness had a removed-API compile failure;
no structural runtime PASS is claimed until its corrected fixture is run.
