# Launcher for the lain profile and verified Lain default

**Local installation completed, 25 October 2026:** the separate patched client
passed its native TUI checks and this launcher was explicitly installed. Exact
pins and scope are in the [delivery report](../../docs/codex-model-groups-delivery-2026-10-25.md).
For a new installation, source preparation or a successful build alone still
does not install the launcher; installation remains an explicit separate step.

N08 added [qualified maintenance and the Profiles receipt contract](maintenance.md).
AP1 also diagnoses schema-3 installations that lack qualified helper bindings;
this worker has not migrated the user installation.

**AP1 source update, 26 October 2026:** the patched runtime now includes a
qualified private `codex-code-mode-host` copy. Install/rebind refuse missing
helper qualification or inputs; status and patched launch check helper integrity.
The registry now qualifies the official 0.164.0 helper with SHA256
`8ee4f472e92ac226a189e98998633b2d07f989f208515750764db660369f2926`.
Its Sigstore signature, exact source commit and native shell/continuation proof
are recorded in [maintenance](maintenance.md#helper-qualification-and-packaging-ap1).
No installed state or the user's temporary system-helper symlink was changed.

The Linux-only launcher replaces the existing `~/.local/bin/codex` **symlink**.
It recognizes interactive root invocations such as `codex --profile lain`,
`codex --profile=lain`, `codex -p lain`, `codex -p=lain` and `codex -plain`.
Codex 0.164.0 defines `-p` in `codex-rs/utils/cli/src/shared_options.rs` as the
same profile option. The exact profile name `lain`, or a root invocation with
the verified Lain default described below, selects the separately installed
patched executable. An explicit other profile always selects the original.
The dispatcher does not edit Codex configuration,
the upstream executable, or the upstream `current` symlink.

All other invocations execute the original resolved binary using `os.execv`,
unchanged option/prompt arguments (`argv[1:]`), the same working directory,
and the original environment. The Python shebang does **not** preserve the
caller's original `argv[0]`: PATH lookup (`codex`) and a custom name supplied by
`exec -a` become the entry script pathname. An absolute-path invocation happens
to retain the same name. This launcher therefore does not promise a completely
identical process invocation. Because Python can add `LC_CTYPE` during startup,
the launcher restores the interpreter's initial environment from Linux
`/proc/self/environ` immediately before exec. This requires a normal mounted
proc filesystem and `/usr/bin/python3` (Python 3.11+ for launcher and installer).

The launcher adds no flags, including `--no-daemon`. Native isolation tests must
pass `--no-daemon` explicitly. An existing shared daemon can otherwise run its
own previously started executable. Default detection reads only the base
configuration and the referenced Lain catalog; it never
overlays the separate `lain.config.toml` profile.

The AP1 schema-3 wrapper checks the selected executable's pinned SHA before
exec, plus the patched runtime's required helper when selecting `codex-lain`.
Changed/missing selected bytes or helper dependencies exit 126 with maintenance guidance; they
are never accepted as an unknown upstream update. See the maintenance contract
for the streaming-hash overhead and the unchanged concurrency boundary.

## Optional complete native package

A qualified patch may additionally contain `runtimePackage` with target
`x86_64-unknown-linux-gnu` and a `files` map of relative paths to lowercase
SHA256 pins. For that record, install requires `--runtime-package DIRECTORY`
alongside the existing explicit CLI/helper paths and pins. The complete package
is copied to `state/runtime/`: `codex-package.json`, `bin/codex`,
`bin/codex-code-mode-host`, `codex-path/rg`, `codex-resources/bwrap` and the
qualified voice runtime, libraries, notices and licenses. CLI/helper bytes have
their separate pins; all other package files belong in `runtimePackage.files`.
The current complete GNU package has 43 files, including 41 auxiliary pins.

The manifest and receipt bind `patchedRelativePath: "runtime/bin/codex"` and
the complete package record. Absence of that record retains the legacy
`state/codex-lain` layout and sibling helper. Existing templates without a
package placeholder remain valid for legacy records. Package records require
the new template, which embeds trusted pins rather than reading mutable
metadata as a source of trust. Each patched launch checks the exact package
inventory, hashes and protected ownership before exec; missing, altered,
linked or special files fail with exit 126. Original-only invocations continue
to check their chosen Original. No flags/profile or environment entries are
injected, and default classification is unchanged.

Only the finite GNU file allowlist in the installer/template is accepted;
unexpected files/directories, traversal, symlinks, hardlinks, special files and
group/other-writable package files/directories are refused. Required base files
are metadata, rg and bwrap; any additional allowed resources must all be pinned
and the tree must exactly match the record plus CLI/helper. Files become owned
protected copies, with hashes checked before copying, during staged verification
and after publication. Package maintenance retains and checks static resources;
helper backups/restores use the patched executable's parent directory.

An explicit legacy-to-package rebind can upgrade the canonical state while
preserving its old binary and root helper, including a user-created helper
symlink. Their byte/identity/link snapshot is bound as `preservedLegacyRuntime`
in the manifest/receipt. This exception applies only to the inactive preserved
runtime; ordinary helper rebind validation remains strict. See the commands and
rollback limits in [maintenance](maintenance.md#canonical-state-package-upgrade).
These source changes perform no production installation or native qualification.

## Verified default detection (source update, 26 October 2026)

Plain `codex` and other recognized root TUI invocations can select the patched
client without `--profile lain`. This requires a newly qualified native binary
whose picker accepts provider `lain` with no selected profile. The previously
qualified native patch required `Some("lain")`; updating this launcher alone
cannot enable grouping there. Native patching, binary qualification and
production installation are separate parent-owned work. This source change
does not install or update the user's launcher.

Detection uses `$CODEX_HOME/config.toml`, or `$HOME/.codex/config.toml` when
`CODEX_HOME` is unset/empty. The home must be absolute. Strict stdlib TOML
parsing must find top-level `model_provider = "lain"`, no legacy `profile`
selector, and an absolute `model_catalog_json` pointing to either the legacy
`$CODEX_HOME/lain.models.json` or
`$CODEX_HOME/.lain-artifacts/<32-lowercase-hex-generation>/lain.models.json`.
Known export location, protected local files and valid grouped metadata provide
the routing evidence. Artifact receipts diagnose installation state separately:
a missing/stale generation receipt or changed catalog hash does not force a
valid existing grouped catalog into the flat picker. Detection does not require
an artifact migration, a new profile installation or a service/profile change.

The catalog must contain unique nonempty model slugs and a valid
`lain_model_picker` schema version 1 with 1–128 groups. Group labels, unique
ids/members, model/router references and extension fields are checked against
the native extension contract. Native Codex still validates complete model
entries and the effective layered configuration. Strict UTF-8 JSON rejects
duplicate keys, nonfinite numbers and malformed documents. Files are bounded:
base config 1 MiB, catalog 16 MiB. Owned directories and regular
single-link files must prohibit group/other writes. Reads use directory handles,
no-follow opens beneath the selected Codex home and nonblocking file opens;
symlinks, FIFOs, changed reads and uncertain evidence select the original.
No provider/auth helper is called and no file is written.

A generation remains eligible when the base config still references it;
detection does not substitute the profile's newer catalog or consult
`.lain-artifacts/current.json`. Base model, reasoning, project settings and
explicit model options remain the user's settings. The launcher inserts no
profile, model or other argument, and preserves the environment as above.

For default detection, `--oss`, local-provider/remote options, routing-related
`-c` overrides (`profile`, `model_provider`, `model_catalog_json`,
`model_providers`, `oss_provider`) and overrides that cannot be parsed as strict
TOML conservatively select the original. Other valid overrides, including
model/reasoning settings, are forwarded unchanged. Existing explicit
`--profile lain` classification and AP1 binary/helper checks remain intact.

## CLI boundary

The recognizer follows the pinned 0.164.0 root interactive option grammar in
`cli/src/main.rs`, `tui/src/cli.rs` and `utils/cli/src/shared_options.rs`.
Single-valued options consume their own values; config/model/image/path values
and prompt text cannot accidentally select a profile. `--` ends option parsing.
Repeated singleton options, unknown options, help/version, unsupported enum
values, missing values, conflicting approval options, and ambiguous invocations
are delegated intact to the original parser. The patched Codex remains
responsible for semantic validation after a recognized CLI invocation, such as
whether a configured model or feature exists.

Every subcommand uses the original executable, including `exec`/`e`, `review`,
`resume`, `fork`, `agents`, and commands prefixed with `--profile lain`. This
feature is limited to the root interactive `/model` picker; it does not opt
noninteractive execution or session-management commands into a modified client.
A subcommand name explicitly supplied as a prompt after `--` is prompt text.

Examples (unless stated otherwise, without a verified Lain default):

| Arguments | Executable |
| --- | --- |
| no arguments, verified Lain default | patched |
| `-m user-model`, verified Lain default | patched (unchanged model option) |
| `--profile=old-cpa`, verified Lain default | original |
| `--profile=lain --no-daemon` | patched |
| `-m gpt-6 -p lain 'hello'` | patched |
| `--profile=lain -- '--profile=other'` | patched (literal prompt) |
| `--config 'model="--profile=lain"'` | original |
| `--model=--profile=lain` | original |
| `'hello --profile=lain'` | original |
| `-- --profile=lain` | original |
| `exec --profile=lain` | original |
| `--profile=lain exec` | original |
| `--profile=lain --profile=other` | original (diagnostic) |

If the installed patched executable is absent or non-executable, a recognized
lain invocation exits 126 with a clear error. There is no fallback to upstream.
Invocations classified for the original continue to work even if the patched
executable or launcher manifest has been removed. A verified Lain default
receives the same AP1 checks and exit-126 behavior as an explicit Lain profile;
it does not silently fall back when its selected binary/helper is broken.
The original binary path is embedded in the private wrapper and
cannot recursively resolve through the Codex entry symlink.

## Review, install, and rollback

The installer is `scripts/clients/install_codex_dispatch.py`. By default it
prints a JSON dry run and writes nothing. It requires explicit SHA256 pins for
the current original binary, the already built patched executable and its
code-mode helper. The Original/Patch pair must occur in `qualified-releases.json`,
and the patched release record must qualify that helper's SHA, version, source
commit and evidence as described in
[maintenance](maintenance.md#helper-qualification-and-packaging-ap1).
The pair must use the implemented `codex-0.164.0` grammar; explicit hashes alone
do not qualify an unknown version.
Read and review the hashes from the actual qualified artifacts; no placeholder
hash or source-archive hash is accepted in place of the binary hash.

```sh
python3 scripts/clients/install_codex_dispatch.py install \
  --patched /absolute/path/to/qualified/codex \
  --original-sha256 ORIGINAL_BINARY_SHA256 \
  --patched-sha256 QUALIFIED_PATCHED_BINARY_SHA256 \
  --code-mode-host /absolute/path/to/qualified/codex-code-mode-host \
  --code-mode-host-sha256 QUALIFIED_HELPER_BINARY_SHA256
```

After the separate authorization and real-binary checks, the same command with
`--apply` **before** `install` performs the installation. `--entry` can target a
test symlink instead of the default. `--state` (before the subcommand) can use a
separate test directory. Neither option changes Codex's own home/config path.

The private directory `~/.local/share/lain/clients/codex-profile-lain` holds the
patched binary and helper copies, generated wrapper, SHA-pinned manifest and a separate
`previous-symlink.json` backup of the exact former symlink target. Contents are
staged in a sibling directory; publication of the directory and the final entry
symlink each use an atomic rename. The original executable is never copied over
or modified. Original and patched inputs are hashed before installation, the
staged patch and helper are hashed again, and the original is rechecked before switching.
The helper is a regular single-link file owned by the current user, executable
and protected from other writers. Installation copies and fsyncs it into state;
it never links to a helper managed by a system/desktop updater. Its full
qualification record is bound into the manifest and receipt, and its hash is
embedded in the wrapper. An explicit hash or a matching version string without
the registry's paired provenance cannot qualify a helper.
The manifest records device/inode identities for the original, wrapper, previous
entry and installed entry. Symlink target and identity are inspected before and
directly after replacement; the original fallback hash and identity are checked
before and after installation/rollback. An observed mismatch stops the operation.
Fresh install refuses existing state, including old audit state after rollback.
Explicit maintenance can replace only its managed files while preserving a
verified prior snapshot and all earlier audit transactions.
Once state has been published, failures retain it for recovery. A failure after
entry replacement can leave the launcher installed; inspect the entry and
manifest before taking another action. No automatic rollback is attempted after
evidence of a foreign update. An incomplete manifest also requires inspection.

```sh
python3 scripts/clients/install_codex_dispatch.py uninstall
python3 scripts/clients/install_codex_dispatch.py --apply uninstall
```

Initially uninstall restores the exact saved symlink target. After an explicit
rebind it restores that consciously selected Original's canonical path, keeping
the initial symlink backup for audit. Uninstall checks that the Codex entry still points directly to this installation's
wrapper with its recorded device/inode identity, and that the wrapper's own
identity and SHA256 match. Recreating an entry or wrapper with the same target or
bytes is detected through identity checks. A detected replacement/modification
aborts. The saved symlink must still resolve to the pinned original fallback;
an upstream `current` change before rebind can therefore abort the initial
rollback. These are checks around the mutation,
not a guarantee against a concurrent foreign installer. The state directory is
retained for inspection/recovery; archive it explicitly before a fresh installation.

Private install/uninstall commands use an exclusive `flock`. A foreign updater
does not share this lock. Concurrent foreign installation/updates are unsupported:
perform apply/rollback only in a quiet, known launcher state. The symlink checks
are not a compare-and-swap operation. A foreign change between the last check and
`os.replace` can still be overwritten without being detected; post-checks cannot
establish that no such intermediate change happened. If checks find evidence of
an update, the operation aborts and leaves recovery state rather than trying an
automatic second mutation. Independently inspect the entry, wrapper/original
identities and fallback hash immediately before and after a live operation.

Upstream `current` updates after installation do not change the dispatcher's
embedded original release path. Status reports that drift. A conscious rebind
can choose only a qualified original; unknown upstream versions first need a
separate qualification and parser review. Neither status nor rebind rebuilds
or silently adopts an upstream release.

## Offline verification

```sh
flock ~/.cache/lain-verification-lane.lock \
  env PYTHONDONTWRITEBYTECODE=1 python3 scripts/clients/test_codex_dispatch.py -v
```

The suite includes AP1 and default-detection regression tests. Tests run serially,
use temporary fixtures only, and perform no build, network
request or live installation. They verify CLI false positives and subcommands,
read-only dry run, SHA pin rejection, unchanged original file, exact rollback,
aborting on observed foreign replacements, edited/recreated wrappers, original
fallback changes and a simulated post-replacement foreign update. Process tests
verify `argv[1:]`/cwd/environment including Python locale coercion. PATH and
custom-`argv[0]` tests explicitly demonstrate the shebang name change against
direct execution of the original binary. Helper cases cover qualified copy
installation, missing provenance, version/source mismatches, staging corruption,
missing/modified/non-executable/linked/writable helpers, early patched-exec
failure, schema-2/3 migration, updates, interrupted updates and verified rollback.
Config/auth/history fixtures and original/patched binaries remain intact through
helper maintenance. Ordinary calls and uninstall can still use the verified
Original when the patched helper is broken. Default tests cover strict TOML/JSON,
safe bounded reads, known legacy and generation exports, receipt independence,
root/subcommand/override boundaries and preserved user settings. Synthetic
installed launches prove that plain root selects the private patched executable
with unchanged arguments, working directory and environment, while excluded
invocations retain the original parser and broken AP1 helpers still fail early.
These checks are implementation tests;
they are not independent acceptance of actual P2 scope or the required real
patched-binary native TUI check.

The package/repair extension brings this Python suite to **92 passing tests**.
It adds a complete synthetic 43-file layout, missing/unsafe/tampered resources,
copy corruption, pinned-template compatibility, default package execution,
nested helper rebind/rollback, preserved-symlink upgrade and interruption,
and explicit same-inode device-only identity acceptance. All new installation
and repair mutations remain confined to temporary fixtures.
