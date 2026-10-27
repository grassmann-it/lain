# Lain 1.0.0 — historical distribution and Linux operations

Recorded state: 26 October 2026. Verified candidate:
`882bdadd09235b35ca74c0ebdcff59e00061b2fe`, frozen source at
`/home/tomka/.cache/lain-release-1.0-source-20261026`.
The main working copy contains unrelated WIP and is not the build source.
The evidence below applies to the exact r4 packages; later accompanying documents
do not change their source archive or qualification binding.

This is a **historical 1.0.0 candidate record**. The selected prospective release
is [0.8.0](release-0.8.0.md), requiring new bytes and evidence. The old hashes,
versions, and paths below must not be presented as qualified 0.8.0 artifacts.

## Contents and approval state

Public distribution format: one Linux x64 tar archive each for **Monitor** (Host,
CLI, Desktop, skills, no inference Core) and **Gateway** (also Lain's CLIProxyAPI
build), each with an external SHA-256 file. Contents include `LICENSE`, `NOTICE`,
`notices/`, `SHA256SUMS`, installer, maintenance helpers, build/source provenance,
and `source/lain-source.tar.gz` with the exact source. No modified Codex binary
client or VSIX is included. Existing configuration takes precedence over the
initial mode. Installing Monitor cannot silently reduce an existing Gateway state.

The historical packages reside at
`/home/tomka/.cache/lain-release-candidate-r4-20261026/`:

| File | SHA-256 |
| --- | --- |
| `package-monitor.tar.gz` | `9dabf8e1913f72c39bf337b50d42b9390479cf7f5ed25be72e78ba6bc5127e93` |
| `package-gateway.tar.gz` | `55a013bb1e35096e8cc2b8474182d24adf6c93cdb00008704428fa9c3ffce243` |

The corresponding `.sha256` files reference portable filenames. Extraction
retains the `package-monitor` and `package-gateway` directories. Renaming a
same-version download requires updating its outer hash file; archive contents
and internal checksums remain unchanged. This does not permit relabeling these
historical archives as another release.

**Not a publicly approved download.** Core, eleven isolated full-package cases,
native workers and drain, and the native Desktop matrix were checked: eight
maintained UI runs and two complete default runs with 698 checks each. The rebuilt
r4 package includes notice corrections; see the
[packaging review](../packaging/licenses/review-20261026.md).
`releaseQualified: false` remains: at this handoff, the local service still ran
`ff5771c`, revision 58/57. The own-update attempt aborted before service stop
because requests were active; admission was released. The user wanted ongoing
research preserved. Update, Apply/catalog export, skill activation, and the real
final model matrix awaited a quiet window.
[Recorded execution state](release-1.0-execution-2026-10-26.md).

## Download and source

At publication, both archives and hash files, this guide,
[release notes](release-1.0-release-notes.md), and the notice review belong in a
versioned release entry. At this recorded stage, there was no confirmed public
HTTPS download channel or signature trust anchor. Do not invent a download URL.
Hash files protect integrity; hashes obtained from the same source alone do not
prove publisher identity. The following commands apply after obtaining files
from the confirmed release channel.

```sh
cd "$HOME/Downloads"
sha256sum --check package-gateway.tar.gz.sha256
# For Monitor, use package-monitor.tar.gz.sha256 instead.
```

