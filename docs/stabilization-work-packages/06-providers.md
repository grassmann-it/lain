# STAB-06 · Provider/model and quota qualification

Planning status: Offline checks ready; real probes require exact scoped credentials/cost authorization.
Owner role: Provider adapters and independent qualification owner.
Dependencies: STAB-00 model-scope decision; target candidate/generation agreed with STAB-01/04/08.

## Outcome and boundaries

Finish and qualify current Synthetic/Fireworks/OpenRouter/Upstage/Cohere work,
plus changed quota readers. Do not infer entitlement from hundreds of catalog
entries. Requesty error handling has changed at47f3c79: inspect before repeating.
The existing request for real testing of every configured model is not waived.

## Ownership

Affected provider adapters/catalogs and provider tests; coordinate Core transport
changes with integrator. Do not select arbitrary cheaper/paid fallback on failure.

## Work

1. Inventory which routes are configured, active, listed and actually qualified.
2. Validate exact products/endpoints/model IDs/plans and quota credential purposes.
3. Reproduce failures (including Cohere400) with sanitized protocol fixtures.
4. Check stream, realistic tools, paired continuation and supported context/reasoning.
5. Run only bounded authorized real probes, then record per-route evidence/limitations.
6. Publish complete matrix; a smaller declared support subset needs user agreement.

## Acceptance — proposed, not executed

- Subscription/free/PAYG remain distinct and use correct endpoint/product.
- Each advertised supported route has applicable client/protocol evidence.
- Unknown quota is distinct from error; cap, spend and wallet are not conflated.
- One provider error does not hide other quota readings; freshness/shared pool/reset clear.
- No key in argv/reports; no new purchase or artificial real quota exhaustion.
- Historical59-alias results do not qualify newly added models or new binaries.

## Delivery

Return changed paths, source/artifact references, focused checks actually run,
remaining gaps and any blocker. Use the shared evidence format and execution
boundaries in [the package index](README.md). This document creates a work package;
it does not authorize production changes, new logins or release publication.
