using System.Net;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Quota;

var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
var clock = new FixtureClock(DateTimeOffset.Parse("2026-10-24T12:00:00Z"));
var observedAt = clock.GetUtcNow();
var account = new AccountDto("account-a", "Groq", "groq", true, "api-key", "ready");
var secondAccount = account with { Id = "account-b" };
var json = """{"version":1,"observations":[]}""";
var status = HttpStatusCode.OK;
var coreHttp = new FixtureHttp((request, _) =>
{
    Check(request.Method == HttpMethod.Get && request.RequestUri!.AbsoluteUri == "http://127.0.0.1:18417/lain/quota/observations", "passive fetch uses only local GET endpoint");
    Check(request.Headers.Authorization is null, "adapter never attaches inference key to core");
    return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json) });
});
using var core = new HttpClient(coreHttp) { BaseAddress = new Uri("http://127.0.0.1:18417/") };
var passive = new GroqQuotaAdapter(core, clock);
var service = new QuotaService(new FixtureAccounts([account, secondAccount]), new NoCredentials(), [passive], clock);
var rows = await service.RefreshAsync();
Check(rows.All(r => r.Error is null && r.Support?.Kind == "awaiting-observation"), "no observation is neutral and does not require a key");

json = JsonSerializer.Serialize(new { version = 1, observations = new[] {
    Groq("account-a", "model-one", 8000, 7900), Groq("account-a", "model-two", 4000, 0), Groq("account-b", "model-one", 5000, 4100)
}});
rows = await service.RefreshAsync();
Check(rows.Length == 6, "separate request and token windows per account and model");
var token = rows.Single(r => r.AccountId == "account-a" && r.Model == "model-one" && r.Unit == "tokens");
Check(token.Remaining == 7900 && token.Limit == 8000 && token.ResetsAt == observedAt.AddSeconds(7.66), "real headers parsed with response-relative reset");
Check(token.DataAsOf == observedAt && token.ObservedAt == observedAt && token.FreshUntil == observedAt.AddSeconds(7.66), "source timestamps survive service projection");
Check(token.ScopeId == "account-a" && token.Scope == "organization-model-observed-via-account" && token.BudgetPoolId is null, "no guessed organization pool");
Check(rows.Single(r => r.AccountId == "account-a" && r.Model == "model-two" && r.Unit == "tokens").Remaining == 0, "explicit zero remains visible");
Check(rows.Single(r => r.AccountId == "account-b" && r.Unit == "tokens").Remaining == 4100, "account observations stay isolated");
clock.Advance(TimeSpan.FromSeconds(10));
rows = await service.RefreshAsync();
token = rows.Single(r => r.AccountId == "account-a" && r.Model == "model-one" && r.Unit == "tokens");
Check(token.State == "stale" && token.ObservedAt == observedAt && token.DataAsOf == observedAt, "polling cannot refresh expired passive values");
Check(token.Remaining == 7900, "elapsed reset does not fabricate restored tokens");
foreach (var failure in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests })
{
    status = failure; json = "private upstream text must never surface";
    rows = await service.RefreshAsync("account-a");
    var old = rows.First(r => r.AccountId == "account-a" && r.Unit == "tokens");
    Check(old.State == "stale" && old.Remaining.HasValue && !old.Error!.Contains("private"), "HTTP failure keeps last-good values and sanitized diagnostic");
}
status = HttpStatusCode.OK;
json = JsonSerializer.Serialize(new { version = 1, observations = new[] { Groq("foreign", "model-one", 9000, 8999) } });
var foreign = await passive.FetchAsync(account, new("unused-secret"));
Check(foreign.Readings.Count == 0 && foreign.Error is null, "foreign account snapshot cannot supply values");
using (var invalid = JsonDocument.Parse("""{"version":1,"observations":[{"accountId":"account-a","provider":"groq","model":"x","observedAt":"2026-10-24T12:00:00Z","statusCode":200,"headers":{"x-ratelimit-limit-tokens":["100","200"],"x-ratelimit-remaining-tokens":["NaN"]}}]}"""))
    Check(GroqQuotaAdapter.Normalize(invalid.RootElement, "account-a", clock.GetUtcNow()).Readings.Count == 0, "ambiguous and malformed header values are absent");

