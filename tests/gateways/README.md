# Gateway synthetic tests

Run `dotnet run --project tests/gateways/Gateways.Tests.csproj` in the integrator's
serialized .NET build slot. The linked production sources use fake HTTP only.
No credentials, provider calls, service operations or inference are involved.

The provider JSON manifests are documentation snapshots, not active core routes.
`GatewayRouteValidation` is a prerequisite checker for the future route writer;
catalog eligibility does not establish live account access or billing posture.
