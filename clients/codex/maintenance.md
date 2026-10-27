# Qualified client maintenance

N08 implements conscious maintenance of the existing 0.164.0 client. Source is
prepared and tested with disposable fixtures; this does not migrate an installed
dispatcher. The existing [delivery](../../docs/codex-model-groups-delivery-2026-10-25.md)
still describes the real earlier installation.

AP1 (26 October 2026) extends schema 3 with qualified runtime dependencies and
an early helper integrity check. Its **56 offline Python tests pass**; no live
client state, config, auth, system helper or service was changed. Production
helper qualification now admits the authenticated official 0.164.0 helper below;
this source/registry update does not migrate production. An older installed checker does not know AP1; use the
trusted updated repository checker to review existing state before migration.

The original AP1 qualification records the Original SHA256
`b93f6811d3f653f0fb83be4050d857fb9abbb111dfa2ce160563327daff7f306`
and patched SHA256
`5b3a058525925731f12eec3366fd6dff74f716fb2967791e4531e88e7a06d484`.
Both are pinned to upstream version 0.164.0; the latter is the `lain-model-picker`
variant. These are byte identities, not a filename/version-string heuristic.
Status looks up the versions by known SHA; it never executes an unknown
executable to ask its version. The recognizer and template separately require
`codex-0.164.0`. Adding an unknown grammar to the JSON cannot enable a parser
that has not been implemented. A future version needs separate source/parser,
lock/toolchain and native qualification before its pins can be added. No
download, rebuild or silent upgrade is performed.

## Helper qualification and packaging (AP1)

The reported failure was a copied `codex-lain` 0.164.0 without its code-mode
host. Every `functions.exec` failed before launching the requested command.
The user's manual link to `/usr/lib/chatgpt/resources/codex-code-mode-host`
restored execution, but provides no durable compatibility or byte pin: a
desktop update can replace that target independently.

The pinned upstream commit `6aaac694803004a63c6f393d153c96d22521a6b9`
defines a separate `codex-code-mode-host` package/binary in
`codex-rs/code-mode-host/Cargo.toml`. `InstallContext::code_mode_host_program`
in `codex-rs/install-context/src/lib.rs` prefers a packaged resource, then a
helper beside the current executable for this loose-binary layout. The local
installer previously copied only the CLI. The parent updated
`scripts/build-codex-picker.sh` to select both CLI and helper in one locked build.
Upstream `.github/workflows/rust-release.yml`
includes the code-mode host in its primary and app-server Linux releases.
The primary release also includes `codex-responses-api-proxy` and `bwrap`;
AP1 covers the missing code-mode host, not a claim to reproduce that full bundle.

The parent's combined build failed with exit 101 while fetching rusty_v8
152.2.0's official prebuilt Rust binding (HTTP 404), before V8 source compilation.
The structured blocker is retained privately in
`~/.cache/lain-qualification-20261026/ap1-helper-provenance-r1/source-build-blocker.json`;
it explicitly records that no full build log was retained. No different V8 or
additional Rust build was used.

The official `rust-v0.164.0` release provides a qualified alternative:

- Helper SHA256: `8ee4f472e92ac226a189e98998633b2d07f989f208515750764db660369f2926`.
- Archive: `codex-code-mode-host-x86_64-unknown-linux-musl.zst`, SHA256
  `2ec1a16d1e872667c0311c57fa7306e0c3ec90d69f1a3284775786cb0a05cf8a`.
- Sigstore 4.5.1 verified the artifact signature, Fulcio chain, Rekor signed
  entry timestamp, Merkle inclusion proof and signed checkpoint against the
  production TUF trust data. Certificate policy required the exact OpenAI
  `rust-release.yml` workflow identity, tag, repository and pinned commit above.
  Altered artifact and source-commit negative checks were rejected.
- The installed patched CLI pin above and explicit official sibling helper
  passed the independent `tests/codex-picker/native_code_mode_host.py --run`
  fixture: `functions.exec` called `tools.exec_command`, a real shell succeeded,
  its matching output reached the second loopback provider request, and the CLI
  returned the final message. The exact running helper SHA was observed.
  Synthetic home was mounted at the original HOME, CODEX_HOME stayed unchanged,
  and no production credentials/configuration or external provider was used.

