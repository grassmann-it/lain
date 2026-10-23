# Desktop redesign · 23 October 2026

The user rejected the initial desktop styling and requested the direction of
[MeowChan](https://github.com/tfgrass/MeowChan), including its Semi/Ursa component
stack. Functional acceptance of the previous UI remains historical evidence;
release acceptance stays open until the revised desktop is rendered and checked.

Reference: local read-only `/home/tomka/Desktop/oChan`, especially the compact
Settings and Downloads screenshots, Semi theme and Ursa components. Implement an
original lain layout using these packages; do not import unrelated MeowChan
application code, logos or behavior.

- Neutral graphite surfaces, restrained blue accent, Inter typography.
- Compact application toolbar and bottom navigation; useful status footer.
- Dense selectable account/profile lists and searchable route catalog.
- Consistent forms, actions, focus states and readable unknown/stale states.
- Usage keeps existing aggregation, filters, paging and five-second polling.
- Existing API, owned service, persistence and account isolation remain intact.
- Every GUI test uses a dedicated Xvfb display. Never fall back to the user's
  native display; unset Wayland in the test environment.

Twelve requested workers: theme/components (Astra xhigh), shell (Sol high),
overview/setup (Sol high), accounts (Sol high), login (Sol medium), routes
(Sol high), profiles (Sol high), usage (Astra high), quota display (Sol medium),
GUI test harness (Astra high), packaging/licenses (Sol medium), independent
visual/accessibility review (Astra xhigh). Each owns its files in an isolated
worktree; the integrator owns merges and final builds.

The existing installed backend continues running. Only a tested desktop package
will replace the desktop portion; no provider probes or legacy service changes
are needed for this redesign.

## Result

Implemented and installed. Independent review accepted 30 private-Xvfb renders;
63 workflow assertions passed. The installed Release desktop also preserved
real Usage totals through polling and close/reopen on private Xvfb. See
[acceptance](acceptance.md) and [visual review](ui-redesign-review.md) for scope.
Host, CLI and Core binaries and both running services were unchanged.
