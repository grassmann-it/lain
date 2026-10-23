# Trial-provider synthetic checks

Run `dotnet run --project tests/provider-trials/ProviderTrials.Tests.csproj`.
This isolated executable links only the prepared validator/request adapter and
uses an in-memory `HttpMessageHandler`. It never opens a socket, registers an
account, reads real credentials or changes billing/console settings.

Coverage includes regional/workspace isolation, Coding Plan exclusion, all four
Mercury 3 reasoning choices, output/temperature validation, request field and
bearer separation, and denial of free-only trial routes without a qualified
provider cutoff. Manifests must retain unknown actual grants/expiry and explicit
untested status. A successful mock is not a live provider qualification.
