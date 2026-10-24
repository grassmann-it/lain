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
        Check(human.Output.Contains("Personal Codex · codex · Enabled") && human.Output.Contains("Coding credits · command-code · Disabled") &&
            human.Output.Contains("Missing credential") && human.Output.Contains("Credential state: Missing"), "overview uses actual account names/providers and retains disabled or missing-credential accounts");
        Check(human.Output.Contains("40% used") && human.Output.Contains("7 days") && human.Output.Contains("96% used") &&
            human.Output.Contains("Quota not reported") && human.Output.Contains("Quota lookup not supported"), "all named windows and unavailable accounts remain visible without invented quotas");

        var structured = await Run("usage", "--json");
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
            await CliApplication.WriteAccountUsage(output, overview, new(width, false));
            Check(output.ToString().Split('\n').All(line => HumanTable.Width(line.TrimEnd('\r')) <= width), $"named all-account overview fits {width} columns");
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
