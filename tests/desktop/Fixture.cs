using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
        Enumerable.Range(1, InitialRoutes).Select(i => new RouteDto($"r{i}", $"gpt-lain-{i:00}", $"gpt-model-{i:00}", "openai", [$"a{(i - 1) % 4 + 1}"],
            ProviderRouteId: i == 1 ? "fixture-explicit-catalog-route" : null, Policy: i == 1 ? new("configured", ["r2"], true) : null)).ToArray(),
        Enumerable.Range(1, InitialProfiles).Select(i => new ProfileDto($"p{i}", i == 1 ? "Daily" : $"Workspace {i}", Enumerable.Range(1, i * 5).Select(n => $"r{n}").ToArray(), "r1", Policy: i == 1 ? new("configured", ["r2"], true) : null)).ToArray());
    public int Imports, EnableCalls, ApplyCalls, ExportCalls, LoginBegins, LoginPolls, LoginCancels, UsageCalls, StatusCalls, AnalysisCalls;
    public bool Offline, UsageFailure, NullTokens, IncompleteCoverage, AnalysisFailure, InspectorFailure;
    public int ProviderCalls, InspectorCalls, InspectorEventCalls, CostPolicyCalls;
    public bool InspectorRestarted, InspectorEmpty, InspectorGap, InspectorAdditionalEvent, QuotaAvailabilityChanged;
    public long InspectorHighWater => InspectorRestarted ? 1 : InspectorAdditionalEvent ? 14 : 13;
    public string? ActiveProfileId = "p2";
    public long? ActiveProfileRevision = 1;
    private readonly DateTimeOffset inspectorStarted = DateTimeOffset.UtcNow;
    public static readonly ProviderCatalogDto ProviderCatalog = LoadProviders();
    public readonly List<string> Requests = [];
    public readonly List<long> InspectorEventAfters = [];
    public readonly List<string> Unexpected = [];
    public readonly List<Dictionary<string, string>> UsageQueries = [];

    public void AddDensityAccounts(int total)
    {
        var extra = Enumerable.Range(InitialAccounts + 1, Math.Max(0, total - Config.Accounts.Length))
            .Select(i => new AccountDto($"density-{i}", $"Density account {i:00}", i % 2 == 0 ? "zai" : "codex", i % 6 != 0, "api-key", "ready"));
        Config = Config with { Revision = Config.Revision + 1, Accounts = Config.Accounts.Concat(extra).ToArray() };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = request.RequestUri!.AbsolutePath;
        Requests.Add($"{request.Method} {path}");
        if (request.RequestUri.Host != "fixture.invalid" || request.Headers.Authorization is not null)
            throw UnexpectedRequest(request);
        if (Offline) throw new HttpRequestException("offline fixture");
        if (AnalysisFailure && path == "/api/v1/usage/analysis")
            return new(HttpStatusCode.ServiceUnavailable) { Content = JsonContent.Create(new ApiError("fixture_analysis_unavailable", "Fixture analysis temporarily unavailable")) };
        if (InspectorFailure && path.StartsWith("/api/v1/inspector", StringComparison.Ordinal))
            return new(HttpStatusCode.ServiceUnavailable) { Content = JsonContent.Create(new ApiError("fixture_inspector_unavailable", "Fixture live source temporarily unavailable")) };
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
                "/api/v1/providers" => Providers(),
                var p when p.StartsWith("/api/v1/routes/") && p.EndsWith("/cost-policy") => CostPolicy(request.RequestUri!),
                "/api/v1/inspector" => Inspector(),
                "/api/v1/inspector/events" => InspectorEvents(request.RequestUri!),
                "/api/v1/usage" => Usage(request.RequestUri!),
                "/api/v1/usage/analysis" => Analysis(request.RequestUri!),
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
            Config = Config with { Revision = Config.Revision + 1, Routes = Config.Routes.Where(route => route.Id != body.Value.Id).Append(body.Value).ToArray() }; value = Config;
        }
        else if (request.Method == HttpMethod.Post && path.EndsWith("/apply"))
        {
            ApplyCalls++; Config = Config with { AppliedRevision = Config.Revision };
            ActiveProfileId = path.Split('/')[4]; ActiveProfileRevision = Config.Revision; value = Config;
        }
        else if (request.Method == HttpMethod.Post && path.EndsWith("/codex/export"))
        {
            ExportCalls++; value = new CodexProfileArtifacts("lain.config.toml", "# fixture configuration", "lain.models.json", "{}", ["--no-daemon", "--profile", "lain"], Config.Revision);
        }
        else if (request.Method == HttpMethod.Put && path.StartsWith("/api/v1/profiles/"))
        {
            var body = await request.Content!.ReadFromJsonAsync<RevisionRequest<ProfileDto>>(cancellationToken: ct);
            Revision(body!.Revision);
            Config = Config with { Revision = Config.Revision + 1, Profiles = Config.Profiles.Where(profile => profile.Id != body.Value.Id).Append(body.Value).ToArray() }; value = Config;
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


    private static ProviderCatalogDto LoadProviders()
    {
        using var json = typeof(Fixture).Assembly.GetManifestResourceStream("Lain.Desktop.Smoke.fixtures.inspection-providers.json")
            ?? throw new InvalidOperationException("Embedded provider UI fixture missing");
        return JsonSerializer.Deserialize<ProviderCatalogDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
    private RouteCostDecisionDto CostPolicy(Uri uri)
    {
        CostPolicyCalls++;
        var strict = uri.Query.Contains("free_only", StringComparison.Ordinal);
        return new(uri.AbsolutePath.Split('/')[4], !strict, strict ? "free_only" : "configured", "unknown",
            strict ? ["upstream_cost_controls_unavailable"] : []);
    }
    private ProviderCatalogDto Providers() { ProviderCalls++; return ProviderCatalog; }
    private string InspectorSource => InspectorRestarted ? "source-ui-2" : "source-ui-1";
    private static AttemptDimensionsDto ProvenDimensions(string? parent = null) => new(
        ConversationId: "conversation-ui", TurnId: "turn-ui", WorkerId: "worker-ui", ParentRequestId: parent,
        Evidence: [new("conversationId", "harness_header", "X-Conversation-Id"), new("turnId", "harness_header", "X-Turn-Id"),
            new("workerId", "request_body", "thread_spawn"), new("parentRequestId", "request_body", "parent_request_id")]);
    private InspectorSnapshotDto Inspector()
    {
        InspectorCalls++;
        var now = DateTimeOffset.UtcNow;
        InspectorStreamDto Stream(string request, string state, long first, long last, AttemptDimensionsDto? dimensions = null, int? status = null)
            => new("stream-" + request, "attempt", request, "attempt-" + request, inspectorStarted, now, state, first, last,
                AccountId: "a1", RouteId: "r1", Provider: "fixture-inference", Model: "fixture-inspector-model", Dimensions: dimensions,
                StatusCode: status, Measurements: state == "success" ? new(120, 2000, Source: "core_monotonic") : null);
        var streams = InspectorRestarted
            ? new[] { Stream("request-new", "streaming", 1, 1) }
            : new[] { Stream("request-a", "success", 1, 4, ProvenDimensions(), 200),
                Stream("request-b", "streaming", 5, 7, ProvenDimensions("request-a")),
                // Deliberately identical claimed conversation, but no evidence: it must remain independent.
                Stream("request-unknown", "active", 8, 8, new(ConversationId: "conversation-ui")),
                Stream("request-error", "failed", 9, 9, status: 503),
                Stream("request-conflict", "active", 10, 10, new(ParentRequestId: "request-unknown", Evidence: [new("parentRequestId", "request_body")])),
                Stream("request-conflict", "active", 11, 11, new(ParentRequestId: "request-error", Evidence: [new("parentRequestId", "request_body")])) with { Id = "stream-request-conflict-retry", AttemptId = "attempt-conflict-retry" } };
        return new(Api.Version, InspectorSource, now, "live", true, InspectorRestarted ? 1 : 11, 1, 0, new(), InspectorEmpty ? [] : streams);
    }
    private InspectorEventPageDto InspectorEvents(Uri uri)
    {
        InspectorEventCalls++;
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "");
        var after = long.TryParse(query.GetValueOrDefault("after"), out var parsed) ? parsed : 0;
        InspectorEventAfters.Add(after);
        var request = query.GetValueOrDefault("requestId");
        var changed = query.GetValueOrDefault("sourceId") != InspectorSource;
        InspectorEventDto E(long cursor, string id, string channel, string text, string? tool = null) => new(cursor, "output_delta", DateTimeOffset.UtcNow,
            id, "attempt-" + id, Dimensions: id is "request-a" or "request-b" ? ProvenDimensions(id == "request-b" ? "request-a" : null) : null,
            Channel: channel, Text: text, ToolCallId: tool is null ? null : "tool-ui", ToolName: tool, RedactionState: "none");
        var all = InspectorRestarted ? new[] { E(1, "request-new", "assistant", "New source output only.") }
            : new[] { E(1, "request-a", "reasoning", "Fixture reasoning summary."), E(2, "request-a", "assistant", "First answer "),
                E(3, "request-a", "assistant", "continued."), E(4, "request-a", "tool_call", "Tool call metadata only.", "fixture_tool"),
                E(5, "request-b", "tool_result", "Fixture tool result.", "fixture_tool"), E(6, "request-b", "assistant", "Answer after tool."),
                E(7, "request-b", "assistant", " Still streaming."), E(8, "request-unknown", "assistant", "Independent output."),
                E(9, "request-error", "assistant", "Retained partial output before failure."),
                // Events can advance between snapshot polls; Clear must never return to the older snapshot cursor.
                E(12, "request-b", "assistant", " Ahead-of-snapshot output."), E(13, "request-b", "assistant", " Still retained.") };
        if (!InspectorRestarted && InspectorAdditionalEvent) all = all.Append(E(14, "request-a", "assistant", "Follow-up event after presentation change.")).ToArray();
        var high = InspectorHighWater;
        return new(InspectorSource, all.Where(e => e.Cursor > (changed ? 0 : after) && e.RequestId == request).ToArray(), high, high, 1, 0,
            Truncated: InspectorGap || changed && after > 0, GapReason: InspectorGap ? "retention_limit" : changed && after > 0 ? "source_changed" : null);
    }

    // Concrete provider budgets exercise percentage, numeric balance, exhaustion and unsupported rendering.
    private QuotaDto[] Quotas()
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
            QuotaAvailabilityChanged ? Reading("a5", "fixture-quota-recovery", "Recovered quota", 10, 100, "%", 10)
                : new("a5", null, "unavailable", "unsupported", observed, null, null, "unknown", null, null, null, false,
                "Provider quota is not available.", "Provider quota")
        ];
    }

    private StatusDto Status()
    {
        StatusCalls++;
        return new(1, "test", "ready", true, Config.Revision, Config.AppliedRevision, DateTimeOffset.UtcNow, ActiveProfileId: ActiveProfileId, ActiveProfileRevision: ActiveProfileRevision);
    }
    private UsageSummaryDto Usage(Uri uri)
    {
        UsageCalls++;
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2)).ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "");
        UsageQueries.Add(query);
        var totals = new UsageTotals(128, 132, 4, NullTokens ? new(null, null, null, null, null) : new(72000, 9986, 18000, 2000, 81986), NullTokens ? 132 : 0);
        return new(1, DateTimeOffset.Parse(query["from"]), DateTimeOffset.Parse(query["to"]), query.GetValueOrDefault("timeZone", "UTC"), DateTimeOffset.UtcNow, 10, !IncompleteCoverage, totals,
            [new(DateTime.UtcNow.ToString("yyyy-MM-dd"), totals)], [new("a1", totals)], [new("gpt-model", totals)]);
    }
    private UsageAnalysisDto Analysis(Uri uri)
    {
        AnalysisCalls++;
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2)).ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "");
        var tokens = NullTokens ? new TokenCounts(null, null, null, null, null) : new(72000, 9986, 18000, 2000, 81986);
        var totals = new UsageTotals(128, 132, 4, tokens, NullTokens ? 132 : 0);
        var first = new MetricStatisticsDto(120, 132, 40, 260, 240, 510, 890);
        var duration = new MetricStatisticsDto(129, 132, 100, 2100, 2000, 2500, 3000);
        var rate = NullTokens ? new MetricStatisticsDto(0, 132, null, null, null, null, null)
            : new MetricStatisticsDto(100, 132, 1, 12, 10, 18, 20);
        var performance = new PerformanceSummaryDto(first, duration, rate);
        var dimension = query.GetValueOrDefault("groupBy", "modelFamily");
        var halfTokens = NullTokens ? new TokenCounts(null, null, null, null, null) : new(36000, 4993, 9000, 1000, 40993);
        var groupKey = dimension switch
        {
            "manufacturer" => "fixture-manufacturer", "routeId" => "r1", "inferenceProvider" => "fixture-inference",
            "gateway" => "fixture-gateway", "billingAccountId" or "accountId" => "a1", "model" => "gpt-model", "provider" => "codex",
            "reasoningEffort" => "high", "mode" => "agent", "conversationId" => "conversation-fixture", "turnId" => "turn-fixture",
            "workerId" => "worker-fixture", "harness" => "fixture-harness", "profileId" => "p1", _ => "fixture-family"
        };
        var knownRate = NullTokens ? new MetricStatisticsDto(0, 88, null, null, null, null, null) : new(66, 88, 1, 12, 10, 18, 20);
        var unknownRate = NullTokens ? new MetricStatisticsDto(0, 44, null, null, null, null, null) : new(34, 44, 1, 12, 10, 18, 20);
        var grouped = new[]
        {
            new UsageDimensionGroupDto(groupKey, new(85, 88, 3, halfTokens, NullTokens ? 88 : 0),
                new(new(80, 88, 40, 260, 240, 510, 890), new(86, 88, 100, 2100, 2000, 2500, 3000), knownRate)),
            new UsageDimensionGroupDto(null, new(43, 44, 1, halfTokens, NullTokens ? 44 : 0),
                new(new(40, 44, 40, 260, 240, 510, 890), new(43, 44, 100, 2100, 2000, 2500, 3000), unknownRate))
        };
        if (dimension is "accountId" or "model") grouped = [new(groupKey, totals, performance)];
        return new(Api.Version, DateTimeOffset.Parse(query["from"]), DateTimeOffset.Parse(query["to"]), query.GetValueOrDefault("timeZone", "UTC"),
            DateTimeOffset.UtcNow, 10, !IncompleteCoverage, totals, performance, [new(dimension, grouped)]);
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
