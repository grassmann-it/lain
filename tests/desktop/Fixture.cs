using System.Net;
using System.Net.Http.Json;
using Lain.Core;
using Lain.Client;

/// <summary>No sockets, disk credentials, provider calls, or real host mutations.</summary>
internal sealed class Fixture : HttpMessageHandler
{
    public const int InitialAccounts = 5;
    public const int InitialRoutes = 45;
    public const int InitialProfiles = 3;
    public ConfigurationDto Config = new(1, 1,
        [
            new("a1", "Personal Codex", "codex", true, "codex-access-snapshot", "ready", SharedWithLegacy: true),
            new("a2", "Command Code Pro", "command-code", true, "api-key", "ready"),
            new("a3", "Z.ai Coding", "zai", true, "api-key", "ready"),
            new("a4", "Kimi Coding", "kimi", true, "api-key", "ready"),
            new("a5", "MiMo API", "mimo", true, "api-key", "ready")
        ],
        Enumerable.Range(1, InitialRoutes).Select(i => new RouteDto($"r{i}", $"gpt-lain-{i:00}", $"gpt-model-{i:00}", "openai", [$"a{(i - 1) % 4 + 1}"])).ToArray(),
        Enumerable.Range(1, InitialProfiles).Select(i => new ProfileDto($"p{i}", i == 1 ? "Daily" : $"Workspace {i}", Enumerable.Range(1, i * 5).Select(n => $"r{n}").ToArray(), "r1")).ToArray());
    public int Imports, EnableCalls, ApplyCalls, ExportCalls, LoginBegins, LoginPolls, LoginCancels, UsageCalls, StatusCalls;
    public bool Offline, UsageFailure, NullTokens;
    public readonly List<string> Requests = [];
    public readonly List<string> Unexpected = [];
    public readonly List<Dictionary<string, string>> UsageQueries = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = request.RequestUri!.AbsolutePath;
        Requests.Add($"{request.Method} {path}");
        if (request.RequestUri.Host != "fixture.invalid" || request.Headers.Authorization is not null)
            throw UnexpectedRequest(request);
        if (Offline) throw new HttpRequestException("offline fixture");
        if (UsageFailure && path == "/api/v1/usage")
            return new(HttpStatusCode.ServiceUnavailable) { Content = JsonContent.Create(new ApiError("fixture_unavailable", "Fixture usage temporarily unavailable")) };
        object value;
        if (request.Method == HttpMethod.Get)
        {
            value = path switch
            {
                var p when p.StartsWith("/api/v1/accounts/login/") => CompleteLogin(p),
                "/api/v1/status" => Status(),
                "/api/v1/configuration" => Config,
                "/api/v1/accounts" => Config.Accounts,
                "/api/v1/models" => new ModelCatalogDto(1, Config.Revision, [new ModelEntryDto("gpt-model", "openai", ["a1"])]),
                "/api/v1/quotas" => Quotas(),
                "/api/v1/usage" => Usage(request.RequestUri!),
                "/api/v1/attempts" => new PageDto<AttemptDto>([new AttemptDto("attempt-fixture", "request-fixture", "a1", "r1", "openai", "gpt-model", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(-1).AddSeconds(2), 200, "completed", new TokenCounts(72000, 9986, 18000, 2000, 81986), 2000, 250)], 0, 25, 1),
                _ => throw UnexpectedRequest(request)
            };
        }
        else if (request.Method == HttpMethod.Post && path == "/api/v1/accounts/login")
        {
            LoginBegins++;
            value = new LoginDto("login" + LoginBegins, "codex", "waiting", "https://example.invalid/authorize?state=fixture");
        }
        else if (request.Method == HttpMethod.Delete && path.StartsWith("/api/v1/accounts/login/"))
        {
            LoginCancels++; value = new LoginDto("login" + LoginBegins, "codex", "cancelled");
        }
        else if (request.Method == HttpMethod.Put && path.StartsWith("/api/v1/routes/"))
        {
            var body = await request.Content!.ReadFromJsonAsync<RevisionRequest<RouteDto>>(cancellationToken: ct);
            Revision(body!.Revision);
            Config = Config with { Revision = Config.Revision + 1, Routes = Config.Routes.Append(body.Value).ToArray() }; value = Config;
        }
        else if (request.Method == HttpMethod.Post && path.EndsWith("/apply"))
        {
            ApplyCalls++; Config = Config with { AppliedRevision = Config.Revision }; value = Config;
        }
        else if (request.Method == HttpMethod.Post && path.EndsWith("/codex/export"))
        {
            ExportCalls++; value = new CodexProfileArtifacts("lain.config.toml", "# fixture configuration", "lain.models.json", "{}", ["--no-daemon", "--profile", "lain"], Config.Revision);
        }
        else if (request.Method == HttpMethod.Put && path.StartsWith("/api/v1/profiles/"))
        {
            var body = await request.Content!.ReadFromJsonAsync<RevisionRequest<ProfileDto>>(cancellationToken: ct);
            Revision(body!.Revision);
            Config = Config with { Revision = Config.Revision + 1, Profiles = Config.Profiles.Append(body.Value).ToArray() }; value = Config;
        }
        else if (request.Method == HttpMethod.Post && path == "/api/v1/import")
        {
            var body = await request.Content!.ReadFromJsonAsync<ImportRequest>(cancellationToken: ct);
            Revision(body!.Revision); Imports++; value = new ImportResult(Config.Revision, 0, 0, ["snapshot warning"]);
        }
        else if (request.Method == HttpMethod.Post && path == "/api/v1/accounts")
        {
            var body = await request.Content!.ReadFromJsonAsync<RevisionRequest<AddApiAccountRequest>>(cancellationToken: ct);
            Revision(body!.Revision);
            Config = Config with { Revision = Config.Revision + 1, Accounts = Config.Accounts.Append(new AccountDto("a-new", body.Value.Name, body.Value.Provider, true, "api-key", "ready")).ToArray() }; value = Config;
        }
        else if (request.Method == HttpMethod.Put && path.EndsWith("/enabled"))
        {
            var body = await request.Content!.ReadFromJsonAsync<RevisionRequest<bool>>(cancellationToken: ct);
            Revision(body!.Revision); EnableCalls++;
            var id = path.Split('/')[4];
            Config = Config with { Revision = Config.Revision + 1, Accounts = Config.Accounts.Select(a => a.Id == id ? a with { Enabled = body.Value } : a).ToArray() }; value = Config;
        }
        else throw UnexpectedRequest(request);
        return new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    // Concrete provider budgets exercise percentage, numeric balance, exhaustion and unsupported rendering.
    private static QuotaDto[] Quotas()
    {
        var observed = DateTimeOffset.Parse("2026-10-23T12:00:00Z");
        QuotaDto Reading(string id, string source, string label, double used, double limit, string unit, double percent, bool shared = false)
            => new(id, null, source, "ok", observed, observed,
                label.Contains("7 days", StringComparison.Ordinal) || label == "Weekly" ? observed.AddDays(7)
                    : label.StartsWith("Monthly", StringComparison.Ordinal) ? DateTimeOffset.Parse("2026-11-01T00:00:00Z") : observed.AddHours(5),
                unit, used, limit, limit - used, shared, WindowLabel: label, UsedPercent: percent);
        return [
            Reading("a1", "https://chatgpt.com/backend-api/wham/usage#primary_window", "7 days", 37, 100, "%", 37, true),
            Reading("a2", "https://api.commandcode.ai/alpha/billing/credits#fiveHour", "5 hours", 4, 20, "USD-Credits", 20),
            Reading("a2", "https://api.commandcode.ai/alpha/billing/credits#weekly", "Weekly", 22, 40, "USD-Credits", 55)
                with { State = "stale", ObservedAt = observed.AddMinutes(6), Error = "Quota refresh temporarily unavailable; previous observation retained." },
            Reading("a2", "https://api.commandcode.ai/alpha/billing/credits#monthlyCredits", "Monthly credits", 20, 80, "USD-Credits", 25),
            new("a2", null, "https://api.commandcode.ai/alpha/billing/credits#purchasedCredits", "ok", observed, observed, null,
                "USD-Credits", null, null, 12, false, WindowLabel: "Purchased credits"),
            new("a2", null, "https://api.commandcode.ai/alpha/billing/credits#freeCredits", "ok", observed, observed, null,
                "USD-Credits", null, null, 0, false, WindowLabel: "Free credits"),
            new("a2", null, "https://commandcode.ai/docs/plans/pro#model-monthly-remaining-unknown", "unknown", observed, null, null,
                "USD", null, null, null, false, WindowLabel: "Model allowance"),
            Reading("a3", "https://api.z.ai/api/monitor/usage/quota/limit#CREDIT_LIMIT/unit/3/number/5", "Credits · 5 hours", 250, 1000, "credits", 25),
            new("a3", null, "https://api.z.ai/api/monitor/usage/quota/limit#CREDIT_LIMIT/unit/6/number/1", "ok", observed, observed, observed.AddDays(7),
                "credits", null, null, null, false, WindowLabel: "Credits · 7 days", UsedPercent: 72),
            Reading("a4", "https://api.kimi.com/coding/v1/usages#limit_5h", "5 hours", 100, 100, "%", 100),
            // Some provider windows supply only a consumed percentage, with no absolute amounts.
            Reading("a4", "https://api.kimi.com/coding/v1/usages#limit_month_total", "Monthly total", 62, 100, "%", 62)
                with { Used = null, Limit = null, Remaining = null },
            Reading("a4", "https://api.kimi.com/coding/v1/usages#limit_month_code", "Monthly coding", 18, 100, "%", 18),
            new("a5", null, "unavailable", "unsupported", observed, null, null, "unknown", null, null, null, false,
                "Provider quota is not available.", "Provider quota")
        ];
    }

