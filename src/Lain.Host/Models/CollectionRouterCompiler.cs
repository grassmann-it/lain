using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Lain.Core;

namespace Lain.Host.Models;

public sealed record CoreCollectionRouter(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("profileId")] string ProfileId,
    [property: JsonPropertyName("selection")] string Selection,
    [property: JsonPropertyName("costPolicy")] string CostPolicy,
    [property: JsonPropertyName("routeIds")] string[] RouteIds,
    [property: JsonPropertyName("memberFingerprints")] IReadOnlyDictionary<string, string> MemberFingerprints);

/// <summary>Ordered pre-dispatch selection, never cross-route retry. References existing bindings only.</summary>
public static class CollectionRouterCompiler
{
    public const string Selection = "ordered_sticky_v1";

    public static void Validate(ConfigurationDto configuration)
    {
        if ((configuration.CollectionRouters?.Length ?? 0) > 128) throw new ArgumentException("At most 128 collection routers are supported.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var aliases = configuration.Routes.Select(route => route.Alias).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var router in configuration.CollectionRouters ?? [])
        {
            if (router is null || string.IsNullOrEmpty(router.Id) || router.Id.Length > 100 ||
                router.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_') || !ids.Add(router.Id))
                throw new ArgumentException("Router identities must be unique and valid.");
            if (string.IsNullOrWhiteSpace(router.Alias) || router.Alias.Length > 256 || router.Alias.Any(char.IsControl) || !aliases.Add(router.Alias))
                throw new ArgumentException("Router aliases must be distinct from all model and router aliases.");
            if (router.CostPolicy is not ("configured" or "free_only")) throw new ArgumentException("Unknown router cost policy.");
            if (string.IsNullOrWhiteSpace(router.GroupId) || string.IsNullOrWhiteSpace(router.ProfileId)) throw new ArgumentException("Router group and profile are required.");
            var profile = configuration.Profiles.SingleOrDefault(p => p.Id == router.ProfileId)
                ?? throw new ArgumentException("Router references an unknown profile.");
            if (!groups.Add(router.ProfileId + "\n" + router.GroupId)) throw new ArgumentException("A profile group can have only one router.");
            _ = MemberIds(configuration, profile, router);
        }
        foreach (var collection in configuration.Collections ?? [])
            if (collection.RouterTargetId is not null && !(configuration.CollectionRouters ?? []).Any(router =>
                router.Id == collection.RouterTargetId && router.GroupId == "collection:" + collection.Id))
                throw new ArgumentException("Collection router target must reference its own router.");
    }

    public static string[] MemberIds(ConfigurationDto configuration, ProfileDto profile, CollectionRouterDto router)
    {
        IEnumerable<string> ids;
        if (router.GroupId.StartsWith("collection:", StringComparison.Ordinal))
            ids = (configuration.Collections ?? []).SingleOrDefault(c => "collection:" + c.Id == router.GroupId)?.RouteIds
                ?? throw new ArgumentException("Router references an unknown collection.");
        else if (router.GroupId.StartsWith("provider:", StringComparison.Ordinal))
        {
            var provider = router.GroupId[9..];
            if (!configuration.Routes.Any(r => r.Provider.ToLowerInvariant() == provider)) throw new ArgumentException("Router references an unknown provider group.");
            ids = profile.RouteIds.Where(id => configuration.Routes.Any(r => r.Id == id && r.Provider.ToLowerInvariant() == provider));
        }
        else if (router.GroupId == "policy:free" && router.CostPolicy == "free_only") ids = profile.RouteIds;
        else throw new ArgumentException("Unknown router group or free group without free_only policy.");
        return ids.Where(profile.RouteIds.Contains).ToArray();
    }

    public static IReadOnlyDictionary<string, CoreCollectionRouter>? Compile(ConfigurationDto configuration, string? profileId,
        IReadOnlyDictionary<string, CoreRouteBinding> routes, IReadOnlySet<string>? qualifiedRoutes)
    {
        var routers = (configuration.CollectionRouters ?? []).Where(r => r.Enabled && r.ProfileId == profileId).ToArray();
        if (routers.Length == 0) return null;
        var profile = configuration.Profiles.Single(p => p.Id == profileId);
        var byId = routes.Values.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var result = new Dictionary<string, CoreCollectionRouter>(StringComparer.Ordinal);
        foreach (var router in routers)
        {
            var members = MemberIds(configuration, profile, router).Where(id => byId.TryGetValue(id, out var route) &&
                route.Visible && route.Selection == "strict" && route.Accounts.Length == 1 && qualifiedRoutes?.Contains(id) == true &&
                (route.UpstreamProtocol is "openai" or "responses") &&
                !(route.Provider == "codex" && route.Model == "codex-auto-review") &&
                (router.CostPolicy != "free_only" || route.CostPolicy == "free_only" && route.FreeOnlyEnforcement is not null)).ToArray();
            if (members.Length == 0) throw new ArgumentException("Router has no visible, strict, qualified profile members under its cost policy.");
            if (routes.ContainsKey(router.Alias)) throw new ArgumentException("Router alias collides with a model route.");
            result.Add(router.Alias, new(router.Id, router.ProfileId, Selection, router.CostPolicy, members,
                members.ToDictionary(id => id, id => Fingerprint(byId[id]), StringComparer.Ordinal)));
        }
        return result;
    }

    private static string Fingerprint(CoreRouteBinding route) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("\n", route.Id, route.Provider, route.Model, route.UpstreamProtocol, route.Accounts[0], route.CostPolicy, route.FreeOnlyEnforcement)))).ToLowerInvariant();
}
