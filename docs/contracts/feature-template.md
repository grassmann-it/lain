# <Feature name>: behavior contract

Status: <proposed / accepted scope with unresolved details / accepted>.
Source: <user decision, agreed specification or linked issue; date>.
Scope: <affected modes, provider/products, accounts, clients and platforms>.
Contract revision: <only if needed>; change reason: <why behavior changes>.

This template is for authoring; replace placeholders and omit irrelevant sections.
Do not invent decisions, requirements, thresholds or test results to fill fields.

## User outcome and scope

<The task the user can complete, and the important boundaries.>

## Existing contracts and behavioral delta

<Relevant FC handles and authoritative sections. What stays valid, what changes,
which existing accepted decision would require a deliberate revision?>

## Observable behavior

- Trigger, prerequisites and inputs: <...>
- Successful result and user feedback: <...>
- State and dependencies affected: <identities, profile/group references,
  secrets, runtime/client artifacts, history; only what matters here>
- Source/uncertainty semantics: <observed versus inferred, null, stale,
  unsupported, partial reads, exact scopes; where relevant>
- Automatic versus explicit actions: <scope of prior user settings/authorization>

## Failure, concurrency and recovery

<Important cancellation, stale revision, duplicate/retry, partial failure,
restart and running-request cases. State the actual transaction boundary and
how any effects outside it are reconciled; do not assume cross-process atomicity.>

## Acceptance criteria

| Case | Setup and action | Observable expected result |
| --- | --- | --- |
| Normal workflow | <...> | <...> |
| Relevant failure or boundary | <...> | <...> |
| Affected existing guarantee | <...> | <...> |

## Implementation and evidence — separate from requirements

Implementation reference: <commit/WIP, paths, known gaps or not implemented>.

| Criterion | Result | Evidence and limits |
| --- | --- | --- |
| <case> | <satisfied / partial / not satisfied / not evaluated> | <test/source, date, exact artifact/client/provider scope where relevant> |

Do not conflate source review, synthetic tests, native client acceptance,
authenticated provider qualification, installation and public delivery. Include
only the evidence levels applicable to the claim. Changed behavior invalidates
only the evidence that depended on it; identify the focused requalification.

## Open decisions and alternatives

<Decision, which work it blocks, recommended option with reason, and meaningful
alternative/tradeoff. Omit if none. An accepted outcome does not approve an
unresolved implementation policy.>
