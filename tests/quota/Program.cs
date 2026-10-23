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
        Check(blockedRows.Single(r => r.AccountId == "unsupported").Remaining is null && noRequests.Urls.Count == 0, "unknown provider never probed");
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
        Console.WriteLine($"Quota: {assertions} assertions passed (synthetic only).");
    }
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
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Urls) Urls.Add(request.RequestUri!.ToString());
        var body = AsyncResponse is not null ? await AsyncResponse(cancellationToken) : Response(request);
        return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
