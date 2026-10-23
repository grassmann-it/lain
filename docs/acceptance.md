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
| A provenance | Manifest-bound source/build/license and private owned processes/ports | Pending |
| A identity | Two distinct credentials for one provider, strict HTTP pinning on 429/deactivation, explicit pool behavior | Pending |
| A durability | Dispatch persisted before send; sync completion usage; restart/replay; write failure, crash and unknown coverage | Pending |
| A protocol | Nonstream, SSE, harmless tool + continuation, cancel; synthetic retry counted as one request and multiple attempts | Pending |
| B host | Unauthorized local API rejected, revision races rejected, failure distinguishes desired/applied revision | Pending |
| B data/privacy | SQLite restart and dedup; no headers/credentials/payloads in usage/export/errors; unknown amounts serialized null | Pending |
| B accounts/quota | API/Codex snapshot import, no refresh token; separate same-provider accounts, isolated quota failure/last-good | Pending |
| C desktop | Actual Linux window, clickable account/model/profile/usage flows; closes without stopping collector | Pending |
| D harness | Isolated Codex config/helper/model menu and harmless actual request; no existing configuration changes | Pending |
| D consistency | CLI/UI/skill consume same host snapshot and bounded queries; no duplicate collector | Pending |
| E delivery | Fresh staged install outside cwd, start/stop/restart, update/rollback, uninstall preserving data | Pending |
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
