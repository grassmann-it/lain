using System.Globalization;
using System.Text.RegularExpressions;
using Lain.Core;

namespace Lain.Client;

public sealed record QuotaSummaryPresentation(
    IReadOnlyList<QuotaDto> Primary,
    IReadOnlyList<QuotaDto> Additional,
    IReadOnlyList<QuotaDto> Missing,
    int HiddenCount)
{
    public bool HasMore => Additional.Count > 0 || Missing.Count > 0 || HiddenCount > 0;
}

/// <summary>Shared display hierarchy. Does not change, combine or manufacture provider readings.</summary>
public static class QuotaPresentation
{
    private static readonly Regex DurationPattern = new(
        @"(?<![a-z0-9])(?<number>\d+(?:\.\d+)?)\s*(?<unit>hours?|h|days?|d|weeks?|w|minutes?|mins?|m|seconds?|secs?|s)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool HasReading(QuotaDto quota)
    {
        if (quota.State is "unsupported" or "not-supported") return false;
        return quota.MeasurementKind switch
        {
            "balance" => Finite(quota.Remaining),
            "usage" or "usage-estimate" or "rate" => Amount(quota.Used),
            "limit" => Amount(quota.Limit),
            _ => Amount(quota.Used) || Amount(quota.Limit) || Amount(quota.Remaining)
                || quota.UsedPercent is { } percent && double.IsFinite(percent) && percent is >= 0 and <= 100
        };
    }

    public static bool HasQuotaReading(IEnumerable<QuotaDto> quotas) => quotas
        .Any(quota => HasReading(quota) && !IsZeroOnlyAdditionalCredit(quota));

    public static bool IsLongWindow(QuotaDto quota) => MonthlyOrLonger(quota)
        || DurationSeconds(quota) is { } seconds && seconds > TimeSpan.FromDays(7).TotalSeconds;

    public static bool IsAdditionalCredit(QuotaDto quota)
    {
        // A rolling credit allowance is still a short quota window, not an extra wallet.
        if (ShortWindow(quota)) return false;
        var label = quota.WindowLabel ?? "";
        if (label.Contains("not credit balance", StringComparison.OrdinalIgnoreCase)) return false;
        return Fragment(quota) is "monthlycredits" or "purchasedcredits" or "freecredits"
            || label.Contains("credits", StringComparison.OrdinalIgnoreCase)
            || label.Contains("credit balance", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSecondary(QuotaDto quota) => !HasReading(quota) || IsLongWindow(quota)
        || quota.MeasurementKind == "balance" || IsAdditionalCredit(quota);

    public static bool IsZeroOnlyAdditionalCredit(QuotaDto quota)
    {
        var label = (quota.WindowLabel ?? "").ToLowerInvariant();
        var explicitlyAdditional = Fragment(quota) is "purchasedcredits" or "freecredits"
            || label.Contains("purchased credits", StringComparison.Ordinal)
            || label.Contains("free credits", StringComparison.Ordinal)
            || label.Contains("add-on credits", StringComparison.Ordinal)
            || label.Contains("bonus credits", StringComparison.Ordinal);
        // Never hide a main balance, exhausted rolling allowance, or monthly budget.
        if (!explicitlyAdditional || !IsAdditionalCredit(quota) || !HasReading(quota)) return false;
        var supplied = new[] { quota.Used, quota.Limit, quota.Remaining, quota.UsedPercent }.Where(value => value.HasValue).ToArray();
        return supplied.Length > 0 && supplied.All(value => value == 0);
    }

    public static IReadOnlyList<QuotaDto> Order(IEnumerable<QuotaDto> quotas) => quotas
        .Select((quota, index) => new { Quota = quota, Index = index })
        .OrderBy(row => Rank(row.Quota))
        .ThenBy(row => ShortWindow(row.Quota) ? DurationSeconds(row.Quota) ?? double.MaxValue : double.MaxValue)
        .ThenBy(row => row.Index)
        .Select(row => row.Quota).ToArray();

    public static QuotaSummaryPresentation ForSummary(IEnumerable<QuotaDto> quotas)
    {
        var ordered = Order(quotas);
        var measured = ordered.Where(HasReading).ToArray();
        var primary = measured.Where(quota => !IsSecondary(quota)).Take(2).ToArray();
        // Accounts with only a monthly quota or wallet still show their actual main reading, including zero.
        if (primary.Length == 0 && measured.FirstOrDefault(quota => !IsZeroOnlyAdditionalCredit(quota)) is { } fallback)
            primary = [fallback];
        var remaining = measured.ToList();
        foreach (var quota in primary) remaining.Remove(quota);
        return new(primary,
            remaining.Where(quota => !IsZeroOnlyAdditionalCredit(quota)).ToArray(),
            ordered.Where(quota => !HasReading(quota)).ToArray(),
            remaining.Count(IsZeroOnlyAdditionalCredit));
    }

    private static int Rank(QuotaDto quota) => !HasReading(quota) ? 4
        : ShortWindow(quota) ? 0 : !IsSecondary(quota) ? 1 : IsLongWindow(quota) ? 2 : 3;

    private static bool Finite(double? value) => value is { } number && double.IsFinite(number);
    private static bool Amount(double? value) => value is { } number && double.IsFinite(number) && number >= 0;
    private static string Fragment(QuotaDto quota) => quota.Source[(quota.Source.LastIndexOf('#') + 1)..].ToLowerInvariant();

    private static bool ShortWindow(QuotaDto quota) => quota.MeasurementKind != "balance" && !MonthlyOrLonger(quota)
        && DurationSeconds(quota) is { } seconds && seconds <= TimeSpan.FromDays(7).TotalSeconds;

    private static bool MonthlyOrLonger(QuotaDto quota)
    {
        var label = (quota.WindowLabel ?? "").ToLowerInvariant();
        var fragment = Fragment(quota);
        return Regex.IsMatch(label, @"\b(monthly|months?|annual|annually|yearly|years?)\b", RegexOptions.CultureInvariant)
            || fragment is "monthlycredits" or "monthly" or "annual" or "yearly"
            || fragment == "model-monthly-remaining-unknown";
    }

    private static double? DurationSeconds(QuotaDto quota)
    {
        var label = (quota.WindowLabel ?? "").ToLowerInvariant();
        var match = DurationPattern.Match(label);
        if (!match.Success) match = DurationPattern.Match(Fragment(quota).Replace('_', ' ').Replace('-', ' '));
        if (match.Success && double.TryParse(match.Groups["number"].Value, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var number) && number > 0)
        {
            var unit = match.Groups["unit"].Value.ToLowerInvariant();
            var multiplier = unit[0] switch { 'w' => 604800, 'd' => 86400, 'h' => 3600, 'm' => 60, _ => 1 };
            var seconds = number * multiplier;
            if (double.IsFinite(seconds)) return seconds;
        }
        if (label.Contains("five-hour", StringComparison.Ordinal) || label.Contains("five hour", StringComparison.Ordinal)) return 18000;
        if (Regex.IsMatch(label, @"\b(weekly|week)\b", RegexOptions.CultureInvariant)) return 604800;
        if (Regex.IsMatch(label, @"\b(daily|day)\b", RegexOptions.CultureInvariant)) return 86400;
        if (label.Contains("per minute", StringComparison.Ordinal)) return 60;
        var fragment = Fragment(quota);
        if (fragment is "fivehour" or "limit_5h") return 18000;
        if (fragment is "weekly" or "seven_day") return 604800;
        if (fragment is "daily" or "utc-day") return 86400;
        // A reset instant describes when, never how long. Only an explicit period supplies a duration.
        if (quota.WindowStart is { } start && quota.WindowEnd is { } end && end > start)
            return (end - start).TotalSeconds;
        return null;
    }
}
