# Independent acceptance

The first Linux delivery and the requested MeowChan-aligned desktop redesign are
accepted at the documented scope. The redesigned Semi/Ursa desktop passed 63
fixture workflow assertions and independent review of 30 private-Xvfb renders,
then an installed-binary real-host Usage/poll/close/reopen check on private Xvfb.
Earlier native desktop evidence below is historical and describes the superseded
interface. All redesign GUI tests used isolated Xvfb displays.

Synthetic checks do not establish real-provider compatibility; selected reference
routes do not qualify every model, and real owned OAuth renewal remains untested.

## Isolation baseline

At the start of W11, the existing `cliproxyapi.service` was active with PID 1882
and start time `2026-10-23 13:32:05 CEST`. Its binary is the existing personal
`easycliproxyapi/cpa-core/cli-proxy-api`. The service status was read only.

`tests/acceptance/legacy_guard.py` captured SHA-256 hashes and modes for 25
existing binary/config/auth files in a private mode-0600 snapshot outside the
repository. No credential values, filenames identifying accounts, or auth-file
hashes are published. Compare immediately after integration using the same
snapshot. Capture/compare does not consume the legacy management usage queue.

```sh
python3 tests/acceptance/legacy_guard.py capture --snapshot /tmp/lain-legacy-before.json
python3 tests/acceptance/legacy_guard.py compare --snapshot /tmp/lain-legacy-before.json
systemctl --user show cliproxyapi.service -p MainPID -p ExecMainStartTimestamp -p ActiveState
```

The snapshot command refuses to overwrite an existing baseline. A changed auth
file must be investigated: autonomous legacy refresh is possible, but does not
justify claiming unchanged contents. File hashes alone do not prove process
continuity; PID and start time are separately compared. No legacy credential
rotation, restart, configuration write, or queue consumption is authorized.

## Acceptance ledger

| Gate | Required independent evidence | Status |
| --- | --- | --- |
| A provenance | Manifest-bound source/build/license and private owned processes/ports | Fresh-worktree 1463-input source manifest passed; exact final binary checked |
| A identity | Two distinct credentials for one provider, strict HTTP pinning on 429/deactivation, explicit pool behavior | W2 strict/pool and distinct-credential tests passed; actual Codex and Command GLM attempts retain selected account/route identity |
| A durability | Dispatch persisted before send; sync completion usage; restart/replay; write failure, crash and unknown coverage | W2 final suite + independent Core crash/auth + 13 SQLite tests passed |
| A protocol | Nonstream, SSE, harmless tool + continuation, cancel; synthetic retry counted as one request and multiple attempts | Chat, Responses translation and native Messages synthetic tests passed; actual Codex and Command GLM Responses tool/continuation passed |
| B host | Unauthorized local API rejected, revision races rejected, failure distinguishes desired/applied revision | HTTP auth, concurrency, pending revision and failed-apply restart guard passed |
| B data/privacy | SQLite restart and dedup; no headers/credentials/payloads in usage/export/errors; unknown amounts serialized null | 13 SQLite tests, journal/public-API privacy checks and export allowlist review passed; actual failed attempts retain null usage; installed usage survives systemd restart |
| B accounts/quota | API/Codex snapshot import, no refresh token; separate same-provider accounts, isolated quota failure/last-good | Read-only API/access-token import and account attribution passed; W6 executed 35 synthetic quota assertions including isolation/last-good; actual Command quota observed; real owned OAuth renewal untested |
| C desktop | Actual Linux window, clickable account/model/profile/usage flows; closes without stopping collector | Semi/Ursa redesign passed 63 workflow assertions, 30 reviewed Xvfb renders and installed real-usage/poll/close-reopen checks; real OAuth browser flow untested |
| D harness | Isolated Codex config/helper/model menu and harmless actual request; no existing configuration changes | Actual Codex profile `lain` used `gpt-6-luna`, catalog has 45 configured models, one request/81,986 tokens persisted; normal Codex configuration unchanged |
| D consistency | CLI/UI/skill consume same host snapshot and bounded queries; no duplicate collector | Redesigned installed GUI/CLI/API agree on 1 request and 81,986 tokens; closed-window host stays ready; skill delegates the same bounded CLI |
| E delivery | Fresh staged install outside cwd, start/stop/restart, update/rollback, uninstall preserving data | Actual staged Host/Core/CLI, usage, restart, same-artifact update/rollback and data-preserving uninstall passed; actual installation/systemd persistence and redesigned installed Xvfb checks passed; desktop-only update preserves backend/service identities |
| Real providers | Explicitly authorized minimal Codex and Command Code stream/tool continuation, usage attribution and persistence | Codex Responses + Command GLM Responses tool/continuation passed; earlier Command GPT failures retained; real OAuth renewal untested |
| Final isolation | Same existing service PID/start and unchanged legacy binary/config/auth hashes, or documented attributable exception | Installed-run report confirms original PID/start unchanged; persistent binary/config/auth additions, removals and changes are zero; only temporary auth-file churn |

