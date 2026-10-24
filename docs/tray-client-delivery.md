# Tray, notifications and CLI client delivery

Use `scripts/client-refresh.py` for this specific client update over the installed
`a66cb2a19774d897702724f9f40755b74c82b2a6` Host. The existing
`stage-ui-structure-release.py` requires three new publishes and replaces Host;
`stage-desktop-refresh.py` preserves CLI. Neither matches this update.

The new helper replaces whole `cli/` and `desktop/` publish directories, including
their own local `Lain.Client.dll` and .NET `Lain.Core.dll`. It never replaces
`host/` or `proxy/`, never changes the launcher/service/desktop entry, and never
opens private credentials, configuration, preferences or usage databases. The user
explicitly selected a replacement logo; only its verified owned launcher icon and
the corresponding ownership-map entry are additionally replaced.
The Go Core remains `bab5ff3fdce446f3bae4c4aa495ef6b5d42a8d14f40306734f79368e5bb92d87`.
Host remains assembly SHA-256
`06fe63b0c845348a7949d95afe8ba0f8eb65bc06063d0ad473c55f88f5b57cb9`.
No Host/service restart or daemon reload is required.

The new original is `/home/tomka/Downloads/00CHR/icon-review-20261024/lain-icon.png`,
copied unchanged as `Assets/Brand/lain-cat.png`, SHA-256
`b6d32ab02ca21965f6941f61ec209ff756c7ea9841641cf20c0f5bda4aefaba7`.
Stage records both the prior baseline artwork hash and this new original hash.
Earlier stages from the superseded icon/source are obsolete and must be rebuilt.

The current installed gate still says `releaseQualified=false`; completed older
observations live in separate evidence. Staging accepts its exact valid manifest
and backend-qualified Core, preserves that historical false gate in provenance,
and sets its own new application acceptance flags false. It invents no acceptance.

## Freeze and publish

The integrator runs builds and installation. Commit the final Tray/CLI source
first, including this helper. Old uncommitted documentation/evidence is outside
its source-freeze check; do not overwrite or commit it as part of this procedure.
Uncommitted application or packaging/notice source changes cause refusal.
Use a clean isolated publish checkout at the frozen commit if needed. All commands
below are preparation guidance, not commands executed by the delivery worker.

```sh
TRAY_REV=$(git rev-parse HEAD)
TRAY_SHORT=$(git rev-parse --short=7 "$TRAY_REV")
TRAY_VERSION="1.0.0+$TRAY_SHORT"
TRAY_PUBLISH="/tmp/lain-client-publish-$TRAY_SHORT"
TRAY_PACKAGE="/tmp/lain-client-release-$TRAY_SHORT/lain-linux-x64-tray-cli"
```

Use fresh publish directories. Restore the exact locked linux-x64 self-contained
CLI/Desktop graphs. Restore Host's unchanged graph solely for notice inventory;
do not publish/rebuild Host or build the Go Core. The CLI/Desktop publishes may
build their local .NET Client/Core project references:

```sh
dotnet restore src/Lain.Cli/Lain.Cli.csproj --locked-mode -r linux-x64 -p:SelfContained=true -m:1 -p:BuildInParallel=false -nr:false
dotnet restore src/Lain.Desktop/Lain.Desktop.csproj --locked-mode -r linux-x64 -p:SelfContained=true -m:1 -p:BuildInParallel=false -nr:false
dotnet restore src/Lain.Host/Lain.Host.csproj --locked-mode -r linux-x64 -p:SelfContained=true -m:1 -p:BuildInParallel=false -nr:false
dotnet publish src/Lain.Cli/Lain.Cli.csproj --no-restore -c Release -r linux-x64 --self-contained true -o "$TRAY_PUBLISH/cli" -m:1 -p:BuildInParallel=false -nr:false -p:ContinuousIntegrationBuild=true -p:Deterministic=true -p:DebugType=None -p:DebugSymbols=false -p:InformationalVersion="$TRAY_VERSION" -p:IncludeSourceRevisionInInformationalVersion=false -p:PathMap="$PWD=/_/lain"
dotnet publish src/Lain.Desktop/Lain.Desktop.csproj --no-restore -c Release -r linux-x64 --self-contained true -o "$TRAY_PUBLISH/desktop" -m:1 -p:BuildInParallel=false -nr:false -p:ContinuousIntegrationBuild=true -p:Deterministic=true -p:DebugType=None -p:DebugSymbols=false -p:InformationalVersion="$TRAY_VERSION" -p:IncludeSourceRevisionInInformationalVersion=false -p:PathMap="$PWD=/_/lain"
```

