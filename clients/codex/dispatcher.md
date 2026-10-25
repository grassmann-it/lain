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
the identical incoming `argv` including `argv[0]`, the same working directory,
and the original environment. Because Python can add `LC_CTYPE` during startup,
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
Existing state is never overwritten, including old audit state after rollback.

```sh
python3 scripts/clients/install_codex_dispatch.py uninstall
python3 scripts/clients/install_codex_dispatch.py --apply uninstall
```

Uninstall restores the exact old symlink target only while the Codex entry
still points directly to this installation's wrapper and that wrapper still
matches its recorded SHA256. If another updater replaced the Codex entry or
someone modified the wrapper, uninstall stops without replacing it. The state
directory is retained for inspection/recovery; archive it explicitly before a
fresh installation.

Private install/uninstall commands use an exclusive `flock`. Symlink ownership
is checked twice immediately before atomic replacement. A foreign updater does
not share this lock, so do not run installation/rollback concurrently with an
upstream Codex update: a final external change between the check and rename
cannot be excluded by this userspace check. Upstream `current` updates after
installation do not change the dispatcher's embedded original release path;
uninstall first before changing/requalifying the upstream release. The saved
original `current` symlink is restored verbatim on rollback.

## Offline verification

```sh
PYTHONDONTWRITEBYTECODE=1 python3 scripts/clients/test_codex_dispatch.py -v
```

Tests run serially, use temporary fixtures only, and perform no build, network
request or live installation. They verify CLI false positives and subcommands,
read-only dry run, SHA pin rejection, unchanged original file, exact rollback,
refusal to overwrite a foreign replacement or edited wrapper, and normal exec
argv/cwd/environment including Python locale coercion. They do not replace the
required real patched-binary native TUI check.
