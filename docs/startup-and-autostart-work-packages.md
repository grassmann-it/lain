# Startup and per-user lifecycle — proposed work packages

Review: 27 October 2026. The user wants opening Lain to check and start its
background service when needed, plus reliable login startup on Linux, macOS and
Windows. This is an architecture proposal, not an implementation or installation
receipt. The user confirmed background-service startup at sign-in as the setup
default, independently configurable from startup when opening Lain.
Implementation has not been commissioned by this review.

## Current evidence

Source reviewed: local HEAD `fef478f` plus the existing working tree. Many apparent
changes already match the cached publication branch; see the
[WIP review](wip-integration-review-2026-10-27.md). Source is not the installed
runtime, which still reports `ff5771c`.

A read-only check on 27 October at approximately 19:02 UTC found Linux
`lain.service` enabled and active since 15:34:31 CET, with zero manager restarts,
and authenticated Lain status reporting Gateway with `proxyReady: true`.
Saved/applied revisions were **58/57**. This is a point-in-time observation,
not permission to apply the pending revision or restart the service.

The earlier incident was different: the Host was active, but Core startup had
exceeded its 20-second readiness deadline. A targeted Core start then succeeded;
the original cause remains unknown. See the
[cold-start recovery record](model-switch-thinking-replay-2026-10-26.md#lain-nach-kaltstart-wiederhergestellt).
An inactive systemd service has not been established as the cause of that event.

| Area | Implemented behavior | Gap |
| --- | --- | --- |
| Linux login | Installer writes a systemd user unit; enabling it is separate. The unit restarts a failed Host. | No integrated startup preference or full readiness result. Host survival does not repair a failed Core. |
| UI opening | Linux wrapper, CLI and direct Desktop entry open/reopen the window. | None ensures the Host is running. Refresh reports an unavailable Host. |
| Service CLI | Start/stop/restart invoke `systemctl --user`; status queries the Host API. | No macOS/Windows adapter; status cannot explain an absent manager registration or disabled autostart. |
| macOS | Installer renders an App bundle, CLI wrapper and LaunchAgent. | Native payload/lifecycle acceptance and CLI service adapter remain open. |
| Windows | Installer renders CLI/Desktop launchers and a login Startup CMD. | Visible Host console, no lifecycle adapter, native payload/Core portability and acceptance remain open. |

Relevant code: [CLI](../src/Lain.Cli/CliApplication.cs),
[Linux wrapper](../packaging/lain), [unit](../packaging/lain.service.in),
[Desktop entry](../src/Lain.Desktop/Program.cs),
[Host startup/status](../src/Lain.Host/Program.cs),
[Core lifecycle](../src/Lain.Host/Operations/OwnedProxyLifecycle.cs),
[platform installer](../packaging/platform-installers/install.py).

The existing API client reads its private Host token lazily. A missing token
causes a failed request until first Host startup, not a constructor crash.
The bootstrap must retry discovery after starting the Host.

## Recommended behavior

Keep the existing ownership boundary: the OS manages the per-user Host; only
the Host manages the Core. Closing the window keeps background work running.
Monitor needs the Host, not an inference Core. No automatic systemd lingering.

Opening or reopening Lain should show the window immediately and run one
coordinated, asynchronous startup operation:

1. Resolve the owned installation and inspect its lifecycle/maintenance state.
2. Accept an already authenticated, compatible Host without restarting it.
3. If stopped, start its registered user service and wait for authenticated
   Host readiness, with a bounded deadline and progress/error feedback.
4. In Monitor, finish when monitoring is available. In Gateway, inspect Core and
   applied-runtime state separately. No applied profile means setup is needed.
5. Resume only a validated, completed **applied** runtime. A newer saved revision
   stays pending. Never Apply, adopt credentials, convert login-only state or
   clear application/drain/credential recovery as a startup side effect.

Place this behavior in a shared lifecycle component called by Desktop itself,
including reopening an existing UI instance. A CLI-only fix would miss the
Linux wrapper and direct GUI launchers. Use installation metadata rather than
assuming binaries live beneath `LAIN_HOME/app`; the macOS/Windows installer
separates data from versioned binaries, and Windows uses `.exe` names.

Manager operations must work while the Host is absent. Suggested operations are
inspect, ensure/start, stop, restart and set-login-autostart. Inspect reports
installation, manager registration, login enablement, Host and Core separately;
an open port or a successful manager command does not establish readiness.
Do not launch a detached second Host when the manager is unavailable.

Startup must coordinate atomically with installation/update/recovery. Current
updater and platform installer locks do not provide this shared protocol.
Checking for a lock file and then starting is insufficient. An active maintenance
operation must prevent UI/Host/Core writers from reopening state during a swap.
Do not remove apparently stale locks automatically.

Manual Stop must remain meaningful. Current Core status has no persisted desired
state, so `stopped` alone cannot distinguish an explicit stop from a failure.
Define that intent before adding automatic Core recovery; periodic UI refresh
must never undo Stop. Recommended rule: Stop inhibits automatic Core restart
until an explicit Start or separately defined resume action. A transient start
failure may support a bounded retry only after its classification is qualified;
recovery-required and integrity/configuration failures remain explicit.

## Settings and platform direction

- **Start background service when opening Lain:** enabled for the requested
  opening behavior, independently configurable in Settings.
- **Start background service at sign-in:** enabled by default during setup,
  confirmed by the user on 27 October 2026. Independently configurable in
  Settings. Disabling future login startup does not stop the current Host.
  Starting on demand does not implicitly enable login startup.
- Existing minimize/close-to-tray preferences remain independent. Login startup
  refers to the background service; automatically opening a window is separate.

| Platform | Recommended integration |
| --- | --- |
| Linux | systemd user service; separate start/stop and enable/disable; no linger. Explain missing user bus or unsupported service manager. |
| macOS | Per-user LaunchAgent with distinct on-demand and login-enabled behavior, plus launchctl lifecycle adapter. |
| Windows | Per-user scheduled task with on-demand/logon triggers and a genuinely windowless Host launch, bounded failure recovery and graceful stop. No administrator/LocalSystem requirement. |

The platform mechanisms remain proposals. Windows Task Scheduler is a
recommendation, replacing the current visible
Startup CMD approach if accepted. Native testing must establish its console,
termination, child ownership and login behavior. Crossbuilds and the existing
[contract CI](platform-installer-ci.md) do not prove these properties.

## Proposed implementation slices

| Package | Deliverable and dependencies | Acceptance |
| --- | --- | --- |
| START-01 — Linux lifecycle | Shared lifecycle/discovery and status contract, Linux adapter, absent-Host diagnostics, coordination with maintenance. | Stopped or already-running Host handled idempotently; concurrent start and maintenance races cannot create duplicate/state-writing processes; no foreign process adopted or killed. |
| START-02 — Desktop and setup | Host bootstrap depends on START-01; automatic Core resume also requires START-03's Stop-intent/recovery contract. Async ensure on direct launch, shortcut, CLI and reopen; progress/retry/error UI; independent startup settings. | First launch without a token reaches authenticated readiness; healthy streams survive repeat opens; Monitor stays Core-free; pending revisions, explicit Stop and recovery remain intact. |
| START-03 — Core cold-start reliability | Can investigate alongside START-01. Capture bounded redacted health stage/reason before cleanup; define Stop intent and qualify recovery policy. | Reproduce under resource limits, identify the actual unmet health condition, verify safe recovery and unexpected Core exit handling. Merely increasing the 20-second timeout is not diagnosis. |
| START-04 — Native platform adapters | Shares START-01 contract. macOS LaunchAgent/paths and Windows windowless task/paths; Windows journal locking, secure opening and filesystem-query portability. | Real Host/CLI/Desktop and Core where offered, running in ordinary native user accounts. Native dependencies and signing/notarization strategy are documented. |
| START-05 — Final package acceptance | Depends on the implemented slices for each offered platform. Bind evidence to exact 0.8.0 payloads. | Fresh install → UI → ready; sign-out/sign-in/reboot with startup on/off; close UI while service remains usable; crash/recovery; occupied ports; missing payload; update/uninstall preserve data and startup choice. |

Additional failure coverage: missing/modified service registration, unavailable
manager/user bus, wrong token or installation, missing applied profile, login-only
runtime, restore recovery, interrupted Apply, stopped Core with manual intent,
and repeated/concurrent opens. Native Windows checks include no extra console,
redirected Desktop and graceful stop; macOS includes Gatekeeper and login-agent
behavior. Tests remain isolated, with no real provider requests needed for
lifecycle acceptance.

Recommendation: complete START-01 through START-03 and Linux acceptance before
the Linux 0.8.0 download. START-04/05 gate each later native platform offer;
they need not block Linux. Existing release work remains source consolidation,
fresh package qualification, optional client qualification, local update and
real model acceptance; see [0.8.0 remaining work](release-0.8.0.md#remaining-work--review-on-27-october-2026).

This review changed documentation only. No configuration, service, profile,
credential, package, Git commit or publication was changed.
