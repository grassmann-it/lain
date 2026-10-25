# N08 — qualified Codex maintenance

Source baseline: `7661886fbcf6acd355eb7346cebad9b8fa1cff1c`. The user-approved
N01–N09 implementation selects conscious qualification/rebind and no automatic
unknown upstream upgrade. This worker changes only `clients/codex/*`,
`scripts/clients/*` and this qualification note. N07 delivery/service operators,
UI, normal Codex configuration, Rust sources/toolchain and the legacy proxy are
outside its write scope.

## Implemented source

- Qualified registry for the existing 0.164.0 Original (`b93f6811…`) and patched
  client (`5b3a0585…`), using the full recorded binary pins. No new release is
  asserted. Installer and recognizer separately enforce the 0.164 grammar.
- Read-only human/JSON status distinguishes absent/legacy/foreign/changed state,
  missing or changed Original/Patch, upstream release-pointer drift, unsupported
  grammar, stale receipts and wrong wrapper bindings. Known hashes identify
  versions without executing untrusted candidates.
- Schema-3 receipt binds fixed absolute entry/state paths, generation,
  manifest/wrapper/Original/Patch pins and maintenance-code hash. The new
  installed maintenance copy can serve N05 after a conscious install/rebind.
  Legacy schema 2 requires explicit migration; it does not become ready by claim.
- Default dry-run rebind requires an unchanged reviewed manifest, selected
  external executable, full known SHA and correct known version. It records a
  verified fsynced backup/journal, preserves entry/patch/config, and supports
  explicit verified rollback of known completed/interrupted states.
- The new wrapper checks selected target bytes before exec and fails clearly on
  a missing/changed target. Normal calls and subcommands still reach only the
  selected Original with unchanged `argv[1:]`, cwd and environment. Unknown
  options/ambiguous parsing are delegated, not declared newly supported.
- Mutation lock now rejects symlink/hardlink/open-permission files, uses
  O_NOFOLLOW/O_CLOEXEC, exclusive creation and descriptor/path identity checks.

Full command, receipt and concurrency contracts:
[maintenance](../../clients/codex/maintenance.md) and
[dispatcher](../../clients/codex/dispatcher.md).

## Local checks

`PYTHONDONTWRITEBYTECODE=1 python3 scripts/clients/test_codex_dispatch.py -v`:
**38 serial tests passed**, most recent measured run approximately 1.2 seconds.
The tests use temporary files, synthetic qualification registries and local
Python/env executables as neutral exec probes. No Codex, provider, service,
user-config or live installer operation is performed. They cover:

- plain/PATH/custom-argv0 argument, cwd and environment preservation, including
  locale coercion; the existing shebang argv0 limitation remains explicit;
- exact known root profile selection, `--`, config/model/prompt false positives,
  singleton conflicts and all known subcommands;
- unknown argument grammar delegation and rejection of an unimplemented grammar
  even if a registry is altered to list it;
- upstream release-path drift while the intact selected Original remains used;
- fallback disappearance/tamper, recursion/foreign owner refusal, dry-run and
  exact restore/rollback guards;
- installed temporary maintenance CLI/receipt, legacy migration, stale manifest,
  altered support/receipt, wrong wrapper binding, intentional qualified rebind,
  interrupted receipt write and explicit verified recovery;
- a hostile lock symlink leaves its target unchanged.

Synthetic hashes/version labels in fixtures test the contract; they do not
qualify Python/env as a Codex release. `git diff --check` passes. No new Cargo
build, large test suite, Rust installation, daemon or native menu was run.
The previous real native/Core evidence and the unrun TUI tests retain their
precise scope in the existing delivery report; these fixtures do not relabel it.

## N05 integration agreement

N05 uses only `status --json`, with timeout/cancellation. Before starting an
installed checker it validates bounded nonsymlink receipt/manifest files,
manifest SHA, fixed expected paths, and regular maintenance-source SHA. Missing
or old receipt data fails closed. The receipt file contains direct fields; the
CLI response nests them under `receipt`. Ready status does not establish N05's
profile/catalog generation or Host revision, provider entitlement or a current
native menu. For launch use the checked absolute entry, or verify PATH matches
it. Both private-home and separate-profile group starts need real CLI
`--profile lain`; upstream resolves `lain.config.toml` in the selected CODEX_HOME.
No status action installs/rebinds the dispatcher or writes ordinary config.

## Smallest remaining real verification (Root lane, not run here)

1. From trusted integrated source, run read-only status against the actual
   schema-2 installation. Confirm the real pins and explicit migration diagnosis
   without changing anything. Preserve ordinary Codex/profile/legacy fingerprints.
2. Review the default rebind plan for the same existing qualified 0.164.0
   Original, with the exact current manifest hash. Only Root's separately
   authorized apply migrates wrapper/support/receipt; no binary rebuild is needed.
   Recheck ready status and the N05 receipt preflight.
3. Compare direct Original and dispatched `--version` (expected actual 0.164.0)
   and one harmless subcommand/help path. Recheck selected binary pins and
   unchanged external fingerprints. Receipt version labels are known-SHA lookup,
   not a new --version measurement from this worker.
4. In the bounded native PTY slot, run the checked entry with
   `--no-daemon --profile lain` and open/dismiss `/model` using the unchanged
   qualified patch. Confirm existing groups and N05's chosen CODEX_HOME/profile
   target. No inference/provider call is required. An isolated copy can exercise
   real rebind rollback/uninstall before considering any live rollback.

No installed migration, live rebind/uninstall, current N05 UI, new native menu or
real provider behavior is claimed here. Multi-file maintenance is not atomic as
a group; known interrupted state fails closed and needs explicit inspected
recovery. A concurrent external updater is unsupported, and receipts are local
consistency records rather than signatures or a filesystem compare-and-swap.
