# Desktop smoke test on private Xvfb

Run from any directory:

```sh
./tests/desktop/run-private-xvfb.sh
# Or keep evidence in an explicit directory:
./tests/desktop/run-private-xvfb.sh /tmp/lain-desktop-evidence
```

Requires .NET 10 SDK, Xvfb and xauth. The script builds the integrated
`src/Lain.Desktop` project through a project reference; no copied UI source or
substitute theme is compiled. After an existing build, use
`LAIN_DESKTOP_SKIP_BUILD=1 ./tests/desktop/run-private-xvfb.sh`.
NuGet restore follows the repository's package configuration.

The runner unsets inherited DISPLAY, WAYLAND_DISPLAY and the desktop D-Bus
session before doing anything else. Xvfb chooses a free display atomically with
`-displayfd`, disables TCP, and requires a random Xauthority cookie. Only this
process's display is passed to the harness. An isolated temporary HOME,
XDG_RUNTIME_DIR and LAIN_HOME prevent real local credential discovery. The
harness refuses to initialize Avalonia unless the runner's live Xvfb PID and
display marker are present. There is no native desktop fallback. EXIT/INT/TERM
cleanup terminates only the child Xvfb and removes its private temporary files;
a 360-second timeout bounds the harness.

Production `App.Initialize()` supplies theme resources/styles; its framework
startup is deliberately not invoked because it constructs the real local API
client. The test's MainWindow, UsageView, ProviderCatalogView and InspectorView are the actual production classes.
Every HTTP request goes to an in-memory handler with an explicit allowlist and
no Authorization header. Five baseline accounts plus a 35-account density scenario, 45 routes, three profiles and persistent
usage totals provide realistic density. All account/import/route/profile/login
mutations affect that handler alone. Codex export writes fixture content only
inside the chosen evidence directory, without launching anything.

Coverage includes account add/disable/import, revisioned route/profile saves,
profile apply, Codex export, login completion/cancel, null token values, API
errors/recovery, stale offline state, five-second Usage polling, input focus
across polling, detach cancellation and window close/reopen. Every captured
state is rendered at both 1120×780 and 800×560, with actual client size asserted.
`evidence.json` records checks, requests, image names, the production assembly
path and SHA-256. `run.log`, `xvfb.log`, `failure.txt` (on failure), PNGs and fake
export files stay in the evidence directory.

Button interactions raise Avalonia routed Click events. These validate actual
handlers and enabled-state transitions, not physical pointer hit-testing,
complete keyboard tab order, desktop dialogs, clipboard integration or browser
authorization. Input focus assertions do not replace keyboard accessibility
review. Provider guidance covers no-account states, honest unknown budgets and limits,
free/trial filtering, explicit route/plan setup, masked credentials, callback-only
link actions and catalog outages. Inspector fixtures cover evidenced conversation
groups, unproven identities, conflicting retry parents, success/active/error states,
reasoning/output/tool chunks, local filtering, pause/manual refresh, source restart,
clear, outage/recovery and detach cancellation. Both pages are rendered at both
viewport sizes.

PNGs require independent visual review for clipping, density and theme
acceptance. This fixture cannot establish real host/provider behavior.

The UI structure navigation checks use the capability/information-level matrix:
Gateway Barebones has Accounts/Profiles/Usage, Simple adds Models, and Advanced
adds Live; other allowed destinations remain in More. Monitor exposes Accounts
and only actually available history. They also verify
Accounts at startup; one nested provider catalog and one account-method chooser;
all quota windows including pure balances/unknown windows in closed tiles;
Barebones/Simple/Advanced and independent Normal/Compact section density;
unsaved account/model/profile controls and focus across reload and Settings/Back;
authoritative applied profile metadata; one Usage breakdown with unchanged
measurement coverage; contextual drillthrough/Back; and singleton, no-match,
empty-source and retention-gap Live states. Pins, order, sort, grouping, mode and
density are reloaded by a new production window. Search/status filters survive
navigation/reload/mode changes in one window and intentionally reset for a fresh
window. A quota availability change verifies stable inventory ordering. The
35-account scenario includes disabled accounts and captures both required sizes.
These view changes are also checked against unchanged configuration/revision and
an unchanged in-memory mutation request count.

For the narrow header/account-order regression, reuse this same runner and fixture:

```sh
./tests/desktop/run-private-xvfb.sh /tmp/lain-account-sort-evidence --account-sort-smoke
```

This entry renders both required sizes, checks the leftmost logo and labelled
Back/Settings order, distinguishes current/stale/unknown five-hour windows from
weekly quotas, verifies threshold colors, holds account IDs through quota reload
and passive polling, then updates the rank explicitly and reloads the sort
preference in a new window. All requests remain in the in-memory fixture; no
provider refresh or host configuration mutation is involved. Frozen rank IDs are
window-local; a new window computes its first rank from its current quota snapshot.

Focused provider/account setup coverage uses the same private runner:

```sh
./tests/desktop/run-private-xvfb.sh /tmp/lain-provider-setup --provider-setup-smoke
```

It checks five synthetic Groq/Gemini/NVIDIA/Cloudflare/OpenRouter accounts, neutral unsupported/unverified presentation, a bounded OpenRouter key cap, connection-first credentials, optional unchecked model selections, private key controls, Simple/Barebones details, custom names, one multiple-model account request, and separate same-account monitoring access including cancel. All requests use the in-memory fixture; no real account, browser, quota endpoint or inference is contacted.
