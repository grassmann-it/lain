# Cumulative v4 delivery from an explicit baseline

The installed baseline for the next cumulative update is
`d7200d7d700bd2d23abf8bf028d40526eb235bf7`. The final candidate source must be
frozen separately after all coordinated changes are committed. This document
and source pin tests do not establish a built or installed candidate.

`tests/delivery-stabilization/v4_update.py` requires both
`--previous-source-commit` and `--new-source-commit`, each a full lowercase
40-character commit. There is no implicit 793822c fallback. Both previous and new
Monitor/Gateway packages must match their respective source pin in build-info,
package metadata and all three application components. Each complete package
manifest and Gateway Core hash remains independently pinned and checked. Both
runtime modes must have identical application component identities within a
source. Exact Core reuse retains the existing source/qualification identity
checks; a changed Core requires its own passing exact-binary/source qualification.

The resulting plan/report carries `sourceCommits.previous` and
`sourceCommits.new` alongside the four package records. Live updater preparation
must compare this complete plan with the hashed passing executed report; an old
report without explicit source pins cannot qualify this new update.

For the d720 baseline, pass:

```sh
--previous-source-commit d7200d7d700bd2d23abf8bf028d40526eb235bf7
```

The existing retained baseline packages are
`artifacts/lain-linux-x64-monitor-quota-state-20261025` and
`artifacts/lain-linux-x64-gateway-quota-state-20261025`. Historical recorded
SHA256SUMS pins are respectively
`58797665e1295f5296690f407b8dc45fd9691e2cff45edd3b821f5d76a4cf93d` and
`ced56fffeecc3e29dcdeb02ae503ec71e0b31ee5234adce0e93afff2db42435c`;
the baseline Core is
`39c09202ca7c38c636cfb949abec3b71c6c9843dd2651cbc8bf44944c13c0629`.
Recheck actual package inventories before use. These are historical baseline
identities, never candidate qualification.

Run the plan-only fixture first. Root owns the later serialized build/runtime
slot. Build with .NET 10.0.111 and Go 1.27.2, locked dependencies, a fresh SSD
TMPDIR and bounded concurrency/memory. Rebuild deliberately cleaned harness
outputs instead of depending on historical bin/obj paths. Qualify changed
Core bytes, publish both modes, and run the exact four-package fixture in a
distinct loopback-only network namespace with its trusted pre-unshare parent
identity. No fallback to the user's network is permitted. Test exact package UI
assemblies in private Xvfb/private D-Bus with fresh evidence paths, preserving
the user's windows and drafts.

The runtime fixture still tests synthetic desired 19/applied 2 state and an unknown
legacy profile. It starts the explicit previous package, snapshots all stopped
state including app.previous, archives that earlier application, updates, returns
application bytes to the pinned previous package while preserving newer synthetic
grants, and updates forward again. It does not prove arbitrary schema4 downgrade,
real OAuth rotation, current real 35/35 state, or recovery using stale grants.

For the eventual live update, the separately reviewed private helper must require
the explicit d720 baseline, matching installed package and passing four-package
report, a shared exclusive updater lease, exact loaded own-unit identity,
no conflicting UI/writers, fresh full stopped snapshot and external integration
backup, and pre-install rechecks. Preserve actual revisions, active identities,
fixed-range History with nullable tokens, runtime/config/journal/source and latest
selected grants. Never Apply, rewrite ordinary Codex configuration, or touch the
other proxy. If the user's window is open, defer the update without closing it.
Failures leave the own service stopped and retain latest state; no automatic
application, schema or credential rollback is authorized by this fixture.

`python3 tests/delivery-stabilization/test_v4_package_pins.py` uses only disposable
synthetic package files and plan-only CLI calls. It never runs payloads, installers,
services, UI, providers or builds and does not access installed user state.

## Additional private updater guards

The prepared cumulative helper additionally rejects loaded `LAIN_PROXY_PATH`
values other than its exact own `app/proxy/lain-proxy` path or no assignment,
any loaded `LAIN_REFERENCE_` assignment, and nonempty or unavailable
`ExecStartPre`, `ExecStartPost`, `ExecStop` or `ExecStopPost` metadata. Cleaning
the updater child's environment alone does not validate a systemd service.
The complete installed old-app package is checked again immediately before the
installer, after the potentially long stopped snapshot and previous-app archive.
Failed child diagnostics persist only a bounded command basename and exit code;
arbitrary stdout/stderr and command arguments are excluded.

`test_updater_guards.py` and its JSON fixture check these bounded guards from an
explicitly hashed private source using AST-selected pure code, fake child results
and disposable files. They never import or start the helper's full module or
invoke actual systemd, services, installer, UI, credentials or provider operations.
An independent reviewer must still assess the exact private helper hash.

These changes do not add a drain/activity check before stopping the own service.
Stable history/journal reads do not prove an empty admitted-request set. Root's
controlled update slot must establish any required quiet state separately; this
guard delta makes no uninterrupted in-flight-stream guarantee.
