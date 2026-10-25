using System.Text.RegularExpressions;
using Lain.Cli;
using Lain.Core;

internal static class UsageStyleChecks
{
    internal static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        var now = DateTimeOffset.UtcNow;
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var basis = CliPresentationFixtures.Quotas[0] with { AccountId = "style", SharedWithLegacy = false,
            LastSuccessAt = now, FreshUntil = now.AddMinutes(5), ResetsAt = null };
        var account = new AccountDto("style", "Style account", "fixture", true, "api-key", "ready");
        async Task<string> Render(AccountUsageOverview overview, int width = 120, bool color = false, bool full = false)
        {
            var writer = new StringWriter();
            await CliApplication.WriteAccountUsage(writer, overview, new(width, color), full: full, timeZone: zone);
            return writer.ToString();
        }
        AccountUsageOverview Overview(params QuotaDto[] quotas) => new(1, "account-usage", [new(account, quotas)]);
        string Plain(string text) => Regex.Replace(text, @"\x1b\[[0-9;]*m", "");
        string Words(string text) => Regex.Replace(Plain(text), @"\s+", " ").Trim();
        var longName = string.Concat(Enumerable.Repeat("研究😀", 18));
        var hostile = "\u001b[31mVisible\u001b[0m\u001b]8;;https://hostile.invalid\aLink\u001b]8;;\a\r\n\t\u202e";
        var longOverview = new AccountUsageOverview(1, "account-usage", [new(account with { Name = longName + hostile },
            [basis with { WindowLabel = string.Concat(Enumerable.Repeat("非常に長い枠", 10)) },
             basis with { WindowLabel = "7 days", Used = 100, Remaining = 0, UsedPercent = 100 }])]);
        foreach (var width in new[] { 20, 40, 60, 80, 120 })
        {
            var text = await Render(longOverview, width);
            var compactCharacters = Regex.Replace(text, @"\s+", "");
            Check(text.Split('\n').All(line => HumanTable.Width(line.TrimEnd('\r')) <= width) &&
                !text.Contains('\u001b') && !text.Contains('\t') && !text.Contains('\u202e') && !text.Contains("hostile.invalid") &&
                compactCharacters.Contains(longName + "VisibleLink") && Words(text).Contains("Exhausted") &&
                text.Contains('━') == (width >= 48),
                $"{width}-column compact view wraps long Unicode names/labels without losing text or admitting terminal controls");
        }

        var colored = await Render(Overview(basis with { ResetsAt = DateTimeOffset.Parse("2030-10-27T00:30:00Z") }, basis with { WindowLabel = "7 days" }), color: true);
        var spans = Regex.Matches(colored, @"\x1b\[([0-9;]*)m([^\x1b]*)").Cast<Match>().ToArray();
        var statusSpans = spans.Where(match => match.Groups[1].Value.Split(';').Any(code => code is "31" or "32" or "33" or "36"))
            .Select(match => match.Groups[2].Value).ToArray();
        Check(statusSpans.Any(span => span.Contains("Style account")) && statusSpans.Any(span => span.Contains('━')) &&
            statusSpans.All(span => !span.Contains("60% left") && !span.Contains("Reset ")) &&
            Plain(colored).Contains("[━━━━······]") && !colored.Contains("[####"),
            "compact colour emphasizes the account and visibly filled quota bars, leaving balance/reset text neutral");
        var metricRows = Plain(colored).Split('\n').Where(line => line.Contains('━')).ToArray();
        Check(metricRows.Length == 2 && metricRows.Select(line => HumanTable.Width(line[..line.IndexOf('━')])).Distinct().Count() == 1,
            "primary quota metrics align within an account despite different window label lengths");
        var deduplicated = await Render(new(1, "account-usage", [new(account with { Name = "UniqueVendor", Provider = "uniquevendor" }, [basis])]));
        Check(Regex.Matches(deduplicated, "uniquevendor", RegexOptions.IgnoreCase).Count == 1,
            "a provider matching the account name does not repeat the same heading text");

