# Private Linux delivery and recovery

This workflow produces private `linux-x64` artifacts. It does not select an
application license, publish a release, start services, or authorize provider
requests. `gate-status.json` keeps final acceptance separate from binary/source
integrity. A successful publish is a staged artifact, not product qualification.

## Repeating a release

Use a committed source checkout and the SDK pinned in `global.json`. The
integrator serializes builds/publishes and runtime checks. Run, using a new
absolute output directory each time:

```sh
scripts/publish-linux.sh --mode monitor /absolute/output/lain-monitor
scripts/publish-linux.sh --mode gateway /absolute/output/lain-gateway
```

The default mode is Gateway for compatibility. Monitor publishes Host, CLI and
Desktop without building/bundling Core, fetching/preparing Core sources, checking
its model catalog or collecting Go binary notices. Application NuGet/runtime and
font notice collection still applies. `LainRuntimeMode` is passed to restore and
publish so project content rules can omit optional Core/catalog inputs.

Gateway retains pinned Core source preparation, binary/source manifest binding,
model catalog agreement, Go notices and optional exact-binary qualification.
`LAIN_QUALIFICATION_FILE` only qualifies its exact Gateway binary/source hashes;
no qualification is invented for Monitor.

Both package formats include `package-metadata.json` (`format`, `runtimeMode`,
`sourceCommit`, `runtime`), `build-info.json`, exact component informational
versions/assembly and shared-assembly hashes, explicit publish properties and
project/lockfile provenance. Informational versions include the frozen Git
revision. Gateway adds Core identities; their absence in Monitor is intentional.
`SHA256SUMS` covers every shipped file. The archive uses sorted names, the commit
mtime (or `SOURCE_DATE_EPOCH`), numeric owners and timestamp-free gzip; exact
reproducibility still needs independent builds with identical toolchains and
inputs and must not be inferred from those flags alone.

## Install and update

From any working directory:

```sh
bash /absolute/package/install-linux.sh /absolute/package /absolute/prefix
```

`LAIN_HOME`, `LAIN_SYSTEMD_DIR` and `LAIN_DESKTOP_DATA_DIR` can select isolated
locations. The installer validates exact file coverage, hashes, safe paths and
required executables before updating. Monitor must have no `proxy/` directory;
legacy packages without metadata are Gateway. No service is started or enabled.

A fresh state directory receives `initial-runtime-mode` only when neither
`configuration.json`, an earlier selector nor a managed app exists. Existing
configuration/selector dominates package defaults. Installer does not edit
accounts, credentials, configuration, profiles, routes, history or other data.
Gateway package updates can keep a Monitor runtime configuration. Replacing a
package containing Core with a Monitor package is refused, so a package update
cannot silently remove a working Core. Choose Monitor runtime explicitly while
retaining the Gateway package when retiring Gateway activity.

An update retains `app.previous` and refuses further updates until it has been
explicitly archived elsewhere. This copy preserves application binaries only;
it is not a safe data/schema/OAuth rollback. Preserve a full stopped snapshot
before any update requiring a rollback path.

## Stopped backup and explicit recovery

Stop every writer, including Host, owned Core and CLI maintenance operations.
The operator must acknowledge this with `--stopped`; the helper additionally
checks own running process environments/command paths and compares source and
copied inventories before committing. A filesystem copy cannot prove that a
future writer will not start. Keep services stopped throughout the operation.

```sh
python3 scripts/lain-maintenance.py inspect /absolute/lain-home
python3 scripts/lain-maintenance.py backup /absolute/lain-home /absolute/new-snapshot --stopped
python3 scripts/lain-maintenance.py restore /absolute/new-snapshot /absolute/new-recovery-home --stopped
```

The packaged helper is `packaging/lain-maintenance.py`. Backup includes all of
LAIN_HOME (including app, SQLite sidecars, journals, configuration, current and
older private credential generations, source/cursor identities and profile
artifacts). External launcher/systemd files, external provider CLI stores and
normal Codex configuration are outside this scope. No retention or deletion is
performed. Snapshot file hashes are private credential-sensitive material; do
not publish them as acceptance evidence.

Source and snapshot must be owned by the current user, contain only regular
files/directories, and have no symlinks. New snapshots/restores require a new
destination outside the source. Integrity failure aborts before installing any
restored state. Failed staging is cleaned up; existing state/backups are never
overwritten or deleted. Files remain private. Inspect reports file count and
bytes only; it does not truncate/archive journals or change history.

Every restore writes `restore-requires-reconnect`. A restored local refresh
token may already have been spent remotely. The integrated Host must therefore
quarantine rotating credentials and prevent Core startup, while allowing
history, static account reads and explicit reconnect/generation recovery.
Removing this marker manually is not a recovery procedure. A generation-aware
recovery must retain a verified current generation or require reconnect before
re-enabling rotating grants. This runtime contract is owned by the account
foundation and integrator; the filesystem helper alone does not qualify OAuth
recovery or schema downgrade compatibility.

Restoring to a new home preserves the current installation for investigation.
Use a schema-compatible application from that snapshot, complete explicit
credential recovery, verify retained account IDs/history/routes and only then
switch the service/launcher to the recovered home. Do not copy `app.previous`
over newer state or claim a binary swap proves rollback. Service switching and
real installations belong to the integrator's runtime window.

## Evidence and remaining qualification

`python3 tests/delivery-stabilization/test_delivery.py` exercises synthetic
filesystem packages and stopped snapshots only. `python3 packaging/test-install.py`
checks the pre-existing isolated installer contract. These tests start no service,
perform no provider request and use no user installation. They cover fresh
Monitor/legacy Gateway, retained mode/data through upgrade, refusal of silent
Core removal, retained rollback binaries, exact package inventory, corruption,
no-overwrite restore and recovery marker creation. They are not real binary,
Host recovery, journal torn-write/full-disk, X11/Wayland or provider acceptance.

Actual Monitor/Gateway publishes and isolated execution of exact binaries,
schema-specific rollback and credential quarantine/reconnect must be recorded
separately with source/artifact identity and test results before release approval.