The stage helper checks the complete version in CLI/Desktop and both local shared
assemblies, exact source/locked restored graphs, linux-x64/self-contained metadata,
matching shared library bytes across the two publishes, original cat/font notices,
and unchanged Host/shared contract source versus the baseline (RID-only lock changes
are allowed, with Host's notice graph separately required to remain identical).
It refuses new shared API changes; those need a different compatibility/restart plan.

## Stage and review

The installed app is a read-only baseline, with only `.lain-package` excluded from
package inventories. Host writes its data outside that directory. Choose a matching
pinned Go toolchain via `LAIN_GO` if needed for the existing notice collector.

```sh
python3 scripts/client-refresh.py stage \
  --base /home/tomka/.local/share/lain/app \
  --cli "$TRAY_PUBLISH/cli" --desktop "$TRAY_PUBLISH/desktop" \
  --output "$TRAY_PACKAGE" \
  --source-commit "$TRAY_REV" --informational-version "$TRAY_VERSION"
(cd "$TRAY_PACKAGE" && sha256sum --check --strict SHA256SUMS)
(cd "$(dirname "$TRAY_PACKAGE")" && sha256sum --check "$(basename "$TRAY_PACKAGE").tar.gz.sha256")
```

The new full package inherits the entire unchanged baseline and replaces only
CLI/Desktop, recollected notices, the CLI usage guide and usage skill, and client
provenance/metadata. Host's original
source provenance and Go notice directory remain intact. `client-refresh.json`
binds the exact complete installed baseline file inventory. The helper checks
inputs before/after, generates the new manifest and normalized archive, and refuses
any changed/new path outside the installation allowlist. A failed stage may leave
an incomplete new output; never install it. Baseline archives remain untouched.

Inspect the exact published CLI JSON/human output and private Tray/notification
fixture results, the staged manifest, version/provenance and original artwork.
Compare every retained Host/Go-Core file against the baseline. Retain prior
acceptance as historical; write current positive application gates only after the
new client checks. If acceptance metadata is added to the staged package, it must
stay inside the existing metadata/provenance allowlist and the manifest/archive
must be regenerated; adding arbitrary new top-level files is refused at install.

## Install the narrow delta

Exit the GUI completely with its Quit action. Tray Hide/closing to the tray leaves
a running client and the installer refuses that state. Do not stop the Host.
Record its existing PID/readiness and private-data invariants for the independent
before/after check. Run the **exact staged helper**, which checks its own manifest
hash. The backup must be a fresh path under `LAIN_HOME`, outside `app/`, on the
same filesystem; it must not overlap the staged package.

```sh
python3 "$TRAY_PACKAGE/client-refresh.py" install \
  --package "$TRAY_PACKAGE" \
  --home /home/tomka/.local/share/lain \
  --backup "/home/tomka/.local/share/lain/backups/clients-$TRAY_SHORT"
(cd /home/tomka/.local/share/lain/app && sha256sum --check --strict SHA256SUMS)
/home/tomka/.local/bin/lain status --json
```

Installation verifies exact baseline parity and refuses running installed CLI/GUI
processes. It prepares copied payloads and uses Linux `renameat2(RENAME_EXCHANGE)`
for each existing client/metadata item; new metadata paths use rename. The Host,
Go-Core and all other baseline files must remain identical. Private application
data and all integration except the explicitly selected owned icon/map are outside
the write set. The two explicitly allowlisted client
documentation files are `docs/cli-usage.md` and `skills/lain-usage/SKILL.md`.
No service command is issued.

For the icon, install discovers exactly one absolute key ending `/icons/lain.png`
in `LAIN_HOME/desktop-integration.json`. Its old hash must match the baseline PNG
and the current file bytes. The same-prefix `/applications/lain.desktop` must be
another verified ownership-map key, and its `Icon=` must name that exact icon.
Foreign/custom artwork, missing or ambiguous ownership, symlinks and a changed
desktop entry are refused before application changes. No arbitrary icon path is
accepted. This also supports isolated installer tests: their temporary ownership
map must contain the same exact icon/desktop pairing under the test prefix.

The icon and ownership map must share the backup/app filesystem. Atomic exchanges
preserve their old exact bytes under `backup/external/lain.png` and
`backup/external/desktop-integration.json`. Only the icon key's hash changes in the
map; other entries and the actual `.desktop` file stay unchanged. The transaction
journal records exact paths and prior/new hashes. Both external swaps participate
in ordinary-error rollback. The active-client refusal remains in place.

Ordinary exceptions trigger reverse exchanges and a verified restoration to the
baseline. `backup/previous/` preserves replaced directories/files and
`transaction.json` records completed operations. Each replacement is atomic; the
whole multi-item operation is not a power-loss transaction. If interrupted by a
power loss, preserve that backup and inspect the journal before another update.
Do not delete private data or restore Host/Go-Core as part of client rollback.

After installation compare every installed manifest file with the staged package,
retain unchanged Host PID/readiness and exact Host/Core hashes, verify existing
accounts/preferences/history, and run the private client/Tray acceptance. Native
launching must use a separate private `XDG_RUNTIME_DIR` for the new single-
instance tray behavior; never attach an acceptance probe to the user's GUI.
