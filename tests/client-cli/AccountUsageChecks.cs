using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lain.Client;
using Lain.Cli;
using Lain.Core;

internal static class AccountUsageChecks
{
    internal static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var metric = new MetricStatisticsDto(0, 0, null, null, null, null, null);
        var analysis = new UsageAnalysisDto(1, CliPresentationFixtures.Observed.AddDays(-1), CliPresentationFixtures.Observed, "UTC",
            CliPresentationFixtures.Observed, 42, true, CliPresentationFixtures.Totals, new(metric, metric, metric), []);
        var handler = new Handler(analysis);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://fixture.invalid/") };
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        async Task<(int Exit, string Output, string Error)> Run(params string[] args)
        {
            handler.Requests.Clear();
            var output = new StringWriter(); var error = new StringWriter();
            var exit = await CliApplication.RunAsync(args, output, error, () => new LainApiClient(http, "fixture-token"));
            return (exit, output.ToString(), error.ToString());
        }

        var human = await Run("usage");
        Check(human.Exit == 0 && human.Error == "" && human.Output.Contains("Account usage (6 accounts)"), "bare usage is the current all-account quota overview");
        Check(handler.Requests.SequenceEqual(new[] { "/api/v1/accounts", "/api/v1/quotas" }), "overview reads accounts and cached quotas on the same client, without historical/configuration/provider calls");
        Check(human.Output.Contains("Codex") && !human.Output.Contains("Personal Codex") && human.Output.Contains("Command Code") && human.Output.Contains("Disabled") &&
            human.Output.Contains("shared") && !human.Output.Contains("Missing credential"),
            "default table uses provider names for single accounts and keeps disabled measured accounts and shared-budget warnings while hiding missing measurements: " + human.Output);
        Check(human.Output.Contains("40% used") && human.Output.Contains("7 days") && human.Output.Contains("96% used") &&
            human.Output.Contains("3 accounts without quota readings hidden") && !human.Output.Contains("Quota not reported"),
            "measured windows stay visible and unavailable accounts contribute only to the hidden count");
        Check(!human.Output.Contains("ID:") && !human.Output.Contains("Source ") && !human.Output.Contains("Checked ") &&
            human.Output.Contains("lain usage --full") && !human.Output.Contains("same provider funds"),
            "compact overview defers source and check-time detail to the explicit full command");
        var full = await Run("usage", "--full");
        Check(full.Exit == 0 && full.Error == "" && full.Output.Contains("ID: account-codex") && full.Output.Contains("Checked ") &&
            full.Output.Contains("source ") && full.Output.Contains("same provider funds") && full.Output.Contains("Used 18 USD-Credits / 20 USD-Credits") &&
            full.Output.Contains("Missing credential") && full.Output.Contains("Quota not reported") && full.Output.Contains("No separate quota lookup"),
            "usage --full retains the detailed account and quota rendering");

