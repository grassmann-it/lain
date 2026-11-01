# STAB-08 · Exact-artifact release acceptance

Planning status: Staging preparation possible; version/support choice and preceding gates block final acceptance.
Owner role: Integrator / packaging and independent acceptance.
Dependencies: STAB-00 decisions; integrated STAB-01–07 relevant changes and any required optional client checks.

## Outcome and boundaries

Freeze reviewed source and produce genuinely new Monitor/Gateway candidate
packages. Verify first use, update/preservation and recovery on the exact bytes.
No publication, local production update or version renaming of old artifacts
follows from this planning task.

## Ownership

Build/release metadata, publish/install scripts, package manifests/notices,
acceptance reports and final support/installation documentation. Other workers
must finish shared-source changes before final freeze.

## Work

1. Agree final version (0.7 proposed,0.8 previously selected) and Linux support scope.
2. Reconcile WIP/locks, secret review, source/core/client identities and English flow.
3. Build serially using pinned SDK/locks and existing provenance tooling.
4. Isolated fresh Monitor and Gateway install, update saved!=applied state, recover.
5. Native client/GUI probes for exact changed versions; final authorized live matrix.
6. Present artifacts, evidence and unresolved limitations before release action.

## Acceptance — proposed, not executed

- Source, assemblies/About, Host, manifests and archives agree on selected identity.
- Fresh install defaults/toggles behave as documented; Monitor needs no patched client.
- Update preserves accounts/names/credentials/history and desired/applied separation.
- Failure/recovery restores a consistent supported state without refreshing external grants.
- Optional patched clients have version-specific evidence and restore path.
- macOS/Windows downloads are not offered based solely on contract CI.
- Source/build/synthetic/native/live/installation/publication claims remain separate.

## Delivery

Return changed paths, source/artifact references, focused checks actually run,
remaining gaps and any blocker. Use the shared evidence format and execution
boundaries in [the package index](README.md). This document creates a work package;
it does not authorize production changes, new logins or release publication.
