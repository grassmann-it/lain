# Local conversation recording and skill comparison: Sprint 1 plan

30 October 2026 · **Plan only. No recorder has been built or enabled.**
This is the current plan after the user's explicit revisions: no archive encryption
or key management; record across all models used through enabled lain connections;
prioritize human inspection and comparing skill/prompt versions. Separate review,
anonymized export and any training/distillation remain later stages. Earlier
mandatory-encryption recommendations are superseded for this scope.

## 1. Outcome

After the recorder is explicitly enabled, conversations passing through lain stay
available across UI/service restarts, with messages, tool requests, client-reported
results, retries/model switches and evidenced worker/branch relationships. The user
can inspect a saved run in lain, relate it to a skill/prompt variant and compare
runs. New installations default off. Metadata-only remains an optional lighter
mode; content opt-in is needed for messages/arguments/results. When content is on,
all used model/provider routes are included by default, without a training-rights
allowlist blocking ordinary local inspection. Optional filters narrow that scope.

Keep private user-owned files/directories, bounded resources, secret exclusion and
no cloud telemetry. "All models" does not mean intercept traffic bypassing lain,
read other applications' credentials, import historical chats, silently collect
repositories or claim complete observation of local tool execution.

## 2. Verified baseline and change isolation

[Audit](observability-audit.md), [coverage](capture-coverage.md),
[existing observability contract](observability-contracts.md),
[Core integration](../proxy/README.md),
[conversation scope](trace-research/conversation-first.md).

Installed Core: CLIProxyAPI8.0.41/lain-patch4, upstream
`8289ec4c71ad914d0612c2776352b76ba3d355a6`, binary SHA256
`191f4f17e4096558c32ebc5b28ec51341fadc6549f3269300af22800dfe6ce18`.
Checkout is patch5. Host835d2e2 had applied917/desired919 at the audit read.
Do not apply pending changes, activate dynamic plugins or slip patch5 routing/
compatibility changes into the recorder update. Freeze a verified baseline and
qualify any separate transition explicitly. Preserve foreign WIP and the installed
Desktop responsiveness fix. Recording does not change the existing fail-closed
Usage accounting journal or its acknowledgement path.

## 3. Proposed pipeline

```text
Existing authenticated admission / handler / dispatch / transport boundaries
    -> one-way bounded observations (no response/auth/routing mutation)
    -> bounded Core buffer + private local handoff, source epochs/sequences/gaps
    -> independent Host worker: redact/validate, normalize, compress, persist
    -> append-only observation journal + payload/artifact files
    -> rebuildable SQLite conversation/attempt/tool/worker/run projections
    -> paged local API -> saved conversations and run-variant inspection
```

Use checksummed/versioned recoverable records and restrictive OS permissions,
**not encryption**. SQLite is a projection of committed source records; it can be
rebuilt. Events can reference large payload/artifact files instead of duplicating
the text throughout metadata. Ordinary debug/error logs never become an alternate
body store. Existing error-only logs require their own audit/policy, not automatic
import into the new recorder.

### Hook strategy

First qualify a static passive SDK bridge using `WithRouterConfigurator` and
`BaseAPIHandler.SetPluginHost`, keeping `cfg.Plugins.Enabled=false`. Existing
callbacks can mutate/drop/terminate, so the bridge must return no mutation fields,
keep headers nil for pass-through, explicitly disable per-chunk request/history
copies, and test replacement/lifetime across config reload. HistoryChunks is
bounded64/1MiB and may be omitted entirely by newer schemas; it is not storage.

Use the existing original-request boundary for true client ingress. After-auth
observes a pre-executor representation; handler response interception precedes
validation/actual writes. Neither is proof of final provider request or actual
client egress. Prefer a small additive send-side observation, if required, over a
new ResponseWriter wrapper; qualify it independently. Existing transport hooks
can supplement provider HTTP boundaries; provider WS and client WS are separate
coverage cases. Missing boundaries must remain explicitly unavailable.

No recorder disk I/O, compression, extensive scanning, AI evaluation or remote
request on the provider callback. Buffer references count against byte limits;
never hold arbitrary HTTP contexts/auth objects or spawn a task per chunk. An
archive fault changes capture health/completeness, not inference or accounting.

## 4. Conversation and data contract

- Separate conversation, branch, turn, API request and provider attempt. Model
  changes can remain in one conversation; forks/retries remain distinct.
- Separate model-requested tool, result supplied by client, and execution proven
  by a later client adapter. Tool name/time similarity is not a join key.
- Link workers using supplied scoped parent/child evidence. Missing/conflicting
  relationships stay unassigned; no fabricated root/worker tree.
- Preserve actual local vs external IDs and their provenance. An unknown model
  revision, context prefix, effective reasoning or usage value remains unknown.
- Keep client ingress, normalized request, provider request/response and emitted
  client output distinct. Mark each available/partial/unobserved representation.
