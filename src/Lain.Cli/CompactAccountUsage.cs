using System.Globalization;
using Lain.Core;
using Lain.Client;

namespace Lain.Cli;

public static partial class CliApplication
{
    private static async Task WriteCompactAccountUsage(TextWriter output, AccountUsageOverview overview, HumanOutputContext? context)
    {
        context ??= HumanOutputContext.Detect(output);
        var now = DateTimeOffset.UtcNow;
        await HumanTable.WriteTextAsync(output, $"Account usage ({overview.Accounts.Length} accounts)", context);
        if (overview.Accounts.Length == 0) { await HumanTable.WriteTextAsync(output, "No accounts configured.", context); return; }
        // Presentation only: the overview and its JSON retain the host's account/window order.
        foreach (var item in overview.Accounts.OrderByDescending(item => QuotaPresentation.HasQuotaReading(item.Quotas)))
        {
            var account = item.Account;
            var summary = QuotaPresentation.ForSummary(item.Quotas);
            var deferred = summary.Additional.Where(quota => !QuotaPresentation.IsAdditionalCredit(quota) || QuotaPresentation.IsLongWindow(quota)).ToArray();
            var tags = new List<string> { Cell(account.Name), Cell(account.Provider) };
            if (!account.Enabled) tags.Add("Disabled");
            if (item.Quotas.Any(quota => quota.State == "error" || !string.IsNullOrWhiteSpace(quota.Error)))
                tags.Add("Quota refresh error");
            if (deferred.Any(quota => QuotaIsLastKnown(quota, now))) tags.Add("Last known quota");
            if (account.State is not ("ready" or "ok" or "available" or "unverified")) tags.Add(ReadableState(account.State));
            if (account.ExpiresAt is { } expiry && expiry <= now && account.State != "expired") tags.Add("Expired");
            if (account.SharedWithLegacy || account.BudgetPoolId is not null || item.Quotas.Any(quota => quota.SharedWithLegacy || quota.BudgetPoolId is not null))
                tags.Add("shared");
            await HumanTable.WriteTextAsync(output, string.Join(" · ", tags), context);
            if (item.Quotas.Length == 0)
                await WriteCompactQuotaFields(output, ["Quota not reported. See provider setup for available access."], context);
            else if (summary.Primary.Count == 0 && summary.Missing.Count == 0)
                await WriteCompactQuotaFields(output, ["No primary quota reported."], context);
            foreach (var quota in summary.Primary)
                await WriteCompactQuotaWindow(output, quota, account.Enabled, context, now);
            // Monthly allowances stay in --full when short windows exist. Extra wallets
            // get only their own compact amount; shared balances are never added together.
            foreach (var quota in summary.Additional.Where(quota => QuotaPresentation.IsAdditionalCredit(quota) && !QuotaPresentation.IsLongWindow(quota)))
                await WriteCompactQuotaWindow(output, quota, account.Enabled, context, now, additional: true);
            // An account without a reading needs a concrete diagnostic, never a fabricated zero.
            // Unknown secondary windows remain in --full when a main reading is available.
            if (summary.Primary.Count == 0 && summary.Missing.FirstOrDefault() is { } missing)
                await WriteCompactQuotaWindow(output, missing, account.Enabled, context, now);
            if (deferred.Length > 0)
                await WriteCompactQuotaFields(output, ["More windows and details: --full"], context);
        }
        await HumanTable.WriteTextAsync(output, "All windows, sources and check times: lain usage --full", context, subtle: true);
    }

