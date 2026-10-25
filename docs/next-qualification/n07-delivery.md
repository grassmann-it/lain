# N07 — Portable Komponentenlieferung

Source implementation for the N01–N09 goal, based on `7661886`. No installed
application or service was changed by this work. This is private Linux-x64
operator tooling; it does not publish a release or establish support for other
platforms. Exact binary/runtime gates remain an integrator task.

## Implemented flow

1. `scripts/prepare-component-delivery.py` binds already produced Host/CLI
   publishes, real assembly-metadata-reader output, source evidence, an exact
   complete baseline and a Core package freshly staged over that baseline.
   It prints a plan by default; `--apply` writes only new binding files.
2. `scripts/host-cli-core-refresh.py stage` consumes that generated
   `mixed-inputs.json` and its SHA256. Default is read-only; `--apply` stages
   the package. The retained Desktop source is now an explicit full commit
   input, checked against baseline metadata and unchanged Desktop bytes.
   Repeated deliveries replace owned previous refresh provenance rather than
   nesting it. The exact previous package remains a separately pinned input.
3. `scripts/service-update.py` supports the retained-Desktop mixed package and a
   separately qualified `full-components` package (Desktop may change). It consumes a pinned operator-input JSON. Default
   only reads/verifies inputs, installed app, integration pins and evidence.
   `--execute` acquires an owned private lease; verifies the loaded own
   `lain.service`, exact Host/Core processes and management listener; drains
   admitted requests; captures stable public identity/history; stops only the
   verified own unit; makes a full stopped snapshot; applies the service-free
   app-only installer; starts the unchanged unit, drains the **new** proven
   backend, verifies identity/history while quiet and reopens admission.
4. On failure, the original exception text is retained in private `update.json`
   and the raised cause. Failed command stdout/stderr are retained in private
   `private-errors.log`. A lost drain response remains a cleanup obligation;
   admission is reopened only on the same proven backend. After stop/install
   failure, a stopped failed-current snapshot is attempted only after a confirmed
   quiet drain of the new backend. Drain failure leaves streams alive, restores
   admission when provable and records `failed-poststart-recovery-required`. No state rollback
   or automatic promotion of old grants occurs.
5. `scripts/service-recovery.py` verifies a pinned snapshot manifest, all
   snapshot files and a pinned repository maintenance helper. Default only
   plans. `--execute --stopped` restores to a **fresh** destination and writes
   `restore-requires-reconnect`. It never overwrites current state, switches
   launchers, starts services or removes the credential quarantine.

The implementation is ported from the already reviewed private publish binder,
model-picker mixed operator, cumulative own-update primitives and finite
platform-process proof. There are no personal home/cache paths or imports from
private evidence directories. The full-package application transaction is in `scripts/package-refresh.py`: it
replaces only `HOME/app`, keeps the exact previous app under `appDeltaBackup/app`,
reverses an interrupted file replacement and never changes external integration.
Its installer/helper are explicit pinned source inputs; they need not be silently
injected into the target package.

The supported own-unit name remains deliberately
`lain.service`, and management remains the qualified loopback port 18417; this
is a finite supported contract, not a general arbitrary-unit updater.

## Publish inputs and commands

Required binaries: complete actual predecessor package; Core package freshly
staged over it with qualified binary/source/evidence; new self-contained
linux-x64 Host/CLI publishes. `DELIVERY` contains `cli/`, `host/` and
`source-evidence/{cli,host}/{project.csproj,packages.lock.json,publish-properties.json}`.
Metadata JSON must contain actual reader results for both exact DLLs, with
`method=assembly-metadata-reader`, informational-version attribute, DLL SHA256,
full informational version and `passed=true`. A report created by this binder
is a publish-binding report, not runtime or installed qualification.

```sh
python3 scripts/prepare-component-delivery.py \
  --delivery "$DELIVERY" --stage-dir "$STAGE" \
  --baseline "$BASELINE" --baseline-manifest-sha256 "$BASE_SHA" \
  --core-package "$CORE_PACKAGE" --core-manifest-sha256 "$CORE_MANIFEST_SHA" \
  --metadata "$METADATA" --metadata-sha256 "$METADATA_SHA" \
  --source-commit "$SOURCE" --desktop-source-commit "$DESKTOP_SOURCE" \
  --installer-sha256 "$INSTALLER_SHA" --helper-sha256 "$CORE_HELPER_SHA"
# Review plan; repeat with --apply to create binding files.
python3 scripts/host-cli-core-refresh.py stage \
  --inputs "$STAGE/mixed-inputs.json" --inputs-sha256 "$INPUT_SHA"
# Review plan; repeat with --apply to stage the package.
```

