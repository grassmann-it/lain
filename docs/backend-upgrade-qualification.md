# CLIProxyAPI v8 upgrade: qualification boundary

The acceptance boundary remains the existing lain launcher CLI and `/lain/*`
wire contract. A new upstream release, unchanged dependency bytes, a successful
build, or a catalogue entry does not replace behavioral evidence. Qualification
uses only temporary files, synthetic credentials, private child processes and
loopback providers. No installed service, personal queue or real provider is used.

The reference source is lain `416fe13` (7.3.17 local baseline plus lain patch 2).
The prior expansion binary was qualified at SHA-256
`bab5ff3fdce446f3bae4c4aa495ef6b5d42a8d14f40306734f79368e5bb92d87`.
Those reports remain historical evidence and do not qualify v8.0.41.

## Required behavior and existing executable evidence

| Promise | Executable acceptance gate | Upgrade requirement / gap |
| --- | --- | --- |
| Stable account attribution with shared model alias | `tests/backend/qualify.py`: `same-alias-200-replay-restart` | 100 requests for each of two distinct synthetic credentials; 200 logical requests, 200 starts, 200 usage facts, 200 separate final measurements; 1,800 total tokens per account. |
| Strict routing and explicit pools | `strict-429`, `pool-429-retry`, `deactivated-account` | Strict account selection must never borrow another account. An explicit pool may retry on B after A/429, retaining request ID and allocating a new attempt ID. |
| Credential versions and historical identities | `credential-lifecycle`, `startup-failures` | Replacement retains stable account ID; removal/recreation uses new account ID. Duplicate account aliases for one credential fail startup. |
| Chat, Responses and Messages streams / tools | `sse-tool-continuation`, `responses-tool-translation`, `anthropic-messages-tools` | Harmless `fixture_echo` continuation, explicit tool result and stream ordering survive the change. Responses here uses a Chat upstream; this is no Codex-live proof. |
| Host-rendered protocol and alias bindings | `tests/backend/host_contract_probe.py` with the synthetic `HostCoreContract.Tests.dll` | Actual Host YAML/bindings for Chat and native Responses must reach the matching outgoing endpoint; stable generated credential IDs, strict membership and hidden aliases survive. |
| Gemini gateway authorization | `tests/backend/upgrade_boundaries.py`: `gemini-native-bearer-and-default-key` | Explicit `auth-header: bearer` sends Bearer authorization without `x-goog-api-key`; the default sends `x-goog-api-key` without Bearer. Both use native Gemini and retain stable durable account attribution. |
| Aborted dispatch and hard crash | `cancel`, `crash-inflight`; `tests/acceptance/core_probe.py` | Client cancellation reaches the provider; SIGKILL leaves a durable unresolved start, with unchanged source identity after restart and no fabricated terminal usage. |
| Durable registration before upstream dispatch | `journal-failures`; adapter journal/dispatch unit tests | Dispatch cannot occur when start append/fsync fails. A request log or volatile usage queue is insufficient. |
| Usage commit before successful completion | `terminal-write-failure`, `responses-terminal-write-failure`; usage sink unit tests | On terminal journal failure, successful Chat/Responses completion markers are suppressed, pending start remains observable and later dispatch fails 503. No RAM-queue event after a failed synchronous durable write. |
| Unknown is different from measured zero | `unknown-token-components`; parser component-presence unit tests | Explicit zero stays zero; absent total/cache/reasoning stays null. Cache/reasoning subsets are not added to total again. |
| Immutable replay and transaction cursor | `same-alias-200-replay-restart`; `tests/usage` `--ingest-journal` | Complete batch is ingested by the actual SQLite store, reopened and replayed ten times: exactly 200 requests/attempts, 3,600 tokens, two accounts, no errors; null cache/reasoning. Cursor is 600 with additive finalization metadata. |
| Journal continuation across versions | `tests/backend/version_transition_probe.py` | v7 creates a fresh synthetic journal; v8 retains source UUID, old bytes/events and cursor, then appends disjoint identities and contiguous sequences. v7 rollback reads that same mixed source; the actual consumer replays it ten times: four requests/attempts, 72 tokens, two accounts. |
| Separate management / inference authority | `tests/acceptance/core_probe.py`: `management-and-inference-auth` | Inference token cannot read journal; management token cannot call inference. |
| External snapshots never become refresh credentials | `tests/backend/upgrade_boundaries.py`; adapter snapshot/login/refresh unit tests | Reject any `refresh_token` member (including null/empty), other OAuth providers and symlinks. Copied owned-file plus route reference grants no ownership. Blackbox rejection is not a proof of every auto/request/explicit refresh hook. |
| Owned OAuth lifecycle | Former `internal/lain/login_test.go` and refresh-gate unit tests | Keep successful atomic persistence, selected-and-owned refresh gate and cancellation during late exchange. No live OAuth probe is authorized by this synthetic run. |
| Inspector identity / redaction / volatile epoch | `tests/inspection/core_probe.py` | Explicit conversation, turn, worker, parent and tool provenance; split requests retain distinct identities; no secrets/tool arguments in observation; no content in journal; restart creates a new volatile epoch. |
| Bounded Inspector and measured performance | Former `internal/lain/inspector_test.go`; `tests/inspection/host` | Preserve global capture capacity, pending-line bounds, expiry, receipt-clock retention and early-usage versus actual-completion measurements. The Python probe alone does not establish memory or overhead bounds. |
| Free-only guard at actual send boundary | `tests/backend/upgrade_boundaries.py`; former `internal/lain/free_only_test.go`, `internal/runtime/executor/lain_free_only_transport_test.go` | Fail unqualified bindings closed. Before actual OpenRouter inference, fresh zero-cap proof and final translated model/body/host must be checked. Labels, pretranslation inspection and upstream auth selection alone are insufficient. No real free call is authorized. |
| Startup snapshot and unsupported endpoint boundary | Adapter startup/middleware unit tests | No hot reload, request-content logs, plugins/discovery, original management UI or unadvertised endpoints may become enabled incidentally. |

