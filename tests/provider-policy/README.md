# Provider policy qualification

Run `dotnet run --project tests/provider-policy/ProviderPolicy.Tests.csproj` in the integrator's serialized build slot.
The executable uses synthetic descriptors and an isolated temporary state root. It never contacts providers or reads installed credentials.

Checks cover legacy configured routes, paid and unknown fallbacks, opt-in, cycles, route/model binding, profile policy,
free-only rejection before credential access, immutable snapshots, revision conflicts and binding provenance.
Documentation-only free claims remain blocked. Only the two explicitly allowed OpenRouter free model pins can be configured
with `openrouter_zero_cap_v1`; actual dispatch still requires a fresh same-key zero-USD hard-cap proof including BYOK,
an exactly qualified endpoint, and a final payload guard. The policy tests prove configuration intent and binding requirements,
not provider availability or live zero-cap behavior. The provider may reject free inference under a zero cap.
Cross-route fallback configuration can be stored with explicit opt-in but cannot be applied until execution is supported.
