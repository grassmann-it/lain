# UI structure delivery

This release updates CLI, Host and Desktop while keeping every byte of the
accepted expansion package's `proxy/` directory. Core remains SHA-256
`bab5ff3fdce446f3bae4c4aa495ef6b5d42a8d14f40306734f79368e5bb92d87`.
The baseline is `artifacts/lain-linux-x64-0.1.0-expansion-20261024`; it is a
read-only input. The older `stage-component-refresh.py` intentionally retains
its older-Core guard. Do not change that guard or rebuild Core for this update.

## Branding

`Branding.Logo` supplies an `IImage` for the title bar; `Branding.WindowIcon`
supplies the Avalonia window icon. Both decode one embedded original PNG.
MainWindow sets its `Icon` property and places the logo beside the brand text.
The controls scale the image to fit their allotted bounds. The original is
1254 × 1254 RGBA with alpha extrema 0–254; it has not been transformed.
Its SHA-256 is `d36cb05911456a363b275631ad7a3b1176770f0f052d2e9747593e33f947a6fd`.
Origin and the absence of a supplied separate license statement are recorded in
`src/Lain.Desktop/Assets/Brand/README.md`. Existing font OFLs and third-party
notices remain required.

## Stage a new package

Freeze and commit the integrated source before serial locked restores and fresh
self-contained linux-x64 publishes of all three application projects. Use the
same source commit and full informational version for all three publishes.
Supply `-p:IncludeSourceRevisionInInformationalVersion=false` if specifying a
short informational version, so the SDK does not append another revision.
Then use a fresh output directory outside the checkout and every input:

```sh
python3 scripts/stage-ui-structure-release.py \
  /absolute/artifacts/lain-linux-x64-0.1.0-expansion-20261024 \
  /absolute/new/lain-linux-x64-ui-structure \
  --source-commit FROZEN_COMMIT \
  --informational-version 1.0.0+FROZEN_SHORT_SHA \
  --cli /absolute/frozen-cli --host /absolute/frozen-host \
  --desktop /absolute/frozen-desktop
```

The helper validates the baseline manifest and exact qualified Core, frozen
source, complete DLL version attributes, RID/self-contained configuration,
restored locked graphs, embedded provider catalogs, unchanged original logo
both in the assembly and publish, and original font OFLs. It replaces each
whole component directory, refreshes installation assets and source provenance,
and recollects original notices from the local caches without network requests.
Go notice collection needs the matching pinned Go toolchain (`LAIN_GO` if needed).
It checks every application file and the whole proxy directory after staging,
and verifies neither publishes nor baseline changed during collection.

Prior acceptance evidence is moved under `historical/expansion-baseline/`.
Current release and expansion gates are false. The exact Core qualification
scope remains unchanged. Staging is not installation or acceptance. On failure,
remove or inspect the partial new staging directory; never install it. The
helper creates a normalized gzip archive and SHA-256 sidecar after manifest
verification. It never modifies existing archives, installed files, services,
credentials, configuration, preferences or usage databases.

## Package and installed validation plan

The integrator owns serialized execution and installation. Before approving:

1. Run `python3 packaging/test-install.py` in the integrated checkout. It uses
   temporary fake executables and verifies absolute launcher selection, argument
   forwarding, desktop quoting for spaces/quotes/dollars/backticks/percent signs/
   backslashes, original icon bytes, update backup and a retained user-data file.
   It also verifies customized user artwork is preserved by refusing replacement
   before application changes. Desktop integration ownership uses exact paths
   and hashes in `LAIN_HOME/desktop-integration.json`.
   If available, it also runs `desktop-file-validate`. No service is started.
2. Verify staged `SHA256SUMS` and archive checksum independently. Compare every
   baseline `proxy/` file hash with staged files, including qualification,
   source and build manifests. Check the Core and artwork hashes above.
3. Check current per-component source/version/assembly hashes, source-provenance
   inventory, original font OFLs and current notices. Verify current gate false
   and historical positive UI evidence marked as previous.
4. On private Xvfb at 1120 × 780 and 800 × 560, inspect title bar logo and window
   icon, initial Accounts and all five destinations. No user screen capture.
5. Before installation, close the owned GUI and follow the controlled Host
   restart/update procedure because Host changes. Retain a rollback copy and
   snapshot the paths/hashes/counts of private configuration, account records,
   preferences and usage data. Never remove the surrounding data directory.
6. Install only the verified new package. Compare installed manifest and entire
   executable component/proxy file inventories against the package. Verify
   absolute `Exec=... ui`, absolute icon location and identical original icon
   bytes; launch through the desktop entry in the isolated desktop session.
7. Confirm private data/preferences survive, current applied-profile metadata
   and five destinations work, and acceptance records bind the exact installed
   package. Write positive release gates only after those independent checks.

The installer changes only application files, its own launcher/service/desktop
entry/icon and its rollback copy. It performs no service start/enable. Updates
preserve data outside `app/`. Uninstall instructions in `docs/install.md` name
only owned application/integration files and explicitly retain private data.
