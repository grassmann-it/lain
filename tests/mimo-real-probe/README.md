# AC08a: real MiMo public-console/auth-gate probe

Prepared against source `6d9af6a`; the executed attempt below stopped before
opening the console. This is a real network
probe, separate from the synthetic `tests/console-connections` suite. It opens
the public console in production `MiMoConsoleBrowser.OpenAsync`, with fresh
lain-owned state/profile on private Xvfb and a private D-Bus. It performs no
sign-in, MFA, account publication, inference or existing-state discovery.

The only connector API reads attempted are production `ReadAsync` operations
`MiMoIdentity` (`GET /api/v1/userProfile`) and `MiMoIndividualUsage`
(`GET /api/v1/tokenPlan/usage`), once each. The actual website also loads its
ordinary page resources. No login form is submitted. No arbitrary CDP/script,
cookie import/export, response-body dump or replacement provider transport exists
in this harness. It does not start the Host or call its API.

The harness waits ten seconds for the public page, records a private X11 capture,
attempts the two reads, records sanitized outcomes and invokes production
`CloseAsync(erase: true)`. A generic session failure is **not proof** of HTTP 401,
successful navigation or a visible sign-in gate. Inspect `public-console-x11.png`
and report the actual page/stop condition separately. Exit 0 means the bounded
probe completed, both reads were attempted and its profile was erased; it does
not mean authentication, valid UID, plan entitlement or usable quota.

## Startup-only mode for N03

After the reviewed N03 Host build, `--startup-only` calls only production
`OpenAsync` and immediate `CloseAsync(erase: true)`. It attempts zero connector
API reads, login/MFA interactions or screenshots. Ordinary public-console page
resources can still load in the browser. Fixed startup reasons and numeric
exit/OS codes are recorded without raw browser output. The runner uses fresh
state under `~/.cache/lain-build-tmp` with private Xvfb/D-Bus/XDG directories;
`HOME` is unchanged. The historical failed attempt below remains unchanged and
its underlying cause is still unknown.

Use the same explicit slot/hash opt-ins below, replacing `--real-mimo-authgate`
with `--startup-only`. Exit0 in this mode means only opened and erased; it proves
neither authentication nor monitoring. See
[N03 qualification](../../docs/next-qualification/n03-mimo.md) for the exact plan.

For Root's single real startup attempt use `run-startup-private.sh`, with the
reviewed **new** probe DLL and its matching Host SHA/source commit. This wrapper
refuses an inherited HOME different from the user's passwd home and leaves the
HOME value unchanged. A private mount overlays its filesystem with a new empty
home; the real user's browser profiles, crash-report directories and sockets are
hidden. Artifact files are mounted read-only. The private `/tmp` contains X11
pathname sockets; `/run`, PID/IPC/UTS, Xauthority, D-Bus and XDG state are private.
The outer network namespace is retained for public-console resources, so X11's
abstract socket uses a unique display and private authentication. No sandbox is
disabled. The runner uses N09's software Mesa/llvmpipe/EGL environment and disables
Xvfb GLX; TMPDIR is on its fresh cache-backed filesystem. It never captures a
screenshot or calls a named connector read in this mode.

After the coordinator supplies the three reviewed inputs, this is the bounded
command (no build is performed by either script):

```sh
: "${LAIN_MIMO_PROBE_DLL:?absolute newly built MiMo.RealProbe.dll}"
: "${LAIN_MIMO_PROBE_HOST_SHA256:?matching reviewed Host SHA256}"
: "${LAIN_MIMO_PROBE_SOURCE_COMMIT:?full source commit for that build}"
: "${LAIN_MIMO_PROBE_EVIDENCE:?absolute new directory below user cache}"
systemd-run --user --wait --collect --unit="lain-mimo-startup-$(date -u +%Y%m%dT%H%M%SZ)" \
  -p MemoryHigh=1536M -p MemoryMax=2G -p MemorySwapMax=256M -p CPUQuota=150% \
  -p RuntimeMaxSec=125s -p TimeoutStopSec=5s -p KillMode=control-group \
  --setenv=LAIN_MIMO_PROBE_SLOT=1 \
  --setenv="LAIN_MIMO_PROBE_HOST_SHA256=$LAIN_MIMO_PROBE_HOST_SHA256" \
  --setenv="LAIN_MIMO_PROBE_SOURCE_COMMIT=$LAIN_MIMO_PROBE_SOURCE_COMMIT" \
  /usr/bin/nice -n 10 /usr/bin/ionice -c3 /usr/bin/bash \
  /home/tomka/.cache/lain-worktrees/next-20261025/n03-mimo/tests/mimo-real-probe/run-startup-private.sh \
  "$LAIN_MIMO_PROBE_DLL" "$LAIN_MIMO_PROBE_EVIDENCE"
```

