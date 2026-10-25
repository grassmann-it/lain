# Codex model groups — implementation and qualification

Work in progress, 25 October 2026. This document distinguishes committed source,
synthetic qualification and installation. The existing installed service remains
the baseline until the delivery section records an actual update.

The user requested groups in the native Codex CLI `/model` menu, followed by
`Automatisch` and the group's individual models. Deployment is restricted to
`codex --profile lain`; the ordinary Codex configuration remains unchanged.
The confirmed automatic rule is the user's model order, with the selected model
and account retained for the conversation. Errors do not select another route.

## Implementation

- Optional revisioned collections reference existing route IDs in user order.
  Provider groups reference the same routes; membership can overlap.
- Explicit collection routers are profile-bound. Saving, applying and exporting
  are separate actions. See [routing contract](collection-router-v1.md).
- The grouped catalog is opt-in through
  `lain profiles install-codex HOST_PROFILE_ID --revision N --model-groups`.
  Existing wire aliases and the selected default route remain unchanged.
- The real Codex TUI is patched over the public `rust-v0.164.0` source, commit
  `6aaac694803004a63c6f393d153c96d22521a6b9`. The source archive, patch bundle,
  Rust toolchain and locked external dependencies are pinned. The patch applies
  only to actual CLI profile `lain` plus effective provider `lain`.
- A separate executable and reversible profile dispatcher select the patched
  client only for unambiguous root interactive `--profile lain` invocations.
  Subcommands and ordinary invocations keep the original executable. The
  [dispatcher contract](../clients/codex/dispatcher.md) records its Python
  `argv[0]` boundary and unsupported concurrent foreign updater case.

The patched catalog extension is our client contract, not an upstream feature.
No classifier, paid fallback or change to approval policy is included.

## Qualification

The serial build/runtime lane uses bounded CPU and memory. Build temporaries
are under `~/.cache/lain-build-tmp`; native sessions use private namespaces and
synthetic data, without access to the user's active desktop.

Already passed before the integrated router delta: model/collection tests,
45 Codex export assertions, 280 client/CLI checks and four lock-verification
tests. The updated dispatcher passed 21 offline tests. These are not substitutes
for the pending integrated routing and native-menu checks.

The narrow retained-Desktop update helper passed 15 synthetic file-operation
fixtures after independent source review. Its tests use mocked payload/process
checks and do not qualify an installed application.

The follow-up compact `lain usage` implementation now uses adaptive tables and
hides accounts without useful measurements behind `--full`. Single imported
email labels and generic account labels use short provider names in this view;
custom names and multiple accounts remain distinguishable. Full/JSON preserve
the original data. No extra TUI package was introduced. Its integrated C# and
PTY checks are still pending the same serial lane.

Evidence root: `~/.cache/lain-qualification-20261025/model-picker/`.
Original failures are retained beside later successful runs.

## Delivery

Not yet installed. Baseline Host is `5f41fba`, CLI `b49096f`, Desktop `67fb26c`;
service ready at desired/applied revision 35/35 with 15 accounts and 58 routes.
The normal Codex config SHA-256 baseline is
`5d0f52fa3779660e0db053568e3ea84dce070d48abb73e1dfe77491328d429f6`.

Installation and post-install evidence must be recorded here before claiming
the new menu is available. Existing account credentials, history, route IDs,
ordinary Codex settings and the separate legacy proxy are preserved.
