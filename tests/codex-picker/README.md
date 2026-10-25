# Native `/model` acceptance harness

`native_model_picker.py` is prepared source only. It has **not been run against a client** in
this handoff. Root must first finish the exact patched binary and release the serial runtime lane.
The script defaults to fixture preparation; only `--run` starts its explicit `--binary`.
It never discovers or launches the installed production `codex` from PATH.

```sh
python tests/codex-picker/native_model_picker.py \
  --binary /absolute/new/source/codex-rs/target/dev-small/codex \
  --source /absolute/new/source \
  --output /absolute/new/artifacts
# Add --run only when the build and root runtime lane are ready.
```

Each case gets its own fresh synthetic home, `.codex/config.toml` sentinel, separate
`lain.config.toml` and `other.config.toml`, synthetic catalog, workspace, PTY, and bwrap
PID/network/IPC/UTS namespaces. The original `HOME` value is preserved and the private home is
bind-mounted at that existing path. `HOME` and `CODEX_HOME` are never reassigned. The environment
is an explicit noncredential allowlist; no user shell or DISPLAY is inherited. The harness
compares actual network/PID namespace IDs to the host and verifies the mounted catalog hash.
The client is
started directly with `--no-daemon --no-alt-screen`, from the private trusted workspace.
The source seed is the public tag's `codex-rs/models-manager/models.json` (the requested
`core/models.json` does not exist in that tag); complete entries are copied before overriding
synthetic slugs and picker presentation.

The configured provider is a local synthetic HTTP server inside the isolated network namespace.
It rejects and records every request. **The only textual input is `/model`; no inference prompt
is submitted.** Any provider request fails acceptance. No external network is available.
No original user config, auth store, or installed daemon is used or read. The ordinary sentinel
in the private mount must remain byte-identical; only the synthetic selected profile may persist
a chosen wire model slug.

Seven cases cover the actual native TUI:

1. `--profile lain` + provider `lain`: Free, IT Sec, Provider, Alle; disabled Automatisch; exact
   membership, overlap, hidden-model filtering, unfiltered Alle, Escape to groups, and selecting
   Shared Model persists `synthetic/shared` only in `lain.config.toml`.
2. Normal invocation with provider `lain`: original quick-auto picker.
3. `--profile other` with provider `lain`: original picker.
4. `--profile lain` with another provider: original picker.
5. Normal invocation with an invalid extension: upstream ignores it.
6. Another profile with an invalid extension: upstream ignores it.
7. `--profile lain` and another provider with an invalid extension: upstream ignores it.

Deterministic actions use Home/Down/Enter and Escape. The startup composer can appear before
session configuration; only the same `/model` command is retried after the known startup-disabled
warning. Membership assertions inspect the active popup region so the ordinary model header
cannot create false failures. The native UI event path is exercised;
there is no model-selection wrapper or synthetic AppEvent injection. The configured real router
case and reasoning/persistence event details are covered by the Rust library tests; this fixture
has no router and makes its disabled status explicit.

Artifacts include exact binary SHA-256, per-case plans/results, raw PTY transcripts, current-screen
text captures, stderr, provider-request logs, and the synthetic home files for inspection.
Provider-request and sentinel checks run after the private client is reaped; the outer supervisor
independently validates each result and the final synthetic sentinel hash. The
small VT recorder supports cursor movement, clears, line insertion/deletion, scrolling, SGR/OSC
skipping and terminal query replies. It is an assertion recorder, not a screenshot or visual
approval. Native startup/terminal incompatibilities fail with a retained final screen; do not
convert such a failure to a passed check. Private Xvfb/xterm screenshots can supplement this PTY
run later if needed. No native test or runtime process has been started while writing the harness.

The separate `test_prepare.py` suite only tests source-lock verification and is safe offline:

```sh
PYTHONDONTWRITEBYTECODE=1 python tests/codex-picker/test_prepare.py
```

Observed native harness corrections are retained in separate evidence attempts:
r1 sent slash text and Enter in one paste burst; r2's two ordinary-invocation
sentinels changed only because upstream first-start screen-reader detection
persisted its marker. The fixture now seeds the existing
`tui.screen_reader_detection_done = true` setting. Neither correction changes
the product binary, scope gate or model picker. Final evidence comes from a
fresh complete run; the original failed attempts remain available.