The registry records `provenance: qualified-release` and the portable
[qualification summary](code-mode-host-0.164.0.qualification.json), SHA256
`5689f88fb1a188cc5e632480f70f5a44e5ba8a0518d6dd3d5d718128ed0ca205`.
The full private report remains in
`~/.cache/lain-qualification-20261026/ap1-official-helper-r1/qualification.json`,
SHA256 `6f332f813c49662d3b417f123847686e186d26b9a2c10c0dbe8657fb673676b5`.
Native evidence is retained in sibling `ap1-official-helper-native-r1`.
This qualifies the concrete CLI/helper shell-and-continuation pair; actual
WorkerBatchRunner sandbox/worktree acceptance is separate. The system helper
`6f39ebe2…` remains unqualified because its exact source/version is unproven.

That separate product-path acceptance passed in
`tests/workers-acceptance/native_runner.py`: the genuine compiled
`WorkerBatchRunner` and installed-artifact binding drove the same pinned CLI,
without a diagnostic wrapper. Both native configuration gates passed before
dispatch, the read-only shell was denied a source write, and the write shell
created `NATIVE_WRITE_OK` in the receipt's verified detached native worktree.
Each synthetic alias made exactly two loopback requests with a real tool
continuation. The synthetic source was explicitly trusted before launch;
base configuration stayed byte-for-byte unchanged. HOME/CODEX_HOME were not
reassigned, and no production state or credentials were used.

The final receiver DLL SHA256 is
`85fd262eefc32a13cdb9dc5ab564e4545025e2a51870863cd0bf127f77bb486e`.
Its complete assembly snapshot, source manifest, exact command and limits are
retained in
`~/.cache/lain-qualification-20261026/workers-native-runner-r2/acceptance.json`,
SHA256 `520908fd5982d463336333354c01729c3faf2aedfaefc8095d3668a3d880bc35`.
The earlier concurrent no-turn probes exposed a fatal SQLite state-runtime
initialization failure; parent source now serializes all probes before parallel
worker execution. A system-skills directory race was also observed, but was
nonfatal in the focused probe. Diagnostic attempts remain retained as failures.
No further native negative trust case was run: parent refusal tests cover
unset/untrusted sources, and this native positive case proves preservation.
This acceptance uses the existing CLI pin and does not qualify a future picker
rebuild or any real external provider/account delivery.

After reviewing the actual helper bytes and compatibility evidence, extend
the existing `patched[PATCHED_SHA256]` record in `qualified-releases.json`:

```json
{
  "runtimeDependencies": {
    "codex-code-mode-host": {
      "sha256": "FULL_QUALIFIED_HELPER_SHA256",
      "version": "0.164.0",
      "upstreamCommit": "6aaac694803004a63c6f393d153c96d22521a6b9",
      "provenance": "same-source-build",
      "evidence": "PATH_TO_REVIEWED_BUILD_AND_NATIVE_QUALIFICATION"
    }
  }
}
```

This is the record shape, not usable qualification. The real lowercase SHA256
must identify the helper executable; the version and source commit must match
the enclosing patched record. `provenance` is `same-source-build` or
`qualified-release`, with nonempty evidence. Evidence must establish the build
inputs or authenticated release artifact and native compatibility of this
specific pair. A filename, `--version`, system installation path, user-supplied
SHA alone or this synthetic test suite does not establish that evidence. No
candidate executable is run by qualification/status or maintenance commands.

Install requires `--code-mode-host PATH --code-mode-host-sha256 SHA` in addition
to Original/Patch pins. It stages an independent executable helper copy at
`state/codex-code-mode-host`, hashes and fsyncs the copy before publication and
checks it again after entry replacement. Helper updates use a streamed,
verified atomic file replacement; no external helper is changed. Installed
helpers must be regular nonsymlink single-link files owned by the current
user, executable and protected from other writers. Hardlinks to an updater's
helper and symlinks to system packages are refused even when their bytes match.

