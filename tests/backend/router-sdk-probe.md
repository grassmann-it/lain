# Real SDK router transport probe

`router_sdk_probe.py` is a source-only qualification harness. Its existence does
not establish a successful run or qualify a release. It uses the existing
`qualify.Run` child-process/journal fixtures and an isolated loopback provider.
It neither builds code nor starts an installed service.

Run against the exact candidate binary, after its build has finished:

```sh
python3 tests/backend/router_sdk_probe.py \
  --core /absolute/path/to/candidate/lain-proxy \
  --evidence-dir /absolute/path/to/new/router-sdk-evidence
```

The evidence directory must not exist. The script snapshots and hashes the
supplied executable before starting any child, and reports its SHA256 in
`summary.json`. Each scenario gets a fresh private runtime, synthetic config,
two synthetic credentials, two distinct upstream models and a new loopback
server. HOME/XDG paths are private; child environment excludes inherited proxy
settings and credentials. Parent HTTP requests explicitly disable proxies.
Only child PIDs created by the harness are terminated. No user config, account
store or existing Core is opened.

Three independent scenarios (`text`, `rate_limit`, `partial_sse`) traverse the
actual supplied Core's Responses endpoint, SDK account selector/executor,
Responses-to-Chat translator and final HTTP transport. They check:

- The first of two usable routes is selected and durably pinned.
- Restart with reversed route order preserves the same thread's real model
  and account; a new thread selects the newly first route.
- An upstream 429 does not switch to the second account. Same-account retries
  are permitted and remain visible in the evidence.
- A streamed text delta followed by a deliberately truncated HTTP chunk reaches
  the client as partial output, never produces successful `response.completed`,
  records a failed durable completion, and never switches accounts.
- An unpinned continuation returns 409 without pin, journal or upstream changes.
- Actual fake-provider account/model observations match the journal's real
  RouteID/AccountID/CredentialID, while RequestedModel retains the router alias.

Each scenario JSON contains response evidence, fake-provider observations,
complete durable events, pin metadata and the synthetic child log tail, including
on failure where available. A failed assertion exits nonzero after writing the
report. Pins contain no prompt, tool or response content. The test evidence does
contain synthetic prompt/response strings and fixed fake credentials may appear
in its log tail; it is not production telemetry.

This probe intentionally does not test live providers, Free enforcement, Host
compilation or the native Codex picker. Existing Free guard and Host/native
qualification gates remain necessary. The 429 follow-up includes synthetic
assistant history to exercise the already-pinned continuation path; it does not
claim that the failed 429 itself generated an assistant response.
