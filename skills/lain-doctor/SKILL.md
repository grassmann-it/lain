---
name: lain-doctor
description: Diagnose concrete lain/Codex errors through public doctor findings and bounded request history, and carry out requested targeted repairs through supported management paths. Use for lain connection, reasoning, helper, tool/context, authentication or response failures.
---

# lain:doctor

Use the installed public `lain` CLI to explain a concrete failure and, when
requested, repair its demonstrated cause. This standalone skill requires no
other skill. Source code, a candidate package, the installed CLI/Host, exported
artifacts and the active client/thread can all have different versions.

## Establish the observed failure

Preserve the user's exact error, approximate time/timezone, intended model and
effort, client (`codex-cli` or `codex-vscode`), project and profile when known.
Use existing context; ask only for missing information that changes the next
step. Distinguish diagnosis-only from an already-authorized concrete repair.

Read `lain --help` for installed capabilities. Prefer the installed executable
(normally `~/.local/bin/lain`); respect an explicitly selected `LAIN_HOME`.
Do not build or run a checkout to make an unavailable installed command appear
available. Missing `doctor` is an installed-version limitation, not proof that
the connection is broken. Report the version gap and use supported public
read operations for partial evidence; do not install/update automatically.

Primary read-only interface, subject to installed help:

```sh
lain doctor --json
lain doctor --target codex-cli --project /absolute/project --profile PROFILE_ID --json
lain doctor --target codex-vscode --project /absolute/project --json
```

Use the actual target and omit unknown optional scope; never invent a workspace
or profile. Consume JSON-v1 `schemaVersion` and findings with `code`, state
`ok|warning|blocked|unknown`, explanation, provenance and next step. Preserve
unknowns and observation time. Unsupported schema/target is a diagnostic limit;
do not reinterpret it as a passing check. Suggestions are data, not commands to
execute blindly. CLI exit status describes command/transport success, while
finding states describe health. Host unavailability can coexist with useful
local findings. Use `lain status --json`, `lain mode --json` or
`lain service status --json` only where needed and supported. Monitor intent is
not a failed Gateway; do not switch modes as a diagnostic action.

Separate Host reachability, account setup, desired/applied revision, installed
export, effective client/workspace binding and active thread snapshot. A valid
export or selected VSIX does not prove a running thread uses it; extension
activation after reload may remain unknown. Inspect only allowlisted nonsecret
binding/version fields through public diagnostics or supported client inspection.
Do not dump configurations, secrets, prompts or raw inspector payloads.

## Correlate only the relevant requests

If doctor cannot explain a request failure, query public attempts/history with
an explicit narrow time interval and available conversation/turn, account,
model, harness or profile identifiers. Verify flags in installed help. Example
shape (replace every placeholder with observed values):

```sh
lain attempts --from START_ISO --to END_ISO --timezone UTC --conversation CONVERSATION_ID --turn TURN_ID --offset 0 --limit 20 --json
lain history --from START_ISO --to END_ISO --timezone UTC --conversation CONVERSATION_ID --json
```

Start with a window around the reported event (normally ±5 minutes). At most
three pages of 20 attempts in the initial pass; stop once the relevant request
is identified. Widen once only when a known clock/identifier mismatch justifies
it, recording the new scope. If no reliable event time exists, obtain it before
collecting broad history. Record truncation, missing retention and correlation
gaps; absence from a page is not proof no request happened. Do not compute
aggregate totals from sampled pages. Summarize request IDs, timestamps, routes,
requested/effective effort, status and sanitized error categories, not payloads.
Never read SQLite, credential stores, usage queues or entire logs, start another
collector, or probe providers to fill an evidence gap.

## Interpret the concrete error