Schema 3 now binds the complete `runtimeDependencies` record into both manifest
and receipt; the wrapper embeds its required SHA. Older schema-2/3 state without
that binding is not ready. Status reports `helper-qualification-missing`,
`helper-binding-upgrade-required`, or `helper-missing-or-changed` as appropriate.
A patched launch hashes both the selected executable and the helper, and exits
126 before exec on helper failure. It never falls back to Original. Ordinary
calls/subcommands check only the selected Original, so a broken patched helper
does not prevent their execution.

## Status and receipt contract for Profiles

Use the trusted repository checker before an installed maintenance checker
exists, or the checked installed copy after an explicit install/rebind:

```sh
python3 scripts/clients/install_codex_dispatch.py status --json
python3 /absolute/state/maintenance.py --state /absolute/state status --json
```

Status writes no state, acquires no mutation lock, executes no Codex binary and
reads no Codex configuration, credentials or database. It hashes the selected
executables, wrapper and maintenance files. Results describe this checked local
snapshot, not a lease against a future updater or a future shell PATH change.
Operational results exit 0 and have these direct outer fields:

```json
{
  "schemaVersion": 1,
  "code": "ready",
  "readyForGroups": true,
  "issues": [],
  "manifestSha256": "FULL_MANIFEST_FILE_SHA256",
  "receipt": {
    "schemaVersion": 1,
    "entry": "/absolute/bin/codex",
    "state": "/absolute/state",
    "generation": "OPAQUE_MAINTENANCE_GENERATION",
    "manifestSha256": "FULL_MANIFEST_FILE_SHA256",
    "dispatcherSha256": "FULL_WRAPPER_SHA256",
    "originalSha256": "FULL_ORIGINAL_SHA256",
    "patchedSha256": "FULL_PATCHED_SHA256",
    "grammarVersion": "codex-0.164.0",
    "originalVersion": "0.164.0",
    "patchedVersion": "0.164.0",
    "runtimeDependencies": {
      "codex-code-mode-host": {
        "sha256": "FULL_QUALIFIED_HELPER_SHA256",
        "version": "0.164.0",
        "upstreamCommit": "6aaac694803004a63c6f393d153c96d22521a6b9",
        "provenance": "same-source-build",
        "evidence": "PATH_TO_REVIEWED_BUILD_AND_NATIVE_QUALIFICATION"
      }
    },
    "maintenance": "/absolute/state/maintenance.py",
    "maintenanceSha256": "FULL_MAINTENANCE_SOURCE_SHA256"
  },
  "upstream": {
    "boundResolvedPath": "/absolute/observed/upstream/release/codex",
    "currentResolvedPath": "/absolute/observed/upstream/release/codex",
    "changed": false
  }
}
```

`code` is `ready`, `not-installed`, `maintenance-required` or `unrecognized`.
Every issue has `code` and a readable `message`. A not-ready result may have a
null receipt or only `entry`, `state` and `manifestSha256`; it must never enable
the group target. A legacy schema-2 manifest gets
`receipt-upgrade-required`. Missing/changed fallback bytes, identity changes,
upstream release path drift, unsupported versions/grammar, wrapper binding
errors and stale/changed receipts produce explicit diagnostics. A malformed or
foreign launcher fails closed. A top-level manifest hash, when available, also
supports inspecting a partially completed maintenance transaction.

The stored `state/receipt.json` contains the **inner receipt fields directly**,
with no envelope. Before executing its `maintenance.py`, Profiles must read
bounded regular nonsymlink receipt/manifest files, check the manifest hash,
compare fixed expected absolute `entry`/`state`/maintenance paths, and hash the
regular nonsymlink maintenance script against `maintenanceSha256`. Missing
fields or an older receipt mean maintenance is required. Then invoke only
`status --json` with timeout/cancellation, and require `readyForGroups: true`.
Do not invoke install/rebind from a status check. These receipts provide local
consistency and generation checks, not a signature against the same local user.
A trusted packaged checker can instead inspect the state without executing its
maintenance copy; its packaging path belongs to N07/Root.