Source test locations above refer to the pre-upgrade tree. If the adapter moves
outside the upstream dependency, port these tests to adapter-owned packages;
do not delete their assertions merely because upstream remains unchanged.

## Reproducible runtime gate

Builds are separate from qualification. Supply an already built launcher and
an already built `Lain.Usage.Tests.dll`; the gate does not build or install them.
The DLL's `--ingest-journal` entry point uses the real `SqliteUsageStore` and
reopens the store on each of ten replays. Its runtimeconfig/deps files must be
adjacent, as for an ordinary .NET executable.

```sh
python3 -m unittest discover -s tests/backend -p 'test_*.py' -v
python3 tests/backend/upgrade_gate.py --core /absolute/lain-proxy \
  --consumer /absolute/Lain.Usage.Tests.dll \
  --host-exporter /absolute/HostCoreContract.Tests.dll \
  --baseline-core /absolute/old-lain-proxy \
  --output /tmp/lain-v8-qualification
```

The gate freezes executable bytes once, runs the original fifteen protocol and
durability checks, independent auth/crash checks, Inspector checks, additional
upgrade boundaries, then validates the unchanged full journal against the
consumer. Every result is linked to the same binary hash. The report separately
lists required adapter-owned unit gates and untested live capabilities; a runtime
pass does not certify those omitted checks. Runtime resources are serialized by
the coordinating worker. A multi-file launcher needs its full runtime dependency
bundle pinned and frozen before this single-executable gate can qualify it.

## New upstream capability evidence

Native server-side `image_generation` was previously permeable but never
qualified for the one-usage-fact-per-attempt contract: Codex may publish separate
image usage with another reporter identity. The upgrade deliberately rejects
explicit native image tools/tool choice before dispatch and disables automatic
injection. This is a documented restriction of an unqualified path, not a claim
that every formerly accepted payload remains accepted. The independent
`tests/backend/native_tool_boundary.py` verifies the new rejection and keeps
ordinary image-named functions and Vision `input_image` as positive controls.
It also injects the native tool through an actual configured payload override
after the client boundary: the final HTTP guard prevents any upstream request,
retains the already registered attempt as failed with unknown usage, and leaves
ordinary routes and the journal operational.

Claude Responses compaction, deferred tools / `tool_search`, Sign in with
ChatGPT, Grok speech, Vertex Interactions, session-affinity rebind and the new
v8 configuration/management API are new upstream capabilities. Retaining
existing lain behavior does not qualify them. Record each separately as exposed,
not exposed, synthetically exercised or live verified. Do not count an endpoint
blocked by the established lain allowlist as a newly introduced regression.

An unchanged-dependency approach is blocked if an existing required behavior
cannot be implemented using public upstream hooks. Record that concrete hook
gap and any required minimal build patch separately from the verified official
download. A patched prepared build must identify its patch hashes and affected
upstream paths. Keep the same acceptance boundary; static source inspection
cannot replace a passing behavioral gate.
