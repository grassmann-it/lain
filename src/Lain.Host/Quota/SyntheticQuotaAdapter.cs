using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Quota;

// Official schema: https://dev.synthetic.new/docs/synthetic/quotas (2026-10-24).
public sealed class SyntheticQuotaAdapter(HttpClient client) : IQuotaAdapter
{
    public const string Endpoint = "https://api.synthetic.new/v2/quotas";
    public string Source => Endpoint;
    public bool Supports(string provider) => provider.Equals("synthetic", StringComparison.OrdinalIgnoreCase);

    public async Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        if (!Supports(account.Provider) || (credential.CredentialKind ?? account.CredentialKind) != "api-key" || string.IsNullOrWhiteSpace(credential.BearerToken))
            throw new QuotaFetchException("Synthetic quota requires its own API key account.");
        // Custom gateway credentials must never leave their configured origin.
        if (credential.BaseUrl?.TrimEnd('/') is not ("https://api.synthetic.new/openai/v1" or
            "https://api.synthetic.new/anthropic/v1" or "https://api.synthetic.new/v1"))
            throw new QuotaFetchException("Synthetic quota requires an official API base URL.");
        try
        {
            using var doc = await QuotaHttp.GetAsync(client, Endpoint, credential, ct, userAgent: "lain/1.0");
            var rows = Normalize(doc.RootElement);
            return new(rows, rows.Count == 0 ? "No recognized subscription quota; remaining budget unknown." : null);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested) { throw new QuotaFetchException(QuotaFetchException.SafeDiagnostic(ex)); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or ArgumentException or FormatException)
        { throw new QuotaFetchException(QuotaFetchException.SafeDiagnostic(ex)); }
    }

    public static IReadOnlyList<QuotaReading> Normalize(JsonElement root)
    {
        var rows = new List<QuotaReading>();
        var plan = JsonFields.Object(root, "planWeeklyLimit");
        var planPercent = Percent(plan, "percentRemaining");
        if (planPercent.HasValue)
            rows.Add(new(Endpoint + "#plan-weekly", "percent", UsedPercent: 100 - planPercent,
                InheritAccountPool: false, WindowLabel: "Weekly plan credits", WindowId: "plan-weekly",
                MeasurementKind: "subscription", Scope: "account", SourceUrl: Endpoint,
                NextRegenerationAt: JsonFields.Date(plan, "nextRegenAt"), NextRegenerationPercent: Percent(plan, "nextRegenPercent")));
        else
        {
            var weekly = JsonFields.Object(root, "weeklyTokenLimit");
            var maximum = ExactCredits(weekly, "maxCredits");
            var remaining = ExactCredits(weekly, "remainingCredits");
            var weeklyPercent = Percent(weekly, "percentRemaining");
            if (maximum is not null || remaining is not null || weeklyPercent.HasValue)
                rows.Add(new(Endpoint + "#weekly-credits", "credits", UsedPercent: weeklyPercent.HasValue ? 100 - weeklyPercent : null,
                    InheritAccountPool: false, WindowLabel: "Weekly credits", WindowId: "weekly-credits",
                    MeasurementKind: "subscription", Scope: "account", SourceUrl: Endpoint,
                    ExactLimit: maximum, ExactRemaining: remaining, NextRegenerationAt: JsonFields.Date(weekly, "nextRegenAt"),
                    ExactNextRegeneration: ExactCredits(weekly, "nextRegenCredits")));
        }
        var hasWeekly = rows.Count > 0;
        // A separately reported rolling constraint is not added to weekly credits.
        var rolling = JsonFields.Object(root, "rollingFiveHourLimit");
        var maximumRolling = JsonFields.Number(rolling, "max");
        var remainingRolling = JsonFields.Number(rolling, "remaining");
        if (maximumRolling.HasValue || remainingRolling.HasValue)
            rows.Add(new(Endpoint + "#rolling-five-hour", "units", Limit: maximumRolling, Remaining: remainingRolling,
                InheritAccountPool: false, WindowLabel: "Rolling five-hour quota (provider units)", WindowId: "rolling-five-hour",
                MeasurementKind: "subscription", Scope: "account", SourceUrl: Endpoint,
                NextRegenerationAt: JsonFields.Date(rolling, "nextTickAt"), NextRegenerationPercent: Percent(rolling, "tickPercent")));
        if (hasWeekly) return rows;

        // Preserve the prior documented subscription contract as a fallback.
        var subscription = JsonFields.Object(root, "subscription");
        if (subscription.ValueKind != JsonValueKind.Object) return rows;
        var limit = JsonFields.Number(subscription, "limit");
        var used = JsonFields.Number(subscription, "requests");
        double? remainingLegacy = limit.HasValue && used.HasValue && used <= limit ? limit - used : null;
        double? percent = limit is > 0 && remainingLegacy.HasValue ? used / limit * 100 : null;
        if (!limit.HasValue && !used.HasValue) return rows;
        rows.Add(new(Endpoint + "#subscription", "units", used, limit, remainingLegacy,
            JsonFields.Date(subscription, "renewsAt"),
            WindowLabel: "Subscription (provider units)", UsedPercent: percent));
        return rows;
    }

    private static double? Percent(JsonElement root, string name) => JsonFields.Number(root, name) is { } value && value <= 100 ? value : null;

    private static string? ExactCredits(JsonElement root, string name)
    {
        // Bounded decimal text is preserved byte-for-byte, including trailing zeros.
        // No double or decimal conversion can silently round a provider credit value.
        var value = JsonFields.String(root, name);
        return value is { Length: > 0 and <= 512 }
            && System.Text.RegularExpressions.Regex.IsMatch(value, @"^[0-9]+(?:\.[0-9]+)?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            ? value : null;
    }
}