All paths are explicit absolute paths without symlink ancestors or traversal.
Hashes are lowercase SHA256; source commits are full 40-character commits.
For today's simultaneous Desktop/Host/CLI delivery, the full variant accepts a
normal **fresh Gateway package from `scripts/publish-linux.sh --mode gateway`**,
produced in Root's serialized build lane. Root separately produces/gates the
fresh Monitor package. The operator validates the publish-linux frozen source
contract, source inventory and each entry, all three component assembly/lock/
shared-assembly/publish-property records, and raw Core build-input/source-manifest
provenance, in addition to `check_core`'s binary/build/qualification/notice chain.
Set `core.sha256` to the explicitly retained qualified binary (currently `191f…`)
and `core.sourceCommit` to the exact fresh package's `build-info.coreSourceCommit`.
A reproducible rebuild retaining that binary hash is a separate build gate.

The full variant does not accept the historical `stage-component-refresh.py`
provenance or silently reinterpret an intermediate mixed+Desktop stage as a
fresh full publish. The old Core guard in that helper remains unchanged. New
operator/installer scripts can run from the frozen checkout via explicit paths
and hashes; copying them into the shipped package is optional and would require
resealing and qualifying the resulting different package manifest.

## Service operator input contract

`--inputs ABSOLUTE_JSON --inputs-sha256 SHA256` is required. `--execute` is the
sole mutation switch. Format 1, kind `portable-mixed-service-update-inputs`:

- `packageVariant` defaults to `host-cli-core-retained-desktop`. For
  `full-components`, additionally supply absolute `installerPath` pointing to the
  reviewed `package-refresh.py`, `coreHelperPath` pointing to the reviewed
  `core-refresh.py`, and their existing `installerSha256` / `coreHelperSha256`.
  `componentInformationalVersions` explicitly pins the complete fresh CLI/Host/
  Desktop version strings (including any SDK-extended source suffix); the report
  binds this map and readiness checks the exact Host value.
  All three component assembly hashes and source/version metadata are checked;
  target `coreSourceCommit` must match the supplied Core pin. Full-package
  qualification uses kind `portable-full-service-update-qualification`, binds
  `packageVariant:full-components`, and replaces the mixed/retained checks with
  `fullComponentSourceProvenance` and `desktopReplacement`. It cannot admit a
  mixed retained-Desktop report.
- `sourceCommit`, `desktopSourceCommit`, `previousHostVersion` identify exact
  new Host/CLI, retained Desktop and previous Host informational version.
- `operatorSha256` pins `scripts/service-update.py`; `operatorHelpers` contains
  `primitivesSha256` and `platformSha256` for its two repository siblings.
- `installation`: absolute `home`, `prefix`, `configHome`, `codexHome`, `launcher`;
  `launcherSha256`, `unitSha256`, `managementPort: 18417`; ordered `externalFiles`
  starts with the launcher and `CONFIG_HOME/systemd/user/lain.service`, followed
  by every external desktop/icon/integration file to preserve. External files
  must be outside the state home. The operator never regenerates these files.
- `baseline` and `package`: `{path, manifestSha256}`; both complete inventories
  are verified. Installed `HOME/app` must already equal the explicit baseline
  during plan, before drain/stop. Installer and Core helper are separately
  pinned by `installerSha256` and `coreHelperSha256`.
- `core`: `{sha256, sourceCommit}` for the exact binary and source provenance.
- `qualification`: `{path, sha256}` for separately reviewed exact-artifact
  synthetic runtime qualification. Its format 1 kind is
  `portable-mixed-service-update-qualification`, status `passed`, `planOnly:false`,
  `syntheticOnly:true`. It binds source/Desktop/operator/helper/Core/preservation
  pins, the exact history window and baseline/package manifest hashes. Every named check below must have
  `passed:true` and a nonempty evidence reference. This is an evidence admission
  gate, not a signature or an automatic producer of truthful qualification.
- Required `checks`: `exactArtifactTests`, `mixedSourceProvenance`,
  `retainedDesktopBytes`, `serviceFreeInstaller`, `appProcessRefusal`,
  `fileTransactionFailureRecovery`, `stoppedBackup`, `syntheticStatePreservation`,
  `quietRequestDrain`, `ownServiceIdentity`, `mainCodexPreservation`.
