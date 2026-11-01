# STAB-04 · Bounded model reconciliation and honest menu delivery

Planning status: Ready to evaluate batching; runtime auto-sync remains out of scope.
Owner role: Account/model owner.
Dependencies: STAB-00 inventory; coordinate state generation with STAB-01.

## Outcome and boundaries

Reduce long partially configured imports using existing multi-model AttachModels
where appropriate. Preserve stable account/route IDs, custom labels, selected
products and paid/free constraints. Do not add a general discovery platform or
silently apply all desired changes as a shortcut.

## Ownership

AccountService attachment path, ModelsService and focused CLI/client mapping.
Current catalog edits/import scripts belong to their active owners; coordinate.
No direct writes to live configuration.json, no concurrent second import.

## Required user-facing flow

The already agreed simplified model-add experience belongs to this stabilization
package, coordinated with STAB-11. Start from the existing account/connection,
show eligible models with search/multi-select and clear billing restrictions;
do not require manual route IDs, copied endpoints or repeated account setup.
Make saved/runtime/client-menu state and the next necessary action visible.
Batch implementation alone does not fulfill this UI/CLI outcome.

## Work

1. Identify current import state and exact account/connection/model identities.
2. Evaluate existing array attachment and bounded validate/commit batches.
3. Define per-item diagnostics, partial batch semantics and idempotent retry.
4. Preserve group memberships/aliases and separate model listing from entitlement.
5. Tie explicit preview/apply/export to coherent generation; label reload limitations.

## Acceptance — proposed, not executed

- Repeating the same permitted model reconciliation creates no duplicate accounts/routes.
- Retry after timeout reconciles actual state before writing again.
- Invalid model/product does not silently change plan or endpoint.
- Names, account separation, history and group memberships survive.
- Provider catalogs do not masquerade as configured or qualified access.
- Menu export does not claim active availability before matching runtime; stale revision rejects.
- Account→add models→review→explicit activation/export is understandable in Desktop
  and CLI, preserves draft selection and does not ask users to reconstruct bindings.

## Delivery

Return changed paths, source/artifact references, focused checks actually run,
remaining gaps and any blocker. Use the shared evidence format and execution
boundaries in [the package index](README.md). This document creates a work package;
it does not authorize production changes, new logins or release publication.
