using Lain.Core;
using Lain.Host.Operations;

namespace Lain.Host.Providers;

public static class ProviderCatalogEndpoints
{
    /// <summary>Read-only catalog operations; map only inside the host's loopback token boundary.</summary>
    public static void MapProviderCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(Api.Prefix + "/providers", (ProviderCatalogRegistry catalog) => catalog.Snapshot());
        app.MapGet(Api.Prefix + "/providers/{id}", (string id, ProviderCatalogRegistry catalog) =>
            catalog.Find(id) is { } provider ? Results.Ok(provider) : Results.NotFound(new ApiError("not_found", "Provider does not exist.")));
        app.MapGet(Api.Prefix + "/providers/{id}/onboarding", (string id, ProviderCatalogRegistry catalog, ConfigurationRepository repository, SecretStore secrets) =>
        {
            var provider = catalog.Find(id);
            if (provider is null) return Results.NotFound(new ApiError("not_found", "Provider does not exist."));
            var accounts = repository.Snapshot.Accounts.Where(a => a.Provider == id).ToArray();
            var hasCredential = accounts.Any(a => secrets.HasAccount(a.Id));
            // Presence is not validation: this endpoint does not contact upstream or inspect secret contents.
            return Results.Ok(new ProviderOnboardingDto(Api.Version, id, hasCredential ? "credentials_present_unverified" : "credentials_required", provider,
                accounts.Select(a => a.Id).ToArray(), provider.Credentials.Where(c => c.Required).Select(c => c.Id).ToArray(), "not_tested",
                hasCredential ? "Credential presence does not verify required fields; these fields still require validation." : null));
        });
        app.MapGet(Api.Prefix + "/routes/{id}/cost-policy", (string id, string? costPolicy, ConfigurationRepository repository, ProviderCostPolicy policy) =>
        {
            var config = repository.Snapshot;
            var route = config.Routes.SingleOrDefault(r => r.Id == id);
            if (route is null) return Results.NotFound(new ApiError("not_found", "Route does not exist."));
            return Results.Ok(policy.Evaluate(config, route, costPolicy is null ? null : new RoutingPolicyDto(costPolicy)));
        });
    }
}
