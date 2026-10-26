using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lain.Client;
using Lain.Cli;
using Lain.Core;

internal static class SetupPresentationChecks
{
    internal static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool condition, string description) { checks++; if (!condition) throw new Exception(description); }
        Check(AccountNameSuggestions.Suggest("groq", "Groq", []) == "Groq", "short provider name");
        Check(AccountNameSuggestions.Suggest("gemini-api", "Gemini API", []) == "Gemini", "Gemini alias");
        Check(AccountNameSuggestions.Suggest("groq", "Groq", ["groq", "Groq 2", "My Groq account"]) == "Groq 3", "case insensitive collision suffix");
        var names = new[] { "My Groq account", "Research Gemini", "NVIDIA custom", "Worker project", "Router key" };
        var providers = new[] { "groq", "gemini-api", "nvidia-build", "cloudflare-workers-ai", "openrouter" };
        var observed = DateTimeOffset.UtcNow;
        var accounts = names.Select((name, index) => new AccountDto("account-" + index, name, providers[index], true, "api-key", "ready")).ToArray();
        var overview = new AccountUsageOverview(1, "account-usage", accounts.Select((account, index) => new AccountUsageItem(account,
            [index == 4 ? new(account.Id, null, "https://openrouter.ai/api/v1/key", "ok", observed, observed, null,
                "USD", 2, 10, 8, false, WindowLabel: "Key spending limit")
             : new(account.Id, null, "unavailable", "unsupported", observed, null, null, "unknown", null, null, null, false,
                Error: "This provider has no supported quota adapter.")])).ToArray());
        var output = new StringWriter();
        await CliApplication.WriteAccountUsage(output, overview, new(120, false));
        var text = output.ToString();
        Check(text.Contains("OpenRouter") && !text.Contains(names[4]) && names.Take(4).All(name => !text.Contains(name)), "compact uses provider for a single measured account and hides accounts without readings");
        Check(text.Contains("4 accounts without quota readings hidden") && !text.Contains("No separate quota lookup"), "unsupported accounts contribute only to the neutral hidden count");
        Check(!text.Contains("Quota warning") && !text.Contains("Quota refresh error") && !text.Contains("Remaining unavailable") && !text.Contains("Reset unknown"), "unsupported is not a warning or repeated missing metrics");
        Check(text.Contains("Key spending limit") && text.Contains("20% used") && text.Contains("8 USD left"), "percentage identifies a bounded key spending limit");
        var fullOutput = new StringWriter();
        await CliApplication.WriteAccountUsage(fullOutput, overview, new(120, false), full: true);
        Check(names.All(fullOutput.ToString().Contains) && fullOutput.ToString().Split("No separate quota lookup", StringSplitOptions.None).Length == 5,
            "full preserves every stored name and all unsupported-account guidance");
        using (var http = new HttpClient(new CachedQuotaHandler(accounts, overview.Accounts.SelectMany(item => item.Quotas).ToArray()))
            { BaseAddress = new Uri("http://fixture.invalid/") })
        {
            var expected = JsonSerializer.Serialize(overview, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }) + Environment.NewLine;
            foreach (var command in new[] { new[] { "usage", "--json" }, new[] { "usage", "--full", "--json" } })
            {
                var writer = new StringWriter(); var errors = new StringWriter();
                var result = await CliApplication.RunAsync(command, writer, errors, () => new LainApiClient(http));
                Check(result == 0 && errors.ToString() == "" && writer.ToString() == expected,
                    string.Join(' ', command) + " preserves exact complete custom names, unsupported snapshots and diagnostics");
            }
        }
        Check(!text.Contains("Source ") && !text.Contains("Checked ") && text.Contains("lain usage --full") &&
            fullOutput.ToString().Contains("source openrouter.ai") && fullOutput.ToString().Contains("Checked "),
            "compact defers observed quota source and check time to full output");
        var balance = overview with { Accounts = [new(accounts[4], [overview.Accounts[4].Quotas[0] with { Used = null, Limit = null, Remaining = 8, WindowLabel = "Credit balance" }])] };
        output = new(); await CliApplication.WriteAccountUsage(output, balance, new(120, false));
        Check(output.ToString().Contains("8 USD left") && !output.ToString().Contains("%") && !output.ToString().Contains("Reset"), "balance without denominator has no percent or invented reset");
        var template = overview.Accounts[4].Quotas[0];
        async Task<string> Render(QuotaDto quota, bool full = false)
        {
            var writer = new StringWriter();
            await CliApplication.WriteAccountUsage(writer, new(1, "account-usage", [new(accounts[4], [quota])]), new(120, false), full: full);
            return writer.ToString();
        }
        var balanceText = await Render(template with { MeasurementKind = "balance", UsedPercent = 20 });
        Check(!balanceText.Contains("%") && balanceText.Contains("8 USD left"), "balance measurement cannot acquire a percent bar from supplied percentage");
        var usageText = await Render(template with { MeasurementKind = "usage", Unit = "requests", Used = 123, Limit = null, Remaining = null, UsedPercent = null,
            Scope = "project", ScopeId = "fixture-project", DataAsOf = observed.AddMinutes(-20) });
        Check(usageText.Contains("Used 123 requests") && !usageText.Contains("Remaining") && !usageText.Contains("%"), "project usage is not a remaining allowance");
        var passive = template with { State = "awaiting-observation", Used = null, Limit = null, Remaining = null,
            Support = new("awaiting-observation", "Limits appear after ordinary model use.", "https://example.invalid/limits") };
        var supportText = await Render(passive);
        var fullPassive = await Render(passive, full: true);
        Check(supportText.Contains("1 account without quota readings hidden") && !supportText.Contains(accounts[4].Name)
            && !supportText.Contains("Limits appear after ordinary model use.") && !supportText.Contains("https://example.invalid/limits")
            && fullPassive.Contains(accounts[4].Name) && fullPassive.Contains("Limits appear after ordinary model use.")
            && fullPassive.Contains("https://example.invalid/limits") && !supportText.Contains("warning"),
            "unmeasured passive accounts stay hidden while full retains their name, guidance and action link");
        var errorText = await Render(template with { State = "error", Error = "HTTP 401: monitor access expired", Support = new("passive", "After use") });
        Check(errorText.Contains("401") && errorText.Contains("Last known"), "real authentication failure retains last-good values and its error");
        var overdrawn = await Render(template with { MeasurementKind = "quota", Used = 13, Remaining = -3 });
        Check(overdrawn.Contains("-3 USD left") && overdrawn.Contains("Exhausted") && !overdrawn.Contains("%"), "negative observed remaining is visible without a fabricated clamped percentage");
        // Host snapshot after HTTP 200 {"timeSeries":[]} from Gemini Monitoring.
        // The quota-source tests separately qualify that upstream normalization.
        const string noSeriesMessage = "No Gemini API request series was returned for this project and UTC day. This does not prove zero usage or remaining quota.";
        var noSeries = new QuotaDto(accounts[1].Id, null, "gemini:cloud-monitoring:api-requests", "unavailable", null, null, null,
            "unknown", null, null, null, false, Support: new("unavailable", noSeriesMessage, "https://aistudio.google.com/usage"));
        foreach (var command in new[] { new[] { "usage" }, new[] { "usage", "--full" }, new[] { "quotas" } })
        {
            using var http = new HttpClient(new CachedQuotaHandler([accounts[1]], [noSeries])) { BaseAddress = new Uri("http://fixture.invalid/") };
            var writer = new StringWriter(); var errors = new StringWriter();
            var result = await CliApplication.RunAsync(command, writer, errors, () => new LainApiClient(http));
            var rendered = writer.ToString();
            var compact = command.SequenceEqual(new[] { "usage" });
            Check(result == 0 && errors.ToString() == "" &&
                (compact ? rendered.Contains("1 account without quota readings hidden") && !rendered.Contains(accounts[1].Name) && !rendered.Contains("No Gemini API request series") &&
                    !rendered.Contains("https://aistudio.google.com/usage") && rendered.Contains("lain usage --full")
                    : rendered.Contains("No Gemini API request series") && rendered.Contains("https://aistudio.google.com/usage")),
                string.Join(' ', command) + " preserves successful empty-monitor guidance, with action links in detailed output");
            Check(!rendered.Contains("Unavailable") && !rendered.Contains("Quota values not reported") && !rendered.Contains("Last known") && !rendered.Contains("%") && !rendered.Contains("Remaining unknown"),
                string.Join(' ', command) + " has no error state, stale value or fabricated zero for HTTP200 no series");
        }
        var failedEmpty = await Render(noSeries with { Error = "HTTP 403: monitoring.read denied" });
        var fullFailedEmpty = await Render(noSeries with { Error = "HTTP 403: monitoring.read denied" }, full: true);
        Check(failedEmpty.Contains("1 account without quota readings hidden") && !failedEmpty.Contains("403")
            && !failedEmpty.Contains(accounts[4].Name) && fullFailedEmpty.Contains("403") && fullFailedEmpty.Contains("Unavailable")
            && !failedEmpty.Contains("No Gemini API request series"),
            "an error without measurements stays hidden by default while full retains the failed lookup diagnostic");
        var retained = await Render(noSeries with { State = "stale", Used = 123, Unit = "requests", MeasurementKind = "usage", LastSuccessAt = observed.AddMinutes(-10) });
        Check(retained.Contains("Last known") && retained.Contains("Used 123 requests") && !retained.Contains("No Gemini API request series"), "empty successful refresh does not hide retained stale usage");
        return checks;
    }

    private sealed class CachedQuotaHandler(AccountDto[] accounts, QuotaDto[] quotas) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get || request.RequestUri?.Host != "fixture.invalid") throw new Exception("Unexpected empty-monitor presentation request");
            object body = request.RequestUri.AbsolutePath switch
            {
                "/api/v1/accounts" => accounts,
                "/api/v1/quotas" => quotas,
                _ => throw new Exception("Empty-monitor fixture permits only cached account/quota reads")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body, body.GetType()) });
        }
    }
}
