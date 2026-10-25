using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lain.Core;
using Lain.Host.Operations;
using Lain.Host.Providers;

namespace Lain.Host.Models;

public sealed record CoreCollectionRouter(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("profileId")] string ProfileId,
    [property: JsonPropertyName("selection")] string Selection,
    [property: JsonPropertyName("costPolicy")] string CostPolicy,
    [property: JsonPropertyName("routeIds")] string[] RouteIds,
    [property: JsonPropertyName("memberFingerprints")] IReadOnlyDictionary<string, string> MemberFingerprints);

/// <summary>In-process evidence from actual profile/transport compilation, never supplied by the HTTP caller.</summary>
public sealed class CollectionRouterExportProof
{
    private readonly string fingerprint;
    internal IReadOnlyDictionary<string, CoreCollectionRouter> Routers { get; }
    internal CollectionRouterExportProof(string fingerprint, IReadOnlyDictionary<string, CoreCollectionRouter>? routers)
    {
        this.fingerprint = fingerprint;
        Routers = (routers ?? new Dictionary<string, CoreCollectionRouter>()).ToDictionary(pair => pair.Key, pair => pair.Value with {
            RouteIds = pair.Value.RouteIds.ToArray(),
            MemberFingerprints = pair.Value.MemberFingerprints.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal)
        }, StringComparer.Ordinal);
    }
    internal bool Matches(ConfigurationDto configuration, string profileId) =>
        fingerprint == CollectionRouterCompiler.SnapshotFingerprint(configuration, profileId);
}

/// <summary>Ordered pre-dispatch selection, never cross-route retry. References existing bindings only.</summary>
public static class CollectionRouterCompiler
{
    public const string Selection = "ordered_sticky_v1";

    public static CollectionRouterExportProof CompileForExport(ConfigurationDto configuration, string profileId,
        ProxyTransportDescription transport, ProviderCatalogRegistry? catalog = null)
    {
        catalog ??= new();
        var profile = configuration.Profiles.SingleOrDefault(p => p.Id == profileId) ?? throw new ArgumentException("Unknown router export profile.");
        var effective = configuration with { Routes = new ProviderCostPolicy(catalog).ResolveProfile(configuration, profile) };
        var bindings = RouteBindings.Compile(effective, transport.CredentialIds, transport.RouteCredentials, catalog, profileId,
            transport.QualifiedProviderRouteIds, transport.TransportBindings);
        return new(SnapshotFingerprint(effective, profileId), bindings.Routers);
    }

    internal static string SnapshotFingerprint(ConfigurationDto configuration, string profileId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            configuration.Revision, ProfileId = profileId,
            Profile = configuration.Profiles.Single(p => p.Id == profileId), configuration.Routes,
            configuration.Collections, configuration.CollectionRouters,
            Accounts = configuration.Accounts.Select(account => new { account.Id, account.Provider, account.Enabled, account.Generation })
        })))).ToLowerInvariant();

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
        else if (router.GroupId.StartsWith("account:", StringComparison.Ordinal))
        {
            var accountId = router.GroupId[8..];
            _ = configuration.Accounts.SingleOrDefault(account => account.Id == accountId)
                ?? throw new ArgumentException("Router references an unknown account group.");
            ids = profile.RouteIds.Where(id => configuration.Routes.Any(route => route.Id == id &&
                !route.Pooled && route.AccountIds.Length == 1 && route.AccountIds[0] == accountId));
        }
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
        IReadOnlyDictionary<string, CoreRouteBinding> routes, IReadOnlySet<string>? qualifiedRoutes,
        IReadOnlyDictionary<string, ModelCompatibilityTransportBinding>? transports = null)
    {
        var routers = (configuration.CollectionRouters ?? []).Where(r => r.Enabled && r.ProfileId == profileId).ToArray();
        if (routers.Length == 0) return null;
        var profile = configuration.Profiles.Single(p => p.Id == profileId);
        var byId = routes.Values.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var result = new Dictionary<string, CoreCollectionRouter>(StringComparer.Ordinal);
        foreach (var router in routers)
        {
            EnsureUnambiguousProvider(configuration, profile, router);
            var members = MemberIds(configuration, profile, router).Where(id => byId.TryGetValue(id, out var route) &&
                route.Visible && route.Selection == "strict" && route.Accounts.Length == 1 &&
                (!router.GroupId.StartsWith("account:", StringComparison.Ordinal) ||
                    route.Accounts[0] == router.GroupId[8..] && configuration.Accounts.Any(account => account.Id == route.Accounts[0] && account.Enabled)) &&
                transports is not null && transports.TryGetValue(id, out var transport) &&
                transport.Provider == route.Provider && transport.UpstreamModel == route.Model &&
                transport.UpstreamProtocol == route.UpstreamProtocol && !string.IsNullOrWhiteSpace(transport.Source) &&
                (route.UpstreamProtocol is "openai" or "responses") &&
                !(route.Provider == "codex" && route.Model == "codex-auto-review") &&
                (router.CostPolicy != "free_only" || qualifiedRoutes?.Contains(id) == true && route.CostPolicy == "free_only" && route.FreeOnlyEnforcement is not null)).ToArray();
            if (members.Length == 0) throw new CollectionRouterValidationException(CollectionRouterFailureReason.NoEligibleMembers, router);
            if (routes.ContainsKey(router.Alias)) throw new CollectionRouterValidationException(CollectionRouterFailureReason.AliasConflict, router);
            result.Add(router.Alias, new(router.Id, router.ProfileId, Selection, router.CostPolicy, members,
                members.ToDictionary(id => id, id => Fingerprint(byId[id]), StringComparer.Ordinal)));
        }
        return result;
    }

    // Configuration validation remains structural: adding an account must not be blocked by
    // an older provider router. Only compiling/exporting that ambiguous router fails closed.
    internal static void EnsureUnambiguousProvider(ConfigurationDto configuration, ProfileDto profile, CollectionRouterDto router)
    {
        if (!router.GroupId.StartsWith("provider:", StringComparison.Ordinal)) return;
        var provider = router.GroupId[9..];
        var members = configuration.Routes.Where(route => profile.RouteIds.Contains(route.Id) &&
            string.Equals(route.Provider, provider, StringComparison.OrdinalIgnoreCase));
        if (DerivedModelGroups.Accounts(configuration, members).Length > 1)
            throw new CollectionRouterValidationException(CollectionRouterFailureReason.ProviderAccountsAmbiguous, router);
    }

    private static string Fingerprint(CoreRouteBinding route) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("\n", route.Id, route.Provider, route.Model, route.UpstreamProtocol, route.Accounts[0], route.CostPolicy, route.FreeOnlyEnforcement)))).ToLowerInvariant();
}
