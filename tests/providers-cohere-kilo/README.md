# Cohere/Kilo synthetic boundaries

Run only in the integrator's serialized build/runtime slot. See
[provider qualification notes](../../docs/providers/cohere-kilo.md) for current
states, official sources and central transport hooks.

The .NET project references the real Host and exercises catalog-to-account-to-YAML
compilation. It creates only disposable own-state directories with synthetic keys.

The Python probe takes an explicit newly built Core binary and reuses
`tests/backend/qualify.py`'s isolated child-process/journal lifecycle. Its custom
provider accepts only loopback, checks the exact Cohere/Kilo route suffix and
model, requires bounded `max_tokens`, and rejects undocumented Cohere fields.
It uses synthetic personal keys and strict account bindings. No environment
tokens, installed service or personal account data are used.

The SSE fixture supplies final usage without relying on `stream_options`.
It is deliberately strict: unsupported injected Cohere fields or a successful
truncated stream produce a failing report rather than a false qualification.
Kilo tool/SSE results would establish local protocol preservation only; its
concrete live capabilities remain unknown in the manifest.

```sh
dotnet build tests/providers-cohere-kilo/Providers.CohereKilo.Tests.csproj -m:1 -p:BuildInParallel=false -nr:false
dotnet run --project tests/providers-cohere-kilo/Providers.CohereKilo.Tests.csproj --no-build --no-restore
python3 tests/providers-cohere-kilo/gateway_probe.py --core /absolute/qualified/lain-proxy --report /tmp/lain-cohere-kilo-gateway.json
```

Local Host state on 2026-10-24: build succeeded with 0 warnings/errors; **43
assertions passed**. Python probe syntax parsed; Core runtime scenarios remain
pending Main's integrated-binary slot. Installation not performed; both providers
live `not_tested`. Kilo account is reported exhausted and no paid replacement
route is prepared.
