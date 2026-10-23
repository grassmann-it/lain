using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Quota;

public sealed class CommandCodeQuotaAdapter(HttpClient client) : IQuotaAdapter
{
    public const string Api = "https://api.commandcode.ai/alpha/";
    public string Source => Api + "billing/credits";
    public bool Supports(string provider) => provider.Equals("commandcode", StringComparison.OrdinalIgnoreCase)
        || provider.Equals("command-code", StringComparison.OrdinalIgnoreCase);

    // Read-only reference: modelcoach/provider_status.py normalize_commandcode.
    // These account plan values are NOT documented per-model monthly ceilings.
    private static readonly IReadOnlyDictionary<string, double> PlanCredits = new Dictionary<string, double>
    {
        ["individual-go"] = 10, ["individual-go-v1"] = 10, ["individual-goat"] = 70,
        ["individual-pro"] = 30, ["individual-pro-v1"] = 80, ["individual-provider"] = 15,
        ["individual-max"] = 150, ["individual-ultra"] = 300, ["teams-pro"] = 40
    };

    public async Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        if (account.CredentialKind != "api-key")
            throw new QuotaFetchException("Command Code quota requires a qualified API key.");
        using var whoami = await QuotaHttp.GetAsync(client, Api + "whoami?limits=1", credential, ct);
        var orgId = JsonFields.String(JsonFields.Object(whoami.RootElement, "org"), "id");
        var query = orgId is null ? "" : "?orgId=" + Uri.EscapeDataString(orgId);
        var errors = new List<string>();
        async Task<JsonDocument?> Optional(string path)
        {
            try { return await QuotaHttp.GetAsync(client, Api + path + query, credential, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is QuotaFetchException or HttpRequestException or JsonException or OperationCanceledException)
            {
                // No exception messages or response bodies: they can contain provider secrets.
                errors.Add(path + " unavailable.");
                return null;
            }
        }
        // Sequential, so partial diagnostics and provider requests are deterministic.
        using var billing = await Optional("billing/credits");
        using var subscription = await Optional("billing/subscriptions");
        var rows = Normalize(billing?.RootElement ?? default, subscription?.RootElement ?? default, whoami.RootElement, orgId);
        return new(rows, errors.Count > 0 ? string.Join(" ", errors) : rows.Count == 0 ? "No recognized quota fields; remaining budget unknown." : null);
    }

    private static List<QuotaReading> Normalize(JsonElement billing, JsonElement subscription, JsonElement whoami, string? orgId)
    {
        var rows = new List<QuotaReading>();
        var credits = JsonFields.Object(billing, "credits");
        var sub = JsonFields.Object(subscription, "data");
        var active = JsonFields.String(sub, "status") == "active";
        var plan = JsonFields.String(sub, "planId") ?? JsonFields.String(credits, "planId");
        var monthly = JsonFields.Number(credits, "monthlyCredits");
        double? total = active && monthly.HasValue && plan is not null && PlanCredits.TryGetValue(plan, out var cap)
            ? Math.Max(cap, monthly.Value) : null;
        if (monthly.HasValue) rows.Add(new(SourceUrl("monthlyCredits"), "USD-Credits",
            total - monthly, total, monthly, active ? JsonFields.Date(sub, "currentPeriodEnd") : null));
        foreach (var key in new[] { "purchasedCredits", "freeCredits" })
        {
            var remaining = JsonFields.Number(credits, key);
            if (remaining.HasValue) rows.Add(new(SourceUrl(key), "USD-Credits", Remaining: remaining));
        }
        var windows = JsonFields.Object(billing, "windowLimits");
        foreach (var key in new[] { "fiveHour", "weekly" })
        {
            var window = JsonFields.Object(windows, key);
            if (window.ValueKind != JsonValueKind.Object) continue;
            var capValue = JsonFields.Number(window, "cap");
            var used = JsonFields.Number(window, "used");
            rows.Add(new(SourceUrl(key), "USD-Credits", used, capValue,
                capValue.HasValue && used.HasValue ? Math.Max(0, capValue.Value - used.Value) : null,
                JsonFields.Date(window, "resetAt")));
        }
        var orgLimits = JsonFields.Object(whoami, "orgLimits");
        if (orgLimits.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var window in orgLimits.EnumerateArray())
            {
                if (window.ValueKind != JsonValueKind.Object) continue;
                var capValue = JsonFields.Number(window, "limit");
                var used = JsonFields.Number(window, "spent");
                var model = JsonFields.String(window, "model");
                var scope = JsonFields.String(window, "scope") == "model" ? "organization_model" : "organization";
                // Only claim an organization pool when its identity is actually returned.
                // Anonymous or model-specific limits must not be summed into account balances.
                var pool = orgId is null || (scope == "organization_model" && model is null) ? null
                    : "commandcode:org:" + orgId + (scope == "organization_model" ? ":model:" + model : "");
                rows.Add(new(Api + "whoami#" + scope + "_" + index++, "USD-Credits", used, capValue,
                    capValue.HasValue && used.HasValue ? Math.Max(0, capValue.Value - used.Value) : null,
                    JsonFields.Date(window, "resetAt"), pool, InheritAccountPool: false));
            }
        }
        // Account credits and rolling windows do not reveal the additional model allowance.
        rows.Add(new("https://commandcode.ai/docs/plans/pro#model-monthly-remaining-unknown", "USD"));
        return rows;
    }

    private static string SourceUrl(string window) => Api + "billing/credits#" + window;
}
