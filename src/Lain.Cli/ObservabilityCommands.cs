using Lain.Client;
using Lain.Core;

namespace Lain.Cli;

public static partial class CliApplication
{
    // Credential values continue to use the dedicated env/stdin API-key path.
    private static readonly string[] AccountSettingNames = ["accountId", "region", "workspaceId", "serviceDeploymentScope", "billingPlan"];
    private static readonly string[] SecretSettingNames = ["apiKey", "api_key", "key", "token", "accessToken", "access_token", "refreshToken", "refresh_token", "authorization"];
    private static Dictionary<string, string>? ParseAccountSettings(Options options)
    {
        if (options.Settings.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(options.Get("provider-route")))
            throw new ArgumentException("--setting requires --provider-route ID.");
        if (options.Settings.Count > 16) throw new ArgumentException("At most 16 nonsecret provider settings may be supplied.");
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var supplied in options.Settings)
        {
            var pair = supplied.Split('=', 2);
            if (pair.Length != 2 || string.IsNullOrWhiteSpace(pair[0]) || pair[0].Length > 80 || pair[0].Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-')) ||
                SecretSettingNames.Contains(pair[0], StringComparer.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(pair[1]) || pair[1].Length > 4096 || pair[1].Any(char.IsControl))
                throw new ArgumentException("--setting requires NAME=VALUE for a declared nonsecret provider field or supported provider setting. Credentials require dedicated secret input.");
            if (!settings.TryAdd(pair[0], pair[1])) throw new ArgumentException("Duplicate provider setting name.");
        }
        return settings;
    }
    private static async Task ValidateAccountSettings(LainApiClient client, string providerId, Dictionary<string, string> settings, CancellationToken ct)
    {
        var catalog = await client.ProvidersAsync(ct);
        var provider = catalog.Providers.SingleOrDefault(p => p.Id == providerId)
            ?? throw new ArgumentException("Provider settings require a catalog provider.");
        foreach (var name in settings.Keys)
        {
            var field = provider.Credentials.FirstOrDefault(f => f.Id == name);
            if (field?.Secret == true || (field is null && !AccountSettingNames.Contains(name, StringComparer.Ordinal)))
                throw new ArgumentException("Provider setting is undeclared or requires dedicated secret input.");
        }
    }
    private static readonly string[] UsageFilters = ["json", "today", "from", "to", "timezone", "account", "model", "status",
        "route", "model-family", "manufacturer", "inference-provider", "gateway", "billing-account", "reasoning", "mode",
        "conversation", "turn", "worker", "harness", "profile", "effective-reasoning-effort"];
    private static readonly string[] GroupDimensions = ["accountId", "routeId", "model", "provider", "modelFamily", "manufacturer",
        "inferenceProvider", "gateway", "billingAccountId", "reasoningEffort", "mode", "conversationId", "turnId", "workerId",
        "parentWorkerId", "parentRequestId", "toolCallId", "harness", "profileId", "responseId", "previousResponseId", "effectiveReasoningEffort"];

    private static void ValidateUsage(string command, Options options)
    {
        var analysis = command == "usage" && options.Positionals.Count > 0;
        options.RequirePositionals(analysis ? 1 : 0);
        if (analysis && options.Positionals[0] is not ("analysis" or "aggregate" or "performance"))
            throw new ArgumentException("Usage action must be analysis, aggregate or performance.");
        options.RequireOnly([.. UsageFilters, .. (analysis ? new[] { "group-by", "group-limit" } : new[] { "offset", "limit" })]);
        if (options.Get("group-by") is { } groups)
        {
            var dimensions = groups.Split(',');
            if (dimensions.Length is < 1 or > 3 || dimensions.Distinct(StringComparer.Ordinal).Count() != dimensions.Length ||
                dimensions.Any(d => !GroupDimensions.Contains(d, StringComparer.Ordinal)))
                throw new ArgumentException("--group-by requires 1 to 3 distinct dimensions: " + string.Join(",", GroupDimensions) + ".");
        }
        _ = ParseQuery(options);
    }

    private static void ValidateInspector(Options options)
    {
        var events = options.Positionals.FirstOrDefault() == "events";
        options.RequirePositionals(options.Positionals.Count == 0 ? 0 : 1);
        if (options.Positionals.Count > 0 && options.Positionals[0] is not ("snapshot" or "events"))
            throw new ArgumentException("Inspector action must be snapshot or events.");
        options.RequireOnly(events ? ["json", "source", "after", "request", "limit"] : ["json", "request", "limit"]);
        _ = InspectorLimit(options);
        var after = InspectorAfter(options);
        if (after > 0 && string.IsNullOrWhiteSpace(options.Get("source")))
            throw new ArgumentException("--source is required with --after greater than zero; cursors belong to one inspector source.");
    }

    private static int InspectorLimit(Options options)
    {
        var limit = options.Get("limit") is { } text ? ParseInt(text, "limit") : 100;
        if (limit is < 1 or > 500) throw new ArgumentException("--limit must be between 1 and 500.");
        return limit;
    }
    private static long InspectorAfter(Options options) => options.Get("after") is { } text ? ParseLong(text, "after") : 0;

    private static async Task<object> Inspector(LainApiClient client, Options options, CancellationToken ct)
    {
        if (options.Positionals.FirstOrDefault() == "events")
            return await client.InspectorEventsAsync(new InspectorEventQuery(options.Get("source"), InspectorAfter(options),
                InspectorLimit(options), options.Get("request")), ct);
        return await client.InspectorAsync(new InspectorQuery(options.Get("request"), InspectorLimit(options)), ct);
    }
    private static void ValidateProviders(Options options)
    {
        var action = options.Positionals.FirstOrDefault() ?? "catalog";
        switch (action)
        {
            case "catalog":
                options.RequireOnly("json");
                options.RequirePositionals(options.Positionals.Count == 0 ? 0 : 1);
                break;
            case "onboarding":
                options.RequireOnly("json"); options.RequirePositionals(2);
                break;
            case "routes":
                options.RequireOnly("json", "provider"); options.RequirePositionals(1);
                break;
            case "check-free":
                options.RequireOnly("json"); options.RequirePositionals(2);
                break;
            default: throw new ArgumentException("Provider action must be catalog, onboarding, routes or check-free.");
        }
    }

    private static async Task<object> Providers(LainApiClient client, Options options, CancellationToken ct)
    {
        var action = options.Positionals.FirstOrDefault() ?? "catalog";
        if (action == "onboarding") return await client.ProviderOnboardingAsync(options.Positionals[1], ct);
        if (action == "check-free") return await client.RouteCostPolicyAsync(options.Positionals[1], "free_only", ct);
        var catalog = await client.ProvidersAsync(ct);
        if (action != "routes") return catalog;
        if (options.Get("provider") is not { } id) return catalog.Providers.SelectMany(p => p.Routes).ToArray();
        var provider = catalog.Providers.SingleOrDefault(p => p.Id == id)
            ?? throw new ArgumentException("The provider is not present in the catalog.");
        return provider.Routes;
    }
}
