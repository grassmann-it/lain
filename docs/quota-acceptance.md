# Provider quota acceptance — 2026-10-23–24

Status: **accepted within the verified scope below**. Static/source review,
real-provider observations, fixture tile/Usage checks and installed Desktop
rendered verification are complete.
A successful synthetic test is not evidence of a successful provider response.

## Scope and acceptance criteria

The reported defect is that real Z.ai and Kimi accounts show `error` instead of
usable quota information. Acceptance requires successful provider observations,
visible progress bars and meaningful window labels, not merely replacing an error
label. MiMo must distinguish an unsupported quota integration from a failed fetch.

| Area | Required behavior | Current evidence |
| --- | --- | --- |
| Percentage semantics | A valid provider percentage is usable even if absolute quantities are unknown; it never becomes token, request or credit counts. Explicit `percent` / 100 normalization is percentage points only. | Common DTO and integrated service inspected: percentage-only success accepted; labels/percentage forwarded to DTO. Actual Z.ai/Kimi semantics verified below. |
| Unknown and zero | Missing, malformed, negative and non-finite quantities are unknown; a real zero is retained. Invalid percentages do not draw empty/full budget bars. | Adapter/static review; root reports 98 quota assertions passing. Actual zero remaining observed on Kimi. |
| Window identity | A reset change and provider array reordering do not move last-good history to another window. | Z.ai uses stable type/unit/number identity for distinct tuples observed live. Identical duplicate tuples still require occurrence suffixes, so arbitrary duplicate-row reordering is not qualified. Kimi uses canonical named windows; review found index-based ambiguous legacy windows, then q2 restricted fallback to a single legacy window. |
| Freshness | Failed/partial refresh retains identifiable last-good data as stale; observed time and last success remain distinguishable. | Integrated service and fixture assertions inspected; actual amber Last known row and separate Checked/source timestamps rendered. |
| Unsupported | A provider without an adapter shows unsupported/manual lookup, without a failed-network implication or a fabricated available balance. | Integrated `QuotaService.Project` inspected: unsupported provider projects `unsupported` with unknown quantities and console guidance for direct MiMo. |
| Overview | Budget use is salient without opening an account; account name, limiting window, consumed percentage and known reset are understandable. | Run4 tile screenshots inspected at 1120×780 and 800×560; see evidence below. |
| Accounts | Tiles show the salient remaining/bar/reset summary immediately; clicking expands all windows and account actions. Source, observation, last success and errors remain inspectable. | Run4 tile screenshots inspected at 1120×780 and 800×560; see evidence below. |
| Pool semantics | Distinct time constraints and accounts are not summed into invented available funds. Direct MiMo and Command Code-provided MiMo remain distinct subscriptions. | Tile/source review confirms independent windows and units; no aggregate balance is created. |
| Real providers | Actual Z.ai and Kimi quota refresh succeeds using the installed implementation, with no provider/modelcoach cache substituted. | Root-owned two-GET real probe passed; independently inspected sanitized JSON evidence below. |

Read-only reference: `~/.local/share/modelcoach/provider_status.py` was inspected
for schema context only. No modelcoach quota cache is consumed by the implementation
or this review. No credential contents were exposed or reused for requests and
no user service was changed by this reviewer. The later legacy investigation used
read-only file hashes/modes, not credential parsing or provider requests.

## Z.ai window-label evidence

The public official plan page
<https://z.ai/manage-apikey/coding-plan/personal/my-plan> loads this frontend asset:

<https://static.bigmodel.cn/z-ai-website/_next/static/chunks/3i6jwetj169n2.js>

Retrieved 2026-10-23; SHA-256:
`208ce91238a34ee264f3ba70ece749c4dbbdebb3e35e83d763748f120bd10916`.

Its `getCreditUsageLimitsDefaults` explicitly associates `CREDIT_LIMIT` and
`unit:3` with `5-hour limit`, and `unit:6` with `Weekly limit`; both use
`unit_text:Credits`. `getDefaultUsageLimits` similarly associates `TOKENS_LIMIT`
unit 3 with `5 Hours Quota`, unit 6 with `Weekly Quota`, and `TIME_LIMIT` unit 5
with `MCP Quota` (`unit_text:Times`). This grounds human-readable labels for the
known provider windows. Unknown combinations must keep an honest generic label.
The asset is implementation evidence, not a promise that future provider schema
versions will retain these enums.

