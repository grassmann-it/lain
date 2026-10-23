using Lain.Core;
using Lain.Host.Operations;

namespace Lain.Host.Models;

public record ExportAccountDto(string Id, string Provider);
public record ProfileExportDto(int ContractVersion, long Revision, ProfileDto Profile, RouteDto[] Routes, ExportAccountDto[] Accounts);
public record AvailableModelDto(string Id, string Provider, string[] AccountIds, string Source);
public record ModelCatalogDto(int ContractVersion, long Revision, AvailableModelDto[] Models);

/// <summary>Mutations use the same revision gate as account changes; applying remains a separate operation.</summary>
public sealed class ModelsService(ConfigurationRepository repository)
{
    public ConfigurationDto Snapshot => repository.Snapshot;

    public ConfigurationDto PutRoute(long revision, RouteDto route) => repository.Update(revision, config =>
    {
        ValidateRoute(config, route);
        return config with { Routes = config.Routes.Where(x => x.Id != route.Id).Append(route).ToArray() };
    });

    public ConfigurationDto DeleteRoute(long revision, string id) => repository.Update(revision, config =>
    {
        if (!config.Routes.Any(x => x.Id == id)) throw new KeyNotFoundException("Route does not exist.");
        if (config.Profiles.Any(x => x.RouteIds.Contains(id))) throw new ArgumentException("Route is selected by a profile.");
        return config with { Routes = config.Routes.Where(x => x.Id != id).ToArray() };
    });

    public ConfigurationDto PutProfile(long revision, ProfileDto profile) => repository.Update(revision, config =>
    {
        ArgumentNullException.ThrowIfNull(profile);
        Identity(profile.Id);
        Text(profile.Name, "Profile name");
        if (profile.RouteIds is null || profile.RouteIds.Length == 0 || profile.RouteIds.Distinct().Count() != profile.RouteIds.Length)
            throw new ArgumentException("A profile needs distinct route IDs.");
        if (profile.RouteIds.Any(id => !config.Routes.Any(x => x.Id == id))) throw new ArgumentException("Profile references an unknown route.");
        if (profile.DefaultRouteId is not null && !profile.RouteIds.Contains(profile.DefaultRouteId))
            throw new ArgumentException("The default route must belong to the profile.");
        return config with { Profiles = config.Profiles.Where(x => x.Id != profile.Id).Append(profile).ToArray() };
    });

    public ConfigurationDto DeleteProfile(long revision, string id) => repository.Update(revision, config =>
    {
        if (!config.Profiles.Any(x => x.Id == id)) throw new KeyNotFoundException("Profile does not exist.");
        return config with { Profiles = config.Profiles.Where(x => x.Id != id).ToArray() };
    });

    public ProfileExportDto Export(string id)
    {
        var config = Snapshot;
        var profile = config.Profiles.SingleOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException("Profile does not exist.");
        var routes = profile.RouteIds.Select(id => config.Routes.Single(x => x.Id == id)).ToArray();
        var accountIds = routes.SelectMany(x => x.AccountIds).Distinct().Order(StringComparer.Ordinal).ToArray();
        var placeholders = accountIds.Select((id, index) => (id, placeholder: $"account-{index + 1}")).ToDictionary(x => x.id, x => x.placeholder);
        return new(Api.Version, config.Revision, profile,
            routes.Select(x => x with { AccountIds = x.AccountIds.Select(id => placeholders[id]).ToArray() }).ToArray(),
            accountIds.Select(id => new ExportAccountDto(placeholders[id], config.Accounts.Single(x => x.Id == id).Provider)).ToArray());
    }

    public (ConfigurationDto Configuration, ProfileDto Profile, RouteDto[] Routes) ResolveForApply(string id, long revision)
    {
        var config = Snapshot;
        if (config.Revision != revision) throw new RevisionConflictException(revision, config.Revision);
        var profile = config.Profiles.SingleOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException("Profile does not exist.");
        var routes = profile.RouteIds.Select(id => config.Routes.Single(x => x.Id == id)).ToArray();
        foreach (var route in routes) ValidateRoute(config, route);
        if (!routes.Any(x => x.Enabled && x.Visible)) throw new ArgumentException("Profile has no enabled visible route.");
        if (profile.DefaultRouteId is not null && !routes.Any(x => x.Id == profile.DefaultRouteId && x.Enabled && x.Visible))
            throw new ArgumentException("Default route must be enabled and visible.");
        return (config, profile, routes);
    }

    public ModelCatalogDto ConfiguredModels()
    {
        var config = Snapshot;
        return new(Api.Version, config.Revision, config.Routes.Where(x => x.Enabled)
            .GroupBy(x => (x.UpstreamModel, x.Provider))
            .Select(g => new AvailableModelDto(g.Key.UpstreamModel, g.Key.Provider, g.SelectMany(x => x.AccountIds).Distinct().ToArray(), "configured"))
            .OrderBy(x => x.Provider).ThenBy(x => x.Id).ToArray());
    }

    private static void ValidateRoute(ConfigurationDto config, RouteDto route)
    {
        ArgumentNullException.ThrowIfNull(route);
        Identity(route.Id);
        Text(route.Alias, "Alias"); Text(route.UpstreamModel, "Upstream model"); Text(route.Provider, "Provider");
        if (route.AccountIds is null || route.AccountIds.Length == 0 || route.AccountIds.Distinct().Count() != route.AccountIds.Length)
            throw new ArgumentException("A route needs distinct account IDs.");
        if (!route.Pooled && route.AccountIds.Length != 1) throw new ArgumentException("Multiple accounts require an explicit pool.");
        if (config.Routes.Any(x => x.Id != route.Id && string.Equals(x.Alias, route.Alias, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Route alias is already in use.");
        foreach (var id in route.AccountIds)
        {
            var account = config.Accounts.SingleOrDefault(x => x.Id == id) ?? throw new ArgumentException("Route references an unknown account.");
            if (!string.Equals(account.Provider, route.Provider, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Account provider does not match route provider.");
            if (route.Enabled && !account.Enabled) throw new ArgumentException("Enabled routes cannot select disabled accounts.");
        }
    }

    private static void Identity(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 100 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new ArgumentException("Invalid identity.");
    }

    private static void Text(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl)) throw new ArgumentException($"Invalid {label}.");
    }
}
