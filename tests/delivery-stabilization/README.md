# Delivery stabilization checks

Run `python3 tests/delivery-stabilization/test_delivery.py` from a checkout.
It uses temporary synthetic packages, the real installer and maintenance helper;
it starts no service, builds no project and makes no network request.

The checks exercise Monitor/Gateway metadata, fresh selection, retained data and
mode during upgrade, refusal of Core removal, checksum coverage/traversal,
retained `app.previous`, stopped snapshot/restore, no overwrite, corruption and
symlink rejection. Embedded publish Python blocks are syntax-checked. Actual
publish/build, Host behavior and safe generation recovery require integrator
qualification; see `docs/linux-delivery-stabilization.md`.

## Actual packages (new qualification, not_run)

`actual_packages.py` reuses `tests/acceptance/upgrade_probe.py` package pins,
complete fingerprints and redirect refusal, the shipped installer, and the
shipped full-home maintenance helper. It does not call the old real-service
upgrade/rollback path. Package arguments and reviewed full SHA-256 pins are
mandatory; there are no defaults pointing at user installations.

```sh
python3 tests/delivery-stabilization/actual_packages.py \
  --monitor-package /absolute/new-monitor \
  --monitor-manifest-sha256 MONITOR_SHA256SUMS_HASH \
  --gateway-package /absolute/new-gateway \
  --gateway-manifest-sha256 GATEWAY_SHA256SUMS_HASH \
  --gateway-core-sha256 NEW_CORE_HASH \
  --previous-gateway /absolute/reviewed-old-package \
  --previous-manifest-sha256 OLD_SHA256SUMS_HASH \
  --previous-core-sha256 OLD_CORE_HASH \
  --report /absolute/new-plan.json
```

Default execution only validates packages and writes `status: not_run` with a
plan; it installs/starts nothing. In the explicitly allocated runtime slot use
the same reviewed inputs, **add `--execute` and choose a new report path**. Both
Gateway packages need their exact existing backend qualification; this harness
never creates or alters qualification metadata. The new Core hash must differ
from the baseline Core hash. A Monitor package must declare its mode and contain
no Core directory, binary, source provenance or Go notices.

Execution creates its own `${TMPDIR:-/tmp}/lain-release-qualification-*` state, prefix,
systemd-file directory and XDG paths. It never calls systemctl, touches a real
service or uses the normal user config/Codex/credentials. Host/Core use dynamically
chosen guarded reference ports and temporary LAIN_HOME. The legacy rollback
fixture uses fixed 18417/18418 exclusively in the separate loopback-only network
namespace. The installed CLI uses the same guarded discovery override; its
launcher must restore the selected private home when the environment omits it.
Only direct child processes created by this probe can be terminated.

Cases: fresh actual Monitor install/start/restart/CLI status without a Core/listener;
Monitor-to-Gateway package update keeps Monitor intent/Core stopped;
actual baseline Gateway package updated to a different qualified Core while
retaining desired11/applied2, explicit unknown legacy profile, historical81986
tokens and cursor2; full stopped current-package backup/restore with legacy owned
refreshgrant quarantine, static account/history access and no Core startup. A
synthetic v3 SQLite checkpoint fixture exercises actual Host migration. It is not a copy of
user data. Background provider quota reads are disabled in the guarded probe;
there is no inference or paid fallback.

The pinned old Host is started only in the explicit staged conversion fixture
inside the isolated loopback namespace. A separate current stopped snapshot
restores the matching marker-aware application/data and tests rotating-grant
quarantine. `app.previous` alone cannot establish schema safety. Filesystem
corruption/no-overwrite checks remain in `test_delivery.py`. Desktop execution
belongs to private-Xvfb acceptance with the exact package assembly.

Time budget after exact artifacts exist: allow 1–3 minutes for the probe (seven
bounded Host starts including restored state); reserve up to 5 minutes for
readiness/failure investigation. Fresh sequential Monitor/Gateway publishes
normally need a separate 5–12 minute warm-cache slot; cold restore/source downloads
can take longer. These are planning allowances, not measurements.

Execution now fails closed unless it is in a distinct loopback-only network
namespace. The trusted launcher captures its pre-unshare `/proc/self/ns/net`
identity and passes `--parent-netns net:[digits]`, creates the namespace and
enables `lo` before passing `--execute`; do not add a new PID namespace for this
guard. Without an explicit anchor, process1's readable namespace is required. Upstream Core
performs incidental startup metadata requests, so disabled quota polling alone
does not establish network isolation. No fallback to the user's network is allowed.

Previous `413588c` Host rejects SQLite user_version4 after current migration004.
An app-only runtime rollback is therefore concretely blocked even with current
credentials retained. See
[`delivery-bounded-rollback.md`](../../docs/account-connect-qualification/delivery-bounded-rollback.md)
for source evidence and the bounded stopped conversion. A plan-only report
records not_run; only a fully successful executed fixture reports bounded
staged conversion success.

### Bounded historical rollback

The actual-package probe now includes genuine closed v3 checkpoint → current
schema4 upgrade → helper-prepared schema3 old application → schema4 forward
upgrade. It checks desired11/applied2, history81986/cursor2, accounts, retained
current synthetic grants and runtime bytes. It runs only with loopback network
namespace isolation and fixed18418/18417 for the legacy fixture. This source
stage remains **not_run** until root executes it against pinned shipped packages.
App-only rollback of schema4 remains unsupported. See
`docs/account-connect-qualification/delivery-bounded-rollback.md` for boundaries.

Source helper checks (synthetic SQLite and nonexecutable fake package only):
`python3 tests/delivery-stabilization/test_legacy_rollback.py`. They never launch
Host/Core or contact providers.
