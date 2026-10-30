# Explicit OpenRouter Free selection and worker policy clarification

30 October 2026 · Workflow instruction correction plus read-only diagnosis.
No live inference test, provider cap mutation, paid substitute, route Apply or
production binary replacement.

## Report and actual observations

A different Codex task reported `free_only_not_documented` and
`upstream_cost_controls_unavailable` for OpenRouter Free and declined to use it
for workers. The user explicitly rejects introducing that extra mode when asking
for a concrete free endpoint such as `openrouter/free`.

Read-only checks against the running Host at desired919/applied917:

| Target | Applied policy | Optional check-free override result |
| --- | --- | --- |
| openrouter/free | configured | allowed=false, the two reported codes |
| meta-llama/llama-3.3-70b-instruct:free | configured | allowed=true, no reasons |
| qwen/qwen3-coder:free | configured | allowed=true, no reasons |

`providers check-free` requests `free_only` explicitly; it is not an availability
probe or a fact that the configured route is blocked. Current narrow qualification
supports three exact models and excludes the general free router. Runtime routes
in configured mode don't invoke that extra guard. A successful strict qualification
also is not proof of a current zero-cap key or a successful provider request.

Public official sources checked separately (no API key):
- https://openrouter.ai/docs/guides/routing/model-variants/free
- https://openrouter.ai/openrouter/free

The documentation describes using openrouter/free on its own as free-only model
fallback and lists zero prompt/completion-token pricing. This is provider behavior/
pricing documentation, not the optional local enforcement certification. Do not
add paid alternatives in the request's model list or substitute a paid route.

## Applied instruction correction

Updated:
- Source `skills/lain-usage/SKILL.md` and `skills/lain-workers/SKILL.md`.
- Project AGENTS.md explicit operator preference.
- Installed global `~/.agents/skills/lain-usage/SKILL.md`.
- Worker contract clarification in docs/workers-v1.md.

Ordinary explicitly chosen worker routes use `costPolicy: configured`; the word
free or a model's name does not imply a request for Lain's extra free_only mode.
Do not use check-free as a mandatory availability gate. Preserve requested model,
existing provider budgets and no-paid-substitution intent. Strict mode remains
optional/backward-compatible only when explicitly requested; actual explicitly
enabled route guards are not silently disabled.

The one located saved worker policy already says configured and was not edited.
No existing spending limits, provider keys, routes, fallback chains or task IDs
changed. Old embedded CLI skill resources and already cached chat instructions
are not magically upgraded by source edits; a caller must read the revised rule
or receive the user's explicit instruction. The installed global usage skill is
updated now. Real worker/provider acceptance and unrelated runtime/reasoning/
capability gates have not been tested by these instruction edits.

## Follow-up work

[PUB-08](public-release-work-packages/08-explicit-free-routes.md) plans optional
qualification-vs-applied-policy presentation and worker-route regression coverage.
No source evidence here justifies claiming a successful live request, relaxing
all existing explicit strict guards or changing key caps to pass a test.
