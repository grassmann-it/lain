# Backend qualification A

Recorded 23 October 2026. **Synthetic Core verification passes all 15 checks.**
Real provider acceptance and full approval of A follow separately; this report
does not claim live calls had already occurred at that stage.

## Verified state and repetition

The Lain launcher was checked on the manifested snapshot
`7.3.17-parent-request.3-model-compat.3-startup-readiness.1`, including lain
extensions. Final Core SHA-256:

`8edf8abf666e3c15f1a04c20c841a969d35229f09e7de5265755ab704cf9dfc0`

W1 confirms this build as the frozen consolidated state of commits `6292b90`
and `50b095f`. Source/toolchain provenance is in `proxy/source-manifest.json`,
`proxy/baseline.json`, `proxy/toolchain.json`, and the generated artifact manifest.
Builder verification precedes compilation; the test report does not replace
provenance verification.

The [machine-readable report](../tests/backend/fixtures/qualification-result.json)
and [actually observed sanitized events](../tests/backend/fixtures/event-examples.json)
are versioned. Examples include success, two accounts, 429/retry, cancellation,
and visible gaps. They are excerpts from different sources, not a contiguous
cursor batch for ingestion. `--batch-fixture` exports a complete contiguous batch.

```sh
./scripts/build-core.sh
python3 -m unittest discover -s tests/backend -p 'test_*.py' -v
python3 tests/backend/qualify.py --core artifacts/proxy/lain-proxy \
  --report /tmp/lain-qualification.json \
  --examples /tmp/lain-event-examples.json \
  --batch-fixture /tmp/lain-source-batch.json \
  --qualified-artifact /tmp/lain-qualified-core
```

The runner freezes binary bytes once so concurrent rebuilding cannot produce a
mixed report. After a fully passing run it can retain that exact artifact for
independent acceptance/live probes. In the executed run it is at
`/tmp/lain-w2-qualified-final15`; its subsequently verified hash matches the
report. A different rebuilt hash requires its own run. Instructions and the
reusable `Run` structure are in [tests/backend/README.md](../tests/backend/README.md).

## Isolation and protocol scope

Python is only a test tool. Every case uses private temporary YAML/binding/auth/
journal files, its own child processes, random loopback ports, and separate
synthetic provider/inference/management keys. Core inherits no provider credentials
or proxy/helper configuration from the shell. Readiness is checked against its
own journal source ID; a port conflict must not accept another process as the
test instance. Cleanup stops only its own children and removes only its own
temporary storage. The existing proxy and its consuming queue are not contacted.

Verified clients are **OpenAI Chat Completions**, **OpenAI Responses SSE** through
the Chat Completions upstream adapter, and **Anthropic Messages** with native
Messages upstream. All tool probes use only `fixture_echo` with literal `safe`.
This does not prove actual Codex OAuth/Command Code transport, live
Responses-to-Claude translation, or an actual harness run. WebSocket was not tested.

## Actually passed checks

| Check | Observation |
| --- | --- |
| Two accounts, same model alias | 100 requests per distinct synthetic credential; 200 logical dispatch requests, 200 attempts, 400 events, 1,800 reported tokens per account |
| Replay, pagination, restart | Repeated and paginated retrieval returns the same events; source/event/request/attempt identities and numbers remain unchanged after Core restart |
| Chat Completions SSE and tools | Content, tool name/arguments, result, and continuation response pass through Core; reported usage is durable; finish/usage frames retain order |
| Responses SSE and tools | Text/function-call events, `call_id`, arguments, explicit tool result, and response are translated through Chat upstream; reported usage is durable |
| Anthropic Messages | Two distinct API credentials sharing an alias remain strictly attributed; native streams, tool JSON, `tool_result`, and continuation work; four attempts are durably attributed |
| Unknown or explicit zero | Missing cache/reasoning fields and total remain JSON `null`; explicitly reported zero values remain `0` and `usageReported=true` |
| Strict account and 429 | Request stays on A; no request to B; error usage has unknown token count and status 429 |
| Explicit pool and retry | At least one logical request has separate A/429 and B/success attempts with the same request ID and different attempt IDs |
| Key rotation and recreation | New credential ID stays bound to the same stable account; removal/recreation with a new account ID does not rename old events |
| Disable and recover | Removing A from the configuration snapshot while retaining the route gives strict 503 with no upstream substitution; explicit pool uses B; re-enabling restores A |
| Client cancellation | Terminal state `canceled`, unknown tokens without provider usage, and observable upstream stream cancellation |
| SIGKILL during stream | Attempt registered durably before first output survives; restart invents no terminal usage; a later request works with a new identity |
| Journal and startup failures | Second writer, non-loopback configuration, and two account names for one credential are rejected; file-size limit blocks dispatch with 503; partial append is rejected unchanged; explicit recovery works |
| Terminal write failure | Chat and Responses hold successful completion markers until usage is durable; write failure suppresses success, leaves exactly one visible open attempt, and blocks further requests with 503 |

The four fixture self-tests passed separately. They check fake-provider reliability;
only the 15 integrated checks above use Core.

Local rejection before dispatch creates no upstream attempt. The disable test
therefore reports three actually dispatched requests and one additional locally
rejected HTTP request. This evidence does not claim complete access counting
of all rejected HTTP requests.

## Failure boundary and remaining acceptance

A hard process termination cannot recreate already consumed provider usage.
The measured state was a durable `attempt_started` without terminal usage event.
The attempt and account remain reconstructible; actual consumed token count
remains unknown. A terminal journal failure produces the same visible state;
after a partial append, Core refuses restart until storage is intentionally
repaired. The suite truncates no journal with dispatched work. Explicit recovery
restores only the demonstrably unused test journal.

The runner qualifies durable source integration, not the later SQLite transaction.
W4 ingested the complete 400-event batch of the earlier passing normal path on
SHA `683c6105…` through unchanged `SqliteUsageStore`. Ten replay/reopen cycles
confirmed exactly 200 requests, 200 attempts, 3,600 tokens, two accounts, and
unknown cache/reasoning fields. Final-hash checks and consumer failure cases
are recorded in independent integration evidence.

Supplementary evidence is now in [acceptance](acceptance.md): real Codex Responses
and Command GLM Responses probes with harmless tool continuation, actual Codex
profile integration, and independent before/after evidence for the existing
service and its durable files. Final Core is qualified through
`proxy/qualification.json`. Incomplete attempts remain visible with unknown usage
as contracted; hard termination cannot reconstruct unreported consumption.
Synthetic key rotation does not prove real OAuth renewal. Successful reference
probes do not automatically qualify every imported model.
