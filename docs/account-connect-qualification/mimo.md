# MiMo console monitoring implementation, 2026-10-25

Source base `9b1cd4b`. Implements an explicitly opened, isolated Chromium console
session for **Individual Token Plan CN/EU**. The previous source-only analysis in
[console-connections.md](console-connections.md) describes the earlier state.
This implementation is not OAuth: the existing inference API key stays intact,
and the monitor uses a separately owned browser session. It does not create,
reveal or rotate a provider key and does not perform inference.

## Connect and read contract

Register one `MiMoConsoleBrowser(OwnStatePaths)` singleton as
`IMiMoConsoleBrowser`; share it between `MiMoConsoleLoginAdapter` and
`MiMoQuotaAdapter(browser, secrets, configuration)`. Register the login adapter
in the existing `LoginOperationService` and quota adapter in `QuotaService`.
The connection method is `xiaomi-mimo` / `console-browser`, purposes `[quota]`.
Runtime availability means a supported installed Chromium executable plus a
Linux desktop environment; it does not prove provider sign-in or plan access.
No browser is started by service startup, ordinary reads, or a quota timer.

1. Connect starts a visible browser with a new random lain-only profile under
   private `credentials/console/mimo/`. No personal browser profile, cookies,
   password store, extension or current tab is read. Password saving and sync
   are disabled in that profile. The user completes Xiaomi sign-in/MFA there.
2. Status reads `GET /api/v1/userProfile`. Numeric idc 310/CN and 314/EU plus a
   nonconflicting provider UID must be established. An unbound account returns
   the observed identity as a resource selection for explicit association.
   This verifies the console identity; it does not prove the owner of a legacy
   inference key whose private metadata never established a Xiaomi UID.
3. Before publication, `GET /api/v1/tokenPlan/usage` must yield usable readings.
   The shared account lifecycle publishes one immutable private quota generation
   with owner `console-browser` and opaque profile reference, retaining the
   account ID, inference capability, routes, profiles and existing API key.
   Monitor-only accounts need no API key, gateway route or Core.
4. Every subsequent read checks the current selected quota handle, account
   generation, expiry and retirement before reading, re-verifies console UID
   and region, and rechecks the handle/generation before returning readings.
   `QuotaService` additionally checks account revision before publication.
   Identity mismatch, cancelled connect, new generation and disconnect cannot
   publish a late result. No failed session falls back to the inference key.
5. The only browser operations are the existing named GETs: identity,
   Individual usage and Individual detail, fixed at
   `https://platform.xiaomimimo.com`. Optional detail adds `currentPeriodEnd`
   only as `WindowEnd`, never as reset/refill. Detail failure retains usable
   measured usage. All weighted-credit buckets remain separate; money, raw
   tokens and annual/monthly pools are never substituted for one another.

## Browser boundary and lifecycle

The host launches only a known installed Chromium/Chrome executable with its
own profile and ephemeral debugging port bound to `127.0.0.1`. The local endpoint
is private implementation state, absent from public DTOs. The host connects to
one page with the exact console origin; it rejects other WebSocket hosts/ports,
ambiguous console tabs and HTTP redirects. Fixed script source performs only
the selected whitelisted same-origin GET, preserving browser Cookie, Origin,
CSRF and SameSite behavior. There is no arbitrary script, protocol URL, fetch,
cookie export or borrowed client identity hook.

Read deadlines are 15 seconds. Provider body size is capped at 256 KiB while
streaming, JSON depth at 24, local target discovery at 1 MiB, and CDP message
size at 2 MiB with at most 64 messages. Browser output is drained without logging.
The profile parent is mode 0700; host-written metadata is mode 0600. A private
PID/start-time receipt permits explicit disconnect to close its owned process
after a host restart without killing a reused PID.

Cancellation closes and deletes the unpublished owned profile. Successful
connect keeps its browser alive independently of closing the lain UI. A closed
browser, lost session or host restart requires explicit monitor reconnect;
quota polling never silently reopens a browser. Normal host disposal closes
owned browsers. Local disconnect must first invalidate/remove the capability,
then invoke `MiMoQuotaAdapter.DisconnectAsync(previousAccount)` to close its
browser and delete its own profile. This is local cleanup and makes no claim
of provider logout/revocation. A successful reconnect can similarly clean up
its previous detached quota generation/profile after publication.

## Qualification limits and checks

The Linux browser/IdP/MFA login and authenticated real account read have **not**
been run in this implementation task. The source-backed implementation is a
bounded usable connection attempt, not evidence that every Xiaomi login or
role works. The provider's private console API can change. No OAuth client,
refresh token, permanent session or provider authorization for a new public
third-party API is asserted. Errors retain prior quota observations and request
monitor reconnect separately from inference repair.

SGP stays fail-closed because the website's default region is not an explicit
identity mapping. PAYG balance and Team project/member/seat scopes are not
enabled. Team's older pure fixture normalizer does not authorize a Team read.

Run in root's serialized build slot:

```sh
dotnet run --project tests/console-connections/ConsoleConnections.Tests.csproj
```

The synthetic fake browser exercises the actual login operation lifecycle,
observed resource selection, attach to existing account preserving its API key,
monitor-only creation, cancellation, wrong UID, session expiry, optional-detail
failure, weighted-credit/validity distinction and public-data redaction. Existing
schema/nonce/origin/replay/payload-bound tests remain. No real credentials,
provider login, personal browser state or provider requests are used by tests.

Local validation on 2026-10-25: pinned `linux-x64` / `SelfContained=true`
locked restore and the fixture harness both passed; no lockfile drift. The run
used CPUQuota=150%, MemoryHigh=1536M, MemoryMax=2G, MemorySwapMax=256M,
IOWeight=10, nice 15 and idle I/O, with the assigned private qualification
TMPDIR. Both existing boundary/schema fixtures and the new fake-browser
account/session lifecycle fixtures passed. This did not launch Chromium or
contact a provider, and does not qualify a real login or provider entitlement.
