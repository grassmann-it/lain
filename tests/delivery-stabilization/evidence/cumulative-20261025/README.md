# Cumulative installed delivery, 25 October 2026

Application source: `5f41fba5324e1cf1e58e2229d6c35c90ba800846`.
Core: `e05d21027a9aa0c543430db8bee6fd8c34d5a9fff78ea81f80953e8f29cf485e`.

Unmodified reports retain synthetic package/update/restore qualification and the
six-case UI index with its separate manual review. The installed-UI summary
references private actual-state evidence and screenshots; those payloads are not
committed. The separate read-only reconciliation verifies resulting installed
state, preserving the original failed live-update record and its qualification
boundary. It does not reclassify that failed execution as a passing updater run.

The Inspector count review preserves the original 106-count operator failure;
the final source requires 107 checks because one retention assertion became two.
No application assertion was removed. Full Core evidence is retained separately
under `tests/core-stabilization/evidence/cumulative-20261025`.

See `docs/cumulative-implementation-2026-10-25.md` for scope and remaining provider
qualification limits. `SHA256SUMS` covers every other file in this directory.