        var structured = await Run("usage", "--json");
        var fullStructured = await Run("usage", "--full", "--json");
        Check(fullStructured.Exit == 0 && fullStructured.Error == "" && fullStructured.Output == structured.Output &&
            handler.Requests.SequenceEqual(new[] { "/api/v1/accounts", "/api/v1/quotas" }), "--full is presentation-only and preserves overview JSON bytes and requests");
        using (var document = JsonDocument.Parse(structured.Output))
        {
            var root = document.RootElement;
            Check(structured.Exit == 0 && root.GetProperty("version").GetInt32() == 1 && root.GetProperty("kind").GetString() == "account-usage", "account overview JSON has its own versioned CLI kind");
            var entries = root.GetProperty("accounts").EnumerateArray().ToArray();
            Check(entries.Length == 6 && entries.Select(entry => entry.GetProperty("account").GetProperty("id").GetString())
                .SequenceEqual(CliPresentationFixtures.Accounts.Select(account => account.Id)), "overview JSON includes every configured account in host order");
            var missing = entries.Single(entry => entry.GetProperty("account").GetProperty("id").GetString() == "account-missing");
            Check(missing.GetProperty("quotas").GetArrayLength() == 0, "a missing quota snapshot stays an empty array instead of a synthetic zero reading");
            var disabled = entries.Single(entry => entry.GetProperty("account").GetProperty("id").GetString() == "account-credits");
            Check(!disabled.GetProperty("account").GetProperty("enabled").GetBoolean() && disabled.GetProperty("quotas").GetArrayLength() == 3,
                "disabled accounts retain their identity and reported windows in JSON");
        }
        var overview = new AccountUsageOverview(1, "account-usage", CliPresentationFixtures.Accounts.Select(account =>
            new AccountUsageItem(account, CliPresentationFixtures.Quotas.Where(quota => quota.AccountId == account.Id).ToArray())).ToArray());
        foreach (var width in new[] { 60, 80, 120 })
        {
            var output = new StringWriter();
            await CliApplication.WriteAccountUsage(output, overview, new(width, false), timeZone: TimeZoneInfo.Utc);
            var detailed = new StringWriter();
            await CliApplication.WriteAccountUsage(detailed, overview, new(width, false), full: true);
            Check(output.ToString().Split('\n').All(line => HumanTable.Width(line.TrimEnd('\r')) <= width) &&
                detailed.ToString().Split('\n').All(line => HumanTable.Width(line.TrimEnd('\r')) <= width), $"compact and full all-account overview fit {width} columns");
            Check(output.ToString().Split('\n').Length < detailed.ToString().Split('\n').Length &&
                output.ToString().Contains("12 USD-Credits left") && output.ToString().Contains("due") && output.ToString().Contains("Last known"),
                $"{width}-column compact overview remains substantially shorter while retaining balances and warnings");
            var compactLines = output.ToString().Split('\n');
            Check(new[] { "40% used", "12 USD-Credits left", "2099-01-01", "+00:00", "due", "Last known" }
                .All(field => compactLines.Any(line => line.Contains(field))), $"{width}-column table retains native amounts, reset date and timezone with warnings");
        }

        var now = DateTimeOffset.UtcNow;
        var freshness = new AccountUsageOverview(1, "account-usage", [new(
            CliPresentationFixtures.Accounts[0] with { State = "unverified", ExpiresAt = now.AddMinutes(-1) },
            [CliPresentationFixtures.Quotas[0] with { LastSuccessAt = now.AddMinutes(-6), ResetsAt = now.AddHours(2), WindowLabel = "Old snapshot" },
             CliPresentationFixtures.Quotas[0] with { LastSuccessAt = now, ResetsAt = now.AddHours(2), WindowLabel = "Fresh snapshot" },
             CliPresentationFixtures.Quotas[5] with { WindowLabel = "Unknown snapshot" }])]);
        var ageOutput = new StringWriter();
        await CliApplication.WriteAccountUsage(ageOutput, freshness, new(160, true));
        var ageLines = ageOutput.ToString().Split('\n');
        Check(ageLines.Single(line => line.Contains("Old snapshot")).Contains("Last known") &&
            ageLines.Single(line => line.Contains("Old snapshot")).Contains("\u001b[33m") &&
            !ageLines.Single(line => line.Contains("Fresh snapshot")).Contains("Last known") &&
            !ageOutput.ToString().Contains("\u001b[32m"), "old successful readings carry explicit last-known warnings while fresh readings remain neutral");
        Check(ageOutput.ToString().Contains("Expired") && !ageOutput.ToString().Contains("Unverified") &&
            !ageOutput.ToString().Contains("Unknown snapshot") && !ageOutput.ToString().Contains("] 0% used") &&
            !ageOutput.ToString().Contains("· 0% left"), "compact output retains expiry and defers the unknown secondary window without fabricating a zero");
        var ageFull = new StringWriter();
        await CliApplication.WriteAccountUsage(ageFull, freshness, new(120, false), full: true);
        var unknownDetail = ageFull.ToString().Split("Unknown snapshot", StringSplitOptions.None).Last();
        Check(ageFull.ToString().Contains("Credential state: Not checked") && ageFull.ToString().Contains("Last known") &&
            ageFull.ToString().Contains("Unknown snapshot") && unknownDetail.Contains("Quota values not reported") &&
            !unknownDetail.Contains("% used") && !unknownDetail.Contains("Remaining 0"),
            "full output retains credential details, age warnings and the unknown secondary window without invented readings");

