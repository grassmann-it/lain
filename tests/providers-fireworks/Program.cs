using System.Net;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Providers;
using Lain.Host.Quota;

var assertions = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); assertions++; }
var root = args.Single();
string Fixture(string name) => File.ReadAllText(Path.Combine(root, "tests/providers-fireworks/fixtures", name + ".json"));
var clock = new Clock();
var credential = new QuotaCredential("synthetic-inference", baseUrl: FireworksQuotaAdapter.ApiBaseUrl,
    settings: new Dictionary<string, string> { ["accountId"] = "demo" });
var account = new AccountDto("local-a", "Fireworks", "fireworks", true, "api-key", "unverified", "unrelated-pool");
var http = new FixtureHttp((_, _) => Task.FromResult(Json(Fixture("mixed"))));
using var client = new HttpClient(http);
var adapter = new FireworksQuotaAdapter(client, clock);
var result = await adapter.FetchAsync(account, credential);
var spend = result.Readings.Single(r => r.WindowId == "monthly-spend-usd");
Check(http.Calls.Single().Url == "https://api.fireworks.ai/v1/accounts/demo/quotas?pageSize=200", "official scoped list endpoint");
Check(http.Calls.Single().Token == "synthetic-inference", "API key only in bearer header");
Check(result.Support?.Kind == "api" && result.Error is null, "official API capability");
Check(spend.Unit == "USD" && spend.Used == 12.5 && spend.Limit == 50 && spend.Remaining == 37.5 && spend.ApprovedLimit == 500, "enforced limit separate from approved ceiling");
Check(spend.ResetsAt is null && spend.DataAsOf is null && spend.ProviderUpdatedAt == DateTimeOffset.Parse("2026-10-01T09:00:00Z"), "updateTime is neither reset nor usage observation time");
Check(spend.Scope == "account" && spend.ScopeId == "demo" && !spend.InheritAccountPool && spend.BudgetPoolId is null, "explicit account identity without inferred pool");
var raw = result.Readings.Single(r => r.WindowId == "h100-us-iowa-1");
Check(raw.Used == 0 && raw.Limit == 16 && raw.ApprovedLimit == 32 && raw.Remaining is null && raw.Unit == "unknown" && raw.MeasurementKind == "limit", "unmapped constraint preserves raw values without fabricated GPU/token unit");
Check(result.Readings.Single(r => r.WindowId == "unmapped-request-limit").Used is null, "missing usage remains missing");
Check(!JsonSerializer.Serialize(result).Contains("synthetic-inference"), "credential not in output");

foreach (var input in new[] { ("zero", 0d, (double?)0), ("limit-only", 50d, (double?)null) })
{
    http.Respond = (_, _) => Task.FromResult(Json(Fixture(input.Item1)));
    var row = (await adapter.FetchAsync(account, credential)).Readings.Single();
    Check(row.Limit == input.Item2 && row.Remaining == input.Item3, "zero observed; limit alone does not manufacture usage or remainder");
}
var before = http.Calls.Count;
foreach (var id in new string?[] { null, "", "../another", "demo?key=value", "demo/quotas/other", "demo\nother" })
{
    var settings = id is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["accountId"] = id };
    var setup = await adapter.FetchAsync(account, new("synthetic-inference", settings: settings));
    Check(setup.Support?.Kind == "setup-required" && setup.Error is null && setup.Readings.Count == 0, "missing/unsafe scope is actionable setup");
}
Check(http.Calls.Count == before, "missing or malformed scope causes no network call");
foreach (var baseUrl in new[] { "https://proxy.invalid/v1", "http://api.fireworks.ai/inference/v1", "https://api.fireworks.ai/inference/v1?x=1", "https://api.fireworks.ai.evil.invalid/inference/v1", "https://api.fireworks.ai/inference" })
{
    try { await adapter.FetchAsync(account, new("synthetic-inference", baseUrl: baseUrl, settings: new Dictionary<string, string> { ["accountId"] = "demo" })); throw new Exception("Expected endpoint rejection"); }
    catch (QuotaFetchException) { Check(http.Calls.Count == before, "off-route credential never sent"); }
}

http.Respond = (_, _) => Task.FromResult(Json(Fixture("mixed")));
var separate = new QuotaCredential("synthetic-inference", settings: new Dictionary<string, string> { ["accountId"] = "wrong" },
    monitor: new("api-token", "synthetic-monitor", "demo"));
