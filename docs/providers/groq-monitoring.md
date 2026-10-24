# Groq monitoring

Implementation basis: `9b1cd4b`, 25 October 2026. Public source contract reviewed
24 October in [the Groq research](../quota-auth-research/groq-mistral.md).
A fresh public-document fetch on 25 October failed because DNS and connector
transport were unavailable. No user credentials, user state, provider login,
authenticated provider request, inference, registration or purchase was used.
Synthetic fixtures are provided separately; they are not account qualification.

## Free and Developer accounts

Connect the existing Groq API key to the intended account. No qualified public
third-party OAuth/device login, ordinary-account remaining-quota endpoint or
spend reader was found in the reviewed official contract. A console login does
not establish those API permissions.

Use [Groq usage](https://console.groq.com/dashboard/usage) for independent
account usage and [Groq limits](https://console.groq.com/settings/limits) for
the account's configured limits. Check the selected organization and project
there. Lain opens these pages without importing a browser identity or cookies.
The ordinary account is not silently upgraded or configured for Enterprise.

With Gateway, normal authorized requests can produce saved RPD and TPM
response-header observations. Reload reads those saved observations only.
It never sends inference to obtain quota headers. The source time and relative
reset are preserved; elapsed time does not refill an observed remainder.
Organization and project limits may overlap and be shared across keys.
Without verified scope IDs, separate account/model rows do not imply separate
budget pools. A monitor without Gateway cannot create these observations.

## Optional existing Enterprise entitlement

Choose the Groq Enterprise monitoring method and enter the exact project ID.
Reuse this account's current inference key or explicitly supply a separate
authorized Groq API key. A standalone monitor may use the separate-key method.
Project IDs already recorded in the inference credential or account binding
must match. Where the inference key has no project metadata, this project
selection is user asserted; successful inference alone does not verify it.
Saving reports `configured-unverified` and does not claim Enterprise access.
No other browser, account or client identity is discovered as a fallback.

The reader uses only authenticated GET requests to the officially documented
`https://api.groq.com/v1/metrics/prometheus/api/v1/query`, pinned to that project.
It reads these documented metrics:

- `model_project_id_status_code:requests:rate5m` by model/project/status.
- `model_project_id:tokens_in:rate5m` by model/project.
- `model_project_id:tokens_out:rate5m` by model/project.

These are five-minute average **rates per second**, not cumulative usage,
remaining quota, spend, prepaid balance or an allowance. No denominator,
remaining quantity, monthly total, reset or percentage is manufactured.
Unsupported dimensions, duplicate series, unknown metrics, mismatched projects,
missing timestamps, non-finite/negative rates and future source times are
rejected rather than flattened into an apparently exact total.

Prometheus instant-vector timestamps can be query evaluation times. The query
therefore obtains metric values and `timestamp(metric)` in one expression,
copying the original metric name to a temporary label before `timestamp`
removes it. Only matching value/time pairs become readings. `ObservedAt`,
`DataAsOf` and `ProviderUpdatedAt` retain the underlying sample time. The local
five-minute freshness policy starts there and is not a provider export SLA.
Repeated polling of unchanged samples cannot make them younger.

A 401/403/429, empty response or partial response does not become zero usage
or trigger an upgrade. Previously read observations are retained as stale;
usable passive observations remain available. Check the project, key and
already existing Enterprise entitlement. The minimum Groq metrics key role,
export delay, retention and actual user entitlement still require provider
confirmation or a separately authorized live read.

## Integration and fixtures

The registered account adapter composes `GroqAccountQuotaAdapter` around the
Core-dependent passive adapter and `GroqEnterpriseUsageReader`. Without a
configured monitor it performs the passive read only. The original
`GroqQuotaAdapter` remains independently usable without any provider key.
The composite permits a credential-free passive fallback through
`IQuotaAdapter.AllowsMissingCredential`; no provider identity is substituted.

Run `dotnet run --project tests/groq-enterprise/GroqEnterprise.Tests.csproj`
with the coordinated build slot. Fixtures cover the fixed HTTP contract,
project/key selection, raw timestamp join, fractional/zero rates, stale
preservation, empty/error responses, malformed scope/sample rejection,
cancellation, and monitor setup without provider requests. Existing passive
integration fixtures remain in `tests/quota-sources`.

Official references: [rate limits](https://console.groq.com/docs/rate-limits),
[projects](https://console.groq.com/docs/projects),
[Enterprise Prometheus](https://console.groq.com/docs/prometheus-metrics),
[spend limits](https://console.groq.com/docs/spend-limits),
[quickstart](https://console.groq.com/docs/quickstart).
