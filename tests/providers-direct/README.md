# Direct API qualification

`python3 tests/providers-direct/test_manifests.py` validates the three secret-free
manifests without compiling or connecting to a provider.

`dotnet run --project tests/providers-direct/DirectApi.Tests.csproj` runs isolated
HTTP mocks for official pinned metadata-only GET endpoints, missing credentials,
redirect refusal, bounded responses, sanitized errors, cancellation and passive
Groq rate header parsing. It uses no live credentials or inference endpoint.
Coordinate .NET builds with the integrator during parallel development.

Metadata availability is **not** an inference, price, API-plan entitlement or quota
verification. The default HTTP handler refuses redirects. No `/models` request is
made automatically by loading a descriptor. Quota polling is unsupported for all
three ordinary API-key providers. Groq headers can be parsed only when a caller
already possesses a real response; that observation retains the calling route's
identity rather than inventing an organization/model pool.
