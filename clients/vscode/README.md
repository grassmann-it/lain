# Codex model groups in VS Code

Local Lain adaptation for `openai.chatgpt` 26.1021.22027, Linux x64;
the local package version is 26.1021.22028. No public package distribution.

The existing Codex picker gains groups and models with back navigation inside
its model selection. Power/reasoning controls, model selection, and thread
updates continue to use native Codex callbacks. Groups, order, overlapping
membership, and routers come from `lain_model_picker` in the effective client
catalog. The All group contains each natively visible option once.

`lain-groups-host.cjs` checks the local app-server context, all workspace
bindings, catalog files, and the complete native model inventory. It returns
only validated group data. Different providers/profiles, remote connections,
custom executables, and bindings that cannot be verified retain the native
presentation. The UI rechecks when opening and while the menu is open;
cancelled or stale responses must not reuse old groups.

The patch source is `scripts/clients/vscode_groups_patch.py`. Exact input hashes
and unique structural anchors constrain the transformations. Complete third-party
bundles are kept only in the private candidate. The bundled alpha binary and
its helper are not replaced.

[Maintenance and rollback](maintenance.md),
[implementation plan](../../docs/vscode-model-groups-plan-2026-10-26.md),
[local delivery status](../../docs/vscode-model-groups-delivery-2026-10-26.md).

The reasoning display follows the catalog. An empty list cannot show selectable
values. For the specifically reported old Astra export, the guarded repair is
`scripts/clients/repair_codex_reasoning.py`: native values `low` through `max`,
default `low`, a new immutable catalog generation, and conditional rollback.
It neither applies a profile nor restarts a service.
