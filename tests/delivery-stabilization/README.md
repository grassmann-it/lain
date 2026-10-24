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

Execution creates its own `/tmp/lain-release-qualification-*` state, prefix,
systemd-file directory and XDG paths. It never calls systemctl, touches a real
service or uses the normal user config/Codex/credentials. Host/Core use dynamically
chosen guarded reference ports and temporary LAIN_HOME. No request is sent to
18417/18418. The installed CLI uses the same guarded discovery override; its
launcher must restore the selected private home when the environment omits it.
Only direct child processes created by this probe can be terminated.

Cases: fresh actual Monitor install/start/restart/CLI status without a Core/listener;
Monitor-to-Gateway package update keeps Monitor intent/Core stopped;
actual baseline Gateway package updated to a different qualified Core while
retaining desired11/applied2, explicit unknown legacy profile, historical81986
tokens and cursor2; full stopped current-package backup/restore with legacy owned
refreshgrant quarantine, static account/history access and no Core startup. A
synthetic v1 SQLite fixture exercises actual Host migration. It is not a copy of
user data. Background provider quota reads are disabled in the guarded probe;
there is no inference or paid fallback.

The baseline old Host is **never started**. A current stopped snapshot restores
the matching marker-aware application and data. Historical old-binary rollback
runtime remains `not_tested` until that Host's schema/marker safety is established;
`app.previous` alone is not such a guarantee. Existing filesystem rollback,
corruption/no-overwrite checks remain in `test_delivery.py`. Actual UI desktop
execution belongs to private-Xvfb acceptance with the exact package assembly;
this new probe checks installed Host/CLI/Core, not desktop interactions.

Time budget after exact artifacts exist: allow 1–3 minutes for the probe (five
bounded Host starts including restored state); reserve up to 5 minutes for
readiness/failure investigation. Fresh sequential Monitor/Gateway publishes
normally need a separate 5–12 minute warm-cache slot; cold restore/source downloads
can take longer. These are planning allowances, not measurements.

Execution now fails closed unless it is in a distinct loopback-only network
namespace. The integrator must create the namespace and enable `lo` before
passing `--execute`; do not add a new PID namespace for this guard. Upstream Core
performs incidental startup metadata requests, so disabled quota polling alone
does not establish network isolation. No fallback to the user's network is allowed.

Previous `413588c` Host rejects SQLite user_version4 after current migration004.
An app-only runtime rollback is therefore concretely blocked even with current
credentials retained. See
[`delivery-bounded-rollback.md`](../../docs/account-connect-qualification/delivery-bounded-rollback.md)
for the source evidence and the separate, unqualified stopped-conversion
candidate. The actual-package report records this blocker instead of claiming
old-binary rollback succeeded.