await adapter.FetchAsync(account, separate);
Check(http.Calls.Last().Token == "synthetic-monitor" && http.Calls.Last().Url.Contains("accounts/demo/"), "monitor token and explicit resource override inference settings together");
before = http.Calls.Count;
var missingSecret = await adapter.FetchAsync(account, new("synthetic-inference", monitor: new("api-token", null, "demo")));
Check(missingSecret.Support?.Kind == "setup-required" && http.Calls.Count == before, "missing separate monitor key never falls back to inference key");
try { await adapter.FetchAsync(account, new("synthetic-inference", monitor: new("api-token", "synthetic-monitor", "demo", clock.GetUtcNow().AddSeconds(-1)))); throw new Exception("Expected expiry"); }
catch (QuotaFetchException) { Check(http.Calls.Count == before, "expired monitor never falls back"); }

foreach (var body in new[] {
    "{}", "{\"quotas\":[]}",
    """{"quotas":[{"name":"accounts/other/quotas/monthly-spend-usd","value":"100","usage":1}]}""",
    """{"quotas":[{"name":"accounts/demo/quotas/monthly-spend-usd","value":"1.5","usage":null}]}""",
    """{"quotas":[{"name":"accounts/demo/quotas/monthly-spend-usd","value":"9007199254740993","usage":"NaN"}]}"""
})
{
    http.Respond = (_, _) => Task.FromResult(Json(body));
    try { Check((await adapter.FetchAsync(account, credential)).Readings.Count == 0, "unrecognized/mismatched/unsafe values never become quota"); }
    catch (QuotaFetchException) { Check(true, "malformed schema rejected"); }
}
foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests })
{
    http.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("synthetic-inference body") });
    try { await adapter.FetchAsync(account, credential); throw new Exception("Expected HTTP rejection"); }
    catch (QuotaFetchException e) { Check(e.Message == $"Quota HTTP {(int)status}.", "401/403/429 sanitized"); }
}
http.Respond = (_, _) => Task.FromResult(Json("""{"quotas":[{"name":"accounts/demo/quotas/monthly-spend-usd","maxValue":"500"}]}"""));
var ceilingOnly = (await adapter.FetchAsync(account, credential)).Readings.Single();
Check(ceilingOnly.ApprovedLimit == 500 && ceilingOnly.Limit is null && ceilingOnly.Used is null && ceilingOnly.Remaining is null, "approved ceiling alone never becomes enforceable or available quota");
http.Respond = (_, _) => Task.FromResult(Json("""{"quotas":[{"name":"accounts/demo/quotas/monthly-spend-usd","value":"50","usage":51.5}]}"""));
Check((await adapter.FetchAsync(account, credential)).Readings.Single().Remaining == -1.5, "observed overspend is preserved");
var pageCalls = 0;
http.Respond = (request, _) =>
{
    pageCalls++;
    if (pageCalls == 1) return Task.FromResult(Json("""{"quotas":[{"name":"accounts/demo/quotas/monthly-spend-usd","value":"50","usage":5}],"nextPageToken":"opaque/+?next"}"""));
    Check(request.RequestUri!.Query == "?pageSize=200&pageToken=opaque%2F%2B%3Fnext", "page token escaped within fixed endpoint");
    return Task.FromResult(Json("""{"quotas":[{"name":"accounts/demo/quotas/second-limit","value":"10"}]}"""));
};
Check((await adapter.FetchAsync(account, credential)).Readings.Count == 2 && pageCalls == 2, "all list pages consumed without inference");
http.Respond = (_, _) => Task.FromResult(Json("""{"quotas":[{"name":"accounts/demo/quotas/monthly-spend-usd","value":"50"}],"nextPageToken":"repeat"}"""));
var repeated = await adapter.FetchAsync(account, credential);
Check(repeated.Readings.Count == 1 && repeated.Error is not null, "duplicate resources and repeated pagination bounded");

pageCalls = 0;
http.Respond = (_, _) => Task.FromResult(++pageCalls == 1
    ? Json("""{"quotas":[{"name":"accounts/demo/quotas/monthly-spend-usd","value":"50","usage":5}],"nextPageToken":"next"}""")
    : new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("synthetic-inference") });
