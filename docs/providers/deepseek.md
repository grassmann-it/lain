# DeepSeek direct API and user balance

EC1, checked 26 October 2026. Direct account connection uses an existing API
key entered in lain's private credential field. The reviewed public contract
documents API-key authentication; no public OAuth flow is claimed or offered.
No login, real key entry, authenticated provider read, inference, purchase or
top-up was performed while implementing this adapter.

Connect the account first. Models are optional and attaching them does not
apply a profile. The existing OpenAI-compatible transport uses
`https://api.deepseek.com` and `/chat/completions`; no inference engine is added.
The catalog offers the current canonical documented IDs `deepseek-flash` and
`deepseek-v4-pro`. The quick start currently says Flash is served by
DeepSeek-V4.1-Flash-1017. It describes the older V4 Flash names as accepted
aliases for retired models, so those aliases are omitted from the picker.
Historical `deepseek-chat` and `deepseek-reasoner` IDs are not inferred from
older knowledge. Catalog capabilities come from the current Chat schema and
the documented model-list example, not an authenticated discovery result.

The same selected API key authorizes the read-only request
`GET https://api.deepseek.com/user/balance`. The adapter permits only the
catalog's official base URL (with an optional trailing slash). It never sends
a compatible gateway's key to DeepSeek. Lain's shared HTTP boundary limits the
response size, applies a timeout and sanitizes error diagnostics, including
401 responses. No retry or inference probe is made by this adapter.

`balance_infos` contains independent `CNY` or `USD` money balances.
`total_balance` is retained exactly in `ExactRemaining`, including decimal
places and values beyond floating-point precision; `Remaining` is null.
Signed plain decimal strings are preserved as provider facts. No currency
conversion, cross-currency total, used amount, limit, percentage, reset or
pool inheritance is invented. The granted and topped-up fields are already
included in total balance and are not added as separate pools.
`is_available` describes sufficiency for API calls; false does not erase a
reported zero balance or convert it into a quota limit.

An independent valid currency survives another malformed row with a fixed
partial-response diagnostic. Invalid decimal strings, undocumented currency
codes and duplicate entries for the same currency stay unknown; duplicate
currencies are not selected by response order. Money strings are bounded to
256 characters. Missing components do not invalidate a reported total;
components alone never imply a total.

The endpoint selects its user through the API key and returns no user ID.
The adapter supports a provider-only declared binding and does not ask for
an account selector. Explicit subject, resource, product or region assertions
are rejected before HTTP because this response cannot verify them. This is
not a server-verified identity check: reconnect retains lain's stable local
account ID/name and uses its generation guard, while a replacement key's
remote owner cannot be established by the balance response. Optional model
attachment remains bound to the saved account and key generation.

Official sources, retrieved 26 October 2026:

- [Quick start](https://api-docs.deepseek.com/): current IDs, API key, compatible base URL.
- [Chat Completions](https://api-docs.deepseek.com/api/create-chat-completion): model enum, tools, streaming, JSON output; `reasoning_effort` none/low/high/max toggles thinking without another transport field.
- [Lists Models](https://api-docs.deepseek.com/api/list-models): documented example reports 1,048,576 context and 393,216 output tokens, Flash image input and Pro text input. These are documentation values, not an account allocation.
- [Get User Balance](https://api-docs.deepseek.com/api/get-user-balance): `balance_infos`, native currency enum and string total/granted/topped-up balances.

The old `/quick_start/pricing` URL redirected to the quick start when checked;
no pricing numbers are copied from that redirect. Actual prices and account
entitlements remain provider-managed.

Focused offline checks live in [tests/deepseek](../../tests/deepseek/README.md).
Host wiring belongs to the integrator: register `DeepSeekQuotaAdapter`, enable
DeepSeek in the direct-quota connection set, and qualify the official root
API endpoint in `QuotaTransportProjection`. The catalog JSON is discovered
through the existing embedded-resource glob.