Use the checked absolute `receipt.entry` for the launch, or explicitly verify
that shell PATH resolves `codex` to it. Status does not inspect the user's shell
configuration or prove another executable earlier in PATH is this dispatcher.
The group launch is `ENTRY --no-daemon --profile lain`. Upstream 0.164 loads
`$CODEX_HOME/lain.config.toml` for that real CLI profile. A private CODEX_HOME
also needs that profile filename and the real `--profile lain`; a private
directory or a legacy TOML profile name alone does not activate the patch.
Status does not validate generated profile/catalog contents, Host revision,
provider entitlement or the current native menu; N05 checks its artifact and
revision contracts separately. Old receipts must be rechecked after maintenance.

## Conscious rebind and update

Install accepts only the qualified Original/Patch/helper tuple and explicit binary
SHA pins. A schema-3 installation also writes its maintenance script, template,
qualification registry and receipt to its own state directory. Normal calls and
subcommands continue to use the chosen resolved Original; no flags, cwd, user
config or environment entries are changed. The shebang `argv[0]` limit in the
[dispatcher contract](dispatcher.md) still applies.

Before each exec the AP1 wrapper hashes its selected target and, on the patched
branch, its required helper. Missing or
changed selected bytes exit 126 with an actionable status/rebind message. No
changed binary is silently accepted and a failed patched branch never falls
back to upstream. This adds streaming hash reads to each invocation; it changes
no executable bytes. Concurrent external updates between hash and exec remain
outside the quiet-operation contract.

An upstream `current` release switch is reported while the dispatcher continues
to use the chosen intact Original. Rebind never follows that switch silently.
Choose an existing qualified executable, the exact known version/hash and the
manifest hash just reviewed in status:

```sh
python3 scripts/clients/install_codex_dispatch.py rebind \
  --original /absolute/chosen/release/bin/codex \
  --original-version 0.164.0 --original-sha256 FULL_KNOWN_ORIGINAL_SHA256 \
  --expect-manifest-sha256 FULL_REVIEWED_CURRENT_MANIFEST_SHA256 \
  --code-mode-host /absolute/qualified/codex-code-mode-host \
  --code-mode-host-sha256 FULL_QUALIFIED_HELPER_SHA256
```

This is a read-only plan. Place `--apply` **before** `rebind` only for the
separately authorized mutation. The same operation can upgrade the current
schema-2/3 launcher with its unchanged original, or recover a missing fallback
using another existing qualified copy. It requires the exact still-owned entry
and wrapper, intact patched binary and unchanged reviewed manifest. Unknown
bytes/version/grammar, recursive fallback, stale manifest and a foreign replaced
entry are refused. Rebind does not adopt another installer's new launcher.

The helper options are required for a missing helper, migration from the old
layout, or a newly qualified helper update. Omit both to retain an installed
copy that already matches the current qualification. Dry run verifies its
inputs but writes no files. Supplying only one option is refused. A regular
damaged/missing managed helper can be repaired explicitly from qualified bytes;
its prior damaged state cannot be restored as a qualified runtime.

The reported manual system-helper symlink is diagnosed and left untouched,
including its external target. Rebind refuses that link even with an explicit
qualified source; it is outside the regular-file backup contract. Parent must
inspect and preserve its exact target/identity in a quiet maintenance window
and explicitly resolve the workaround before retrying migration. AP1 does not
automatically remove or adopt it.

Rebind changes wrapper/manifest/receipt, its own maintenance support files and
the required helper when explicitly supplied. It retains the entry symlink,
patched binary and every ordinary Codex
config/profile file. The observed upstream pointer baseline is recorded anew;
the user-selected fallback may be a different qualified copy. A deliberately
acknowledged absent upstream pointer may remain null while that selected
fallback is intact. After rebind, uninstall restores the explicitly selected
Original's canonical path; the exact initial upstream symlink target remains
in the original backup for audit. This avoids uninstall unexpectedly selecting
a later unknown `current` release.

## Reversibility and interruption

