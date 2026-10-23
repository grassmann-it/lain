# Isolated backend qualification

Python 3 standard library only. No existing proxy, queue, configuration or auth
directory is used. The runner freezes the supplied binary, starts only its own
processes, binds random loopback ports, creates private temporary paths and cleans
up only those paths and children. All credentials are fixed synthetic values.

```sh
python3 -m unittest discover -s tests/backend -p 'test_*.py' -v
python3 tests/backend/qualify.py --core /absolute/path/lain-proxy \
  --report /tmp/lain-qualification.json --examples /tmp/lain-event-examples.json
```

The self-tests validate the fixture. Only `qualify.py` tests the actual Core.
Its exit status is nonzero if any synthetic check fails; the JSON records the
exact binary SHA-256 and each measured result. A synthetic pass does not imply
real-provider approval or completion of package A. Read `pending` and
`docs/backend-qualification.md` before allowing dependent live work.

The fake provider understands OpenAI Chat Completions. The Core runner tests
client Chat Completions and Responses SSE, including translation from Responses
tool-call items into Chat tool messages and back. It does not simulate a Codex
OAuth or Command Code transport. The fixture marker is a synthetic user message,
not an internal routing header. Observations retain only scenario/account IDs,
model, stream/tool-result flags, status and disconnect state.

`Run` in `qualify.py` is reusable by independent acceptance checks. Its public
helpers are `post`, `responses`, `get`, `events`, `start`, `stop`; `provider`
exposes sanitized `snapshot()` observations. `Run` always owns its resources.

The write-failure test uses Linux `resource.prlimit` on only its own Core child.
It proves dispatch is blocked when the durable registration cannot append,
rejects the resulting partial journal without changing it, and explicitly
restores the fixture's known empty journal before recovery. It never truncates
a journal containing a dispatched attempt. The hard-crash test separately
preserves an unresolved registered attempt and proves replay after restart.
