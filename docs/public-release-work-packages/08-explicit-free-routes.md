# PUB-08 · Explicit free-model worker routing without inferred strict mode

Owner: worker/cost-policy UX and skill owner; shared DTO/entrypoint edits through00.
Priority: confirmed correction after the user rejected the unnecessary extra gate.
Current source and installed global usage-skill instructions already clarified;
that is not a real worker execution/provider result.

## Verified starting point

At read: applied openrouter/free alias has costPolicy configured. `providers
check-free` deliberately sends a free_only override and returns the quoted
free_only_not_documented/upstream_cost_controls_unavailable for that model.
Static optional enforcement currently qualifies three exact free models; Llama/
Qwen check allowed true. That qualification is neither actual endpoint availability
nor evidence of a fresh zero-cap key. Existing located worker project already
uses configured. No budgets, routes, fallback or provider calls were changed.

## Outcome and proposed scope

Explicit user selection of openrouter/free uses that route's configured policy
for ordinary workers. Do not infer strict free_only from the word free or turn
check-free into an availability veto. Keep requested model and no-paid-substitution
intent, preserve actual provider limits and explicitly enabled route-level guards.
No permission to substitute a paid model or change a key spending cap.

Own skills/lain-workers and lain-usage guidance, scoped worker-route policy
presentation and tests; optional strict-check DTO/presentation changes through00.
Existing WorkerPlanningService validates actual explicitly selected policy; don't
relax all existing strict policies globally as a shortcut. Applied route guards
and upstream/provider enforcement remain separate from agent instructions.

## Tasks

- Test configured worker selection across the real named free-router identity;
  no implicit free_only override or static three-name allowlist on normal routing.
- Make optional qualification output distinguish actual applied policy, requested
  stricter policy, model price classification and actual availability/evidence.
  Compatibility for existing consumers; don't misreport a strict-check failure
  as provider unavailable.
- Keep explicit free_only backward-compatible for users who request it; when this
  user rejects the extra project mode, only authorized preferences change to
  configured and exact routes/defaults/other fields remain intact.
- Update bundled/global/project skill installation workflow appropriately; source
  edits alone don't update old embedded CLI skills or cached running chats. Preserve
  user edits and validate a freshly loaded skill/setup using the revised rule.
- Any wider strict-mode generic-free qualification is separate source research/
  runtime proof, not required just to honor ordinary configured explicit routing.

## Acceptance

Synthetic configured openrouter/free worker plan isn't vetoed by optional
qualification; explicit strict request still respects its own checks. No paid
substitution, key-cap mutation or implicit fallback. Existing budget/auth/account/
revision/continuation checks unaffected; unsupported capability failures remain
honest separate blockers, not mislabeled free-policy problems.

Return exact policy/CLI/skill and route/evidence differences, fixtures and remaining
provider verification limits. A real free call is separately scoped when requested;
this plan starts no request and doesn't claim free-only hard spend certification.
