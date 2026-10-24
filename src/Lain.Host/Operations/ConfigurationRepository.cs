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
        ValidateMode(configuration.RuntimeMode);
    }
    public ConfigurationDto Snapshot { get { lock (gate) return Clone(configuration); } }
    public ConfigurationDto Update(long expectedRevision, Func<ConfigurationDto, ConfigurationDto> change)
    {
        lock (gate)
        {
            if (expectedRevision != configuration.Revision) throw new RevisionConflictException(expectedRevision, configuration.Revision);
            var next = change(Clone(configuration)) with { Revision = checked(configuration.Revision + 1), AppliedRevision = configuration.AppliedRevision };
            ValidateMode(next.RuntimeMode);
            if (next.Accounts.Select(x => x.Id).Distinct().Count() != next.Accounts.Length ||
                next.Routes.Select(x => x.Id).Distinct().Count() != next.Routes.Length ||
                next.Profiles.Select(x => x.Id).Distinct().Count() != next.Profiles.Length)
                throw new ArgumentException("Duplicate configuration identities.");
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
        Profiles = value.Profiles.Select(x => x with { RouteIds = x.RouteIds.ToArray(), Policy = ClonePolicy(x.Policy) }).ToArray()
    };
    private static RoutingPolicyDto? ClonePolicy(RoutingPolicyDto? policy) => policy is null ? null : policy with { FallbackRouteIds = policy.FallbackRouteIds?.ToArray() };

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
