using System.Text.Json;
using Lain.Host.Accounts.Managed;

namespace Lain.Host.Quota;

public sealed record KimiBoosterWallet(long BalanceCents, long TotalCents, bool MonthlyChargeLimitEnabled,
    long MonthlyChargeLimitCents, long MonthlyUsedCents, string Currency);
public sealed record KimiManagedUsageSnapshot(QuotaReading[] Readings, string BoosterState, KimiBoosterWallet? Booster);

/// <summary>Local CLI envelope parser only. No CLI owner, bearer handoff or live source is enabled by this parser.</summary>
public static class KimiManagedUsageParser
{
    public const string Source = "kimi-cli:/api/v1/oauth/usage";
    public static KimiManagedUsageSnapshot Parse(JsonElement envelope)
    {
        var code = ManagedJson.Field(envelope, "code");
        if (code.ValueKind != JsonValueKind.Number || !code.TryGetInt32(out var status) || status != 0) throw ManagedJson.Invalid();
        var data = ManagedJson.Field(envelope, "data");
        if (ManagedJson.Text(data, "kind") != "ok") throw new InvalidDataException("Kimi owner did not return usable quota data.");
        var quota = ManagedJson.Field(data, "quota");
        var usages = ManagedJson.Field(quota, "usages");
        if (usages.ValueKind != JsonValueKind.Object) throw ManagedJson.Invalid();
        var readings = new List<QuotaReading>();
        foreach (var (key, label) in new[] { ("limit5h", "5 hours"), ("limit7d", "7 days"), ("monthTotal", "Monthly total"), ("monthCode", "Monthly coding") })
        {
            var entry = ManagedJson.Field(usages, key);
            if (entry.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) continue;
            var ratio = ManagedJson.Number(entry, "usedRatio");
            _ = ManagedJson.Field(entry, "resetAt");
            double? percent = ratio is >= 0 and <= 1 ? ratio * 100 : null;
            readings.Add(new(Source + "#" + key, "percent", percent, percent.HasValue ? 100 : null,
                percent.HasValue ? 100 - percent : null, JsonFields.Date(entry, "resetAt"), WindowLabel: label, UsedPercent: percent));
        }
        var extra = ManagedJson.Field(quota, "extraUsage");
        if (extra.ValueKind == JsonValueKind.Null) return new(readings.ToArray(), "absent", null);
        if (extra.ValueKind == JsonValueKind.Undefined) return new(readings.ToArray(), "unknown", null);
        long Integer(string name)
        {
            var value = ManagedJson.Field(extra, name);
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number)) throw ManagedJson.Invalid();
            return number;
        }
        var enabled = ManagedJson.Field(extra, "monthlyChargeLimitEnabled");
        var currency = ManagedJson.Text(extra, "currency");
        if (enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || string.IsNullOrWhiteSpace(currency) || currency.Length > 16 || currency.Any(char.IsControl))
            throw ManagedJson.Invalid();
        var wallet = new KimiBoosterWallet(Integer("balanceCents"), Integer("totalCents"), enabled.GetBoolean(),
            Integer("monthlyChargeLimitCents"), Integer("monthlyUsedCents"), currency);
        // Preserve provider integer cents independently; total/balance are not a derived quota window.
        return new(readings.ToArray(), "present", wallet);
    }
}