    private static async Task WriteCompactQuotaWindow(TextWriter output, QuotaDto quota, bool enabled,
        HumanOutputContext context, DateTimeOffset now, bool additional = false)
    {
        if (QuotaHasSupportOnly(quota))
        {
            await WriteCompactQuotaFields(output, [QuotaSupportMessage(quota)], context);
            return;
        }
        var percent = QuotaPercentage(quota);
        var due = quota.ResetsAt is { } reset && reset <= now;
        var stale = QuotaIsLastKnown(quota, now);
        var hasNumber = QuotaPresentation.HasReading(quota);
        var fields = new List<string> { (additional ? "Additional: " : "") + QuotaWindowName(quota) };
        if (!additional && percent is { } used)
        {
            const int cells = 10;
            var consumed = (int)Math.Round(used / 100 * cells, MidpointRounding.AwayFromZero);
            fields.Add("[" + new string('#', consumed) + new string('-', cells - consumed) + "] "
                + QuotaDisplayFacts.Percent(used) + "% used");
        }
        else if (quota.Remaining is null && quota.Used is null && quota.Limit is null) fields.Add("Quota values not reported");
        if (quota.MeasurementKind is not ("usage" or "usage-estimate" or "limit" or "rate"))
            fields.Add(CompactRemaining(quota, percent, false));
        if (quota.MeasurementKind == "limit" && quota.Limit is { } reportedLimit && double.IsFinite(reportedLimit)) fields.Add("Limit " + QuotaAmount(reportedLimit, quota.Unit));
        if ((percent is null || additional && quota.Remaining is null) && quota.Used is { } amountUsed && double.IsFinite(amountUsed) && amountUsed >= 0)
            fields.Add((quota.MeasurementKind == "rate" ? "Observed " : "Used ") + QuotaAmount(amountUsed, quota.Unit));
        if (quota.ResetsAt is { } next)
            fields.Add(due ? "Reset due" : "Reset " + next.UtcDateTime.ToString(next.Year == now.Year ? "MM-dd HH:mm'Z'" : "yyyy-MM-dd HH:mm'Z'", CultureInfo.InvariantCulture));
        if (quota.MeasurementKind is not ("usage" or "usage-estimate" or "limit" or "rate") && quota.Remaining is { } left && double.IsFinite(left) && left <= 0)
            fields.Add(stale ? "Last known exhausted" : "Exhausted");
        else if (stale && hasNumber) fields.Add("Last known");
        if (quota.State is not ("ok" or "available") && !(quota.State == "stale" && hasNumber))
            fields.Add(ReadableState(quota.State));
        if (!string.IsNullOrWhiteSpace(quota.Error)) fields.Add("Note: " + Cell(quota.Error));
        var color = additional || !percent.HasValue ? null : !enabled || quota.State == "disabled" ? "2"
            : stale ? "33" : percent >= 95 ? "31" : percent >= 80 ? "33" : "32";
        await WriteCompactQuotaFields(output, fields, context, color);
    }

    private static async Task WriteCompactQuotaFields(TextWriter output, IEnumerable<string> fields, HumanOutputContext context, string? color = null)
    {
        var line = "";
        var indent = "  ";
        foreach (var field in fields)
        {
            var candidate = line.Length == 0 ? field : line + " · " + field;
            if (line.Length > 0 && HumanTable.Width(indent + candidate) > context.Width)
            {
                await HumanTable.WriteTextAsync(output, line, context, indent, color: color);
                indent = "    ";
                line = field;
            }
            else line = candidate;
        }
        if (line.Length > 0) await HumanTable.WriteTextAsync(output, line, context, indent, color: color);
    }

    private static bool QuotaIsLastKnown(QuotaDto quota, DateTimeOffset now) =>
        quota.ResetsAt is { } reset && reset <= now || quota.State is "stale" or "error" or "unavailable" or "refreshing" || QuotaCheckIsOld(quota, now);

    private static bool QuotaCheckIsOld(QuotaDto quota, DateTimeOffset now) =>
        quota.FreshUntil is { } freshUntil ? freshUntil <= now : quota.LastSuccessAt is not { } success || now - success > TimeSpan.FromMinutes(5);

    private static string CompactRemaining(QuotaDto quota, double? percent, bool unsupported)
    {
        if (unsupported) return "Remaining unavailable";
        if (quota.Remaining is { } amount && double.IsFinite(amount)) return QuotaAmount(amount, quota.Unit) + " left";
        if (percent is { } known && quota.Unit is ("percent" or "%"))
            return Lain.Client.QuotaDisplayFacts.Percent(100 - known) + "% left";
        return "Remaining unknown";
    }
}
