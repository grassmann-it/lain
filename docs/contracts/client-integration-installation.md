# Progressive setup and optional client integration

28 October2026. The user requests this setup order: accounts and quotas first,
optional Gateway next, then easy client-menu setup through install buttons and
CLI commands, including an explicitly applied VS-Code patch. Extended menus are
desired in terminal/Codex CLI and Desktop where feasible. This accepts the product
workflow; commands, UI placement, new client schemas and Desktop patch mechanisms
below are proposals, not an implementation or delivery claim.

## Setup levels

1. **Monitor:** connect accounts and inspect supported quotas. No Core, inference
   route setup, client patch or client configuration is a prerequisite.
2. **Gateway:** add/enable the owned Core and configured model access. Preserve
   monitoring accounts, names, history and settings. Show applied/desired state;
   enabling Gateway does not silently apply unrelated pending changes.
3. **Clients:** detect installed client/version/profile and expose only available
   connection/menu actions. Ordinary gateway connection and extended menu patch
   are separate capabilities; either may be configured independently if supported.

The information-level setting Barebones/Simple/Advanced remains independent of
these setup capabilities. Choosing a presentation mode does not install a Core
or patch an application. Optional integrations are not a Monitor dependency.

## One workflow behind UI and CLI

Suggested Clients settings cards: terminal/Codex CLI, VS Code, and the concrete
Desktop product/mode. Each card offers detect/status, connect/configure, enable
extended menu, repair/update and restore original as supported. No generic green
Installed state for a merely generated catalog.

Illustrative future CLI shape, NOT commands present in the installed CLI:

```text
lain clients list
lain clients inspect vscode
lain clients install vscode --feature model-menu
lain clients repair vscode
lain clients restore vscode
```

Final command names should be fitted to existing CLI conventions. Button and
CLI must invoke the same integration service and return the same plan/receipt,
version support, progress and failure classification. Do not put independent
installer logic or elevation scripts in UI event handlers. Operations on user
client files run in their owning user context, not arbitrary remote Host paths.

Workflow: detect exact target → inspect capabilities/ownership → present affected
files, local adaptation and update implications → prepare verified artifacts and
backup → explicit install/patch operation → verify selected artifacts/config →
report activation/reload state. The user's Install action is the explicit step;
do not add repeated confirmations unless a new material effect requires it.

Reload a client only through a separate user action or agreed safe workflow.
Running conversations/agents do not get terminated as an install side effect.
A connection test without inference can inspect effective configuration/model
inventory; actual paid/provider inference is separately authorized and reported.

## Current evidence and integration limits

| Target | Existing mechanism | What remains unproved or needs work |
| --- | --- | --- |
| Codex terminal | Separate patched runtime, qualified local dispatcher and catalog export; original binary retained | Easy shared installer UI/CLI, new nested picker schema, native qualification for new bytes/platforms |
| VS Code Codex extension | Existing pinned VSIX preparation/install/status/rollback via scripts/clients/vscode_groups.py | Shared install-button/CLI facade, new menu version qualification, release/distribution suitability |
| Desktop local Codex surface | Official gateway documentation describes CLI plus macOS/Windows app config; product/mode/build must be identified | Actual installed target, local-provider connection, menu injection mechanism, packaging/signing and native acceptance |
| Ordinary ChatGPT chat | No arbitrary inference redirection established by these sources | Do not promise gateway/model-menu support based on local Codex support |
| Other terminal/chat clients | Protocol-compatible flat routes may be configurable | No universal menu patch; add explicit adapter/capability support per client |

Official sources read on28 October2026:
[Gateway connection](https://learn.chatgpt.com/docs/enterprise/connect-to-a-gateway),
[Codex IDE settings](https://developers.openai.com/codex/ide/settings/).
Configuration inheritance/gateway support does not establish menu patch support.
The user phrase ChatGPT Desktop must not silently be substituted with another
app. Detect or clarify the intended local Codex mode before implementing its adapter.

The historical local VSIX keeps extension ID openai.chatgpt and shares its storage
identity. It selects a local modified package in place of the official package;
original bytes are preserved, but this is not a side-by-side isolated extension.
Versions/hashes are pinned, unknown updates are not blindly patched. The current
mechanism is Linux-x64/version-specific and not an already supported public package
for every VS-Code installation. See [maintenance](../../clients/vscode/maintenance.md).

## Installation guarantees

- Preserve user accounts, unrelated settings/extensions and histories. Keep
  credential references separate; client gets only its local inference key.
- Operate on detected exact versions with qualified transforms; unknown build
  reports unavailable/update qualification needed. No fuzzy patch success.
- Bind preparation, backup and installation evidence to exact source/artifact
  identity. Recheck target state before mutation; do not overwrite concurrent updates.
- Interrupted operations retain identifiable intent and recovery. Timeout means
  uncertain until reconciled; do not repeat an install as a fresh operation.
- Restore only recorded owned changes under matching preconditions; preserve
  intervening user edits and explain conflicts. A package backup is not a
  guarantee of arbitrary user-data rollback.
- Display managed update posture, original/patched version, and compatibility
  status. Prefer a verified update or the native menu when unsupported; never
  automatically adopt unknown bundles or silently disable global editor updates.
- Keep runtime generation and client catalog coherence; a menu file alone does
  not prove execution readiness. Scoped menu auto-sync approval remains open.

## Proposed acceptance cases

Fresh Monitor install needs no client package. Gateway can be added later without
account/history reset. A supported VS-Code target can be inspected, patched,
activated after explicit reload, inspected again and restored without unrelated
extension/settings changes. Unsupported versions remain untouched. Interrupted
installation and stale target produce explicit recoverable states. CLI and UI
share results. A running client is not reported active merely because disk bytes
changed. Concrete Desktop connection and nested-menu claims need separate native
verification for the actual target product/mode/platform.

No new installation, code patch, service change or acceptance run was performed
when writing this contract. The deeper menu proposal is
[model/access picker](model-access-picker.md).
