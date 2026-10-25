using System.Globalization;
using Lain.Core;
using Lain.Client;

namespace Lain.Cli;

public static partial class CliApplication
{
    private sealed record CompactUsageField(string Text, string? Color = null, string Separator = " · ");

    private static async Task WriteCompactAccountUsage(TextWriter output, AccountUsageOverview overview,
        HumanOutputContext? context, TimeZoneInfo? timeZone)
    {
        context ??= HumanOutputContext.Detect(output);
        timeZone ??= TimeZoneInfo.Local;
        var now = DateTimeOffset.UtcNow;
        await WriteCompactUsageFields(output, [new($"Account usage ({overview.Accounts.Length} accounts)", "1"),
            new(Cell(timeZone.Id), "2")], context, "", "  ");
        if (overview.Accounts.Length == 0)
        {
            await WriteCompactUsageFields(output, [new("No accounts configured.")], context, "", "  ");
            return;
        }
        // Presentation only: the overview and its JSON retain the host's account/window order.
        var ordered = overview.Accounts.OrderByDescending(item => QuotaPresentation.HasQuotaReading(item.Quotas)).ToArray();
        var readingCount = ordered.Count(item => QuotaPresentation.HasQuotaReading(item.Quotas));
        for (var index = 0; index < ordered.Length; index++)
        {
            var item = ordered[index];
            if (index == readingCount && readingCount > 0)
                await WriteCompactUsageFields(output, [new($"No quota readings ({ordered.Length - readingCount})", "2")], context, "", "  ");
            var account = item.Account;
            var summary = QuotaPresentation.ForSummary(item.Quotas);
            var deferred = summary.Additional.Where(quota => !QuotaPresentation.IsAdditionalCredit(quota) || QuotaPresentation.IsLongWindow(quota)).ToArray();
            var tags = new List<CompactUsageField> { new(Cell(account.Name), account.Enabled ? "1;36" : "1;2") };
            if (!string.Equals(Cell(account.Name), Cell(account.Provider), StringComparison.OrdinalIgnoreCase))
                tags.Add(new(Cell(account.Provider), "2"));
            if (!account.Enabled) tags.Add(new("Disabled", "2"));
            if (item.Quotas.Any(quota => !QuotaHasSupportOnly(quota) && (quota.State == "error" || !string.IsNullOrWhiteSpace(quota.Error))))
                tags.Add(new("Quota refresh error", "31"));
            if (deferred.Any(quota => QuotaIsLastKnown(quota, now))) tags.Add(new("Last known quota", "33"));
            if (account.State is not ("ready" or "ok" or "available" or "unverified")) tags.Add(new(ReadableState(account.State), "33"));
            if (account.ExpiresAt is { } expiry && expiry <= now && account.State != "expired") tags.Add(new("Expired", "31"));
            if (account.SharedWithLegacy || account.BudgetPoolId is not null || item.Quotas.Any(quota => quota.SharedWithLegacy || quota.BudgetPoolId is not null))
                tags.Add(new("shared", "2"));
            if (deferred.Length > 0) tags.Add(new("more: --full", "2"));
            await WriteCompactUsageFields(output, tags, context, "", "  ");

            var additional = summary.Additional.Where(quota => QuotaPresentation.IsAdditionalCredit(quota) && !QuotaPresentation.IsLongWindow(quota)).ToArray();
            var labelWidth = context.Width >= 60
                ? Math.Min(24, summary.Primary.Select(quota => HumanTable.Width(QuotaWindowName(quota))).DefaultIfEmpty(0).Max()) : 0;
            if (item.Quotas.Length == 0)
                await WriteCompactUsageFields(output, [new("Quota not reported", "2")], context);
            else if (summary.Primary.Count == 0 && summary.Missing.Count == 0)
                await WriteCompactUsageFields(output, [new("No primary quota reported", "2")], context);
            foreach (var quota in summary.Primary)
                await WriteCompactQuotaWindow(output, quota, account.Enabled, context, now, timeZone, labelWidth);
            // Extra wallets keep their native amounts; shared balances are never added together.
            foreach (var quota in additional)
                await WriteCompactQuotaWindow(output, quota, account.Enabled, context, now, timeZone, additional: true);
            // Unknown secondary windows stay in --full when a main reading is available.
            if (summary.Primary.Count == 0 && summary.Missing.FirstOrDefault() is { } missing)
                await WriteCompactQuotaWindow(output, missing, account.Enabled, context, now, timeZone);
        }
        await WriteCompactUsageFields(output, [new("Details: lain usage --full", "2")], context, "", "  ");
    }

