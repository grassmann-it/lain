# Host to core compatibility fixtures

This test runs the actual Host configuration and binding writers using fake API
keys, then checks the existing private login wire contract through an in-memory
HTTP handler. It does not launch a proxy, access provider accounts or read installed
state. The generated credentials retain the Host's current SHA-based identities;
disabled credentials, strict/pool membership and hidden aliases stay explicit.

Run in the root-coordinated .NET lane:

```sh
dotnet run --project tests/host-core-contract/HostCoreContract.Tests.csproj
```

A binary qualifier can export the actual writer output into a new empty directory:

```sh
dotnet run --project tests/host-core-contract/HostCoreContract.Tests.csproj -- \
  --export /tmp/lain-host-fixture \
  --provider-url http://127.0.0.1:19001/v1 --proxy-port 19002 \
  --upstream-protocol openai-chat
```

`fixture.json` records the absolute config, bindings, journal, owned-auth directory
and management-token file paths plus synthetic account identities. The usual
launcher flags can consume those paths. The private inference token is at
`credentials/inference.token`; the generated management and inference tokens are
separate, freshly generated synthetic values. No real credentials are exported.
The fake provider receives `acct-fixture-a-key` or `acct-fixture-b-key` and model
`fixture-model`. Strict aliases select the corresponding account; `fixture-pool`
selects either listed account; `fixture-hidden` is bound but excluded from models.

Only the fixture overrides the fixed product listener port after Host rendering.
The exported YAML otherwise uses the production writer unchanged. v8.0.41 retains
the legacy-only configuration spellings that this writer produces; nested v8
spellings win when mixed and are intentionally not added here. Reading config or
setting `config-version: 8` does not itself migrate a legacy file. Native v8
management ETags are unrelated to the Host's revisioned startup-file contract.

`--upstream-protocol openai-responses` exports the existing explicit Responses
transport extension instead. The Host must retain this field and the core adapter
must honor it; the official v8 OpenAICompatibility struct by itself lacks this
existing lain extension. This fixture therefore records the requested protocol
in its manifest so a binary qualifier can check the actual outgoing endpoint.
