# Bounded real reference probes

## Per-model realistic request qualification (2026-10-26)

`realistic_models.py` prepares a frozen plan from the **existing applied** Lain
profile, public model inventory and active Codex catalog. Preparation sends no
inference. Execution uses a local in-memory inference credential from
`lain auth token --audience codex`, pins each existing account and sends at most
two HTTP requests per concrete alias. Routers are excluded, equal upstream names
on distinct providers remain distinct. It performs no login, configuration write,
profile application, route substitution or harness retry. Provider/gateway
internal attempts are not established by counting client calls.

New schema-2 plans use `auto-v2`: seven captured Codex 0.164.0 native function
schemas, 22,638 bytes of synthetic invoice-project context, streaming, a fixed
file read and `tool_choice:auto` on both requests. The output budget remains
2,048. Optional `--tool-mode forced-exec-v2` explicitly chooses forced first
tool/none continuation. These modes do **not** reproduce native omitted output
budgets, multiagent namespace/custom tools, MCP, plugins, the complete VS Code
environment or maximum conversation length.

```sh
python3 -B tests/live/realistic_models.py --output /absolute/private/new-run
# Review the plan; execute only within the user's authorized inference scope.
flock /home/tomka/.cache/lain-verification-lane.lock \
  python3 -B tests/live/realistic_models.py --output /absolute/private/new-run \
  --execute --expect-plan-sha256 EXACT_PRINTED_PLAN_SHA --workers 1
```

For a distinct bounded regression scenario, preparation accepts repeatable
`--only-alias EXACT_ALIAS` after validating the complete inventory. It never
substitutes another provider's route. The plan freezes the payload mode, initial
and static continuation templates, quality criteria and harness source SHA.
Old or drifting plans fail before auth/inference; preserve historical evidence
and prepare a new output directory.

The harness executes only `/usr/bin/cat` on its own fixture, never model-supplied
shell text. New v2 execution accepts exact command/workdir with bounded neutral
native metadata (`login:false`, `tty:false`, default sandbox, bounded output and
yield settings); privilege/shell/command changes are rejected. Legacy helper
calls remain strict. `tool_arguments_outside_fixture` is inconclusive about
route compatibility. Structured answer checks exercise the proposed quantity
guard on positive, zero, negative, boolean, float and nonnumeric boundary cases
using a restricted AST interpreter; arbitrary model code is never executed.
Passing this small task is not broad quality certification.
At most one request runs per provider. An output-directory `stop` file cancels
pending cases. Existing reports cannot be overwritten/retried; review failures
before authorizing a distinct follow-up scenario. Exit zero means the batch
finished, **not** that every row passed. Read `counts`, `rows` and binding status.

`model_boundaries.py` separately prepares four bounded diagnostic scenarios:
Cohere with `auto` and omitted output budget; Kimi k3 with `auto`; Beam with
exactly 128 and 129 top-level definitions. Beam's extra definitions are labeled
synthetic unused functions. At most six client requests are permitted; the
observed run used five. This does not load the user's actual plugins.

```sh
python3 -B tests/live/model_boundaries.py --source-run /absolute/private/completed-run \
  --output /absolute/private/new-diagnostics
flock /home/tomka/.cache/lain-verification-lane.lock \
  python3 -B tests/live/model_boundaries.py --output /absolute/private/new-diagnostics \
  --execute --expect-plan-sha256 EXACT_PRINTED_DIAGNOSTIC_PLAN_SHA
```

Diagnostics send explicit conversation/turn headers for independent public
`lain attempts` correlation. Bounded error fields are redacted; tokens, raw
answers and provider credentials are never saved. The original sweep saved
only hashes of error bodies and has no explicit conversation correlation.
Archived executed source pins in private evidence distinguish it from later
diagnostic improvements. Shared results and limits:
[report](../../docs/model-live-qualification-2026-10-26.md),
[machine-readable summary](reports/realistic-models-20261026.json).
That historical 59-alias sweep used forced choice and a marker heuristic;
its reports are not regraded with v2. The [follow-up report](../../docs/provider-tool-context-repair-2026-10-26.md)
and [summary](reports/provider-tools-context-20261026.json) document native-auto
quality checks, independent public attempts correlation and compact native context.

