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

The integrated .NET checks pass: models/collections/router fixtures, 101 profile
application checks, 66 Codex export assertions, 78 transport assertions, 104
applied-profile checks and 342 client/CLI checks. All six builds complete without
warnings or errors. The CLI fixture has 71 successful PTY/pipe/JSON captures,
including widths from 20 to 200 columns. The updated dispatcher previously
passed 21 offline tests and the source preparation passed four lock tests.

The exact new Core passes 214 Go test events across three packages, 15 synthetic
regression cases including 200 same-alias requests and durable replay/restart,
three real SDK/router HTTP cases, and an actual admission-drain HTTP probe.
No real provider request is part of these checks.

The exact patched Codex passes [seven native PTY cases](../tests/codex-picker/native-acceptance-2026-10-25.md).
Group/member menus, overlapping membership, Escape, visibility and exact model
slug persistence are verified; six out-of-scope/profile/provider cases retain
the upstream picker. The separate mixed-package run passes seven Host/CLI/Core
checks in a private PID/network namespace, including two Responses turns across
a Host restart, matching Usage identities, opt-in export and stale CAS 409.
Desktop remains byte-identical to the baseline. Fourteen targeted Rust library
tests are still running and are not yet counted as passed.

The narrow retained-Desktop update helper passed 15 synthetic file fixtures after
independent source review. Five additional tests exercise real exact-package
file transactions, an argv-reference process guard, a complete stopped synthetic
backup, tamper refusal and actual file-exchange reversal after a write fault.
Thirteen isolated mocked own-operator cases pass. The exact own-service update
plan passes without service actions; installed acceptance remains pending.

The follow-up compact `lain usage` implementation now uses adaptive tables and
hides accounts without useful measurements behind `--full`. Single imported
email labels and generic account labels use short provider names in this view;
custom names and multiple accounts remain distinguishable. Empty reset columns
are omitted; mixed reset data uses a dash for missing values. Full/JSON preserve
the original data. No extra TUI package was introduced. Its integrated C# and
PTY checks pass; the actual installed display is still to be checked.

Evidence root: `~/.cache/lain-qualification-20261025/model-picker/`.
Original failures are retained beside later successful runs.

## Delivery

Not yet installed. Baseline Host is `5f41fba`, CLI `b49096f`, Desktop `67fb26c`;
service ready at desired/applied revision 35/35 with 15 accounts and 58 routes.
The normal Codex config SHA-256 baseline is
`5d0f52fa3779660e0db053568e3ea84dce070d48abb73e1dfe77491328d429f6`.
That early file changed before this delivery's first installed mutation; its
cause is not established. The current preservation boundary uses SHA-256
`2a21ff22a80155ecda864c3757f9f824be284081c0b5f5b613c0c80fac7e94bb`
and separate opaque fingerprints for the other ordinary Codex files.

The prepared group activation preserves all accounts, routes and profiles.
Free and IT Sec are collections over their existing route IDs. Automatic
provider/IT Sec targets must first pass the actual Host export compiler. Free
automation is not activated: no current route has effective `free_only`, and
the cached OpenRouter key spend cap is 20 USD; the qualified runtime guard
requires a fresh same-key zero-USD cap. Existing key limits and route policies
are not changed to conceal this prerequisite.

Installation and post-install evidence must be recorded here before claiming
the new menu is available. Existing account credentials, history, route IDs,
ordinary Codex settings and the separate legacy proxy are preserved.
