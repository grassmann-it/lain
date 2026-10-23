# Bounded real reference probes

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
