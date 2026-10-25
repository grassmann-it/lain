# N02 — Synthetic regeneration and OpenRouter Free day

Source baseline: `7661886`. This package changes only the two readers and focused
fixtures. No provider request, login, inference, installation or runtime mutation
was performed. The two small standalone checks below passed in the subsequently
allocated bounded slot. Shared cache-expiry integration remains Root-owned.

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

Commands executed sequentially in the allocated slot (small standalone .NET
projects, no GUI/Core/provider/network service required):

```sh
dotnet build tests/next-readers/NextReaders.Tests.csproj -m:1 -nr:false -p:BuildInParallel=false -p:UseSharedCompilation=false --verbosity minimal
dotnet run --project tests/next-readers/NextReaders.Tests.csproj --no-build --no-restore
dotnet build tests/gateways/Gateways.Tests.csproj -m:1 -nr:false -p:BuildInParallel=false -p:UseSharedCompilation=false --verbosity minimal
dotnet run --project tests/gateways/Gateways.Tests.csproj --no-build --no-restore
```

| Project | Result | Build / run wall time | cgroup memory peak |
| --- | --- | --- | --- |
| NextReaders | 44 assertions passed; zero build warnings/errors | 14.388 s / 1.391 s | 201,461,760 bytes (192.1 MiB) |
| Gateways | 37 assertions passed; zero build warnings/errors | 11.079 s / 1.317 s | 203,083,776 bytes (193.7 MiB) |

Each project ran in its own `systemd-run --user --scope`, respectively
`lain-n02-nextreaders` and `lain-n02-gateways`, with requested properties
`MemoryHigh=768M MemoryMax=1280M MemorySwapMax=128M CPUQuota=100% IOWeight=20
MemoryAccounting=yes`. Inside the scope the actual memory/cpu limits were read
back and asserted before dotnet: 805306368 / 1342177280 / 134217728 bytes and
`cpu.max=100000 100000`. `nice -n 10 ionice -c3` wrapped the process;
`MSBUILDDISABLENODEREUSE=1` and
`TMPDIR=/home/tomka/.cache/lain-build-tmp` were set. Nice=10 was verified.
An initial attempt using the service-only `Nice` scope property was rejected
before dotnet; process nice was used instead. A second pre-build instrumentation
attempt found `/usr/bin/time` absent; final timings used Python monotonic time
and cgroup `memory.peak`. No unbounded dotnet alternative ran.

The user cgroup delegates cpu/memory/pids but no I/O controller. Requested
`IOWeight=20` therefore could not be effective; Root explicitly authorized
continuing with real memory/CPU bounds, nice 10 and ionice idle priority. Both
runs had zero memory high/max/oom/oom_kill events. Both scopes were inactive when
the exclusive slot was released. Available memory before the first actual build
was 8520 MiB, before the second 8753 MiB and after both 7652 MiB.

Private logs with exact command arrays, bounds, results and peaks:
`~/.cache/lain-qualification-20261025/next/n02/next-readers.log` and
`~/.cache/lain-qualification-20261025/next/n02/gateways.log`. The private Python
measurement wrapper is adjacent; it is instrumentation, not a build dependency.
Restore changed no tracked source or existing lockfile. The new standalone
project's generated lockfile is included because the repository enables locked
dependencies; its `net10.0` dependency set is empty (no new packages).

Still pending in Root's normal Host build lane: the existing account-connect
reader regression and Root's new cached-day-expiry test. They were **not** run by
this worker. The two passing projects establish bounded source-reader behavior,
not Host cache expiry, installed behavior or real provider qualification.
Static verification passed: `git diff --check`, project XML parsing and all 13
literal JSON fixtures parsed. The focused suite has 29 static assertion callsites;
loops add cases at runtime (44 assertions executed). No GUI/CLI runtime test or
installed/real-provider qualification is claimed.
