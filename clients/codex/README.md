# Opt-in Codex CLI model groups

This is a small patch over the public `rust-v0.164.0` source, not a selection wrapper.
It changes the real TUI `/model` path in `tui/src/chatwidget/model_popups.rs`.
The original installed Codex binary and configuration are not changed by preparation or build.
Deployment must use a separate binary and the `lain` profile only; it is outside this bundle.

The extension is active only with **`--profile lain`**, effective model provider `lain`, and
`lain_model_picker` in the configured `model_catalog_json`. The selected user-layer provenance
records the real CLI profile; legacy TOML `profile` and filenames do not activate it. Outside
this exact scope, the original upstream catalog deserializer ignores the extension completely,
including invalid metadata. Other catalogs and profiles keep the upstream picker.
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
patch, and writes source provenance. It never overwrites an existing checkout.

```sh
python scripts/prepare-codex-picker.py --fetch --output /absolute/new/source
# Or replace --fetch with --archive /path/to/upstream-rust-v0.164.0.tar.gz.
scripts/build-codex-picker.sh /absolute/new/source
```

The tag requires **Rust 1.99.1**. The build script uses the upstream `dev-small` profile, one Cargo
job, no incremental cache and `--locked`. It only builds `codex-cli`'s `codex` executable and does
not install or launch it. Upstream Cargo.lock contains 1474 workspace packages; the CLI dependency
closure and peak memory have not yet been measured. Its git dependencies include mxc, nucleo,
rmcp, runfiles and the upstream-pinned terminal/WebSocket forks. Network/toolchain/dependency
preparation and any build must run in the root's bounded serial lane.

## Verification status

Prepared: five Core tests cover optional routers/overlap, strict validation, unchanged legacy
catalog loading, exact profile/provider gating, and invalid extensions outside scope. Eight TUI
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
