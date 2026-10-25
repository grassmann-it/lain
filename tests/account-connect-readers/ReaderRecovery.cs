using System.Net;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Quota;

static class ReaderRecovery
{
    public static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
        foreach (var provider in new[] { "kilo", "requesty", "synthetic", "stepfun", "commandcode", "zai", "kimi", "openrouter" })
        {
            var calls = 0;
            using var client = new HttpClient(new RecoveryHttp((request, _) =>
            {
                calls++;
                return Task.FromResult(Reply(Body(provider, request)));
            }));
            var adapter = Adapter(provider, client);
            var account = new AccountDto("existing-id", "Fixture", provider, true, "external-snapshot", "connected");
            var credential = new QuotaCredential("fixture-key", baseUrl: Base(provider), credentialKind: "api-key");
            var result = await adapter.FetchAsync(account, credential);
            Check(calls > 0 && result.Readings.Count > 0 && result.Error is null,
                provider + " reads selected API-key generation despite legacy account kind");
            var before = calls;
            try
            {
                await adapter.FetchAsync(account with { CredentialKind = "api-key" },
                    new("fixture-key", baseUrl: Base(provider), credentialKind: "access-token-snapshot"));
                throw new Exception(provider + " accepted an incompatible selected credential kind");
            }
            catch (Exception ex) when (ex.Message == RejectedKind(provider)) { Check(calls == before && !ex.Message.Contains("fixture-key"), provider + " cannot fall back from selected incompatible kind to account label"); }
        }

        using (var aliasClient = new HttpClient(new RecoveryHttp((request, _) => Task.FromResult(Reply(Body("kilo", request))))))
        {
            var result = await new KiloQuotaAdapter(aliasClient).FetchAsync(
                new("existing-kilo", "Fixture", "kilo", true, "api-key", "connected"),
                new("fixture-key", baseUrl: "https://api.kilo.ai/api/gateway/v1"));
            Check(result.Readings.Single().Remaining == 8 && result.KiloPass is { Status: "active" },
                "Manufacturer-qualified legacy Kilo gateway URL reuses same personal key for wallet and Pass");
        }

        var monitorCalls = 0;
        using (var monitorClient = new HttpClient(new RecoveryHttp((request, _) =>
        {
            monitorCalls++;
            Check(request.Headers.Authorization?.Parameter == "independent-monitor-key"
                && request.RequestUri!.AbsoluteUri == "https://api.fireworks.ai/v1/accounts/demo/quotas?pageSize=200",
                "Fireworks independent monitor uses only selected token and fixed bound quota endpoint");
            return Task.FromResult(Reply("{\"quotas\":[{\"name\":\"accounts/demo/quotas/monthly-spend-usd\",\"value\":\"50\",\"usage\":2}]}"));
        })))
        {
            var adapter = new FireworksQuotaAdapter(monitorClient);
            var account = new AccountDto("existing-monitor", "Fixture", "fireworks", true, "monitor", "connected",
                Binding: new("fireworks", ResourceType: "account", ResourceId: "demo"));
            var monitor = new QuotaMonitorCredential("api-token", "independent-monitor-key", "demo");
            var result = await adapter.FetchAsync(account, new("", monitor: monitor, credentialKind: "api-token"));
            Check(result.Readings.Single() is { Used: 2, Limit: 50, Remaining: 48, ScopeId: "demo" },
                "Fireworks monitor-only projection needs no inference bearer or inference URL and keeps rows");
            var before = monitorCalls;
            try
            {
                await adapter.FetchAsync(account with { Binding = account.Binding! with { ResourceId = "other" } },
                    new("", monitor: monitor, credentialKind: "api-token"));
                throw new Exception("Expected monitor binding rejection");
            }
            catch (Exception ex) when (ex.Message == "Fireworks quota does not match the bound account.") { Check(monitorCalls == before && !ex.Message.Contains("independent-monitor-key"), "Fireworks separate monitor cannot read another bound account"); }
            var missing = await adapter.FetchAsync(account, new("unused-inference", monitor: new("api-token", null, "demo")));
            Check(missing.Support?.Kind == "setup-required" && monitorCalls == before,
                "Missing independent monitor token does not fall back to inference access");
        }