`cohere_candidate.py` separately exercises an owned candidate Core against the
existing Cohere route, through a loopback forwarder holding only an in-memory
local inference token. No provider credential reads or production restarts.
At most two existing-gateway calls; auto/parallel defaults and omitted output
budget are checked at the translated wire. New plans freeze all harness source
dependencies and continuation templates. The completed technical roundtrip
failed semantic quality; do not rerun it just to obtain a green result.

Focused offline checks:
`python3 -B -m unittest discover -s tests/live -p 'test_*.py' -v`.

## Earlier reference probes

These probes are prepared for the user-authorized minimal live check. Execution
waits for W1/W2 synthetic qualification of an exact binary SHA; the real probes
then contribute to final A qualification. The default command only prints a four-call
plan and opens no sockets:

```sh
python3 tests/live/reference_probe.py
python3 -B -m unittest discover -s tests/live -v
```

The approved calls are two Responses SSE requests each through own
`127.0.0.1:18417`, selecting Codex `gpt-6-luna` and Command Code
`cmd-gpt-6-luna`. The first request forces exactly one local `echo_probe`
function call with fixed argument `ok`. The second replays the response and
returns the literal result `ok`, requesting a fixed short answer. The harness
does not execute generated code or invoke external tools. It makes at most
four inference requests, with no retries or automatic alternate routes.

Before executing, the integrator must import through the host and apply an
isolated profile containing strict single-account routes for those models.
The default Codex public alias is `live-codex-gpt-6-luna`; use `--codex-route`
if the created alias differs. The Command route's imported account must use
the source `codex-api-key` entry at
`https://api.commandcode.ai/provider/v1`, whose configured model name is
`gpt-6-luna` and alias is `cmd-gpt-6-luna`. Do not use legacy proxy ports,
queues, management endpoints or credential files for inference.

After W1/W2 confirms the synthetic exact-SHA gate and the isolated own host is ready:

```sh
python3 tests/live/reference_probe.py --execute \
  --lain-home /absolute/path/to/isolated-lain-state \
  --proxy-binary /absolute/path/to/qualified/lain-proxy
```

Execution requires a sibling `qualification.json` with `syntheticPassed: true`
(or a final `passed: true`), matching both actual binary SHA-256 and
`binary-manifest.json`. The provisional gate permits only this bounded reference
run in an isolated temporary `LAIN_HOME`; it does not qualify production install.
It verifies own host readiness and
applied revision, strict enabled imported account bindings, distinct accounts,
and unexpired Codex `AccountDto.expiresAt`. It reads only own host local token
and requests own inference authorization from `/api/v1/auth/token`; provider
credentials remain behind the imported host account. Requests include explicit
`X-Lain-Account-ID`. All HTTP endpoints are fixed loopback URLs, redirects and
environment proxies are disabled, and stream/response sizes are bounded.

The JSON result contains selected own route/account IDs, counts, state and
numeric usage only. Provider response bodies, prompts, tool arguments,
credentials, response IDs and arbitrary upstream errors are not printed. A
failure stops immediately; do not retry without reviewing the actual call
count against the authorized live budget. Usage durability/account association
must independently be checked through the host Usage API by the integrator.

`inspect_sources.py` is a separate read-only metadata inspection helper, using
the existing Python `yaml` package. It never contacts any service or imports
credentials. On 2026-10-23 the existing snapshot
`~/.local/share/easycliproxyapi/oauth/codex-existing.json` was enabled, had
account identity and an access token, and reported expiry
`2026-10-29T07:55:58+01:00`. Its decoded JWT exp was
`2026-10-29T06:55:59Z`. This is only a local expiry/format check, not proof that
the provider still accepts the token. No refresh/revoke call is permitted.
The legacy config had no Codex OAuth alias mapping; the integrator may need to
create the approved direct Codex route explicitly after checking Core catalog.