## Reproduction and review rules

Run the legacy guard unit test with:

```sh
python3 -m unittest discover -s tests/acceptance -p 'test_*.py' -v
```

Record the integrated commit, artifact hash, command, observed exit code and
relevant result for each gate. Tests that only assert a fake fixture's own
behavior, direct SDK pinning, in-memory DTO examples, or successful text output
cannot substitute for the end-to-end boundaries listed above. Unrun checks stay
pending; partial implementations and unsupported adapters stay explicit.

## Executed independent evidence

The HTTP probe passed against the integrated Host DLL observed with main at
`63ba6a4d2d830256d37c43e50c7031766f995ea4`. The tested DLL SHA-256 was
`9bbefd0b4945cd0726a37c6508c88761033afabcd8701adc6348f4ec9246d5ad`.

```sh
python3 tests/acceptance/host_probe.py -- dotnet /absolute/checkout/src/Lain.Host/bin/Debug/net10.0/Lain.Host.dll
```

Exit 0: unauthorized, invalid and inference-audience tokens were rejected by
management; two competing writes with one revision produced one success and one
409; stale revisions were rejected; unapplied desired changes remained distinct;
configuration/accounts/usage/attempts/quotas exposed no synthetic credential or
host token; invalid/range-exceeding usage filters returned 400; a second host
could not acquire the same state; credential files had private permissions.
The probe used temporary own state, stopped its child, and made no provider calls.
Sandbox networking initially rejected the run; it passed after the explicit
loopback test permission. No permission failure is counted as a product failure.

The installer fixture `python3 packaging/test-install.py` independently exited 0
for arbitrary working directory and arguments, private permissions, update backup,
and checksum rejection. Executables in this fixture are stubs; this does not
qualify a real installed product or actual rollback.

A later read-only legacy service check still showed active PID 1882 and the same
`13:32:05 CEST` start time. Midpoint file comparison found only changing `.tmp`
files in the active legacy OAuth directory; all persistent auth files, config and
binary remained byte-identical. The guard now reports persistent and full-file-set
results separately; temporary-file churn does not become an invented claim that
the entire directory was unchanged. The final installed-run comparison is recorded below.

Independent code review identified and escalated runtime-file/apply races and
missing token-component values becoming zero. The Host now serializes mutation
and stops the own Core before file writes, with runtime validity/revision checks.
The Core presence fix and final protocol/durability results are recorded below
against the exact accepted artifact. The Codex external-profile format was
queried: W5 supplied an isolated run of installed Codex 0.162.1 confirming
`--profile lain` loads `lain.config.toml`; no nested `[profiles.lain]` is needed
for that tested build. This is peer evidence, not a second independent live run.

The independent Core auth/crash probe exited 0 on snapshot artifact SHA-256
`683c610548b185669c1a638fb27dba6eb239ef7c08f1b84f58f36f44655d82ad`:
management and inference tokens were mutually rejected at the other audience;
a SIGKILL after the first stream output preserved the already fsynced dispatch
and source identity through restart; no terminal usage was fabricated; a later
request was recorded normally. This artifact was **not accepted as the full
synthetic gate**: W2 separately found completion-marker ordering failure, requiring
a new build and repeat checks. Final own-login code is also not in this artifact.

```sh
python3 tests/acceptance/core_probe.py --core /absolute/lain-proxy --report /tmp/lain-core-independent.json
dotnet test tests/usage/Lain.Usage.Tests.csproj --no-build --no-restore --nologo -v minimal
```

W11 independently built and ran the usage suite: **9 passed, 0 failed, 0 skipped**.
These include restart/replay, kill-before-transaction-commit, pending unknown usage,
and time-zone boundary assertions. The test runner required local socket permission;
its sandbox abort was rerun successfully and is not counted as a product failure.

## Final synthetic Core gate

