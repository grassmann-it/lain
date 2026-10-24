using System.Globalization;
using Lain.Core;

namespace Lain.Client;

/// <summary>Provider facts shared by CLI and desktop. Renewal is not a quota reset.</summary>
public static class QuotaDisplayFacts
{
    public static string Number(double value, string? exact = null)
    {
        if (exact is not null && decimal.TryParse(exact, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return exact;
        return value.ToString("G15", CultureInfo.InvariantCulture);
    }

    public static IEnumerable<string> Describe(QuotaDto quota)
    {
        if (quota.NextRegenerationAt is { } regeneration)
        {
            var amount = quota.NextRegenerationPercent is { } percent ? Number(percent, quota.ExactNextRegeneration) + "%" : quota.ExactNextRegeneration;
            yield return "Partial regeneration" + (amount is null ? "" : " · " + amount) + " · " + regeneration.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'");
        }
        if (quota.KiloPassObservation is { } passObservation)
            yield return DescribeObservation("Kilo Pass details", passObservation);
        if (quota.KiloPass is { } pass)
        {
            yield return "Kilo Pass · " + string.Join(" · ", new[] { pass.Status, pass.Tier }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (pass.CurrentPeriodBaseCreditsUsd is { } credits) yield return "Pass base credits · " + Number(credits) + " USD";
            if (pass.CurrentPeriodUsageUsd is { } used) yield return "Pass period usage · " + Number(used) + " USD";
            if (pass.CurrentPeriodBonusCreditsUsd is { } bonus) yield return "Pass bonus credits · " + Number(bonus) + " USD";
            if (pass.NextBillingAt is { } billing) yield return "Next billing · " + billing.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'");
            if (pass.NextRenewalAt is { } renewal) yield return "Pass renewal · " + renewal.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'");
            if (pass.IsConsumable is { } consumable) yield return consumable ? "Pass consumable: yes" : "Pass consumable: no";
        }
        if (quota.RequestyKeyObservation is { } keyObservation)
            yield return DescribeObservation("Requesty key details", keyObservation);
    }

    private static string DescribeObservation(string title, QuotaMetadataObservationDto observation)
    {
        var state = observation.State == "ok" && observation.FreshUntil <= DateTimeOffset.UtcNow
            ? "stale" : observation.State;
        var lastSuccess = observation.LastSuccessAt is { } success
            ? " · last success " + success.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'")
            : " · no successful reading";
        return title + " · " + state + lastSuccess +
            (string.IsNullOrWhiteSpace(observation.Error) ? "" : " · " + observation.Error);
    }
}
