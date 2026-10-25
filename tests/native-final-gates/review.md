# Native editor gates, 25 October 2026

The four target editor cases have **eight open product failures**: Models and
Profiles, each at 1120×780 and 800×560, each after Save and Cancel. Actual
`FocusedElement` is null after remounting. The expected current stable trigger is
`Edit selected model access <alias>` for r1, or `Edit selection` for p1. No harness
code sets a control's focus to hide this. The product owner received the source
sites, IDs, exact assembly hash and native request/focus evidence.

Across the qualifying runs, **272/280 assertions passed**. Eighteen model inputs
and 95 profile inputs per size are reached with actual XTEST Tab traversal;
Shift+Tab works. Save and Cancel remain fully reachable through native scrolling.
Cancel preserves configuration revision and all original fields; Save changes
only the intended name on exact r1/p1 while retaining account/route order,
provenance, flags, default and complete spending/fallback policy. Reopen shows
the appropriate saved value. Neither flow applies a profile nor exports files.

The first run's two Models cases are separate **harness startup errors**: pointer
navigation was sent before initial layout. The personally opened failure PNG
remained on Accounts. The next harness waits for the genuine initial snapshot,
laid-out navigation and destination controls; both focused model reruns then
completed. Historical errors and binary hashes are retained, not rewritten.

| Run | Result | Evidence |
| --- | --- | --- |
| `source-run` Models only | Two startup errors | Preserved separately from assertion results. |
| `source-run` Profiles only | 212 passed, four focus-return failures | Both sizes; 216 assertions. |
| `models-r2` | 60 passed, four focus-return failures | Both sizes; 64 assertions. |

The first summary row denotes two startup errors separately from the 216 Profile
assertions; errors are not converted to assertions or additional product bugs.
`evidence-summary.json` contains exact manifests, hashes, source/WIP identity,
assembly identity, action focus records and five personally reviewed PNGs.

Actual runtime text samples use Muted `#89938E` on `#050706/#08100D/#0C1511`
with solid-layer ratios **6.38/6.08/5.86:1**, and Ink `#EDF2EF` with
**17.84/17.01/16.40:1**. Recorded cumulative visual opacity is 1 for these samples.
These are actual resolved brush/ancestor measurements, not copied reference
ratios. Native screenshots show readable labels and visible name-field focus.
They do not certify all contrast, glyph antialiasing, screenreaders or compositors.

The free-only toggle really disables paid permission, but its caption was below
the captured viewport: **no disabled text contrast result is claimed**. The next
harness revision uses native Tab to its following Check-spending-policy action
before measuring/capturing, and asserts the disabled control's viewport bounds.
That small harness correction is not yet executed; it accompanies the planned
product-focus-fix rerun instead of adding a redundant baseline run.

Restore and both builds exited zero; production locks remain unchanged. The
first compile took 25.41 seconds; the readiness-only compile 4.83 seconds. Both
reported zero errors and 28 warnings (27 unused linked fixture variation fields,
one obsolete bitmap save overload). Both runtime processes exited 1 for the
retained failures. All owned runtime scopes are terminal/inactive; Xvfb and own
session buses were cleaned up. GUI and builds used CPU150%, MemoryHigh1536M,
MemoryMax2G, Swap256M, IOWeight10, nice15/ionice3 and an owned SSD temporary folder.

No product source, installed service, ordinary Codex configuration, real account,
provider endpoint, user display or user session bus was modified or used.