    private StatusDto Status()
    {
        StatusCalls++;
        return new(1, "test", "ready", true, Config.Revision, Config.AppliedRevision, DateTimeOffset.UtcNow);
    }
    private UsageSummaryDto Usage(Uri uri)
    {
        UsageCalls++;
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2)).ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "");
        UsageQueries.Add(query);
        var totals = new UsageTotals(128, 132, 4, NullTokens ? new(null, null, null, null, null) : new(72000, 9986, 18000, 2000, 81986), NullTokens ? 132 : 0);
        return new(1, DateTimeOffset.Parse(query["from"]), DateTimeOffset.Parse(query["to"]), query.GetValueOrDefault("timeZone", "UTC"), DateTimeOffset.UtcNow, 10, true, totals,
            [new(DateTime.UtcNow.ToString("yyyy-MM-dd"), totals)], [new("a1", totals)], [new("gpt-model", totals)]);
    }
    private LoginDto CompleteLogin(string path)
    {
        LoginPolls++; Config = Config with { Revision = Config.Revision + 1 };
        return new(path.Split('/').Last(), "codex", "completed");
    }
    private void Revision(long revision)
    {
        if (revision != Config.Revision) throw new InvalidOperationException($"Fixture revision mismatch: {revision} != {Config.Revision}");
    }
    private Exception UnexpectedRequest(HttpRequestMessage request)
    {
        var message = $"Unexpected fixture request: {request.Method} {request.RequestUri!.AbsolutePath}";
        Unexpected.Add(message); return new InvalidOperationException(message);
    }
}