- Incremental parser preserves supported parts, roles, item/call IDs, fragmented
  arguments, terminal usage, aborts, partial failures and compaction boundaries.
  Normalized JSON is not byte-original wire data. Invalid/omitted content is
  flagged, never reconstructed as a complete success.
- Keep only reported nullable token/cache/reasoning measures with source and timing
  definitions. A usage observation is not actual vendor billing or verified task
  success; no hidden-reasoning reconstruction or invented cost/efficiency scores.

## 5. Skill/prompt variant provenance

The immediate comparison unit is a **run with a variant manifest**, not a future
training example. Proposed fields:

| Field | Meaning |
| --- | --- |
| comparison_id, run_id, variant_id | Local grouping such as skill-v1 vs skill-v2 |
| skill ID/revision/content hash | Exact explicitly selected or actually observed skill source, when available |
| system/developer prompt hash | Version of the instructions actually available at the observed boundary |
| harness/client version | Known client configuration and reporting source |
| requested/reported model, reasoning, tool-schema hash | Controls and capabilities supplied for this run, unknown fields retained |
| task/context/repository snapshot reference | Available starting-state identity for a controlled comparison |
| source/evidence status | Configured by user, reported by client, or observed in payload |

A hash/version label does not prove a skill was loaded or followed. Do not scan
all skill directories or invent a loaded-skill list when the harness exposes none.
A manually attached run label is useful but remains user-provided provenance.
Preserve original content and model identities; later anonymization creates a
separate derivative without destroying joins. Basic variant labels/run metadata
belong in the first browser; automated grading, replay and blind review do not.

Comparisons should keep model/task/harness/budgets stable while changing a skill
version, or keep the instruction version stable while changing model. All runs
can still be recorded for exploratory reading; uncontrolled differences aren't
proof of which model/skill is better. An optional explanatory skill is itself a
prompt intervention and must be labeled, not silently installed by the recorder.

## 6. Persistence, recovery and resource limits

1. One committed source journal is authoritative. SQLite stores projections,
   unique event identity and a committed-source checkpoint. Commit payloads and
   journal before checkpoint/index progress; replay duplicates idempotently after
   a crash. RAM acceptance is not durability.
2. Independently validated bounded frames/segments with checksums, rotation and
   crash-tail handling; corruption inside a sealed segment is visible. Checksums
   detect corruption, not adversarial authenticity. No key/unlock/Vault workflow.
3. Compress records/sealed segments and use payload references. Qualify cross-
   record CAS/dedup separately if needed; preserve every logical occurrence of
   repeated histories even when bytes share storage. Blob-first commit, orphan
   recovery and reference-aware deletion are mandatory if CAS is introduced.
4. Bound queue count/bytes, active assembly state, parser expansion and working
   set. Limit total disk including index/WAL/temporary files and duplicates;
   reserve shared-disk headroom and stop before exhaustion. No unbounded spool.
5. Capture can drop/stop under pressure while inference continues. Show source
   gaps, oversize/redaction omissions, partial sessions and unknown crash-loss
   intervals. Don't promise complete capture under arbitrary pressure.
6. Retention/source deletion removes affected payloads/projections/derivatives;
   journal immutability holds during retained life, not as a ban on deletion.
   Backup/restore tests use a local isolated directory; no cloud/offsite setup.

## 7. Privacy, rights and operating choices

**Confirmed:** no archive encryption/key management, all-model local inspection
scope, separate later assessment/export/training. Keep0600 files/0700 directories
on Linux, symlink/path protection and existing authenticated local endpoints.
Authorization/Cookies/API keys/OAuth tokens/passwords/private keys and known secret
files remain excluded from capture. This exclusion happens before persistence,
not deferred until export. Redaction failure omits content and marks a gap;
allowlisted metadata can continue. Logs/errors/metrics don't dump payloads.

Training rights may remain `unknown_not_approved` without blocking human inspection.
Later training/export checks are separate. Specific restrictions on retaining
particular content still need to be represented; a blanket model family or
Open-Weight label is not a rights verdict. No external judge or cloud sink.

Remaining recommendations, **not approved numeric defaults**:

| Choice | Recommendation and consequence |
| --- | --- |
| First qualified client/protocol | Codex/Responses first; other admitted protocols get independent parity gates; unsupported endpoints remain unsupported |
| Client scope | Register a trusted local capture principal; shared keys/User-Agent don't distinguish applications. All models for enabled scope, narrower filters optional |
| Hard stop | Deny new observations atomically, discard pending uncommitted content; retain committed history under retention policy |
| Initial retention | Propose content7days, metadata30days; user-visible and configurable |
| Storage budget | Propose total5GiB plus shared-volume free-space cutoff, including index/temp/WAL |
| Experimental queue | Start measurement with bounded16MiB, then size from real fixture/latency/RSS evidence; large omission explicit |
| First UI location | Saved-conversation mode beside Live; no duplicate top-level workspace |

