using System.Net;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Quota;

var checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
const string id = "0123456789abcdef0123456789abcdef";
const string otherId = "abcdef0123456789abcdef0123456789";
var now = DateTimeOffset.Parse("2026-10-25T08:15:30Z");
var start = CloudflareWorkersAiReader.UtcDayStart(now);
var account = new AccountDto("stable-account-id", "Cloudflare", "cloudflare-workers-ai", true, "api-key", "ready",
    Binding: new("cloudflare-workers-ai", Subject: id, ResourceType: "account", ResourceId: id));
var credential = new QuotaCredential("fixture-inference-and-analytics-token", settings: new Dictionary<string, string> { ["accountId"] = id });
JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
object Usage(object? neurons, string accountId = id) => new { data = new { viewer = new { accounts = new[] { new { accountTag = accountId, aiInferenceAdaptiveGroups = new[] { new { sum = new { totalNeurons = neurons } } } } } } } };

using (var handler = new Handler(async request =>
{
    Check(request.Method == HttpMethod.Post && request.RequestUri!.AbsoluteUri == CloudflareWorkersAiReader.Endpoint, "fixed GraphQL endpoint");
    Check(request.Headers.Authorization?.Parameter == credential.BearerToken, "existing analytics-capable API token is used");
    var body = await request.Content!.ReadAsStringAsync();
    using var requestJson = JsonDocument.Parse(body);
    var query = requestJson.RootElement.GetProperty("query").GetString()!;
    Check(query.Contains(id) && query.Contains("limit:1") && query.Contains("totalNeurons"), "account-pinned neuron aggregate");
    Check(query.Contains("2026-10-25T00:00:00") && query.Contains("2026-10-25T08:15:30"), "UTC day-to-observation filter");
    Check(!query.Contains("dimensions") && !query.Contains("10000") && !query.Contains("modelId"), "no top-N or invented allowance");
    return Handler.Json(Usage(123.5));
}))
using (var adapter = new CloudflareQuotaAdapter(handler: handler, clock: new Clock(now)))
{
    var result = await adapter.FetchAsync(account, credential);
    var row = result.Readings.Single();
    Check(handler.Calls == 1 && row.Used == 123.5, "default adapter reads source without maintainer qualification flag");
    Check(row.Unit == "neurons" && row.MeasurementKind == "usage-estimate", "native neurons remain estimated");
    Check(row.Limit is null && row.Remaining is null && row.ResetsAt is null && row.DataAsOf is null && row.FreshUntil is null, "no invented quota balance or freshness");
    Check(row.WindowStart == start && row.WindowEnd == start.AddDays(1) && row.ObservedAt == now, "reporting window calendar boundary distinct from source data time");
    Check(row.ScopeId == id && row.BudgetPoolId == "cloudflare-account:" + id && !row.InheritAccountPool, "keys in same Cloudflare account share the usage scope");
}

Check(CloudflareQuotaAdapter.Normalize(Json(Usage(0)), id, start, now).Readings.Single().Used == 0, "explicit measured zero preserved");
foreach (var value in new object?[] { null, -1, "NaN", "Infinity", 9007199254740992d })
    Check(CloudflareQuotaAdapter.Normalize(Json(Usage(value)), id, start, now).Readings.Count == 0, "invalid/missing neurons not zero");
Check(CloudflareQuotaAdapter.Normalize(Json(Usage(5, otherId)), id, start, now).Readings.Count == 0, "foreign account rejected");
var partial = CloudflareQuotaAdapter.Normalize(Json(new { data = Json(Usage(42)).GetProperty("data"), errors = new[] { new { message = "fixture-secret-upstream-diagnostic" } } }), id, start, now);
Check(partial.Readings.Count == 0 && partial.Error is not null && !partial.Error.Contains("fixture-secret"), "partial GraphQL errors sanitized and stale-capable");
var noGroups = Json(new { data = new { viewer = new { accounts = new[] { new { accountTag = id, aiInferenceAdaptiveGroups = Array.Empty<object>() } } } } });
Check(CloudflareQuotaAdapter.Normalize(noGroups, id, start, now).Readings.Count == 0, "empty analytics is unknown, not zero");
var grouped = Json(new { data = new { viewer = new { accounts = new[] { new { accountTag = id, aiInferenceAdaptiveGroups = new[] { new { dimensions = new { modelId = "one" }, sum = new { totalNeurons = 5 } } } } } } } });
Check(CloudflareQuotaAdapter.Normalize(grouped, id, start, now).Readings.Count == 0, "model group cannot become whole-account usage");