The [official usage plugin](https://github.com/zai-org/zai-coding-plugins/blob/main/plugins/glm-plan-usage/skills/usage-query-skill/scripts/query-usage.mjs)
confirms the monitor quota endpoint and raw-key authorization. Its older
`TOKENS_LIMIT => Token usage(5 Hour)` type-only mapping is insufficient for new
credit or weekly windows. [The plan update notice](https://docs.z.ai/devpack/notice/usage-revision.md)
also documents 5-hour and weekly limits and the transition to credits.

## MiMo: confirmed display limitation

Public official documentation was inspected without authentication on 2026-10-23:

- [Individual Token Plan](https://mimo.mi.com/docs/en-US/tokenplan/Token%20Plan/subscription)
  says: “You can check the quota and usage of your current plan at Token Plan,”
  linking to <https://platform.xiaomimimo.com/#/console/plan-manage>.
- [Pay-as-you-go pricing](https://mimo.mi.com/docs/en-US/price/pay-as-you-go)
  says the ordinary API key consumes account balance and is not interoperable
  with Token Plan package quota.
- [OpenAI-compatible API reference](https://mimo.mi.com/docs/en-US/api/chat/openai-api)
  documents per-completion `usage` statistics. Those request totals are not a
  remaining subscription quota endpoint.

No supported API-key endpoint for remaining Token Plan quota was confirmed in
these sources. This does **not** establish that no endpoint exists. The truthful
current product behavior is unsupported quota lookup with a console direction;
request usage, subscription price and another provider's balance must not be used
to invent MiMo's remaining quota. Some old sitemap documentation paths redirect
to a page shell; the source links above are the current rendered documents.

## Scoped Usage source review

The root extended this review to the TUI-inspired Usage changes in `6c3b0c8`.
Source inspection found no blocking measurement/filter defect:

- Summary, account/model and daily metrics consume host aggregates. They are not
  reconstructed from the current 25-attempt page. No aggregate median, p95 or
  exact 429 summary is claimed without a supporting API field.
- Error percentages use host errors divided by attempts. Requests and attempts
  remain separate. Cached/reasoning fields are not added to reported totals.
- Output rate is shown only per completed non-pending attempt with reported
  nonnegative output and finite positive duration. Its label explicitly says
  output divided by full end-to-end duration; it is not token-generation speed.
- Date/account/model/status filters become server-side queries. Calendar presets
  use the selected zone, start is inclusive and end exclusive; invalid input
  leaves the applied snapshot and its filter scope visible.
- Unknown daily token values retain a dash marker, and host coverage, unknown
  usage counts, observation time and cursor remain explicit. Pending input and
  expansion/focus state are preserved across polling.

This is source review, not a runtime claim for every newly added interaction.
Usage screenshots and fixture assertions are assessed separately below.

## Final evidence and outcome

Root reports **98 synthetic quota assertions passed**. The reviewer separately
inspected integrated service/adapters and
[`quota-real-providers.json`](../tests/acceptance/evidence/quota-real-providers.json):

- Actual adapters observed both providers at `2026-10-23T21:06:51Z` using two
  read-only GETs, zero inference requests; both credential hashes were unchanged.
- Z.ai returned two credit windows: 5 hours, 3% consumed, 434 used, 12,000 limit,
  11,565 remaining; 7 days, 29% consumed, 17,929 used, 60,000 limit, 42,070
  remaining. Provider count/percentage rounding is preserved independently;
  remaining is not recalculated from the other fields.
- Kimi returned 5 hours at 100% consumed / 0% remaining, monthly total at 78.87%,
  and monthly coding at 68.8%. These are explicitly percentage units, with no
  invented token or credit balance. Each row carries its provider reset.

First actual Xvfb render inspected:
`/tmp/lain-quota-ui-evidence-1/overview-1120x780.png` (build before the final heading
class change). Budget data and consumed bars are immediately visible, including
stale Command Code, exhausted Kimi and unsupported MiMo. The dark green direction
is consistent with the requested TUI influence.

**Superseded table finding:** Overview table rows shrink to their varying content
width and center independently, while the header stretches to the table width.
Account/window/consumption/reset columns do not align. Reported to root and q3;
the table implementation was subsequently superseded by the user's tile request. The same run also failed
the 800×560 viewport assertion before producing the smaller capture.

The later user instruction explicitly requests **tiles**, with useful quota
summary at first glance and all details on click. Table alignment/inline-window
layout is no longer the visual acceptance target. Functional/provider evidence
above remains applicable. The final fixture tile captures supersede these preliminary table runs.

### Tile and Usage fixture evidence

Root-owned final run `/tmp/lain-quota-ui-evidence-5/evidence.json` completed with
**226 checks and 48 screenshots, PASS**. The visually inspected preceding run4
passed 222 checks and 48 screenshots at source `24fe90f`; both runs use Desktop
assembly SHA-256
`1B5F54B1DF533C7328172B1B77F818637BC01ABA94F1F15C4859C2D3F0C1260B`.
The reviewer inspected actual PNGs, not just element counts:

| Actual renders inspected | Result |
| --- | --- |
| Overview 1120×780 and 800×560; Accounts closed 1120×780 | Named window bars, used percentage, remaining, exact reset and Checked time are visible in closed tiles. Supported budgets precede unsupported MiMo. Normal page scrolling exposes lower tiles; no horizontal collision observed. |
| Command expanded 1120×780 and last-window 800×560 | Three percentage windows plus purchased 12, free 0 and unknown model allowance remain distinct. Last model window is reachable through ordinary page scrolling. Stale weekly data is amber and says Last known. |
| Z.ai expanded technical details at both sizes | Actual count units, source/window identity, observation and last success are readable. Percentage-only weekly fixture has 72% used / 28% left, while absolute amounts explicitly remain Not supplied. |
| Kimi expanded 800×560 | All three windows and exact resets visible; 100% used / 0% left is marked Exhausted, with other windows separate. |
| MiMo expanded at both sizes | Not available and unsupported wording, no synthetic zero-percent bar. Provider console action remains in expandable details; the screenshots show the tile and require further page scrolling for lower actions. |
| Usage at both sizes; filtered groups at 800×560; details/null/error at 1120×780; output chart at 800×560 | Filters wrap cleanly. Host-derived totals, per-attempt error denominator, nullable token fields and exact daily output totals are readable. Error capture is scrolled below its banner; this does not establish that a banner stays in view while scrolling. |

The fixture validates production controls against in-memory HTTP. Routed button
Click events are not proof of OS pointer hit testing. No real provider or installed
Desktop claim follows from fixture screenshots alone. The installed host is
separately confirmed by
[`quota-host-installed.json`](../tests/acceptance/evidence/quota-host-installed.json):
Z.ai/Kimi rows `ok`, MiMo `unsupported`, usage/credentials/legacy service/config
unchanged. Installed Desktop verification is recorded below.


Runtime font verification separately inspected in
`/tmp/lain-font-runtime-probe/run.log`: the actual displayed Overview, Provider
budgets and Personal Codex controls resolve `IBM Plex Sans Condensed SemiBold`,
and the lain wordmark resolves `Oxanium SemiBold`. Four regular/semibold font
lookup assertions also pass. The visual suspicion of fallback typography was
therefore not confirmed and is closed.


### Installed Desktop and CLI verification

The reviewer inspected
[`quota-ui-installed.json`](../tests/acceptance/evidence/quota-ui-installed.json)
and [`quota-desktop-installed.json`](../tests/acceptance/evidence/quota-desktop-installed.json),
alongside actual installed screenshots under
`/tmp/lain-quota-installed-gui-check/`:
`overview-1500x1000.png`, Z.ai/Kimi closed and technical-detail views at
1120×780, and first/reopened Usage at 1120×780.

The real installed closed tiles visibly contain both Z.ai windows (5% and 30%
consumed at this observation) and all three Kimi windows (100%, 78.87%, 68.8%),
with units, remaining quantities/percentages, reset and Checked times. Expanded
technical details show the same identities, source, last-success times and
amounts as the host snapshot. They preserve provider values instead of estimating
a fresh balance when a reset time passes. MiMo remains explicitly unsupported.
No blocking quota layout defect was found in these installed captures.

The installed harness passed on the production Desktop DLL
`cb775d4f9c917c50851b35c9cbef6b42ddb1dd771b3420e8a6a051358cede4b8`.
It records 22 screenshots at 1120×780 and 1500×1000. The minimum 800×560 behavior
is qualified by the production-control fixture, not by this installed run.
Usage retains 1 request, 81,986 total tokens and cursor 2 across window close and
reopen within the same harness process. The host/proxy remain available while the
window is closed. Desktop/CLI installation preserves additive quota fields;
service operations and inference requests are zero in this verification.

The installed harness is GET-only for observations, with native tile/detail
expansion and Usage filter application. It does not exercise credential checks,
login, browser opening, quota-refresh actions or account mutation against the
real service. Background host activity is not disabled or attributed to the
harness. These boundaries do not weaken the directly verified Z.ai/Kimi display
fix, but prevent broader claims about unrelated account operations.

### Legacy guard discrepancy: logs, not credential/config changes

A later comparison against the private baseline captured at 19:17 on 2026-10-23
reported **4 added, 4 removed and 0 changed persistent paths**. The reviewer
independently classified every differing path without printing file contents:
all eight are rotating error-response `.log` files under `oauth/logs/`.
The removed names carry timestamps 16:31–17:02; the added files have modification
times 21:20:03–04, before this late quota/UI installation. Both snapshots retain
10 error logs. Logging configuration has request logging disabled; the core's
default error-log retention is 10 and deletes the oldest error logs.

This metadata is consistent with ordinary error-log retention. It does not prove
who generated those requests; log bodies were not inspected. There is no evidence
that the baseline was sanitized. Independently compared hashes/modes of the
three non-log OAuth credential files, legacy binary and legacy configuration
remain unchanged. The root separately verifies unchanged legacy service PID.

Consequently, **all persistent legacy files unchanged is not a valid claim** for
this comparison. The accurate claim is unchanged credential/configuration/binary
files, with four old and four new runtime error logs. Earlier zero-difference
reports remain evidence only for their own comparison time. No legacy file or
service was modified by this review.