| Evidence | Interpretation and targeted next step |
| --- | --- |
| Rejected `none`, including Astra | Compare explicit thread effort, model capability, applied profile and effective catalog. `none` is a specific value, not “unknown.” Preserve a valid explicit choice. A verified stale export may be regenerated through its supported public path; changing an unsupported explicit choice requires an authorized valid replacement, never an automatic downgrade. |
| Auto alias with an empty effort list | Empty/unknown support does not establish `none` support or provider acceptance. Use only qualified common capabilities of its routes. A Source export fix is not evidence the installed export or live router forwards effort correctly. |
| Missing/wrong auth helper | Establish the exact executable path and qualified installed artifact/binding. Use supported client maintenance or profile installation when requested and appropriate; never retrieve a token to test the helper or substitute an arbitrary executable. |
| Wrong provider/catalog/workspace or old thread | Compare the actual launch/project/profile and active binding against the intended target. Repair only the identified binding through supported operations; preserve unrelated ordinary Codex settings. A corrected file does not change an existing thread snapshot. |
| “128 tools” or tools too large | Distinguish provider top-level tool-definition count from namespace members, callable functions and serialized bytes/token cost. A namespace with 160 members is not automatically 160 top-level definitions. Historical 128-accepted/129-rejected Beam evidence is route-specific. Without the actual request metadata, count and cause remain unknown. |
| Context exceeded | Separate input history, tool schemas, output reservation and actual model/route token limit. Tool count is not token context; do not infer tokens from bytes or advertised context alone. Offer a supported narrower context/tool mode only within the requested scope; no silent truncation or lost project instructions. |
| Auth error versus HTTP 429 | Correlate which layer rejected the request: local management authorization, helper/binding or upstream credentials. 401/403 need their actual error meaning; 429 may be quota, rate or concurrency and is not proof of expired login. Use cached quota/reset evidence where available; unknown is not zero. Never refresh/login or rotate accounts to diagnose. |
| HTTP 200 but wrong/unusable answer | Transport success does not establish semantic correctness, valid tool execution or completion. Use the reported expected/actual result and existing correlated evidence. Classify adapter/protocol versus task-quality failure only where supported; do not claim a repair from a successful status code. |

Unknown provider support remains `unknown`, including when local configuration
looks valid. Do not turn old qualification reports into a current live guarantee.

## Carry out a requested repair

Diagnosis alone performs no mutation, refresh, inference, login, profile apply,
service start/restart, client turn, IDE reload or package installation. An existing
concrete repair request authorizes its necessary supported steps; do not add a
second blanket confirmation gate. Keep within its target, model/effort, files
and operational scope. If a materially different choice is required, present
the specific missing choice rather than guessing.

Read current state/revision, select the smallest supported correction, explain
its intended effect, then execute through installed public CLI/UI/client
maintenance. Examples available in some versions:

```sh
lain profiles preview PROFILE_ID --revision N --json
lain profiles install-codex PROFILE_ID --revision N --json
lain profiles apply PROFILE_ID --revision N --json
```

These are distinct operations: preview reads, install updates separate client
artifacts, apply changes Host routing. Do not perform all three by rote. Verify
the installed help contract and preview the intended profile before a requested
export/apply. For helper repair, default binding or VS Code maintenance, discover
the supported public maintenance path and its qualification first; do not invent
a `doctor --fix` command or patch production files/scripts from a checkout.
If no public repair exists, state the exact limitation and provide a development
finding: versions, target, sanitized error/finding code, evidence, expected versus
actual behavior and missing supported operation. Do not claim “fixed.”

On revision conflict, re-read and reassess before one revised action; a changed
scope or repeated conflict ends the attempt with the remaining blocker. After a
successful targeted change, rerun the same scoped doctor/read checks once. Stop
if the cause persists or evidence contradicts the hypothesis; report it rather
than looping through guessed fixes. No unchanged request retries, longer-timeout
workarounds, silent model/account/provider fallback, or global effort rewriting.

A necessary visible provider grant uses the existing private login/UI/TTY path
and remains with the user. Never ask for credentials in chat, print
`lain auth token`, or refresh/revoke imported external OAuth snapshots. A repair
request does not itself authorize a live inference probe. Only when that probe
is explicitly in scope, use its concrete request/count/cost budget and stop at
that bound; otherwise leave semantic/provider verification unobserved. A pending
IDE reload or new-thread activation must be reported, not performed implicitly.

## Report the outcome

State the diagnosed cause and evidence, executed correction (or diagnosis-only),
post-check result and remaining unknowns. Name installed versus Source/candidate
versions separately. Include only relevant sanitized findings and request IDs.
A passing local doctor is a local/configuration result, not proof of provider
acceptance or answer quality. Leave an actionable next step when blocked.