The recorder can protect against its own quota/failure modes; a globally full
filesystem can still break the existing accounting journal. Test and report this
baseline limitation instead of claiming universal isolation.

## 8. Work packages and dependencies

The detailed parallel handoff is now the [REC-00–08 package register](recording-work-packages/README.md), including exclusive file ownership, shared seams, wave ordering and per-package acceptance. The table below remains the conceptual Sprint1 summary; REC-00 is the shared-contract authority and REC-04 the sole recording-migration owner. These are planning documents, not code or permission to start implementation under the current plan-only instruction.


| Package | Ownership and planned output | Acceptance |
| --- | --- | --- |
| S1-A contract + fixtures | Integrator: shared DTO/schema, source/identity/variant/nullability/stop contract | Synthetic200-call/100-tool graph, fork/retry/model-switch/worker/compaction cases with no guessed joins |
| S1-B independent store | Storage owner: new archive migrations only, journal/payload/index/recovery/retention | Restart, torn tail, idempotence, corruption, quota, deletion/restore and bounded resource tests |
| S1-C observer | Adapter owner: static-hook spike, minimal capture hooks and provenance | Auth/routing/bytes/headers/flush/retries unchanged off/on/fault; no dynamic plugin activation or accidental patch5 rollout |
| S1-D normalizer | Protocol owner: incremental parts/messages/calls and available usage | Split SSE/UTF-8/arguments, parallel tools, reported result linkage, final usage, partial/unsupported cases |
| S1-E query + browser | Host/client/Desktop owner: health, paged archive/API views, lazy workers, run/skill labels | UI closed while recording; reopen persisted conversations, narrow/wide Xvfb, long data without full-archive load |
| S1-F qualification | Independent reviewer/integrator: integrated tests, benchmark, exact artifacts, rollback/coverage | Reproducible evidence with real limitations, no customer data or paid test calls |

B/C/D parallelize after A's executable contract. E can use those fixtures before
capture is wired. Keep one serial build/publish lane, cache TMPDIR and bounded
concurrency; GUI tests exclusively private Xvfb/D-Bus. Preserve foreign WIP and
freeze owned source in worktrees. **No implementation workers start while the
latest user instruction remains plan-only.**

## 9. Definition of Done for Sprint 1, not full P0/P1 completion

Planned, not executed recorder tests:

- Baseline/off/on/failed parity per claimed protocol: bytes, response status and
  headers, flush sequence, cancellation, account selection, retry count, Usage
  journal/gates, completion/error semantics. Include nil-vs-empty interceptor
  headers, disabled-copy paths, hook reload, short writes, write/flush errors,
  keepalives and post-interceptor validation failures.
- Synthetic graph and protocol cases including root+two workers, same-name
  parallel tools, partial arguments, later result input, retry with partial output,
  full-history resends, previous-response state, forks, resume and compaction.
- Queue full, consumer unavailable, slow/failing/full archive storage, crash at
  append/index boundaries, corruption and disk cap. Inference not newly failed;
  exact known durable facts reconstructed, data loss explicitly represented.
- Canary secrets absent from archives/index/temp/debug logs; malformed and
  injection-bearing content cannot change capture or permission policy.
- Source deletion and local restore, permissions and symlink/path handling.
- Native minimal browser, lazy worker/request expansion and copied-content scope;
  actual published-artifact checks, not just mock UI or source tests.
- Repeated controlled load1/4/8 concurrency: p50/p95/p99 TTFT/stream gaps,
  throughput, CPU/RSS, allocations, queue bytes, disk bytes/write amplification.
  Proposed budget for baseline review: <=2ms added local p99 TTFT/stream gap,
  <=2% throughput loss, bounded memory/disk. Measure noise and settle budget before
  claiming it passed; no existing Inspector result proves recorder performance.

The ten approved-trace SFT/DPO/eval export demo, real execution verification,
OpenCode adapter, training, automatic evaluations and Memory are later acceptance,
not fake completed Sprint1 checklist entries.

## 10. Delivery and rollback

Each package returns owned diff/source pin, actual tests, migration/config docs,
coverage and explicit gaps. Isolated fresh-install qualification first, ship
recording disabled, then explicitly enable the selected local scope. No historic
log import, pending route Apply, credential replacement, cloud configuration or
paid inference merely to install the recorder.

Rollback disables observation/discards pending capture without stopping inference;
stop archive worker before reverting its code/config. Keep the independently
versioned data with a compatible reader or explicit retention decision. Do not
roll back/delete existing Usage, accounts, secrets or profiles.

After Sprint1: Sprint2 genuine client/tool/test adapters; Sprint3 authorized import,
annotation and anonymized permission-aware exports; Sprint4 controlled model/skill/
prompt experiments and replay; Sprint5 separately scoped context/Memory or approved
training. Preserve the human-inspection goal rather than making a training pipeline
a prerequisite for opening a saved chat.
