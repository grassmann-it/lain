using System.Globalization;
using Lain.Core;

namespace Lain.Cli;

public static partial class CliApplication
{
    private static async Task WriteCompactAccountUsage(TextWriter output, AccountUsageOverview overview, HumanOutputContext? context)
    {
        context ??= HumanOutputContext.Detect(output);
        var now = DateTimeOffset.UtcNow;
        await HumanTable.WriteTextAsync(output, $"Account usage ({overview.Accounts.Length} accounts)", context);
        if (overview.Accounts.Length == 0) { await HumanTable.WriteTextAsync(output, "No accounts configured.", context); return; }
        foreach (var item in overview.Accounts)
        {
            var account = item.Account;
            var tags = new List<string> { Cell(account.Name), Cell(account.Provider) };
            if (!account.Enabled) tags.Add("Disabled");
            if (account.State is not ("ready" or "ok" or "available" or "unverified")) tags.Add(ReadableState(account.State));
            if (account.ExpiresAt is { } expiry && expiry <= now && account.State != "expired") tags.Add("Expired");
            if (account.SharedWithLegacy || account.BudgetPoolId is not null || item.Quotas.Any(quota => quota.SharedWithLegacy || quota.BudgetPoolId is not null))
                tags.Add("shared");
            await HumanTable.WriteTextAsync(output, string.Join(" · ", tags), context);
            if (item.Quotas.Length == 0)
                await WriteCompactQuotaFields(output, ["Quota not reported", "Remaining unknown", "Reset unknown"], context);
            foreach (var quota in item.Quotas)
            {
                var percent = QuotaPercentage(quota);
                var unsupported = quota.State is "unsupported" or "not-supported";
                var due = quota.ResetsAt is { } reset && reset <= now;
                var stale = !unsupported && (due || quota.State is "stale" or "error" or "unavailable" or "refreshing" || QuotaCheckIsOld(quota, now));
                var hasNumber = percent.HasValue || quota.Remaining.HasValue || quota.Used.HasValue;
                var label = QuotaWindowName(quota);
                var fields = new List<string> { label };
                if (percent is { } used)
                {
                    const int cells = 10;
                    var consumed = (int)Math.Round(used / 100 * cells, MidpointRounding.AwayFromZero);
                    fields.Add("[" + new string('#', consumed) + new string('-', cells - consumed) + "] "
                        + used.ToString("0.##", CultureInfo.InvariantCulture) + "% used");
                }
                else fields.Add(unsupported ? "Quota lookup not supported" : "Percentage unknown");
                fields.Add(CompactRemaining(quota, percent, unsupported));
                fields.Add(unsupported ? "Reset unavailable" : due ? "Reset due" : quota.ResetsAt is { } next
                    ? "Reset " + next.UtcDateTime.ToString(next.Year == now.Year ? "MM-dd HH:mm'Z'" : "yyyy-MM-dd HH:mm'Z'", CultureInfo.InvariantCulture)
                    : "Reset unknown");
                if (stale && hasNumber) fields.Add("Last known");
                if (!unsupported && quota.State is not ("ok" or "available") && !(quota.State == "stale" && hasNumber))
                    fields.Add(ReadableState(quota.State));
                if (!string.IsNullOrWhiteSpace(quota.Error) && quota.State != "error") fields.Add("Quota warning");
                var color = !percent.HasValue ? null : !account.Enabled || quota.State == "disabled" ? "2"
                    : stale ? "33" : percent >= 95 ? "31" : percent >= 80 ? "33" : "32";
                await WriteCompactQuotaFields(output, fields, context, color);
            }
        }
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

    private static bool QuotaCheckIsOld(QuotaDto quota, DateTimeOffset now) =>
        quota.LastSuccessAt is not { } success || now - success > TimeSpan.FromMinutes(5);

    private static string CompactRemaining(QuotaDto quota, double? percent, bool unsupported)
    {
        if (unsupported) return "Remaining unavailable";
        if (quota.Remaining is { } amount && double.IsFinite(amount) && amount >= 0) return QuotaAmount(amount, quota.Unit) + " left";
        if (percent is { } known && quota.Unit is ("percent" or "%"))
            return (100 - known).ToString("0.##", CultureInfo.InvariantCulture) + "% left";
        return "Remaining unknown";
    }
}
