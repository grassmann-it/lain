# PUB-07 · Explicit offline demonstration

Owner: optional Desktop demo worker. Depends on00/shared presentation contract;
uses existing real view classes where possible. Not a first-release gate.

## Outcome and ownership

A user explores sample account quotas/models or a synthetic trace before entering
credentials. Own explicit demo fixture/provider module and isolated tests; shared
startup/navigation entry through00. No changes to real account stores/services,
provider credentials or inference clients.

## Tasks

- Define an explicit Demo entry/mode and persistent badge; never launch it as an
  unannounced fallback when a real service fails.
- Use bounded synthetic fake provider/clock/data, no user home/state/auth discovery,
  no real network/provider/browser login. Sample budgets never mix with real ones.
- Reuse actual UI layouts for setup/outcomes/quotas and, only if available, archive
  reading. Do not create a second simulated product UI to produce pretty screenshots.
- Leaving demo restores ordinary startup without writing sample accounts, usage,
  configuration or autostart preferences into the real installation.

## Acceptance

Private-Xvfb offline launch without accounts/services; unambiguous Demo marking on
screens and copied sample reports. Mutation affects in-memory fake state only,
network attempts absent, exit/reopen normal state unchanged. Unsupported features
stay unavailable instead of pretending a real login or test succeeded.

Return fixture/provenance/native isolation evidence. Implement only if chosen;
deferring this optional idea doesn't block any existing product requirement.
