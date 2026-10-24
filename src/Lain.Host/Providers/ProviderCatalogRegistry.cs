using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Lain.Host.Accounts.Monitoring;
using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Providers;

/// <summary>Secret-free documentation. A catalog entry never authorizes paid usage or proves a live account's tier.</summary>
public sealed class ProviderCatalogRegistry
{
    private readonly ProviderDescriptorDto[] providers;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public ProviderCatalogRegistry() : this(ReadEmbedded(typeof(ProviderCatalogRegistry).Assembly)) { }

    public ProviderCatalogRegistry(IEnumerable<ProviderDescriptorDto> descriptors)
    {
        providers = descriptors.Select(p => Prepare(Clone(p))).OrderBy(p => p.Id, StringComparer.Ordinal).ToArray();
        if (providers.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != providers.Length)
            throw new InvalidDataException("Duplicate provider catalog identity.");
        foreach (var provider in providers) Validate(provider);
    }

    public ProviderCatalogDto Snapshot() => new(Api.Version, DateTimeOffset.UtcNow, providers.Select(Clone).ToArray());
    public ProviderDescriptorDto? Find(string id) => providers.FirstOrDefault(p => p.Id == id) is { } provider ? Clone(provider) : null;
    public ProviderRouteDescriptorDto? FindRoute(string providerId, string? routeId) =>
        routeId is null ? null : Find(providerId)?.Routes.SingleOrDefault(r => r.Id == routeId);

    public static ProviderDescriptorDto Parse(string json)
    {
        var descriptor = JsonSerializer.Deserialize<ProviderDescriptorDto>(json, Json)
            ?? throw new InvalidDataException("Empty provider manifest.");
        return Prepare(descriptor);
    }

    public ProviderConnectionDto? FindConnection(string providerId, string? connectionId) =>
        connectionId is null ? null : Find(providerId)?.Connections?.SingleOrDefault(c => c.Id == connectionId);

    private static ProviderDescriptorDto Prepare(ProviderDescriptorDto provider)
    {
        Validate(provider);
        var connections = provider.Connections ?? provider.Routes
            .GroupBy(r => (Endpoint: r.BaseUrl.TrimEnd('/'), r.Protocol))
            .OrderBy(g => g.Key.Endpoint, StringComparer.Ordinal).ThenBy(g => g.Key.Protocol, StringComparer.Ordinal)
            .Select(g => new ProviderConnectionDto(
                "connection-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(g.Key.Endpoint + "\0" + g.Key.Protocol))).ToLowerInvariant()[..16],
                provider.Name + " · " + g.Key.Protocol, g.Key.Endpoint, g.Key.Protocol,
                g.SelectMany(r => r.BillingPlanIds).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                g.Select(r => r.Id).Order(StringComparer.Ordinal).ToArray())).ToArray();
        Unique(connections.Select(c => c.Id));
        Unique(connections.Select(c => c.BaseUrl.TrimEnd('/') + "\0" + c.Protocol));
        var membership = new HashSet<string>(StringComparer.Ordinal);
        foreach (var connection in connections)
        {
            Identity(connection.Id); Text(connection.Name); Text(connection.Protocol); EndpointUrl(connection.BaseUrl);
            if (connection.ProviderRouteIds is null || connection.ProviderRouteIds.Length == 0 || connection.BillingPlanIds is null || connection.BillingPlanIds.Length == 0)
                throw new InvalidDataException("Provider connections require model templates and billing plans.");
            Unique(connection.ProviderRouteIds); Unique(connection.BillingPlanIds);
            var routes = connection.ProviderRouteIds.Select(id => provider.Routes.SingleOrDefault(r => r.Id == id)
                ?? throw new InvalidDataException("Provider connection references an unknown model template.")).ToArray();
            if (routes.Any(r => r.Protocol != connection.Protocol || r.BaseUrl.TrimEnd('/') != connection.BaseUrl.TrimEnd('/')) ||
                connection.ProviderRouteIds.Any(id => !membership.Add(id)))
                throw new InvalidDataException("Provider connection models must share one endpoint and protocol without overlapping connections.");
            var plans = routes.SelectMany(r => r.BillingPlanIds).ToHashSet(StringComparer.Ordinal);
            if (!plans.SetEquals(connection.BillingPlanIds))
                throw new InvalidDataException("Provider connection billing plans must match its model templates.");
        }
        if (membership.Count != provider.Routes.Length)
            throw new InvalidDataException("Each model template must belong to exactly one provider connection.");
        var defaultName = provider.DefaultAccountName ?? (provider.Id switch
        {
            "nvidia-build" => "NVIDIA Build", "cloudflare-workers-ai" => "Cloudflare Workers AI",
            "gemini-api" => "Gemini", _ => provider.Name
        });
        Text(defaultName);
        if (defaultName.Length > 120 || defaultName.Any(char.IsControl)) throw new InvalidDataException("Provider default account name is too long.");
        return provider with { Connections = connections, DefaultAccountName = defaultName,
            MonitorAccessOptions = MonitorAccessCatalog.Options(provider.Id) };
    }

