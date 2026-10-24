# Monitor credential isolation tests

Run `tests/monitor-auth/run.sh` in the Main-assigned build slot. All credentials
are synthetic and all state is under a temporary directory. No real provider,
login, refresh, revocation or inference request is performed.

The suite covers secret-free status, existing account identity, separate monitor
storage surviving inference credential replacement, revision conflicts, provider
and resource boundaries, snapshot expiry, malformed credential data, symlinks,
private file modes and local removal.

Source/auth decisions: [monitor-auth.md](../../docs/providers/monitor-auth.md).

Local result, 24 October 2026: **58 checks passed**, zero build warnings/errors.
The coordinated run used `TMPDIR` under the worktree `artifacts/tmp`; the runner
honors that directory for its temporary project and synthetic state.