- `backup`: new outer full-backup destination; `appDeltaBackup`: new destination
  inside home and outside app. Neither may overlap package/baseline inputs.
- `preservationBaseline`: nonnegative `accounts`, `routes`, `profiles`, `revision`,
  `appliedRevision`, plus `configurationSha256`. Account/profile/revision values
  are checked against stable public CLI output; configured routes are protected
  by the complete configuration hash and profile/model identities. Route count
  is qualification metadata, not a separately measured public CLI count.
- `legacyFingerprints`: explicit list of `{path,sha256}` (empty allowed).
  `mainCodexFingerprints`: explicit files inside `codexHome`, including
  `config.toml` and `auth.json`, each `{path,sha256}` or `{path,missing:true}`.
- `history`: explicit `from` and `to` for the public CLI history capture, in UTC.
  `to` must be a fixed past boundary before plan/execution. New admitted calls
  during startup may advance the global history cursor; it must not regress.
  Totals/tokens for the closed historical window remain exact. The previous
  journal file must remain a byte-identical prefix (append-only growth allowed).
  Other frozen config/runtime/Codex/legacy hashes remain exact. Postflight runs
  behind the newly established admission barrier and restores admission only
  after all guards pass.

Invoke with `env -u LAIN_HOME python3 scripts/service-update.py ...` when the shell
exports this installation's `LAIN_HOME`. Such an inherited matching variable is
refused during plan, before drain/stop: otherwise the maintenance helper would
correctly classify the still-running parent as a possible state writer.

Operator reports and snapshots contain private fingerprints and public account
metadata. Keep them private; directory mode is 0700, created files 0600. Existing
runtime/OAuth material is copied only opaquely by the stopped snapshot helper.

## Targeted preserved V4 diagnostic

The old WIP was reviewed individually. Its useful changed-value hash idea is
adapted as `require_identity`: only changed top-level identity fields and
before/after hashes enter `protected-state-delta.json`; raw values do not.
The old whole-file patch and optional relocation of V4 fixture roots were not
merged. The current operator already retains its own private failure root.

## Verification and remaining gates

Executed 51 temporary-file and mocked drain/failure fixtures (16 mixed refresh,
15 drain/failure, 20 portable binding/plan/recovery/lifecycle):

```sh
python3 scripts/test-host-cli-core-refresh.py
python3 scripts/test-service-update.py
python3 scripts/test-component-delivery.py
```

The lifecycle fixtures execute real synthetic package staging, full stopped
snapshot and application-file installation, with only process/service/network
boundaries mocked. Both the retained-Desktop and changed-Desktop/full variants
complete install → start → target-bound postflight. Additional cases admit a
poststart request and inject postflight failure (stop only after the new quiet
drain), and keep a stream active past timeout (no second stop, recovery required).
A failed full-app write transaction reverses only app files and preserves the
synthetic configuration. A false exact-qualification check fails before mutation.

These cover plan without mutation, input tamper and wrong installed predecessor
before subprocess/service invocation, explicit nonhistorical preservation counts,
publish-binding plan/apply, repeated provenance staging, file transaction faults,
drain races/lost responses and quarantined recovery. Synthetic DLL/qualification
claims in file fixtures are schema fixtures, not actual PE or runtime evidence.

Integrator still must qualify the exact new packages and this exact operator:
fresh Monitor and Gateway installs separately; real metadata reads; stopped
full backup; synthetic writefault and return with identity/history preserved;
request drain; exact package process identities; then any explicitly authorized
own installation. No such runtime, package, service or actual installation gate
was executed by the N07 worker.

## Review correction record

Review of `08d9044` identified a deterministic postflight bug: the previous
`plan(config, fresh=False)` still required the installed tree to equal the old
baseline after installation. The original guard text was `Installed app differs
from explicit complete baseline`; this was a source/fixture finding, not a real
service installation attempt. Preflight now explicitly requires `installed=baseline`
and postflight `installed=target`; neither accepts arbitrary installed bytes.
The old preflight refusal is retained by regression alongside successful target
postflight. Review also found the new-backend traffic/cleanup race and inherited
`LAIN_HOME` backup refusal; the changes and lifecycle fixtures above close those
source defects. Exact package runtime qualification remains required.