var googleAccount = account with { Provider = "gemini-api", Name = "Gemini" };
var monitor = new QuotaMonitorCredential("access-token-snapshot", "synthetic-monitor-token", "project-one", clock.GetUtcNow().AddHours(1));
var credential = new QuotaCredential("inference-key-must-not-be-used", monitor: monitor);
var googleCalls = 0;
var googleStatus = HttpStatusCode.OK;
var googleBody = "";
var paginated = true;
var googleHttp = new FixtureHttp((request, _) =>
{
    googleCalls++;
    Check(request.Method == HttpMethod.Get && request.RequestUri!.Host == "monitoring.googleapis.com" && request.RequestUri.AbsolutePath == "/v3/projects/project-one/timeSeries", "fixed read-only project Monitoring API");
    Check(request.Headers.Authorization?.Parameter == "synthetic-monitor-token", "separate monitoring access token only");
    var query = Uri.UnescapeDataString(request.RequestUri!.Query);
    Check(query.Contains("generativelanguage.googleapis.com") && query.Contains("project-one") && query.Contains("consumed_api"), "query pins service, resource and project");
    var body = paginated ? GooglePage(request.RequestUri.Query.Contains("pageToken=") ? 0 : 7,
        request.RequestUri.Query.Contains("pageToken=") ? null : "next-page") : googleBody;
    return Task.FromResult(new HttpResponseMessage(googleStatus) { Content = new StringContent(body) });
});
using var googleClient = new HttpClient(googleHttp);
var google = new GeminiMonitoringQuotaAdapter(googleClient, clock);
var missing = await google.FetchAsync(googleAccount, new("inference-key"));
Check(missing.Support?.Kind == "setup-required" && missing.Error is null && googleCalls == 0, "missing monitor is neutral and does not send inference key");
var expired = await google.FetchAsync(googleAccount, new("unused", monitor: new("access-token-snapshot", "expired", "project-one", clock.GetUtcNow().AddSeconds(-1))));
Check(expired.Support?.Kind == "setup-required" && googleCalls == 0, "expired snapshot never leaves machine");
var usage = await google.FetchAsync(googleAccount, credential);
var reading = usage.Readings.Single();
Check(googleCalls == 2 && reading.Used == 7 && reading.Limit is null && reading.Remaining is null && reading.ResetsAt is null, "pagination sums distinct intervals without inventing quota");
Check(reading.MeasurementKind == "usage" && reading.ScopeId == "project-one" && reading.Model is null, "usage scope and lack of model evidence preserved");
Check(reading.WindowStart == DateTimeOffset.Parse("2026-10-24T00:00:00Z") && reading.WindowEnd == clock.GetUtcNow() && reading.DataAsOf == DateTimeOffset.Parse("2026-10-24T11:31:00Z"), "UTC usage window differs from returned sample time");
paginated = false; googleBody = GooglePage(0, null);
usage = await google.FetchAsync(googleAccount, credential);
Check(usage.Readings.Single().Used == 0, "observed zero usage remains zero");
googleBody = "{}";
usage = await google.FetchAsync(googleAccount, credential);
Check(usage.Readings.Count == 0 && usage.Support?.Kind == "unavailable" && usage.Error is null, "no exported series is not zero usage");
googleBody = GooglePage(7, null).Replace("project-one", "wrong-project");
usage = await google.FetchAsync(googleAccount, credential);
Check(usage.Readings.Count == 0 && usage.Error is not null, "wrong project cannot become usage");
googleBody = GooglePage(7, null);
var googleService = new QuotaService(new FixtureAccounts([googleAccount]), new FixtureCredentials(credential), [google], clock);
var googleRows = await googleService.RefreshAsync();
Check(googleRows.Single().Used == 7 && googleRows.Single().MeasurementKind == "usage", "HTTP monitoring result reaches public snapshot");
foreach (var failure in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests })
{
    googleStatus = failure; googleBody = "do not expose this credential or provider response";
    googleRows = await googleService.RefreshAsync();
    Check(googleRows.Single().State == "stale" && googleRows.Single().Used == 7 && !googleRows.Single().Error!.Contains("credential"), "monitor failure retains stale observation with sanitized HTTP status");
}
using var cancellation = new CancellationTokenSource();
var entered = new TaskCompletionSource();
googleHttp.Handle = async (_, ct) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(); };
var cancelledRead = googleService.RefreshAsync(ct: cancellation.Token);
await entered.Task; cancellation.Cancel();
try { await cancelledRead; throw new Exception("cancellation must propagate"); } catch (OperationCanceledException) { checks++; }
Check((await googleService.GetSnapshotsAsync()).Single().Used == 7, "cancelled read restores last-good snapshot");
Console.WriteLine($"Quota source integration fixtures passed ({checks} assertions). No live provider calls.");

object Groq(string id, string model, int limit, int remaining) => new {
    accountId = id, provider = "groq", model, requestId = "synthetic-request", observedAt, statusCode = 200,
    headers = new Dictionary<string, string[]> {
        ["x-ratelimit-limit-requests"] = ["1000"], ["x-ratelimit-remaining-requests"] = ["999"], ["x-ratelimit-reset-requests"] = ["1h"],
        ["x-ratelimit-limit-tokens"] = [limit.ToString()], ["x-ratelimit-remaining-tokens"] = [remaining.ToString()], ["x-ratelimit-reset-tokens"] = ["7.66s"]
    }
};
string GooglePage(int used, string? next) => JsonSerializer.Serialize(new {
    nextPageToken = next,
    timeSeries = new[] { new {
        metric = new { type = GeminiMonitoringQuotaAdapter.Metric, labels = new { response_code = "200" } },
        resource = new { type = "consumed_api", labels = new { project_id = "project-one", service = GeminiMonitoringQuotaAdapter.Service, method = "GenerateContent" } },
        metricKind = "DELTA", valueType = "INT64",
        points = new[] { new { interval = new {
            startTime = used == 0 ? "2026-10-24T11:30:00Z" : "2026-10-24T11:29:00Z",
            endTime = used == 0 ? "2026-10-24T11:31:00Z" : "2026-10-24T11:30:00Z"
        }, value = new { int64Value = used.ToString() } } }
    } }
});

sealed class FixtureHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
{
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Handle { get; set; } = handle;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Handle(request, cancellationToken);
}
sealed class FixtureClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan by) => now += by;
}
sealed class FixtureAccounts(IReadOnlyList<AccountDto> accounts) : IQuotaAccountSource
{
    public Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken ct = default) => Task.FromResult(accounts);
}
sealed class NoCredentials : IQuotaCredentialSource
{
    public Task<QuotaCredential?> GetCredentialAsync(string accountId, CancellationToken ct = default) => throw new Exception("Passive source must not read a secret.");
}
sealed class FixtureCredentials(QuotaCredential credential) : IQuotaCredentialSource
{
    public Task<QuotaCredential?> GetCredentialAsync(string accountId, CancellationToken ct = default) => Task.FromResult<QuotaCredential?>(credential);
}
