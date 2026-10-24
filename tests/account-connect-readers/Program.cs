using System.Net;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Quota;
using Lain.Host.Providers.Xai;

var checks = 0;
void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
JsonElement Json(string value) { using var doc = JsonDocument.Parse(value); return doc.RootElement.Clone(); }
AccountDto Account(string provider, string id = "fixture-account") => new(id, "Fixture", provider, true, "api-key", "connected");
const string secret = "fixture-private-api-key";
var zeroWallet = KiloQuotaAdapter.Normalize(Json("{\"balance\":0}"));
Check(zeroWallet.Single().Remaining == 0 && zeroWallet[0].Limit is null && zeroWallet[0].UsedPercent is null && zeroWallet[0].ResetsAt is null, "Kilo zero is balance without invented denominator/reset");
foreach (var body in new[] { "{}", "{\"balance\":null}", "{\"balance\":-1}", "{\"balance\":\"NaN\"}" })
    Check(KiloQuotaAdapter.Normalize(Json(body)).Count == 0, "Kilo invalid balance stays unknown");
var pass = KiloPassReader.Normalize(Json("[{\"result\":{\"data\":{\"json\":{\"subscription\":{\"status\":\"active\",\"currentPeriodBaseCreditsUsd\":20,\"currentPeriodUsageUsd\":0,\"currentPeriodBonusCreditsUsd\":5,\"nextBillingAt\":\"2026-11-01T00:00:00Z\",\"nextRenewalAt\":\"2026-11-02T00:00:00Z\"}}}}}]"));
Check(pass is { Status: "active", IsConsumable: true, CurrentPeriodUsageUsd: 0, CurrentPeriodBaseCreditsUsd: 20, CurrentPeriodBonusCreditsUsd: 5 } && pass.NextBillingAt != pass.NextRenewalAt, "Pass fields remain separate");
Check(KiloPassReader.Normalize(Json("{\"subscription\":{\"status\":\"canceled\",\"currentPeriodUsageUsd\":null}}")) is { Status: "canceled", IsConsumable: false, CurrentPeriodUsageUsd: null }, "Canceled Pass is a status, never zero quota");
Check(KiloPassReader.Normalize(Json("{\"subscription\":{\"status\":\"surprise\",\"currentPeriodBaseCreditsUsd\":12,\"nextRenewalAt\":\"invalid\"}}")) is { Status: null, IsConsumable: null, NextRenewalAt: null }, "Unknown status/time not invented");
Check(KiloPassReader.Normalize(Json("[]")) is null, "Empty batch unknown");
var month = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
var uncapped = Json("{\"id\":\"key-1\",\"monthly_limit\":0,\"monthly_spend\":0,\"permissions\":{\"manage\":\"none\",\"completions\":\"write\"},\"group\":{\"id\":\"group-1\"}}");
Check(RequestyQuotaAdapter.NormalizeMetadata(uncapped) is { IsUncapped: true, ManagePermission: "none", GroupId: "group-1" }, "Requesty Self needs no manage permission");
Check(RequestyQuotaAdapter.Normalize(uncapped, month).Single() is { Limit: null, Remaining: null, Used: 0, WindowStart: var start } && start == month, "Uncapped zero not exhausted");
var capped = RequestyQuotaAdapter.Normalize(Json("{\"id\":\"key-2\",\"monthly_limit\":20,\"monthly_spend\":4}"), month).Single();
Check(capped.Remaining == 16 && capped.WindowEnd == month.AddMonths(1) && capped.ScopeId == "key-2", "Requesty monthly cap has period and key scope");
var usage = RequestyQuotaAdapter.NormalizeUsage(Json("{\"usage\":{\"2026-10-01\":{\"spend\":\"1.25\",\"total_requests\":0,\"total_tokens\":12},\"2026-10-02\":{\"spend\":\"0.75\",\"total_requests\":2,\"total_tokens\":8}}}"), month, month.AddDays(3));
Check(usage.Count == 3 && usage[0].Used == 2 && usage.All(x => x.Limit is null && x.WindowEnd == month.AddDays(3)), "Self usage bounded and separate from cap");
Check(RequestyQuotaAdapter.NormalizeUsage(Json("{\"usage\":{\"a\":{\"spend\":1},\"b\":{}}}"), month, month.AddDays(1)).Count == 0, "Incomplete usage not undercounted");
var modern = SyntheticQuotaAdapter.Normalize(Json("{\"planWeeklyLimit\":{\"percentRemaining\":0,\"nextRegenAt\":\"2026-10-25T00:00:00Z\",\"nextRegenPercent\":20},\"weeklyTokenLimit\":{\"percentRemaining\":75,\"maxCredits\":\"999\"},\"rollingFiveHourLimit\":{\"remaining\":0,\"max\":5,\"nextTickAt\":\"2026-10-25T01:00:00Z\",\"tickPercent\":10}}").Clone());
Check(modern.Count == 2 && modern[0].WindowId == "plan-weekly" && modern[0].UsedPercent == 100 && modern.All(x => x.ResetsAt is null) && modern[0].NextRegenerationPercent == 20, "Synthetic new preferred; actual rolling constraint separate; partial refill");
var exact = SyntheticQuotaAdapter.Normalize(Json("{\"weeklyTokenLimit\":{\"maxCredits\":\"123456789012345678901234567890.1234567890\",\"remainingCredits\":\"0.00000000000000000001\",\"nextRegenCredits\":\"20.00\",\"percentRemaining\":1,\"nextRegenAt\":\"2026-10-25T00:00:00Z\"}}")).Single();
Check(exact.ExactLimit == "123456789012345678901234567890.1234567890" && exact.ExactRemaining == "0.00000000000000000001" && exact.ExactNextRegeneration == "20.00" && exact.Limit is null && exact.Remaining is null, "Exact Synthetic strings never pass through double");
Check(SyntheticQuotaAdapter.Normalize(Json("{\"subscription\":{\"limit\":10,\"requests\":0,\"renewsAt\":\"2026-11-01T00:00:00Z\"}}")).Single().Remaining == 10, "Legacy Synthetic preserved");
Check(SyntheticQuotaAdapter.Normalize(Json("{\"subscription\":{\"limit\":10,\"requests\":1},\"rollingFiveHourLimit\":{\"remaining\":2,\"max\":5}}")).Count == 2, "Synthetic legacy subscription preserved beside rolling constraint");
Check(SyntheticQuotaAdapter.Normalize(Json("{\"planWeeklyLimit\":{\"percentRemaining\":101,\"nextRegenPercent\":-1},\"weeklyTokenLimit\":{\"remainingCredits\":\"NaN\"}}")).Count == 0, "Malformed Synthetic stays unknown");
foreach (var type in new[] { "prepaid", "postpaid" })
    Check(StepFunQuotaAdapter.Normalize(Json("{\"object\":\"account\",\"type\":\"" + type + "\",\"balance\":0,\"total_cash_balance\":100,\"total_voucher_balance\":50}")).Single() is { Remaining: 0, Limit: null, UsedPercent: null }, "Step retains zero and no recharge-derived limit");
