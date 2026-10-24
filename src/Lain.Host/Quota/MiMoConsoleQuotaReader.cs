using System.Text.Json;
using Lain.Host.Accounts.ConsoleConnections;
using Lain.Host.Accounts.ConsoleConnections.MiMo;

namespace Lain.Host.Quota;

// Pure normalization, intentionally NOT IQuotaAdapter: no qualified session
// transport, no DI registration and no API-key/Bearer substitution for cookies.
public static class MiMoConsoleQuotaReader
{
    public const string IndividualSource = "https://platform.xiaomimimo.com/api/v1/tokenPlan/usage";
    public static QuotaFetchResult Individual(JsonElement identity, JsonElement payload,
        ConsoleResourceBinding binding, DateTimeOffset observedAt)
    {
        if (!MiMoConsoleIdentity.Matches(identity, binding)) return new([], "Console identity or region does not match.");
        if (!MiMoConsoleIdentity.TrySuccessData(payload, out var data)) return new([], "MiMo console read unsuccessful.");
        var items = JsonFields.Object(JsonFields.Object(data, "usage"), "items");
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 128)
            return new([], "MiMo individual usage schema unavailable.");
        var rows = new List<QuotaReading>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        bool partial = false;
        foreach (var item in items.EnumerateArray())
        {
            var name = JsonFields.String(item, "name");
            if (!MiMoConsoleIdentity.IsIdentifier(name) || !names.Add(name!))
                return new([], "MiMo individual usage bucket identity invalid.");
            var used = JsonFields.Number(item, "used");
            var limit = JsonFields.Number(item, "limit");
            var percent = JsonFields.Number(item, "percent");
            if (percent > 1) percent = null;
            if (used is null || limit is null || percent is null) partial = true;
            if (used is null && limit is null && percent is null) continue;
            rows.Add(new(IndividualSource + "#" + name, "weighted-credits", Used: used, Limit: limit,
                Remaining: used.HasValue && limit.HasValue && used <= limit ? limit - used : null,
                InheritAccountPool: false, WindowLabel: "Individual Token Plan", UsedPercent: percent * 100,
                WindowId: name, MeasurementKind: "plan-credit-pool", Scope: "user", ScopeId: binding.Subject,
                SourceUrl: IndividualSource, ObservedAt: observedAt));
        }
        // currentPeriodEnd is validity, not an automatically known refill/reset.
        return new(rows, partial ? "MiMo individual usage partially measured." : rows.Count == 0 ? "MiMo individual usage unknown." : null);
    }

    // Team normalization is separate: a scalar team percentage is not a credit
    // total, member/seat measurement or the Individual 0..1 fraction convention.
    // Caller must separately verify project membership; this is fixture-only.
    public static QuotaFetchResult TeamPercent(JsonElement payload, string projectId, DateTimeOffset observedAt)
    {
        if (!MiMoConsoleIdentity.IsIdentifier(projectId)) return new([], "MiMo team project invalid.");
        if (!MiMoConsoleIdentity.TrySuccessData(payload, out var data)) return new([], "MiMo console read unsuccessful.");
        var percent = JsonFields.Number(data, "usedPercent");
        if (percent is null or > 100) return new([], "MiMo team percentage unknown.");
        var source = "https://platform.xiaomimimo.com/api/v1/project/" + projectId + "/teamTokenPlan/usage";
        return new([new(source, "percent", UsedPercent: percent, ResetsAt: JsonFields.Date(data, "nextResetTime"),
            InheritAccountPool: false, WindowLabel: "Team Token Plan", WindowId: "team-usage",
            MeasurementKind: "plan-credit-pool", Scope: "project", ScopeId: projectId, SourceUrl: source,
            ObservedAt: observedAt)]);
    }
}
