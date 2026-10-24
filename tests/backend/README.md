# Isolated backend qualification

Python 3 standard library only. No existing proxy, queue, configuration or auth
directory is used. The runner freezes the supplied binary, starts only its own
processes, binds random loopback ports, creates private temporary paths and cleans
up only those paths and children. All credentials are fixed synthetic values.

```sh
python3 -m unittest discover -s tests/backend -p 'test_*.py' -v
python3 tests/backend/qualify.py --core /absolute/path/lain-proxy \
  --report /tmp/lain-qualification.json --examples /tmp/lain-event-examples.json \
  --batch-fixture /tmp/lain-source-batch.json --qualified-artifact /tmp/lain-qualified-core
```

The self-tests validate the fixture. Only `qualify.py` tests the actual Core.
Its exit status is nonzero if any synthetic check fails; the JSON records the
exact binary SHA-256 and each measured result. A synthetic pass does not imply
real-provider approval or completion of package A. Read `pending` and
`docs/backend-qualification.md` before allowing dependent live work.

The fake provider understands OpenAI Chat Completions and Anthropic Messages.
The Core runner tests client Chat Completions and Responses SSE, including
translation from Responses tool-call items into Chat tool messages and back,
plus native Messages streams and tool-result continuation through the Claude API
executor. It does not simulate a Codex OAuth or Command Code transport.
The fixture marker is a synthetic user message,
not an internal routing header. Observations retain only scenario/account IDs,
model, stream/tool-result flags, status and disconnect state.

`Run` in `qualify.py` is reusable by independent acceptance checks. Its public
helpers are `post`, `responses`, `messages`, `get`, `events`, `start`, `stop`; `provider`
exposes sanitized `snapshot()` observations. `Run` always owns its resources.

The write-failure test uses Linux `resource.prlimit` on only its own Core child.
It proves dispatch is blocked when the durable registration cannot append,
rejects the resulting partial journal without changing it, and explicitly
restores the fixture's known empty journal before recovery. It never truncates
a journal containing a dispatched attempt. The hard-crash test separately
preserves an unresolved registered attempt and proves replay after restart.

## Upstream upgrade qualification

The original fifteen binary checks remain the acceptance boundary. Additional
startup probes reject refresh-bearing snapshots (including null/empty tokens),
foreign OAuth providers and symlinks, and verify that a route reference cannot
promote a copied credential into owned status. They also reject unqualified
free-only routes and check native Gemini Bearer override versus the default
API-key header using only synthetic credentials and the loopback fixture:

```sh
python3 tests/backend/upgrade_boundaries.py --core /absolute/lain-proxy \
  --report /tmp/lain-upgrade-boundaries.json
```

`upgrade_gate.py` runs the original checks, independent auth/crash and Inspector
probes, the additional boundaries, and ten actual SQLite store replay/reopen
cycles over the complete 600-event source batch. It requires an already built
`Lain.Usage.Tests.dll`; it performs no build, install or service operation. The
output directory must be new or empty. On success it preserves the exact frozen
launcher and all reports. Adapter-owned unit gates remain separately required.

```sh
python3 tests/backend/upgrade_gate.py --core /absolute/lain-proxy \
  --consumer /absolute/Lain.Usage.Tests.dll --output /tmp/lain-v8-qualification
```

See [the upgrade acceptance matrix](../../docs/backend-upgrade-qualification.md)
for required behavior, evidence limits and the distinction between existing lain
behavior and newly available upstream capabilities.

With an already built `HostCoreContract.Tests.dll`, add `--host-exporter /path/to/DLL`
to include the actual Host YAML/bindings writer in that gate. The separate
`host_contract_probe.py --core ... --exporter ... --report ...` checks strict and
pool aliases, hidden-but-callable aliases, stable generated credential IDs and
both Chat and native Responses outgoing endpoints. It invokes only the synthetic
exporter and local fake provider. The fake provider now accepts native Responses
SSE/tool continuations as well as Chat and Messages; observed endpoint paths
allow a silently dropped `upstream-protocol` to fail qualification.

`--baseline-core /absolute/old-lain-proxy` adds a synthetic version-transition
gate: the old binary creates a private journal, the new binary preserves its
source/cursor/old bytes and appends fresh identities, then the old binary reads
the combined journal. The actual SQLite consumer replays the mixed source ten
times without double counting. This uses no existing journal or database.
Standalone invocation:

```sh
python3 tests/backend/version_transition_probe.py --baseline-core /absolute/v7 \
  --core /absolute/v8 --consumer /absolute/Lain.Usage.Tests.dll \
  --report /tmp/lain-version-transition.json
```

`native_tool_boundary.py` checks a deliberately new exclusion: native server-side
`image_generation` tools and their explicit tool choice fail 400 before dispatch.
Ordinary function tools named `image_generation` or `generate_image`, and Vision
`input_image`, remain accepted and reach the fake provider. This check is included
in `upgrade_gate.py`; it is not an assertion that the old binary already enforced
the exclusion. No image generation or real provider is involved.
An injected post-translation payload override is also blocked at the final HTTP
boundary; that later rejection retains one failed durable attempt with unknown
usage, leaves the journal healthy and allows an ordinary second route to dispatch.
