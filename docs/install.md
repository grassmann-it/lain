# Linux distribution

Initial target: Linux x64, .NET SDK **10.0.111**, self-contained Host, CLI, and
Avalonia Desktop. The build uses `global.json`, explicit NuGet versions, and
committed `packages.lock.json` files; locked restore rejects dependency-graph
changes. Desktop requires an X11/Wayland desktop system with Avalonia's usual
native libraries (particularly fontconfig/X11). The bundled proxy is Lain's
verified build of the external CLIProxyAPI source archive pinned in
`proxy/upstream.json`, the integration in `proxy/adapter`, and explicit hooks
in `proxy/patches`. It does not use an existing installed proxy. The package
includes its provenance and hashes.

## Monitor and Gateway in the current source

The historical Gateway/Desktop procedures below do not prove a newly installed
version. The current publisher offers explicit packages:

```sh
./scripts/publish-linux.sh --mode monitor /absolute/new/lain-monitor
./scripts/publish-linux.sh --mode gateway /absolute/new/lain-gateway
```

Monitor contains Host/CLI/Desktop and application notices, but no Core, Core
catalog, Go build, or Go notice inventory. Gateway retains all Core integrity,
source, and acceptance guarantees. `package-metadata.json` and `build-info.json`
record the mode; `source-provenance/publish-properties` records `LainRuntimeMode`
for each component. A fresh installer sets only the initial value; existing
configuration takes precedence. Services are not started.

The complete new procedure, including guarded updates, stopped snapshots,
rotating credentials, and old-Host limitations, is in
[Linux delivery stabilization](linux-delivery-stabilization.md). The actual
package probe at `tests/delivery-stabilization/actual_packages.py` defaults to
planning. Explicit execution uses only new temporary XDG/LAIN_HOME paths and
never systemd or existing data. The integrator coordinates all build/runtime
slots. Only the exact verified artifacts can justify a new installation.

## Build a Gateway package (earlier procedure)

From the checkout, with the .NET SDK and the Go version required by the proxy build:

```sh
./scripts/publish-linux.sh /absolute/path/lain-linux-x64
```

The destination must be new. The script publishes `cli/Lain.Cli`, `host/Lain.Host`,
`desktop/Lain.Desktop`, and `proxy/lain-proxy`, the license, source manifests,
a `notices/` inventory with exact NuGet license metadata and available package
and official runtime license texts, and an inventory of actually linked Go
modules with cached root licenses and the Go toolchain license. It also includes
the static `proxy/codex-model-catalog.json`, CLI skill, and installer.
`SHA256SUMS` protects package files; a `.tar.gz` and its checksum are created
beside the directory. The archive uses sorted filenames, fixed owners, and the
commit timestamp (overridden by `SOURCE_DATE_EPOCH`). .NET builds deterministically
with normalized source paths. `build-info.json` records the commit, SDK, and
uncommitted changes. Use a clean commit and identical toolchains for a comparable
release build.

Manifest verification alone does not replace backend acceptance under
`docs/execution.md`. Without final acceptance evidence, the result is only a
staging package: `gate-status.json` contains `releaseQualified: false` and no
`qualification.json` is generated. For a backend-accepted package, set
`LAIN_QUALIFICATION_FILE=/absolute/approved/qualification.json`. This evidence
must contain `passed: true` and the exact `sha256` of the newly built proxy;
a mismatch fails the build. The publisher never assigns a passing result itself.
Passing backend evidence sets only `backendQualified: true`; `releaseQualified`
stays `false` until separate package, Desktop, and installed-workflow acceptance.

If the lock state needs an intentional update:

```sh
dotnet restore Lain.sln -r linux-x64 -p:SelfContained=true
```

Review and commit changes to all lockfiles, then publish again. The release
pipeline never updates lockfiles automatically.

## Repackage only Desktop

A UI-only update uses the already qualified package directory as its baseline
and is assembled first in a new staging directory. `publish-linux.sh` is
unsuitable: it also rebuilds Host, CLI, and Core. Publish Desktop separately
with the current lock state:

