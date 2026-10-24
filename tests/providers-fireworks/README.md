# Fireworks local fixtures

Run `tests/providers-fireworks/run.sh` only in a Main-granted build/test slot.
It creates a temporary .NET project under `${TMPDIR:-/tmp}` and removes its
build output afterward. Prefer a disk-backed `artifacts/tmp` TMPDIR.
All HTTP goes through an in-memory `HttpMessageHandler`; no provider, GPU,
credential store, inference endpoint, or purchased budget is contacted.

Coverage includes:

- Fixed official GET endpoint, explicit provider account scope and bearer auth.
- Separate monitor key selection, missing/expired key without inference fallback.
- Null/missing/zero quantities, enforced versus approved limits, unknown units.
- `updateTime` preserved only as provider update metadata; no reset fabricated.
- 401/403/429, pagination escaping/loops, mismatch rejection and sanitized output.
- Host freshness, account isolation, last-good retention and caller cancellation.
- Manifest parsing and conservative billing/free-only capability declarations.

This suite verifies quota HTTP boundaries and Host projection, not just parsers.
Shared inference transport tests belong to Main integration. Live provider access
remains untested independently of fixture success; see
[`docs/providers/fireworks.md`](../../docs/providers/fireworks.md).

Local result on 2026-10-24: 49 assertions passed; build reported 0 warnings and
0 errors. Installed/live verification was not performed.