        var fifteenWindows = new AccountUsageOverview(1, "account-usage", Enumerable.Range(0, 6).Select(index =>
            new AccountUsageItem(new("compact-" + index, "Account " + index, "fixture", true, "api-key", "unverified"),
                Enumerable.Range(0, index < 3 ? 3 : 2).Select(window => CliPresentationFixtures.Quotas[0] with
                { LastSuccessAt = now, ResetsAt = now.AddHours(2), WindowLabel = "Window " + window }).ToArray())).ToArray());
        var budgetOutput = new StringWriter();
        await CliApplication.WriteAccountUsage(budgetOutput, fifteenWindows, new(160, false));
        Check(budgetOutput.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length <= 24 &&
            budgetOutput.ToString().Split('\n').Count(line => line.Contains("40% used")) == 12 &&
            budgetOutput.ToString().Split('\n').Count(line => line.Contains("more: --full")) == 3,
            "fifteen quota windows show at most two primary readings per account, with explicit full-detail paths, in twenty-four lines");
        // Presentation hierarchy must not change the source envelope or lose the only known budget.
        var basis = CliPresentationFixtures.Quotas[0] with { AccountId = "hierarchy", SharedWithLegacy = false, LastSuccessAt = now,
            FreshUntil = now.AddMinutes(5), ResetsAt = now.AddHours(2) };
        var weekly = basis with { WindowLabel = "7 days", Source = "fixture#weekly" };
        var sevenHours = basis with { WindowLabel = "7 hours", Source = "fixture#seven-hour" };
        var fiveHours = basis with { WindowLabel = "5 hours", Source = "fixture#fiveHour" };
        var monthly = basis with { WindowLabel = "Monthly allowance", Source = "fixture#monthly" };
        var purchased = basis with { WindowLabel = "Purchased credits", Source = "fixture#purchasedCredits", MeasurementKind = "balance",
            Unit = "credits", Used = null, Limit = null, Remaining = 12, UsedPercent = null, ResetsAt = null };
        var freeZero = purchased with { WindowLabel = "Free credits", Source = "fixture#freeCredits", Remaining = 0 };
        var unknown = basis with { WindowLabel = "Unknown budget", Source = "fixture#unknown", Used = null, Limit = null,
            Remaining = null, UsedPercent = null, ResetsAt = null, State = "unavailable" };
        async Task<string> Render(QuotaDto[] quotas, bool fullDetails = false)
        {
            var writer = new StringWriter();
            await CliApplication.WriteAccountUsage(writer, new(1, "account-usage", [new(
                new("hierarchy", "Hierarchy account", "fixture", true, "api-key", "ready"), quotas)]), new(120, false), full: fullDetails);
            return writer.ToString();
        }
        var hierarchyQuotas = new[] { monthly, weekly, sevenHours, freeZero, purchased, fiveHours };
        var hierarchy = await Render(hierarchyQuotas);
        Check(hierarchy.IndexOf("5 hours", StringComparison.Ordinal) < hierarchy.IndexOf("7 hours", StringComparison.Ordinal)
            && !hierarchy.Contains("Monthly allowance") && !hierarchy.Contains("7 days")
            && hierarchy.Contains("more: --full"), "shortest two explicit periods lead compact output; longer windows stay reachable via full");
        Check(hierarchy.Contains("Purchased credits") && hierarchy.Contains("12 credits left") && !hierarchy.Contains("Free credits"),
            "positive extra credits stay concise while zero-only free credits do not occupy compact rows");
        var hierarchyFull = await Render(hierarchyQuotas, fullDetails: true);
        Check(new[] { "5 hours", "7 hours", "7 days", "Monthly allowance", "Purchased credits", "Free credits" }.All(hierarchyFull.Contains)
            && hierarchyFull.Contains("Remaining 0 credits"), "full rendering preserves every longer and zero-credit reading");
        Check((await Render([monthly])).Contains("Monthly allowance") && (await Render([purchased])).Contains("12 credits left"),
            "a sole monthly allowance or credit balance remains the visible main reading");
        var mainZero = purchased with { WindowLabel = "Account balance", Source = "fixture#account-balance", Remaining = 0 };
        var emptyBalance = await Render([mainZero, freeZero]);
        Check(emptyBalance.Contains("Account balance") && emptyBalance.Contains("0 credits left") && emptyBalance.Contains("Exhausted")
            && !emptyBalance.Contains("Free credits") && !emptyBalance.Contains("more: --full"),
            "a real exhausted primary balance remains visible; hidden zero-only extras do not add detail hints");
        var unknownExtra = freeZero with { Remaining = null, State = "unavailable" };
        var unknownOutput = await Render([unknownExtra]);
        Check(!unknownOutput.Contains("Free credits") && unknownOutput.Contains("1 account without quota readings hidden")
            && !unknownOutput.Contains("0 credits") && !unknownOutput.Contains("Exhausted"), "unknown extra credits remain unknown and hidden by default");
        var shortCredits = fiveHours with { WindowLabel = "5 hours credits", Unit = "credits", Used = 18, Limit = 20,
            Remaining = 2, UsedPercent = 90 };
        var exhaustedShort = await Render([shortCredits with { Used = 20, Remaining = 0, UsedPercent = 100 }, monthly]);
        Check(exhaustedShort.Contains("5 hours credits") && exhaustedShort.Contains("100% used") && exhaustedShort.Contains("0 credits left")
            && !exhaustedShort.Contains("Monthly allowance"), "five-hour credit allowances remain primary even when exhausted");
        var resetOnly = basis with { WindowLabel = "Unspecified period", Source = "fixture#opaque", ResetsAt = now.AddMinutes(1) };
        var explicitPeriod = fiveHours with { WindowLabel = "Explicit five-hour window", ResetsAt = now.AddDays(3) };
        var durationOutput = await Render([resetOnly, explicitPeriod]);
        Check(durationOutput.IndexOf("Explicit five-hour window", StringComparison.Ordinal) < durationOutput.IndexOf("Unspecified period", StringComparison.Ordinal),
            "an imminent reset does not manufacture a shorter quota duration");
        var faultedMonthly = monthly with { State = "error", Error = "Refresh failed" };
        var faultOutput = await Render([fiveHours, sevenHours, faultedMonthly]);
        var unknownMonthlyOutput = await Render([fiveHours, unknown with { WindowLabel = "Unknown monthly allowance" }]);
        Check(faultOutput.Contains("Quota refresh error") && faultOutput.Contains("Last known quota") && faultOutput.Contains("--full")
            && !unknownMonthlyOutput.Contains("Unknown monthly allowance") && !unknownMonthlyOutput.Contains("more: --full"),
            "deferred refresh errors stay visible while unknown secondary windows do not add compact diagnostic rows");
        var orderedOverview = new AccountUsageOverview(1, "account-usage", [
            new(new("missing", "Missing first", "fixture", true, "api-key", "ready"), [unknown]),
            new(new("zero", "Zero extras", "fixture", true, "api-key", "ready"), [freeZero]),
            new(new("known", "Real balance", "fixture", true, "api-key", "ready"), [mainZero])]);
        var orderWriter = new StringWriter();
        await CliApplication.WriteAccountUsage(orderWriter, orderedOverview, new(120, false));
        Check(orderWriter.ToString().Contains("Real balance") && !orderWriter.ToString().Contains("Missing first")
            && !orderWriter.ToString().Contains("Zero extras") && orderWriter.ToString().Contains("2 accounts without quota readings hidden"),
            "real zero primary balances remain visible while unmeasured accounts and zero-only extra wallets stay hidden");
        var expectedOverview = JsonSerializer.Serialize(overview, jsonOptions) + Environment.NewLine;
        Check(structured.Output == expectedOverview && fullStructured.Output == expectedOverview,
            "compact selection and human sorting preserve the original account/window order and exact complete JSON bytes");

