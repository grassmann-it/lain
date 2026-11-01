# Model-first picker and explicit access selection

Design proposal,28 October2026. User is considering a /model menu with provider,
custom-group and manufacturer/model views, including automatic access selection
for a chosen model and explicit selection of that model through a particular API.
This is not yet a selected menu order or an implemented routing extension.

## Problem and current boundary

Hundreds of concrete provider routes make a single flat provider list hard to
navigate. Manufacturer-based manual collections can be an interim view, but
should not become the only source of model identity or duplicate route records.
The ongoing account/model setup is a separate operation; this design does not
modify its processes, accounts, collections or pending revisions.

Source inspected at835d2e2 plus working tree: CodexProfileAdapter exports picker
schema_version1 with a flat groups array of models/optional router_model, up to
128 nonempty groups. DerivedModelGroups generates provider/account views.
ModelCollectionDto is a view over route IDs. Neither contract provides an
arbitrary nested manufacturer/model/access tree. Such a tree needs explicit
client/export capability negotiation and appropriate native client work.

## Proposed visible model

Three views over the same available route inventory:

- **Models:** manufacturer → model family if useful → exact model/version →
  automatic access or concrete provider/account access.
- **Providers:** provider → account where needed → models, optionally organized
  by manufacturer for large catalogs. Retain direct route selection.
- **My groups:** ordered user collections such as Free,1M and IT Sec, with their
  separately configured group router and model/access choices.

Recommendation: expose these as separate views/sections with search and a
configurable preferred starting view. Model-first is a useful default candidate
for large aggregator catalogs; the user has not chosen the default or section
order. Do not turn every nested node into a top-level flat group. Search includes
manufacturer, model name, provider and account; all views resolve to the same
stable route identities. Choosing a view does not activate configuration.

Example (illustrative structure, not a claim of live qualified entitlements):

```text
Models
  Moonshot
    Kimi
      Kimi K3 [exact qualified version]
        Auto access · allowed accesses only
        Synthetic · subscription
        OpenRouter · PAYG
        Command Code · subscription
Providers
  OpenRouter
    Moonshot → Kimi K3
My groups
  IT Sec
    Auto model · configured group policy
    Kimi K3 → Auto access / concrete accesses
```

Badges distinguish subscription/PAYG/free and ready/cooldown/exhausted/unknown.
Manufacturer is the model maker, provider the actual access service; OpenRouter
must not become the manufacturer of every catalog item. Unknown manufacturer
stays unknown. Multiple accounts retain clear names; personal/work accounts are
not silently merged simply because the model matches.

## Two different automatic actions

1. **Auto access for this exact model.** Candidate set includes only qualified,
   explicitly allowed routes for the same canonical model variant. It changes
   the access path, not the requested model.
2. **Auto model within this group.** Existing user collection policy may choose
   between different models. Reuse its ordered/sticky and scoped quota rules.

Family-level auto would be a third, explicitly enabled model-changing policy;
not a consequence of expanding a family node. A manufacturer's newest model is
not automatically a substitute for a selected older version.

An unknown generic button labelled Auto must not hide which action it performs.
Concrete leaf selection pins that route/account and must not silently acquire
cross-provider failover. Existing strict-account semantics remain intact.

## Identity and candidate qualification

Add a curated canonical-model/variant mapping only where equality is defensible.
Do not deduplicate by display label, a slug prefix, manufacturer or modelFamily.
Version/date, quantization, fine-tune, distillation, endpoint product and vendor
modifications may make apparently similar model names different variants.

Each eligible access retains actual provider/model/route/account, product/region,
cost policy, known context/output/tools/modalities and transport provenance.
Same model does not imply identical tool behavior or continuation compatibility.
Unknown mapping leaves routes separate; uncertain access is visible as unqualified
rather than becoming an automatic candidate. Live provider model lists do not
prove exact equivalence or paid entitlement.

The requested logical model/access selector and selected physical route must be
recorded separately in attempts/Inspector. Preserve stable aliases and historical
IDs. Merely hiding redundant menu entries must not disable inference routes.

## Selection, limits and continuation

Deterministic access policy first: candidates that meet explicit data/account/
cost/tool/context requirements, then applicable quota/429 cooldown evidence, then
the configured order or separately agreed allocation strategy. Unknown is not
healthy or exhausted. Shared budgets are counted once. The mere presence of a
paid OpenRouter key does not make all paid routes authorized fallback targets.

Selection for a new conversation stays bound. Existing continuation and limited
migration guarantees in [the routing contract](../collection-router-v1.md) remain.
Cross-provider tool/reasoning/cache/reference state is not inherently portable.
If qualified complete-history replay is unavailable, request a new conversation
instead of switching invisibly or stripping signed/opaque records.

No valid access remains: explain known reasons and next reset/check action;
never substitute another model or enable another paid tier on the user's behalf.
The exact multi-access preference/unknown policy and account inclusion controls
are still unresolved.

## Delivery and review

This proposal potentially changes the previously agreed provider-first flat
menu, but does not change active configuration on its own. Evolve picker schema
and qualified clients together. Older clients need a truthful supported flat
projection or an explicit unsupported result, not silently missing models.

Menu layout preferences and semantic automatic targets are distinct settings.
Automatic targets require compiled current runtime proof; menu entries are not
executable merely because generated JSON includes them. Preserve the unresolved
Auto-Sync/Apply decision described in [the feature register](../feature-contracts.md).

Proposed bounded slices: qualify canonical access mapping for one multi-provider
model; extend client/export navigation; add explicit exact-model access selector;
then integrate permitted quota distribution and broaden mappings. An interim
flat manufacturer collection remains usable, without claiming this full design.

## Proposed acceptance, not executed

- Three views refer to the same routes; no usage duplication or new credentials.
- Hundreds of routes remain searchable/navigable without creating hundreds of
  flat top-level groups. Multi-account labels remain distinct.
- Different model versions/variants never merge solely by name.
- Auto access preserves the canonical chosen model and reports actual provider;
  Auto model is visibly different and follows its configured group policy.
- Concrete provider/account selection stays strict. Exhaustion does not grant
  paid fallback or model substitution. Running tool continuations stay valid
  or fail with a clear supported recovery action.
- New menu generation and applied runtime match; incompatible client schema is
  detected. No migration/Apply of unrelated pending changes.