    private static IEnumerable<ProviderDescriptorDto> ReadEmbedded(Assembly assembly)
    {
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.Contains(".Providers.Catalog.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException("Missing provider manifest.");
            using var reader = new StreamReader(stream);
            yield return Parse(reader.ReadToEnd());
        }
    }

    private static ProviderDescriptorDto Clone(ProviderDescriptorDto value) =>
        JsonSerializer.Deserialize<ProviderDescriptorDto>(JsonSerializer.Serialize(value, Json), Json)!;

    private static void Validate(ProviderDescriptorDto provider)
    {
        Identity(provider.Id); Text(provider.Name); Text(provider.Summary);
        Url(provider.AccountUrl); Url(provider.DocumentationUrl);
        if (provider.Credentials is null || provider.SetupSteps is null || provider.BillingPlans is null || provider.Routes is null || provider.Sources is null || provider.Restrictions is null)
            throw new InvalidDataException("Provider manifest collections must be present.");
        Unique(provider.Credentials.Select(c => c.Id)); Unique(provider.BillingPlans.Select(b => b.Id)); Unique(provider.Routes.Select(r => r.Id));
        foreach (var credential in provider.Credentials) { Identity(credential.Id); Text(credential.Label); Text(credential.Kind); }
        foreach (var step in provider.SetupSteps) { Text(step.Title); Text(step.Description); if (step.Url is not null) Url(step.Url); }
        foreach (var plan in provider.BillingPlans)
        {
            Identity(plan.Id); Text(plan.Name); Text(plan.Kind); Text(plan.Scope); Text(plan.Unit);
            if (plan.IncludedAmount is < 0 || plan.RecurringPrice is < 0 || (plan.IncludedAmount is { } amount && !double.IsFinite(amount)))
                throw new InvalidDataException("Provider budget amounts must be finite and nonnegative.");
        }
        foreach (var route in provider.Routes)
        {
            Identity(route.Id); Identity(route.Model); Text(route.Name); Text(route.Protocol); EndpointUrl(route.BaseUrl);
            if (route.Capabilities is null || route.BillingPlanIds is null || route.BillingPlanIds.Length == 0 || route.BillingPlanIds.Any(id => !provider.BillingPlans.Any(p => p.Id == id)))
                throw new InvalidDataException("Provider route references an unknown billing plan.");
            if (route.Capabilities.ContextWindow is <= 0 || route.Capabilities.MaximumOutput is <= 0)
                throw new InvalidDataException("Unknown model limits must be null, not zero or negative.");
            Unique(route.BillingPlanIds);
            foreach (var source in route.Sources ?? []) Source(source);
        }
        foreach (var source in provider.Sources) Source(source);
    }

    private static void Source(ProviderSourceDto source) { Url(source.Url); Text(source.Title); }
    private static void Unique(IEnumerable<string> values)
    {
        var all = values.ToArray();
        if (all.Distinct(StringComparer.OrdinalIgnoreCase).Count() != all.Length) throw new InvalidDataException("Duplicate manifest identity.");
    }
    private static void Identity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            throw new InvalidDataException("Invalid manifest identity.");
    }
    private static void Text(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(c => char.IsControl(c) && c != '\n' && c != '\r' && c != '\t'))
            throw new InvalidDataException("Invalid manifest text.");
    }
    private static void Url(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException("Provider documentation and endpoints must use HTTPS without credentials.");
    }
    private static void EndpointUrl(string? value)
    {
        // Catalog templates are documentation. Runtime transport validation separately substitutes and validates
        // the account-scoped value before any request; a placeholder is never an executable endpoint.
        var concrete = value?.Replace("{WorkspaceId}", "catalog-workspace", StringComparison.Ordinal)
            .Replace("{workspaceId}", "catalog-workspace", StringComparison.Ordinal)
            .Replace("{accountId}", "catalog-account", StringComparison.Ordinal);
        if (concrete is not null && (concrete.Contains('{') || concrete.Contains('}')))
            throw new InvalidDataException("Unknown provider endpoint template field.");
        Url(concrete);
        var endpoint = new Uri(concrete!, UriKind.Absolute);
        if (endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
            throw new InvalidDataException("Provider endpoints must not contain query strings or fragments.");
    }
}
