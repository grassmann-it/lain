# Linux component refresh packaging

For the current five-destination UI release on the accepted expansion Core,
use [UI structure delivery](ui-structure-delivery.md) and
`scripts/stage-ui-structure-release.py`. The historical helpers below retain
their original guards and do not replace that release procedure.

Use `scripts/stage-component-refresh.py` when CLI, Host and Desktop change
together. The desktop-only helper deliberately refuses changed Host/CLI graphs.
This staging operation does not install software, execute application binaries,
or change services. The existing qualified proxy remains exactly
`8edf8abf666e3c15f1a04c20c841a969d35229f09e7de5265755ab704cf9dfc0`.

First publish each application separately into a fresh directory. Record its
actual source commit and `InformationalVersion`; the already published quota Host
uses source `de872b7` and informational version `1.0.0+de872b7`. Do not rebuild
the proxy. Restore all three current locked application graphs before collecting
notices; if publishing from different source commits, first verify their NuGet
lock graphs agree with the restored graphs. The helper compares each source
commit's lockfile with the checkout and records its hash. It requires an executable
launcher, DLL, deps file and runtime configuration for each component, and checks
the complete claimed informational version is serialized in its DLL as a .NET
string attribute. A prefix of an SDK-extended version is refused. Supported
revision formats are one source SHA (at least seven characters), or
`<shortsha>.<fullsha>` when both identify the same supplied source commit.
The existing CLI from `d9cfc2f` has actual version
`1.0.0+d9cfc2f.d9cfc2f7bfec7f4e77fe3cbf818135a3a0472755`; pass this complete
value. For new publishes, `-p:IncludeSourceRevisionInInformationalVersion=false`
keeps an explicitly supplied `InformationalVersion` unchanged.

From the integrated checkout, with new absolute output paths:

```sh
python3 scripts/stage-component-refresh.py \
  /absolute/baseline-package /absolute/new/lain-linux-x64-quota-ui \
  --host /tmp/lain-quota-host-publish de872b7 1.0.0+de872b7 \
  --cli /absolute/fresh-cli CLI_COMMIT 1.0.0+CLI_COMMIT \
  --desktop /absolute/fresh-desktop UI_COMMIT 1.0.0+UI_COMMIT
```

The baseline checksum manifest must pass before staging. Output, archive and
archive checksum must not exist or overlap any input or checkout. The helper
copies the baseline and replaces the three whole component directories. It
checks every copied application file and the entire unchanged proxy directory,
rebuilds notices, updates per-component commit/version/assembly hashes, and
preserves inherited backend evidence. Old application evidence remains historical;
`releaseQualified` is always `false` pending current UI and component acceptance.
The helper writes `SHA256SUMS`, a deterministic `.tar.gz` at gzip level 3 and its
SHA-256 sidecar. Failure may leave incomplete staging output; never install it.

Bundled Oxanium and IBM Plex Sans Condensed are embedded resources rather than
NuGet dependencies. Their original `OFL.txt` files must appear in the Desktop
publish under `Assets/Fonts/<family>/OFL.txt`, byte-identical to the checkout.
The notice collector also copies each family license into `notices/fonts/`,
records font and notice hashes in `inventory.json` under `bundledFonts`, and
retains the original copyright and reserved-font-name wording.

Before any subsequent installation, independently check staged `SHA256SUMS`, the
exact proxy hash and per-component provenance. A changed Host requires its own
controlled restart procedure; this staging helper performs no installation or
restart. See [the Linux installation guide](install.md) for the existing package
installation and rollback procedure. Keep the current UI acceptance gate open.

## Frozen expansion with a new instrumented Core

Use the separate `scripts/stage-expansion-release.py` for a new Core. The earlier
`stage-component-refresh.py` intentionally remains locked to the old qualified
Core and cannot be used to replace it. The standalone masonry archive is a
separate checkpoint and is neither overwritten nor copied as the expansion base.

Freeze the final application source commit and publish Host, CLI and Desktop
from that exact commit, with the same complete informational version. Set
`-p:IncludeSourceRevisionInInformationalVersion=false` during publishing to retain
a supplied short version. Restore all three locked `linux-x64` self-contained
graphs before publishing. The package helper requires executable launchers,
matching complete version attributes, self-contained runtime configurations,
RID-specific dependency manifests and restored graphs matching their lockfiles.
Tracked source drift and untracked application sources cause refusal.

Separately freeze the instrumented Core source after regenerating
`proxy/source-manifest.json` with `lainPatchVersion: 2`. Build it using the pinned
toolchain; retain the emitted binary sidecar. Obtain independent qualification for
the exact binary SHA-256. Its report must contain `passed: true` and `sha256` for
that binary, rather than a previous Core. If the report also records
`sourceManifestSha256`, it must match. The helper retains the original report
bytes and its scope; it never manufactures a qualification result. The sidecar
must bind the binary, source manifest, patch version and snapshot version.

```sh
python3 scripts/stage-expansion-release.py /absolute/new/lain-expansion-staged \
  --source-commit FINAL_SOURCE_SHA \
  --informational-version 1.0.0+FINAL_SOURCE_SHORT_SHA \
  --cli /absolute/frozen-cli --host /absolute/frozen-host \
  --desktop /absolute/frozen-desktop \
  --core /absolute/frozen/lain-proxy \
  --core-build-manifest /absolute/frozen/lain-proxy.manifest.json \
  --core-source-commit FROZEN_CORE_SOURCE_SHA \
  --qualification /absolute/independent-core-qualification.json
```

Final application, Core source commit and working checkout must contain identical
source-manifest bytes, and every current vendored Core file must match that
manifest. The new package is assembled only from the explicit publishes, frozen
git source objects and exact Core inputs. No prior accepted application evidence
is inherited. The new `proxy/binary-manifest.json` supplies the hash used by Host;
`proxy/build-manifest.json`, `source-manifest.json`, `qualification.json`, original
Core license and pinned toolchain/baseline/catalog metadata preserve provenance.

All five application/Core/Client lockfiles and project definitions are copied into
`source-provenance/`. Provider catalog JSON, adapter source files and
`docs/providers/` official-source research notes come from the frozen final
commit. Each catalog must also occur unchanged as an embedded resource in the
published Host DLL. Their inventory records source commits and file hashes;
catalog inclusion does not establish working credentials or live inference.
NuGet/runtime packages, linked Go modules, pinned official notices and bundled
font OFLs are recollected. The Go notice inventory must describe the new binary
hash; notice collection remains evidence for review, not legal approval.

The helper verifies the new package manifest and produces a normalized gzip
level 3 archive plus checksum. It does not execute application binaries, build,
install, modify normal configuration, change services or touch prior archives.
`backendQualified: true` refers only to the supplied exact-Core report;
`releaseQualified` and `expansionQualified` remain `false` until separate package,
installed workflow and current UI acceptance. A failure can leave incomplete
staging output, which must not be installed.

Synthetic guard verification:

```sh
python3 scripts/test-expansion-release.py
```
