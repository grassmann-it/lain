# Legacy CPA thinking replay mitigation

This is a prepared maintenance tool, not an installed fix. No production config,
service, binary, credentials or model catalog was changed during qualification.
It requires Python 3 and PyYAML. There is no build or provider request.

## Qualified scope

The installed legacy CPA binary has SHA256
`8b2e25d6546e2d84ab42d2f2a843fa1efc106adef27e2b9477d1cc2171bb0cb9`.
Both prepare and apply refuse another binary hash. This mitigation is deliberately
separate from the current Lain adapter implementation.

The tool adds three idempotent `payload.default` rules only for configured
Command Code `claude-sonnet-5-5` / `claude-opus-5-5` routes with `is-compat: true`,
base URL exactly `https://api.commandcode.ai/provider`, and distinct canonical
`cmd-claude-…` aliases. A colliding alias on another route is refused. Exact alias
matching matters because CPA also tests upstream model names as rule candidates.

Rules require source protocol Responses, target protocol Claude, adaptive
thinking, and nonempty signed thinking or redacted assistant history. Forced
`tool_choice` any/tool is excluded. Disabled thinking, unsigned/empty history,
direct Messages, unrelated providers/models and omitted thinking that remains
omitted are excluded. The rule does not enable thinking. Existing binding policy
wins. Applicable later override/filter rules that change thinking or model are
refused because those could invalidate the earlier adaptive gate.

For eligible requests the policy default is
`thinking.block_binding.prefix_mismatch_behavior: drop_block`. A required
`thinking-binding-controls-2026-08-01` beta is added to an absent beta list or
appended to an existing beta array without replacing other entries. Existing
required beta is not duplicated. Incoming beta headers are preserved by the
qualified gateway path. Arbitrary scalar/body beta layouts are not a generalized
merge contract. Responses has no native binding-policy field; explicit-policy
preservation was tested with an earlier configured translated-payload default
and with an excluded direct Messages request.

## Source evidence and tests

The matching legacy source is under
`Desktop/work/codex-proxy-tui/integrations/proxy-core/source`:

- `internal/config/config_types.go`: PayloadModelRule protocol/from-protocol,
  Match/NotMatch/Exist/NotExist fields.
- `internal/runtime/executor/helps/payload_helpers.go`: default first-write-wins,
  original-field preservation, conditional query expansion across arrays,
  `&&` query support, sequential rule application and SJSON append paths.
- `internal/runtime/executor/claude_executor_stream.go` and
  `claude_executor_execute.go`: payload rules run before beta extraction and
  before the final forced-tool thinking removal.
- `internal/runtime/executor/claude_executor_request.go`: body beta extraction,
  custom gateway beta preservation and binding beta gating.
- `internal/watcher/events.go`: watches the config file inode, not its parent.
- `internal/watcher/config_reload.go`: 150ms debounce; empty files and YAML load
  failures leave the old config in memory.

Run the offline fixture:

```sh
python scripts/maintenance/tests/test_thinking_replay_hotfix.py
```

Final result: **12 tests passed in 3.632 seconds** against the exact installed
binary. The fixture uses random loopback ports, private temporary config/auth/cwd,
synthetic credentials, `--local-model`, no plugins, disabled control-panel asset
handling, and unchanged HOME. The provider URL is redirected to a local stub.
The hotreload test substitutes only route selection for that loopback fixture;
production selection guards are separately tested.

Coverage includes stream/nonstream, Sonnet/Opus 5.5, signed/redacted history,
tool continuation, retained beta arrays/headers, caller-policy precedence,
forced tools, disabled/empty/direct Messages exclusions, alias/conflict guards,
wrong binary/hash refusal, private exact backup/rollback, and write error without
retry. Three in-place apply/rollback cycles preserve the same PID and inode and
restore exact bytes. One Claude SSE stream remains open through all three cycles
and completes afterward. Malformed YAML and an empty file were held beyond the
watcher debounce and the previous valid configuration continued to serve.

An earlier atomic-replace hotreload experiment **failed**: even after waiting for
`file watcher started`, replacing the config pathname did not update wire policy
within four seconds. This negative result is separate from the passing in-place
tests. Default atomic apply is suitable for a future startup config, not a proven
live update of this binary.

## Write modes and operational limits

Default writes use private temp files plus fsync/rename. Explicit
`--preserve-config-inode` performs a **non-atomic in-place update** for the legacy
watcher. It opens O_NOFOLLOW, verifies regular file/owner/single hardlink,
path-to-FD identity and expected bytes, then uses one pwrite. When shrinking,
newline padding replaces the old suffix before truncate/fsync. Short writes,
write/truncate/fsync/close errors and failed postchecks produce RecoveryRequired
with the private receipt path; there is no retry or automatic repair. Exact
before and after bytes plus receipt are durable before the first mutation, so
both apply and rollback have recovery bytes available.

Apply and rollback share the existing `.codex-proxy-admin.lock` and recheck
expected SHA under that lock. This serializes cooperating maintenance writers;
it is not an atomic compare-and-swap against an uncooperative writer.

File readers can briefly observe intermediate bytes in the in-place mode.
Empty/malformed input protection does **not** cover a syntactically valid partial
YAML document. The 150ms debounce is no guarantee under process or filesystem
pauses. This limitation is explicit, not an atomicity claim.

A same-PID reload also does **not** prove uninterrupted Codex WebSocket sessions.
The legacy runtime re-registers CodexAutoExecutor on config reload; replacement
can close its execution sessions. The fixture proves Claude HTTP/SSE behavior
only. A confirmed quiet window is required before any production live apply;
there is no authorization to apply or restart in this preparation report.

## Reviewable commands, not executed against production

Use reviewed SHA values; commands intentionally require them rather than reading
and approving changing bytes implicitly. A private existing output directory is
required for prepare; choose a new filename. Output config contains credentials
and is written mode 0600. Backup directory must be owned and mode 0700.

```sh
python scripts/maintenance/thinking_replay_hotfix.py prepare \
  --config /home/tomka/.local/share/easycliproxyapi/cpa-core/config.yaml \
  --expected-sha256 REVIEWED_ORIGINAL_SHA256 \
  --output /PRIVATE_DIRECTORY/config.thinking-replay.prepared.yaml
```

After independent review and a confirmed quiet window, a live legacy update
would use:

```sh
python scripts/maintenance/thinking_replay_hotfix.py apply \
  --config /home/tomka/.local/share/easycliproxyapi/cpa-core/config.yaml \
  --expected-sha256 REVIEWED_ORIGINAL_SHA256 \
  --backup-dir /PRIVATE_BACKUP_DIRECTORY \
  --preserve-config-inode
```

The JSON result gives the private receipt path and resulting SHA. Exact rollback
uses the same explicit write mode and refuses config drift:

```sh
python scripts/maintenance/thinking_replay_hotfix.py rollback \
  --receipt /PRIVATE_BACKUP_DIRECTORY/RECEIPT.json \
  --preserve-config-inode
```

After RecoveryRequired, do not rerun blindly: the receipt records the expected
before/after hashes and durable exact before/after backup paths. Normal rollback
can refuse a partial file because its expected after hash no longer matches.
Inspect those private artifacts and the stopped/active state before a deliberate
recovery. Never print the configuration or backups into a shared transcript.
