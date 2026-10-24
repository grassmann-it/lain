# Fireworks serverless and account quota connector

Prepared on 2026-10-24. Gateway and monitoring have independent credentials,
permissions, checks, and live qualification. No private account state was read.

## Gateway

The manifest supplies the standard `openai-chat` transport at
`https://api.fireworks.ai/inference/v1`, with the full model resource
`accounts/fireworks/models/glm-5p3-flash`. The current official serverless
quickstart documents this exact model, streaming, and JSON schema output.
Context/output limits and tool support for this particular route remain unknown;
the quickstart's tool example uses another model. Catalog capability documentation
does not prove a successful account-specific request.

Create an API key at <https://app.fireworks.ai/settings/users/api-keys> and enter it
privately through lain. No dedicated deployment, service account creation, purchase,
Auto Reload, or fallback is part of this connector. Fire Pass keys and its separate
router/subscription restrictions are outside this serverless route.

The FAQ describes a $1 starter credit and suspension when it runs out without a
payment method. This is an offer, not an observed balance, recurring free tier,
or guarantee for this account. Actual eligibility and expiry remain unverified.
The route is consequently priced as paid and ineligible for enforced free-only
routing; do not activate a live probe without confirming the existing free budget.

## Independent account quota monitor

The official List API is
`GET https://api.fireworks.ai/v1/accounts/{account_id}/quotas`.
Its `pageSize` maximum is 200; subsequent pages use `nextPageToken` as `pageToken`.
The adapter requests up to ten pages, escaping opaque tokens, rejecting loops,
and marking partial results. The same schema is documented by the single-resource
Get API at `/v1/accounts/{account_id}/quotas/{quota_id}`. List already returns all
fields; no redundant Get or inference request is necessary.

Both APIs explicitly document Bearer authentication using a Fireworks API key.
Use the ID of the billing/quota account, not a local lain ID, email, user ID,
display name, or the `fireworks` owner of a published model. Configure that ID in
`Settings["accountId"]` or the monitor's `ResourceId`. Resource names in responses
must match the requested account. Nothing is inferred from API-key text.

An optional monitor with `Kind="api-token"` uses only its separate `Secret`;
missing or expired credentials do not fall back to the inference key.
`Kind="inference-key"` uses the account's existing API key. An absent monitor also
uses that existing key. Missing/invalid account identification returns neutral
`setup-required`, without a request. HTTP 401, 403, and 429 remain actual sanitized
errors. Monitoring failure does not disable inference.

The published role matrix documents resource viewing and inference for
`inference-user`, but does **not** explicitly assign List/Get Quotas permissions
to a minimum role or named scope. Therefore neither inference success nor a generic
role proves quota access. Confirm the selected account/key is authorized to list
quotas. If the existing key is insufficient, a separately authorized Fireworks API
key can be attached to the same lain account. No OAuth scope or native OAuth flow
is asserted or implemented here; no new service account is created automatically.

## Values and freshness

| API field | Representation |
| --- | --- |
| `name` | Exact validated resource identity, independent row and source URL |
| `value` | Enforced `Limit`, never substituted from `maxValue` |
| `maxValue` | Independent `ApprovedLimit`, not extra funds or enforced limit |
| `usage` | Observed `Used`; omitted/null remains unknown, zero stays zero |
| `updateTime` | `ProviderUpdatedAt` only; never reset or usage `DataAsOf` |

The API has no unit or reset field. The canonical quota guide explicitly names
`monthly-spend-usd`; this exact ID is represented in USD with account monthly
spend scope. Where both `value` and `usage` are present, `Remaining = value - usage`
means room under that spend constraint, **not prepaid credit balance**. A negative
remainder preserves observed overspend. Enterprise spend alerts need not enforce
service suspension. The API does not establish an exact reset boundary.

Other IDs retain independent raw quantities with `Unit="unknown"` and
`MeasurementKind="limit"`. No GPU counts, throughput, or model tokens are
summed or relabeled as a global token budget; unknown IDs get no derived remainder.
The approved ceiling alone never supplies used, remaining, or enforced limit.
Integer values outside exact public numeric representation are omitted.

The Host's actual successful fetch time describes freshness of the response.
`DataAsOf`, `WindowStart`, `WindowEnd`, `FreshUntil`, and `ResetsAt` stay unset.
The API does not promise when usage was measured. Billing's Total Spend can lag
traffic by a day or more, particularly for new models. A recent fetch therefore
does not prove real-time usage. Host last-good observations survive subsequent
failures and cancellation with the shared stale/error semantics.

No provider pool is inferred, even when several local accounts claim the same
Fireworks account. This avoids merging distinct GPU/spend/throughput constraints
into one budget. External billing history is a separate future capability.

## Qualification status

- Prepared: catalog, independent official quota adapter, synthetic HTTP/service
  fixtures, setup and scope documentation.
- Locally checked: `tests/providers-fireworks/run.sh` passed 49 assertions with
  zero build warnings/errors in a Main-granted slot. HTTP/service fixtures are
  synthetic; new metadata is asserted on adapter readings. Shared metadata
  projection and gateway transport qualification are coordinated by Main.
- Installed: not performed by this worker.
- Live checked: not performed. Account permissions, starter balance, expiry,
  inference, stream/tool behavior, and observed API quantities remain unverified.

## Official sources checked 2026-10-24

- [Serverless quickstart and key setup](https://docs.fireworks.ai/getting-started/quickstart)
- [OpenAI compatibility, final-stream-chunk usage](https://docs.fireworks.ai/tools-sdks/openai-compatibility)
- [List Quotas: pagination and schema](https://docs.fireworks.ai/api-reference/list-quotas)
- [Get Quota: field semantics and API-key authorization](https://docs.fireworks.ai/api-reference/get-quota)
- [Account quotas: monthly-spend-usd, account-wide controls, enterprise distinction](https://docs.fireworks.ai/guides/quotas_usage/account-quotas)
- [Accounts and resource identity](https://docs.fireworks.ai/getting-started/concepts)
- [Role matrix](https://docs.fireworks.ai/accounts/users)
- [Billing and delayed Total Spend](https://docs.fireworks.ai/faq-new/billing-pricing/how-does-billing-and-credit-usage-work)
- [Starter credit exhaustion](https://docs.fireworks.ai/faq-new/billing-pricing/what-happens-when-i-finish-my-1-dollar-credit)

The earlier provider research identifies unresolved commercial-distribution terms
questions; this implementation makes no broader distribution or worker entitlement
claim. No live credentials were requested, read, or forwarded.