Before mutation, `maintenance-history/TRANSACTION/previous/` stores every
managed file, including any existing helper, with a hash/mode inventory. Backup content and directory entries
are fsynced before replacing the wrapper. The journal records known old/planned
states and planned helper SHA. Each file replacement is atomic; the combined wrapper/helper/manifest/receipt
update is **not** one filesystem transaction. During an interruption, status is
not-ready. There is no automatic retry, adoption, rollback, upstream switch or
credential/config recovery.

After inspecting the chosen transaction and current manifest hash:

```sh
python3 scripts/clients/install_codex_dispatch.py rollback-rebind \
  --transaction TRANSACTION_ID \
  --expect-manifest-sha256 FULL_REVIEWED_CURRENT_MANIFEST_SHA256
```

The default is a read-only rollback plan. `--apply` before the command explicitly
restores verified old wrapper/support/helper bytes and selection, recomputes wrapper
identity and a new receipt generation, and retains the audit transaction.
Known interrupted states are supported; unknown edits, foreign entry changes
or changed/missing prior Original are refused for manual inspection. Helper
backups are checked against the verified prior qualification registry and
binding; changed current helpers or backups abort before mutation. Rollback
to an old layout that had no helper removes only this transaction's known new
helper and remains maintenance-required, including legacy schema 3 without
helper binding. A prior manifest claiming a helper without a valid backup is
not recoverable through this rollback. Uninstall may still restore the checked
Original when the patched helper is missing; it keeps state/history for audit.
Rollback itself is
also several file operations; power loss during that explicit recovery requires
manual inventory/journal inspection. Do not discard backups or run an external
updater concurrently.

Install/uninstall/rebind/rollback share an owned private no-follow single-link
lock. Other installers do not share it; quiet operation and pre/post inspection
remain required. It is not a filesystem compare-and-swap guarantee. No service,
Rust version, legacy proxy, Host/CLI delivery helper or external user file is
modified by these operations.

## Canonical-state package upgrade

Package-aware installation remains optional for legacy qualified records. A
record with `runtimePackage: {"target": "x86_64-unknown-linux-gnu", "files": {...}}`
requires the complete `--runtime-package` directory. Its map pins every package
file except `bin/codex` and `bin/codex-code-mode-host`, whose existing pins remain
separate. The exact finite GNU inventory includes voice libraries and notices;
unlisted paths or symlinks/special files are refused. The new runtime resides at
`state/runtime/` and the manifest/receipt carry its full record plus
`patchedRelativePath: "runtime/bin/codex"`. The separately qualified default-menu
CLI is `0d45eb864e1773cdd692194f3d1e7b2056614560444df5ed48e027733aab349a`;
qualification/registry changes and real native acceptance belong to the parent.

To repair the existing canonical state used by Desktop, rebind also accepts
the following explicit legacy-to-package transition. The default is read-only;
place `--apply` before `rebind` for the authorized mutation:

```sh
python3 scripts/clients/install_codex_dispatch.py --state /absolute/canonical/state rebind \
  --original /absolute/chosen/original/codex --original-version 0.164.0 \
  --original-sha256 FULL_QUALIFIED_ORIGINAL_SHA256 \
  --expect-manifest-sha256 FULL_REVIEWED_CURRENT_MANIFEST_SHA256 \
  --patched /absolute/package/bin/codex --patched-sha256 FULL_QUALIFIED_NEW_CLI_SHA256 \
  --runtime-package /absolute/package \
  --code-mode-host /absolute/package/bin/codex-code-mode-host \
  --code-mode-host-sha256 FULL_QUALIFIED_HELPER_SHA256
```

Both patch options must be supplied together and select a different qualified
package release. This mode only transitions from legacy `codex-lain`; an
existing `state/runtime` destination is refused. No package-to-package upgrade,
download, build, daemon restart or native execution is performed by maintenance.

