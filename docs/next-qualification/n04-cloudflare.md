# N04 — Cloudflare analytics diagnostics

Baseline: `7661886fbcf6acd355eb7346cebad9b8fa1cff1c`.
Source implementation and focused fixtures prepared in the isolated N04 worktree.
No live token, provider account, API, browser, inference, credential mutation,
build, restore or product runtime was accessed by this worker.

## Changes and limits

- A locally conflicting monitor/inference account binding is identified before a
  request; a response naming another account is discarded with a binding reason.
  Empty/missing account selections retain uncertainty between selection and access.
- HTTP 401 remains credential rejection. Generic HTTP 403 no longer claims that
  one specific permission is missing. Only an explicit named Account Analytics
  permission rejection directs a grant review; raw provider text never leaves
  the parser. No error implies a paid plan, upgrade or automatic new login.
- GraphQL failures may read the selected account's dataset settings. Explicit
  `enabled: false` and a valid available-field list lacking `sum.totalNeurons`
  have separate safe reasons. Neither response distinguishes permission from
  entitlement or establishes billing coverage. Unknown/denied/malformed settings
  retain the original usage failure. Schema introspection remains diagnostic only.
- A successful account-bound query with no samples is a completed empty response,
  with unknown usage and no login failure. QuotaService's existing unavailable
  projection retains old measurements as stale. Explicit measured zero is retained.
- Existing endpoint, credential precedence, account identity, UTC interval,
  1-MiB/depth bounds and 30-second total deadline remain. At most three sequential
  GraphQL POST reads run; no query mutation, inference, OAuth or Wrangler was added.

The settings-query shape follows the recorded official settings/discovery source
C3 in `docs/provider-monitoring-review-2026-10-25/google-cloudflare.md` (Main,
public research dated 25 October). Actual Workers-AI dataset/schema access remains
account-specific and has not been live-qualified here. A refused settings query
is explicitly supported and does not become a claim about plan or permissions.

## Verification

`tests/cloudflare-quota/Program.cs` retains its existing token-selection,
normalization, UTC, transport and cancellation cases. New
`CloudflareDiagnosticCases.cs` supplies distinct fake account/permission/dataset/
empty cases, diagnostic failure/cancellation/request-bound cases, sanitization,
and actual QuotaService-to-QuotaDto projection for the shared GUI/CLI contract.
No production DTO, QuotaService, registry or endpoint was changed.

Worker checks: `git diff --check` only; C# fixtures **not built or executed**.
Root's serial slot must run the focused Cloudflare suite and any required
shared quota/UI/CLI regressions before source/runtime acceptance is claimed.

## Later read-only live qualification

A narrow probe is technically possible using the existing saved monitoring
selection: refresh only the selected Cloudflare account through the existing
`POST /api/v1/quotas/refresh` with that stable account ID. This is a read of provider
analytics with local cache updates; it does not alter tokens, grants or routes.
It must use the exact installed new Host, not this unbuilt source worktree.

Preconditions: a valid same-account monitoring grant already exists, Root has the
live runtime slot and the request is within the authorized live-read scope. The
probe should retain only sanitized Support/Error, selected resource, UTC window,
source and nullable measurements; no Authorization header/token/provider raw
diagnostic belongs in evidence. Do not refresh all accounts, create usage by
inference, change rights, or reconnect as a side effect. Compare an available
interval with the same account's dashboard if the user can inspect it. A clear
access/dataset limitation is an honest method result; successful quota/balance
support still requires actual measured data and its coverage qualification.
