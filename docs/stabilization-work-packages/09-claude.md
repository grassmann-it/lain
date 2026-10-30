# STAB-09 · Claude monitoring and separate Anthropic API access

Planning status: Additional requested scope: research ready, implementation conditional on verified access contract. Not a0.7 release prerequisite by default.
Owner role: Provider research / account-credential owner.
Dependencies: Existing provider/monitor contracts; STAB-00 decides whether any part enters0.7.

## Outcome and boundaries

User requests Claude subscription usage via OAuth/authorized login if suitable,
but explicitly no OAuth inference. Separately evaluate Anthropic Console API-key
inference and account reporting, including any real trial credits. These are
separate commercial balances and capability grants, even if displayed together.

## Ownership

New provider research report and scoped contract first; no raw credential export,
browser session scraping or inferred arbitrary OAuth app support. Later qualified
adapter/catalog implementation requires its own concrete plan.

## Work

1. Verify current official consumer subscription monitoring and allowed auth paths;
   distinguish official public API, official CLI and private/undocumented endpoint.
2. Identify rights, expiry, ownership/refresh rules and account/workspace scope;
   unsupported access remains unsupported, not an invented login flow.
3. Research separate API key inference and org/project usage/cost reporting rights.
   Admin/monitor credentials, if required, must not be sent to inference.
4. Verify trial eligibility, amount/currency/expiry and payment prerequisites in
   current official docs or an explicitly authorized account view. Do not promise
   free credits from an unverified assumption or from a paid Claude subscription.
5. Plan account binding, shared scopes, source freshness and clear UI labels.
6. Only after qualification, implement bounded readers and authorized API probes.

## Acceptance — proposed, not executed

- OAuth credential cannot reach any inference transport, including fallback/override.
- Subscription windows and API costs/balance display separately; no double budget.
- Inference key never impersonates a reporting/admin grant; monitoring cannot create charges.
- Missing reporting permission produces actionable setup, not fake remaining credits.
- A trial is shown only when positively reported; no signup, top-up or purchase by this package.
- Auth expiry/revocation is handled according to verified ownership; source clients untouched.

## Delivery

Return changed paths, source/artifact references, focused checks actually run,
remaining gaps and any blocker. Use the shared evidence format and execution
boundaries in [the package index](README.md). This document creates a work package;
it does not authorize production changes, new logins or release publication.


## Research starting points and present uncertainty

Official documentation retrieval on28 October2026 returned HTTP403 for the
[Usage and Cost API](https://platform.claude.com/docs/en/api/usage-cost-api),
[API overview](https://platform.claude.com/docs/en/api/overview),
[billing help](https://support.claude.com/en/articles/8977456-how-do-i-pay-for-my-claude-api-usage)
and [Claude Code legal/compliance](https://code.claude.com/docs/en/legal-and-compliance).
These links are starting points, not freshly verified support/trial claims.
The official Python SDK source was reachable but did not establish the proposed
subscription-monitoring permission or a trial offer. Use current official
sources and distinguish unavailable evidence from evidence of no support.
