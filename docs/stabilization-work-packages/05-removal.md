# STAB-05 · Dependency-aware removal

Planning status: Impact inventory ready; cascade execution conditional on default/empty-profile policy.
Owner role: Host change-operation owner / UI integration.
Dependencies: STAB-00; active/credential completion aligns with STAB-01; serialize with STAB-04 on shared files.

## Outcome and boundaries

Replace dead-end dependency errors with a complete impact/removal flow.
Differentiate hide, disable, remove group membership, remove model access and
disconnect account. Preserve usage/history and durable credential retirement.
Do not implement arbitrary automatic replacement of removed defaults.

## Ownership

Shared change planner near Models/Accounts, repository cleanup boundaries,
client/CLI and Desktop actions. Integrator owns shared operation DTOs and migrations.

## Existing UX commitment

A dependency-aware removal experience is an agreed usability need, not a new1.0
idea. Prepare shared impact/ref-cleanup and clear Hide/Disable/Remove actions now.
Only the unresolved default/empty-profile and active credential completion cases
remain conditional; do not treat them as a reason to defer the entire workflow.
Coordinate entry points and draft/reconnect behavior with STAB-11.

## Work

1. Inventory profile/default/collection/router/fallback/pool/continuation references.
2. Produce one revision-bound impact plan; reuse existing repository cleanup.
3. Resolve default replacement and empty-profile behavior before cascade mutation.
4. Commit dependent saved edits together; handle secret/runtime effects with recovery.
5. Ensure later reconciliation does not restore intentionally removed managed access.

## Acceptance — proposed, not executed

- Referenced route removal lists all affected objects and requires no manual reference hunt.
- Stale review mutates nothing; no GUI loop of independent DELETE calls.
- Shared pools lose only intended membership, with empty objects handled explicitly.
- No default switches to unapproved paid access; historical identity remains.
- Old running runtime is not reported removed merely because desired state changed.
- Credential fences and running/continued requests are handled under existing guarantees.

## Delivery

Return changed paths, source/artifact references, focused checks actually run,
remaining gaps and any blocker. Use the shared evidence format and execution
boundaries in [the package index](README.md). This document creates a work package;
it does not authorize production changes, new logins or release publication.
