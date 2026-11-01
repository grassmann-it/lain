# STAB-02 · Linux lifecycle and Monitor acceptance

Planning status: Ready for targeted source checks and isolated Linux tests; native OS targets separate.
Owner role: Lifecycle / installer owner.
Dependencies: STAB-00; final exact-artifact acceptance follows STAB-08.

## Outcome and boundaries

Qualify implemented startup rather than rebuilding it from the stale proposal.
Monitor needs Host only. Start-on-open and login-autostart are independent;
login startup defaults on for fresh setup, updates retain user choices.
OS owns Host, Host owns Core; explicit Stop and maintenance remain meaningful.

## Ownership

Client/Lifecycle, Desktop startup/settings, Host startup coordination, installer
and lifecycle/core-startup/desktop-startup tests. Shared packaging entrypoints
reserved with STAB-08 owner. GUI only private Xvfb/D-Bus.

## Work

1. Reconcile current classes with lifecycle contracts and existing tests.
2. Test absent Host, active Host, Monitor without Core, Gateway applied runtime.
3. Exercise Stop/ensure/start, maintenance admission and concurrent opening.
4. Exercise fresh install/autostart choice and update preservation in isolated state.
5. Record cold-start diagnostics before cleanup; investigate failures, not just timeout increases.

## Acceptance — proposed, not executed

- Opening/reopening UI reaches existing compatible Host without redundant restart.
- Start-on-open and login-start toggles act independently and survive restart/update.
- Stop and maintenance prevent unintended automatic restart; pending edits stay pending.
- UI closure leaves background operation working; Monitor has no Core dependency.
- Startup failure explains Host/Core/recovery stage without a crash/restart loop.
- Windows/macOS contracts are not presented as native package acceptance.

## Delivery

Return changed paths, source/artifact references, focused checks actually run,
remaining gaps and any blocker. Use the shared evidence format and execution
boundaries in [the package index](README.md). This document creates a work package;
it does not authorize production changes, new logins or release publication.
