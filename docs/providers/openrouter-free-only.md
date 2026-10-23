# OpenRouter: narrow free-only dispatch preparation

Official sources checked 24 October 2026. No key, key administration, live probe,
inference request or service operation was used. This is a prepared implementation
and evidence record; a successful mock is not evidence that a real zero-limit key
can use free models.

## Controls and their limits

The [free variant documentation](https://openrouter.ai/docs/guides/routing/model-variants/free)
explains that a free variant is a separate model entry and that paid model
fallbacks incur their own charges. Our allowlist contains only
`meta-llama/llama-3.3-70b-instruct:free` and `qwen/qwen3-coder:free`; no random
router, guessed suffix, preset or fallback model array qualifies.

The [provider routing documentation](https://openrouter.ai/docs/guides/routing/provider-selection#max-price)
defines endpoint price filters for prompt/completion tokens, requests and images.
It states that `max_price` prevents execution when the price is unavailable.
The final outgoing guard pins the exact model and writes
`allow_fallbacks: false`, `require_parameters: true`, and zero values for all four
documented price categories. These are endpoint filters, not a documented total
charge cap covering every plugin or future feature.

The [plugin documentation](https://openrouter.ai/docs/guides/features/plugins#default-plugin-settings)
is the reason request filtering alone is insufficient. Account or organization
defaults can enable plugins, and an administrator can prevent request overrides.
An empty request plugin array does not establish that no default plugin runs.
The local guard rejects explicit plugins, gateway server tools, file/image/audio
input, provider/model overrides and unknown request extensions. It does not claim
that this overrides administrator-enforced defaults.

## Candidate provider-side zero-spend prerequisite

The official [spend-control decision map](https://openrouter.ai/blog/insights/governing-team-ai-spend/)
describes a per-key limit as a **“hard cap”**, with the scope **“Total credits one
API key can spend”** and behavior **“Blocks (rejects over limit)”**. Those quoted
phrases total 18 words. The same source notes that already-running requests can
finish above a *workspace* budget. It does not explicitly extend that caveat to
zero per-key limits, and it does not supply a zero-limit/free-model example.

The official [limits reference](https://openrouter.ai/docs/api_reference/limits)
distinguishes a numeric key cap from `null` (unlimited), explains USD-key limits
and the `include_byok_in_limit` flag, and describes checking spending restrictions
on paid requests. Its in-flight estimate excludes plugin, image and request fees;
that in-flight mechanism must not be substituted for a zero-spend key cap.

The [key-creation reference](https://openrouter.ai/docs/api/api-reference/api-keys/create-a-new-api-key)
defines `limit` as an optional USD spending limit and `include_byok_in_limit` as
including external provider-key use. The
[current-key reference](https://openrouter.ai/docs/api/api-reference/api-keys/get-current-api-key)
documents the read-only Bearer endpoint `GET /api/v1/key` and its response fields.

The prepared helper requires this endpoint, using the *actual outgoing Bearer
key*, to freshly return numeric `limit: 0`, numeric `limit_remaining: 0`,
`include_byok_in_limit: true` and `is_management_key: false`. Missing, null, string,
positive, stale, malformed and failed readings are rejected. No cached local
quota value or user-declared boolean qualifies. A dedicated zero-cap key must be
configured by the user; this code does not create or update one.

**Evidence boundary:** numeric zero as a hard no-spend limit follows from the
documented cap/null distinction; however, no explicit official assurance was
found that a zero-limit key continues serving free models while preventing all
administrator-enforced billable defaults. No live access has tested that boundary.
Therefore do not represent it as observed usability or activate strict free-only
solely because this helper or a manifest exists. The integrator must resolve this
qualification boundary before enabling the route. A subsequent external change
to the key policy is also outside local control; fresh per-dispatch checks reduce
the interval but cannot atomically bind a remote policy read to an inference call.

## Code and required wiring

`proxy/core/internal/freeonly/openrouter.go` exports:

- `IsOpenRouterFreeModel(model)` for the two exact qualified IDs.
- `ValidateOpenRouterInput(body, alias, model)` before alias translation.
- `WithOpenRouterPin` / `OpenRouterPin` for the trusted selected-route intent.
- `PrepareOpenRouterRequest(ctx, client, request, model)` immediately before HTTP
  dispatch, after every translation, payload rule and header customization.

The last function only permits the official HTTPS Chat Completions endpoint. It
requires the exact model, rejects duplicate JSON keys recursively, verifies the
same credential through a bounded five-second read-only call, applies provider
price controls, disables redirects and removes cookie-jar identity. The caller
must supply a fresh per-execution HTTP client. The final call is mandatory;
checking the inbound body alone cannot prevent later payload transformations.

`openrouter_zero_cap_v1` describes this prepared mechanism, not a safety claim
that bypasses runtime checks. Other providers and unqualified route intents keep
failing closed. `openrouter_test.go` uses an in-memory fake RoundTripper for all
HTTP, including the key proof; it exercises body injection, independent fresh
proofs, redirection, zero/unknown/positive caps, tiny nonzero cap values, sanitized
failures and attempted routing/tool/plugin overrides. Run only in the root's
serialized Go slot; tests have not been executed in this worktree.
