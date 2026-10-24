using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lain.Client;
using Lain.Cli;
using Lain.Core;

internal static class HumanRenderingChecks
{
    internal static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var columns = new HumanColumn[] { new("Provider"), new("ID", Secondary: true, Identifier: true), new("Routes", Numeric: true), new("Live check", Status: true) };
        var longId = "provider/" + new string('x', 65);
        var rows = new HumanRow[]
        {
            new(["Café", "provider/cafe", "2", "Not checked"]),
            new(["東京", longId, "120", "Not checked"])
        };
        var wide = new StringWriter();
        await HumanTable.WriteAsync(wide, "Providers (2)", columns, rows, new(160, false));
        var wideText = wide.ToString();
        Check(wideText.Contains("Provider") && wideText.Contains("Routes") && wideText.Contains(longId) && !wideText.Contains('\t'), "wide human tables have headers and complete identifiers without tabs");
        var first = wideText.Split('\n').Single(line => line.StartsWith("Café", StringComparison.Ordinal));
        var second = wideText.Split('\n').Single(line => line.StartsWith("東京", StringComparison.Ordinal));
        Check(HumanTable.Width(first[..(first.IndexOf("2", StringComparison.Ordinal) + 1)]) ==
            HumanTable.Width(second[..(second.IndexOf("120", StringComparison.Ordinal) + 3)]), "numeric columns share their right edge with Unicode names");
        Check(wideText.Split('\n').All(line => HumanTable.Width(line.TrimEnd('\r')) <= 160), "wide table fits its selected terminal width");
        Check(HumanTable.Width("東京") == 4 && HumanTable.Width("e\u0301") == 1, "display width handles CJK and combining marks");

        var narrow = new StringWriter();
        await HumanTable.WriteAsync(narrow, "Providers (2)", columns, rows, new(44, false));
        Check(narrow.ToString().Contains("ID: " + longId) && !narrow.ToString().Contains('…'), "narrow table moves full IDs into copyable detail lines rather than truncating them");
        Check(narrow.ToString().Split('\n').Where(line => !line.TrimStart().StartsWith("ID:", StringComparison.Ordinal))
            .All(line => HumanTable.Width(line.TrimEnd('\r')) <= 44), "narrow nonidentifier fields fit the terminal");
        var tiny = new StringWriter();
        await HumanTable.WriteAsync(tiny, "Providers", columns, rows, new(20, false));
        Check(tiny.ToString().Contains("Provider: Café") && tiny.ToString().Contains("Live check: Not") && tiny.ToString().Contains(longId), "very narrow terminals use labelled records with full IDs");
        var empty = new StringWriter();
        await HumanTable.WriteAsync(empty, "Accounts (0)", columns, [], new(80, false));
        Check(empty.ToString().Contains("No entries."), "empty lists explain their state");

        const string hostile = "\u001b[31mRed\u001b[0m\rnext\nline\t\u202e";
        Check(HumanTable.Clean(hostile) == "Red next line", "SGR, carriage returns, line breaks, tabs and bidi controls are inert");
        Check(HumanTable.Clean("\u001b]8;;https://evil.invalid\u0007safe\u001b]8;;\u0007") == "safe", "OSC hyperlinks are removed while their visible label remains");
        Check(HumanTable.Clean("\u009b31mvisible\u009b0m") == "visible" && HumanTable.Clean("value\u001b[31") == "value", "C1 and incomplete ANSI sequences are removed");
        Check(!HumanOutputContext.ColorAllowed(false, null, "xterm") && !HumanOutputContext.ColorAllowed(true, "", "xterm")
            && !HumanOutputContext.ColorAllowed(true, "1", "xterm") && !HumanOutputContext.ColorAllowed(true, null, "dumb")
            && HumanOutputContext.ColorAllowed(true, null, "xterm"), "colour is limited to a terminal and respects NO_COLOR and TERM=dumb");
        Check(!HumanOutputContext.Detect(new StringWriter()).Color, "captured and redirected writers never receive colour");
        var coloured = new StringWriter();
        await HumanTable.WriteAsync(coloured, "Providers", columns, rows, new(160, true));
        Check(coloured.ToString().Contains("\u001b[36;1m") && coloured.ToString().Contains("\u001b[33m"), "enabled terminal colour decorates only renderer-owned headers and status values");