    private static async Task WriteCompactQuotaWindow(TextWriter output, QuotaDto quota, bool enabled,
        HumanOutputContext context, DateTimeOffset now, TimeZoneInfo timeZone, int labelWidth = 0, bool additional = false)
    {
        if (QuotaHasSupportOnly(quota))
        {
            var message = quota.Support?.Kind switch
            {
                "setup-required" => "Quota setup required",
                "console" => "Quota: check provider console",
                "unimplemented" => "Quota integration not available",
                "awaiting-observation" or "passive" => "Waiting for quota observation",
                "unavailable" => "No quota series reported",
                _ => "No separate quota lookup configured"
            };
            await WriteCompactUsageFields(output, [new(message, "2")], context);
            return;
        }
        var percent = QuotaPercentage(quota);
        var stale = QuotaIsLastKnown(quota, now);
        var hasNumber = QuotaPresentation.HasReading(quota);
        var color = !enabled || quota.State == "disabled" ? "2"
            : stale ? "33" : percent >= 95 || quota.Remaining <= 0 ? "31" : percent >= 80 ? "33" : "32";
        var label = (additional ? "Additional: " : "") + QuotaWindowName(quota);
        var fields = new List<CompactUsageField>();
        // A synthetic fallback label says nothing useful when no values were reported.
        if (hasNumber || quota.WindowLabel is not null) fields.Add(new(label + new string(' ', Math.Max(0, labelWidth - HumanTable.Width(label)))));
        if (!additional && percent is { } used)
        {
            var metric = QuotaDisplayFacts.Percent(used) + "% used";
            // Very narrow terminals keep the actual number together instead of splitting a bar.
            if (context.Width >= 48)
            {
                const int cells = 10;
                var consumed = (int)Math.Round(used / 100 * cells, MidpointRounding.AwayFromZero);
                metric = "[" + new string('━', consumed) + new string('·', cells - consumed) + "] " + metric;
            }
            fields.Add(new(metric, color, "  "));
        }
        else if (!hasNumber) fields.Add(new("Quota values not reported", "2"));
        if (quota.MeasurementKind is not ("usage" or "usage-estimate" or "limit" or "rate") && hasNumber)
            fields.Add(new(CompactRemaining(quota, percent), "1"));
        if (quota.MeasurementKind == "limit" && quota.Limit is { } reportedLimit && double.IsFinite(reportedLimit))
            fields.Add(new("Limit " + QuotaAmount(reportedLimit, quota.Unit)));
        if ((percent is null || additional && quota.Remaining is null) && quota.Used is { } amountUsed && double.IsFinite(amountUsed) && amountUsed >= 0)
            fields.Add(new((quota.MeasurementKind == "rate" ? "Observed " : "Used ") + QuotaAmount(amountUsed, quota.Unit)));
        if (quota.ResetsAt is { } next) fields.Add(new(CompactReset(next, now, timeZone), "2"));
        if (quota.MeasurementKind is not ("usage" or "usage-estimate" or "limit" or "rate") && quota.Remaining is { } left && double.IsFinite(left) && left <= 0)
            fields.Add(new(stale ? "Last known exhausted" : "Exhausted", color));
        else if (stale && hasNumber) fields.Add(new("Last known", "33"));
        if (quota.State is not ("ok" or "available") && !(quota.State == "stale" && hasNumber))
            fields.Add(new(ReadableState(quota.State), quota.State == "error" ? "31" : "33"));
        if (!string.IsNullOrWhiteSpace(quota.Error)) fields.Add(new("Note: " + CompactUsageNote(quota.Error), "33"));
        await WriteCompactUsageFields(output, fields, context);
    }

