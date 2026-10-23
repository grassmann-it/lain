using System.Net;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Providers;
using Lain.Host.Quota;

var count = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); count++; }
using var handler = new FakeHttp();
using var client = new HttpClient(handler);
var adapter = new OpenRouterQuotaAdapter(client);
var account = new AccountDto("local-a", "Gateway", "openrouter", true, "api-key", "ready", "unrelated-pool");
handler.Body = """{"data":{"is_free_tier":false,"include_byok_in_limit":false,"limit":100,"limit_remaining":70,"limit_reset":"monthly","usage":400,"usage_monthly":30,"free_model_daily_requests":{"used":10,"limit":50,"remaining":40},"budgets":[{"scope":"workspace","budget_id":"budget-a","limit_usd":20,"spend_usd":25,"remaining_usd":-5,"resets_at":"2026-11-01T00:00:00Z"}],"effective_budget":{"remaining_usd":-5},"rate_limit":{"requests":1000}}}""";
var result = await adapter.FetchAsync(account, new QuotaCredential("mock-key", baseUrl: "https://openrouter.ai/api/v1"));
Check(handler.Calls == 1 && handler.Method == HttpMethod.Get && handler.Url == OpenRouterQuotaAdapter.Endpoint, "read-only fixed key endpoint");
Check(handler.Authorization == "Bearer mock-key", "current API bearer credential");
Check(handler.AccountHeader is null, "no unrelated account identity header");
var daily = result.Readings.Single(r => r.Unit == "requests");
Check(daily.Used == 10 && daily.Limit == 50 && daily.Remaining == 40 && daily.ResetsAt is null, "reported counter preserved, no inferred ceiling/reset");
var cap = result.Readings.Single(r => r.Source.EndsWith("key-spend-cap"));
Check(cap.Used == 30 && cap.Limit == 100 && cap.Remaining == 70 && cap.Unit == "usd", "current key window separate from lifetime and account balance");
Check(result.Readings.Count == 3 && result.Readings.All(r => !r.InheritAccountPool), "overlapping constraints separate, effective duplicate ignored, no shared-pool invention");
Check(result.Readings.Single(r => r.Source.Contains("#budget/")).Remaining == -5, "reported exceeded budget preserved");
using var byok = JsonDocument.Parse("""{"data":{"include_byok_in_limit":true,"limit":20,"limit_reset":"daily","usage_daily":3,"byok_usage_daily":2}}""");
Check(OpenRouterQuotaAdapter.Normalize(byok.RootElement).Single().Used == 5, "BYOK spend included only when explicitly counted and both measurements exist");
using var unknownByok = JsonDocument.Parse("""{"data":{"include_byok_in_limit":true,"limit":20,"limit_reset":"daily","usage_daily":3}}""");
Check(OpenRouterQuotaAdapter.Normalize(unknownByok.RootElement).Single().Used is null, "missing counted BYOK spend remains unknown");

using var sparse = JsonDocument.Parse("""{"data":{"is_free_tier":true,"limit":null,"limit_remaining":null,"usage":0}}""");
Check(OpenRouterQuotaAdapter.Normalize(sparse.RootElement).Count == 0, "free tier and null key cap do not fabricate quota");
using var nullCounters = JsonDocument.Parse("""{"data":{"free_model_daily_requests":{"used":null,"limit":null,"remaining":null}}}""");
Check(OpenRouterQuotaAdapter.Normalize(nullCounters.RootElement).Single().Remaining is null, "missing counter remains unknown");
using var invalid = JsonDocument.Parse("""{"data":{"free_model_daily_requests":{"used":-1,"limit":"NaN","remaining":0}}}""");
var invalidRow = OpenRouterQuotaAdapter.Normalize(invalid.RootElement).Single();
Check(invalidRow.Used is null && invalidRow.Limit is null && invalidRow.Remaining == 0, "bad numeric values excluded; real zero retained");
var budgetA = """{"scope":"workspace","budget_id":"a","limit_usd":20,"remaining_usd":10}""";
var budgetB = """{"scope":"member","guardrail_id":"b","limit_usd":30,"remaining_usd":15}""";
using var before = JsonDocument.Parse("{\"data\":{\"budgets\":[" + budgetA + "," + budgetB + "]}}");
using var after = JsonDocument.Parse("{\"data\":{\"budgets\":[" + budgetB + "," + budgetA + "]}}");
Check(OpenRouterQuotaAdapter.Normalize(before.RootElement).Select(r => r.Source).Order().SequenceEqual(OpenRouterQuotaAdapter.Normalize(after.RootElement).Select(r => r.Source).Order()), "budget window identity stable across reordering");
foreach (var bad in new[] { "http://openrouter.ai/api/v1", "https://openrouter.ai.evil/api/v1", "https://openrouter.ai/api/v1?x=y", "https://user@openrouter.ai/api/v1", "https://openrouter.ai:444/api/v1", "https://openrouter.ai/other" })
{
    try { await adapter.FetchAsync(account, new QuotaCredential("must-not-leak", baseUrl: bad)); throw new Exception("Expected endpoint rejection"); }
    catch (QuotaFetchException) { Check(handler.Calls == 1, "invalid endpoint fails before network"); }
}
handler.Status = HttpStatusCode.Unauthorized;
handler.Body = "raw provider message containing mock-key";
try { await adapter.FetchAsync(account, new QuotaCredential("mock-key")); throw new Exception("Expected HTTP rejection"); }
catch (QuotaFetchException ex) { Check(ex.Message == "Quota HTTP 401.", "HTTP errors omit provider body and credentials"); }
handler.Status = HttpStatusCode.Redirect;
try { await adapter.FetchAsync(account, new QuotaCredential("mock-key")); throw new Exception("Expected redirect rejection"); }
catch (QuotaFetchException ex) { Check(ex.Message == "Quota HTTP 302.", "redirect response rejected"); }