        var route = new ProviderRouteDescriptorDto("route-copyable", "vendor/model", "Model", "https://example.invalid/v1", "openai-chat", [], new(), FreeOnlyEligible: true);
        var descriptor = new ProviderDescriptorDto("provider-copyable", hostile, "Fixture", "https://example.invalid/account", "https://example.invalid/docs", [], [], [], [route], [], [], AdapterState: "available", LiveVerification: "live_not_tested");
        var catalog = new ProviderCatalogDto(1, DateTimeOffset.Parse("2026-10-24T12:00:00Z"),
            [descriptor, descriptor with { Id = "prepared-provider", Name = "Prepared example", AdapterState = "prepared", Routes = [] }]);
        var values = new Dictionary<string, object>
        {
            ["providers"] = catalog,
            ["accounts"] = new AccountDto[] { new("account-copyable", hostile, "provider", true, "api-key", "ready") },
            ["models"] = new ModelCatalogDto(1, 3, [new("vendor/model-copyable", "provider", ["account-copyable"])]),
            ["profiles"] = new ProfileDto[] { new("profile-copyable", hostile, ["route-copyable"], "route-copyable") },
            ["quotas"] = new QuotaDto[] { new("account-copyable", "pool-copyable", "https://example.invalid/quota#fiveHour", "stale", null, null,
                DateTimeOffset.Parse("2099-01-01T00:00:00Z"), "credits", null, null, null, true, Error: hostile, WindowLabel: "5 hours", UsedPercent: 95) }
        };
        var handler = new RenderingHandler(values);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://fixture.invalid/") };
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        foreach (var (command, value) in values)
        {
            var output = new StringWriter(); var error = new StringWriter();
            var exit = await CliApplication.RunAsync([command], output, error, () => new LainApiClient(http, "fixture-token"));
            var text = output.ToString();
            Check(exit == 0 && error.ToString() == "" && !text.Contains('\u001b') && !text.Contains('\t') && !text.Contains('\u202e'), command + " captured output is readable and terminal-safe");
            Check(text.Contains(command == "providers" ? "Providers (2)" : command == "accounts" ? "Accounts (1)" : command == "models" ? "Models (1)" : command == "profiles" ? "Profiles (1)" : "Quota windows (1)"), command + " has a labelled inventory heading");
            if (command == "providers")
            {
                Check(text.Contains("Available") && text.Contains("Prepared") && text.Contains("Not checked") && !text.Contains("live_not_tested"), "provider states are friendly without fabricating a live check");
                Check(text.Contains("not verified account access or free usage") && text.Contains("provider-copyable"), "catalog eligibility is distinct from real access and provider IDs remain usable");
            }
            if (command == "quotas") Check(text.Contains("95% used") && text.Contains("Last known") && text.Contains("Remaining 5% (amount unknown)") && text.Contains("same provider funds"), "quota rendering retains stale percentages, unknown amounts and shared budget scope");
            output = new(); error = new();
            exit = await CliApplication.RunAsync([command, "--json"], output, error, () => new LainApiClient(http, "fixture-token"));
            Check(exit == 0 && error.ToString() == "" && output.ToString() == JsonSerializer.Serialize(value, value.GetType(), jsonOptions) + Environment.NewLine,
                command + " JSON remains byte-for-byte unchanged by human rendering");
        }
        Check(handler.Calls == 10, "human and JSON listings issue only their existing read request");
        foreach (var width in new[] { 60, 80, 120 })
        {
            var quotaOutput = new StringWriter();
            await CliApplication.WriteQuotas(quotaOutput, CliPresentationFixtures.Quotas, new(width, false));
            var text = quotaOutput.ToString();
            Check(text.Split('\n').All(line => HumanTable.Width(line.TrimEnd('\r')) <= width), $"quota fixture fits {width} columns");
            Check(text.Contains("[" + new string('#', 18)) && text.Contains("40% used") && text.Contains("96% used") && text.Split('\n').Any(line => line.TrimEnd('\r').EndsWith(" 0% used", StringComparison.Ordinal)), $"{width}-column quotas retain actual bars, percentages and known zero");
            Check(text.Split('\n').Count(line => line.TrimStart().StartsWith("[", StringComparison.Ordinal)) == 4 && !text.Contains('\u001b'), "unknown, balance-only and unsupported windows do not become empty zero bars or emit colour to captures");
            Check(text.Contains("Last known") && text.Contains("Reset due") && text.Contains("Percentage unknown") && text.Contains("Quota lookup not supported"), $"{width}-column quota states distinguish stale, due, unknown and unsupported");
            Check(text.Contains("Remaining 12 USD-Credits") && text.Contains("Remaining 0 USD-Credits") && text.Contains("Reset unknown"), $"{width}-column balance-only windows retain amounts and explicit unknown resets");
            Check(text.Split("Account ID: account-codex", StringSplitOptions.None).Length == 2 && !text.Contains("Pool ID:") && !text.Contains("https://"), "account identity is grouped and technical source noise is reduced without inventing names");
            foreach (var command in new[] { "status", "usage", "help" })
            {
                var rendered = new StringWriter();
                if (command == "status") await CliApplication.WriteStatus(rendered, CliPresentationFixtures.Status, new(width, false));
                else if (command == "usage") await CliApplication.WriteUsage(rendered, CliPresentationFixtures.Usage, new(width, false));
                else await CliApplication.WriteHelp(rendered, new(width, false));
                Check(!rendered.ToString().Contains('\t') && rendered.ToString().Split('\n').All(line => HumanTable.Width(line.TrimEnd('\r')) <= width), $"{command} fits {width} columns without tabs");
                if (command == "status") Check(rendered.ToString().Contains("4 saved / 3 applied") && rendered.ToString().Contains("changes pending"), "status preserves the saved/applied distinction");
                if (command == "usage") Check(rendered.ToString().Contains("incomplete") && rendered.ToString().Contains("account-codex") && rendered.ToString().Contains("vendor/model") && rendered.ToString().Contains("unknown"), "usage preserves coverage, IDs and unknown token totals");
            }
        }
        var quotaColour = new StringWriter();
        await CliApplication.WriteQuotas(quotaColour, CliPresentationFixtures.Quotas, new(80, true));
        Check(new[] { "\u001b[32m[", "\u001b[33m[", "\u001b[31m[", "\u001b[2m" }.All(quotaColour.ToString().Contains), "terminal quotas use green/yellow/red bars and subtle freshness metadata without colouring unknown values as free");
        var fixtureJson = new StringWriter(); var fixtureError = new StringWriter();
        Check(await CliPresentationFixtures.RunAsync(["usage", "--json"], fixtureJson, fixtureError) == 0 && fixtureError.ToString() == "" &&
            fixtureJson.ToString() == JsonSerializer.Serialize(CliPresentationFixtures.Usage, jsonOptions) + Environment.NewLine, "usage human changes leave JSON byte-for-byte unchanged");
        fixtureJson = new(); fixtureError = new();
        Check(await CliPresentationFixtures.RunAsync(["status", "--json"], fixtureJson, fixtureError) == 0 && fixtureError.ToString() == "" &&
            fixtureJson.ToString() == JsonSerializer.Serialize(CliPresentationFixtures.Status, jsonOptions) + Environment.NewLine, "status human changes leave JSON byte-for-byte unchanged");
        return checks;
    }

    private sealed class RenderingHandler(Dictionary<string, object> values) : HttpMessageHandler
    {
        internal int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get || request.RequestUri?.Host != "fixture.invalid" || request.Headers.Authorization?.Parameter != "fixture-token")
                throw new Exception("Unexpected rendering test request");
            var command = request.RequestUri.AbsolutePath.Split('/').Last();
            var value = values[command];
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(value, value.GetType()) });
        }
    }
}
