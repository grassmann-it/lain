using System.Globalization;
using Lain.Core;
using Lain.Client;

namespace Lain.Cli;

public static partial class CliApplication
{
    private sealed record CompactUsageField(string Text, string? Color = null, string Separator = " · ");
    private sealed record CompactUsageRow(string Account, string Window, string Reading, string Used,
        string Reset, string State, string? StateColor);

    private static async Task WriteCompactAccountUsage(TextWriter output, AccountUsageOverview overview,
        HumanOutputContext? context, TimeZoneInfo? timeZone)
    {
        context ??= HumanOutputContext.Detect(output);
        timeZone ??= TimeZoneInfo.Local;
        var now = DateTimeOffset.UtcNow;
        // Start from the terminal default, including when invoked after a coloured shell prompt.
        if (context.Color) await output.WriteAsync("\u001b[0m");
        await WriteCompactUsageFields(output, [new($"Account usage ({overview.Accounts.Length} accounts)", "1"),
            new(Cell(timeZone.Id))], context, "", "  ");
        if (overview.Accounts.Length == 0)
        {
            await WriteCompactUsageFields(output, [new("No accounts configured.")], context, "", "  ");
            return;
        }
        // Filter presentation only. Full/JSON retain every account, snapshot and host ordering.
        var measured = overview.Accounts.Where(item => QuotaPresentation.HasQuotaReading(item.Quotas)).ToArray();
        var rows = new List<CompactUsageRow>();
        foreach (var item in measured)
        {
            var account = item.Account;
            var summary = QuotaPresentation.ForSummary(item.Quotas);
            var additional = summary.Additional.Where(quota => QuotaPresentation.IsAdditionalCredit(quota)
                && !QuotaPresentation.IsLongWindow(quota)).ToArray();
            var deferred = summary.Additional.Except(additional).ToArray();
            var accountStates = new List<string>();
            if (!account.Enabled) accountStates.Add("Disabled");
            if (account.State is not ("ready" or "ok" or "available" or "unverified")) accountStates.Add(ReadableState(account.State));
            if (account.ExpiresAt is { } expiry && expiry <= now && account.State != "expired") accountStates.Add("Expired");
            if (account.SharedWithLegacy || account.BudgetPoolId is not null
                || item.Quotas.Any(quota => quota.SharedWithLegacy || quota.BudgetPoolId is not null)) accountStates.Add("shared");
            if (deferred.Any(quota => quota.State == "error" || !string.IsNullOrWhiteSpace(quota.Error))) accountStates.Add("Quota refresh error");
            if (deferred.Any(quota => QuotaIsLastKnown(quota, now))) accountStates.Add("Last known quota");
            if (deferred.Length > 0) accountStates.Add("more: --full");
            var first = true;
            foreach (var quota in summary.Primary.Concat(additional))
            {
                var states = new List<string>();
                var stale = QuotaIsLastKnown(quota, now);
                var percent = CompactUsagePercentage(quota);
                var remainingKind = quota.MeasurementKind is not ("usage" or "usage-estimate" or "limit" or "rate");
                var exhausted = remainingKind && quota.Remaining is { } left && double.IsFinite(left) && left <= 0;
                if (exhausted) states.Add(stale ? "Last known exhausted" : "Exhausted");
                else if (stale) states.Add("Last known");
                if (quota.State is not ("ok" or "available" or "stale")) states.Add(ReadableState(quota.State));
                if (!string.IsNullOrWhiteSpace(quota.Error)) states.Add("Note: " + CompactUsageNote(quota.Error));
                if (first) states.AddRange(accountStates);
                rows.Add(new(first ? CompactUsageAccountName(account, overview.Accounts) : "",
                    QuotaWindowName(quota), CompactUsageReading(quota, percent),
                    percent is { } used ? QuotaDisplayFacts.Percent(used) + "% used" : "",
                    quota.ResetsAt is { } reset ? CompactReset(reset, now, timeZone)[6..] : "unknown",
                    string.Join(" · ", states), quota.State == "error" || exhausted && !stale ? "31" : stale ? "33" : null));
                first = false;
            }
        }
        if (rows.Count > 0) await WriteCompactUsageTable(output, rows, context);
        else await WriteCompactUsageFields(output, [new("No quota readings available.")], context, "", "  ");
        var hidden = overview.Accounts.Length - measured.Length;
        if (hidden > 0)
            await WriteCompactUsageFields(output, [new($"{hidden} {(hidden == 1 ? "account" : "accounts")} without quota readings hidden; see lain usage --full.")], context, "", "  ");
        else await WriteCompactUsageFields(output, [new("Details: lain usage --full")], context, "", "  ");
    }

