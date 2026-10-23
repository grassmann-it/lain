# Independent Inspector boundary tests

These tests use synthetic data and a supplied new Core executable. They never
attach to an installed proxy, read account credentials, or call a real provider.
They do not build the application. Integration runs are serialized by the root worker.

```sh
python3 tests/inspection/core_probe.py \
  --core /absolute/path/to/new/lain-proxy \
  --report /tmp/lain-inspector-core-result.json
```

The probe freezes the executable before launching a private child process and
loopback fake provider. It checks management authentication, cursor integrity,
explicit conversation/turn/worker/parent provenance, identical-prompt separation,
a tool continuation across two requests, canceled terminal events, omitted tool
arguments, credential redaction, volatile response content, and restart epochs
without rewriting durable usage.

`fixtures/protocol-correlations.json` contains protocol inputs and independently
specified identities. Responses fixtures containing a fictitious previous response
are extraction/Host-boundary inputs; they are not dispatched to an upstream that
would need a stored response session. The probe's real tool cycle uses the
synthetic upstream's returned tool call ID.

A syntax check is not an execution result. Host collector/ring integration and
provider-policy assertions are added as those public contracts become available.
