# STAB-00 · Baseline, scope and effective contracts

Planning status: Ready for inventory; release version and supported-model scope remain user decisions.
Owner role: Integrator / documentation.
Dependencies: None.

## Outcome and boundaries

Establish a reproducible starting point without interrupting ongoing provider imports.
Re-read HEAD, dirty paths and active work; the planning baseline was47f3c79, with
older835d2e2 reviews. Do not redo Requesty error handling already committed.
Reconcile effective routing/startup requirements with current source and evidence.
Propose0.7 stabilization scope while preserving the earlier0.8 decision as reopened.

## Ownership

Own this package report, effective-scope headers in routing/startup docs and
support-matrix inventory. Integrator alone owns shared DTO/schema/build/version
changes; coordinate any overlap before editing. No version mutation in inventory.

## Work

1. Capture source/WIP and relevant installed metadata through supported read paths.
2. Identify active owners/imports; do not run a second attachment/apply script.
3. Record accepted/current/proposed scope and dated evidence in the existing contracts.
4. Identify exact Linux target, client builds and model/account matrix.
5. Record choices required for version, live-test spend/scope and optional removal policy.

## Acceptance — proposed, not executed

- A new reader distinguishes required, implemented, installed and verified behavior.
- WIP and active requests/imports survive the inventory unchanged.
- Existing tests are linked with their exact scope; none is promoted to a new pass.
- Version/model-scope blockers are explicit and do not block independent source fixes.

## Delivery

Return changed paths, source/artifact references, focused checks actually run,
remaining gaps and any blocker. Use the shared evidence format and execution
boundaries in [the package index](README.md). This document creates a work package;
it does not authorize production changes, new logins or release publication.
