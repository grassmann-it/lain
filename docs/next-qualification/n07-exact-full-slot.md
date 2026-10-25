# N07 — Next exact full-package slot

Preparation only. Main's Desktop/UXD lane retains its source freeze; no build,
GUI, Core, Host, service operation or actual installation was executed by this
worker. Publisher, Core and installer contracts are unchanged. New preparation:

- `tests/delivery-stabilization/assembly-metadata/`: portable version of the
  previously private PEReader, extended to CLI, Host **and Desktop**. It reads
  actual PE metadata; it does not load inspected application assemblies.
- `full_packages.py`: existing real package/install/Host and schema4 primitives,
  plus the actual full-app transaction, with explicit package/source/tool pins.
  Default verifies/plans without writing. Execution produces an honest
  **`exact-full-package-runtime` subreport**, not an own-update admission report.
- `drain_exact_core.py`: portable earlier real Core streaming/drain probe.
  Default only plans; execution uses a new exact pinned child and loopback fake
  provider. It does not operate an installed Core or service.

The old `actual_packages.py` top-level gate intentionally requires changed Core;
`v4_update.py` top-level plan requires four homogeneous-source packages. Those
historical conditions are not relaxed or bypassed by rewriting their inputs.
The new wrapper calls their reusable install/Host/seed/fingerprint/database/
assertion primitives directly, retaining the actual mixed predecessor component
map and allowing an unchanged explicitly pinned Core. A failed assertion yields
`status:failed`, its actual cause and retained private scratch fixture; no passed
operator checks are synthesized from missing evidence.

## Inputs Root must establish

| Input | How to determine it |
| --- | --- |
| `CHECKOUT`, `FROZEN_SOURCE` | Integrated checkout and full final source commit after this preparation is integrated and the current lane ends. Assert HEAD and application/packaging inputs frozen. No publish from the worker's older source tree. |
| `BUILD_TMP`, `EVIDENCE` | Existing private absolute build temp, and a fresh private evidence directory outside checkout/packages. Use mode 0700. All new package/output/archive paths must be absent. |
| `GO`, `DOTNET` | Absolute resolved executables. `proxy/toolchain.json` requires Go 1.27.2; `global.json` pins .NET SDK. Record executable/version identity without changing global configuration. |
| `CORE_SHA` | Expected retained binary, presently `191f4f17e4096558c32ebc5b28ec51341fadc6549f3269300af22800dfe6ce18`; compare exact output, never relabel a changed binary. |
| `CORE_QUAL`, `CORE_QUAL_SHA` | Existing genuine exact binary+source-manifest qualification. Recheck file SHA, passed/core/source pins before using it. Preserve original bytes and original qualification source commit. |
| `BASELINE`, `BASE_SHA`, `BASE_CORE_SHA` | Actual complete current installed predecessor package, not an older similarly named build. Recompute manifest/hash and verify full coverage. The historical current final manifest is `3ea1444b49e1cbd0303eadf361020c1dca9ab9fc72ec0a606b0be5c832dea7ea`; Root must re-establish that it remains the right predecessor. |
| `GATEWAY`, `MONITOR` | New full publish output directories under evidence, not existing reused package directories. Their manifests are determined only after publication completes. |
| Operator/installer/helper/reader pins | Compute SHA256 of frozen checkout `scripts/service-update.py`, `package-refresh.py`, `core-refresh.py`, and the actually compiled PEReader DLL. New tools do not contain personal paths or hardcoded artifact hashes. |

Existing candidate evidence paths (operator inputs, not runtime prerequisites):

- Core qualification: `~/.cache/lain-qualification-20261025/model-picker/core-r1/qualification.json`,
  observed SHA256 `602bfdc95021abb63b53f67de3be3d43deb7eadf52a3491f7bffb474e0fb3c2d`.
- Actual previous final package candidate:
  `~/.cache/lain-qualification-20261025/model-picker/stage-13519ef-current/package`;
  its observed manifest hash is the `3ea1444…` value above.
- Source-manifest SHA observed in the reviewed checkout:
  `365ee543e281bd53d256239caeb91e0039ac655cd4b25854bb2e43f783ff534c`.
  Existing Core qualification binds that same source manifest.

Do not use `publish-13519ef/package` or older `9bd1daf…` as a substitute merely
because the Host source looks familiar. The earlier wrong-baseline failure is
retained evidence; the actual installed complete manifest controls admission.
No current credential/configuration contents need be printed to determine these
package/source inputs.

## 1. Serialize one Gateway build and one Monitor build

The current publisher does **not** support an externally supplied Core binary.
`LAIN_QUALIFICATION_FILE` supplies prior exact binary/source qualification, not a
binary-reuse switch. Gateway runs `build-core.sh` once; Monitor never builds or
contains Core. No new external-Core path was added.

The existing Go path uses pinned toolchain, verified prepared sources,
`-mod=readonly -trimpath -buildvcs=false`. With unchanged build inputs and the
same compiler, expect the existing `191f…` hash; warm Go caches avoid unnecessary
work. `GOMAXPROCS=2` bounds the default Go build parallelism; `build-core.sh`
intentionally resets `GOFLAGS`, so do not claim `GOFLAGS=-p=1` was honored.
A mismatch stops this lane and needs actual new-Core qualification, not a changed
label on the old report.

