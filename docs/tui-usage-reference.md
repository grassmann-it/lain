# Usage reference review · 23 October 2026

The user requested a review of the previous TUI and useful Usage improvements
in lain. This review is a source inspection, not a claim that the previous
application has been run or qualified against the current services.

## Sources and applicability

- `~/.local/bin/codex-proxy-tui` launches the Python module from
  `/home/tomka/Desktop/work/codex-proxy-tui/.venv`.
- That repository's `src/codex_proxy_tui/app.py` (`render_usage`,
  `render_requests`, row-selection handlers) and `presentation.py` implement
  the read-only views. Its `docs/architecture.md` defines their measurement
  semantics; `docs/implementation-plan.md` records bounded history reads.
- `~/.local/share/modelcoach/usage-and-workers.md`, `capacity_advice.py` and
  `provider_status.py` explain separate quota windows, source age, account
  pools and the limits of observed performance. They were read as references;
  their commands, caches, credentials and services were not used or changed.
- lain's accepted boundaries remain [architecture](architecture.md) and
  [service contracts](service-contracts.md): the host owns collection and
  aggregation; the desktop consumes its bounded API. The previous projects'
  requirements do not automatically become requirements for lain.

## Transferable findings

| Reference behavior | Consequence for lain |
| --- | --- |
| Requests and executions are separate; an internal 429 remains visible after a later successful request. | Keep request and attempt counts separate. Derive an error percentage from the host's errors / attempts, never requests. The host includes failed/canceled attempts and HTTP status ≥400. |
| Usage rows expose model, provider pool, effort, speed, output tokens, timing and sample count; selecting a row reveals detail. | Expose the token fields already present on lain's account/model aggregates. Keep unsupported grouping dimensions out of the UI. |
| Usage text search only filters the current historical snapshot, explicitly labelled as such. | Retain lain's stronger server-side date, account, actual-model and exact-status filters. Add convenient calendar ranges and a group-to-filter action. |
| End-to-end output rate uses reported output tokens divided by a positive duration for a completed execution. | Show this labelled rate only within an attempt with a terminal timestamp and valid measurements. Do not invent aggregate medians from the current page. |
| Missing tokens remain unknown; cached/reasoning tokens are not added to total; median values are not added together. | Preserve every nullable field from the host. A daily token chart must show unknown buckets as unknown, not zero. |
| Source freshness and coverage accompany historical data. | Retain observed time, cursor, selected zone, incomplete coverage and last snapshot on errors. Five-second polling remains lifecycle-bound. |
| modelcoach separates quota windows, account/model scopes, credits and observed tokens. | Usage remains measured traffic, separate from remaining quota, price or a promise of future capacity. |

## Bounded implementation direction

These are implementation choices within the user's authorized Usage improvement,
not new product requirements:

- Today, last 7/30 calendar days and this month in the selected time zone,
  with inclusive start and exclusive end still visible. Reject ranges beyond
  the existing 366-day API limit before requesting them.
- Daily chart selection for attempts, total tokens or output tokens, using the
  existing host buckets. Keep the accessible exact daily totals.
- Account/model detail rows with input, output, cached and reasoning tokens,
  error percentage and a button to apply that identity as a server-side filter.
- Explicit error-rate denominator and per-attempt output / end-to-end rate.
  Preserve unsubmitted filters, expansion and keyboard focus across polling.
- Use shared `Ui` colors and typography; the separate branding change owns
  the palette and fonts.

## Recommendations requiring a later API decision

The TUI's model/provider/effort/speed combinations, aggregate median/p95 with
sample counts, exact 429 summary, error-class filter, and cross-harness/worker
joins are useful candidates. lain's current aggregate contract does not provide
them. Adding them would require host/query contracts and corresponding evidence;
the desktop must not infer them from 25 paginated attempts or model names.

The present changes therefore add no collector, database reads, provider
probes, queue consumer, recommendation engine or API fields. Source review
alone is not runtime verification; integration builds and private-Xvfb checks
belong to the final integrated change.
