using Lain.Core;
using System.Text.Json.Serialization;
using System.Text.Json;
using Lain.Host.Operations;

namespace Lain.Host.Models;

public record CoreRouteBinding([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("accounts")] string[] Accounts, [property: JsonPropertyName("selection")] string Selection,
    [property: JsonPropertyName("credentials"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? Credentials = null);
public record CoreBindings([property: JsonPropertyName("accounts")] IReadOnlyDictionary<string, string> Accounts,
    [property: JsonPropertyName("routes")] IReadOnlyDictionary<string, CoreRouteBinding> Routes);

public sealed class RouteBindingsWriter(OwnStatePaths paths)
{
    public string BindingsPath => Path.Combine(paths.Proxy, "bindings.json");

    public string Write(ConfigurationDto config, IReadOnlyDictionary<string, string> credentialIds,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? routeCredentials = null)
    {
        var bindings = RouteBindings.Compile(config, credentialIds, routeCredentials);
        OwnStatePaths.WritePrivate(BindingsPath, JsonSerializer.Serialize(bindings));
        return BindingsPath;
    }
}

public static class RouteBindings
{
    /// <summary>Startup contract for the qualified launcher. Every active route must have complete account bindings.</summary>
    public static CoreBindings Compile(ConfigurationDto config, IReadOnlyDictionary<string, string> credentialIds,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? routeCredentials = null)
    {
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
            if (!routes.TryAdd(route.Alias, new(route.Id, route.UpstreamModel, route.AccountIds.ToArray(), route.Pooled ? "pool" : "strict",
                    overrides?.ToDictionary(x => x.Key, x => x.Value))))
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
        return new(defaults, routes);
    }
}
