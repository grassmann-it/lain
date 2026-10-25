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
        string Lines(string text) => string.Join('\n', Plain(text).Split('\n').Select(line => line.TrimEnd()));
        var longName = string.Concat(Enumerable.Repeat("研究😀", 18));
        var hostile = "\u001b[31mVisible\u001b[0m\u001b]8;;https://hostile.invalid\aLink\u001b]8;;\a\r\n\t\u202e";
        var longOverview = new AccountUsageOverview(1, "account-usage", [new(account with { Name = longName + hostile },
            [basis with { WindowLabel = string.Concat(Enumerable.Repeat("非常に長い枠", 10)) },
             basis with { WindowLabel = "7 days", Used = 100, Remaining = 0, UsedPercent = 100 }])]);
        foreach (var width in new[] { 20, 40, 60, 80, 100, 120, 160, 200 })
        {
            var text = await Render(longOverview, width);
            var compactCharacters = Regex.Replace(text, @"\s+", "");
            Check(text.Split('\n').All(line => HumanTable.Width(line.TrimEnd('\r')) <= width)
                && !text.Contains('\u001b') && !text.Contains('\t') && !text.Contains('\u202e') && !text.Contains("hostile.invalid")
                && compactCharacters.Contains(longName + "VisibleLink") && Words(text).Contains("Exhausted")
                && text.Contains("---"), $"{width}-column table preserves long Unicode account names and rejects terminal controls");
        }

        foreach (var width in new[] { 60, 80, 100, 120, 160, 200 })
        {
            var text = await Render(Overview(basis, basis with { WindowLabel = "7 days" }), width);
            var metricRows = text.Split('\n').Where(line => line.Contains("60% left")).ToArray();
            Check(metricRows.Length == 2 && metricRows.Select(line => line.IndexOf("60% left", StringComparison.Ordinal)).Distinct().Count() == 1,
                $"{width}-column readings share a fixed table column across windows");
            Check((width >= 110) == text.Split('\n').Any(line => line.TrimStart().StartsWith("Account  ")),
                $"{width}-column layout uses account cells only when the table has room");
        }
        foreach (var width in new[] { 20, 60, 120, 160 })
        {
            var wallet = basis with { MeasurementKind = "balance", Used = null, Limit = null, Remaining = 8,
                UsedPercent = null, Unit = "USD", WindowLabel = "Credit balance" };
            var noReset = await Render(Overview(wallet), width);
            Check(noReset.Contains("8 USD left") && !noReset.Contains("Reset") && !noReset.Contains("unknown"),
                $"{width}-column wallet-only overview omits empty reset columns and details");
        }
        var mixedReset = await Render(Overview(basis with { ResetsAt = now.AddHours(2) }, basis with { WindowLabel = "7 days" }), width: 160);
        Check(mixedReset.Contains("Reset") && mixedReset.Contains('—') && !mixedReset.Contains("Reset unknown"),
            "mixed reset data keeps its reset column with a neutral missing-value dash");
        var colored = await Render(Overview(basis with { State = "stale", FreshUntil = now.AddMinutes(-1) }), color: true);
        var spans = Regex.Matches(colored, @"\x1b\[([0-9;]*)m([^\x1b]*)").Cast<Match>().ToArray();
        Check(colored.StartsWith("\u001b[0m", StringComparison.Ordinal) && !colored.Contains("\u001b[36") && !colored.Contains("\u001b[32")
            && spans.Where(match => match.Groups[1].Value is "31" or "33").All(match => !match.Groups[2].Value.Contains("60% left"))
            && colored.Contains("\u001b[33mLast known\u001b[0m"),
            "table resets inherited shell styling and reserves colour for explicit warning text");
        Check(Lines(colored) == Lines(await Render(Overview(basis with { State = "stale", FreshUntil = now.AddMinutes(-1) }))),
            "NO_COLOR/plain presentation retains the same table content and alignment");

        var names = new[] {
            ("gemini-api", "My Gemini API account", "Gemini"), ("nvidia-build", "My NVIDIA Build account", "NVIDIA"),
            ("cloudflare-workers-ai", "My Cloudflare Workers AI account", "Cloudflare"),
            ("command-code", "My Command Code account", "Command Code") };
        foreach (var (provider, name, expected) in names)
        {
            var text = await Render(new(1, "account-usage", [new(account with { Provider = provider, Name = name }, [basis])]));
            Check(text.Contains(expected) && !text.Contains(name) && !text.Contains(provider),
                "known generated " + provider + " names use short provider labels without changing stored data");
        }
        var ownName = "My science account";
        var custom = await Render(new(1, "account-usage", [new(account with { Provider = "gemini-api", Name = ownName }, [basis])]));
        Check(custom.Contains(ownName), "an unrelated My … account name remains an actual custom name");
        var emailAccount = account with { Provider = "codex", Name = "person@example.invalid" };
        var emailOverview = new AccountUsageOverview(1, "account-usage", [new(emailAccount, [basis])]);
        var singleEmail = await Render(emailOverview);
        Check(singleEmail.Contains("Codex") && !singleEmail.Contains(emailAccount.Name)
            && (await Render(emailOverview, full: true)).Contains(emailAccount.Name),
            "a single provider account uses its provider label for an imported email name; full retains the actual name");
        var multiEmail = await Render(emailOverview with { Accounts = [emailOverview.Accounts[0],
            new(emailAccount with { Id = "another", Name = "other@example.invalid" }, [basis])] });
        Check(multiEmail.Contains(emailAccount.Name) && multiEmail.Contains("other@example.invalid"),
            "multiple provider accounts preserve distinct email names");
        var multiple = new AccountUsageOverview(1, "account-usage", [
            new(account with { Id = "first", Name = "My Gemini API account", Provider = "gemini-api" }, [basis]),
            new(account with { Id = "second", Name = "My Gemini API account", Provider = "gemini-api" }, [basis]),
            new(account with { Id = "hidden", Name = "My Gemini API account", Provider = "gemini-api" }, [])]);
        var multipleText = await Render(multiple);
        Check(multipleText.Contains("Gemini 1") && multipleText.Contains("Gemini 2") && !multipleText.Contains("Gemini 3")
            && (await Render(multiple with { Accounts = [multiple.Accounts[1], multiple.Accounts[2]] })).Contains("Gemini 1"),
            "multiple default names remain distinct; numbering uses all configured siblings before measurement filtering");
        var duplicateCustom = await Render(new(1, "account-usage", [
            new(account with { Id = "one", Name = ownName }, [basis]), new(account with { Id = "two", Name = ownName }, [basis])]));
        Check(Regex.Matches(duplicateCustom, ownName).Count == 2 && duplicateCustom.Contains("(Fixture 1)") && duplicateCustom.Contains("(Fixture 2)"),
            "duplicate own account names stay distinguishable without displaying raw IDs");

        foreach (var kind in new[] { "setup-required", "console", "unimplemented", "passive", "awaiting-observation", "unavailable" })
        {
            var quota = basis with { State = kind == "unavailable" ? "unavailable" : "unsupported", Used = null,
                Limit = null, Remaining = null, UsedPercent = null, Error = null,
                Support = new(kind, "Complete provider instructions remain available here.", "https://example.invalid/setup") };
            var compact = Words(await Render(Overview(quota)));
            var full = await Render(Overview(quota), full: true);
            Check(!compact.Contains("Style account") && compact.Contains("1 account without quota readings hidden")
                && !compact.Contains("Complete provider instructions") && full.Contains("Complete provider instructions") && full.Contains("https://example.invalid/setup"),
                kind + " accounts are hidden in default while full retains instructions and actions");
        }
        var unknown = basis with { Used = null, Limit = null, Remaining = null, UsedPercent = null, State = "error", Error = "No data" };
        var mixed = await Render(new(1, "account-usage", [new(account with { Name = "No snapshot" }, []),
            new(account with { Name = "No measurement" }, [unknown]), new(account, [basis])]));
        Check(mixed.Contains("Style account") && !mixed.Contains("No snapshot") && !mixed.Contains("No measurement")
            && mixed.Contains("2 accounts without quota readings hidden"), "unmeasured accounts contribute only to the neutral hidden count");
        var invalid = basis with { Remaining = double.NaN, Used = double.PositiveInfinity, UsedPercent = -1, Limit = null };
        Check(!Words(await Render(Overview(invalid))).Contains("Style account"), "nonfinite and invalid numeric fields do not create a measured account");
        var stale = basis with { State = "stale", Used = 100, Remaining = 0, UsedPercent = 100, LastSuccessAt = now.AddMinutes(-10), FreshUntil = now.AddMinutes(-5) };
        var staleText = Words(await Render(Overview(stale)));
        Check(staleText.Contains("100% used") && staleText.Contains("0% left") && staleText.Contains("Last known exhausted"),
            "real exhausted last-known readings remain visible");
        foreach (var (kind, label) in new[] { ("usage", "Used"), ("usage-estimate", "Est. used"), ("rate", "Observed"), ("limit", "Limit") })
        {
            var quota = basis with { MeasurementKind = kind, Unit = "tokens", Used = 12, Limit = 100, Remaining = 88, UsedPercent = 12 };
            var text = Words(await Render(Overview(quota), width: 160));
            Check(text.Contains(label + (kind == "limit" ? " 100 tokens" : " 12 tokens")) && !text.Contains("left")
                && !text.Contains("12% used") && !text.Contains("Exhausted"), kind + " remains its native reading rather than an invented remaining budget");
        }
        var error = "Refresh failed: " + new string('x', 120) + " END-OF-DETAIL";
        var faulted = basis with { State = "error", Error = error };
        var compactFault = Words(await Render(Overview(faulted)));
        Check(compactFault.Contains("Refresh failed:") && compactFault.Contains('…') && !compactFault.Contains("END-OF-DETAIL")
            && compactFault.Contains("Last known") && Words(await Render(Overview(faulted), full: true)).Contains(error),
            "compact errors retain a bounded cause and last-known reading; full retains the complete diagnostic");

        var first = basis with { WindowLabel = "5 hours", ResetsAt = DateTimeOffset.Parse("2030-10-27T00:30:00Z") };
        var second = basis with { WindowLabel = "7 days", ResetsAt = DateTimeOffset.Parse("2030-10-27T01:30:00Z") };
        var dst = Words(await Render(Overview(first, second), width: 200));
        var utc = Words(await Render(Overview(first, second), full: true));
        Check(dst.Contains("Europe/Berlin") && dst.Contains("2030-10-27 02:30 +02:00") && dst.Contains("2030-10-27 02:30 +01:00")
            && utc.Contains("2030-10-27 00:30 UTC") && utc.Contains("2030-10-27 01:30 UTC"),
            "table reset offsets distinguish repeated DST wall times while full keeps exact UTC timestamps");
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        var tomorrow = new DateTimeOffset(localNow.Date.AddDays(1).AddHours(12), zone.GetUtcOffset(localNow.Date.AddDays(1).AddHours(12)));
        Check(Words(await Render(Overview(basis with { ResetsAt = tomorrow }), width: 200)).Contains("tomorrow 12:00 " + tomorrow.ToString("zzz")),
            "relative reset labels use the selected timezone calendar date and offset");
        return checks;
    }
}
