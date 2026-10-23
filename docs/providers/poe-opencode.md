# Poe and OpenCode Go preparation

Official documentation checked **2026-10-24**. No account, subscription, client
registration or purchase was created. No API keys were available. Public model
catalog GETs succeeded; credential-backed balance, history and inference calls
remain **not tested**. Source URLs and check dates also live in the secret-free
provider catalog manifests.

## Poe

The [external application guide](https://creator.poe.com/docs/external-applications/external-application-guide)
explicitly supports a user's own API key, including for applications distributed
to other users. Points are charged to that key's user account. Paid models need
an active subscription or add-on points; the attachment's suggested minimum
subscription price was not verified and is not encoded. No unlimited allowance
or permanently free account allocation is assumed.

The [OpenAI compatibility guide](https://creator.poe.com/docs/external-applications/openai-compatible-api)
documents `https://api.poe.com/v1/chat/completions` and `/v1/responses` with bearer
API-key authorization. Chat Completions supports streaming and tools, but ignores
`response_format` and function `strict`; it must not promise strict schema output.
Responses has a distinct `text.format` schema feature. Reasoning and media
capabilities vary by bot. The documentation includes conflicting general and
endpoint-specific structured-output wording, so prepared Responses routes leave
that capability unknown pending endpoint qualification. Poe documents 500
requests/minute per user and request rate-limit headers; these are separate from
the points balance and are not token limits.

[Current balance](https://creator.poe.com/api-reference/getCurrentBalance):

```http
GET https://api.poe.com/usage/current_balance
Authorization: Bearer <user API key>
```

```json
{"current_point_balance": 295932027}
```

This is one combined plan-plus-add-on balance in **points**. The response does
not provide a monthly allocation, consumed amount, plan/add-on split, expiry or
reset. `PoeQuotaAdapter` exposes only measured remaining points. It requires an
API-key credential qualified for the exact official `https://api.poe.com/v1`
base URL before making its fixed-endpoint GET. Zero is known; absent, malformed,
negative, fractional or non-exactly-representable values stay unknown.

[Usage history](https://creator.poe.com/api-reference/getPointsHistory):

```http
GET https://api.poe.com/usage/points_history?limit=20&starting_after=<query_id>
```

```json
{
  "has_more": true,
  "data": [{
    "creation_time": 1761523099056852,
    "bot_name": "Claude-Sonnet-4.5",
    "query_id": "ltol8bhnkya9ak3twt",
    "usage_type": "API",
    "cost_points": 6554
  }]
}
```

`creation_time` is Unix **microseconds**. The endpoint covers up to 30 days,
returns most recent entries first, and caps pages at 100 entries. `query_id` of
the final provider row is the pagination cursor. `PoeUsageHistory` fetches one
page at a time and preserves `API`, `Chat`, and `Canvas App` usage types. It
does not label the account history as lain-only activity, compute a monthly
total from one page, or turn points into tokens/USD. Invalid rows mark the page
incomplete; a missing final cursor stays missing.

[Sign in with Poe](https://creator.poe.com/docs/external-applications/sign-in-with-poe)
is an official OAuth 2 authorization-code flow with **mandatory S256 PKCE**:

- Register a client at `https://poe.com/api/clients`; obtain its client ID.
- Authorize at `https://poe.com/oauth/authorize`, scope `apikey:create`, exact
  redirect URI, code challenge, and locally checked state.
- Exchange the single-use code using a form POST to `https://api.poe.com/token`
  with client ID, redirect URI and code verifier.
- Read `api_key` and `api_key_expires_in` (seconds or explicit null). This issues
  an API key; the documented response has no refresh token or refresh grant.

`PoeOAuthAttempt` prepares this official flow, validates state, consumes each
attempt once, permits HTTPS or loopback HTTP callbacks, and parses key expiry.
It does not register a client, open a browser, bind a callback, transmit a token
request or persist a key. The host must own callback lifetime and secure key
persistence before enabling OAuth onboarding.

The [model list reference](https://creator.poe.com/api-reference/listModels) and
public `https://api.poe.com/v1/models` were checked. `owned_by` is a publisher
claim. `PoeModelCatalog` leaves actual manufacturer and inference operator
unknown rather than deriving them from publisher or model ID. Explicit catalog
examples identify Claude/Anthropic and GPT/OpenAI from documented model
descriptions, while keeping publisher status `provider_reported` and inference
operator null. A bot name or lab-named publisher alone is not a verified
manufacturer-operated bot.

The live list illustrated the distinction: `step-5-preview-el` describes a
StepFun model but reports publisher **EmpirioLabs AI**; `kimi-k3-el` likewise
differs from the Moonshot AI-published Kimi bot. These rows currently advertise
an empty `supported_endpoints` list, so no Step/community route is prequalified
in the static catalog. A null `pricing` field is explicitly documented for
both free models and request-priced models: it never proves free-only eligibility.

## OpenCode Go / Go Plus

The [official Go page](https://opencode.ai/v2/docs/console/go/) confirms **Go
$10/month** and **Go Plus $40/month**, single-member workspace subscriptions.
It supports OpenCode and other coding agents generating typical coding-agent
requests. Clients should identify themselves (`lain/1.0`) and supply a stable
conversation session ID (`x-opencode-session`); native recognized session headers
must be preserved when forwarding. No claim of general unrestricted background
proxy use is made.

Usage has simultaneous **5-hour (20%)**, **weekly (50%)**, and **monthly
(100%)** allowance constraints. Model-specific monthly USD-equivalent reference
amounts affect how usage counts toward those allowances. For example, the
checked Go table lists Step 5 Preview at $15 and GLM-5.3-Flash at $60; Go Plus
lists $60 and $180 respectively. These are model accounting references, not
separately spendable cash balances. The descriptor keeps a workspace-plan scope
and separate model reference limits, and never adds the model rows or time
windows into a total. The current public prose does not specify a machine-readable
cross-model accounting formula; no weighted consumption is computed locally.
Estimated request tables are examples using assumed token mixes, not API quotas.

Current usage is linked to the Console. A public Go quota/history endpoint and
response schema were **not verified**. `quotaSupport` is `unsupported`; current
used/remaining percentages, exact resets, rate limits and parallelism remain
unknown. A key's documented `Usage: Read` permission does not establish an
endpoint or a payload shape. No private Console endpoints are scraped.

Go's **Extra Usage** setting can fall back to a PAYG Console balance once plan
limits are reached. Setup directs users to keep it disabled unless they
deliberately configure paid overflow. Every prepared Go route is paid and
excluded from free-only routing. Temporary free models are not encoded as a
permanent free plan; official external-client free-tier restrictions also apply.

The Go endpoint table, rather than model-family naming, selects each protocol:

| Example model | Protocol | Exact endpoint |
| --- | --- | --- |
| Step 5 Preview, GLM-5.3, Kimi K3 | OpenAI Chat | `/inference/go/openai/v1/chat/completions` |
| GPT 6 Luna | OpenAI Responses | `/inference/go/openai/v1/responses` |
| Claude Haiku 5.5, Qwen3.8 Max | Anthropic Messages | `/inference/go/anthropic/v1/messages` |
| Gemini 4 Argon Contributor | Gemini | `/inference/go/google/v1beta/models/gemini-4-argon-contributor:generateContent` |

Gemini streaming uses `:streamGenerateContent?alt=sse`. `OpenCodeGoAdapter`
prepares requests only for the explicit qualified model table, requires matching
protocol and model identity, and adds the real client and stable session headers.
The caller must supply a body already in that protocol. No implicit cross-protocol
conversion or unpriced fallback is attempted. The Gemini Contributor route also
requires explicit workspace privacy permission for paid endpoints that train on
request data; its capability flags stay unknown.

The [public models endpoint](https://opencode.ai/inference/go/v1/models) currently
returns minimal `{object:"list", data:[{id,object,created,owned_by}]}` metadata
with `owned_by:"opencode"`. This identifies a gateway catalog entry, not the
model manufacturer, upstream inference provider, protocol, allowance or token
capabilities. Availability changes independently of the documented table;
unlisted routes require review instead of name-based guessing.

Additional official sources:
[API keys](https://opencode.ai/v2/docs/console/api/) and
[inference](https://opencode.ai/v2/docs/console/api/inference/).

## Integration and verification

The catalog JSON matches the new provider descriptor contract and contains no
secrets. Shared registry, account creation flow, host service registration and
root route wiring belong to the integrator. Existing quota DTOs carry Poe points;
no shared DTO, migration, service configuration or legacy store was changed.

Synthetic checks are in `tests/providers-poe-opencode/run.sh`; run them only in
the integrator's coordinated build slot. They cover origin qualification,
nonadditive/unknown semantics, microsecond history timestamps and pagination,
publisher provenance, PKCE/state/expiry, and protocol-specific Go request paths.
Real account verification remains `not_tested` until the user supplies keys.
