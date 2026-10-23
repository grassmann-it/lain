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

After merging the common contracts, provider registry/policy and all provider
manifests, run the separate no-network policy/catalog executable:

`dotnet run --project tests/inspection/policy/PolicyIntegration.csproj`

It embeds the actual fifteen-provider manifests, verifies documented onboarding
and billing units, and applies free-only checks to every declared route. Injected
graphs cover paid/trial/unknown fallback opt-in, strict route/profile inheritance,
cycles, missing/disabled targets, unqualified execution, forged free controls and
concrete model-route mismatches. A declarative free label does not prove that
runtime controls are qualified.
