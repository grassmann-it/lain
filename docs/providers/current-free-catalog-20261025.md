# Concrete free gateway catalogue refresh, 2026-10-25

Both gateway catalogues now offer the exact concrete model
`liquid/lfm-2.5-2.6b:free` as a small test candidate. The manifests add
`kilo/liquid/lfm-2.5-2.6b:free` and
`openrouter-liquid-lfm-2.5-2.6b-free`. This does not change any saved account,
route, profile, runtime selection, installed Codex export or quota snapshot.

The public observations were obtained without authentication:

| Gateway | Source | Observed UTC | Models | Previous concrete IDs absent |
|---|---|---|---|---|
| Kilo | <https://ai-gateway.kilo.ai/api/v1/models> | 2026-10-25 01:53:55 | 401 | `google/gemma-4-26b-a4b-it:free` |
| OpenRouter | <https://openrouter.ai/api/v1/models> | 2026-10-25 01:53:12 | 467 | `meta-llama/llama-3.3-70b-instruct:free`, `qwen/qwen3-coder:free` |

The selected model has zero advertised prompt/completion prices, a 65,536
context window and a `tools` supported-parameter entry in each observation.
Those are the only new capability claims. Maximum output, streaming, concrete
reasoning effort levels and actual account access remain unknown. The presence
of a `reasoning` parameter does not establish `low` or a reasoning-off mode.
See the public-only fixture at
`tests/providers-free-catalog/fixtures/public-models-20261025.json`.

Kilo's official models-and-providers documentation identifies the public models
endpoint and still contains the previous Gemma default example. The dated API
observation therefore takes precedence for current catalogue presence. An absent
model can explain a stale selection, but it is not a complete diagnosis of an
individual HTTP 404. No inference request was made for this source change.

Historical descriptors remain present and retain their exact IDs. Their names
and restrictions now identify the catalogue absence observation. No migration,
paid sibling, auto router, random router, BYOK path or fallback is introduced.
An HTTP 429 remains a provider quota/rate response and is not repaired or reset
by refreshing model metadata.

## Integration boundaries

`ProviderCatalogRegistry.Normalize` derives connections by endpoint and
protocol. Both new descriptors reuse their gateway's existing endpoint,
protocol and billing plan, so generated connection IDs stay stable and the new
catalogue route IDs join their existing connections. No generated file needs
editing.

Strict `free_only` is separate from configured-policy access. Kilo remains
ineligible for that strict guarantee. The new OpenRouter descriptor is marked eligible as part of a coordinated
change; it must be integrated together with the matching runtime allowlists and
synthetic checks. Its concrete model pin requires changes in both shared
allowlists:

- `src/Lain.Host/Providers/ProviderCostPolicy.cs`,
  `SupportsOpenRouterZeroCap`;
- `proxy/adapter/internal/freeonly/openrouter.go`,
  `IsOpenRouterFreeModel`.

The final outgoing guard must retain the same-key fresh zero-USD cap including
BYOK, endpoint/header checks, zero provider price caps, no paid extras, no
redirects and no fallback. A public catalogue entry does not replace any of
those checks. Random `openrouter/free` remains outside the concrete allowlist.

Shared synthetic tests also need coordinated updates outside this worktree's
ownership: `tests/providers-cohere-kilo/Program.cs` assumes a single Kilo route
and a single source date; `tests/inspection/policy/Program.cs` hardcodes the two
old OpenRouter model IDs. Existing old-descriptor coverage should be retained
while current-descriptor coverage is added. No build or test process was run in
this bounded catalogue task.
