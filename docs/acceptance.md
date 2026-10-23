# Independent acceptance

This is an evidence ledger, not a release approval. W11 owns the independent
review. A–E remain unaccepted until the corresponding checks below have actual
results against the integrated commit and staged artifacts. Synthetic evidence
never establishes real-provider or GUI compatibility.

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
| A identity | Two distinct credentials for one provider, strict HTTP pinning on 429/deactivation, explicit pool behavior | W2 final synthetic suite passed; real routes pending |
| A durability | Dispatch persisted before send; sync completion usage; restart/replay; write failure, crash and unknown coverage | W2 final suite + independent Core crash/auth + 9 SQLite tests passed |
| A protocol | Nonstream, SSE, harmless tool + continuation, cancel; synthetic retry counted as one request and multiple attempts | Chat, Responses translation, native Messages synthetic tests passed; real probes pending |
| B host | Unauthorized local API rejected, revision races rejected, failure distinguishes desired/applied revision | HTTP auth, concurrency, pending revision passed; failed live apply/restart pending |
| B data/privacy | SQLite restart and dedup; no headers/credentials/payloads in usage/export/errors; unknown amounts serialized null | Pending |
| B accounts/quota | API/Codex snapshot import, no refresh token; separate same-provider accounts, isolated quota failure/last-good | Pending |
| C desktop | Actual Linux window, clickable account/model/profile/usage flows; closes without stopping collector | Pending |
| D harness | Isolated Codex config/helper/model menu and harmless actual request; no existing configuration changes | Pending |
| D consistency | CLI/UI/skill consume same host snapshot and bounded queries; no duplicate collector | Pending |
| E delivery | Fresh staged install outside cwd, start/stop/restart, update/rollback, uninstall preserving data | Stub-package installer passed; actual staged application pending |
| Real providers | Explicitly authorized minimal Codex and Command Code stream/tool continuation, usage attribution and persistence | Pending |
| Final isolation | Same existing service PID/start and unchanged legacy binary/config/auth hashes, or documented attributable exception | Pending |

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
the entire directory was unchanged. A final comparison is still required.

Independent code review identified and escalated runtime-file/apply races and
missing token-component values becoming zero. The Host now serializes mutation
and stops the own Core before file writes, with runtime validity/revision checks.
The Core presence fix and its final protocol/durability tests remain subject to
the exact artifact's qualification report. The Codex external-profile format was
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