Check(StepFunQuotaAdapter.Normalize(Json("{\"object\":\"account\",\"type\":\"postpaid\",\"balance\":-1}")).Count == 0, "Step negative not silently interpreted");
Check(StepFunQuotaAdapter.Normalize(Json("{\"object\":\"account\",\"type\":\"prepaid\",\"balance\":50,\"organization_id\":\"org-1\"}")).Count == 0, "Step org scope unknown");
var xai = XaiKeyMetadataReader.Normalize(Json("{\"team_id\":\"team-1\",\"api_key_id\":\"key-1\",\"acls\":[\"api-key:model:*\"],\"api_key_disabled\":false,\"api_key_blocked\":true,\"team_blocked\":false,\"redacted_api_key\":\"omit\"}"));
Check(xai is { TeamId: "team-1", ApiKeyId: "key-1", ApiKeyDisabled: false, ApiKeyBlocked: true } && !JsonSerializer.Serialize(xai).Contains("omit"), "xAI selected metadata only");
Check(XaiKeyMetadataReader.Normalize(Json("{}")) is null, "xAI unknown schema");

// All HTTP is fake. There are no live credentials or provider calls in this suite.
using (var client = new HttpClient(new FakeHandler((req, _) => Task.FromResult(req.RequestUri!.AbsolutePath.EndsWith("balance") ? Reply("{\"balance\":0}") : new HttpResponseMessage(HttpStatusCode.Forbidden)))))
{
    var result = await new KiloQuotaAdapter(client).FetchAsync(Account("kilo"), new(secret));
    Check(result.Readings.Single().Remaining == 0 && result.Error is not null && result.KiloPass is null, "Pass failure preserves wallet");
}
using (var client = new HttpClient(new FakeHandler((req, _) => Task.FromResult(Reply(req.RequestUri!.AbsolutePath.EndsWith("balance") ? "{\"balance\":1}" : "{\"subscription\":{\"status\":\"active\",\"currentPeriodUsageUsd\":0}}")))))
{
    var adapter = new KiloQuotaAdapter(client);
    var first = await adapter.FetchAsync(Account("kilo", "one"), new(secret));
    var second = await adapter.FetchAsync(Account("kilo", "two"), new("different-fixture-key"));
    Check(first.Readings[0].BudgetPoolId is null && second.Readings[0].BudgetPoolId is null && !first.Readings[0].InheritAccountPool, "No automatic pool merge between accounts");
}
using (var client = new HttpClient(new FakeHandler((req, _) =>
{
    Check(req.RequestUri!.Host == "api-v2.requesty.ai" && req.RequestUri.AbsolutePath.StartsWith("/v1/manage/apikey/self"), "Requesty only reads fixed Self resource");
    if (!req.RequestUri.AbsolutePath.EndsWith("/usage")) return Task.FromResult(Reply(uncapped.GetRawText()));
    Check(req.RequestUri.Query.Contains("start=") && req.RequestUri.Query.Contains("end="), "Requesty usage explicit bounded period");
    return Task.FromResult(Reply("{\"usage\":{\"2026-10-01\":{\"spend\":0,\"total_requests\":0,\"total_tokens\":0}}}"));
})))
{
    var result = await new RequestyQuotaAdapter(client).FetchAsync(Account("requesty"), new(secret));
    Check(result.Error is null && result.RequestyKey is { ManagePermission: "none", IsUncapped: true } && result.Readings.Count == 4, "Same ordinary Requesty key reads own metadata and usage");
}
foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.Found })
{
    var sent = 0;
    using var client = new HttpClient(new FakeHandler((_, _) => { sent++; var reply = new HttpResponseMessage(status) { Content = new StringContent(secret) }; reply.Headers.Location = new Uri("https://untrusted.invalid/leak"); return Task.FromResult(reply); }));
    var result = await new KiloQuotaAdapter(client).FetchAsync(Account("kilo"), new(secret));
    Check(sent == 2 && result.Readings.Count == 0 && !JsonSerializer.Serialize(result).Contains(secret), "HTTP errors and redirects sanitized without following");
}
using (var client = new HttpClient(new FakeHandler((_, _) => throw new HttpRequestException(secret))))
{
    var result = await new RequestyQuotaAdapter(client).FetchAsync(Account("requesty"), new(secret));
    Check(!JsonSerializer.Serialize(result).Contains(secret), "Transport exception never echoes key");
}
using (var client = new HttpClient(new FakeHandler((_, _) => throw new TaskCanceledException(secret))))
{
    var result = await new KiloQuotaAdapter(client).FetchAsync(Account("kilo"), new(secret));
    Check(result.Error?.Contains("timed out") == true && !JsonSerializer.Serialize(result).Contains(secret), "Timeout sanitized");
}
using (var client = new HttpClient(new FakeHandler((_, _) => Task.FromResult(Reply(new string('x', 1024 * 1024 + 1))))))
{
    var result = await new KiloQuotaAdapter(client).FetchAsync(Account("kilo"), new(secret));
    Check(result.Readings.Count == 0 && result.Error is not null, "Oversized response bounded");
}
using (var client = new HttpClient(new FakeHandler((_, _) => Task.FromResult(Reply("{\"team_id\":\"team-1\",\"api_key_id\":\"key-1\"}")))))
{
    var reader = new XaiKeyMetadataReader(client);
    Check((await reader.ReadAsync(Account("xai"), new(secret)))?.TeamId == "team-1", "xAI confirms returned team");
    await Denied(() => reader.ReadAsync(Account("xai"), new(secret, "different-team")), "Wrong bound team rejected");
}
using (var client = new HttpClient(new FakeHandler((_, _) => Task.FromResult(Reply("{\"id\":\"actual-key\",\"group\":{\"id\":\"actual-group\"},\"monthly_limit\":10,\"monthly_spend\":0}")))))
{
    var mismatch = await new RequestyQuotaAdapter(client).FetchAsync(Account("requesty") with { Binding = new("requesty", ResourceType: "group", ResourceId: "different-group") }, new(secret));
    Check(mismatch.Readings.Count == 0 && mismatch.RequestyKey is null && mismatch.Error is not null, "Mismatched Requesty binding exposes no usage or metadata");
}
using (var client = new HttpClient(new FakeHandler((_, _) => throw new Exception("Unexpected network"))))
{
    await Denied(() => new KiloQuotaAdapter(client).FetchAsync(Account("kilo"), new(secret, baseUrl: "https://untrusted.invalid/v1")), "Wrong Kilo host rejected before request");
    await Denied(() => new KiloQuotaAdapter(client).FetchAsync(Account("kilo") with { Binding = new("kilo", Product: "gateway", ResourceType: "organization", ResourceId: "org-1") }, new(secret)), "Kilo organization binding rejected before request");
    await Denied(() => new StepFunQuotaAdapter(client).FetchAsync(Account("stepfun") with { Binding = new("stepfun", Product: "payg", Region: "ai", ResourceType: "personal") }, new(secret, baseUrl: StepFunQuotaAdapter.ApiBaseUrl)), "Step incompatible region rejected before request");
    await Denied(() => new RequestyQuotaAdapter(client).FetchAsync(Account("requesty"), new(secret, baseUrl: "https://router.requesty.ai.evil.invalid/v1")), "Wrong Requesty host rejected");
    await Denied(() => new XaiKeyMetadataReader(client).ReadAsync(Account("xai"), new(secret, baseUrl: "https://api.x.ai/v1?key=private")), "Wrong xAI target rejected");
}
Console.WriteLine($"PASS: {checks} account-connect reader checks");

async Task Denied(Func<Task> action, string message)
{
    try { await action(); } catch (QuotaFetchException ex) { Check(!ex.Message.Contains(secret), message); return; }
    throw new Exception(message);
}
static HttpResponseMessage Reply(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri!.ToString().Contains("fixture-private-api-key", StringComparison.Ordinal)) throw new Exception("Credential in URL");
        if (request.Headers.Authorization?.Scheme != "Bearer" || request.Method != HttpMethod.Get) throw new Exception("Expected bearer GET");
        return reply(request, ct);
    }
}
