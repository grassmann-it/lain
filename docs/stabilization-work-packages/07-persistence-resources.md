# STAB-07 · Persistence and bounded resource checks

Planning status: Ready for bounded synthetic scenarios; resource thresholds must be grounded in measurement.
Owner role: Usage/measurement owner.
Dependencies: STAB-00; rerun affected scenarios after STAB-03/04 changes.

## Outcome and boundaries

Verify durable usage and bounded live/resource behavior on changed components.
No new429 adaptive controller or full concurrency telemetry project in this
stabilization package. Do not blame an unrelated model-usage process on lain.

## Ownership

Usage/journal fixture suites, Inspector boundedness tests and private measurement
harness. One owner for DB migrations; propose any migration to integrator first.

## Work

1. Replay duplicates and crash/restart interrupted attempts using existing suites.
2. Compare summaries/CLI/GUI aggregation for fixed data/timezone.
3. Measure idle, closed-window, sustained text/tool feeds, expiry and reconnect.
4. Check object/retained-content bounds and eager technical rendering identified by Live review.
5. Record PID/binary, exact artifact, input rate, duration, baseline/peak/steady readings.

## Acceptance — proposed, not executed

- Ten replay passes leave sums unchanged; unknown usage remains unknown.
- Restart preserves committed observations/checkpoint and exposes incomplete attempts.
- Same fixed query returns consistent aggregation; no client-side incompatible totals.
- Content/render memory stays within configured retained-data bounds and does not grow
  indefinitely through identical repeated cycles; GC/RSS distinction documented.
- Performance regressions use measured before/after, not invented acceptable numbers.
- /tmp is not filled by builds; no private stream content persisted for convenience.

## Delivery

Return changed paths, source/artifact references, focused checks actually run,
remaining gaps and any blocker. Use the shared evidence format and execution
boundaries in [the package index](README.md). This document creates a work package;
it does not authorize production changes, new logins or release publication.
