# Provider policy qualification

Run `dotnet run --project tests/provider-policy/ProviderPolicy.Tests.csproj` in the integrator's serialized build slot.
The executable uses synthetic descriptors and an isolated temporary state root. It never contacts providers or reads installed credentials.

Checks cover legacy configured routes, paid and unknown fallbacks, opt-in, cycles, route/model binding, profile policy,
free-only rejection before credential access, immutable snapshots, revision conflicts and binding provenance.
Documentation-only free claims remain blocked until provider-specific upstream cost controls are implemented and qualified.
Cross-route fallback configuration can be stored with explicit opt-in but cannot be applied until execution is supported.
