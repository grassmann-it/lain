# SambaNova and Upstage gateways

Official public documentation checked 2026-10-24. These entries use lain's
standard private API-key setup and OpenAI Chat transport. There is no provider
quota adapter, billing setup, purchase or paid fallback in this package.

| Provider | Private key page | Pinned upstream model | Base URL |
| --- | --- | --- | --- |
| SambaNova | [SambaCloud APIs](https://cloud.sambanova.ai/apis) | `Meta-Llama-3.3-70B-Instruct` | `https://api.sambanova.ai/v1` |
| Upstage | [Organization API keys](https://console.upstage.ai/api-keys) | `solar-mini4-260922` | `https://api.upstage.ai/v1` |

Select the provider and concrete route in lain, then enter the API key privately.
The key belongs in lain's credential field, never chat, argv, route settings,
logs or source control. Saving marks the account unverified. The CLI wizard and
desktop consume the same catalog; private setup does not trigger inference.

SambaNova's [Cloud key/URL reference](https://docs.sambanova.ai/docs/en/get-started/api-keys-urls)
fixes the Chat endpoint. The [current model catalog](https://docs.sambanova.ai/docs/en/models/sambacloud-models)
lists Llama 3.3 70B as a production text model, published context 128k. Its
[function-calling guide](https://docs.sambanova.ai/docs/en/features/function-calling)
explicitly lists this model and demonstrates tool results; the
[quickstart](https://docs.sambanova.ai/docs/en/get-started/quickstart) demonstrates
streaming and `stream_options.include_usage` for it. Maximum output and other
unqualified features remain unknown. [Current limits](https://docs.sambanova.ai/docs/en/models/rate-limits)
list Free Tier for accounts without a payment method, currently 20 RPM, 20 RPD
and 200,000 TPD for this model. These are published limits, not observed remaining
values. Adding a payment method changes the tier. Confirm the actual account
state before a free probe. SambaStack credentials and endpoints are separate.

Upstage's [Solar Mini 4 specification](https://console.upstage.ai/docs/models/solar-mini-4)
identifies the dated snapshot, 512K context, 128K output and explicit tool support.
The [Chat API](https://console.upstage.ai/api/chat) documents SSE chunks,
`tool_calls`, usage, and the global `/v1/chat/completions` path. The older Solar
Mini/Pro 2/Pro 3 series is scheduled for deprecation on 2026-10-30; this entry
uses the current Mini 4 snapshot. Reasoning and structured outputs are documented
upstream but remain unqualified by this package and are not advertised in its
route fields. Native Responses and Korean regional endpoints are separate work.

The [Upstage setup guide](https://console.upstage.ai/docs/getting-started)
advertises $10 signup credits. This does not establish this user's credit grant,
balance, expiry, card requirement or no-charge entitlement. Confirm them privately
in Console before any live probe. [The billing FAQ](https://console.upstage.ai/docs/resources/faq)
states that organization keys share billing and that exhausted credits may be
charged to a registered payment method; Console budget limits can permit minor
overages. No payment method, credit purchase or auto-recharge is configured here.

Both providers remain ineligible for lain's strict `free_only` policy because no
runtime prevention of paid usage has been qualified. A published free tier or
trial does not supply that guarantee. No public standalone quota polling API was
qualified. SambaNova's documented RPM/RPD response headers are not collected by
this package. Local usage history covers only requests lain observes, with
unknown cost or missing upstream usage preserved as unknown.

Status: **prepared**. Local fixtures await the coordinated build/runtime slot;
installation and live checks are outstanding. No private credentials were read,
requested or forwarded. See [fixture commands and scope](../../tests/providers-samba-upstage/README.md).
