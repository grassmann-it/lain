# Starter recommendations — work package

## Goal and status

On 27 October 2026, the user requested a prominent starting selection of roughly
five recommended subscriptions and supplied NanoGPT/Synthetic/Kimi/Z.ai/MiMo/Venice referral URLs.
English-only and the selected 0.8.0 release identity remain binding. All six explicit links are in the [referral guide](provider-referrals.md);
NanoGPT/Synthetic also appear in optional provider catalog signup steps. This package plans the starter experience; it does not implement it.

Recommended design below is provisional. Its assumption is that newcomers need
help choosing one useful account, while existing users should reach their work
quickly. A compulsory purchase funnel or five simultaneous subscriptions would
not serve that need.

## Proposed experience

- Keep Accounts as the initial destination. When no accounts are configured,
  put a prominent `Start with a recommended provider` section above the existing
  connection controls. Offer `I already have an account` and `Try free options`
  alongside the curated selection. No account or purchase is required to browse.
- Show up to five compact cards; each explains the use case, verified monthly
  price/currency (or `Check current pricing`), included API/coding access,
  important limits, Lain verification status and evidence date. Do not fill
  five slots with unqualified purchase recommendations merely to meet a count.
- Main actions: `View plans` (or explicitly `View plans — referral`) and
  `Connect existing account`. Referral cards explain that Lain's maintainer may
  receive rewards and retain an ordinary provider link. Only explicit clicks
  open an external browser. Connecting accounts does not add attribution.
- Keep recommendations reachable from Providers and Getting started. For
  configured users, use a compact dismissible entry instead of repeatedly
  expanding the starter cards. Store dismissal in local UI preferences; do not
  change provider configuration, profiles or routing to manage promotion.

## Candidate selection, not a newly verified buying recommendation

The [dated comparison](provider-recommendations-2026-10-26.md) supports NanoGPT
Pro, Synthetic Go and OpenCode Go as alternatives. A fourth card can explain
using existing ChatGPT/Codex access. Kilo Pass is a conditional fifth candidate:
paid endpoint/plan coverage remains unqualified in Lain. Review that route before
recommending its purchase; otherwise show fewer paid cards and the free path.
Kimi's supplied referral can be offered as a membership alternative after its
specific Lain access and campaign terms are checked. A referral alone does not
qualify a recommended subscription. Its supplied share text promises a benefit
up to one-year membership credits; do not turn that into guaranteed one-year
service or Moonshot API access. No new prices, live access or provider
compatibility were verified by this plan.

MiMo referral code `6BLV24` and the exact supplied link
`https://platform.xiaomimimo.com?ref=6BLV24` are recorded in the guide. The
invitation advertises 10% off the first paid Token Plan order, subject to provider
terms. A referral alone does not qualify MiMo for a recommendation card.

Venice referral `https://venice.ai/chat?ref=oTNWrc` is also supplied. Venice is
not yet implemented: keep it as an expansion candidate until route qualification
and current campaign terms are checked, rather than offering it as ready to connect.

The selection order must follow usable access, cost and task fit, independently
of whether a referral link exists. Existing connector/catalog presence is not
proof of subscription entitlement or end-to-end acceptance. Recheck final plan
terms and the exact Lain route before activating purchase recommendations.

## Implementation slices

1. **Curated content and contract.** Add a small bundled recommendation model
   referencing stable provider IDs. Keep recommendation copy and dated price/
   verification evidence distinct from provider credentials, routing and
   technical compatibility. Explicit optional referral URLs and ordinary URLs
   are presentation metadata, never auth/OAuth parameters. Reuse the existing
   catalog/installed-method projection for `Connect existing account`; avoid
   copying a second authentication capability list. Document the selected
   cards and any unresolved qualification before enabling their purchase CTA.
2. **Startup and catalog presentation.** Implement the shared cards/entry points
   using existing Desktop components, link opener and local preferences. Reuse
   them on empty Accounts, Providers and Getting started. Inspect actual installed
   connection methods and replace the stale Getting started claim that other
   OAuth providers are unavailable; do not replace it with a blanket OAuth claim.
   Host unavailable/unknown configuration is not a proven zero-account state.
3. **Acceptance and documentation.** Verify the integrated flow on private
   Xvfb at wide/narrow viewports; update English screenshots/help. Check empty,
   configured, dismissed, offline/unknown and catalog-unavailable states. Test
   local preference persistence and account-connection routing without opening
   real signup pages. Inspect the final external URLs and referral disclosure.

Content and UI can be prepared separately once their small presentation contract
is agreed. Integration/acceptance follows both. No automatic signup, purchase,
network price scraping, remote promotions, tracking events or runtime routing
changes are part of this package.

## Acceptance criteria

- A fresh installation exposes a useful provider choice and free/existing-account
  paths without visiting a separate hidden settings page.
- Prices, inclusions and live verification claims have explicit evidence; missing
  data stays unknown. Kilo's unresolved paid route is not presented as ready.
- The exact user-supplied NanoGPT, Synthetic, Kimi, Z.ai, MiMo and Venice URLs appear only on explicitly
  labelled referral actions. Ordinary links and existing-account setup work
  without attaching referral parameters; browsing performs no requests/sign-in.
- Dismissed promotion does not return on every render/restart; recommendations
  remain discoverable. Existing startup navigation and host-offline diagnostics
  remain useful. Cards/actions are readable and reachable at 800×560.
- All first-party copy is English; source tests and native UI evidence apply to
  the final candidate, with no transfer of old binary qualification.

The remaining decision is the exact visual prominence/card selection. The
recommendation is an inline empty-state section, rather than a startup modal.
This affects presentation only; it need not expand the 0.8.0 provider scope.
