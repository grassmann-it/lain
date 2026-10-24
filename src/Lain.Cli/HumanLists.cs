using System.Globalization;
using Lain.Client;
using Lain.Core;

namespace Lain.Cli;

public static partial class CliApplication
{
    private static async Task WriteProviderCatalog(TextWriter output, ProviderCatalogDto catalog)
    {
        await HumanTable.WriteAsync(output, $"Providers ({catalog.Providers.Length})",
            [new("Provider"), new("ID", Secondary: true, Identifier: true), new("Adapter", Status: true),
                new("Live check", Status: true), new("Routes", Numeric: true), new("Free-only", Numeric: true, Secondary: true)],
            catalog.Providers.Select(provider => new HumanRow([provider.Name, provider.Id, ReadableState(provider.AdapterState),
                LiveCheck(provider.LiveVerification), provider.Routes.Length.ToString(CultureInfo.InvariantCulture), provider.Routes.Count(route => route.FreeOnlyEligible).ToString(CultureInfo.InvariantCulture)])));
        await HumanTable.WriteTextAsync(output, "Available / Prepared describe adapter readiness; live account checks are separate.");
        await HumanTable.WriteTextAsync(output, "Free-only counts eligible catalog routes, not verified account access or free usage.");
        await HumanTable.WriteTextAsync(output, "Details: lain providers onboarding <ID>");
        await HumanTable.WriteTextAsync(output, $"Updated {catalog.ObservedAt:yyyy-MM-dd HH:mm} UTC");
    }

    private static async Task WriteAccounts(TextWriter output, AccountDto[] accounts)
    {
        await HumanTable.WriteAsync(output, $"Accounts ({accounts.Length})",
            [new("Account"), new("ID", Secondary: true, Identifier: true), new("Provider"),
                new("Access", Status: true), new("Credential", Status: true)],
            accounts.Select(account => new HumanRow([account.Name, account.Id, account.Provider,
                account.Enabled ? "Enabled" : "Disabled", ReadableState(account.State)])));
        await HumanTable.WriteTextAsync(output, "Credential state describes local presence and expiry, not verified provider access.");
    }

    private static Task WriteModels(TextWriter output, ModelCatalogDto catalog) =>
        HumanTable.WriteAsync(output, $"Models ({catalog.Models.Length})",
            [new("Model", Identifier: true), new("Provider"), new("Account IDs", Secondary: true, Identifier: true)],
            catalog.Models.Select(model => new HumanRow([model.Id, model.Provider, JoinIds(model.AccountIds)])));

    private static Task WriteProfiles(TextWriter output, ProfileDto[] profiles) =>
        HumanTable.WriteAsync(output, $"Profiles ({profiles.Length})",
            [new("Profile"), new("ID", Secondary: true, Identifier: true), new("Routes", Numeric: true),
                new("Default route", Secondary: true, Identifier: true)],
            profiles.Select(profile => new HumanRow([profile.Name, profile.Id, profile.RouteIds.Length.ToString(CultureInfo.InvariantCulture), profile.DefaultRouteId ?? "none"],
                [new("Route IDs", JoinIds(profile.RouteIds), Identifier: true)])));

    private static Task WriteQuotas(TextWriter output, QuotaDto[] quotas) =>
        HumanTable.WriteAsync(output, $"Quota windows ({quotas.Length})",
            [new("Account", Identifier: true), new("Window"), new("State", Status: true), new("Used %", Numeric: true),
                new("Remaining", Numeric: true), new("Reset (UTC)", Secondary: true)],
            quotas.Select(quota =>
            {
                var details = new List<HumanDetail>
                {
                    new("Balance", $"remaining {QuotaNumber(quota.Remaining)} / {QuotaNumber(quota.Limit)} {Cell(quota.Unit)}; used {QuotaNumber(quota.Used)}"),
                    new("Source", quota.Source, Identifier: true),
                    new("Observed", Timestamp(quota.ObservedAt)),
                    new("Last success", Timestamp(quota.LastSuccessAt))
                };
                if (quota.BudgetPoolId is { } pool) details.Add(new("Pool ID", pool, Identifier: true));
                if (quota.SharedWithLegacy) details.Add(new("Budget", "Shared budget with legacy usage; these are the same provider funds."));
                if (!string.IsNullOrWhiteSpace(quota.Error)) details.Add(new("Error", quota.Error));
                var state = (quota.State is "stale" or "error" or "unavailable") && (QuotaPercentage(quota).HasValue || quota.Remaining.HasValue || quota.Used.HasValue)
                    ? "Last known" : ReadableState(quota.State);
                if (quota.ResetsAt is { } reset && reset <= DateTimeOffset.UtcNow) state = state == "Last known" ? "Last known; reset due" : "Reset due";
                var used = QuotaPercentage(quota);
                return new HumanRow([quota.AccountId, quota.WindowLabel ?? "Not reported", state,
                    used is { } percent ? percent.ToString("0.##", CultureInfo.InvariantCulture) + "%" : "unknown",
                    QuotaNumber(quota.Remaining) + " " + Cell(quota.Unit), Timestamp(quota.ResetsAt)], details.ToArray());
            }));

    private static string JoinIds(string[] ids) => ids.Length == 0 ? "none" : string.Join(", ", ids.Select(Cell));
    private static string Timestamp(DateTimeOffset? time) => time?.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "unknown";
    private static string QuotaNumber(double? value) => value is { } number && double.IsFinite(number) && number >= 0
        ? number.ToString("0.##", CultureInfo.InvariantCulture) : "unknown";

    private static double? QuotaPercentage(QuotaDto quota)
    {
        if (quota.State is "unsupported" or "not-supported") return null;
        if (quota.UsedPercent is { } supplied) return double.IsFinite(supplied) && supplied is >= 0 and <= 100 ? supplied : null;
        if (quota.Limit is not { } limit || !double.IsFinite(limit) || limit <= 0) return null;
        if (quota.Used is { } used && double.IsFinite(used) && used >= 0 && used <= limit) return used / limit * 100;
        if (quota.Remaining is { } remaining && double.IsFinite(remaining) && remaining >= 0 && remaining <= limit) return (limit - remaining) / limit * 100;
        return null;
    }

    private static string LiveCheck(string? state) => state switch
    {
        null or "" or "unknown" or "not_tested" or "live_not_tested" or "not_checked" => "Not checked",
        "verified" or "live_verified" => "Verified",
        "failed" => "Failed",
        _ => ReadableState(state)
    };

    private static string ReadableState(string? state)
    {
        var clean = Cell(state).Replace('_', ' ').Replace('-', ' ');
        return clean.Length == 0 ? "Unknown" : char.ToUpperInvariant(clean[0]) + clean[1..];
    }
}
