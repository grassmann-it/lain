using System.Text;
using Lain.Core;

namespace Lain.Cli;

public static partial class CliApplication
{
    internal static async Task WriteQuotas(TextWriter output, QuotaDto[] quotas, HumanOutputContext? context = null)
    {
        context ??= HumanOutputContext.Detect(output);
        await HumanTable.WriteTextAsync(output, $"Quota windows ({quotas.Length})", context);
        if (quotas.Length == 0) { await HumanTable.WriteTextAsync(output, "No quota snapshots available.", context); return; }
        foreach (var account in quotas.GroupBy(quota => quota.AccountId))
        {
            await output.WriteLineAsync();
            // QuotaDto supplies identity only. Do not fetch configuration just to
            // decorate a read-only snapshot or invent an account display name.
            await output.WriteLineAsync("Account ID: " + Cell(account.Key));
            if (account.Any(quota => quota.SharedWithLegacy))
                await HumanTable.WriteTextAsync(output, "Shared budget with legacy usage; these are the same provider funds.", context, "  ");
            else if (account.Any(quota => quota.BudgetPoolId is not null))
                await HumanTable.WriteTextAsync(output, "Provider budget pool · balances may be shared.", context, "  ");
            await WriteQuotaWindows(output, account, context);
        }
        await HumanTable.WriteTextAsync(output, "Full source and pool IDs: lain quotas --json", context, subtle: true);
    }

    private static async Task WriteQuotaWindows(TextWriter output, IEnumerable<QuotaDto> quotas, HumanOutputContext context,
        bool disabled = false, bool checkAge = false)
    {
        foreach (var quota in quotas)
        {
            var percent = QuotaPercentage(quota);
            var unsupported = QuotaHasSupportOnly(quota);
            if (unsupported)
            {
                await HumanTable.WriteTextAsync(output, QuotaSupportMessage(quota), context, "  ");
                if (quota.Support?.ActionUrl is { Length: > 0 } actionUrl) await HumanTable.WriteTextAsync(output, "Next step: " + Cell(actionUrl), context, "  ", subtle: true);
                continue;
            }
            var due = quota.ResetsAt is { } reset && reset <= DateTimeOffset.UtcNow;
            var stale = !unsupported && (due || quota.State is "stale" or "error" or "unavailable" or "refreshing"
                || (checkAge && QuotaCheckIsOld(quota, DateTimeOffset.UtcNow)));
            var state = stale && (percent.HasValue || quota.Remaining.HasValue || quota.Used.HasValue)
                ? "Last known" : quota.State is "ok" or "available" ? "" : ReadableState(quota.State);
            await HumanTable.WriteTextAsync(output, QuotaWindowName(quota) + (state.Length > 0 ? " · " + state : ""), context, "  ");
            if (percent is { } used)
            {
                var metric = Lain.Client.QuotaDisplayFacts.Percent(used) + "% used";
                var cells = Math.Clamp(context.Width - 4 - metric.Length - 3, 8, 44);
                var consumed = (int)Math.Round(used / 100 * cells, MidpointRounding.AwayFromZero);
                var bar = "[" + new string('#', consumed) + new string('-', cells - consumed) + "] " + metric;
                await output.WriteLineAsync("  " + HumanTable.Paint(bar, disabled || quota.State == "disabled" ? "2" : stale ? "33" : used >= 95 ? "31" : used >= 80 ? "33" : "32", context));
            }
            else if (quota.Remaining is null && quota.Used is null && quota.Limit is null) await HumanTable.WriteTextAsync(output, "Quota values not reported", context, "  ");

            var remaining = unsupported ? "Remaining unavailable"
                : quota.Remaining is { } amount && double.IsFinite(amount) ? "Remaining " + QuotaAmount(amount, quota.Unit)
                : percent is { } known ? "Remaining " + Lain.Client.QuotaDisplayFacts.Percent(100 - known)
                    + (quota.Unit is "percent" or "%" ? "%" : "% (amount unknown)")
                : "Remaining unknown" + (quota.Unit is "unknown" or "" ? "" : " " + Cell(quota.Unit));
            var resetLabel = unsupported ? "Reset unavailable" : quota.ResetsAt is { } next
                ? (due ? "Reset due " : "Reset ") + Timestamp(next) + " UTC" : "Reset unknown";
            if (quota.MeasurementKind is not ("usage" or "limit") && quota.ResetsAt is not null) await WriteQuotaPair(output, remaining, resetLabel, context);
            else if (quota.MeasurementKind is not ("usage" or "limit") && (quota.Remaining is not null || percent is not null)) await HumanTable.WriteTextAsync(output, remaining, context, "  ");
            if (due && !unsupported) await HumanTable.WriteTextAsync(output, "Reset due · refresh to check the next window.", context, "  ");
            if (!unsupported && quota.Unit is not ("percent" or "%"))
            {
                if (quota.Used is { } amountUsed && double.IsFinite(amountUsed) && amountUsed >= 0)
                    await HumanTable.WriteTextAsync(output, "Used " + QuotaAmount(amountUsed, quota.Unit)
                        + (quota.Limit is { } suppliedLimit && double.IsFinite(suppliedLimit) && suppliedLimit >= 0 ? " / " + QuotaAmount(suppliedLimit, quota.Unit) : quota.MeasurementKind == "usage" ? "" : " · limit unknown"), context, "  ");
                else if (quota.Limit is { } limit && double.IsFinite(limit) && limit >= 0)
                    await HumanTable.WriteTextAsync(output, "Limit " + QuotaAmount(limit, quota.Unit) + (quota.MeasurementKind == "limit" ? "" : " · usage unknown"), context, "  ");
            }
            foreach (var fact in Lain.Client.QuotaDisplayFacts.Describe(quota)) await HumanTable.WriteTextAsync(output, fact, context, "  ");
            if (quota.ExactUsed is not null || quota.ExactLimit is not null || quota.ExactRemaining is not null)
                await HumanTable.WriteTextAsync(output, $"Exact amounts · used {quota.ExactUsed ?? "unknown"} · limit {quota.ExactLimit ?? "unknown"} · remaining {quota.ExactRemaining ?? "unknown"} {Cell(quota.Unit)}", context, "  ");
            if (!string.IsNullOrWhiteSpace(quota.Error)) await HumanTable.WriteTextAsync(output, "Note: " + Cell(quota.Error), context, "  ");
            if (quota.MeasurementKind is { Length: > 0 } || quota.Scope is { Length: > 0 })
                await HumanTable.WriteTextAsync(output, string.Join(" · ", new[] { quota.MeasurementKind, quota.Scope, quota.ScopeId, quota.Model }.Where(value => !string.IsNullOrWhiteSpace(value)).Select(Cell)), context, "  ", subtle: true);
            if (quota.DataAsOf is { } dataAsOf) await HumanTable.WriteTextAsync(output, "Data as of " + Timestamp(dataAsOf) + " UTC", context, "  ", subtle: true);
            var checkedAt = quota.LastSuccessAt is { } success ? "Checked " + Timestamp(success) + " UTC"
                : quota.ObservedAt is { } observed ? "Observed " + Timestamp(observed) + " UTC · check time unknown" : "Check time unknown";
            await HumanTable.WriteTextAsync(output, checkedAt + " · source " + QuotaSource(quota.Source, Math.Max(12, context.Width - 11)), context, "  ", subtle: true);
            await output.WriteLineAsync();
        }
    }