    private static string CompactReset(DateTimeOffset reset, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        if (reset <= now) return "Reset due";
        var local = TimeZoneInfo.ConvertTime(reset, timeZone);
        var today = TimeZoneInfo.ConvertTime(now, timeZone).Date;
        var day = local.Date == today ? "today" : local.Date == today.AddDays(1) ? "tomorrow"
            : local.ToString(local.Year == today.Year ? "MM-dd" : "yyyy-MM-dd", CultureInfo.InvariantCulture);
        // The offset identifies both occurrences of a repeated hour at a DST boundary.
        return "Reset " + day + " " + local.ToString("HH:mm zzz", CultureInfo.InvariantCulture);
    }

    private static string CompactUsageNote(string note)
    {
        var clean = Cell(note);
        if (HumanTable.Width(clean) <= 88) return clean;
        var shortened = "";
        foreach (var rune in clean.EnumerateRunes())
        {
            if (HumanTable.Width(shortened + rune) > 87) break;
            shortened += rune;
        }
        return shortened.TrimEnd() + "…";
    }

    // Apply renderer-owned ANSI only after measuring cleaned text. Wrapping is local
    // to this overview; generic tables keep their copyable, unbroken identifier rules.
    private static async Task WriteCompactUsageFields(TextWriter output, IEnumerable<CompactUsageField> fields,
        HumanOutputContext context, string indent = "  ", string continuation = "    ")
    {
        var parts = new List<CompactUsageField>();
        var lineWidth = HumanTable.Width(indent);
        async Task Flush()
        {
            if (parts.Count == 0) return;
            await output.WriteAsync(indent);
            for (var index = 0; index < parts.Count; index++)
            {
                if (index > 0) await output.WriteAsync(parts[index].Separator);
                var text = index == parts.Count - 1 ? parts[index].Text.TrimEnd() : parts[index].Text;
                await output.WriteAsync(HumanTable.Paint(text, parts[index].Color, context));
            }
            await output.WriteLineAsync();
            parts.Clear();
            indent = continuation;
            lineWidth = HumanTable.Width(indent);
        }
        foreach (var field in fields)
        {
            var needed = HumanTable.Width(field.Text) + (parts.Count == 0 ? 0 : HumanTable.Width(field.Separator));
            if (parts.Count > 0 && lineWidth + needed > context.Width) await Flush();
            var available = Math.Max(1, context.Width - HumanTable.Width(indent));
            if (HumanTable.Width(field.Text) > available)
            {
                foreach (var chunk in WrapCompactUsageText(field.Text, Math.Max(1, context.Width - Math.Max(HumanTable.Width(indent), HumanTable.Width(continuation)))))
                {
                    parts.Add(field with { Text = chunk });
                    await Flush();
                }
            }
            else
            {
                lineWidth += HumanTable.Width(field.Text) + (parts.Count == 0 ? 0 : HumanTable.Width(field.Separator));
                parts.Add(field);
            }
        }
        await Flush();
    }

    private static IEnumerable<string> WrapCompactUsageText(string text, int width)
    {
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && HumanTable.Width(line) + 1 + HumanTable.Width(word) > width)
            {
                yield return line;
                line = "";
            }
            if (line.Length > 0) line += " ";
            foreach (var rune in word.EnumerateRunes())
            {
                if (HumanTable.Width(line + rune) > width)
                {
                    yield return line;
                    line = "";
                }
                line += rune;
            }
        }
        if (line.Length > 0) yield return line;
    }

    private static bool QuotaIsLastKnown(QuotaDto quota, DateTimeOffset now) =>
        quota.ResetsAt is { } reset && reset <= now || quota.State is "stale" or "error" or "unavailable" or "refreshing" || QuotaCheckIsOld(quota, now);

    private static bool QuotaCheckIsOld(QuotaDto quota, DateTimeOffset now) =>
        quota.FreshUntil is { } freshUntil ? freshUntil <= now : quota.LastSuccessAt is not { } success || now - success > TimeSpan.FromMinutes(5);

    private static string CompactRemaining(QuotaDto quota, double? percent)
    {
        if (quota.Remaining is { } amount && double.IsFinite(amount)) return QuotaAmount(amount, quota.Unit) + " left";
        if (percent is { } known && quota.Unit is ("percent" or "%"))
            return QuotaDisplayFacts.Percent(100 - known) + "% left";
        return "Remaining unknown";
    }
}
