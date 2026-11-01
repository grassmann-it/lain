# PUB-02 · Active runtime and pending-edit integrity

Owner: existing STAB-01 runtime owner. Depends on PUB-00. This is that same
implementation lane, not a concurrent duplicate of STAB-01.

## Outcome and ownership

An unrelated desired edit does not remove valid exclusions from the applied
runtime; credential retirement/recovery fences stay authoritative. Own Host
Quota/RouterAvailability* and approved applied-snapshot readers; shared config/
Apply boundaries through integrator. No independent Usage migration changes.

## Tasks

- Reproduce current Projector Revision!=AppliedRevision empty-exclusion behavior
  and Publisher replacement with bounded synthetic fixtures.
- Project against a proven immutable applied snapshot/bindings/generation while
  honoring current revocation/retirement/recovery fences. Never restore a revoked
  credential solely because it exists in an older snapshot.
- Reject delayed/wrong-Core-generation messages and stale quota observations;
  preserve true0 vs unknown and shared-pool/reset scope.
- Expose desired/applied/client-generation status through existing DTO/report
  seams; preparation/export success isn't proof that desired routes are active.
- Retain partial Apply/recovery behavior and route continuation/account pinning.
  No automatic Apply to make mismatch disappear.

## Acceptance

Saved B while A runs preserves A's legitimate exclusions until expiry; retirement
or recovery-denied access remains blocked. Reordered source events cannot override
new generation. Faulted Apply/export doesn't produce a false ready state. UI/CLI
show the same effective status. Existing account/cost/continuation guarantees pass.

Return focused reproduction, changed source/tests, generation/credential cases,
unknown-data behavior and exact-artifact gate requests. Real model calls or state
writes aren't needed for initial reproduction. Full original STAB-01 criteria
still apply; this packet doesn't waive them.