        foreach (var args in new[]
        {
            new[] { "usage", "--full", "--today", "--json" },
            new[] { "usage", "analysis", "--full" },
            new[] { "history", "--full" }
        })
        {
            var rejected = await Run(args);
            Check(rejected.Exit == 2 && rejected.Output == "" && rejected.Error.Contains("--full") && handler.Requests.Count == 0,
                string.Join(' ', args) + " rejects overview-only --full before host access");
        }

        var expectedHistory = JsonSerializer.Serialize(CliPresentationFixtures.Usage, jsonOptions) + Environment.NewLine;
        foreach (var args in new[]
        {
            new[] { "history", "--json" },
            new[] { "usage", "--today", "--json" },
            new[] { "history", "--today", "--json" },
            new[] { "usage", "--from", "2026-10-23T00:00:00Z", "--to", "2026-10-24T00:00:00Z", "--account", "account-codex", "--json" },
            new[] { "history", "--from", "2026-10-23T00:00:00Z", "--to", "2026-10-24T00:00:00Z", "--account", "account-codex", "--json" }
        })
        {
            var result = await Run(args);
            Check(result.Exit == 0 && result.Error == "" && result.Output == expectedHistory && handler.Requests.Count == 1 && handler.Requests[0].StartsWith("/api/v1/usage?", StringComparison.Ordinal),
                string.Join(' ', args) + " retains historical API and exact JSON");
        }
        var expectedAnalysis = JsonSerializer.Serialize(analysis, jsonOptions) + Environment.NewLine;
        foreach (var command in new[] { "history", "usage" })
        foreach (var action in new[] { "analysis", "aggregate", "performance" })
        {
            var result = await Run(command, action, "--group-by", "accountId", "--json");
            Check(result.Exit == 0 && result.Error == "" && result.Output == expectedAnalysis && handler.Requests.Single().StartsWith("/api/v1/usage/analysis?", StringComparison.Ordinal),
                command + " " + action + " preserves aggregate payload and endpoint");
        }
        var raw = await Run("quotas", "--json");
        Check(raw.Exit == 0 && raw.Output == JsonSerializer.Serialize(CliPresentationFixtures.Quotas, jsonOptions) + Environment.NewLine && handler.Requests.Single() == "/api/v1/quotas", "raw quotas JSON remains its unchanged DTO array and one GET");
        handler.QuotaFailure = true;
        var failed = await Run("usage", "--json");
        Check(failed.Exit == 3 && failed.Output == "" && failed.Error.Contains("fixture_quota_unavailable"), "overview quota GET failures retain existing exit and diagnostic paths without partial JSON");
        return checks;
    }

    private sealed class Handler(UsageAnalysisDto analysis) : HttpMessageHandler
    {
        internal readonly List<string> Requests = [];
        internal bool QuotaFailure;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get || request.RequestUri?.Host != "fixture.invalid" || request.Headers.Authorization?.Parameter != "fixture-token")
                throw new Exception("Unexpected account overview test request");
            Requests.Add(request.RequestUri.PathAndQuery);
            var path = request.RequestUri.AbsolutePath;
            if (QuotaFailure && path == "/api/v1/quotas") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                { Content = JsonContent.Create(new ApiError("fixture_quota_unavailable", "Quota snapshots unavailable.")) });
            object value = path switch
            {
                "/api/v1/accounts" => CliPresentationFixtures.Accounts,
                "/api/v1/quotas" => CliPresentationFixtures.Quotas,
                "/api/v1/usage" => CliPresentationFixtures.Usage,
                "/api/v1/usage/analysis" => analysis,
                _ => throw new Exception("Unexpected overview test endpoint")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(value, value.GetType()) });
        }
    }
}
