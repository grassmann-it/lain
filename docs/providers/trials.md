# Inception, Alibaba Model Studio and Nebius

Official sources checked on 24 October 2026 (Europe/Berlin; 23 October UTC).
The catalog describes offers, not an account's granted or remaining balance.
No accounts, purchases, credentials or live inference were used. Connection and
quota verification remain `not_tested`/unknown. No recurring free tier is claimed.

## Inception Mercury

The [quick start](https://docs.inceptionlabs.ai/get-started/get-started.md)
confirms a one-time allocation of **100 million tokens for a new account**, with
no payment details required. It does not state an expiry date. Expiry, actual
remaining tokens, eligible-account status, rate limits and parallelism therefore
remain unknown. Adding payment details enables continued paid use; trial credit
does not make this route safe for an unconditional free-only policy.

The [model reference](https://docs.inceptionlabs.ai/get-started/models.md) and
[19 October launch post](https://www.inceptionlabs.ai/blog/introducing-mercury-3)
confirm `mercury-3` on `https://api.inceptionlabs.ai/v1/chat/completions`, a 1M-token
context and maximum 131,072 output tokens. The documented route supports tool
calling, structured outputs and text input. Its `reasoning_effort` choices are
`instant`, `low`, `medium`, `high`; temperature is 0.5–1.0. Streaming remains
unverified in this catalog rather than copied from another Mercury version.
Launch prices are promotional, so this implementation does not freeze them into
a billing calculation or claim a measured token rate from marketing benchmarks.

Onboarding: open [Inception Platform](https://platform.inceptionlabs.ai), create
your own account if desired, and obtain an API key from its API Keys page.
The application requires `api_key`; it neither registers nor enters payment data.

## Alibaba Cloud Model Studio

The [free-quota rules](https://www.alibabacloud.com/help/en/model-studio/new-free-quota)
(updated 16 October) restrict the documented new-user trial to **Singapore,
International service deployment scope**. Eligible concrete models typically
receive 1M tokens, but not every model is eligible and amounts must be read from
the account. Each model/snapshot has a separate quota; root account and RAM users
share it, and input plus output draw from one combined model total. Never sum
these into an interchangeable workspace token balance.

The normal validity is **90 days from the latest of activation, model release or
model-access approval**. Existing users can be governed by earlier rules. No
activation date or expiry is manufactured for an unconfigured account.

After exhaustion or expiry, regular API use can incur PAYG charges.
**Free Quota Only is disabled by default**, configured per eligible model in the
web console, and changes have a propagation delay. When effective, exhausted
quota returns `AllocationQuota.FreeTierOnly`. There is no verified request flag
for enabling this protection. A local checkbox alone is not provider enforcement;
the prepared adapter rejects `free_only` until such enforcement is implemented
and qualified. It cannot change the provider console. A trial-domain endpoint is
an endpoint class, not proof that inference is free.

The [regions page](https://www.alibabacloud.com/help/en/model-studio/regions)
(13 October) recommends a workspace-dedicated host. Use the exact API Host copied
from Workspace Management and the matching regional API key:

| Region | Region ID | Recommended OpenAI base URL |
| --- | --- | --- |
| China (Beijing) | cn-beijing | `https://{WorkspaceId}.cn-beijing.maas.aliyuncs.com/compatible-mode/v1` |
| Singapore | ap-southeast-1 | `https://{WorkspaceId}.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1` |
| Germany (Frankfurt) | eu-central-1 | `https://{WorkspaceId}.eu-central-1.maas.aliyuncs.com/compatible-mode/v1` |
| Japan (Tokyo) | ap-northeast-1 | `https://{WorkspaceId}.ap-northeast-1.maas.aliyuncs.com/compatible-mode/v1` |
| China (Hong Kong) | cn-hongkong | `https://{WorkspaceId}.cn-hongkong.maas.aliyuncs.com/compatible-mode/v1` |
| US (Virginia) | us-east-1 | `https://{WorkspaceId}.us-east-1.maas.aliyuncs.com/compatible-mode/v1` |

Keys, available models and endpoints differ by region. Some scopes require a
new workspace. Legacy `dashscope.aliyuncs.com`/`dashscope-intl.aliyuncs.com`
integrations are documented but no longer receive new features after 30 September
2026. The current [quick start](https://www.alibabacloud.com/help/en/model-studio/getting-started/first-api-call-to-qwen)
contains contradictory prose about Virginia workspace prefixes, while its region
table and code use the workspace-prefixed host. Prefer the region table and
console-copied API Host; never guess an account's workspace.

The [Coding Plan policy](https://www.alibabacloud.com/help/en/model-studio/coding-plan)
permits interactive programming tools and explicitly prohibits automated scripts,
application backends and other non-interactive scenarios. Its dedicated key and
endpoint are excluded from this autonomous-backend adapter. Use a general-purpose
regional Model Studio API key instead. Token/Coding Plan keys do not consume the
regular free token quota.

## Nebius Token Factory

The official [billing guide](https://docs.tokenfactory.nebius.com/other-capabilities/billing-new.md)
states that normal onboarding requires a billing account and bank card and grants
**$1 of trial credit valid for 30 days** on first signup. Currency credit is not
tokens. Billing may automatically charge the card, so credit does not qualify a
PAYG endpoint for unconditional free-only routing.

The [Builder Program](https://nebius.com/builders) currently advertises optional
membership with $25 Token Factory credit, subject to eligibility, availability and
terms. This is a separate offer requiring joining and claiming credit, not an
automatic addition to every new account. The page describes learning/testing/
experimentation as the primary use. Compute grants require a separate application
and are not guaranteed. Neither offer is added to a displayed account balance;
claimed amount and expiry remain unknown until observed.

The [quick start](https://docs.tokenfactory.nebius.com/quickstart.md) documents
the OpenAI-compatible `https://api.tokenfactory.nebius.com/v1` endpoint and
`deepseek-ai/DeepSeek-R1-0528` example. GLM/Kimi availability is not established by
that example. Obtain an API key and select a concrete model from the account's
Models page or the documented [`GET /models?verbose=true`](https://docs.tokenfactory.nebius.com/api-reference/examples/list-of-models.md).
The verbose response can provide actual model pricing, context and rate limits;
no unauthenticated model list or inference request was sent here. Capability fields
for the example stay unknown until qualified for that concrete route.

[Rate limits](https://docs.tokenfactory.nebius.com/ai-models-inference/rate-limits.md)
are dynamic, not universal fixed RPM/TPM. The documented response headers report
current request/token limits and remaining headroom. Rate-limit tokens are not
remaining trial credit. No hardcoded default or advertised throughput is presented
as a measured account limit.

## Prepared integration boundary

The catalog manifests are secret-free onboarding data. The isolated configuration
validator and request adapter prepare OpenAI-compatible requests with injected
credentials and preserve JSON tool/stream fields. Mock-only coverage validates
regional isolation, denied Coding Plan endpoints, Mercury reasoning, payload/key
separation and rejection of free-only use without enforceable protection. These
helpers do not claim live qualification or host execution wiring.