var partial = await adapter.FetchAsync(account, credential);
Check(partial.Readings.Single().Used == 5 && partial.Error == "Quota HTTP 429.", "later-page failure preserves independent usable rows as partial");

http.Respond = (_, _) => Task.FromResult(Json(Fixture("mixed")));
var service = new QuotaService(new Accounts([account, account with { Id = "local-b" }]), new Credentials(credential), [adapter], clock);
var snapshots = await service.RefreshAsync();
Check(snapshots.Where(r => r.AccountId == "local-a").All(r => r.BudgetPoolId is null), "service never merges quota resources into account budget pool");
var lastGood = snapshots.Single(r => r.AccountId == "local-a" && r.Source.EndsWith("/monthly-spend-usd", StringComparison.Ordinal)).LastSuccessAt;
clock.Advance(TimeSpan.FromMinutes(6));
Check((await service.GetSnapshotsAsync()).All(r => r.State == "stale"), "service freshness independent of provider updateTime");
http.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("synthetic-inference") });
snapshots = await service.RefreshAsync("local-a");
var stale = snapshots.Single(r => r.AccountId == "local-a" && r.Source.EndsWith("/monthly-spend-usd", StringComparison.Ordinal));
Check(stale.State == "stale" && stale.Used == 12.5 && stale.LastSuccessAt == lastGood && stale.Error == "Quota HTTP 403.", "403 retains last good account-specific snapshot");
Check(snapshots.Where(r => r.AccountId == "local-b").All(r => r.Error is null), "one account refresh does not fail another account");
http.Respond = (_, _) => throw new TaskCanceledException("synthetic-inference timeout");
var timedOut = await service.RefreshAsync("local-a");
Check(timedOut.Single(r => r.AccountId == "local-a" && r.Source.EndsWith("/monthly-spend-usd", StringComparison.Ordinal)) is { State: "stale", Used: 12.5, Error: "Quota refresh failed." }, "upstream timeout retains last good data with sanitized error");
var entered = new TaskCompletionSource();
http.Respond = async (_, ct) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); return Json("{}"); };
using var cancel = new CancellationTokenSource();
var refresh = service.RefreshAsync("local-a", cancel.Token);
await entered.Task;
cancel.Cancel();
try { await refresh; throw new Exception("Expected cancellation"); } catch (OperationCanceledException) { }
Check((await service.GetSnapshotsAsync()).Single(r => r.AccountId == "local-a" && r.Source.EndsWith("/monthly-spend-usd", StringComparison.Ordinal)).LastSuccessAt == lastGood, "cancellation preserves previous successful observation");
Check(http.Calls.All(c => c.Method == HttpMethod.Get && c.Url.StartsWith("https://api.fireworks.ai/v1/accounts/", StringComparison.Ordinal)), "quota transport never invokes inference, deployment creation, billing purchase or refresh endpoints");
var manifest = ProviderCatalogRegistry.Parse(File.ReadAllText(Path.Combine(root, "src/Lain.Host/Providers/Catalog/fireworks.json")));
Check(manifest.Id == "fireworks" && manifest.LiveVerification == "not_tested" && manifest.QuotaSupport == "partial", "catalog states provider capabilities separately from live verification");
Check(manifest.Routes.Single().BaseUrl == FireworksQuotaAdapter.ApiBaseUrl && !manifest.Routes.Single().FreeOnlyEligible, "serverless route does not fabricate a free-only guarantee");
Console.WriteLine($"PASS: {assertions} Fireworks assertions; synthetic HTTP only, no live requests.");

static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
sealed class FixtureHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond = respond;
    public List<(string Url, HttpMethod Method, string? Token)> Calls = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    { lock (Calls) Calls.Add((request.RequestUri!.ToString(), request.Method, request.Headers.Authorization?.Parameter)); return Respond(request, ct); }
}
sealed class Clock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.Parse("2026-10-24T12:00:00Z");
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan amount) => now += amount;
}
sealed class Accounts(AccountDto[] accounts) : IQuotaAccountSource
{ public Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AccountDto>>(accounts); }
sealed class Credentials(QuotaCredential credential) : IQuotaCredentialSource
{ public Task<QuotaCredential?> GetCredentialAsync(string accountId, CancellationToken ct = default) => Task.FromResult<QuotaCredential?>(credential); }
