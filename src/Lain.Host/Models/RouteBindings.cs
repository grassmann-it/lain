using Lain.Core;
using System.Text.Json.Serialization;
using System.Text.Json;
using Lain.Host.Operations;

namespace Lain.Host.Models;

public record CoreRouteBinding([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("accounts")] string[] Accounts, [property: JsonPropertyName("selection")] string Selection);
public record CoreBindings([property: JsonPropertyName("accounts")] IReadOnlyDictionary<string, string> Accounts,
    [property: JsonPropertyName("routes")] IReadOnlyDictionary<string, CoreRouteBinding> Routes);

public sealed class RouteBindingsWriter(OwnStatePaths paths)
{
    public string BindingsPath => Path.Combine(paths.Proxy, "bindings.json");

    public string Write(ConfigurationDto config, IReadOnlyDictionary<string, string> credentialIds)
    {
        var bindings = RouteBindings.Compile(config, credentialIds);
        OwnStatePaths.WritePrivate(BindingsPath, JsonSerializer.Serialize(bindings));
        return BindingsPath;
    }
}

public static class RouteBindings
{
    /// <summary>Startup contract for the qualified launcher. Every active route must have complete account bindings.</summary>
    public static CoreBindings Compile(ConfigurationDto config, IReadOnlyDictionary<string, string> credentialIds)
    {
        var routes = new Dictionary<string, CoreRouteBinding>(StringComparer.Ordinal);
        foreach (var route in config.Routes.Where(x => x.Enabled))
        {
            if (route.AccountIds.Length == 0 || (!route.Pooled && route.AccountIds.Length != 1))
                throw new ArgumentException("Active route must select one strict account or an explicit pool.");
            foreach (var id in route.AccountIds)
            {
                if (!config.Accounts.Any(x => x.Id == id && x.Enabled) || !credentialIds.TryGetValue(id, out var credential) || string.IsNullOrWhiteSpace(credential))
                    throw new ArgumentException("Active route has no enabled qualified credential binding.");
            }
            if (!routes.TryAdd(route.Alias, new(route.Id, route.UpstreamModel, route.AccountIds.ToArray(), route.Pooled ? "pool" : "strict")))
                throw new ArgumentException("Duplicate route alias.");
        }
        var used = routes.Values.SelectMany(x => x.Accounts).ToHashSet();
        return new(credentialIds.Where(x => used.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value), routes);
    }
}
