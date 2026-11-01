# Codex auto-compaction metadata repair · 30 October 2026

Status: **local client catalogs repaired; exporter source fixed/tested, not installed in the running Host**. No recording code, provider replay or service restart.

## Observed failure and boundary of the conclusion

The VS Code screenshot reports `stream closed before response.completed` while retrying. Latest Code log `20261030T170216/window1/.../Codex.log` records five disconnect warnings at17:03:05–17:03:42 local for turn `01a18d8c-ce2f-7370-8f43-b05e752ec920`. Public lain attempts show six failures recorded400 for conversation `01a18840-f0f0-7203-8453-885c385fc37b`, native Codex `gpt-6.1-sol`, requested high, no reported usage. The earlier turn and a new fork also failed. Another conversation succeeded on the same model/effort/account in the same interval. This is not a demonstrated clipboard/Vision failure or general model/account outage.

Only metadata was extracted from the selected session files: their last known input usage is920268 tokens, repeated without new measurements during errors; modelContextWindow is null. The old conversation has one recorded compaction, the fork retains the same last usage. These values are not fresh token measurements of each failed request. Provider rejection details were not available; this audit does not prove that context is the sole cause of every400.

## Confirmed auto-compaction defect

Our CodexProfileAdapter exported neither context_window nor max_context_window nor an explicit auto_compact_token_limit. Root config supplies no context/auto-compact override. In the pinned native client, ModelInfo.auto_compact_token_limit derives90% of resolved_context_window, otherwise returns the configured optional limit. With both absent the result is None; context_window_token_status cannot trigger a threshold-based compaction.

Pinned source: `clients/codex/upstream.json`, upstream commit `6aaac694803004a63c6f393d153c96d22521a6b9`; `codex-rs/protocol/src/openai_models.rs:529`, `core/src/session/context_window.rs:65`. Native model metadata provides standard context272000/max872000 for the selected Sol and most current native models. These are **native client defaults**, not proof of provider entitlement or an exact universal server rejection threshold. Automatic compaction defaults to244800 tokens; usable context reporting with the client's95% headroom is258400. Other pinned models keep their own distinct values.

## Source correction

- `ModelCompatibilityContext.cs`: exact known native model + actual qualified Codex transport yields pinned client context defaults; stale transports, unknown clients/models and third-party name lookalikes don't borrow them. Qualified route-specific documented context can be retained separately.
- `CodexProfileAdapter.cs`: emits context_window/max_context_window per route; routers use the intersection/minimum of actual known member windows, unknown targets don't acquire fabricated limits.
- `InputModalityCases.cs`: context defaults, unknown/mixed router, stale transport, third-party alias and unqualified-client regression assertions alongside the image tests.

117 Codex gateway export assertions passed; existing two platform-analyzer warnings remain unrelated. No test calls a real provider.

## Native behavioral proof

Both installed backends were tested with isolated HOME/workspace, a loopback fake Responses server and two tiny user turns. Only the first fake response **declares**300000 input tokens to exercise the threshold; no such tokens were actually billed/spent.

| Backend | Catalog without context | Catalog with pinned context |
| --- | --- | --- |
| CLI0.164.0 | 2HTTP requests,0compaction items, reported window null | 3HTTP requests, start/completed compaction items, reported window258400 |
| VS Code backend0.164.0-alpha.11.1 | Same missing-threshold behavior | Same automatic-compaction behavior |

Evidence: `~/.cache/lain-context-fix-20261030/native-compaction-evidence.json`. This proves the native metadata/threshold behavior; it does not prove that a manual compaction of the user's existing huge history will succeed through the live provider.

## Local catalog repair

Using exact currently applied Core bindings and pinned model metadata, added only context_window/max_context_window to eight native entries in a copy of the active image-repaired catalog. Other464 entries, all groups/order, reasoning settings, defaults and image capabilities preserved. No root model-context global override was introduced.

Used existing CodexArtifactInstaller validation/transaction code, first in an isolated destination, then for the separate lain profile at applied revision917. Updated root's sole catalog pointer after parsed TOML equality checks; all other root settings preserved. Desired919 was not applied or re-exported.

Active generation at delivery:
`~/.codex/.lain-artifacts/c0042f6e5011422f8abf921693831105/lain.models.json`.
Private backups/receipt: `~/.cache/lain-context-fix-20261030/backup` and `receipt.json` beside it. Existing transcripts were not edited or replayed.

## User recovery and remaining delivery

Reload the VS Code window/backend to load the new model metadata; restart CLI processes as needed. In the oversized conversation, try `/compact`, wait for confirmed completion, then retry the user message. A fork alone retains the prior context and is not compaction. If compaction itself rejects, obtain the concrete error; do not claim a successful recovery or silently truncate/rewrite history.

The running Host still has its old exporter. Another catalog/profile export from it can reintroduce missing context/image metadata until the qualified Host update carries both source fixes. Current local catalog repair is active; no installed Host/Core update is claimed.
