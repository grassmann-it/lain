# CLIProxyAPI v8.0.41: upgrade and external dependency

On 24 October 2026 the user commissioned a direct upgrade: use CLIProxyAPI with
as few changes as possible and preserve existing lain features. This supersedes
the first delivery's v7 pin for that follow-up task.

## Implementation

CLIProxyAPI is obtained from the official hashed v8.0.41 module archive at commit
`8289ec4c71ad914d0612c2776352b76ba3d355a6`. The complete vendored source tree is
removed. `proxy/adapter` contains Lain's launcher, account bindings, durable
journal, bounded Inspector, and login ownership rules. Provider implementations
and protocol translation still come from CLIProxyAPI.

An unchanged official binary cannot supply existing guarantees: usage initially
enters a RAM queue; executor registration, field presence, and some existing
transport parameters require additional hooks. These extensions are openly
maintained as patch files. The build manifest records
`upstreamSourceModified: true`; the upstream module cache remains unchanged.
Exact boundaries are in [proxy/README.md](../proxy/README.md), provenance and file
hashes in [proxy/upstream.json](../proxy/upstream.json).

Host production sources need no change for this upgrade. Startup flags,
`/lain/*` interfaces, and journal format remain. Delivery replaces only Core,
Go license evidence, and bounded Core provenance/metadata. Host, CLI, Desktop,
configuration, secrets, and pending profile changes retain their state.

One deliberately closed side feature is native server-side image generation:
its additional model-specific usage record already conflicted with the qualified
single-attempt/primary-usage contract in v7. Explicit native image tools are
therefore clearly rejected before sending, automatic injection is disabled,
and payload overrides are checked at the final HTTP send point. Ordinary function
tools and image inputs for analysis remain supported. This is a transparent
restriction of a previously untested path, not a claim of new image support.

## Acceptance

The Core was installed and accepted after a controlled lain restart.
Delivery source: `734d7471703054496e7dc0361c4990bab38f6afd`.
Binary SHA-256:
`e39e141daaca6c1946d93866e4d67e93651a07ff9664edf03078d4a9c49b6fe6`.
Source manifest SHA-256:
`c5ff7548dcf72a0dd40b9c9a389799303bd7efebb7d1d30835aec13b1349d0cb`.

| Separate check | Result |
| --- | --- |
| Adapter/hook regressions and independent source review | 69 adapter tests; five targeted upstream cases including subtests; no open findings within the reviewed hook scope. Two independently prepared builds and sidecars were byte-identical. |
| Independent runtime | All eight groups passed: protocols/durability, auth/crash, Inspector, upgrade boundaries, native tool boundary, Host writer, version switching, SQLite replay. Only private loopback namespace and synthetic providers. |
| Complete journal through real SQLite consumer ten times | 200 requests, 200 attempts, 3,600 tokens, two accounts, cursor 600; unknown cache/reasoning quantities remain null. |
| v7→v8→v7 | Old bytes, events, and source ID unchanged; new sequences continue. Mixed journal ten times: four requests/attempts and 72 tokens. No real state copied for the test. |
| Fresh Linux package build | Actual `publish-linux.sh` run, 59 linked Go modules with no missing root license evidence, 40 NuGet/runtime packages. Fresh independent application builds were not installed or given blanket release qualification. |
| Core-only installer | Exact package on a complete private app copy; intentional failure after two swaps restores all original bytes. Subsequent installation matches the complete package manifest. |
| Real installation | Consistent app/state backup while stopped; only lain stopped/started. Five accounts, 45 active model aliases, revision 4/2, existing credentials and runtime unchanged. Journal bytes/source ID and history with 81,986 tokens/cursor 2 preserved. |
| Installed UI/CLI | 166 checks of unchanged Desktop assemblies against updated service on private Xvfb. CLI from `/tmp`: 20 compact versus 99 full lines, identical JSON, unchanged historical numbers. |
| Isolation | Personal proxy service and protected files, normal Codex configuration, separate lain client integration, icon, and UI preferences unchanged. No real inference triggered. |

The complete vendored tree of 1,476 files is removed. Two explicit patches affect
16 upstream paths; 576 patch lines also include associated regression tests.
Package provenance preserves original app pins and earlier gate evidence;
only Core/Go notices and explicitly bounded metadata were replaced.

Private consistent backup:
`~/.local/share/lain/upgrade-backups/core-v8-20261024T142030Z`.
The finally verified package is at
`artifacts/lain-linux-x64-0.1.0-core-v8-20261024.tar.gz`; all 1,205 files match the
sealed installation. `releaseQualified` and `coreRefreshQualified` are set for
exactly that state. No database migration was needed. Version rollback was
checked synthetically; the user's installation was not actually reverted.

Evidence: [source/unit gates](../tests/acceptance/evidence/core-v8-source.json),
[independent runtime](../tests/acceptance/evidence/core-v8/acceptance.json),
[fresh package build](../tests/acceptance/evidence/core-v8-fresh-package.json),
[installer](../tests/acceptance/evidence/core-v8-installer.json),
[installation](../tests/acceptance/evidence/core-v8-installed.json),
[UI](../tests/acceptance/evidence/core-v8-installed-ui.json),
[CLI](../tests/acceptance/evidence/core-v8-cli.json),
[final delivery evidence](../tests/acceptance/evidence/core-v8-release-final.json).

Executable matrix: [backend qualification](backend-upgrade-qualification.md).
Staging/installation contract: [Core-only delivery](core-upgrade-delivery.md).

A version change does not automatically enable or live-qualify new upstream
features in lain. Existing endpoint scope remains. Real OAuth rotation, new
provider access, and new upstream capabilities require separate evidence.
The personal existing proxy and its queue are outside upgrade verification.
