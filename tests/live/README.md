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