        var supportCases = new[] {
            ("setup-required", "Quota setup required"), ("console", "Quota: check provider console"),
            ("unimplemented", "Quota integration not available"), ("passive", "Waiting for quota observation"),
            ("awaiting-observation", "Waiting for quota observation"), ("unavailable", "No quota series reported") };
        var supportValid = true;
        foreach (var (kind, expected) in supportCases)
        {
            var quota = basis with { State = kind == "unavailable" ? "unavailable" : "unsupported", Used = null,
                Limit = null, Remaining = null, UsedPercent = null, Error = kind == "unavailable" ? null : "Adapter not implemented",
                Support = new(kind, "Complete provider instructions remain available here.", "https://example.invalid/setup") };
            var compact = Words(await Render(Overview(quota)));
            var full = await Render(Overview(quota), full: true);
            supportValid &= compact.Contains(expected) && !compact.Contains("Quota refresh error") &&
                !compact.Contains("Complete provider instructions") && full.Contains("Complete provider instructions") && full.Contains("https://example.invalid/setup");
        }
        Check(supportValid, "compact support guidance is concise and neutral while full retains every provider instruction and action link");
        var unavailable = basis with { State = "unsupported", Used = null, Limit = null, Remaining = null, UsedPercent = null };
        var grouped = await Render(new(1, "account-usage", [new(account with { Name = "Missing first" }, []),
            new(account with { Name = "Unavailable second" }, [unavailable]), new(account, [basis])]));
        Check(Regex.Matches(grouped, "No quota readings").Count == 1 && grouped.IndexOf("Style account", StringComparison.Ordinal) <
            grouped.IndexOf("No quota readings", StringComparison.Ordinal), "accounts without readings share one secondary group after measured accounts");
        var stale = basis with { State = "stale", Used = 100, Remaining = 0, UsedPercent = 100, LastSuccessAt = now.AddMinutes(-10), FreshUntil = now.AddMinutes(-5) };
        var staleText = Words(await Render(Overview(stale)));
        Check(staleText.Contains("100% used") && staleText.Contains("0% left") && staleText.Contains("Last known exhausted"),
            "styling retains the exhausted last-known reading rather than substituting unknown or a fresh zero");
        var error = "Refresh failed: " + new string('x', 120) + " END-OF-DETAIL";
        var faulted = basis with { State = "error", Error = error };
        var compactFaultRaw = await Render(Overview(faulted));
        var compactFault = Words(compactFaultRaw);
        var note = compactFaultRaw.Split('\n').Single(line => line.Contains("Note: "));
        var shortenedError = note[(note.IndexOf("Note: ", StringComparison.Ordinal) + "Note: ".Length)..].Trim();
        Check(compactFault.Contains("Refresh failed:") && compactFault.Contains('…') && !compactFault.Contains("END-OF-DETAIL") &&
            compactFault.Contains("Last known") && HumanTable.Width(shortenedError) <= 88 &&
            Words(await Render(Overview(faulted), full: true)).Contains(error),
            "compact errors retain their cause and stale reading with an ellipsis; full retains the complete diagnostic");

        // The two identical Berlin wall times straddle the autumn clock change.
        var first = basis with { WindowLabel = "5 hours", ResetsAt = DateTimeOffset.Parse("2030-10-27T00:30:00Z") };
        var second = basis with { WindowLabel = "7 days", ResetsAt = DateTimeOffset.Parse("2030-10-27T01:30:00Z") };
        var dst = Words(await Render(Overview(first, second)));
        var utc = Words(await Render(Overview(first, second), full: true));
        Check(dst.Contains("Europe/Berlin") && dst.Contains("Reset 2030-10-27 02:30 +02:00") && dst.Contains("Reset 2030-10-27 02:30 +01:00") &&
            utc.Contains("2030-10-27 00:30 UTC") && utc.Contains("2030-10-27 01:30 UTC"),
            "compact local reset offsets distinguish repeated DST wall times while full keeps exact UTC timestamps");
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        var tomorrow = new DateTimeOffset(localNow.Date.AddDays(1).AddHours(12), zone.GetUtcOffset(localNow.Date.AddDays(1).AddHours(12)));
        var soon = TimeZoneInfo.ConvertTime(now.AddMinutes(2), zone);
        var soonDay = soon.Date == localNow.Date ? "today" : "tomorrow";
        Check(Words(await Render(Overview(basis with { ResetsAt = tomorrow }))).Contains("Reset tomorrow 12:00 " + tomorrow.ToString("zzz")) &&
            Words(await Render(Overview(basis with { ResetsAt = soon }))).Contains("Reset " + soonDay + " " + soon.ToString("HH:mm zzz")),
            "compact relative reset labels use the selected zone's calendar day, time and offset");
        return checks;
    }
}