        foreach (var provider in new[] { "kilo", "commandcode", "zai", "openrouter" })
        {
            var phase = "success";
            var clock = new RecoveryClock();
            using var client = new HttpClient(new RecoveryHttp((request, _) => phase switch
            {
                "authentication" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("private-fixture-body") }),
                "permission" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("private-fixture-body") }),
                "rate-limit" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("private-fixture-body") }),
                "timeout" => throw new TaskCanceledException("private-fixture-timeout"),
                "invalid" => Task.FromResult(Reply("{}")),
                _ => Task.FromResult(Reply(Body(provider, request)))
            }));
            var account = new AccountDto("preserved-" + provider, "Fixture", provider, true, "api-key", "connected");
            var credential = new QuotaCredential("fixture-key", baseUrl: Base(provider), credentialKind: "api-key");
            var service = new QuotaService(new RecoveryAccounts(account), new RecoveryCredentials(credential), [Adapter(provider, client)], clock);
            var first = (await service.RefreshAsync()).First(r => r.LastSuccessAt.HasValue && r.Unit != "unknown");
            foreach (var failure in new[] { "authentication", "permission", "rate-limit", "timeout", "invalid" })
            {
                clock.Advance(); phase = failure;
                var stale = (await service.RefreshAsync()).Single(r => r.Source == first.Source);
                Check(stale.State == "stale" && stale.LastSuccessAt == first.LastSuccessAt && stale.ObservedAt == first.ObservedAt
                    && stale.Used == first.Used && stale.Remaining == first.Remaining,
                    provider + " " + failure + " preserves actual values and original freshness on existing ID");
                Check(stale.AccountId == account.Id && !JsonSerializer.Serialize(stale).Contains("private-fixture"),
                    provider + " " + failure + " retains account identity and sanitized guidance");
                if (failure != "invalid") Check(stale.Error?.Contains("(" + failure + ")") == true,
                    provider + " exposes safe actionable " + failure + " category");
            }
            clock.Advance(); phase = "success";
            var recovered = (await service.RefreshAsync()).Single(r => r.Source == first.Source);
            Check(recovered.State == "ok" && recovered.LastSuccessAt == clock.GetUtcNow() && recovered.Error is null && recovered.AccountId == account.Id,
                provider + " recovers on next successful quota read without reconnect or account replacement");
        }
        foreach (var provider in new[] { "kilo", "commandcode" })
        {
            var independentReadEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var client = new HttpClient(new RecoveryHttp(async (request, ct) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (provider == "kilo" && path.Contains("kiloPass") || provider == "commandcode" && path.EndsWith("subscriptions"))
                    independentReadEntered.TrySetResult();
                if (path.EndsWith("balance") || path.EndsWith("credits")) await independentReadEntered.Task.WaitAsync(ct);
                return Reply(Body(provider, request));
            }));
            var result = await Adapter(provider, client).FetchAsync(new("id", "Fixture", provider, true, "api-key", "connected"),
                new("fixture-key", baseUrl: Base(provider)));
            Check(independentReadEntered.Task.IsCompletedSuccessfully && result.Error is null && result.Readings.Count > 0,
                provider + " independent reporting reads overlap instead of consuming consecutive request timeouts");
        }
        return checks;
    }

    private static string RejectedKind(string provider) => provider switch
    {
        "kilo" => "Kilo requires its own API key account.", "requesty" => "Requesty Self requires its own API key account.",
        "synthetic" => "Synthetic quota requires its own API key account.", "stepfun" => "StepFun PAYG balance requires an API key.",
        "commandcode" => "Command Code quota requires a qualified API key.", "zai" => "Z.ai quota requires a qualified API key.",
        "kimi" => "Kimi quota requires a qualified API key.", "openrouter" => "OpenRouter quota requires an API key.",
        _ => throw new Exception("Provider")
    };
    private static string Base(string provider) => provider switch
    {
        "kilo" => "https://ai-gateway.kilo.ai/api/v1", "requesty" => "https://router.requesty.ai/v1",
        "synthetic" => "https://api.synthetic.new/openai/v1", "stepfun" => "https://api.stepfun.com/v1",
        "commandcode" => "https://api.commandcode.ai/provider/v1", "zai" => "https://api.z.ai/api/coding/paas/v4",
        "kimi" => "https://api.kimi.com/coding/v1", "openrouter" => "https://openrouter.ai/api/v1", _ => throw new Exception("Provider")
    };
    private static IQuotaAdapter Adapter(string provider, HttpClient client) => provider switch
    {
        "kilo" => new KiloQuotaAdapter(client), "requesty" => new RequestyQuotaAdapter(client), "synthetic" => new SyntheticQuotaAdapter(client),
        "stepfun" => new StepFunQuotaAdapter(client), "commandcode" => new CommandCodeQuotaAdapter(client), "zai" => new ZaiQuotaAdapter(client),
        "kimi" => new KimiQuotaAdapter(client), "openrouter" => new OpenRouterQuotaAdapter(client), _ => throw new Exception("Provider")
    };
    private static string Body(string provider, HttpRequestMessage request) => provider switch
    {
        "kilo" => request.RequestUri!.AbsolutePath.EndsWith("balance") ? "{\"balance\":8}" : "{\"subscription\":{\"status\":\"active\"}}",
        "requesty" => request.RequestUri!.AbsolutePath.EndsWith("/usage") ? "{\"usage\":{\"a\":{\"spend\":1,\"total_requests\":2,\"total_tokens\":3}}}" : "{\"id\":\"key-1\",\"monthly_limit\":20,\"monthly_spend\":4}",
        "synthetic" => "{\"planWeeklyLimit\":{\"percentRemaining\":30}}",
        "stepfun" => "{\"object\":\"account\",\"type\":\"prepaid\",\"balance\":5}",
        "commandcode" => request.RequestUri!.AbsolutePath.EndsWith("credits") ? "{\"credits\":{\"monthlyCredits\":8}}" : "{}",
        "zai" => "{\"success\":true,\"data\":{\"limits\":[{\"type\":\"CREDIT_LIMIT\",\"remaining\":8}]}}",
        "kimi" => "{\"usages\":{\"limit_5h\":{\"used_ratio\":0.2}}}",
        "openrouter" => "{\"data\":{\"limit\":10,\"limit_remaining\":8,\"usage\":2,\"include_byok_in_limit\":false}}",
        _ => throw new Exception("Provider")
    };
    private static HttpResponseMessage Reply(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
sealed class RecoveryHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request, ct);
}
sealed class RecoveryClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.Parse("2026-10-25T10:00:00Z");
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance() => now += TimeSpan.FromMinutes(1);
}
sealed class RecoveryAccounts(AccountDto account) : IQuotaAccountSource
{
    public Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AccountDto>>([account]);
}
sealed class RecoveryCredentials(QuotaCredential credential) : IQuotaCredentialSource
{
    public Task<QuotaCredential?> GetCredentialAsync(string accountId, CancellationToken ct = default) => Task.FromResult<QuotaCredential?>(credential);
}
