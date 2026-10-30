# STAB-01 · Applied runtime, quota exclusion and client generation

Planning status: Ready for source investigation and bounded fixtures; no automatic Apply policy is implied.
Owner role: Host/runtime owner.
Dependencies: STAB-00 baseline; coordinate shared contract changes with integrator.

## Outcome and boundaries

Fix coupling between desired edits and active availability. Existing Projector
returns no exclusions when Revision differs from AppliedRevision and Publisher
sends an empty replacement. Generate active availability against a proven applied
snapshot, bindings and credential generations. Keep desired credential retirement/
recovery fences effective. Clarify preview/preparation vs runtime/client publication.

## Ownership

Host Quota/RouterAvailability*, Models/AppliedProfileStatus and necessary applied
snapshot readers. Existing configuration/apply boundaries are shared with STAB-04/05;
propose changes to integrator first. Do not independently rewrite migrations.

## Work

1. Reproduce pending-edit exclusion loss synthetically on current code.
2. Identify trustworthy applied snapshot and exact Core binding generation.
3. Project from active state while applying current revocation/recovery exclusions.
4. Keep desired/runtime/client generation visible through operation results.
5. Add focused cases to router-availability-host and applied-profile suites.

## Acceptance — proposed, not executed

- Unrelated pending edits preserve valid active exclusions until original expiry.
- Retired/recovery-blocked credentials never regain eligibility from old snapshots.
- Reset/stale observations remain unknown; delayed Core-generation messages reject.
- Preparation of desired B while runtime A is not labeled executable B.
- Partial runtime/client publication exposes effective state and bounded recovery.
- Existing strict accounts, explicit paid fallback and continuation guarantees hold.

## Delivery

Return changed paths, source/artifact references, focused checks actually run,
remaining gaps and any blocker. Use the shared evidence format and execution
boundaries in [the package index](README.md). This document creates a work package;
it does not authorize production changes, new logins or release publication.
