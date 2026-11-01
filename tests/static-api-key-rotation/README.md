# Static API-key rotation and reconnect

The focused executable creates isolated synthetic account/configuration roots. It
performs no HTTP, provider login, inference, running-host change or real credential
read. It covers three preserved transport variants (Responses, Anthropic, Chat),
new-key injection, scrubbed YAML auth/key fields, kept non-authentication headers,
compiled runtime output without old keys, endpoint/settings conflict rejection,
explicit purpose reconnect, shared credential-cohort rotation including models,
independent quota credentials, immutable previous generations, stale revision and
generation, missing referenced generation, verified binding and failed save.

Run through the shared verification lane:

```sh
flock /home/tomka/.cache/lain-verification-lane.lock dotnet run --project tests/static-api-key-rotation/StaticApiKeyRotation.Tests.csproj
```

The standalone `src/Lain.Host/Accounts/StaticApiKeyRotation.cs` helper depends on
System.Text.Json and the already existing YamlDotNet package. Operators may link
that source directly. Its stable API is:

```csharp
var descriptor = StaticApiKeyRotation.Rotate(explicitlySelectedTemplateJson, intendedNewApiKey);
StaticApiKeyRotation.ValidateReconnectOptions(currentTemplateJson, optionalEndpoint, optionalSettings);
```

The helper performs no file lookup and chooses no historical generation. A repair
operator must explicitly select the authoritative transport metadata and separately
obtain the intended current key. It then uses normal credential preparation and
account revision/generation CAS; generating this string does not publish it.

Only known static descriptor fields are copied. Embedded YAML key lists and
credential/authentication fields are removed; ordinary transport headers, protocol,
models, endpoints, route/transport IDs and settings survive. Unknown private JSON
fields, cyclic/shared YAML graphs, unrecognized uses of the previous primary key,
and conflicting endpoint/settings submissions fail before preparation. No guessing
or textual global replacement is performed. For real providers, recheck the exact
compiled route/protocol mapping before an authorized publication.

`ConnectCredential` retains the request's exact purposes: its transport handoff
must not silently rotate unrelated purposes. The explicit `ReplaceApiCredential`
rotation operation moves all capabilities referencing the old inference handle,
including models; an independently bound quota/cost handle is preserved. Existing
verified-identity checks remain in force. No missing modern generation falls back
to older generation material.