The consolidated final candidate is SHA-256
`8edf8abf666e3c15f1a04c20c841a969d35229f09e7de5265755ab704cf9dfc0`.
W2 retained that exact artifact and all **15 synthetic checks passed**, including
Chat/SSE/tool continuation, Responses translation, native Anthropic Messages
SSE/tool continuation, omitted-token null semantics, strict 429 and disabled-account
binding, explicit pool retry, credential lifecycle, cancellation, startup failure,
crash, and dispatch/terminal journal write failures. W11 inspected the report and
independently reran auth and kill-after-dispatch/restart against the retained
artifact: **both passed**, and its hash was unchanged before/after execution.
The committed [independent report](../tests/acceptance/evidence/core-final.json)
contains no credentials or payloads.

A fresh W11 checkout merged from main passed `python3 scripts/build-core-verify.py`:
**1463 pinned inputs verified**. An earlier missing ignored upstream template
was found independently and corrected before this pass. This gate permits the
authorized short real-provider probes; it is not a real-provider/desktop/package
acceptance or a claim of independently proven real OAuth renewal.

## Final Host, persistence and staged package evidence

The expanded HTTP probe passed with Host DLL SHA-256
`47d119af3bc17a1d081697e59e918a5405f6bf8462a81392a4e2997119cea09d`:
in addition to the earlier checks, a failed unqualified profile apply kept the
previous applied revision, invalidated its runtime generation, remained not-ready,
and could not be restarted through either service action.

Independent review found a second coverage gap: replacing a journal source UUID
between clean polls could otherwise hide its unread tail. Migration 002 records
source transitions and time-bounded uncertainty in the same SQLite transaction as
events and the cursor. Overlapping filters cannot erase that gap. After a forced
fresh build, W11 independently ran **13 tests: 13 passed, 0 skipped**, including
empty/nonempty replacement, same-source restart, rollback without phantom gaps,
and v1-to-v2 preservation of existing usage/cursors.

The first actual package failed to start from working directory `/`. The corrected
Host pins its content root to its executable directory. Two subagent rerun tools
stalled before returning execution sessions; the integrator therefore ran the
same W11 probe in the foreground. The corrected package passed all seven check
groups. W11 reviewed the report and the frozen artifact hashes independently.

The [staged-package report](../tests/acceptance/evidence/package-final.json) records
fresh private installation, actual installed Host → bundled Core → synthetic
provider inference, exactly one request/attempt with 18 tokens, installed CLI/API
parity, preserved usage through stop/update/restart and rollback/restart, explicit
offline CLI failure, and uninstall preserving credentials and the usage database.
There was **one synthetic provider call, zero real provider calls and zero systemd
operations**. Update/rollback used the same artifact; cross-schema downgrade is
not claimed. This run exercised the real self-contained package from `/`, not
stub executables. The frozen tested Host DLL is
`a2a6778a41ad05f65ca3aac201f2975751b8cfb6e241273adc8e6b8d1eded93d`,
and bundled Core remains the exact accepted `8edf8abf…04cf9dfc0` artifact.

The real-provider reports were independently inspected: two Codex Responses
stream/tool-continuation calls passed; the two authorized Command GLM calls passed
with truthful client identification `lain-reference-probe/0.1.0`. Seven terminal
records, including three earlier failures with unknown usage, survived restart
with identical CLI/API totals and groups. This does not qualify Command GPT,
which was not retested successfully, or prove real owned OAuth renewal.

After the corrected package run, the private legacy guard again showed **zero
persistent additions/removals/changes** for binary, configuration and auth files.
Only transient `.tmp` churn was observed (four added, three removed at that
instant). A first read raced a disappearing temporary file and was retried;
no credential content was emitted. The installed-run report subsequently confirms
the original service PID/start time remained unchanged.

The actual user installation, isolated Codex CLI session and installed native
desktop baseline are now evidenced below. The user then requested a visual redesign; its subsequent Xvfb and installed
acceptance appears at the end of this ledger. These results are not a blanket claim that all provider models, interactive client menu paths or OAuth
lifecycle operations have been tested.

## Actual installed service and Codex CLI

W11 reviewed the integrator's [installed-run report](../tests/acceptance/evidence/installed-final.json).
This is actual installed-service evidence, distinct from the temporary synthetic
package run: Codex profile `lain` used `gpt-6-luna` through the own proxy, and the
configured client catalog contains 45 models. The observed request produced one
request, one attempt, no errors and 81,986 total tokens (81,981 input and 5 output).
Reported cached tokens are 3,968 and reasoning tokens are zero; these subsets are
not added to total again. CLI/API totals match, cursor is 2, and the same usage
survived an actual own systemd service restart. The own unit is enabled.

