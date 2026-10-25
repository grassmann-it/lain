# Cohere and Kilo Gateway

Prepared 2026-10-24 from public official documentation. No personal credentials,
provider inference, account API probe, purchase or top-up was used. These are two
independent Gateway packages; neither implements an account quota monitor.

| Package | Exact route | Credential and scope | Status |
| --- | --- | --- | --- |
| PV-COHERE | `https://api.cohere.ai/compatibility/v1` + `/chat/completions`; `command-a-plus-05-2026` | Own Cohere evaluation API key; intended organization; Bearer authentication | Prepared; 43 local Host checks passed; Core probe pending; not installed; live not tested |
| PV-KILO | `https://ai-gateway.kilo.ai/api/v1` + `/chat/completions`; `google/gemma-4-26b-a4b-it:free` | Existing personal Kilo Gateway API key; personal account; Bearer authentication | Prepared; 43 local Host checks passed; Core probe pending; not installed; live not tested; existing account reported exhausted |

Both use the existing `openai-chat` transport and provider-specific private
identities (`cohere-openai-chat`, `kilo-openai-chat`). Exact base paths matter:
Cohere's native `/v2/chat` and Kilo Code's other provider integrations are separate
products/protocols. Saving an account does not establish inference entitlement.

## Private setup

Cohere: open [API keys](https://dashboard.cohere.com/api-keys) on the existing
account, select an evaluation key for the intended organization and enter it
privately through lain. Pick the concrete Command A+ route and evaluation plan.
The published trial ceiling is 1,000 calls/month with a 20 requests/minute model
limit. This is an advertised limit, not a measured remaining allowance, and no
reset timestamp is known. Trial rights do not authorize production. Cohere's
going-live process requires agreements and possibly use-case approval; newer
Chat variants can remain trial-limited even on a production key. No upgrade is
performed here, and selecting a plan cannot verify the actual key type.

Kilo: open [Your Profile](https://app.kilo.ai/profile) on the existing **personal**
account; the documented Gateway API key is at the bottom of that page. Enter it
privately in lain. No organization ID, organization token, upstream BYOK key,
login-token import or refresh flow is included. An organization token has a
different lifetime and policy scope; it is not interchangeable with this setup.

The exact Gemma free route appears in the official public model catalog with
zero input and output prices, a 262,144-token context and 32,768 maximum output.
That observation does not prove the exhausted user's account can run it. Check
the route's current availability and data handling before a later small test.
The package prepares no `kilo-auto/*`, paid sibling, paid fallback, BYOK or
top-up. A 402/403/429 keeps the external live gap visible. Published 200 free
requests/hour are an IP-scoped ceiling; they are not account credit or an
observed remaining quota. No reset is invented. BYOK may charge the upstream
provider even when Kilo reports zero cost.

## Protocol and safety boundaries

Use a short text request with `max_tokens: 64` for qualification. Cohere documents
Chat streaming and tools for the selected model. Its compatibility contract is
a subset: unsupported upstream fields include `parallel_tool_calls`, `store`, `metadata`,
`n`, audio and native Cohere `documents`/`connectors`. Use `max_tokens`, not an
assumed `max_completion_tokens` equivalent. Compatibility reasoning accepts only
`none`/`high`; this catalog entry advertises no reasoning mode. The local
Responses-to-Chat bridge accepts a validated literal `store:false` as stateless
Chat without sending a store field. `store:true`, stored-response IDs and stored
conversations are rejected; complete function-call input history remains supported
by the translator. Native Chat requests retain the stricter documented field
subset. See [the bounded follow-up](stream-outcome-and-cohere-2026-10-25.md#bounded-stateless-responses-follow-up)
for declaration-loss guards and synthetic qualification scope. Kilo's gateway
supports Chat SSE and tools generally; the Gemma entry keeps those capabilities
unknown until the concrete route has been qualified. A synthetic successful tool
roundtrip proves local preservation, not the vendor model's capability.

Neither route qualifies for the strict `free_only` policy. Kilo's `:free` suffix
and Cohere's evaluation offer do not provide lain's required runtime spending
guard. Configured routing still pins the exact model and supplies no fallback;
this package does not expand strict policy enforcement.

There is no independently qualified quota/balance endpoint in either package.
The UI's quota capability must remain unsupported with a neutral explanation;
the Cohere API-key page and Kilo dashboard provide the manual check. Local lain
history covers only requests passing through lain; provider/other-client usage
is not imported or implied. No standalone monitor is created.

## Integration and local evidence

The manifests are embedded automatically by the Host catalog wildcard. No
central registration, protocol, credential contract or UI-specific provider card
is required. Main owns the Core and shared Host transport boundary.

`tests/providers-cohere-kilo/Program.cs` exercises the actual embedded catalog,
`AccountService`, private temporary synthetic credentials, `ProviderCostPolicy`
and `ProxyConfigurationWriter`. It checks exact endpoints, wrong-model rejection,
plan membership, no partial account after invalid enrollment, account isolation,
explicit model paths, unverified state and strict-free-policy rejection.

`gateway_probe.py` reuses the existing backend `Run` process/journal harness with
an explicitly supplied binary and loopback HTTP contracts. It checks JSON/SSE,
tool continuation, 401/403/429/404, Kilo exhausted-balance 402 without another
account/fallback, abort, truncated SSE, token coverage, account/route attribution,
credential-free history and durable replay. Provider model claims are unchanged
by synthetic fixture results. No existing service is contacted.

One central qualification hook is requested before declaring streaming locally
qualified: the generic Core currently injects `stream_options.include_usage`.
That parameter is absent from Cohere's documented supported subset. The strict
Cohere fixture rejects unsupported outbound fields, including that injection;
Main must qualify or suppress unsupported fields after translation. Main must
also retain failure semantics for a stream that ends before its terminal marker.
These are shared transport concerns, not missing provider credentials.

Serialized validation commands, after Main grants the build/runtime slot:

```sh
dotnet build tests/providers-cohere-kilo/Providers.CohereKilo.Tests.csproj -m:1 -p:BuildInParallel=false -nr:false
dotnet run --project tests/providers-cohere-kilo/Providers.CohereKilo.Tests.csproj --no-build --no-restore
python3 tests/providers-cohere-kilo/gateway_probe.py --core /absolute/qualified/lain-proxy --report /tmp/lain-cohere-kilo-gateway.json
```

Local Host qualification passed on 2026-10-24 after Main granted its serialized
build slot: build succeeded with **0 warnings / 0 errors**, then **43 assertions
passed** against the real Host services. The gateway probe passed a static Python
syntax parse; its Core runtime scenarios have **not** been executed. Keep local
Host, local Core, installation and live states separate. Missing free credit/access leaves live
qualification open; it does not block catalog/fixture implementation or authorize
a purchase. Main coordinates installation and any eventual private live check.

## Official sources

All checked 2026-10-24; model availability can change. Public documentation only.

- [Cohere compatibility API](https://docs.cohere.com/docs/compatibility-api): exact base, selected model, Chat, streaming, tools and parameter subset.
- [Cohere key types and limits](https://docs.cohere.com/v2/docs/rate-limits): evaluation/production distinction and newer-model restrictions.
- [Cohere going live](https://docs.cohere.com/v2/docs/going-live): production agreements and approval process.
- [Kilo API reference](https://kilo.ai/docs/gateway/api-reference): exact base, Chat body, SSE and token usage.
- [Kilo model routing](https://kilo.ai/docs/gateway/models-and-providers): explicit model IDs, changing free availability and auto-route distinctions.
- [Exact Kilo Gemma free model](https://kilo.ai/models/google-gemma-4-26b-a4b-it-free): exact ID, zero input/output pricing and context/output limits.
- [Kilo authentication](https://kilo.ai/docs/gateway/authentication): personal Bearer key, separate organization-token and BYOK semantics.
- [Kilo private-key instructions](https://kilo.ai/docs/getting-started/setup-authentication): personal profile location.
- [Kilo billing](https://kilo.ai/docs/gateway/usage-and-billing): free suffix, IP limits, exhausted-balance response and manual dashboard.