The complete new package is copied and verified in the maintenance transaction
before publishing `runtime/` and replacing the canonical wrapper/support/binding.
The canonical entry symlink stays in place. Legacy `codex-lain` and its helper
remain byte/identity unchanged for old sessions. A user-created root helper
symlink is recorded by exact link text/device/inode, never followed, copied,
removed or accepted as qualified helper bytes. The manifest and receipt bind
this exception as `preservedLegacyRuntime`. Later status and ordinary package
rebinds verify this preserved runtime while using the owned qualified helper at
`runtime/bin/codex-code-mode-host`. Without an explicit preservation binding,
an inactive root helper is refused by maintenance snapshots.

Rollback verifies the preserved old runtime, the new package and the transaction's
known current state before restoring the old wrapper/support/manifest selection.
It retains the published `runtime/` for audit and any processes already using it;
retrying a package upgrade then requires explicit inspection of that retained
destination. Rolling back to the old user helper symlink restores an unqualified,
maintenance-required baseline, never a claim of a qualified legacy pair. A changed
old binary/link, changed new package, foreign entry or stale Original identity
aborts. The combined update remains several atomic file operations; interrupted
known states can be rolled back explicitly. Rollbacks require their own known
manifest/snapshot; later unrelated transactions are not automatically unwound.

Static package resources are retained and verified during ordinary package
rebind/rollback. Its helper backup is nested under
`previous/runtime/bin/codex-code-mode-host`; legacy helper backups retain their
old root path. Damaged/missing package resources require explicit inspection and
reinstallation rather than silent repair. Ordinary helper maintenance retains
its existing prohibition against symlink helpers in the active runtime.

## Explicit device-only identity acceptance

A remount can change the device number while keeping the same inode and bytes.
`reaccept-identities` is a separate metadata-only operation for that case:

```sh
python3 scripts/clients/install_codex_dispatch.py --state /absolute/canonical/state \
  reaccept-identities --expect-manifest-sha256 FULL_REVIEWED_CURRENT_MANIFEST_SHA256
```

Dry run verifies the exact stored entry target, wrapper/Original/patched hashes,
Original path resolution and qualified CLI pair. Entry, wrapper and Original must
each retain their recorded inode, and all three must show the same old-to-new
device-number change. Recreated files, inconsistent devices, stale manifest or
receipt, changed bytes and changed link targets are refused. Nothing implicitly
relaxes the ordinary ownership checks.

`--apply` before the operation snapshots raw prior manifest/receipt under a
separate audit transaction, then writes only current identities, a new receipt
generation and receipt. It changes no binary, helper, wrapper/support source,
entry symlink or config. It neither validates nor qualifies the legacy helper.
The metadata pair is not one filesystem transaction; interruption requires audit
inspection. Its audit is not a `rollback-rebind` transaction: restoring obsolete
device numbers would not restore the filesystem. Review the resulting manifest
hash before a separate package rebind, whose snapshot then contains the accepted
identity baseline and can pass the unchanged rollback identity checks.

The final Python-only suite has **92 passing tests**, including complete package
layout, tampering/unsafe inputs, staging corruption, nested maintenance paths,
canonical preserved-symlink upgrade/rollback/interruption, subsequent ordinary
rebind, and device-only acceptance followed by upgrade/rollback. All fixtures are
temporary; this source work performs no production mutation, network request,
heavy build, registry edit or native qualification.

### Legacy lock permissions

An older installation may leave `STATE_PARENT/.codex-profile-lain.lock` at
`0644`. Maintenance requires `0600` and refuses that lock before changing client
state; a successful read-only rebind plan does not acquire or validate the lock.
For the default state, inspect
`~/.local/share/lain/clients/.codex-profile-lain.lock`.

A manual correction is appropriate only after confirming quiet operation, an
owned protected parent directory, and an empty regular file owned by the current
user, with one link, no symlink, and the same inspected device/inode. Open it
without following symlinks, obtain a nonblocking exclusive `flock`, recheck the
descriptor/path identity, then use `fchmod` on that same descriptor to tighten its
mode to `0600` and `fsync` it. If any check or the lock acquisition fails, stop for
inspection. Do not unlink/recreate the lock or repair an unknown lock
automatically. Review status and the manifest pin again before an explicit retry.
