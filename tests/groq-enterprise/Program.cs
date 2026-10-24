using System.Net;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Quota;
using Lain.Host.Accounts.Monitoring;
using Lain.Host.Operations;

var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
var sampleAt = DateTimeOffset.Parse("2026-10-25T12:00:00Z");
var clock = new FixtureClock(sampleAt.AddMinutes(1));
var project = "project-one";
var calls = 0;
var status = HttpStatusCode.OK;
var body = Vector([
    Sample(GroqEnterpriseUsageReader.RequestsMetric, "value", "1.5", "200"),
    Sample(GroqEnterpriseUsageReader.RequestsMetric, "timestamp", sampleAt.ToUnixTimeSeconds().ToString(), "200"),
    Sample(GroqEnterpriseUsageReader.InputMetric, "value", "0"),
    Sample(GroqEnterpriseUsageReader.InputMetric, "timestamp", sampleAt.ToUnixTimeSeconds().ToString())
]);
var handler = new FixtureHttp((request, _) =>
{
    calls++;
    Check(request.Method == HttpMethod.Get && request.RequestUri!.GetLeftPart(UriPartial.Path) == GroqEnterpriseUsageReader.Endpoint, "fixed read-only official query endpoint");
    Check(request.Headers.Authorization?.Parameter == "selected-monitor-key", "only explicitly selected monitor identity used");
    var query = Uri.UnescapeDataString(request.RequestUri.Query);
    Check(query.Contains("project_id=\"project-one\"") && query.Contains("timestamp(") && query.Contains("lain_sample_kind"), "project pinned and raw sample timestamps requested");
    Check(!query.Contains("selected-monitor-key") && !query.Contains("unused-inference-key"), "no secret in URL");
    return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
});
using var http = new HttpClient(handler);
var enterprise = new GroqEnterpriseUsageReader(http, clock);
var credential = new QuotaCredential("unused-inference-key", monitor: new("api-token", "selected-monitor-key", project));
var result = await enterprise.FetchAsync(new("unused-inference-key"));
Check(calls == 0 && result.Support?.Kind == "setup-required", "no implicit enterprise probe");
result = await enterprise.FetchAsync(credential);
Check(result.Readings.Count == 2 && result.Error is null, "rate and timestamp vectors joined by metric/model/status");
var requestRate = result.Readings.Single(r => r.Unit == "requests/second");
Check(requestRate.Used == 1.5 && requestRate.MeasurementKind == "rate" && requestRate.Limit is null && requestRate.Remaining is null && requestRate.ResetsAt is null, "fractional rate is not a remaining quota or reset");
Check(requestRate.Scope == "project-model" && requestRate.ScopeId == project && requestRate.Model == "model-one" && requestRate.BudgetPoolId is null, "project/model scope has no inferred shared budget");
Check(requestRate.DataAsOf == sampleAt && requestRate.ObservedAt == sampleAt && requestRate.FreshUntil == sampleAt.AddMinutes(5), "underlying sample time preserved instead of query timestamp");
Check(result.Readings.Single(r => r.Unit == "tokens/second").Used == 0, "observed zero is legitimate");

var account = new AccountDto("account-a", "Groq", "groq", true, "api-key", "ready");
var passive = new FixturePassive();
var combined = new GroqAccountQuotaAdapter(passive, enterprise);
var before = calls;
result = await combined.FetchAsync(account, new("unused-inference-key"));
Check(calls == before && passive.Calls == 1 && result.Readings.Single().Remaining == 10, "no monitor keeps passive-only behavior without provider call");
var service = new QuotaService(new FixtureAccounts(account), new FixtureCredentials(credential), [combined], clock);
var snapshots = await service.RefreshAsync();
Check(snapshots.Length == 3 && snapshots.Single(r => r.MeasurementKind == "rate" && r.Unit == "requests/second").State == "ok", "independent rates coexist with passive rate limits");
clock.Advance(TimeSpan.FromMinutes(6));
snapshots = await service.RefreshAsync();
var old = snapshots.Single(r => r.MeasurementKind == "rate" && r.Unit == "requests/second");
Check(old.State == "stale" && old.DataAsOf == sampleAt && old.LastSuccessAt == sampleAt, "polling an unchanged exported sample cannot rejuvenate it");
foreach (var failure in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests })
{
    status = failure;
    body = "private provider response must never be exposed";
    snapshots = await service.RefreshAsync();
    old = snapshots.Single(r => r.MeasurementKind == "rate" && r.Unit == "requests/second");
    Check(old.Used == 1.5 && old.State == "stale" && !old.Error!.Contains("private"), "failure preserves last-good rates with sanitized status");
    Check(snapshots.Single(r => r.Unit == "requests").Remaining == 10, "enterprise failure retains usable passive observation");
}
status = HttpStatusCode.OK;
body = Vector([]);
snapshots = await service.RefreshAsync();
Check(snapshots.Any(r => r.MeasurementKind == "rate" && r.Used == 1.5 && r.State == "stale"), "empty metrics response does not silently drop older rates");
result = await enterprise.FetchAsync(credential);
Check(result.Readings.Count == 0 && result.Support?.Kind == "unavailable", "empty series is not observed zero");