var accountId = new string('a', 32);
Check(GatewayRouteValidation.CloudflareBaseUrl(accountId) == $"https://api.cloudflare.com/client/v4/accounts/{accountId}/ai/v1", "account-specific direct Cloudflare endpoint");
Check(GatewayRouteValidation.IsOfficialEndpoint("cloudflare-workers-ai", GatewayRouteValidation.CloudflareBaseUrl(accountId), accountId), "Cloudflare direct endpoint accepted");
Check(!GatewayRouteValidation.IsOfficialEndpoint("cloudflare-workers-ai", GatewayRouteValidation.CloudflareBaseUrl(accountId), new string('b', 32)), "wrong billing account endpoint rejected");
Check(!GatewayRouteValidation.IsOfficialEndpoint("cloudflare-workers-ai", "https://api.cloudflare.com/client/v4/accounts/{accountId}/ai/v1"), "unexpanded endpoint template rejected");
Check(!GatewayRouteValidation.IsOfficialEndpoint("requesty", "https://router.requesty.ai.evil/v1"), "gateway lookalike rejected");
try { GatewayRouteValidation.CloudflareBaseUrl("../other"); throw new Exception("Expected account ID rejection"); }
catch (ArgumentException) { Check(true, "account path injection rejected"); }
var free = new HashSet<string> { "qwen/qwen3-coder:free", "meta-llama/llama-3.3-70b-instruct:free", "nvidia/nemotron-3-super-120b-a12b", "@cf/google/gemma-4-26b-a4b-it", "policy/unsafe" };
Check(GatewayRouteValidation.FreeOnlyRejections("openrouter", "qwen/qwen3-coder:free", free, true).Length == 0, "qualified concrete free route accepted");
Check(GatewayRouteValidation.FreeOnlyRejections("openrouter", "qwen/qwen3-coder:free", free, true, ["paid/model"]).Length > 0, "paid or unknown fallback blocked");
Check(GatewayRouteValidation.FreeOnlyRejections("openrouter", "qwen/unknown:free", free, true).Length > 0, "suffix alone is not price qualification");
Check(GatewayRouteValidation.FreeOnlyRejections("openrouter", "openrouter/free", free, true).Length == 0, "random free router explicit option");
Check(GatewayRouteValidation.FreeOnlyRejections("openrouter", "openrouter/free", free, true, ["qwen/qwen3-coder:free"]).Length > 0, "random router cannot hide a fallback chain");
Check(GatewayRouteValidation.FreeOnlyRejections("requesty", "policy/unsafe", free, true).Length > 0, "Requesty policy reference blocked even when mislabeled free");
Check(GatewayRouteValidation.FreeOnlyRejections("requesty", "nemotron-3-super-120b-a12b", free, true).Length > 0, "Requesty provider prefix required");
Check(GatewayRouteValidation.FreeOnlyRejections("requesty", "nvidia/nemotron-3-super-120b-a12b", free, true, usesGatewayPolicy: true).Length > 0, "unqualified gateway policy blocked");
Check(GatewayRouteValidation.FreeOnlyRejections("cloudflare-workers-ai", "@cf/google/gemma-4-26b-a4b-it", free, null).Length > 0, "unknown Cloudflare billing posture blocked");
Check(GatewayRouteValidation.FreeOnlyRejections("openrouter", "qwen/qwen3-coder:free", free, true, usesPaidExtras: true).Length > 0, "paid extras blocked");
Console.WriteLine($"Gateway synthetic tests passed ({count} assertions). No live provider access tested.");

sealed class FakeHttp : HttpMessageHandler
{
    public string Body = "{}";
    public HttpStatusCode Status = HttpStatusCode.OK;
    public int Calls;
    public string? Url, Authorization, AccountHeader;
    public HttpMethod? Method;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;
        Url = request.RequestUri!.ToString(); Method = request.Method;
        Authorization = request.Headers.Authorization?.ToString();
        AccountHeader = request.Headers.TryGetValues("ChatGPT-Account-Id", out var ids) ? ids.Single() : null;
        return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") });
    }
}