```sh
dotnet restore src/Lain.Desktop/Lain.Desktop.csproj --locked-mode -m:1 \
  -p:BuildInParallel=false -nr:false -r linux-x64 -p:SelfContained=true
dotnet publish src/Lain.Desktop/Lain.Desktop.csproj --no-restore -m:1 \
  -p:BuildInParallel=false -nr:false -c Release -r linux-x64 --self-contained true \
  -o /absolute/new/desktop-staging -p:ContinuousIntegrationBuild=true \
  -p:Deterministic=true -p:DebugType=None -p:DebugSymbols=false \
  -p:InformationalVersion=0.8.0+DESKTOP_COMMIT \
  -p:PathMap="$PWD=/_/lain"
```

Replace only the complete `desktop/` directory in the copied package; replacing
individual files would retain removed dependencies. Compare all Host, CLI, and
`proxy/` file hashes before and after copying. Leave the live installation and
service unchanged until Desktop verification.

The new NuGet inventory must come from the current restore graph. Before
`python3 scripts/collect-notices.py /absolute/new/package/notices`, also restore
Host and CLI with their unchanged lockfiles and verify their inventoried
dependencies match the baseline package. Use a fresh `notices/` directory to
avoid retaining removed packages. The script collects Semi, Ursa, and transitive
packages automatically; `official/` adds license texts from the NuGet packages'
repository commits recorded in `sources.json`. The old Avalonia 11 license text
remains as a historical supplement; the current inventory names the exact version.

Update only Desktop provenance and the new packaging state in `build-info.json`,
preserving other component states. Backend acceptance and its exact hashes remain;
old Desktop evidence applies only to its recorded state. `releaseQualified` stays
`false` until renewed visual acceptance. Then regenerate `SHA256SUMS` for all
package files, the archive, and its checksum. Use only the verified package for
a separate Desktop update; the normal installer replaces the whole application.

After restoring all three projects and publishing Desktop separately, this
staging helper automates those steps:

```sh
python3 scripts/stage-desktop-refresh.py /absolute/baseline-package \
  /absolute/new/desktop-staging /absolute/new/lain-desktop-refresh \
  --desktop-source-commit DESKTOP_COMMIT \
  --desktop-informational-version 0.8.0+DESKTOP_COMMIT
```

It rejects existing outputs, verifies the baseline, compares non-Desktop
dependencies, preserves Host/CLI/Proxy byte for byte, and creates new provenance,
notices, checksums, and an archive. Failures leave an incomplete staging directory
for investigation; do not install it. The helper starts no programs or services
and writes to no installation.

For the later update, close only the GUI; the service stays active. Before any
installation change, compare all `cli/`, `host/`, and `proxy/` file hashes between
installation and staging; abort on any difference. First copy `desktop/` and
`notices/` into new sibling directories of the installation, move the old
directories to uniquely named backups outside `app/`, and place the prepared
directories at `app/desktop` and `app/notices`. Then copy only `build-info.json`,
`gate-status.json`, and `SHA256SUMS` from the verified package and run
`sha256sum --check --quiet SHA256SUMS` inside the installed `app/`. This Desktop
switch leaves the launcher, service file, and private data untouched; no service
restart is required.

## Install and start

Verify the archive checksum before extraction, then supply the absolute package path:

```sh
/absolute/path/lain-linux-x64/install-linux.sh /absolute/path/lain-linux-x64
systemctl --user daemon-reload
systemctl --user start lain.service
~/.local/bin/lain status --json
~/.local/bin/lain ui
```

The installer neither starts nor enables a service. Optionally,
`systemctl --user enable lain.service` enables startup at user login. Linger is
not configured. The Host runs independently of the GUI window, but only within
the lifetime of the user service manager.

Default paths:

