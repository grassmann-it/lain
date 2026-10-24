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
        if ((credential.CredentialKind ?? account.CredentialKind) != "api-key")
            throw new QuotaFetchException("Command Code quota requires a qualified API key.");
        using var whoami = await QuotaHttp.GetAsync(client, Api + "whoami?limits=1", credential, ct);
        var orgId = JsonFields.String(JsonFields.Object(whoami.RootElement, "org"), "id");
        var query = orgId is null ? "" : "?orgId=" + Uri.EscapeDataString(orgId);
        async Task<(JsonDocument? Document, string? Error)> Optional(string path)
        {
            try { return (await QuotaHttp.GetAsync(client, Api + path + query, credential, ct), null); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is QuotaFetchException or HttpRequestException or JsonException or OperationCanceledException or IOException or ArgumentException or FormatException)
            {
                return (null, path + " unavailable. " + QuotaFetchException.SafeDiagnostic(ex));
            }
        }
        // Both reads use the same identity already established by whoami. Keep
        // result/error ordering deterministic without serializing independent I/O.
        var billingTask = Optional("billing/credits");
        var subscriptionTask = Optional("billing/subscriptions");
        try { await Task.WhenAll(billingTask, subscriptionTask); }
        catch
        {
            // A canceled sibling must not leave a successfully parsed document undisposed.
            if (billingTask.IsCompletedSuccessfully) billingTask.Result.Document?.Dispose();
            if (subscriptionTask.IsCompletedSuccessfully) subscriptionTask.Result.Document?.Dispose();
            throw;
        }
        var billingResult = await billingTask;
        var subscriptionResult = await subscriptionTask;
        using var billing = billingResult.Document;
        using var subscription = subscriptionResult.Document;
        var errors = new[] { billingResult.Error, subscriptionResult.Error }.Where(error => error is not null).ToArray();
        var rows = Normalize(billing?.RootElement ?? default, subscription?.RootElement ?? default, whoami.RootElement, orgId);
        return new(rows, errors.Length > 0 ? string.Join(" ", errors) : rows.Count == 0 ? "No recognized quota fields; remaining budget unknown." : null);
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
            total - monthly, total, monthly, active ? JsonFields.Date(sub, "currentPeriodEnd") : null, WindowLabel: "Monthly credits"));
        foreach (var key in new[] { "purchasedCredits", "freeCredits" })
        {
            var remaining = JsonFields.Number(credits, key);
            if (remaining.HasValue) rows.Add(new(SourceUrl(key), "USD-Credits", Remaining: remaining, WindowLabel: key == "purchasedCredits" ? "Purchased credits" : "Free credits"));
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
                JsonFields.Date(window, "resetAt"), WindowLabel: key == "fiveHour" ? "5 hours" : "7 days"));
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
                    JsonFields.Date(window, "resetAt"), pool, InheritAccountPool: false, WindowLabel: scope == "organization_model" ? $"Organization · {model ?? "model"}" : "Organization budget"));
            }
        }
        // Account credits and rolling windows do not reveal the additional model allowance.
        rows.Add(new("https://commandcode.ai/docs/plans#model-monthly-remaining-unknown", "USD", WindowLabel: "Monthly model limits"));
        return rows;
    }

    private static string SourceUrl(string window) => Api + "billing/credits#" + window;
}
