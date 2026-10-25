# Opt-in launcher for the lain profile

Prepared implementation only. Installing this launcher is a separate explicit
step after the patched Codex binary has passed its native TUI checks. The source
patch or a successful build alone does not install it.

The Linux-only launcher replaces the existing `~/.local/bin/codex` **symlink**.
It recognizes interactive root invocations such as `codex --profile lain`,
`codex --profile=lain`, `codex -p lain`, `codex -p=lain` and `codex -plain`.
Codex 0.164.0 defines `-p` in `codex-rs/utils/cli/src/shared_options.rs` as the
same profile option. Only the exact profile name `lain` selects the separately
installed patched executable. The dispatcher does not edit Codex configuration,
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
proc filesystem and `/usr/bin/python3` (Python 3.11+ for the installer).

The launcher adds no flags, including `--no-daemon`. Native isolation tests must
pass `--no-daemon` explicitly. An existing shared daemon can otherwise run its
own previously started executable. No config/profile contents are interpreted
by the dispatcher.

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

Examples:

| Arguments | Executable |
| --- | --- |
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
Ordinary Codex continues to work even if the patched executable or manifest has
been removed. The original binary path is embedded in the private wrapper and
cannot recursively resolve through the Codex entry symlink.

## Review, install, and rollback

The installer is `scripts/clients/install_codex_dispatch.py`. By default it
prints a JSON dry run and writes nothing. It requires explicit SHA256 pins for
both the current original binary and the already built patched executable.
Read and review the hashes from the actual qualified artifacts; no placeholder
hash or source-archive hash is accepted in place of the binary hash.

```sh
python3 scripts/clients/install_codex_dispatch.py install \
  --patched /absolute/path/to/qualified/codex \
  --original-sha256 ORIGINAL_BINARY_SHA256 \
  --patched-sha256 QUALIFIED_PATCHED_BINARY_SHA256
```

After the separate authorization and real-binary checks, the same command with
`--apply` **before** `install` performs the installation. `--entry` can target a
test symlink instead of the default. `--state` (before the subcommand) can use a
separate test directory. Neither option changes Codex's own home/config path.

The private directory `~/.local/share/lain/clients/codex-profile-lain` holds the
patched binary copy, generated wrapper, SHA-pinned manifest and a separate
`previous-symlink.json` backup of the exact former symlink target. Contents are
staged in a sibling directory; publication of the directory and the final entry
symlink each use an atomic rename. The original executable is never copied over
or modified. Original and patched inputs are hashed before installation, the
staged patch is hashed again, and the original is rechecked before switching.
The manifest records device/inode identities for the original, wrapper, previous
entry and installed entry. Symlink target and identity are inspected before and
directly after replacement; the original fallback hash and identity are checked
before and after installation/rollback. An observed mismatch stops the operation.
Existing state is never overwritten, including old audit state after rollback.
Once state has been published, failures retain it for recovery. A failure after
entry replacement can leave the launcher installed; inspect the entry and
manifest before taking another action. No automatic rollback is attempted after
evidence of a foreign update. An incomplete manifest also requires inspection.

```sh
python3 scripts/clients/install_codex_dispatch.py uninstall
python3 scripts/clients/install_codex_dispatch.py --apply uninstall
```

Uninstall checks that the Codex entry still points directly to this installation's
wrapper with its recorded device/inode identity, and that the wrapper's own
identity and SHA256 match. Recreating an entry or wrapper with the same target or
bytes is detected through identity checks. A detected replacement/modification
aborts. The saved symlink must still resolve to the pinned original fallback;
an upstream `current` change also aborts. These are checks around the mutation,
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
embedded original release path. Uninstall before changing/requalifying the
upstream release. In the supported quiet setup, rollback restores the saved
original symlink target verbatim.

## Offline verification

```sh
PYTHONDONTWRITEBYTECODE=1 python3 scripts/clients/test_codex_dispatch.py -v
```

Tests run serially, use temporary fixtures only, and perform no build, network
request or live installation. They verify CLI false positives and subcommands,
read-only dry run, SHA pin rejection, unchanged original file, exact rollback,
aborting on observed foreign replacements, edited/recreated wrappers, original
fallback changes and a simulated post-replacement foreign update. Process tests
verify `argv[1:]`/cwd/environment including Python locale coercion. PATH and
custom-`argv[0]` tests explicitly demonstrate the shebang name change against
direct execution of the original binary. These checks are implementation tests;
they are not independent acceptance of actual P2 scope or the required real
patched-binary native TUI check.