## Complete orchestration

`orchestrate_reference.py` prepares own temporary state, imports through the
host API, creates the Codex strict route and two-route profile, applies it,
runs exactly the same four requests, independently queries Usage until four
terminal attempts exist (two per correct account/route), then stops and restarts
the own host with unchanged state to verify totals and attempt identity persist.
All temporary records and `sanitized-report.json` remain under the reported
private `/tmp/lain-reference-*` directory. Its default invocation prints a plan
without starting processes or opening sockets.

Only after root confirms the exact W2/W11 synthetic-qualified SHA, with both
18417 and 18418 free and the current main Host DLL already built:

```sh
python3 tests/live/orchestrate_reference.py --execute \
  --approved-sha256 EXACT_SHA_CONFIRMED_BY_ROOT \
  --host-dll /absolute/path/to/Lain.Host.dll \
  --cli-dll /absolute/path/to/Lain.Cli.dll \
  --proxy-binary /absolute/path/to/qualified/lain-proxy
```

The orchestrator never creates or edits qualification evidence. The host gets
`LAIN_REFERENCE_QUALIFICATION=1` and `LAIN_REFERENCE_DISABLE_QUOTA=1`; current
host support for the latter is required to suppress background real quota
requests, especially at restart. These reference overrides must be restricted
to isolated temporary state. The script fingerprints legacy files read-only
before and after, reporting counts only; it never signals a discovered port
owner or touches the legacy process/queue. Only its own launched host/session
is cleaned up. A failure preserves records and stops without inference retries.

The optional `--cli-dll` additionally runs the real CLI usage command before
and after host restart. API and CLI receive the same fixed `from`, `to` and
`timeZone=UTC` window (`--timezone UTC` is the actual CLI flag). Totals and
daily/account/model groups must match exactly, including unknown JSON null
values. The CLI uses the same private `LAIN_HOME` and Host API; no second
collector or direct database access is introduced. This is also the command
documented by `skills/lain-usage/SKILL.md`.

## Explicit follow-up diagnostics

`diagnose_reference.py` is separately authorized work, not an automatic retry.
It sends one tiny nonstream Command request and optionally queries on-demand
Command quota through the Host adapter. Its `--verify-only` mode sends zero
inference/quota requests and verifies preserved records, restart and real
CLI/API parity. `command_glm_reference.py` explicitly selects the already
imported `cmd-glm-5.3` route on the same account for at most two stream/tool
requests, stopping after the first failure. It is not an automatic fallback.

Observed follow-up evidence is in `reports/diagnostic-20261023.json`. Command
returned HTML Cloudflare 1010 / Access denied, while account and rolling-window
balances were positive. The Core normalizes 403 to `insufficient_quota`; this
code alone must not be treated as actual budget exhaustion. HTML classification
reports only booleans and known title classes. Raw bodies and arbitrary titles
are omitted; exact own credential values and identities are redacted from error
fields. Two successful Codex calls and two failed Command GPT calls were verified
unchanged across own Host restart, with exact CLI/API totals and groups matching.
The later GLM first request also failed at Cloudflare and its continuation was
not sent. This evidence does not mark final A qualification passed.

The separately approved final pair corrected the diagnostic client's own
application identification to truthful `User-Agent: lain-reference-probe/0.1.0`.
The source and own Command transport had `disable-codex-cloaking: true`, which
preserved the earlier Python client's User-Agent upstream. No provider/Core or
production configuration changed. Both Command GLM streaming/tool requests then
passed (245 and 112 tokens), as recorded in
`reports/identified-client-20261023.json`. All seven terminal records, including
the three historical failed attempts with unknown usage, survived restart;
CLI/API totals and groups matched exactly before and after. The corrected-client
pair did not retest Command GPT. Earlier evidence remains preserved. The probe
itself does not award final product qualification.
