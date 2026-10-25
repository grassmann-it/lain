using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Operations;

public sealed class RevisionConflictException(long expected, long actual)
    : Exception($"Configuration revision conflict: expected {expected}, current {actual}.")
{
    public long Expected { get; } = expected;
    public long Actual { get; } = actual;
}

public sealed class ConfigurationRepository
{
    private readonly OwnStatePaths paths;
    private readonly object gate = new();
    private ConfigurationDto configuration;
    public ConfigurationRepository(OwnStatePaths paths)
    {
        this.paths = paths;
        configuration = File.Exists(paths.Configuration)
            ? JsonSerializer.Deserialize<ConfigurationDto>(File.ReadAllText(paths.Configuration)) ?? throw new InvalidDataException("Invalid configuration.")
            : new(0, 0, [], [], [], ReadInitialMode(paths));
        configuration = Clone(configuration);
        ValidateMode(configuration.RuntimeMode);
        ValidateCollections(configuration);
    }
    public ConfigurationDto Snapshot { get { lock (gate) return Clone(configuration); } }
    public ConfigurationDto Update(long expectedRevision, Func<ConfigurationDto, ConfigurationDto> change)
    {
        lock (gate)
        {
            if (expectedRevision != configuration.Revision) throw new RevisionConflictException(expectedRevision, configuration.Revision);
            var next = change(Clone(configuration)) with { Revision = checked(configuration.Revision + 1), AppliedRevision = configuration.AppliedRevision };
            next = next with { Collections = next.Collections ?? [] };
            // Route deletion is not restricted to ModelsService. Remove only references to routes
            // that existed before this mutation; newly introduced unknown references remain errors.
            var removed = configuration.Routes.Select(route => route.Id).Except(next.Routes.Select(route => route.Id), StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
            if (removed.Count > 0)
                next = next with { Collections = next.Collections.Select(collection => collection is null
                    ? throw new ArgumentException("Invalid collection.") : collection with
                {
                    RouteIds = collection.RouteIds?.Where(id => !removed.Contains(id)).ToArray()!
                }).ToArray() };
            ValidateMode(next.RuntimeMode);
            if (next.Accounts.Select(x => x.Id).Distinct().Count() != next.Accounts.Length ||
                next.Routes.Select(x => x.Id).Distinct().Count() != next.Routes.Length ||
                next.Profiles.Select(x => x.Id).Distinct().Count() != next.Profiles.Length)
                throw new ArgumentException("Duplicate configuration identities.");
            ValidateCollections(next);
            Save(next);
            return Clone(next);
        }
    }
    public void MarkApplied(long revision)
    {
        lock (gate)
        {
            if (revision != configuration.Revision) throw new RevisionConflictException(revision, configuration.Revision);
            Save(configuration with { AppliedRevision = revision });
        }
    }
    private void Save(ConfigurationDto next)
    {
        OwnStatePaths.WritePrivate(paths.Configuration, JsonSerializer.Serialize(next));
        configuration = Clone(next);
    }
    private static ConfigurationDto Clone(ConfigurationDto value) => value with
    {
        Accounts = value.Accounts.Select(account => account with
        {
            Credentials = account.Credentials?.Select(handle => handle with { Purposes = handle.Purposes.ToArray() }).ToArray(),
            Capabilities = account.Capabilities?.ToArray()
        }).ToArray(),
        Routes = value.Routes.Select(x => x with { AccountIds = x.AccountIds.ToArray(), Policy = ClonePolicy(x.Policy) }).ToArray(),
        Profiles = value.Profiles.Select(x => x with { RouteIds = x.RouteIds.ToArray(), Policy = ClonePolicy(x.Policy) }).ToArray(),
        Collections = (value.Collections ?? []).Select(collection => collection is null
            ? throw new ArgumentException("Invalid collection.")
            : collection with { RouteIds = collection.RouteIds?.ToArray()! }).ToArray()
    };
    private static RoutingPolicyDto? ClonePolicy(RoutingPolicyDto? policy) => policy is null ? null : policy with { FallbackRouteIds = policy.FallbackRouteIds?.ToArray() };

    private static void ValidateCollections(ConfigurationDto value)
    {
        var collections = value.Collections ?? [];
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var routeIds = value.Routes.Select(route => route.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var collection in collections)
        {
            if (collection is null || string.IsNullOrEmpty(collection.Id) || collection.Id.Length > 100 ||
                collection.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
                throw new ArgumentException("Invalid collection identity.");
            if (!identities.Add(collection.Id)) throw new ArgumentException("Duplicate collection identities.");
            if (string.IsNullOrWhiteSpace(collection.Name) || collection.Name.Length > 256 || collection.Name.Any(char.IsControl))
                throw new ArgumentException("Invalid collection name.");
            if (collection.RouteIds is null || collection.RouteIds.Distinct(StringComparer.Ordinal).Count() != collection.RouteIds.Length)
                throw new ArgumentException("Collection route IDs must be distinct.");
            if (collection.RouteIds.Any(id => !routeIds.Contains(id))) throw new ArgumentException("Collection references an unknown route.");
            if (collection.RouterTargetId is not null) throw new ArgumentException("Collection routing is not yet qualified.");
        }
    }

    private static string ReadInitialMode(OwnStatePaths paths)
    {
        var selector = Path.Combine(paths.Root, "initial-runtime-mode");
        if (!File.Exists(selector)) return "gateway";
        if (new FileInfo(selector).LinkTarget is not null || new FileInfo(selector).Length > 32)
            throw new InvalidDataException("Invalid initial operating mode.");
        var mode = File.ReadAllText(selector).Trim();
        ValidateMode(mode);
        return mode;
    }

    private static void ValidateMode(string mode)
    {
        if (mode is not ("gateway" or "monitor")) throw new ArgumentException("Operating mode must be gateway or monitor.");
    }
}