    private static double? CompactUsagePercentage(QuotaDto quota) =>
        quota.MeasurementKind == "usage-estimate" ? null : QuotaPercentage(quota);

    private static string CompactUsageReading(QuotaDto quota, double? percent)
    {
        // Usage, rate and configured limits are facts with their own labels, never remaining funds.
        if (quota.MeasurementKind == "limit" && quota.Limit is { } limit && double.IsFinite(limit))
            return "Limit " + QuotaAmount(limit, quota.Unit);
        if (quota.MeasurementKind is "usage" or "usage-estimate" or "rate")
            return (quota.MeasurementKind == "rate" ? "Observed " : quota.MeasurementKind == "usage-estimate" ? "Est. used " : "Used ")
                + (quota.Used is { } used && double.IsFinite(used) ? QuotaAmount(used, quota.Unit) : "unknown");
        if (quota.Remaining is { } remaining && double.IsFinite(remaining)) return QuotaAmount(remaining, quota.Unit) + " left";
        if (percent is { } known && quota.Unit is ("percent" or "%")) return QuotaDisplayFacts.Percent(100 - known) + "% left";
        if (quota.Used is { } amount && double.IsFinite(amount)) return "Used " + QuotaAmount(amount, quota.Unit);
        if (quota.Limit is { } ceiling && double.IsFinite(ceiling)) return "Limit " + QuotaAmount(ceiling, quota.Unit);
        return "Remaining unknown";
    }

