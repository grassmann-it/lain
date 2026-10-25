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
3. `scripts/service-update.py` consumes a pinned operator-input JSON. Default
   only reads/verifies inputs, installed app, integration pins and evidence.
   `--execute` acquires an owned private lease; verifies the loaded own
   `lain.service`, exact Host/Core processes and management listener; drains
   admitted requests; captures stable public identity/history; stops only the
   verified own unit; makes a full stopped snapshot; applies the service-free
   mixed installer; starts the unchanged unit and verifies identity/history.
4. On failure, the original exception text is retained in private `update.json`
   and the raised cause. Failed command stdout/stderr are retained in private
   `private-errors.log`. A lost drain response remains a cleanup obligation;
   admission is reopened only on the same proven backend. After stop/install
   failure, a stopped failed-current snapshot is attempted. No state rollback
   or automatic promotion of old grants occurs.
5. `scripts/service-recovery.py` verifies a pinned snapshot manifest, all
   snapshot files and a pinned repository maintenance helper. Default only
   plans. `--execute --stopped` restores to a **fresh** destination and writes
   `restore-requires-reconnect`. It never overwrites current state, switches
   launchers, starts services or removes the credential quarantine.

The implementation is ported from the already reviewed private publish binder,
model-picker mixed operator, cumulative own-update primitives and finite
platform-process proof. There are no personal home/cache paths or imports from
private evidence directories. The supported own-unit name remains deliberately
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
For a changed Desktop, first produce and qualify the new complete baseline or
Desktop refresh through its separate delivery lane; the mixed updater preserves
its explicitly supplied Desktop and cannot substitute one silently.

## Service operator input contract

`--inputs ABSOLUTE_JSON --inputs-sha256 SHA256` is required. `--execute` is the
sole mutation switch. Format 1, kind `portable-mixed-service-update-inputs`:

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
  pins and baseline/package manifest hashes. Every named check below must have
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

Executed 39 temporary-file and mocked drain/failure fixtures (16 mixed refresh,
15 drain/failure, 8 portable binding/plan/recovery):

```sh
python3 scripts/test-host-cli-core-refresh.py
python3 scripts/test-service-update.py
python3 scripts/test-component-delivery.py
```

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
