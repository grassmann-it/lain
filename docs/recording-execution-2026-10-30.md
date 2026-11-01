# Recording and release implementation execution

31 October 2026 · **Implemented and qualified within the scope below.**
Source freeze: `10734a4` (full hash in the [machine-readable receipt](recording-verification-2026-10-31.json)).
No production installation, capture enablement, public version choice or publication
was performed. The staged package deliberately retains its existing backend/public
release gates as false; it is a verification candidate, not an approved update.

## Delivery and ownership

The user authorized six GPT6.1Sol and four GLM workers. Six Sol modules were
integrated: contracts/policy, storage, observer, normalization/projection,
API/client/CLI and Desktop. Four actual GLM5.3 native workers used the already
configured Z.ai route for active-state correctness, safe Core errors, Doctor and
synthetic acceptance tooling. There was no paid replacement route, extra
`free_only` veto, provider-cap change or account/profile mutation.

The root integrator froze and tested the owned source separately from pre-existing
CLI route commands and unrelated documentation WIP. Those working changes remain
preserved. All builds/tests used one serial lane and cache TMPDIR. GUI runs used
private Xvfb/D-Bus; packaged process tests used a private user/network namespace
with loopback only. No real inference provider or local user tool was exercised.

## Implemented result

- Independent default-off recording policy, explicit content consent, separate
  revision/stop generation and a stable installation identity scope. All used
  model routes on the owned Gateway are in scope by default; route filters are
  optional. Unknown training rights do not block local human reading.
- Nonblocking bounded Core feed for client ingress, actual attempts, executor
  output and reported usage. Stop, contention, overflow and incomplete source
  coverage have explicit states. Existing routing, auth and accounting remain
  their own paths.
- Transient SSE assembly followed by credential screening before durable writes.
  Raw fragments are not a persistent fallback. Source identity, observation
  boundary and redaction state are retained; unavailable facts stay unknown.
- Unencrypted private local checksummed journal and independently compressed
  payloads, bounded asynchronous writer, recovery, retention and clear. Storage
  accounting/visibility/seek avoid a directory or historical payload scan per
  append. Existing archive recovery does not block Host construction.
- Separate SQLite conversation/worker/run metadata index, bounded summary LRU,
  lazy selected-source hydration, and proactive index cleanup after retention.
- Authenticated Host recording API, common client, CLI controls/list/detail/run
  labels, and production Saved conversations browser. Message/part/text/source
  pages are bounded and expose gaps. Opening the browser never enables capture.
- Prioritized release correctness: applied-state-aware quota publication, safe
  allowlisted context/encrypted-content error classification, extended Doctor
  with a read-only UI bridge, actual-account capability/outcome summaries and
  observed routing explanations. The existing image/context/auto-compaction
  exporter fixes are included and regression-tested in the candidate.

Tool requests and client-reported results are distinct. Missing worker identities
remain unknown. Skill/prompt manifests label configured/reported variants without
claiming that a skill was actually loaded or followed. No cloud recorder, hidden
reasoning recovery, training exporter, automatic evaluation or Memory was added.

## Defects found and fixed during integration

Actual Host/Core testing exposed `/v1/responses` being rejected by a parser that
accepted only a symbolic protocol name, and completed objects with `error: null`
being marked failed. Both are fixed with regression fixtures. Stable identity
scope now joins native conversation IDs across per-request capture IDs and model
switches. Cumulative source loss does not taint every future body.

Retention now budgets the encoded metadata it actually rewrites, rather than the
uncompressed size of surviving compressed content. Deletion-control v2 uses fixed
hash tombstones within a consistent 100,000-entry/8 MiB bound, migrates v1 decisions,
and rejects further deletion before poisoning purge state. At full deletion
capacity new capture pauses until explicit clear; reads remain available. Old
readers reject v2. Retention proactively synchronizes the external index, including
idle UI, startup recovery and retry after failure.

Selected-source UTF-16 pages preserve whole surrogate pairs and always progress.
Native tests wait for actual configured window geometry rather than capturing a
misleading crop after a fixed delay.

## Verification actually completed

| Evidence | Result and boundary |
| --- | --- |
| Frozen solution | Build passed, zero errors |
| .NET groups | 13/13 passed: maintenance10, policy52, normalization56, storage22 tests, projection41, API40, CLI10, Doctor, active-state119, diagnostics29, account outcomes33, route presentation11, client export117 |
| Core source | All adapter unit tests, recording race tests and pinned build passed; source manifest verified for upstream8.0.41/lain patch6 |
| Packaged Core protocols | 15/15 actual-process synthetic cases passed: protocols/tools, auth/account lifecycle, retries/429, cancellation, journal failure, crash/restart and terminal write failure |
| Host/Core together | 10 checks passed on actual packaged Release DLL/Core: off, stream forwarding, redaction, selected-source paging, model/tool/worker correlation, load, stop, restart and clear |
| Native archive | 23 checks on production view, then repeated successfully with packaged Desktop/Core/Client assemblies; 1120×780 and800×560, 1000 messages/1000 parts, detach/reopen/source failure/delete |
| Existing Inspector | 123 native checks and12 screenshots passed against frozen source |
| Installer | Fresh isolated install and same-candidate managed reinstall passed; unrelated synthetic state and previous app preserved; no user service/bus started |

Private full receipts/screenshots live under
`~/.cache/lain-implementation-20261030/`. The committed receipt carries exact
artifact pins and bounded measurement summaries. Synthetic graph generator output
is separate and is not claimed as a real executed agent trace.

## Resource evidence and remaining delivery gates

The packaged HTTP test ran12 requests per mode/concurrency cell at1/4/8, all200.
A source observation was dropped at content concurrency4 in that run; loss was
reported. An earlier Debug run dropped2 and1 observations at4/8. Complete capture
under contention is therefore **not** claimed. The source queue and writer remain
bounded and inference continues.

These short full-response loopback measurements are not TTFT, streaming-gap,
long-duration RSS or an approved overhead budget. Packaged results varied with
concurrency; no negligible-latency claim is made. The earlier Debug measurement
observed about201 MiB Host RSS after catch-up; Core RSS was unavailable and stayed
null. UI working set and resize measurements belong only to their synthetic
native fixture. Sustained large-history/load and full delivery/rollback gates
remain separate from this implementation receipt.

The Linux candidate is at
`~/.cache/lain-implementation-20261030/lain-linux-x64-recording-candidate.tar.gz`,
with sibling SHA256 receipt and expanded package. It is not installed and its
`gate-status.json` is deliberately not promoted. Public release version, platform/
model support promise and recorder inclusion remain open. Windows/macOS native
acceptance and a distinct old-version archive rollback are not claimed.

## Operations and next scope

Use [operation and rollback guidance](recording-operations.md) and
[actual capture coverage](capture-coverage.md). REC00–07 are implemented; REC08 has
the bounded Linux evidence above, not a blanket full-release/performance approval.
PUB01–05 correctness/presentation increments are implemented and tested; PUB06
retains the full deployment/upgrade/rollback gate, PUB07 offline demo is optional,
and PUB08 keeps the previously corrected configured-route workflow without
asserting a new live provider test.

Next feature scope remains explicit client execution adapters, import, human
annotation/blind comparison, anonymized export/rights review, controlled evaluation
and only then training/Memory. Do not silently grow this recorder into those paths.
