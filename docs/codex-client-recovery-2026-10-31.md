# Codex client recovery — 31 October 2026

## Current local delivery

The installed Lain application matches all 1,155 files of the intended package
(source `80dcf135da1f48d27e573afa668fe2b698c22070`, manifest
`2529d5f51bf0839716fd8239ca1e0b8aac92570b26c24223efbe02e4f4885371`).
Live Host reports version `0.8.0+80dcf13…`, Gateway ready, revision **926/926**,
profile `lain`. Systemd login autostart is enabled. The supported Core service-start
operation cleared the stale automatic-start suppression without restarting the Core.
This independently reconciles present state; it does not rewrite the failed original
upgrade transaction or qualify a public release.

The automated full upgrade exchanged the application, then failed during its
apply/client-export phase (`owned_command_refused_lain`). The receipt lacks the exact
failed CLI arguments/stderr, so the individual failing command is unknown. Preserve
`~/.cache/lain-full-upgrade-20261031/runs/20261031T204308Z-2e44bab8/transaction/upgrade.json`
as failed. **Do not rerun that old operator against revision 926.** Full preservation
and closed-history checks from that transaction were not retrospectively proved.

## Terminal model groups: repaired and installed

An upstream Codex installation replaced `~/.local/bin/codex` with a direct symlink to
its official 0.166.0 executable, bypassing Lain's dispatcher. The catalog itself was
valid: **467 models, 38 groups**, including OpenRouter (376 models) and Synthetic
(3 models).

The normal entry now points to
`~/.local/share/lain/clients/codex-profile-lain/dispatch.py`.
The managed installer reports `readyForGroups: true`, no issues. The previous client
state was retained at `~/.cache/lain-codex-menu-repair-20261031/previous-client`.

This repair uses the previously qualified **0.164.0 patched TUI** for ordinary Lain
interactive root starts. The official **0.166.0** executable is retained for original
routes, including version/help, other profiles and all shell subcommands. In
particular, **`codex resume …` still enters the original client and has its flat
picker**. This is not a port of the menu patch to 0.166.0.

To enter the repaired picker, open a fresh Terminal session with:

```sh
codex --no-daemon
```

Then use `/model`. Existing processes are not hotpatched. `codex --version` continues
to report original 0.166.0 by design; that command does not identify the grouped TUI.

Validation: 92 offline dispatcher tests passed; default and explicit Lain-profile
native cases passed; patched TUI compatibility with an already running private
0.166.0 daemon passed. An additional isolated test launched the **actual installed
wrapper with the exact current catalog**, opened the group menu, entered the seven
Codex models, and returned with Esc. No provider requests were sent and private
configuration stayed unchanged. The official fallback executable was checked against
the official release archive digest; the new qualification is fallback-only.

Evidence under `~/.cache/lain-codex-menu-repair-20261031/`:

- `production-install.json`, `installed-status.json`, `dispatcher-tests.log`.
- `native-staged-r2/summary.json`, `native-existing-166/summary.json`.
- `actual-catalog-proof/result.json`, raw `session.ansi`, and numbered terminal text
  snapshots. PNGs there render terminal text; they are not desktop screenshots.
- `installed-package-reconciliation.json`, `runtime-start-reconciliation.json`, and
  `final-runtime-status.json` / `final-service-status.json`.

## VS Code: recovered; intermittent startup cause remains open

The installed local groups extension `openai.chatgpt-26.1021.22028` is intact:
22,607 files match the trusted candidate, with only permitted package metadata
excluded. Initial JS imports and syntax checks found no missing resource or parse
fault. Current descriptor limits do not support an FD-exhaustion explanation.

The previous error was the extension's **30-second renderer-ready watchdog**, while
its app server had initialized. No concrete renderer JS/CSP/resource error is
available in the production logs. Do not claim a proven root cause or patched fix.

After the latest production reload, the log explicitly reports `app_start
outcome=success` at **22:39:42 Europe/Berlin**, followed by `thread_hydration
outcome=success` at **22:41:20**. The exact installed extension and actual catalog
also rendered the grouped menu successfully in private native state. Evidence:
`~/.cache/lain-vscode-renderer-20261031-installed-actualcatalog/command-result.json`,
`snapshot.json`, `screen.png`. The old five-effort fixture assertion rejected the
current catalog's additional `ultra` value before UI startup; that failed evidence
is retained. A subsequent isolated UI run proved rendering, not provider support
for that effort. No production cache reset or extension reinstall was performed.

If it recurs, capture the original webview Console/Network before the watchdog
replaces the document. Preserve user state/auth. Never delete globalStorage,
workspaceStorage or all WebStorage as a speculative repair. The preserved original
VSIX and guarded rollback remain available in `~/.cache/lain-vscode-groups/final-r3/`.

## Recording: enabled, currently incomplete

Content recording is enabled for owned Gateway routes, policy revision 1, 5 GiB disk
cap. **This is not lossless capture.** Final inspection reports `collection_failed`,
`unknownLoss: true`, and an increasing collection drop count, while local storage
reports ready and zero storage drops. See `final-recording-status.json` for the
exact observation. Do not infer that every conversation/tool fragment was archived.
Recorder collection/backpressure diagnosis remains a separate outstanding defect.

## Follow-up packages

1. Doctor should consume existing launcher/VSIX maintenance truth, distinguishing
   selected launcher, intact package and observed runtime activation.
2. Full-upgrade receipts should include optional client integration postconditions;
   backend success must not imply that an externally updated client still has groups.
3. Retain native regression cases for upstream launcher overwrite and actual VSIX
   activation/picker, without provider inference.
4. Expose existing guarded client repair paths through the shared CLI/UI facade;
   separately qualify grouped shell-resume routing and a newer patched Codex release.
5. Diagnose recorder `collection_failed` and visible losses before claiming reliable
   conversation capture. These follow-ups were identified, not implemented here.

Sixteen requested workers (5 Astra, 11 Sol) completed the investigations and native
proofs. Existing unrelated route/CLI work in the working tree was preserved.