    private static string CompactUsageAccountName(AccountDto account, AccountUsageItem[] accounts)
    {
        var provider = account.Provider;
        var display = provider.ToLowerInvariant() switch
        {
            "nvidia" or "nvidia-build" => "NVIDIA",
            "cloudflare" or "cloudflare-workers-ai" => "Cloudflare",
            "google" or "google-gemini" or "gemini" or "gemini-api" => "Gemini",
            "xiaomi-mimo" or "mimo" => "MiMo",
            _ => ProviderDisplayNames.Get(provider)
        };
        display = Cell(display);
        var name = Cell(account.Name);
        var siblings = accounts.Where(item => string.Equals(item.Account.Provider, provider, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (siblings.Length == 1 && System.Net.Mail.MailAddress.TryCreate(name, out var address)
            && string.Equals(address.Address, name, StringComparison.OrdinalIgnoreCase)) return display;
        var known = new[] { provider, display, ProviderDisplayNames.Get(provider),
            "My " + provider + " account", "My " + display + " account", "My " + ProviderDisplayNames.Get(provider) + " account" };
        if (!known.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            var sameName = accounts.Where(item => string.Equals(Cell(item.Account.Name), name, StringComparison.OrdinalIgnoreCase)).ToArray();
            return sameName.Length == 1 ? name : name + " (" + display + " "
                + (Array.FindIndex(sameName, item => item.Account.Id == account.Id) + 1) + ")";
        }
        return siblings.Length == 1 ? display : display + " " + (Array.FindIndex(siblings, item => item.Account.Id == account.Id) + 1);
    }

    private static async Task WriteCompactUsageTable(TextWriter output, List<CompactUsageRow> rows, HumanOutputContext context)
    {
        if (context.Width < 48)
        {
            // A native unit such as USD-Credits needs more room than two tiny cells allow.
            await WriteCompactUsageFields(output, [new("Window / reading", "1")], context, "", "  ");
            await output.WriteLineAsync(new string('-', context.Width));
            foreach (var row in rows)
            {
                if (row.Account.Length > 0)
                    await WriteCompactUsageFields(output, [new(row.Account, "1")], context, "", "  ");
                await WriteCompactUsageFields(output, [new(row.Window)], context);
                await WriteCompactUsageFields(output, [new(row.Reading)], context);
                var details = new List<CompactUsageField>();
                if (row.Used.Length > 0) details.Add(new(row.Used));
                details.Add(new("Reset " + row.Reset));
                if (row.State.Length > 0) details.Add(new(row.State, row.StateColor));
                await WriteCompactUsageFields(output, details, context);
            }
            return;
        }
        // Long names wrap locally instead of forcing the entire table into a field list.
        var showAccount = context.Width >= 110;
        var showUsed = context.Width >= 72;
        var showReset = context.Width >= 72;
        var showState = context.Width >= 140;
        var headings = new List<string>();
        var maxima = new List<int>();
        var minima = new List<int>();
        var getters = new List<Func<CompactUsageRow, string>>();
        void Column(string heading, int minimum, int maximum, Func<CompactUsageRow, string> get)
        { headings.Add(heading); minima.Add(minimum); maxima.Add(maximum); getters.Add(get); }
        if (showAccount) Column("Account", 10, 24, row => row.Account);
        Column("Window", 8, 26, row => row.Window);
        Column("Available / reading", 19, 30, row => row.Reading);
        if (showUsed) Column("Used", 9, 12, row => row.Used);
        if (showReset) Column("Reset", 12, 26, row => row.Reset);
        if (showState) Column("State", 12, 36, row => row.State);
        var widths = getters.Select((get, index) => Math.Clamp(Math.Max(HumanTable.Width(headings[index]),
            rows.Max(row => HumanTable.Width(get(row)))), minima[index], maxima[index])).ToArray();
        var budget = context.Width - (widths.Length - 1) * 2;
        while (widths.Sum() > budget)
        {
            var widest = Enumerable.Range(0, widths.Length).Where(index => widths[index] > 4)
                .OrderByDescending(index => widths[index]).First();
            widths[widest]--;
        }
        async Task Line(string[] cells, string?[] colors)
        {
            var wrapped = cells.Select((cell, index) => WrapCompactUsageText(cell, widths[index]).DefaultIfEmpty("").ToArray()).ToArray();
            for (var line = 0; line < wrapped.Max(parts => parts.Length); line++)
            {
                var parts = wrapped.Select((lines, index) =>
                {
                    var text = line < lines.Length ? lines[line] : "";
                    var pad = new string(' ', widths[index] - HumanTable.Width(text));
                    return HumanTable.Paint(text + pad, colors[index], context);
                });
                await output.WriteLineAsync(string.Join("  ", parts).TrimEnd());
            }
        }
        await Line(headings.ToArray(), headings.Select(_ => (string?)"1").ToArray());
        await output.WriteLineAsync(string.Join("  ", widths.Select(width => new string('-', width))));
        foreach (var row in rows)
        {
            var separateName = !showAccount || HumanTable.Width(row.Account) > widths[0] * 2;
            if (separateName && row.Account.Length > 0)
                await WriteCompactUsageFields(output, [new(row.Account, "1")], context, "", "  ");
            var displayed = showAccount && separateName ? row with { Account = "" } : row;
            var colors = getters.Select((_, index) => headings[index] == "State" ? row.StateColor : null).ToArray();
            await Line(getters.Select(get => get(displayed)).ToArray(), colors);
            var details = new List<CompactUsageField>();
            if (!showUsed && row.Used.Length > 0) details.Add(new(row.Used));
            if (!showReset) details.Add(new("Reset " + row.Reset));
            if (!showState && row.State.Length > 0) details.Add(new(row.State, row.StateColor));
            if (details.Count > 0) await WriteCompactUsageFields(output, details, context);
        }
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

}
