# Repeating expansion verification

`scripts/verify-expansion.sh` is the synthetic integration runner for the
Inspector, persisted dimensions/performance, provider catalog/transports and
typed client/CLI. It is intended for the integrator's single compiler lane.
Preparing this runner is not evidence that its checks have passed.

```sh
scripts/verify-expansion.sh
```

The runner first builds `Lain.sln` in Release, then builds and runs the relevant
console and xUnit suites sequentially. Builds use `-m:1`,
`-p:BuildInParallel=false`, and `-nr:false`; console runs use `--no-build
--no-restore`. Standalone NVIDIA/StepFun, Poe/OpenCode and Synthetic/xAI wrappers
use their own temporary projects with the same serial build policy. No two
builds are launched by this runner at once. Other terminals and agents must also
respect the single compiler lane.

Covered suites include usage migration/replay and whole-range metric tests,
focused and independent Inspector collector/storage checks, policy/catalog and
transport tests, direct/trial/gateway adapters, all provider preparation groups,
independent qualification-validator fixtures, and client/CLI wire contracts.
Fixtures use synthetic credentials and fake HTTP. The runner does not execute
`tests/live`, enroll real accounts, install anything, or launch a GUI.

The existing `scripts/verify.sh` retains the original packaging, account, model,
quota and baseline checks. This runner does not call it or repeat those suites
automatically. Shared product compilation and the changed usage/client suites
are intentionally included here. Execute baseline checks as required by the
actual change; do not run both scripts' Core qualification on the same unchanged
artifact without a reason.

## Explicit process probes

Provide an absolute new Core path and/or a Host executable or DLL. Paths are
passed as argument vectors, never evaluated as shell commands.

```sh
scripts/verify-expansion.sh \
  --core /absolute/new/lain-proxy \
  --host /absolute/build/Lain.Host.dll \
  --report-dir /tmp/lain-expansion-new-result
```

The supplied report directory must not already exist. Without that option the
runner creates a private temporary report directory and retains it. Each check
has a log, usage produces a TRX report, and `result.json` records the source
revision, tracked dirty state and requested primary artifact hashes. These
hashes do not describe every dependent Host assembly or an installed package.
A failed run leaves `passed: false`; only completion of all selected checks sets
it to true. That flag is a synthetic-suite outcome, not a release approval.

`--core` runs the existing identity/durability qualification once for the new
binary, retains the exact frozen executable that passed, and uses it for the
independent Core acceptance and Inspector probes. Its complete contiguous
synthetic journal is then replayed through the usage-store helper. This checks
the new journal against the real SQLite adapter without real provider traffic.
The runner does not build or certify a Core implicitly; build provenance and
release markers still belong to the integrator.

`--host` runs the actual authenticated endpoint smoke probe with private
temporary state, synthetic local enrollment and disabled quota polling. It
checks registration, query bounds, null metrics, no-key onboarding and rejected
policy mutations. It never starts a qualified inference Core. Both process
options require Linux `unshare` and `ip`, with unprivileged user/network
namespaces enabled. Every probe receives a fresh namespace and brings up only
its own loopback interface. The installed Host/Core ports remain untouched.
Namespace failure aborts; there is no fallback to the user's network.

The Host probe can also be run separately after an allocated build:

```sh
unshare --user --map-root-user --net -- bash -c '
set -euo pipefail
ip link set lo up
exec "$@"
' lain-host-probe python3 -B tests/acceptance/host_probe.py -- \
  dotnet /absolute/build/Lain.Host.dll
```

## Separate native GUI verification

Always use the private-Xvfb runner. Do not substitute the current desktop,
Wayland session, or an already-running display.

```sh
tests/desktop/run-private-xvfb.sh /tmp/lain-expansion-gui-evidence
```

That runner has its own serial Debug fixture build. Use
`LAIN_DESKTOP_SKIP_BUILD=1` only after that exact fixture was built in the
allocated compiler lane; the Release solution build above does not satisfy
this condition. The runner clears inherited desktop variables, creates a
private X server and isolated application state, and retains real captures and
functional evidence. Passing console tests does not establish visual or
keyboard acceptance. Review the actual screenshots and evidence for the exact
fixture/package under test.

Current acceptance and limitations belong in
[the expansion review](expansion-review.md), with the precise source/binary
hashes and executed evidence. The [observability contracts](observability-contracts.md)
define semantics; the [architecture](architecture.md) distinguishes implemented
paths from verified capabilities. New provider live verification and executable
strict-free controls remain separate from mock or catalog success.
