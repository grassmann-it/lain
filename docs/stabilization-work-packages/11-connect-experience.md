# STAB-11 · Finish the agreed account-connect experience

28 October2026. Requested correction to the stabilization plan: the simplified
account/model add/remove workflows and best qualified browser/CLI login were
already agreed. They are stabilization outcomes, not deferred wholesale to1.0.
Status: ready for current method/UI inventory and existing qualified integrations;
new vendor authentication/credential ownership requires separate qualification.
Owner: Account-connect experience owner, coordinating Host capability definitions.
Dependencies: STAB-00, existing AC-00/01/02 contracts; model handoff STAB-04 and
removal STAB-05. Final native acceptance on STAB-08 artifacts.

## User outcome

Provider → best suitable available connection → minimum genuinely required
resource/plan choices → usable account with explicit Inference/Models/Quota
outcomes → suitable model-add action. No key copy when a qualified login already
provides the required credential. Existing account can gain monitoring/inference
without duplicate identity, lost routes or lost history.

Primary sources: [existing account-connect packages](../account-connect-work-packages/README.md),
[AC-02](../account-connect-work-packages/02-experience-and-capabilities.md),
[connect design](../account-connect-redesign-2026-10-24.md),
[UI acceptance](../ux-design-work-packages/README.md).
Reconcile completed work instead of rebuilding an independent wizard.

## Fresh installed-method evidence,28 October2026

Read-only `lain accounts methods --json` returned these capabilities. Availability
is not a freshly successful login/inference test, nor universal tariff eligibility.

| Provider | Method status | Advertised purposes and important limitation |
| --- | --- | --- |
| OpenRouter | browser available | inference, models, quota; dedicated key bootstrap; account/cap/balance semantics still separate |
| Kilo | device available | inference, models, quota; actual Pass/wallet reading scope still qualified independently |
| Codex | codex-owned available | inference, quota; current own login needs Gateway/Core; standalone Monitor login unavailable |
| NanoGPT | browser available | inference, models; do not promise quota from this method |
| Kimi | device available | quota only; existing inference access remains separate on same account |
| Gemini API | official-cli available | quota only; installed qualified Google CLI, resource selection/rights required; standard grant is broader than read-only |
| MiMo | console-browser available | quota only; separate owned console/browser session and platform prerequisites |
| Fireworks | official-cli setup-required | inference, models, quota advertised but current prerequisites unsatisfied |
| Poe / cloud OAuth | setup-required | configured/qualified client prerequisites required; no working universal OAuth button implied |
| Requesty / Alibaba / Command Code / Mistral | official-cli unavailable | no fully qualified general executable/store/handoff; a historical private login does not enable the adapter |
| Z.ai managed | unavailable | no qualified independent managed login; existing suitable key method remains |

This table is a dated operational capability read, not a current provider-policy
or whole-installed-account audit. Re-read before execution; infer no quotas or
credentials from method labels. Source: ConnectionMethodCatalog and registered
login adapters in Host Program. No logins or provider calls performed here.

## Scope and ownership

Reuse one method/capability-driven flow for Desktop and CLI. Primary files:
ConnectionMethodCatalog, registered login method descriptions, Desktop account/
login/model views and Cli/AccountWizard. Shared DTO/API/migration changes owned
by integrator. STAB-04 owns attachment/reconciliation semantics; avoid two editors
independently replacing the same account/model flow.

## Work

1. Compare actual UI/CLI actions with method catalog and existing AC/UXD acceptance.
   List hidden good paths, misleading defaults and dead-end setup-only entries.
2. Choose a recommended method based on requested purpose, runtime/platform and
   prerequisites, not simply because it is called OAuth. Explain actual grant
   scope; an official CLI grant can be broad. Keep existing-key alternative.
3. Expose one clear primary Connect action and secondary alternative methods.
   Unavailable methods explain why; setup-required offers concrete supported
   preparation, not an active button that always fails. Installing an external
   CLI or starting its browser login is a deliberate supported operation.
4. Attach outcomes to the stable account: inference/models/quota independently
   ready, missing prerequisites, unsupported, unverified or failed. Reuse same-key
   quota where supported; don't ask for a second token without a real need.
5. After connection offer suitable model selection/addition with account/endpoint/
   product already filled; include search/select-all/filter for large catalogs.
   Supported optional Free-only is a real policy, not a decorative checkbox:
   preserve current qualified enforcement, no silent keycap changes or paid fallback.
6. Reconnect/enrich existing account by stable identity/generation; preserve own
   name, other capability grants, routes/profile membership and usage history.
7. Respect cancel/timeout/pending browser state, retain drafts and avoid creating
   a duplicate account after uncertain completion. Prepare source-scoped success
   summary and next action rather than generic credentials warnings.
8. Add focused desktop/CLI fixtures plus private native first-use walkthrough.
   Broader provider research stays separate from making existing paths usable.

## Acceptance — proposed, not executed

- OpenRouter/Kilo qualified login path offers the promised credential handoff
  without manual key copying; each capability's result is independently reported.
- Kimi/Google/MiMo monitor setup never claims new inference access or extra budget.
- NanoGPT browser access does not invent quota support; Codex Monitor explains
  Gateway dependency instead of presenting unavailable standalone login as ready.
- Setup-required/unavailable Fireworks/Poe/Requesty methods do not appear as
  functioning one-click login until their prerequisites/qualification hold.
- Existing account gains an allowed missing capability without new duplicate ID,
  route deletion, forced profile Apply or changes to another app's credentials.
- Cancellation/expiry/reauth and conflict retain understandable repair/draft state.
- After connection, model addition is discoverable and not a series of manual
  account/route/profile fields. Selection distinguishes saved vs runtime/client.
- Desktop and CLI consume the same supported methods/purpose semantics.
- No purchase, unrequested paid inference, silent external credential refresh,
  browser cookie import or automatic global Apply as a Connect side effect.

General provider discovery, arbitrary new OAuth implementations and nested model
picker remain later scope. Existing agreed onboarding UX is not dependent on them.
Return changed paths, actual tests/evidence and unresolved source-specific gaps;
follow [the common package rules](README.md).
