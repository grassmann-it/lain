# N03 — MiMo browser startup and cleanup

Baseline: `7661886`. Scope: the existing CN/EU Individual Token Plan browser
connector. No Team, SGP, PAYG, account migration, inference-key change or new
provider grant. Worktree: `next-20261025/n03-mimo`.

## Observed failure and source diagnosis

The original [public probe evidence](../../tests/mimo-real-probe/public-probe-evidence.json)
records a production `OpenAsync` failure against Host `9d4f708`, hash
`9021bde8b76aecd4400191b1c96357684d4673f7702ada0b4903f86b18453860`.
Restore/build passed in its second attempt; the only observed terminal category
was `production-browser-read-or-start-rejected`, before a successful open or any
fixed API read. Its owned profile and processes were cleaned up. Browser stderr
was discarded. The evidence cannot identify the historical underlying cause.
It does not establish an X11, sandbox, provider, identity or entitlement failure.

Source review identifies two independently reproducible defects:

- Profile allocation, preference writing and `Process.Start` were outside the
  cleanup `try`. A thrown launch error could leave the freshly allocated profile.
- Startup exceptions reached the generic shared Begin failure, losing the local
  stage/reason. The MiMo adapter now returns its own fixed safe failure result;
  no shared Foundation exception pass-through is added.

The fix wraps allocation through endpoint publication in one cleanup scope,
retains ownership bookkeeping until cleanup succeeds, and fences host disposal
against a concurrent launch. Endpoint timeout is distinct from caller cancellation;
partial `DevToolsActivePort` publication is retried. A valid endpoint needs its
bounded browser path and a nonprivileged port. No sandbox is disabled and no
speculative Ozone/backend argument is added.

`MiMoConsoleBrowserStartException` exposes `MiMoBrowserStartReason`, a fixed
`ReasonCode`, `PublicMessage`, and optional numeric exit/OS codes. It has no inner
exception. Only exact known startup stderr markers are classified; an unknown
exit explicitly remains unknown. Output is still drained continuously, with at
most the first 16 KiB of stderr inspected in memory. Neither raw output nor paths,
cookies, URLs or credentials are exported in the failure. Cleanup failure is
reported separately and keeps the owned session available for cleanup retry.

## Local checks and pending serial qualification

Performed without builds or browser/provider processes:

- `git diff --check`;
- `bash -n tests/mimo-real-probe/run-private.sh`;
- fixture project XML parse and byte-identical dependency lock copied from the
  existing console-connections harness.

The new fake-process/runtime fixture project exercises 19 Linux cases: unavailable
runtime, invalid/pre-cancelled input, thrown native launch, five early-exit reasons,
bounded/unknown diagnostics, receipt failure, timeout, cancellation, cleanup retry,
invalid/linked/partially published endpoints, reconnect, disposal race, and safe
reason propagation through the actual login operation with preserved configuration
and inference key. It starts no browser, subprocess or network connection.
These are prepared fixtures, **not yet passed tests**.

In Root's assigned serial build slot, with private artifact/TMP directories,
existing NuGet cache and the repository's pinned SDK:

```sh
dotnet restore tests/mimo-browser-startup/MiMo.BrowserStartup.Tests.csproj \
  --locked-mode -r linux-x64 --disable-parallel -p:SelfContained=true \
  -p:BuildInParallel=false
dotnet run --project tests/mimo-browser-startup/MiMo.BrowserStartup.Tests.csproj \
  --no-restore -r linux-x64 -p:SelfContained=true \
  -p:BuildInParallel=false -p:UseSharedCompilation=false
```

Then run the existing `tests/console-connections` harness, including its synthetic
MiMo identity/generation/lifecycle and receipt-reconciliation checks. No full Host
startup or provider process is needed for either fixture harness. Keep the assigned
CPU/RAM limits and never parallelize builds.

## Smallest real startup probe, only after Root runtime-slot approval

The existing opt-in real probe now also accepts `--startup-only`. It calls the
production `OpenAsync`, immediately closes/erases its new owned browser profile,
and attempts **zero named provider reads, login interactions or screenshots**.
The browser still navigates to its normal public console URL; normal public page
network activity is possible. Its structured evidence records fixed startup reason,
numeric status, successful open and cleanup, not raw browser output.

1. Build `tests/mimo-real-probe` against the just-reviewed Host artifact in the
   serialized slot; pin that Host DLL SHA256. Do not use an older Host lacking
   the new exception contract.
2. Run a single browser under a fresh private Xvfb and private D-Bus, with fresh
   XDG directories and state below `~/.cache/lain-build-tmp`. The runner now uses
   this cache location instead of `/tmp`; `HOME` remains unchanged.
3. Use this exact opt-in mode with a new absolute evidence directory:

```sh
LAIN_MIMO_PROBE_SLOT=1 LAIN_MIMO_PROBE_HOST_SHA256=REVIEWED_HOST_DLL_SHA256 \
  bash tests/mimo-real-probe/run-private.sh --startup-only \
  /ABS/PRIVATE/ARTIFACTS/MiMo.RealProbe.dll /ABS/NEW/EVIDENCE
```

The production endpoint deadline is 15 seconds, cleanup has its own 5-second
budget, and the existing outer runner limit is 90 seconds. Root should bound the
unit's CPU/RAM and stop on resource pressure. Inspect only sanitized evidence and
verify no owned browser remains. No existing user browser profile or credentials
are an input. The historical cause remains unknown unless a new failure gives a
specific measured reason; a new successful run cannot retrospectively explain it.

## Separate live gate

Neither synthetic fixtures nor successful private startup qualify monitoring.
A real user must separately complete Xiaomi login/MFA in the fresh lain-owned
window and explicitly associate the discovered UID/region. CN/EU identity and
usable Individual usage must then be compared with the console before claiming
an authenticated monitor. This gate is open; no login, MFA, provider account,
entitlement or real quota success is claimed here.

No package update, installation or live-state modification has been performed.
