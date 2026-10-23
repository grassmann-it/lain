using System.Net;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Quota;

static class Test
{
    private static int assertions;
    private static void Check(bool condition, string label) { if (!condition) throw new Exception(label); assertions++; }
    public static async Task Main()
    {
        var account = new AccountDto("a", "a", "codex", true, "codex-access-snapshot", "ready", SharedWithLegacy: true);
        var source = new Accounts([account, account with { Id = "b", SharedWithLegacy = false }]);
        var clock = new Clock();
        var transport = new FakeHttp(request =>
        {
            Check(request.Method == HttpMethod.Get, "read only GET");
            Check(request.RequestUri!.ToString() == CodexQuotaAdapter.Endpoint, "fixed quota endpoint");
            var token = request.Headers.Authorization?.Parameter;
            Check(request.Headers.GetValues("ChatGPT-Account-Id").Single() == "provider-" + token, "per account header");
            return token == "a" ? """{"rate_limit":{"primary_window":{"used_percent":20,"reset_at":1800000000},"secondary_window":{"used_percent":null}}}"""
                : """{"rate_limit":{"primary_window":{"used_percent":65}}}""";
        });
        using var client = new HttpClient(transport);
        var service = new QuotaService(source, new Credentials(), [new CodexQuotaAdapter(client)], clock);
        var rows = await service.GetSnapshotsAsync();
        Check(rows.All(r => r.State == "unknown" && r.Remaining is null && r.LastSuccessAt is null), "unknown before refresh");
        rows = await service.RefreshAsync();
        Check(rows.Single(r => r.AccountId == "a" && r.Source.EndsWith("primary_window")).Remaining == 80, "Codex percentage");
        Check(rows.Single(r => r.AccountId == "b").Remaining == 35, "accounts isolated");
        Check(rows.Single(r => r.AccountId == "a" && r.Source.EndsWith("secondary_window")).State == "unknown", "null window unknown");
        Check(rows.Where(r => r.AccountId == "a").All(r => r.SharedWithLegacy && r.BudgetPoolId is null), "legacy flag; no inferred shared provider pool");
        var success = rows.First().LastSuccessAt;
        clock.Advance(TimeSpan.FromMinutes(6));
        Check((await service.GetSnapshotsAsync()).First().State == "stale", "five minute stale");
        transport.Response = request => request.Headers.Authorization!.Parameter == "a" ? throw new HttpRequestException("secret bearer a") : """{"rate_limit":{"primary_window":{"used_percent":66}}}""";
        rows = await service.RefreshAsync();
        var stale = rows.First(r => r.AccountId == "a");
        Check(stale.State == "stale" && stale.Remaining == 80 && stale.LastSuccessAt == success && stale.ObservedAt > success, "failure retains last success");
        Check(rows.Single(r => r.AccountId == "b").State == "ok", "one failure does not hide others");
        Check(!JsonSerializer.Serialize(rows).Contains("secret bearer"), "sanitized failures");
        using var cancel = new CancellationTokenSource();
        var entered = new TaskCompletionSource();
        transport.AsyncResponse = async ct => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); return "{}"; };
        var refresh = service.RefreshAsync("a", cancel.Token);
        await entered.Task;
        Check((await service.GetSnapshotsAsync()).First().State == "refreshing", "in progress visible");
        cancel.Cancel();
        try { await refresh; throw new Exception("Cancellation expected"); } catch (OperationCanceledException) { }
        Check((await service.GetSnapshotsAsync()).First().State == "stale", "cancellation restores snapshot");

        var commandTransport = new FakeHttp(request => request.RequestUri!.AbsolutePath switch
        {
            "/alpha/whoami" => """{"org":{"id":"org-1"},"orgLimits":[{"scope":"organization","limit":100,"spent":25}]}""",
            "/alpha/billing/credits" => """{"credits":{"monthlyCredits":60,"purchasedCredits":0},"windowLimits":{"fiveHour":{"cap":20,"used":4,"resetAt":0}}}""",
            "/alpha/billing/subscriptions" => """{"data":{"planId":"individual-pro-v1","status":"active","currentPeriodEnd":"2026-11-01T00:00:00Z"}}""",
            _ => throw new Exception("Unexpected endpoint")
        });
        using var commandClient = new HttpClient(commandTransport);
        var command = new CommandCodeQuotaAdapter(commandClient);
        var cmdAccount = account with { Provider = "commandcode", CredentialKind = "api-key", BudgetPoolId = "account-pool" };
        var result = await command.FetchAsync(cmdAccount, new("cmd-secret"));
        var monthly = result.Readings.Single(r => r.Source.EndsWith("monthlyCredits"));
        Check(monthly.Remaining == 60 && monthly.Limit == 80 && monthly.Used == 20, "Command account table separate from model caps");
        Check(result.Readings.Single(r => r.Source.EndsWith("fiveHour")).ResetsAt is null, "inactive rolling reset zero unknown");
        Check(result.Readings.Single(r => r.Source.EndsWith("purchasedCredits")).Remaining == 0, "zero distinct from unknown");
        Check(result.Readings.Single(r => r.Source.Contains("organization_0")).BudgetPoolId == "commandcode:org:org-1", "proven organization pool");
        Check(result.Readings.Single(r => r.Source.Contains("model-monthly")).Remaining is null, "monthly model budget unknown");
        Check(commandTransport.Urls.All(u => !u.Contains("usage/summary") && !u.Contains("oauth")), "no inference or refresh endpoints");
        commandTransport.Response = request => request.RequestUri!.AbsolutePath == "/alpha/billing/credits" ? throw new HttpRequestException("raw secret") : "{}";
        var partial = await command.FetchAsync(cmdAccount, new("cmd-secret"));
        Check(partial.Error == "billing/credits unavailable.", "partial failure sanitized");
        var secret = QuotaCredential.FromSecretJson("""{"kind":"codex-access-snapshot","accessToken":"private","accountId":"provider-a","refreshToken":"never-used"}""");
        Check(secret?.BearerToken == "private" && secret.ProviderAccountId == "provider-a" && !secret.ToString().Contains("private"), "normalized secret parse and redaction");
        var ownedSecret = QuotaCredential.FromSecretJson("""{"kind":"codex-owned","accessToken":"owned-access","accountId":"provider-owned","expiresAt":"2026-10-29T06:55:59Z","refreshToken":"must-not-be-used","idToken":"must-not-be-used"}""");
        Check(ownedSecret?.BearerToken == "owned-access" && ownedSecret.ProviderAccountId == "provider-owned" && ownedSecret.ExpiresAt == DateTimeOffset.Parse("2026-10-29T06:55:59Z"), "owned normalized token identity and expiry");
        var ownedHttp = new FakeHttp(request =>
        {
            Check(request.Method == HttpMethod.Get && request.RequestUri!.ToString() == CodexQuotaAdapter.Endpoint, "owned quota only GET usage; never refresh");
            Check(request.Headers.Authorization?.Parameter == "owned-access" && request.Headers.GetValues("ChatGPT-Account-Id").Single() == "provider-owned", "owned current access token and account header");
            return """{"rate_limit":{"primary_window":{"used_percent":9}}}""";
        });
        using var ownedClient = new HttpClient(ownedHttp);
        var ownedResult = await new CodexQuotaAdapter(ownedClient).FetchAsync(account with { CredentialKind = "codex-owned" }, ownedSecret!);
        Check(ownedResult.Readings.Single().Remaining == 91 && ownedHttp.Urls.Count == 1, "owned quota supports same percentage semantics without refresh");
        Check(QuotaCredential.FromSecretJson("""{"kind":"codex-owned","refreshToken":"not-an-access-token"}""") is null, "refresh token never substitutes for owned access token");
        var noRequests = new FakeHttp(_ => throw new Exception("Must not call provider"));
        using var noClient = new HttpClient(noRequests);
        var blocked = new QuotaService(new Accounts([account with { Enabled = false }, account with { Id = "expired" }, account with { Id = "unsupported", Provider = "other" }]),
            new ExpiredCredentials(), [new CodexQuotaAdapter(noClient)], clock);
        var blockedRows = await blocked.RefreshAsync();
        Check(blockedRows.Single(r => r.AccountId == "a").State == "disabled", "disabled account not refreshed");
        Check(blockedRows.Single(r => r.AccountId == "expired").Error!.Contains("expired"), "expired snapshot never refreshed");
        Check(blockedRows.Single(r => r.AccountId == "unsupported").State == "unsupported" && blockedRows.Single(r => r.AccountId == "unsupported").Remaining is null && noRequests.Urls.Count == 0, "unsupported provider is explicit and never probed");
        var partialService = new QuotaService(new Accounts([cmdAccount]), new Credentials(), [command], clock);
        commandTransport.Response = request => request.RequestUri!.AbsolutePath switch
        {
            "/alpha/whoami" => "{}",
            "/alpha/billing/credits" => """{"credits":{"monthlyCredits":12}}""",
            "/alpha/billing/subscriptions" => "{}",
            _ => "{}"
        };
        var previousMonthly = (await partialService.RefreshAsync()).Single(r => r.Source.EndsWith("monthlyCredits"));
        clock.Advance(TimeSpan.FromSeconds(1));
        commandTransport.Response = request => request.RequestUri!.AbsolutePath switch
        {
            "/alpha/billing/credits" => """{"windowLimits":{"weekly":{"cap":4,"used":2}}}""",
            "/alpha/billing/subscriptions" => throw new HttpRequestException("private response"),
            _ => "{}"
        };
        var partialRows = await partialService.RefreshAsync();
        var oldMonthly = partialRows.Single(r => r.Source.EndsWith("monthlyCredits"));
        Check(oldMonthly.State == "stale" && oldMonthly.Remaining == 12 && oldMonthly.LastSuccessAt == previousMonthly.LastSuccessAt, "partial response retains old missing window");
        Check(partialRows.Single(r => r.Source.EndsWith("weekly")).State == "ok", "partial response exposes fresh successful window");
        await ProviderQuotaTests(account, clock);
        Console.WriteLine($"Quota: {assertions} assertions passed (synthetic only).");
    }

    private static IReadOnlyList<QuotaReading> Normalize(string json, Func<JsonElement, IReadOnlyList<QuotaReading>> parse)
    {
        using var document = JsonDocument.Parse(json);
        return parse(document.RootElement);
    }

    private static async Task Rejected(Func<Task<QuotaFetchResult>> action, string label)
    {
        var failed = false;
        try { await action(); } catch (Exception) { failed = true; }
        Check(failed, label);
    }

    private static async Task ProviderQuotaTests(AccountDto template, Clock clock)
    {
        const string zaiJson = """{"code":200,"success":true,"data":{"limits":[{"type":"CREDIT_LIMIT","usage":1000,"currentValue":250,"remaining":750,"percentage":25,"nextResetTime":1800000000000},{"type":"TIME_LIMIT","usage":100,"currentValue":0,"remaining":100,"percentage":0},{"type":"TOKENS_LIMIT","usage":0,"currentValue":0,"remaining":0,"percentage":null}]}}""";
        var zaiRows = Normalize(zaiJson, ZaiQuotaAdapter.Normalize);
        var credit = zaiRows.Single(r => r.Unit == "credits");
        Check(credit.Used == 250 && credit.Limit == 1000 && credit.Remaining == 750 && credit.UsedPercent == 25, "Z.ai credits preserve numeric balance and consumed percentage");
        Check(credit.ResetsAt == DateTimeOffset.FromUnixTimeMilliseconds(1800000000000), "Z.ai millisecond reset parsed");
        Check(zaiRows.Single(r => r.Unit == "requests").UsedPercent == 0, "Z.ai measured zero percent is known");
        var zero = zaiRows.Single(r => r.Unit == "tokens");
        Check(zero.Used == 0 && zero.Limit == 0 && zero.Remaining == 0 && zero.UsedPercent is null, "Z.ai zero counts retained without inventing a percentage");
        var missing = Normalize("""{"data":{"limits":[{"type":"CREDIT_LIMIT","currentValue":null,"usage":"not-a-number","remaining":null,"percentage":null}]}}""", ZaiQuotaAdapter.Normalize).Single();
        Check(missing.Used is null && missing.Limit is null && missing.Remaining is null && missing.UsedPercent is null, "Z.ai malformed or null measurements stay unknown");
        var invalid = Normalize("""{"data":{"limits":[{"type":"TIME_LIMIT","currentValue":-1,"usage":"NaN","remaining":"Infinity","percentage":101}]}}""", ZaiQuotaAdapter.Normalize).Single();
        Check(invalid.Used is null && invalid.Limit is null && invalid.Remaining is null && invalid.UsedPercent is null, "Z.ai rejects negative nonfinite and out-of-range measurements");
        var percentageOnly = Normalize("""{"data":{"limits":[{"type":"CREDIT_LIMIT","percentage":42}]}}""", ZaiQuotaAdapter.Normalize).Single();
        Check(percentageOnly.Used is null && percentageOnly.Limit is null && percentageOnly.Remaining is null && percentageOnly.UsedPercent == 42, "Z.ai provider percentage never invents absent numeric counts");
        Check(Normalize("""{"data":{"limits":[{"type":"UNRECOGNIZED","usage":12,"currentValue":3}]}}""", ZaiQuotaAdapter.Normalize).Count == 0, "Z.ai unknown enum never becomes a fabricated quota");
        var reordered = Normalize("""{"data":{"limits":[{"type":"TOKENS_LIMIT","usage":0,"currentValue":0,"remaining":0},{"type":"CREDIT_LIMIT","usage":1000,"currentValue":250,"remaining":750,"percentage":25}]}}""", ZaiQuotaAdapter.Normalize);
        Check(reordered.Single(r => r.Unit == "credits").Source == credit.Source, "Z.ai window source stable under reordering");

        const string kimiJson = """{"usages":{"limit_5h":{"used_ratio":1,"reset_time":"2026-11-01T00:00:00Z"},"limit_7d":{"used_ratio":0},"limit_month_total":{"used_ratio":0.62},"limit_month_code":{"used_ratio":null}}}""";
        var kimiRows = Normalize(kimiJson, KimiQuotaAdapter.Normalize);
        var five = kimiRows.Single(r => r.Source.EndsWith("#limit_5h"));
        Check(five.Used == 100 && five.Limit == 100 && five.Remaining == 0 && five.UsedPercent == 100, "Kimi ratio one means fully consumed, zero remaining");
        Check(five.ResetsAt == DateTimeOffset.Parse("2026-11-01T00:00:00Z"), "Kimi reset timestamp preserved");
        Check(kimiRows.Single(r => r.Source.EndsWith("#limit_7d")).UsedPercent == 0, "Kimi ratio zero is a known unused window");
        Check(kimiRows.Single(r => r.Source.EndsWith("#limit_month_total")).UsedPercent == 62, "Kimi ratio converted to percent");
        var unknown = kimiRows.Single(r => r.Source.EndsWith("#limit_month_code"));
        Check(unknown.Used is null && unknown.Limit is null && unknown.Remaining is null && unknown.UsedPercent is null, "Kimi null ratio stays unknown including limit");
        var badRatios = Normalize("""{"usages":{"limit_5h":{"used_ratio":-0.1},"limit_7d":{"used_ratio":1.1},"limit_month_total":{"used_ratio":"NaN"},"limit_month_code":{"used_ratio":"Infinity"}}}""", KimiQuotaAdapter.Normalize);
        Check(badRatios.All(r => r.UsedPercent is null && r.Used is null && r.Remaining is null), "Kimi invalid ratios never produce a progress percentage");
        var kimiReordered = Normalize("""{"usages":{"month_total":{"used_ratio":0.62},"limit_5h":{"used_ratio":1}}}""", KimiQuotaAdapter.Normalize);
        Check(kimiReordered.Single(r => r.Source.EndsWith("#limit_month_total")).Source == kimiRows.Single(r => r.Source.EndsWith("#limit_month_total")).Source, "Kimi aliases and reordering preserve source identity");
        Check(Normalize("""{"usages":{},"limits":[{"detail":{"limit":100,"used":25,"remaining":75}}]}""", KimiQuotaAdapter.Normalize).Count == 0, "Kimi modern empty payload never falls back to conflicting legacy counts");
        var legacy = Normalize("""{"limits":[{"detail":{"limit":100,"used":25,"remaining":75,"resetTime":1800000000000}}]}""", KimiQuotaAdapter.Normalize).Single();
        Check(legacy.Limit == 100 && legacy.Used == 25 && legacy.Remaining == 75 && legacy.UsedPercent == 25, "Kimi legacy explicit counts retained with justified percent");

        var zaiAccount = template with { Provider = "zai", CredentialKind = "api-key" };
        var kimiAccount = template with { Provider = "kimi", CredentialKind = "api-key" };
        var zaiHttp = new FakeHttp(request =>
        {
            Check(request.Method == HttpMethod.Get && request.RequestUri!.ToString() == ZaiQuotaAdapter.Endpoint, "Z.ai uses one fixed read-only quota endpoint");
            Check(request.Headers.GetValues("Authorization").Single() == "zai-private-key", "Z.ai authorization is raw API key");
            Check(!request.Headers.Contains("ChatGPT-Account-Id"), "Z.ai receives no Codex account identity header");
            return zaiJson;
        });
        var kimiHttp = new FakeHttp(request =>
        {
            Check(request.Method == HttpMethod.Get && request.RequestUri!.Host is "api.kimi.com" or "api.kimi.ai" && request.RequestUri.AbsolutePath == "/coding/v1/usages", "Kimi uses read-only official coding quota endpoint");
            Check(request.Headers.Authorization?.Scheme == "Bearer" && request.Headers.Authorization.Parameter == "kimi-private-key", "Kimi authorization is Bearer API key");
            Check(!request.Headers.Contains("ChatGPT-Account-Id"), "Kimi receives no Codex identity header");
            return kimiJson;
        });
        using var zaiClient = new HttpClient(zaiHttp);
        using var kimiClient = new HttpClient(kimiHttp);
        var zai = new ZaiQuotaAdapter(zaiClient);
        var kimi = new KimiQuotaAdapter(kimiClient);
        var zaiCredential = new QuotaCredential("zai-private-key", baseUrl: "https://api.z.ai/api/coding/paas/v4");
        var kimiCredential = new QuotaCredential("kimi-private-key", baseUrl: "https://api.kimi.com/coding/v1");
        Check((await zai.FetchAsync(zaiAccount, zaiCredential)).Readings.Any(r => r.Remaining == 750), "Z.ai end-to-end fake HTTP yields balance");
        Check((await kimi.FetchAsync(kimiAccount, kimiCredential)).Readings.Any(r => r.UsedPercent == 100), "Kimi end-to-end fake HTTP yields exhausted window");
        foreach (var url in new string?[] { null, "https://evil.invalid/v1", "http://api.z.ai/api/v1", "https://api.z.ai.evil.invalid/api/v1", "https://api.z.ai/api/v1?redirect=evil", "https://user@api.z.ai/api/v1", "https://api.z.ai:444/api/v1" })
            await Rejected(() => zai.FetchAsync(zaiAccount, new("must-not-leak", baseUrl: url)), "Z.ai rejects unqualified credential origin before request");
        foreach (var url in new string?[] { null, "https://evil.invalid/coding/v1", "http://api.kimi.com/coding/v1", "https://api.kimi.com.evil.invalid/coding/v1", "https://api.kimi.com/v1", "https://api.kimi.com/coding/v1?redirect=evil", "https://user@api.kimi.com/coding/v1", "https://api.kimi.com:444/coding/v1" })
            await Rejected(() => kimi.FetchAsync(kimiAccount, new("must-not-leak", baseUrl: url)), "Kimi rejects unqualified credential origin before request");
        Check(zaiHttp.Urls.Count == 1 && kimiHttp.Urls.Count == 1, "rejected origins make zero authenticated HTTP requests");
        await Rejected(() => zai.FetchAsync(zaiAccount with { CredentialKind = "codex-owned" }, zaiCredential), "Z.ai never sends OAuth credentials");
        await Rejected(() => kimi.FetchAsync(kimiAccount with { CredentialKind = "codex-owned" }, kimiCredential), "Kimi never sends OAuth credentials");
        Check(zaiHttp.Urls.Count == 1 && kimiHttp.Urls.Count == 1, "wrong credential kinds make zero HTTP requests");
        Check((await kimi.FetchAsync(kimiAccount, new("kimi-private-key", baseUrl: "https://api.kimi.ai/coding/v1/"))).Readings.Single(r => r.Source.EndsWith("#limit_5h")).Source == five.Source, "Kimi official alternate host retains canonical window identity");
        var parsedApiKey = QuotaCredential.FromSecretJson("""{"kind":"api-key","apiKey":"private","baseUrl":"https://api.kimi.com/coding/v1"}""");
        Check(parsedApiKey?.BaseUrl == "https://api.kimi.com/coding/v1" && !parsedApiKey.ToString().Contains("private"), "API key origin preserved while credential string stays redacted");

        var service = new QuotaService(new Accounts([zaiAccount, kimiAccount with { Id = "kimi" }]), new ProviderCredentials(), [zai, kimi], clock);
        var before = await service.RefreshAsync();
        var beforeZai = before.Single(r => r.AccountId == "a" && r.Unit == "credits");
        var beforeKimi = before.Single(r => r.AccountId == "kimi" && r.Source.EndsWith("#limit_5h"));
        Check(beforeZai.UsedPercent == 25 && beforeKimi.UsedPercent == 100 && before.All(r => !string.IsNullOrEmpty(r.WindowLabel)), "service transports semantic window labels and consumed percentages");
        clock.Advance(TimeSpan.FromMinutes(1));
        zaiHttp.Response = _ => """{"success":false,"message":"zai-private-key","data":{"limits":[{"type":"CREDIT_LIMIT","remaining":999999}]}}""";
        kimiHttp.Response = _ => "{invalid-json-kimi-private-key";
        var failed = await service.RefreshAsync();
        var staleZai = failed.Single(r => r.AccountId == "a" && r.Unit == "credits");
        var staleKimi = failed.Single(r => r.AccountId == "kimi" && r.Source.EndsWith("#limit_5h"));
        Check(staleZai.State == "stale" && staleZai.Remaining == 750 && staleZai.LastSuccessAt == beforeZai.LastSuccessAt, "Z.ai rejected envelope retains last good numeric balance");
        Check(staleKimi.State == "stale" && staleKimi.Remaining == 0 && staleKimi.UsedPercent == 100 && staleKimi.LastSuccessAt == beforeKimi.LastSuccessAt, "Kimi malformed payload retains last good exhausted window");
        var serialized = JsonSerializer.Serialize(failed);
        Check(!serialized.Contains("zai-private-key") && !serialized.Contains("kimi-private-key") && !serialized.Contains("999999"), "provider failures never expose credentials or trust rejected envelope quantities");
        zaiHttp.Response = _ => "{}";
        kimiHttp.Response = _ => "{}";
        var empty = await service.RefreshAsync();
        Check(empty.Where(r => r.LastSuccessAt.HasValue).All(r => r.State == "stale"), "empty provider payloads preserve last good values as stale");
        zaiHttp.Response = _ => "zai-private-key";
        kimiHttp.Response = _ => "kimi-private-key";
        zaiHttp.Status = HttpStatusCode.Unauthorized;
        kimiHttp.Status = HttpStatusCode.TooManyRequests;
        var denied = await service.RefreshAsync();
        Check(denied.Where(r => r.LastSuccessAt.HasValue).All(r => r.State == "stale"), "HTTP authentication and throttling failures retain last good windows");
        Check(!JsonSerializer.Serialize(denied).Contains("private-key"), "HTTP failure bodies never leak into quota diagnostics");
    }
}

