using Lain.Core;
using System.Text.Json.Serialization;
using System.Text.Json;
using Lain.Host.Operations;
using Lain.Host.Providers;

namespace Lain.Host.Models;

public record CoreRouteBinding([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("accounts")] string[] Accounts, [property: JsonPropertyName("selection")] string Selection,
    [property: JsonPropertyName("credentials"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? Credentials = null,
    [property: JsonPropertyName("visible")] bool Visible = true,
    [property: JsonPropertyName("provider"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Provider = null,
    [property: JsonPropertyName("costPolicy")] string CostPolicy = "configured",
    [property: JsonPropertyName("pricingClass")] string PricingClass = "unknown",
    [property: JsonPropertyName("freeOnlyEnforcement"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FreeOnlyEnforcement = null,
    [property: JsonPropertyName("dimensions"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CoreRouteDimensions? Dimensions = null,
    [property: JsonPropertyName("upstreamProtocol"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? UpstreamProtocol = null);
public record CoreRouteDimensions(
    [property: JsonPropertyName("modelFamily"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ModelFamily = null,
    [property: JsonPropertyName("manufacturer"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Manufacturer = null,
    [property: JsonPropertyName("inferenceProvider"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? InferenceProvider = null,
    [property: JsonPropertyName("gateway"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Gateway = null,
    [property: JsonPropertyName("mode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Mode = null,
    [property: JsonPropertyName("profileId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ProfileId = null);
public record CoreBindings([property: JsonPropertyName("accounts")] IReadOnlyDictionary<string, string> Accounts,
    [property: JsonPropertyName("routes")] IReadOnlyDictionary<string, CoreRouteBinding> Routes,
    [property: JsonPropertyName("routers"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, CoreCollectionRouter>? Routers = null);

public sealed class RouteBindingsWriter(OwnStatePaths paths)
{
    public string BindingsPath => Path.Combine(paths.Proxy, "bindings.json");

    public string Write(ConfigurationDto config, IReadOnlyDictionary<string, string> credentialIds,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? routeCredentials = null, string? profileId = null,
        IReadOnlySet<string>? qualifiedProviderRouteIds = null,
        IReadOnlyDictionary<string, ModelCompatibilityTransportBinding>? transportBindings = null)
    {
        var bindings = RouteBindings.Compile(config, credentialIds, routeCredentials, profileId: profileId, qualifiedProviderRouteIds: qualifiedProviderRouteIds, transportBindings: transportBindings);
        OwnStatePaths.WritePrivate(BindingsPath, JsonSerializer.Serialize(bindings));
        return BindingsPath;
    }
}

public static class RouteBindings
{
    /// <summary>Startup contract for the qualified launcher. Every active route must have complete account bindings.</summary>
    public static CoreBindings Compile(ConfigurationDto config, IReadOnlyDictionary<string, string> credentialIds,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? routeCredentials = null,
        ProviderCatalogRegistry? catalog = null, string? profileId = null, IReadOnlySet<string>? qualifiedProviderRouteIds = null,
        IReadOnlyDictionary<string, ModelCompatibilityTransportBinding>? transportBindings = null)
    {
        catalog ??= new();
        var costPolicy = new ProviderCostPolicy(catalog);
        costPolicy.ValidateRuntime(config);
        var routes = new Dictionary<string, CoreRouteBinding>(StringComparer.Ordinal);
        foreach (var route in config.Routes.Where(x => x.Enabled))
        {
            if (route.AccountIds.Length == 0 || route.AccountIds.Distinct().Count() != route.AccountIds.Length || (!route.Pooled && route.AccountIds.Length != 1))
                throw new ArgumentException("Active route must select one strict account or an explicit pool.");
            IReadOnlyDictionary<string, string>? overrides = null;
            if (routeCredentials is not null && routeCredentials.TryGetValue(route.Id, out overrides))
            {
                if (overrides.Count != route.AccountIds.Length || overrides.Keys.Any(id => !route.AccountIds.Contains(id)))
                    throw new ArgumentException("Route credential overrides must exactly match selected accounts.");
            }
            var effectiveCredentials = overrides ?? credentialIds;
            foreach (var id in route.AccountIds)
            {
                if (!config.Accounts.Any(x => x.Id == id && x.Enabled) || !effectiveCredentials.TryGetValue(id, out var credential) || string.IsNullOrWhiteSpace(credential))
                    throw new ArgumentException("Active route has no enabled qualified credential binding.");
            }
            ModelCompatibilityTransportBinding? transport = null;
            if (transportBindings is not null)
            {
                if (!transportBindings.TryGetValue(route.Id, out transport) || transport.Provider != route.Provider ||
                    transport.UpstreamModel != route.UpstreamModel || string.IsNullOrWhiteSpace(transport.Source) ||
                    transport.UpstreamProtocol is not ("openai" or "responses" or "anthropic" or "gemini"))
                    throw new ArgumentException("Active route requires matching compiled transport provenance.");
            }
            // Catalog identity alone cannot prove that the selected credential targets the documented inference endpoint.
            var descriptor = qualifiedProviderRouteIds?.Contains(route.Id) == true ? catalog.FindRoute(route.Provider, route.ProviderRouteId) : null;
            var freeOnly = route.Policy?.CostPolicy == "free_only";
            var enforcement = freeOnly && descriptor is not null ? costPolicy.RuntimeEnforcement(route) : null;
            if (freeOnly && enforcement is null)
                throw new ArgumentException("Free-only routing requires a qualified provider transport and runtime enforcement.");
            var dimensions = descriptor is not null || profileId is not null
                ? new CoreRouteDimensions(descriptor?.ModelFamily, descriptor?.Manufacturer, descriptor?.InferenceProvider, descriptor?.Gateway, ProfileId: profileId)
                : null;
            if (!routes.TryAdd(route.Alias, new(route.Id, route.UpstreamModel, route.AccountIds.ToArray(), route.Pooled ? "pool" : "strict",
                    overrides?.ToDictionary(x => x.Key, x => x.Value), route.Visible, route.Provider,
                    route.Policy?.CostPolicy ?? "configured", descriptor?.PricingClass ?? "unknown", enforcement, dimensions, transport?.UpstreamProtocol)))
                throw new ArgumentException("Duplicate route alias.");
        }
        var used = routes.Values.SelectMany(x => x.Accounts).ToHashSet();
        var defaults = credentialIds.Where(x => used.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value);
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in defaults.Concat(routes.Values.Where(x => x.Credentials is not null).SelectMany(x => x.Credentials!)))
        {
            if (string.IsNullOrWhiteSpace(entry.Value)) throw new ArgumentException("Credential identities must not be empty.");
            if (owners.TryGetValue(entry.Value, out var owner) && owner != entry.Key)
                throw new ArgumentException("A core credential identity cannot belong to multiple logical accounts.");
            owners[entry.Value] = entry.Key;
        }
        return new(defaults, routes, CollectionRouterCompiler.Compile(config, profileId, routes, qualifiedProviderRouteIds));
    }
}
