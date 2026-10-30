# REC-08 · Independent qualification, integration and delivery

Owner: independent qualification worker; integrator owns release/package mutation.
Dependencies: [REC-00](00-contracts.md) for criteria; integrated01–07 for completion.
Status: plan only. No recorder build/test/deployment/real capture in this task.

## Outcome and ownership

Prove what the exact recorder build observes and preserves, with bounded failure
behavior, usable saved conversations and a reversible disabled-first delivery.
Own future `tests/recording-acceptance/**`, fixture/benchmark runners and evidence
reports. Implementation defects return to owning package; no concurrent fixes to
other owners' source, SQL migrations or shared contracts. Existing build/package
scripts, Core provenance and actual installation remain integrator-owned.

## Independent test plan

1. Freeze exact installed baseline vs proposed disabled/on/faulted artifacts.
   Compare source/runtime pins; reject incidental Core patch4->5 routing work or
   pending revision Apply. Test plan coverage isn't source/runtime evidence.
2. Capture-point correctness: client ingress vs normalized/handler output vs final
   provider request/response vs actual client egress. Prove each claimed boundary;
   missing hooks produce explicit gaps. Static SDK hook reload/detector semantics,
   nil header pass-through, no per-chunk request/history copies and no mutation
   fields are mandatory negative cases.
3. Response/auth/routing parity per claimed protocol: nonstream/SSE, exact payload
   bytes/order, status/headers, flush/cancel/error, account selection, retries,
   terminal suffix/Usage durability. Short write, flush error, post-hook validation,
   keepalive and disconnect after observed terminal must not be called successful
   client delivery without evidence. WS/Gemini-native unsupported cells remain so.
4. Shared synthetic graph200 model calls/100 tool events, >=3 turns, model switch,
   full-history resend, fork, root+two workers, same-name parallel tools and partial
   arguments/results, retries, compaction/resume, missing/conflicting identity.
   No local execution claims unless clearly fake harness evidence is labeled
   synthetic. A successful parser does not prove a real client's coverage.
5. Queue bytes/count/active-parser limits, stopped reader, slow writer, own archive
   quota, filesystem-full boundary, corrupt/torn records, process kill during each
   commit/index step and duplicate replay. Known committed records survive;
   uncertain loss is explicit. Rebuild/delete/read generation races cannot replay
   deleted content or join different sessions. Preserve old Usage-fault fail-close.
6. Canary secrets absent from archive/index/temp/normal logs/errors; malformed or
   injection-bearing tool/web data never changes consent/rights policy. Existing
   error-only logging must be checked as a separate path: if baseline itself
   writes secrets, report the blocker and request a minimal independent fix;
   don't hide failures or import old error logs into the archive.
7. Private OS0700/0600, symlink/path/source access, unauthorized payload reference,
   retention/deletion/reference rules, local backup/restore and old-schema reader
   behavior. No encryption/keyring dependency or training-rights gate for ordinary
   local views; no cloud/remote backup configured automatically.
8. UI uses the real integrated view under private Xvfb/D-Bus; long contiguous output
   and many small tool/worker cards, narrow/wide/focus/copy/follow/pagination/reopen
   and unavailable-source cases. Prior Inspector benchmark numbers do not prove
   archive behavior.
9. Controlled repeatable local fake load at1/4/8 concurrent calls, same payloads/
   upstream delay. Record warm/cold p50/p95/p99 TTFT and inter-frame gaps,
   throughput, CPU/RSS, queue bytes/peaks, disk bytes/write amplification and
   repeated-history space. Report baseline noise and failures, not only averages.
   Numbers in the main plan are proposed budgets until recorded in REC-00.

## Delivery gates

- Publish from frozen owned source/dependencies with exact provenance, all package
  checksums and supported representation/protocol/client cells. No module-cache
  edits or undocumented SDK patches; no customer data in artifacts/evidence.
- Fresh isolated installation/migration/restart/rollback with synthetic data,
  recording off by default. Compare unrelated Host/CLI/Core/Desktop/launcher/
  service/private-state changes; only intended components may differ.
- Data-reader compatibility and removal/disable rollback explicit. Disabling
  producer/pending capture doesn't stop inference or delete committed archive;
  rollback never clears Usage, accounts, profiles or credentials.
- Report source tests, synthetic runtime, native UI, packaged artifact, installed
  and real-provider evidence separately. Actual real capture/inference requires
  the intended operational action; no extra paid calls or legacy-log import.

## Completion receipt

Return source/artifact pins, test settings/results, benchmark data, coverage,
remaining unsupported cells, migration/config/rollback guide and redacted failure
summaries. A training-grade data schema doesn't make the whole system training-
ready: real client execution adapters are Sprint2, anonymization/dataset export/
review later, controlled replay/eval and Memory later still. Never mark the full
P0/P1 brief complete because the Sprint1 recorder passes its narrower gates.