First-party source uses Artistic-2.0; third-party components retain their own
licenses. Photo permission covers Lain, including the edited logo; attribution:
Alexander Huxel · [Webentwicklung Huxel](https://webentwicklung-huxel.de/)
([Flickr](https://flickr.com/people/204852984@N05/)). This is not a general photo
license. The source archive includes pinned third-party source references and
patch inputs, not a complete offline cache of all NuGet/Go/Rust dependencies or
separate upstream archives. Rebuilding requires the corresponding downloads.

## Optional clients: source and patches

Public client delivery consists exclusively of Lain's patches, transformers,
pins, and instructions in the source archive. Local client qualification does
not grant general binary redistribution permission. Users obtain unchanged
clients through each vendor's official channel. Group menus/native usage
extensions require the matching local patch state; ordinary API compatibility
does not guarantee these UI features.

For Codex: extract source into a new directory, then review pins in
`clients/codex/upstream.json` and `clients/codex/README.md`. Provide Python 3,
Git, upstream build dependencies, and exactly Rust 1.99.1. These steps build
locally and install nothing:

```sh
python3 scripts/prepare-codex-picker.py --fetch --output /absolute/new/codex-source
scripts/build-codex-picker.sh /absolute/new/codex-source
```

The preparer binds upstream 0.164.0, archive/patch hashes, and the lockfile;
the builder uses `--locked`, one job, and builds **codex and codex-code-mode-host**.
Replacing only the CLI binary is not a complete runtime package. Flagless daemon
operation additionally requires matching upstream helpers/resources obtained by
the user; review assembly and hash binding in `clients/codex/dispatcher.md` and
`maintenance.md`. Do not publicly upload resources from another local installation.
Included LGPL-2.1 texts do not prove compliance with the conditions of every
affected runtime library. Native acceptance covers only the exact qualified
bytes, not automatically a user's rebuild.

For VS Code: obtain original `openai.chatgpt` 26.1021.22027 Linux x64 from the
vendor. Only Lain's transformer is supplied, with no original bundle or modified
VSIX. Use Python 3.11+, a complete trusted original hash manifest, and the
helper's fixed pins:

```sh
python3 scripts/clients/vscode_groups.py prepare \
  --source /absolute/original-extension \
  --input-manifest /absolute/reviewed-input.json \
  --bundle /absolute/new-local-bundle
```

The target local version is 26.1021.22028. Complete manifest format, qualification,
installation planning, and rollback are in `clients/vscode/maintenance.md`.
Without the complete matching original and independent pins, stop; do not adapt
an arbitrary current Marketplace version. Generated bundles/VSIX stay local.
Installation requires exact native qualification; the user explicitly reloads
the window. The original vendor's terms still apply before local adaptation;
this guide does not claim confirmed modification/redistribution permission.

## Linux: fresh installation

Requirements: Linux x86_64 with compatible native runtime libraries, Bash,
Python 3, tar/gzip, and sha256sum; systemd as the user service manager for the
documented service operation. Desktop requires a graphical session and native
Avalonia dependencies, including fontconfig/X11. No specific accepted distribution
or package list is recorded yet. The finished self-contained package needs
neither the .NET SDK nor Go.

After obtaining a finally verified archive (filenames and directories are
examples, not existing public downloads):

```sh
cd "$HOME/Downloads"
sha256sum package-gateway.tar.gz
# Compare with the confirmed release hash, then extract.
tar -xzf package-gateway.tar.gz
package="$HOME/Downloads/package-gateway"
(cd "$package" && sha256sum --check SHA256SUMS)
bash "$package/install-linux.sh" "$package"
systemctl --user daemon-reload
systemctl --user start lain.service
"$HOME/.local/bin/lain" status --json
"$HOME/.local/bin/lain" ui
```

The directory name must match the actual archive contents. The installer verifies
all inventoried package files and rejects extra files, symlinks, path traversal,
and foreign desktop integration. It neither starts nor enables a service.
Optional: `systemctl --user enable lain.service` for startup at user login;
linger is not configured.

Defaults: data and app at `~/.local/share/lain`, launcher `~/.local/bin/lain`,
unit `~/.config/systemd/user/lain.service`, desktop file
`~/.local/share/applications/lain.desktop`, icon `~/.local/share/icons/lain.png`.
`LAIN_HOME`, `LAIN_SYSTEMD_DIR`, `LAIN_DESKTOP_DATA_DIR`, and the second installer
argument for the prefix allow other absolute paths. Use the same values for
updates. Symlink paths are excluded; LAIN_HOME must not contain quotes,
backslashes, percent signs, or tabs. Bundled skills are not automatically
installed into personal skill directories. The archive contains no automatically
installed patched Codex client.

## Linux: update and backup

For a complete offline update, first stop clients/workers, finish all jobs,
and close Desktop/tray. The installer itself neither drains requests nor checks
for a stopped service. Only then:

```sh
systemctl --user stop lain.service
systemctl --user is-active lain.service
# Expect inactive; separately started Host/Core processes must also be stopped.
```

Then back up using the maintenance helper from a verified package. The variables
below denote absolute paths to choose individually; the snapshot must not yet
exist and must be outside LAIN_HOME:

```sh
lain_state="$HOME/.local/share/lain"
old_package="$lain_state/app"
snapshot="$HOME/lain-backup-before-1.0.0"
python3 "$old_package/packaging/lain-maintenance.py" \
  backup "$lain_state" "$snapshot" --stopped
```

This is a complete private file snapshot including app and credentials, not an
export for support or public release evidence. These commands are documented,
not executed here. The helper checks ownership, file types, hashes, and unchanged
state. Launcher, unit, and desktop integration outside LAIN_HOME are not included;
retain them separately for complete recovery.

Archive an existing `app.previous` outside the installation before another update;
the installer does not overwrite it. Verify and extract the new archive as above,
and set `package` to its absolute directory:

```sh
bash "$package/install-linux.sh" "$package"
systemctl --user daemon-reload
systemctl --user start lain.service
"$HOME/.local/bin/lain" status --json
```

The previous app then resides at `app.previous`; data outside `app` persists.
After startup, verify version/mode and the workflows intended for this release.
A successful installer is not a model/client test.

[service-update.py](../scripts/service-update.py) additionally supports controlled
Gateway updates with verified drain/process/generation checks:

```sh
python3 scripts/service-update.py \
  --inputs /absolute/reviewed-update-inputs.json \
  --inputs-sha256 EXPECTED_INPUT_SHA256
# After successful planning, use the same bound inputs with --execute.
```

This tool needs installation-specific, hash-bound inputs and qualification
evidence. It is not a universal public one-liner; the publisher does not currently
include it as a standalone end-user updater. This does not establish a generated
public update feed.

## Linux: recovery

Switching binaries using `app.previous` proves neither schema compatibility nor
validity of rotating OAuth grants. After a failed update, stop the service and
leave it stopped, close Desktop/clients, and preserve the failed state. Do not
blindly run old programs against already migrated data.

Restore the complete snapshot only into a **new** directory. Use the helper
from the separately retained verified package; the recovery destination's
parent directory must exist:

```sh
recovery="$HOME/lain-recovery-1.0.0"
python3 "$package/packaging/lain-maintenance.py" \
  restore "$snapshot" "$recovery" --stopped
```

Restore overwrites no existing state and writes `restore-requires-reconnect`.
Do not manually delete this marker. Before starting Gateway, explicitly reconnect
rotating credentials or reconcile them with a verified current generation using
the implemented generation recovery. Old refresh tokens may already have been
consumed server-side. The Host provides quarantine; file restore alone does not
prove successful account recovery.

The additional repository helper [service-recovery.py](../scripts/service-recovery.py)
verifies pinned snapshot/helper hashes and defaults to planning:

```sh
python3 scripts/service-recovery.py \
  --snapshot /absolute/private-snapshot \
  --snapshot-manifest-sha256 EXPECTED_SNAPSHOT_MANIFEST_SHA256 \
  --helper-sha256 EXPECTED_MAINTENANCE_HELPER_SHA256 \
  --destination /absolute/new-recovery-home
# Add only for an intentionally selected restore: --execute --stopped
```

It replaces no installation, switches no unit, and starts no backend process.
Switch to the recovery home only with a snapshot-compatible app, completed
credential recovery, and verified accounts/history/routes. The concrete
production-ready user workflow for account recovery remains to be demonstrated
in final acceptance; no invented CLI command is given here. Do not start a
quarantined backup with a historical unprotected Host. See the
[recovery contract](linux-delivery-stabilization.md).

## Linux: removal

First finish all jobs and close clients/workers and Desktop/tray. For a default
installation, then run:

```sh
systemctl --user disable --now lain.service
rm -- "$HOME/.config/systemd/user/lain.service"
systemctl --user daemon-reload
rm -- "$HOME/.local/bin/lain" \
  "$HOME/.local/share/applications/lain.desktop" \
  "$HOME/.local/share/icons/lain.png"
```

Before removal, verify these files belong to your Lain installation. For custom
installation paths, use the actual prefix, LAIN_SYSTEMD_DIR, and
LAIN_DESKTOP_DATA_DIR values. Separately running Host/Core processes must also
be stopped cleanly. Data under `~/.local/share/lain` remains. After a stopped
private backup, intentionally archive or remove this directory with
`app`/`app.previous`; it contains private accounts and history. There is no
automatic purge or claimed uninstaller. Separately installed skills, custom
Codex clients, VS Code adaptations, and provider access are not removed or
revoked by this procedure. For VS Code, use the documented qualified rollback;
for Codex, undo its dispatcher according to its installation receipt. Deleting
files does not revoke provider grants.

## Before public upload

Making the repository public was authorized; that did not publish a public 1.0
release with tag and downloads. Tracked history, candidate, and actual remote
state are checked before switching visibility. New accompanying documents and
CI contracts must live on a separate clean publication state; the ongoing main
working copy is preserved. The r4 archives remain bound to source882. Document
full approval and actual installation/model acceptance separately; do not derive
macOS/Windows product approval from installer fixtures.
