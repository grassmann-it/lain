# Qualified client maintenance

N08 implements conscious maintenance of the existing 0.164.0 client. Source is
prepared and tested with disposable fixtures; this does not migrate an installed
dispatcher. The existing [delivery](../../docs/codex-model-groups-delivery-2026-10-25.md)
still describes the real earlier installation.

`qualified-releases.json` admits only the already recorded Original SHA256
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

Install still accepts only the qualified Original/Patch pair and explicit binary
SHA pins. A schema-3 installation also writes its maintenance script, template,
qualification registry and receipt to its own state directory. Normal calls and
subcommands continue to use the chosen resolved Original; no flags, cwd, user
config or environment entries are changed. The shebang `argv[0]` limit in the
[dispatcher contract](dispatcher.md) still applies.

Before each exec the new wrapper hashes only its selected target. Missing or
changed selected bytes exit 126 with an actionable status/rebind message. No
changed binary is silently accepted and a failed patched branch never falls
back to upstream. This adds a streaming hash read to each invocation; it changes
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
  --expect-manifest-sha256 FULL_REVIEWED_CURRENT_MANIFEST_SHA256
```

This is a read-only plan. Place `--apply` **before** `rebind` only for the
separately authorized mutation. The same operation can upgrade the current
schema-2 launcher with its unchanged original, or recover a missing fallback
using another existing qualified copy. It requires the exact still-owned entry
and wrapper, intact patched binary and unchanged reviewed manifest. Unknown
bytes/version/grammar, recursive fallback, stale manifest and a foreign replaced
entry are refused. Rebind does not adopt another installer's new launcher.

Rebind changes only wrapper/manifest/receipt and its own maintenance support
files. It retains the entry symlink, patched binary and every ordinary Codex
config/profile file. The observed upstream pointer baseline is recorded anew;
the user-selected fallback may be a different qualified copy. A deliberately
acknowledged absent upstream pointer may remain null while that selected
fallback is intact. After rebind, uninstall restores the explicitly selected
Original's canonical path; the exact initial upstream symlink target remains
in the original backup for audit. This avoids uninstall unexpectedly selecting
a later unknown `current` release.

## Reversibility and interruption

Before mutation, `maintenance-history/TRANSACTION/previous/` stores every
managed file with a hash/mode inventory. Backup content and directory entries
are fsynced before replacing the wrapper. The journal records known old/planned
states. Each file replacement is atomic; the combined wrapper/manifest/receipt
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
restores verified old wrapper/support bytes and selection, recomputes wrapper
identity and a new receipt generation, and retains the audit transaction.
Known interrupted states are supported; unknown edits, foreign entry changes
or changed/missing prior Original are refused for manual inspection. Returning
to legacy schema 2 returns to maintenance-required status. Rollback itself is
also several file operations; power loss during that explicit recovery requires
manual inventory/journal inspection. Do not discard backups or run an external
updater concurrently.

Install/uninstall/rebind/rollback share an owned private no-follow single-link
lock. Other installers do not share it; quiet operation and pre/post inspection
remain required. It is not a filesystem compare-and-swap guarantee. No service,
Rust version, legacy proxy, Host/CLI delivery helper or external user file is
modified by these operations.