Run from the integrated frozen checkout, sequentially (variables are Root's
explicit absolute inputs):

```sh
cd "$CHECKOUT"
test "$(git rev-parse HEAD)" = "$FROZEN_SOURCE"
printf '%s  %s\n' "$CORE_QUAL_SHA" "$CORE_QUAL" | sha256sum --check --status

env TMPDIR="$BUILD_TMP" LAIN_GO="$GO" GOMAXPROCS=2 \
  DOTNET_PROCESSOR_COUNT=2 MSBUILDDISABLENODEREUSE=1 \
  LAIN_QUALIFICATION_FILE="$CORE_QUAL" \
  bash scripts/publish-linux.sh --mode gateway "$GATEWAY" \
  > "$EVIDENCE/publish-gateway.log" 2>&1
printf '%s  %s\n' "$CORE_SHA" "$GATEWAY/proxy/lain-proxy" | sha256sum --check --status

env TMPDIR="$BUILD_TMP" DOTNET_PROCESSOR_COUNT=2 MSBUILDDISABLENODEREUSE=1 \
  bash scripts/publish-linux.sh --mode monitor "$MONITOR" \
  > "$EVIDENCE/publish-monitor.log" 2>&1
```

Retain exit codes, source pin and stdout/stderr; shell must stop on failure.
Both publishers already perform locked restore, source-freeze rechecks, component
version/provenance generation, notices, complete checksums and archives. The
Gateway publisher only imports `CORE_QUAL` if its actual newly built binary and
source-manifest hashes match. Leave `releaseQualified:false` until all applicable
product/runtime/desktop gates pass; this workflow does not publish a release.

## 2. Build the metadata reader once in the same serialized slot

```sh
env TMPDIR="$BUILD_TMP" DOTNET_PROCESSOR_COUNT=2 MSBUILDDISABLENODEREUSE=1 \
  "$DOTNET" restore tests/delivery-stabilization/assembly-metadata/AssemblyMetadata.csproj \
  --locked-mode -m:1 -nr:false

env TMPDIR="$BUILD_TMP" DOTNET_PROCESSOR_COUNT=2 MSBUILDDISABLENODEREUSE=1 \
  "$DOTNET" build tests/delivery-stabilization/assembly-metadata/AssemblyMetadata.csproj \
  --no-restore -c Release -m:1 -nr:false -p:UseSharedCompilation=false \
  -o "$EVIDENCE/assembly-reader" > "$EVIDENCE/assembly-reader-build.log" 2>&1
```

This new project has no third-party package dependencies. Its committed lockfile
is still subject to actual locked restore; neither preparation nor a marker
search is an actual PE read. `READER` is the produced `AssemblyMetadata.dll`,
with `READER_SHA` computed from those actual bytes. Keep the entire generated
reader runtime directory together. The full runtime harness invokes it for
both exact packages and retains actual CLI/Host/Desktop metadata output.

## 3. Plan, then execute exact no-service package/runtime qualification

After all builds complete, calculate the manifest/source-tool pins:

```sh
GATEWAY_SHA=$(sha256sum "$GATEWAY/SHA256SUMS" | cut -d ' ' -f1)
MONITOR_SHA=$(sha256sum "$MONITOR/SHA256SUMS" | cut -d ' ' -f1)
OPERATOR_SHA=$(sha256sum scripts/service-update.py | cut -d ' ' -f1)
INSTALLER_SHA=$(sha256sum scripts/package-refresh.py | cut -d ' ' -f1)
HELPER_SHA=$(sha256sum scripts/core-refresh.py | cut -d ' ' -f1)
READER_SHA=$(sha256sum "$READER" | cut -d ' ' -f1)
PARENT_NETNS=$(readlink /proc/self/ns/net)
```

Construct this one argv sequence, first without `--execute`:

```sh
env TMPDIR="$BUILD_TMP" python3 tests/delivery-stabilization/full_packages.py \
  --gateway-package "$GATEWAY" --gateway-manifest-sha256 "$GATEWAY_SHA" \
  --monitor-package "$MONITOR" --monitor-manifest-sha256 "$MONITOR_SHA" \
  --baseline-package "$BASELINE" --baseline-manifest-sha256 "$BASE_SHA" \
  --core-sha256 "$CORE_SHA" --baseline-core-sha256 "$BASE_CORE_SHA" \
  --source-commit "$FROZEN_SOURCE" --operator-sha256 "$OPERATOR_SHA" \
  --installer "$CHECKOUT/scripts/package-refresh.py" --installer-sha256 "$INSTALLER_SHA" \
  --core-helper "$CHECKOUT/scripts/core-refresh.py" --core-helper-sha256 "$HELPER_SHA" \
  --metadata-reader "$READER" --metadata-reader-sha256 "$READER_SHA" --dotnet "$DOTNET" \
  --temporary-parent "$BUILD_TMP" --output "$EVIDENCE/exact-full-runtime" \
  --parent-netns "$PARENT_NETNS"
```