sealed class ProviderCredentials : IQuotaCredentialSource
{
    public Task<QuotaCredential?> GetCredentialAsync(string accountId, CancellationToken ct = default)
        => Task.FromResult<QuotaCredential?>(accountId == "kimi"
            ? new("kimi-private-key", baseUrl: "https://api.kimi.com/coding/v1")
            : new("zai-private-key", baseUrl: "https://api.z.ai/api/coding/paas/v4"));
}

sealed class Accounts(AccountDto[] accounts) : IQuotaAccountSource
{
    public Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AccountDto>>(accounts);
}
sealed class Credentials : IQuotaCredentialSource
{
    public Task<QuotaCredential?> GetCredentialAsync(string accountId, CancellationToken ct = default) => Task.FromResult<QuotaCredential?>(new(accountId, "provider-" + accountId));
}
sealed class ExpiredCredentials : IQuotaCredentialSource
{
    public Task<QuotaCredential?> GetCredentialAsync(string accountId, CancellationToken ct = default) => Task.FromResult<QuotaCredential?>(new("expired", expiresAt: DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
}
sealed class Clock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.Parse("2026-10-23T12:00:00Z");
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan amount) => now += amount;
}
sealed class FakeHttp(Func<HttpRequestMessage, string> response) : HttpMessageHandler
{
    public Func<HttpRequestMessage, string> Response = response;
    public Func<CancellationToken, Task<string>>? AsyncResponse;
    public List<string> Urls = [];
    public HttpStatusCode Status = HttpStatusCode.OK;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Urls) Urls.Add(request.RequestUri!.ToString());
        var body = AsyncResponse is not null ? await AsyncResponse(cancellationToken) : Response(request);
        return new(Status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