using (var handler = new Handler(_ => throw new Exception("invalid bindings cannot reach provider")))
using (var adapter = new CloudflareQuotaAdapter(handler: handler, clock: new Clock(now)))
{
    foreach (var invalid in new[] {
        new QuotaCredential("fixture", settings: new Dictionary<string, string> { ["accountId"] = otherId }),
        new QuotaCredential("fixture", monitor: new("api-key", "fixture-monitor", otherId)),
        new QuotaCredential("fixture", monitor: new("api-key", null, id)),
        new QuotaCredential("fixture", monitor: new("access-token-snapshot", "fixture-monitor", id, now)),
        new QuotaCredential("fixture", expiresAt: now)
    }) Check((await adapter.FetchAsync(account, invalid)).Support?.Kind == "setup-required", "invalid/expired/mismatched credential needs setup");
    Check(handler.Calls == 0, "invalid selection has no network side effect");
}

using (var handler = new Handler(request =>
{
    Check(request.Headers.Authorization?.Parameter == "fixture-monitor", "separate monitor credential wins");
    return Task.FromResult(Handler.Json(Usage(1)));
}))
using (var adapter = new CloudflareQuotaAdapter(handler: handler, clock: new Clock(now)))
    Check((await adapter.FetchAsync(account, new("expired-inference", expiresAt: now.AddDays(-1), monitor: new("api-key", "fixture-monitor", id)))).Readings.Count == 1, "monitor independent of inference expiry");

foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden })
{
    using var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("fixture-secret") }));
    using var adapter = new CloudflareQuotaAdapter(handler: handler, clock: new Clock(now));
    var result = await adapter.FetchAsync(account, credential);
    Check(result.Readings.Count == 0 && result.Error is not null && result.Support?.Kind == "setup-required" && !result.Error.Contains("fixture-secret"), "auth failures actionable and sanitized");
}

using (var handler = new Handler(async request =>
{
    var body = await request.Content!.ReadAsStringAsync();
    if (body.Contains("__type")) return Handler.Json(new { data = new { __type = new { fields = new[] { new { name = "otherDataset" } } } } });
    return Handler.Json(new { errors = new[] { new { message = "Cannot query field aiInferenceAdaptiveGroups secret-upstream" } } });
}))
using (var adapter = new CloudflareQuotaAdapter(handler: handler, clock: new Clock(now)))
{
    var result = await adapter.FetchAsync(account, credential);
    Check(handler.Calls == 2 && result.Readings.Count == 0 && result.Support!.Message.Contains("does not expose"), "schema failure diagnosed with bounded official introspection");
    Check(!result.Support.Message.Contains("secret-upstream"), "schema diagnostics do not leak upstream messages");
}
Check(CloudflareWorkersAiReader.DatasetAvailable(Json(new { errors = new[] { new { message = "denied" } } })) is null, "failed introspection is unknown");
Check(CloudflareWorkersAiReader.UtcDayStart(DateTimeOffset.Parse("2026-10-25T00:30:00+02:00")) == DateTimeOffset.Parse("2026-10-24T00:00:00Z"), "local day/DST cannot shift UTC quota day");
using (var handler = new Handler(_ => throw new Exception("empty interval cannot be queried")))
using (var adapter = new CloudflareQuotaAdapter(handler: handler, clock: new Clock(start)))
    Check((await adapter.FetchAsync(account, credential)).Readings.Count == 0 && handler.Calls == 0, "midnight does not fabricate zero");

foreach (var response in new[] {
    new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("fixture-secret") },
    new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{invalid fixture-secret") },
    new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 1024 * 1024 + 1)) }
})
{
    using var handler = new Handler(_ => Task.FromResult(response));
    using var adapter = new CloudflareQuotaAdapter(handler: handler, clock: new Clock(now));
    var rejected = false;
    try { await adapter.FetchAsync(account, credential); }
    catch (Exception error) when (error.GetType().Name == "QuotaFetchException")
    { rejected = true; Check(!error.Message.Contains("fixture-secret"), "transport/malformed/oversize diagnostics sanitized"); }
    Check(rejected, "rate-limit/malformed/oversized response never returns successful empty usage");
}
using (var handler = new Handler(_ => throw new OperationCanceledException()))
using (var adapter = new CloudflareQuotaAdapter(handler: handler, clock: new Clock(now)))
{
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    var cancelledCorrectly = false;
    try { await adapter.FetchAsync(account, credential, cancelled.Token); }
    catch (OperationCanceledException) { cancelledCorrectly = true; }
    Check(cancelledCorrectly, "caller cancellation propagates");
}

Console.WriteLine($"Cloudflare quota: {checks} checks passed.");
sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Calls++; return send(request); }
    public static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) };
}