The normal Codex configuration remained unchanged. The original proxy service's
PID and start time remained unchanged, and the final persistent legacy-file
comparison found no added, removed or changed binary/config/auth files. Temporary
`.tmp` churn in the independently running legacy OAuth directory remains excluded
from that persistent-file statement.

This single request qualifies the observed installed Codex route and exported
profile/helper/catalog workflow. It does not test all 45 catalog entries, a GUI
OAuth authorization or real refresh-token renewal. The account/last-good quota
semantics also retain their explicit scope: W6 confirmed an executed
`bash tests/quota/run.sh` run on commit `8baeeb1` with **35 assertions passed
(synthetic only)**. Those assertions include two same-provider accounts, independent
credentials and values, one-account failure retaining last-good data without
hiding the other account, stale/unknown windows, cancellation and owned access
credentials without refresh/ID-token use. W11 reviewed that peer execution
confirmation and the test sources; it did not run another quota test. The
committed live diagnostic contains actual Command quota observations. The quota
cache intentionally starts unknown after host restart, and missing model allowance
remains unknown.

## Installed native desktop

W11 reviewed the [installed-desktop report](../tests/acceptance/evidence/desktop-installed-final.json)
and the reopened Usage screenshot. The native harness loaded the actual installed
`Lain.Desktop`, `Lain.Client` and Avalonia assemblies; it did not compile replacement
application sources or serve fixture data. Installed Desktop DLL SHA-256:
`da0961adf7ae2b8894f9c5c45596f03be2397e33c21fd032f7ce1ab84620ce67`.

The actual Usage view showed the same one request and 81,986 tokens as the installed
CLI/API. On each opening it stayed open for more than 6.4 seconds and its polling
generation advanced from 1 to 2. Closing and reopening the window within the same
harness process preserved the displayed numbers; the host remained reachable and
the proxy ready while the window was closed, with cursor 2 unchanged. No new
inference was sent during that closed interval, so this check establishes service
independence and unchanged persisted data, not additional unseen usage generation.
The integrator separately launched the actual `lain ui` command from `/` and
closed it afterward. No test windows remain open.

The installed native run used GET-only observations and Usage navigation; account,
route, profile and login control actions have the earlier fixture-control scope.
It did not perform configuration mutations, a browser authorization, real OAuth
renewal, or an interactive Codex `/model` inspection. The generated 45-entry
catalog and successful actual `--profile lain` request are the harness evidence.
Any further GUI tests must use Xvfb as requested by the user; no additional test
was needed for this documentation review.

## Visual redesign requested after baseline review

The user rejected the visual design shown in the installed baseline and requested
a MeowChan direction using Ursa/Semi. Functional backend, persistence, isolation
and installed-package evidence remains valid for its recorded artifacts; it does
not establish acceptance of the requested new interface. In particular, the
Desktop hash and screenshots above describe the **prior design**, not the replacement
design. The required replacement was subsequently implemented and checked as recorded below.

## Redesigned desktop final acceptance

The [independent visual review](ui-redesign-review.md) inspected all 30 renders
from 15 scenes at 1120×780 and 800×560. The [workflow report](../tests/acceptance/evidence/desktop-redesign.json)
records 63 passing assertions against production UI/theme code with fake HTTP
handlers. This includes account and route/profile workflows, login cancellation,
export, unknown/error/offline states, five-second polling, focus retention,
detachment and window reopening. Routed Click events are not physical pointer
or exhaustive tab-order testing; the visual report retains these limits.

Avalonia 12.1.4, Semi 12.1.0.1 and Ursa 2.2.0 replace the initial Simple theme.
The desktop was published self-contained from `38a2d1c`, with DLL SHA-256
`a7433e824304841e3d67522d28daa52afcf8af07d481e98b17fdb4dace917218`.
The [desktop-only update report](../tests/acceptance/evidence/desktop-update.json)
confirms byte-identical Host, CLI and Core, unchanged own/legacy service PID and
start time, and unchanged normal Codex configuration. Previous desktop and notices
are retained outside the installed app for rollback. No service restart occurred.

The [installed private-Xvfb report](../tests/acceptance/evidence/desktop-redesign-installed.json)
loads the actual installed assemblies and production App theme. It shows the same
real recorded request and **81,986 tokens** before and after closing/reopening;
each window observed at least one polling refresh, the host/proxy remained ready
while closed, and cursor 2 stayed unchanged. No inference, credential operation
or configuration mutation was performed. This closes the new UI's installed
usage parity and background-independence gate; the prior CLI/API equality and
own-service restart persistence remain valid for the unchanged backend.