| Purpose | Path |
| --- | --- |
| Launcher | `~/.local/bin/lain` |
| Desktop entry | `~/.local/share/applications/lain.desktop` |
| Original logo | `~/.local/share/icons/lain.png` |
| Private data | `~/.local/share/lain` (mode 0700) |
| Programs | `~/.local/share/lain/app/{cli,host,desktop,proxy}` |
| Service | `~/.config/systemd/user/lain.service` |
| Host API | `http://127.0.0.1:18418` |
| Dedicated inference proxy | `http://127.0.0.1:18417` |

`lain` works from any working directory; `lain ui` launches the GUI, and all other
arguments pass unchanged to the CLI. `LAIN_HOME` overrides the private directory
and must be absolute. Spaces are supported; the installer rejects quotes,
backslashes, percent signs, and tabs in `LAIN_HOME` before changing anything
because these paths are not a supported systemd executable-path form. Installation
records the selected value in both launcher and service. A different installation
prefix as the second argument changes the location of `bin/lain`.

The current installer also places `share/applications/lain.desktop` and
`share/icons/lain.png` under that prefix; `LAIN_DESKTOP_DATA_DIR` can independently
set the absolute `share` path. The desktop entry starts the absolute launcher
with `ui`, independently of `PATH` and working directory. Exec arguments and
desktop strings are escaped according to their respective syntax. The original
transparent cat image is copied unchanged; the desktop scales it for menus and
the taskbar. The application menu shows **lain — Local AI Gateway**. The CLI
command remains `lain`; existing installation paths do not change.

`LAIN_HOME/desktop-integration.json` records written desktop/icon paths and hashes.
Existing foreign files, symlinks, or subsequently customized desktop entries/icons
are rejected before any installation change, preserving customizations.
`LAIN_SYSTEMD_DIR` selects the service-file directory; otherwise it uses
`$XDG_CONFIG_HOME/systemd/user` or `~/.config/systemd/user`.

Isolated installation test without system changes or service startup:

```sh
LAIN_HOME=/tmp/lain-test/data LAIN_SYSTEMD_DIR=/tmp/lain-test/units \
  ./scripts/install-linux.sh /absolute/path/lain-linux-x64 /tmp/lain-test/prefix
cd /tmp
/tmp/lain-test/prefix/bin/lain --help
```

Installation does not change an existing proxy, its queues, OAuth credentials,
or normal Codex configuration. Later read-only import and separate Codex
integration are explicit CLI actions. The skill is bundled and is not
automatically copied into the personal skill library.

## Update and switch back

Run `systemctl --user stop lain.service`, install the new package using the same
paths, then run `daemon-reload` and `start`. Data stays outside `app`; the previous
application then resides at `app.previous`. Before another update, intentionally
archive or remove that backup. The installer refuses to overwrite a foreign
`app` directory. To switch back, stop the service, archive `app`, and restore
`app.previous` as `app`. An application backup does not replace a data-directory
backup; preserve compatible data when database schemas change.

To uninstall, stop and disable the service, remove the identified service file,
launcher, owned desktop entry `share/applications/lain.desktop`, owned icon
`share/icons/lain.png`, and owned `app` directory, then run
`systemctl --user daemon-reload`. Delete private data only on explicit request.
The parent `LAIN_HOME` and `app.previous` remain; do not recursively delete
`~/.local/share/lain`. For custom paths, use the exact files reported by the
installer. Remove desktop entry and icon only when their paths and current
SHA-256 match `desktop-integration.json`; retain customized or foreign files.

## Synthetic verification

`./scripts/verify.sh` builds serially and checks accounts, CLI/Client, models,
quota, SQLite usage, installer, and existing Python unit tests. It makes no real
provider probes. Optional `--core /absolute/lain-proxy` and
`--host /absolute/Lain.Host` add synthetic process checks; the Host test requires
Lain's port 18418 to be free. Backend reports reside in the reported temporary
directory. These checks do not replace final acceptance for the exact delivery.

The isolated installer test `python3 packaging/test-install.py` also verifies
that unsupported paths leave no installation changes.
`LAIN_VERIFY_SYSTEMD=1 python3 packaging/test-install.py` additionally verifies
generated units for default and space-containing paths using
`systemd-analyze --user verify`, without starting or changing services.
