# N02 — Synthetic regeneration and OpenRouter Free day

Source baseline: `7661886`. This package changes only the two readers and focused
fixtures. No provider request, login, inference, installation or runtime mutation
was performed. Source completion is separate from the pending build/runtime gate.

## Implemented

- Synthetic rolling `tickPercent` is a bounded ratio (0..1), converted to
  percentage points. `0.02` becomes 2%; modern `nextRegenPercent:2` remains 2%.
  Missing/null/invalid inputs stay unknown, true zero stays zero. Weekly exact
  credit strings, preferred modern blocks, legacy subscription fallback and
  independently supplied rolling constraints retain their existing contracts.
- OpenRouter Free readings carry an explicit account-scoped UTC-day window,
  observation time, next UTC midnight as policy-derived reset and a freshness
  deadline capped at that midnight. The five-minute deadline matches the existing
  quota refresh interval. Injected `TimeProvider` permits deterministic checks.
  Request-start time anchors the window; a response crossing the day boundary
  supplies no current Free usage/remainder. USD key caps are unchanged.
- Free `WindowId` is `free-model-daily-requests`, `MeasurementKind` is `quota`,
  `Scope` is `account`. No account `ScopeId`, budget pool or cross-key merge is
  inferred. The label and window retain that the reset derives from UTC-day
  policy; it is not a provider-supplied `resets_at` or a wallet refill.

Existing research was read in Main, not copied or committed:
`docs/provider-monitoring-review-2026-10-25/subscriptions.md:209` and
`docs/provider-monitoring-review-2026-10-25/kilo-openrouter.md:48,99–101`.

## Shared integration owned by Root

The existing `QuotaService.Project` marks expired `FreshUntil` readings stale but
still projects numeric `Remaining`. Reader changes alone cannot invalidate a
previously cached day at a later GET. Root owns the corresponding projection and
fake-clock regression: for this account-scoped Free window, at/after `WindowEnd`
the current `Remaining`, `UsedPercent`, and `ExactRemaining` become unknown;
historical `Used`/`Limit`, observation timestamps and stale state remain. It must
not fabricate the new day's full allowance or alter the independent USD key cap.
No DTO or client policy was changed by this package. Whole-package acceptance
depends on Root's shared expiry integration and the tests below.

## Prepared checks and execution boundary

`tests/next-readers/NextReaders.Tests.csproj` links production reader sources and
shared quota contracts without building the Host. Fake HTTP/clock cases cover:

- Ratio and percentage units, zero/null/missing/malformed/out-of-range values.
- Preferred new schema, exact old credit strings, legacy subscription fallback
  and coexisting rolling constraint without invented additive pools.
- UTC-day reset, before/exactly-at/after-midnight reads, ordinary freshness,
  Europe/Berlin spring/fall DST offsets and JSON field projection.
- Explicit Free zero vs unknown, separate USD keycap and unknown account identity.
- Fixed metadata endpoint, selected synthetic credential, injected clock and a
  response crossing UTC midnight. No inference is used to obtain a counter.

The prior gateway fixture's obsolete `ResetsAt=null` assertion was updated with
Root's explicit authorization to check the policy-bound one-day window instead.

Pending commands for the serialized Root slot (small standalone .NET projects,
one process at a time, no GUI/Core/provider/network service required):

```sh
dotnet build tests/next-readers/NextReaders.Tests.csproj -m:1 -p:BuildInParallel=false -nr:false
dotnet run --project tests/next-readers/NextReaders.Tests.csproj --no-build --no-restore
dotnet build tests/gateways/Gateways.Tests.csproj -m:1 -p:BuildInParallel=false -nr:false
dotnet run --project tests/gateways/Gateways.Tests.csproj --no-build --no-restore
```

Additionally run the existing account-connect reader regression in Root's normal
Host build lane and Root's new cached-day-expiry test. These runtime checks have
**not** been executed by this worker; no build/restore/dotnet command was run.
Static verification passed: `git diff --check`, project XML parsing and all 13
literal JSON fixtures parsed. The focused suite has 29 static assertion callsites;
loops add cases at runtime. This is not evidence
of compiled or installed behavior, real provider access or GUI/CLI runtime tests.