    private static bool QuotaHasSupportOnly(QuotaDto quota) => quota.State is "unsupported" or "not-supported"
        // A successful monitor response without exported series is guidance, not an update failure.
        // Retained readings (stale) and any real error keep their diagnostic presentation.
        || quota is { State: "unavailable", Error: null, Used: null, Remaining: null, Limit: null, UsedPercent: null,
            Support: { Kind: "unavailable" } }
        || quota.State is not ("error" or "unavailable" or "stale") && quota.Used is null && quota.Remaining is null && quota.Limit is null && quota.UsedPercent is null
        && quota.Support?.Kind is "console" or "unsupported" or "unimplemented" or "setup-required" or "awaiting-observation" or "passive";

    private static string QuotaSupportMessage(QuotaDto quota) => string.IsNullOrWhiteSpace(quota.Support?.Message)
        ? "No separate quota lookup is configured. See provider setup for available access." : Cell(quota.Support.Message);

    private static async Task WriteQuotaPair(TextWriter output, string left, string right, HumanOutputContext context)
    {
        var free = context.Width - 4 - HumanTable.Width(left) - HumanTable.Width(right);
        if (free >= 2) await output.WriteLineAsync("  " + left + new string(' ', free) + right);
        else
        {
            await HumanTable.WriteTextAsync(output, left, context, "  ");
            await HumanTable.WriteTextAsync(output, right, context, "  ");
        }
    }

    private static string QuotaAmount(double amount, string unit) => (unit is "%" or "percent" ? Lain.Client.QuotaDisplayFacts.Percent(amount) : Lain.Client.QuotaDisplayFacts.Amount(amount))
        + (unit is "%" or "percent" ? "%" : unit is "" or "unknown" ? "" : " " + Cell(unit));

    private static string QuotaWindowName(QuotaDto quota)
    {
        if (!string.IsNullOrWhiteSpace(quota.WindowLabel)) return Cell(quota.WindowLabel);
        var fragment = quota.Source.Split('#').Last();
        return fragment switch
        {
            "fiveHour" or "limit_5h" => "5 hours",
            "weekly" or "limit_7d" => "7 days",
            "primary_window" => "Primary window",
            "secondary_window" => "Secondary window",
            "monthlyCredits" => "Monthly credits",
            "purchasedCredits" => "Purchased credits",
            "freeCredits" => "Free credits",
            _ => "Budget window"
        };
    }

    private static string QuotaSource(string source, int maxWidth)
    {
        var value = Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Host.Length > 0 ? uri.Host + uri.Fragment : Cell(source);
        value = Cell(value);
        if (HumanTable.Width(value) <= maxWidth) return value;
        var text = "";
        foreach (var rune in value.EnumerateRunes())
        {
            if (HumanTable.Width(text + rune) > maxWidth - 3) break;
            text += rune;
        }
        return text + "...";
    }
}