Run from the original user environment, outside any earlier private UI runner;
never assign a synthetic HOME before this command. The inner probe is limited to
90 seconds, namespace supervisor to 115 seconds, and unit to 125 seconds plus its
5-second kill grace. Keep `inputs.sha256`, `source-commit.txt`, `exit-code.txt`,
`supervisor.log` and `probe-evidence/`. The wrapper deletes only its newly created
private home afterward; PID-namespace exit and the unit kill all owned descendants.
No real attempt with this wrapper has yet been run.

## Build handoff: assigned serial slot only

`/usr/bin/chromium` was found by read-only inventory. Existing Main Host DLLs
carried informational commit strings `c273a1d` / `a66cb2a`; neither is a proven
exact `6d9af6a` build. Do not silently reuse them. Default to a bounded source
build, using the existing locked production dependencies:

```sh
dotnet restore tests/mimo-real-probe/MiMo.RealProbe.csproj --locked-mode -r linux-x64 --disable-parallel -p:BuildInParallel=false
dotnet build tests/mimo-real-probe/MiMo.RealProbe.csproj --no-restore -r linux-x64 -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -nr:false
```

No global `RestorePackagesWithLockFile=false`: only the new harness disables its
own lockfile. If a proven matching Host/Core artifact becomes available first,
build with `-p:MiMoProbeHostDirectory=/absolute/qualified/host/directory` for
direct assembly references, skipping the production ProjectReference. Record
its source provenance and both DLL hashes. Set the SHA below to the **loaded
production Host DLL** beside the built probe; the harness refuses a mismatch.

The integrator launches the build/probe inside the same transient user unit,
with `MemoryHigh=1536M`, `MemoryMax=2G`, `MemorySwapMax=256M`, `CPUQuota=150%`,
`nice -n 10 ionice -c2 -n7`, `DOTNET_PROCESSOR_COUNT=1` and a dedicated SSD TMPDIR.
Example runtime command, after successful reviewed build:

```sh
export LAIN_MIMO_PROBE_SLOT=1
export LAIN_MIMO_PROBE_HOST_SHA256=REPLACE_WITH_64_HEX_SHA256
bash tests/mimo-real-probe/run-private.sh --real-mimo-authgate \
  /absolute/MiMo.RealProbe.dll /absolute/new-evidence-directory
```

Keep `evidence.json`, logs, actual loaded assembly hashes, screenshot hash,
resource-unit result, relevant WIP and a short manual screenshot finding.
`ownedProfileErased` records production cleanup; the runner additionally removes
only its new temporary state and owned browser process after abnormal exit.
No user display, browser profile, lain account/configuration or secret is an input.

## Necessary user action for later authenticated qualification

This private unattended probe intentionally stops before sign-in. A later,
separately authorized interactive production connection requires the user to
complete Xiaomi sign-in/MFA in a newly opened, lain-owned console window, then
explicitly associate the discovered UID/region. Production must verify supported
CN/EU identity and usable Individual Token Plan usage before publishing monitoring.
The current probe neither requests credentials nor leaves a sign-in session open.
SGP, Team and PAYG remain outside this qualification.

## Executed attempt, 25 October 2026

[Evidence](public-probe-evidence.json) records the bounded run against production
Host source `9d4f708` (Host/Core inputs unchanged in the integrated candidate).
Reference-only harness restore/build passed with zero warnings/errors. Production
`OpenAsync` then returned a sanitized start/read rejection in less than a second:
no completed browser opening, API reads, screenshot or visible auth gate was
observed. The owned profile was erased and all owned processes stopped.

The underlying startup cause remains unknown: production discards browser stderr,
and source/runner review found no conclusive environment error. This is an open
browser-start diagnosis, **not successful MiMo login or quota qualification**.
No sign-in, existing cookies, account mutation or inference occurred. The initial
offline restore-cache failure is retained separately in the evidence. Further
authenticated qualification still requires the user action described above.
