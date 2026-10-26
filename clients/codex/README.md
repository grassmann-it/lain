# Opt-in Codex CLI model groups

**Additional local delivery,26 October2026:** patch0003 preserves the native
ChatGPT account display for command-authenticated Lain sessions, enabling the
unchanged OpenAI `/usage` UI alongside separate Lain inference authentication.
Its own build/native/installation status is recorded in the
[usage delivery report](../../docs/native-openai-usage-delivery-2026-10-26.md).
This client has its own qualification record and is locally installed. Existing
daemons were preserved; `codex --no-daemon` starts the new bytes immediately.

**Current local delivery, 25 October 2026:** the separate client and dispatcher
are installed for the lain profile. Seven native PTY cases and a bounded harness
of the six unchanged Core module tests passed. The full Core test build hit its
memory limit; eight TUI library tests were not run. See the authoritative
[delivery report](../../docs/codex-model-groups-delivery-2026-10-25.md) for exact
binary pins and boundaries. The preparation handoff below records its earlier
stage; it does not supersede that delivery. Remaining maintenance is in
[N08](../../docs/next-work-packages-2026-10-25.md).

N08 source now provides a [versioned status/receipt and explicit rebind](maintenance.md)
for the same qualified 0.164.0 bytes. Its [qualification note](../../docs/next-qualification/n08-codex.md)
separates temporary Python fixtures from the remaining installed/native check.
It neither introduces another Rust version nor upgrades an installed dispatcher
automatically.
An older `0644` maintenance lock can block migration before client state changes;
see [legacy lock permissions](maintenance.md#legacy-lock-permissions) for the
manual inspection and permission correction required before retrying.

This is a small patch over the public `rust-v0.164.0` source, not a selection wrapper.
It changes the real TUI `/model` path in `tui/src/chatwidget/model_popups.rs`.
The original installed Codex binary and configuration are not changed by preparation or build.
Deployment uses a separate binary; preparation is not an installation or dispatcher update.

**Source follow-up, 26 October 2026:** the extension is active with effective model provider
`lain`, either the default native profile (`None`, plain `codex`) or exact **`--profile lain`**,
and valid `lain_model_picker` metadata in the configured `model_catalog_json`. The selected
user-layer provenance records the real CLI profile; legacy TOML `profile` and filenames do not
select it. Explicit other profiles (including `old-cpa` and case variants) and non-lain providers
keep the original upstream deserializer, including its behavior of ignoring invalid extensions.
Missing metadata keeps the upstream picker even for the lain provider. Malformed or repeated
extension metadata is rejected for both default and named lain profiles by the same validator.
The 25 October delivery above qualified the earlier explicit-profile scope; these new source
changes still require a rebuilt binary and fresh native acceptance.
Core reads the extension and existing model metadata from the same file snapshot. No additional
configuration setting, environment variable, network lookup, or model-id naming convention is used.

```json
{
  "models": ["... existing valid Codex ModelInfo entries ..."],
  "lain_model_picker": {
    "schema_version": 1,
    "groups": [
      {"id": "free", "name": "Free", "router_model": null, "models": ["provider/model-a"]},
      {"id": "it-sec", "name": "IT Sec", "models": ["provider/model-a"]},
      {"id": "provider", "name": "Provider", "router_model": "real-router-alias", "models": ["provider/model-a"]}
    ]
  }
}
```

Group/member order follows the export. Collections may overlap. The optional `router_model`
must reference a real existing catalog slug; the client cannot implement routing. It is shown as
**Automatisch**, above specific models, and selection uses the unchanged model/effort/persistence
path with the exact wire slug. If absent or no longer visible, the top row is disabled and says
**Noch nicht eingerichtet**. Selecting it cannot dispatch a model action.

**Alle** is appended if no group with id `all` is exported. It always shows the unfiltered visible
inventory, including names starting with `codex-auto-`; its member list never filters the fallback.
An explicit `all` group may supply a real router. Hidden/unavailable presets remain unselectable.
Escape returns from a group to the group list. A completed selection dismisses both levels.
An inventory refresh keeps the open group and the highlighted model slug.

The loader rejects unsupported extension versions, duplicate group ids/members, control characters
in labels, and references outside the catalog. Missing metadata preserves upstream behavior.
The extension changes presentation only. Route capabilities, account identity, inference behavior,
router availability, and cost policy remain responsibilities of lain's real exported routes.

## Reconstruct and build

`upstream.json` pins the public tag, peeled commit and archive SHA-256. The source archive is
not vendored. Preparation validates it, safely extracts into a new directory, checks/applies the
patches, and writes source provenance. It never overwrites an existing checkout. Temporary
extraction uses `~/.cache/lain-build-tmp` when `TMPDIR` is unset; source archives do not default
to the RAM-backed `/tmp` directory.

```sh
python scripts/prepare-codex-picker.py --fetch --output /absolute/new/source
# Or replace --fetch with --archive /path/to/upstream-rust-v0.164.0.tar.gz.
scripts/build-codex-picker.sh /absolute/new/source
```

The offline reconstruction command for the default-profile patch on this workspace is:

```sh
python scripts/prepare-codex-picker.py \
  --archive /home/tomka/.cache/lain-worktrees/codex-picker-client-cbf083f/clients/codex/upstream-rust-v0.164.0.tar.gz \
  --output /home/tomka/.cache/lain-codex-sources/prepared-picker-default-lain-0.164.0-20261026
```

Run from the repository root and choose another new output path if it already exists. The
manifest pins the updated `0001-lain-model-picker.patch` SHA-256 to
`fda0a18221eff37f578e2f27cba6cc90556ef888ec2891c6c82067c7c10ac92d`; upstream tag,
commit, archive and lock pins are unchanged. No model override is required.

The public release bumps workspace package versions to `0.164.0` while its committed lock
still records the 166 local packages as `0.0.0`. The second pinned patch corrects only those
local version fields. Preparation independently compares all 1308 complete external package
records (including dependencies), every local field except the expected version, lock metadata,
and the final lock hash. These checks are included in `lain-picker-source.json`; no unlocked
build or dependency update is needed. `tests/codex-picker/test_prepare.py` checks rejection
of changes to external records, local dependencies, and lock metadata.

The tag requires **Rust 1.99.1**. The build script uses the upstream `dev-small` profile, one Cargo
job, no incremental cache and `--locked`. It builds `codex-cli`'s `codex` executable and
the required sibling `codex-code-mode-host`; it does not install or launch either.
Upstream Cargo.lock contains 1474 workspace packages; the CLI dependency
closure and peak memory have not yet been measured. Its git dependencies include mxc, nucleo,
rmcp, runfiles and the upstream-pinned terminal/WebSocket forks. Network/toolchain/dependency
preparation and any build must run in the root's bounded serial lane.

## Default-profile source qualification, 26 October 2026

The patch includes eight Core tests and ten lain TUI tests, including actual profile-v2
user-layer provenance, default/named scope, valid/missing metadata, equal malformed/duplicate
metadata refusal, explicit other profiles, non-lain providers, and default nested navigation.
The product CLI was compiled with pinned Rust 1.99.1. These Rust unit tests were
not executed; the independent native PTY acceptance below exercises the built client.

`PYTHONDONTWRITEBYTECODE=1 python tests/codex-picker/test_prepare.py` passes fourteen offline tests
for the lock verifier, patch hashes/syntax, and native fixture/persistence contracts. It launches
no Codex client. The source preparer requires no changes.

`tests/codex-picker/native_model_picker.py` now prepares fifteen fresh isolated PTY cases.
Default and named lain invocations both expect groups; explicit other profiles/non-lain
providers and missing metadata expect the upstream picker. Invalid and duplicate default
metadata require a bounded, nonzero native configuration-parser exit before any input is sent.
Default selection may persist only the chosen model in the synthetic `config.toml`; named
selection writes only its profile file. Every nonselected config remains byte-identical.
The fixture pre-seeds reasoning `none`, matching the synthetic catalog, so the unchanged native
model/effort persistence does not add an unrelated setting during the selection check.

Separate `default-native-flags` and `lain-native-flags` cases launch the binary with no extra
flags and with only `--profile lain`, respectively. The default case requires a private daemon
socket/PID, the tested executable hash, and matching private PID/network namespaces, so an
embedded fallback cannot pass as daemon acceptance. `/run` is hidden along with the private
home and temporary directory; no production daemon or service is contacted. The existing
environment allowlist and unchanged `HOME` value/private mount remain in place; `CODEX_HOME`
is not reassigned. Provider traffic still fails every case.

The source paths explain why these cases differ: `tui/src/daemon_startup.rs::exclusion` treats
`--profile` as embedded-only, while `tui/src/startup_orchestration.rs` loads the local TUI config
through `ConfigBuilder` for either transport. Picker metadata is read by that local loader and
stored in the TUI config; it does not rely on a new daemon wire field. All fifteen cases passed
on CLI SHA256 `0d45eb864e1773cdd692194f3d1e7b2056614560444df5ed48e027733aab349a`, including
the flagless private daemon with that exact executable hash. Evidence is retained under
`~/.cache/lain-qualification-20261026/codex-default-menu-native-r2/` and in
[the portable qualification](default-menu-0.164.0.qualification.json). The initial r1 correctly
failed on musl metadata paired with the GNU build; r2 uses accurate GNU metadata.

Flagless startup also requires upstream's complete local package, not just a standalone CLI.
The harness accepts `--package /absolute/local/linux/package` with `codex-package.json`,
`bin/codex-code-mode-host`, `codex-path/rg`, and `codex-resources/bwrap`. It mounts that package
read-only at `/mnt/package`, overlays the exact `--binary` at `bin/codex`, and hashes the
manifest/helper/resources. Upstream may copy it only into the synthetic private home for its
daemon. `--run` requires this explicit input; there is no package discovery, download, or
fallback to production. Preparation without `--run` remains possible with just the CLI/source.

`--dispatcher-state /absolute/installed/state` additionally runs the two native startup cases
through that exact installed wrapper. Its state is mounted read-only at its recorded path,
and the private fixture uses a recognized `lain.models.json` location. The wrapper and bound
CLI hashes must match the installation manifest. All user configuration remains synthetic.

## Verification status at the original preparation handoff

Prepared: six Core tests cover optional routers/overlap, strict validation, unchanged legacy
catalog loading, Unicode labels, exact profile/provider gating, and invalid extensions outside scope. Eight TUI
tests cover both menu levels and Escape, real-router slug persistence, disabled missing router,
unfiltered Alle, refresh after a router disappears, removed groups, Reserve restrictions,
provider opt-in, and missing/other profiles despite provider `lain`.

**These Rust tests and the binary have not been compiled or run in this handoff.** No Rust
compiler/toolchain was installed and no client/provider request was started by this work. A clean
patch application, source reconstruction, shell syntax, Python parsing and git whitespace checks
are preparatory checks only. Native `/model` acceptance remains required before deployment.

Suggested root-lane checks after dependencies are prepared:

```sh
cd /absolute/new/source/codex-rs
cargo test --locked -p codex-core lain_model_picker --lib -j 1
cargo test --locked -p codex-tui lain_ --lib -j 1
cargo test --locked -p codex-tui model_picker --lib -j 1
```

Run upstream formatting in that same toolchain and review the resulting patch. Include any
intentional formatting changes in the pinned patch before final packaging.