For execution repeat **the same argv** with `--execute`, under a new loopback-only
network namespace, with the parent anchor captured outside. Prefix the argv with:

```sh
unshare --user --map-root-user --net sh -c 'ip link set lo up; exec "$@"' n07-lane \
  env TMPDIR="$BUILD_TMP" python3 tests/delivery-stabilization/full_packages.py
# append exactly the arguments above, plus --execute
```

The namespace contract deliberately does not add a PID namespace, matching the
reused package probes. No caller port is trusted; each synthetic Host/Core gets
guarded fixture ports. Failure retains its private scratch directory and error
in `exact-full-runtime/report.json`; success removes synthetic package copies
while retaining actual metadata and assertion evidence. Product output is never
forwarded as credential text.

The executed cases are: real PE reads of both packages; fresh Monitor and fresh
Gateway install/Host start/restart; real old mixed baseline Host/Core; actual
full stopped backup; service-free full-app plan/atomic update; desired/applied,
account/grant/fixed history/database preservation; external verified app-only
return and forward with latest synthetic grants; fresh quarantined recovery
while current/latest state stays unchanged; one real post-exchange journal-write
fault and app reversal; real Python argv-reference process refusal; and changed
installed-baseline refusal with no fixture writes.

## 4. Exact Core drain and existing model-group runtime

Run serially after the previous namespace exits:

```sh
PARENT_NETNS=$(readlink /proc/self/ns/net)
unshare --user --map-root-user --net sh -c 'ip link set lo up; exec "$@"' n07-drain \
  env TMPDIR="$BUILD_TMP" python3 tests/delivery-stabilization/drain_exact_core.py \
  --core "$GATEWAY/proxy/lain-proxy" --core-sha256 "$CORE_SHA" \
  --parent-netns "$PARENT_NETNS" --report "$EVIDENCE/exact-core-drain.json" --execute

python3 tests/codex-picker/host_model_groups.py --package "$GATEWAY" \
  --output "$EVIDENCE/exact-model-groups" --run
```

The drain probe sends exactly synthetic loopback requests, proves an admitted
stream finishes under the barrier, a new request is refused, quiet drain holds
and explicit reopening works. The existing model-group runner supplies its own
bwrap PID/network isolation and synthetic backing for the unchanged HOME path;
it checks actual package CLI/Host/Core/group artifacts, synthetic ordinary Codex
preservation and inference. Do not pass its old retained-Desktop hash from the
previous delivery; it records the exact fresh package executable hashes itself.

The current Desktop/UXD lane remains a separate gate on the **shipped Desktop**;
source UI acceptance is not a substitute for exercising the final package.

## 5. Seal only genuine evidence; own installation remains Root's later step

`full_packages.py` deliberately does not emit
`portable-full-service-update-qualification`. Its successful report can support
`exactArtifactTests`, `fullComponentSourceProvenance`, `desktopReplacement`,
`serviceFreeInstaller`, `appProcessRefusal`, `fileTransactionFailureRecovery`,
`stoppedBackup` and `syntheticStatePreservation` only for its executed scope.
The actual Core drain adds runtime evidence for `quietRequestDrain`; the real
model-group fixture adds synthetic `mainCodexPreservation` evidence. Preserve
all report/log hashes and exact package/operator/helper/reader pins.

`ownServiceIdentity` is **not** proven by no-systemd package tests. Root must
separately verify the actual loaded unit and exact current Host/Core/listener
against the true baseline before claiming that check. The production operator's
`prepare_update_class(...).own_unit(require_running=True)` and
`backend_identity(files=before)` already implement those read-only guards; do
not replace them with an arbitrary passed string or a mock unit report. Any
synthetic service-boundary fixtures remain explicitly source fixtures, not
actual loaded-unit proof. Own-unit proof and final operation are coordinated
only after these package and Desktop gates.

The eventual operator report must bind the exact final baseline/target manifests,
source/Desktop pins, exact full component informational versions from build-info,
operator/helper/installer SHAs, exact Core/source pins, explicit real preservation
baseline and fixed **past** history window. It uses kind
`portable-full-service-update-qualification`, `packageVariant:full-components`;
all required named checks must have honest referenced evidence. Root determines
current accounts/routes/profiles/revisions and private Codex/legacy fingerprints
without printing secrets. `env -u LAIN_HOME` prevents the known inherited-parent
backup refusal. Explicit plan precedes any actual serviceupdate; actual installation
is not authorized or attempted by this preparation.

## Preparation validation

Five tiny file/schema fixtures passed (same-Core acceptance without homogenizing
the predecessor, no-mutation plan, explicit post-output revalidation, external
return copy and old Monitor component refusal under new global metadata).
They use synthetic nonexecutable binary/evidence bytes and mock only the separate
Core qualification semantic check; they are not runtime/PE/qualification evidence.
The independent source reviewer found no remaining issue in the requested
wrapper/reader/drain scope after the return and Monitor-provenance fixes. No
runtime, SDK build, GUI or service operation was executed by the worker.
