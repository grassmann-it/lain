using System.Net;
using System.Net.Http.Json;
using Lain.Client;
using Lain.Cli;
using Lain.Core;

/// <summary>Public-output fixtures for private PTYs and redirected captures; no real host or credentials.</summary>
internal static class CliPresentationFixtures
{
    internal static readonly DateTimeOffset Observed = DateTimeOffset.Parse("2026-10-24T12:00:00Z");
    internal static readonly QuotaDto[] Quotas =
    [
        new("account-codex", null, "https://chatgpt.com/backend-api/wham/usage#primary_window", "ok", Observed, Observed,
            DateTimeOffset.Parse("2099-01-01T00:00:00Z"), "percent", 40, 100, 60, true, WindowLabel: "5 hours", UsedPercent: 40),
        new("account-codex", null, "https://chatgpt.com/backend-api/wham/usage#secondary_window", "ok", Observed, Observed,
            DateTimeOffset.Parse("2099-01-07T00:00:00Z"), "percent", 96, 100, 4, true, WindowLabel: "7 days", UsedPercent: 96),
        new("account-credits", "pool-credits", "https://api.commandcode.ai/alpha/billing/credits#fiveHour", "stale", Observed, Observed,
            DateTimeOffset.Parse("2020-01-01T00:00:00Z"), "USD-Credits", 18, 20, 2, false, WindowLabel: "5 hours", UsedPercent: 90),
        new("account-credits", null, "https://api.commandcode.ai/alpha/billing/credits#purchasedCredits", "ok", Observed, Observed,
            null, "USD-Credits", null, null, 12, false, WindowLabel: "Purchased credits"),
        new("account-credits", null, "https://api.commandcode.ai/alpha/billing/credits#freeCredits", "ok", Observed, Observed,
            null, "USD-Credits", null, null, 0, false, WindowLabel: "Free credits"),
        new("account-unknown", null, "https://api.kimi.com/coding/v1/usages#limit_5h", "error", Observed, null,
            null, "percent", null, null, null, false, Error: "Quota temporarily unavailable.", WindowLabel: "5 hours"),
        new("account-unsupported", null, "unavailable", "unsupported", Observed, null,
            null, "unknown", null, null, null, false, WindowLabel: "Quota API"),
        new("account-zero", null, "https://example.invalid/quota#fiveHour", "ok", Observed, Observed,
            null, "percent", 0, 100, 100, false, WindowLabel: "5 hours", UsedPercent: 0)
    ];

    internal static readonly StatusDto Status = new(1, "fixture", "ready", true, 4, 3, Observed);
    internal static readonly AccountDto[] Accounts =
    [
        new("account-codex", "Personal Codex", "codex", true, "codex-owned", "ready", SharedWithLegacy: true),
        new("account-credits", "Coding credits", "command-code", false, "api-key", "ready"),
        new("account-unknown", "Kimi coding", "kimi", true, "api-key", "ready"),
        new("account-unsupported", "Provider without quota API", "mimo", true, "api-key", "ready"),
        new("account-zero", "Unused account", "provider-fixture", true, "api-key", "ready"),
        new("account-missing", "Missing credential", "zai", true, "api-key", "missing")
    ];
    internal static readonly UsageTotals Totals = new(123, 128, 2, new(1200, 80, 20, null, null), 3);
    internal static readonly UsageSummaryDto Usage = new(1, Observed.AddDays(-1), Observed, "Europe/Berlin", Observed, 42, false, Totals,
        [new("2026-10-24", Totals)], [new("account-codex", Totals)], [new("vendor/model", Totals)]);

    internal static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)
    {
        var provider = new ProviderDescriptorDto("provider-fixture", "Fixture provider", "Synthetic display fixture", "https://example.invalid/account", "https://example.invalid/docs",
            [], [], [], [], [], [], AdapterState: "available", LiveVerification: "not_tested");
        var values = new Dictionary<string, object>
        {
            ["quotas"] = Quotas, ["status"] = Status, ["usage"] = Usage,
            ["providers"] = new ProviderCatalogDto(1, Observed, [provider, provider with { Id = "prepared-fixture", Name = "Prepared fixture", AdapterState = "prepared" }]),
            ["accounts"] = Accounts,
            ["profiles"] = new ProfileDto[] { new("profile-fixture", "Fixture profile", ["route-fixture"], "route-fixture") },
            ["models"] = new ModelCatalogDto(1, 4, [new("vendor/model", "provider-fixture", ["account-codex"])])
        };
        if (args.Length == 0 || (args[0] is not ("help" or "history") && !values.ContainsKey(args[0]))) throw new ArgumentException("Unknown rendering fixture.");
        using var http = new HttpClient(new Handler(values)) { BaseAddress = new Uri("http://fixture.invalid/") };
        return await CliApplication.RunAsync(args, output, error, () => new LainApiClient(http, "fixture-token"));
    }

    private sealed class Handler(Dictionary<string, object> values) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get || request.RequestUri?.Host != "fixture.invalid") throw new Exception("Unexpected rendering fixture request.");
            var key = request.RequestUri.AbsolutePath.Split('/').Last();
            var value = values[key];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(value, value.GetType()) });
        }
    }
}