foreach (var malformed in new[]
{
    Vector([Sample(GroqEnterpriseUsageReader.InputMetric, "value", "12")]),
    Vector([Sample(GroqEnterpriseUsageReader.InputMetric, "value", "12"), Sample(GroqEnterpriseUsageReader.InputMetric, "timestamp", sampleAt.ToUnixTimeSeconds().ToString())]).Replace("project-one", "wrong-project"),
    Vector([Sample(GroqEnterpriseUsageReader.InputMetric, "value", "NaN"), Sample(GroqEnterpriseUsageReader.InputMetric, "timestamp", sampleAt.ToUnixTimeSeconds().ToString())]),
    Vector([Sample(GroqEnterpriseUsageReader.InputMetric, "value", "12"), Sample(GroqEnterpriseUsageReader.InputMetric, "value", "13"), Sample(GroqEnterpriseUsageReader.InputMetric, "timestamp", sampleAt.ToUnixTimeSeconds().ToString())]),
    Vector([Sample(GroqEnterpriseUsageReader.InputMetric, "value", "12"), Sample(GroqEnterpriseUsageReader.InputMetric, "timestamp", clock.GetUtcNow().AddHours(1).ToUnixTimeSeconds().ToString())])
})
{
    using var document = JsonDocument.Parse(malformed);
    result = GroqEnterpriseUsageReader.Normalize(document.RootElement, project, clock.GetUtcNow());
    Check(result.Readings.Count == 0 && result.Error is not null, "missing, mismatched, duplicate, invalid or future sample is rejected");
}
before = calls;
result = await enterprise.FetchAsync(new("unused", monitor: new("api-token", "secret", "project\"} or up")));
Check(result.Support?.Kind == "setup-required" && calls == before, "query injection resource never leaves machine");
var expired = new QuotaCredential("unused", monitor: new("api-token", "expired-secret", project, clock.GetUtcNow().AddSeconds(-1)));
try { await enterprise.FetchAsync(expired); throw new Exception("expired credential accepted"); }
catch (QuotaFetchException) { Check(calls == before, "expired credential is not transmitted"); }
using var cancel = new CancellationTokenSource();
cancel.Cancel();
try { await enterprise.FetchAsync(credential, cancel.Token); throw new Exception("cancellation ignored"); }
catch (OperationCanceledException) { checks++; }
var temporary = Path.Combine(Path.GetTempPath(), "lain-groq-monitor-" + Guid.NewGuid().ToString("N"));
try
{
    var paths = new OwnStatePaths(temporary);
    var repository = new ConfigurationRepository(paths);
    var secrets = new SecretStore(paths);
    var store = new MonitorCredentialStore(paths);
    var access = new MonitorAccessService(repository, secrets, store, clock);
    repository.Update(0, state => state with { Accounts = [account] });
    secrets.WriteAccount(account.Id, JsonSerializer.Serialize(new { kind = "api-key", apiKey = "fixture-inference-key", settings = new { projectId = project } }));
    Check(access.Get(account.Id).State == "not-configured", "Enterprise monitor remains opt-in");
    Check(MonitorAccessCatalog.Options("groq").All(o => o.Kind is "inference-key" or "api-token"), "no guessed Groq OAuth flow exposed");
    try { access.Set(repository.Snapshot.Revision, account.Id, new("api-token", "fixture-key", "wrong-project")); throw new Exception("project mismatch accepted"); }
    catch (ArgumentException) { checks++; }
    access.Set(repository.Snapshot.Revision, account.Id, new("inference-key", ResourceId: project));
    Check(access.GetCredential(account.Id) is { Kind: "inference-key", Secret: null }, "reuse selects the current account key without copying a second identity");
    Check(repository.Snapshot.Accounts.Single().Binding is { ResourceType: "project", ResourceId: "project-one" }, "Groq monitor resource binds a project");
    access.Set(repository.Snapshot.Revision, account.Id, new("api-token", "fixture-separate-key", project));
    Check(access.Get(account.Id).State == "configured-unverified", "saving a key does not claim Enterprise entitlement");
    Check(access.GetCredential(account.Id)?.Secret == "fixture-separate-key", "explicit separate monitor stays separate");
    Check(!JsonSerializer.Serialize(access.Get(account.Id)).Contains("fixture-separate-key"), "public setup result never exposes credentials");
    try { access.Set(repository.Snapshot.Revision, account.Id, new("api-token", "fixture-key", "changed-project")); throw new Exception("bound project mismatch accepted"); }
    catch (ArgumentException) { checks++; }
}
finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
Console.WriteLine($"Groq Enterprise fixtures passed ({checks} assertions). No live provider requests.");

object Sample(string metric, string lane, string value, string? statusCode = null)
{
    var labels = new Dictionary<string, string> { ["lain_metric"] = metric, ["lain_sample_kind"] = lane, ["project_id"] = project, ["model"] = "model-one" };
    if (lane == "value") labels["__name__"] = metric;
    if (statusCode is not null) labels["status_code"] = statusCode;
    // Deliberately newer evaluation timestamp proves timestamp(value) is needed.
    return new { metric = labels, value = new object[] { clock.GetUtcNow().ToUnixTimeSeconds(), value } };
}
string Vector(object[] samples) => JsonSerializer.Serialize(new { status = "success", data = new { resultType = "vector", result = samples } });
sealed class FixtureClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan by) => now += by;
}
sealed class FixtureHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request, cancellationToken);
}
sealed class FixturePassive : IQuotaAdapter
{
    public int Calls { get; private set; }
    public string Source => "groq:response-headers";
    public bool RequiresCredential => false;
    public bool Supports(string provider) => provider == "groq";
    public Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(new QuotaFetchResult([new(Source, "requests", Remaining: 10)], Support: new("passive", "Saved response headers.")));
    }
}
sealed class FixtureAccounts(AccountDto account) : IQuotaAccountSource
{
    public Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AccountDto>>([account]);
}
sealed class FixtureCredentials(QuotaCredential credential) : IQuotaCredentialSource
{
    public Task<QuotaCredential?> GetCredentialAsync(string accountId, CancellationToken ct = default) => Task.FromResult<QuotaCredential?>(credential);
}
