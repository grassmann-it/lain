using System.Net;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Accounts.Cloud;
using Lain.Host.Accounts.Credentials;
using Lain.Host.Quota;

var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
async Task Reject(Func<Task> action, string name)
{
    try { await action(); } catch (Exception ex) when (ex is InvalidDataException or CloudProviderException or ArgumentException || ex.GetType().FullName == "Lain.Host.Quota.QuotaFetchException") { checks++; return; }
    throw new Exception(name);
}
var now = DateTimeOffset.Parse("2026-03-29T16:00:00Z");
var clock = new Clock(now);
var account = new AccountDto("local-google", "Fixture", "gemini-api", true, "cloud-oauth", "ready",
    Binding: new("gemini-api", Subject: "123456", ResourceType: "project", ResourceId: "project-one"));
JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
var projectRows = new { projects = new[] { new { name = "projects/123456", projectId = "project-one", state = "ACTIVE" }, new { name = "projects/654321", projectId = "project-two", state = "ACTIVE" } } };
using (var handler = new Handler((r, i) =>
{
    Check(r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath == "/v3/projects:search", "project discovery uses fixed read endpoint");
    return Handler.Response(projectRows);
}))
using (var http = new CloudProviderHttp(handler))
{
    var projects = await new CloudResourceDiscovery(http).GoogleProjectsAsync("fixture-access");
    Check(projects.Length == 2 && projects[0].Subject == "123456" && projects[1].ResourceId == "project-two", "both project identities preserved");
}
using (var http = new CloudProviderHttp(new Handler((r, i) => Handler.Response(new { nextPageToken = "again" }))))
    await Reject(() => new CloudResourceDiscovery(http).GoogleProjectsAsync("fixture-access"), "repeated project cursor rejected");
using (var http = new CloudProviderHttp(new Handler((r, i) => Handler.Response(new { projects = new[] { new { name = "projects/not-number", projectId = "project-one", state = "ACTIVE" } } }))))
    await Reject(() => new CloudResourceDiscovery(http).GoogleProjectsAsync("fixture-access"), "invalid project number rejected");
const string cfId = "0123456789abcdef0123456789abcdef";
using (var http = new CloudProviderHttp(new Handler((r, i) => Handler.Response(new { success = true, result = new[] { new { id = cfId } }, result_info = new { page = 1, total_pages = 1 } }))))
{
    var resources = await new CloudResourceDiscovery(http).CloudflareAccountsAsync("fixture-access");
    Check(resources.Length == 1 && resources[0].Subject == cfId && resources[0].ResourceId == cfId, "Cloudflare account pinned");
}
using (var http = new CloudProviderHttp(new Handler((r, i) => new(HttpStatusCode.Forbidden) { Content = new StringContent("secret-provider-body") })))
{
    try { await new CloudResourceDiscovery(http).GoogleProjectsAsync("fixture-access"); throw new Exception("403 expected"); }
    catch (CloudProviderException ex) { Check(ex.Message.Contains("permission", StringComparison.OrdinalIgnoreCase) && !ex.Message.Contains("secret"), "permission action safe and distinct"); }
}

var start = GeminiModelQuotaAdapter.PacificDayStart(now);
Check(start == DateTimeOffset.Parse("2026-03-29T07:00:00Z"), "Pacific daylight time midnight");
Check(GeminiModelQuotaAdapter.PacificDayStart(DateTimeOffset.Parse("2026-01-15T12:00:00Z")) == DateTimeOffset.Parse("2026-01-15T08:00:00Z"), "Pacific standard time midnight");
const string family = "generate_content_free_tier_requests";
object Series(string project = "123456", string model = "same-model", long value = 7, string method = "GenerateContent", DateTimeOffset? from = null, DateTimeOffset? to = null) => new
{
    metric = new { type = "generativelanguage.googleapis.com/quota/" + family + "/usage", labels = new { model, limit_name = "requests-per-minute", method } },
    resource = new { type = "generativelanguage.googleapis.com/Location", labels = new { resource_container = "projects/" + project, location = "global" } },
    metricKind = "DELTA", valueType = "INT64",
    points = new[] { new { interval = new { startTime = (from ?? now.AddMinutes(-5)).ToString("O"), endTime = (to ?? now.AddMinutes(-4)).ToString("O") }, value = new { int64Value = value.ToString() } } }
};
var usagePage = Json(new { timeSeries = new[] { Series(), Series(), Series("654321"), Series(method: "StreamGenerateContent", value: 0) } });
var usage = GeminiModelQuotaAdapter.NormalizeUsage([usagePage], "project-one", "123456", family, start, now);
Check(usage.Readings.Count == 2 && usage.Readings.Sum(r => r.Used) == 7, "duplicate points deduped, foreign project excluded, method scope separate");
Check(usage.Readings.Any(r => r.Used == 0), "measured zero retained");
Check(usage.Readings.All(r => r.Limit is null && r.Remaining is null && r.UsedPercent is null && r.ResetsAt is null), "no invented usage-limit join or minute reset");
Check(usage.Error is not null && usage.Readings.All(r => r.DataAsOf == now.AddMinutes(-4) && r.FreshUntil is null), "partial and delayed telemetry remains explicit");
var conflict = GeminiModelQuotaAdapter.NormalizeUsage([Json(new { timeSeries = new[] { Series(), Series(value: 8) } })], "project-one", "123456", family, start, now);
Check(conflict.Readings.Count == 0 && conflict.Error is not null, "conflicting duplicate not summed");
var overlap = GeminiModelQuotaAdapter.NormalizeUsage([Json(new { timeSeries = new[] { Series(), Series(from: now.AddMinutes(-4.5), to: now.AddMinutes(-3.5)) } })], "project-one", "123456", family, start, now);
Check(overlap.Readings.Count == 0, "overlapping DELTA points excluded");
Check(GeminiModelQuotaAdapter.NormalizeUsage([Json(new { })], "project-one", "123456", family, start, now).Readings.Count == 0, "missing model series unknown");
object Limits(string project = "123456") => new
{
    quotaInfos = new[] { new { name = $"projects/{project}/locations/global/services/generativelanguage.googleapis.com/quotaInfos/requests-minute", quotaId = "requests-minute", service = "generativelanguage.googleapis.com", metric = "generativelanguage.googleapis.com/generate_content_free_tier_requests", refreshInterval = "minute", metricUnit = "1/min/{project}/{model}", dimensionsInfos = new[] { new { dimensions = new { model = "same-model", location = "global" }, details = new { value = "25" } } } } }
};
var limits = GeminiModelQuotaAdapter.NormalizeLimits([Json(Limits())], "project-one", "123456", now);
Check(limits.Length == 1 && limits[0].Limit == 25 && limits[0].Used is null && limits[0].Remaining is null && limits[0].ResetsAt is null, "effective limit separate from telemetry");
await Reject(() => Task.FromResult(GeminiModelQuotaAdapter.NormalizeLimits([Json(Limits("654321"))], "project-one", "123456", now)), "foreign quota project rejected");
using (var adapter = new GeminiModelQuotaAdapter(new Handler((r, i) => r.RequestUri!.Host == "cloudquotas.googleapis.com"
    ? new(HttpStatusCode.Forbidden) : Handler.Response(i == 0 ? new { timeSeries = new[] { Series() } } : new { timeSeries = Array.Empty<object>() })), clock))
{
    var result = await adapter.FetchAsync(account, new("fixture-access", settings: new Dictionary<string, string> { ["projectId"] = "project-one", ["projectNumber"] = "123456" }, monitor: new("access-token-snapshot", "fixture-access", "project-one", now.AddHours(1))));
    Check(result.Readings.Count == 1 && result.Error!.Contains("cloudquotas.quotas.get"), "missing optional limit permission retains usage");
}

object Cf(double neurons, string id = cfId) => new { data = new { viewer = new { accounts = new[] { new { accountTag = id, aiInferenceAdaptiveGroups = new[] { new { sum = new { totalNeurons = neurons } } } } } } } };
var cfStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
var cf = CloudflareQuotaAdapter.Normalize(Json(Cf(0)), cfId, cfStart, now);
Check(cf.Readings.Single().Used == 0 && cf.Readings.Single().MeasurementKind == "usage-estimate", "Cloudflare measured zero remains estimated telemetry");
Check(cf.Readings.All(r => r.Limit is null && r.Remaining is null && r.ResetsAt is null && r.DataAsOf is null && r.FreshUntil is null), "no 10000-neuron balance or fabricated freshness");
Check(CloudflareQuotaAdapter.Normalize(Json(Cf(5, new string('a', 32))), cfId, cfStart, now).Readings.Count == 0, "Cloudflare account mismatch rejected");
Check(CloudflareQuotaAdapter.Normalize(Json(new { errors = new[] { new { message = "secret upstream body" } }, data = Cf(5) }), cfId, cfStart, now).Readings.Count == 0, "partial GraphQL errors never become total");
using (var handler = new Handler((r, i) => throw new Exception("unqualified candidate must not send")))
using (var adapter = new CloudflareQuotaAdapter(handler: handler, clock: clock))
{
    var result = await adapter.FetchAsync(account with { Provider = "cloudflare-workers-ai" }, new("fixture-token"));
    Check(result.Readings.Count == 0 && handler.Calls == 0, "unqualified Cloudflare candidate gated");
}
using (var handler = new Handler((r, i) =>
{
    Check(r.Method == HttpMethod.Post && r.RequestUri!.AbsoluteUri == "https://api.cloudflare.com/client/v4/graphql", "GraphQL endpoint fixed");
    var body = r.Content!.ReadAsStringAsync().Result;
    Check(body.Contains("totalNeurons") && !body.Contains("modelId") && !body.Contains("10000"), "single aggregate no top-N model summation");
    return Handler.Response(Cf(17));
}))
using (var adapter = new CloudflareQuotaAdapter(true, handler, clock))
{
    var result = await adapter.FetchAsync(account with { Provider = "cloudflare-workers-ai", Binding = new("cloudflare-workers-ai", Subject: cfId, ResourceType: "account", ResourceId: cfId) }, new("fixture-token", settings: new Dictionary<string, string> { ["accountId"] = cfId }));
    Check(result.Readings.Single().Used == 17, "qualified candidate reports telemetry");
}

CloudOAuthClient Client(bool enabled = true) => new("gemini-api", "fixture.apps.googleusercontent.com", [CloudOAuthAdapter.GoogleCloudScope], Qualified: enabled);
LoginOperationContext Context(List<PreparedLoginCredential> checkpoints, string id = "operation1") => new(id, "local-google", 1, 0,
    new("gemini-api", "browser", ["quota"]), now.AddMinutes(5))
{ CheckpointCredentialAsync = (credential, _) => { checkpoints.Add(credential); return Task.CompletedTask; } };
async Task Authorize(CloudOAuthAdapter adapter, LoginOperationContext context, string? callbackError = null)
{
    var begun = await adapter.BeginAsync(context, default); var query = Query(new Uri(begun.Url!).Query);
    Check(query["code_challenge_method"] == "S256" && query["code_challenge"].Length == 43 && query["access_type"] == "offline", "S256 offline consent");
    using var browser = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
    var target = query["redirect_uri"] + "?state=" + Uri.EscapeDataString(query["state"]) + (callbackError is null ? "&code=fixture-code" : "&error=" + callbackError);
    using var result = await browser.GetAsync(target);
    Check(result.StatusCode == HttpStatusCode.OK, "local callback accepted");
}
async Task<LoginAdapterResult> Poll(CloudOAuthAdapter adapter, LoginOperationContext context)
{
    for (var i = 0; i < 100; i++) { var result = await adapter.StatusAsync(context, default); if (result.State != "waiting") return result; await Task.Delay(10); }
    throw new Exception("callback did not complete");
}
var checkpoints = new List<PreparedLoginCredential>(); var context = Context(checkpoints);
using (var handler = new Handler((r, i) =>
{
    if (r.RequestUri!.Host == "oauth2.googleapis.com")
    {
        var body = r.Content!.ReadAsStringAsync().Result;
        Check(body.Contains("code_verifier=") && !body.Contains("client_secret"), "public code exchange has no distributed secret");
        return Handler.Response(new { access_token = "fixture-access", refresh_token = "fixture-refresh", token_type = "Bearer", expires_in = 3600 });
    }
    Check(checkpoints.Count == 1 && checkpoints[0].Purposes.Length == 0, "grant checkpoint precedes discovery without publication rights");
    return Handler.Response(projectRows);
}))
using (var adapter = new CloudOAuthAdapter(Client(), handler, clock))
{
    await Authorize(adapter, context);
    var pick = await Poll(adapter, context);
    Check(pick.State == "resource-required" && pick.Resources!.Length == 2, "OAuth result requires verified project selection");
    var selected = context with { Request = context.Request with { Binding = pick.Resources![1] } };
    var prepared = await adapter.StatusAsync(selected, default);
    Check(prepared.State == "prepared" && prepared.Credential!.Binding.ResourceId == "project-two" && prepared.Credential.Purposes.SequenceEqual(["quota"]), "only selected project monitoring published");
    Check(prepared.Credential!.PrivateJson.Contains("projectNumber") && prepared.Credential.Owner == "google-oauth", "project number and owner persisted privately");
    await adapter.StatusAsync(selected, default);
    Check(handler.Calls == 2, "callback and status replay never repeats exchange");
    using var refresh = new CloudOAuthAdapter(Client(), new Handler((r, i) => Handler.Response(new { access_token = "renewed-access", refresh_token = "rotated-refresh", token_type = "Bearer", expires_in = 3600, scope = "narrowed" })), clock);
    var credential = prepared.Credential;
    var rotated = await refresh.RefreshAsync(new("local-google", new("handle", 1, credential.Owner, credential.Purposes, credential.Binding), credential.PrivateJson), default);
    Check(rotated.Purposes.Length == 0 && rotated.PrivateJson.Contains("rotated-refresh") && !rotated.PrivateJson.Contains("fixture-refresh"), "narrowed rotated grant recoverable, never old token rollback");
    Check(!rotated.ToString().Contains("rotated-refresh"), "grant diagnostics redacted");
    await adapter.ReleaseAsync(selected, default);
}
using (var handler = new Handler((r, i) => throw new Exception("unconfigured client sent network request")))
using (var adapter = new CloudOAuthAdapter(Client(false), handler, clock))
{
    Check((await adapter.BeginAsync(Context([]), default)).State == "failed" && handler.Calls == 0, "maintainer prerequisite gate");
}
foreach (var providerOnlyBinding in new[] { false, true })
{
    var saved = new List<PreparedLoginCredential>();
    var singleton = Context(saved, providerOnlyBinding ? "singleton-provider" : "singleton-unbound");
    if (providerOnlyBinding) singleton = singleton with { Request = singleton.Request with { Binding = new("gemini-api") } };
    using var handler = new Handler((request, _) => request.RequestUri!.Host == "oauth2.googleapis.com"
        ? Handler.Response(new { access_token = "fixture-access", refresh_token = "fixture-refresh", token_type = "Bearer", expires_in = 3600 })
        : Handler.Response(new { projects = new[] { new { name = "projects/123456", projectId = "project-one", state = "ACTIVE" } } }));
    using var adapter = new CloudOAuthAdapter(Client(), handler, clock);
    await Authorize(adapter, singleton);
    var prepared = await Poll(adapter, singleton);
    Check(prepared.State == "prepared" && prepared.Credential!.Binding.ResourceId == "project-one" &&
        prepared.Credential.Binding.Subject == "123456" && saved.Count == 1,
        "single verified resource auto-selects with " + (providerOnlyBinding ? "provider-only binding" : "no binding"));
}
using (var handler = new Handler((request, _) => request.RequestUri!.Host == "oauth2.googleapis.com"
    ? Handler.Response(new { access_token = "fixture-access", refresh_token = "fixture-refresh", token_type = "Bearer", expires_in = 3600 })
    : Handler.Response(projectRows)))
using (var adapter = new CloudOAuthAdapter(Client(), handler, clock))
{
    var multiple = Context([], "multiple-provider");
    multiple = multiple with { Request = multiple.Request with { Binding = new("gemini-api") } };
    await Authorize(adapter, multiple);
    var selection = await Poll(adapter, multiple);
    Check(selection.State == "resource-required" && selection.Resources!.Length == 2 && selection.Credential is null,
        "provider-only binding cannot implicitly select among multiple authorized projects");
}
using (var handler = new Handler((r, i) => throw new Exception("canceled client exchanged code")))
using (var adapter = new CloudOAuthAdapter(Client(), handler, clock))
{
    var cancelled = Context([], "operation2"); await adapter.BeginAsync(cancelled, default); await adapter.CancelAsync(cancelled, default);
    Check((await adapter.StatusAsync(cancelled, default)).State == "cancelled" && handler.Calls == 0, "cancel stops local callback without provider work");
}
var deniedCheckpoint = new List<PreparedLoginCredential>();
using (var handler = new Handler((r, i) => Handler.Response(new { access_token = "fixture-access", refresh_token = "fixture-refresh", token_type = "Bearer", expires_in = 3600, scope = "narrowed" })))
using (var adapter = new CloudOAuthAdapter(Client(), handler, clock))
{
    var denied = Context(deniedCheckpoint, "operation3"); await Authorize(adapter, denied);
    var result = await Poll(adapter, denied);
    Check(result.State == "recovery-required" && handler.Calls == 1 && deniedCheckpoint.Single().Purposes.Length == 0, "narrowed initial grant safely checkpointed without discovery");
}
var configPath = Path.Combine(Path.GetTempPath(), "lain-cloud-maintainer-" + Guid.NewGuid().ToString("N") + ".json");
try
{
    Check(CloudMaintainerConfiguration.Load(configPath).Clients.Length == 0, "missing maintainer file keeps clients unavailable");
    File.WriteAllText(configPath, "{\"clients\":[],\"clientSecret\":\"fixture\"}");
    await Reject(() => Task.FromResult(CloudMaintainerConfiguration.Load(configPath)), "maintainer configuration rejects secret fields");
    File.WriteAllText(configPath, "{\"clients\":[]}");
    Check(!CloudMaintainerConfiguration.Load(configPath).CloudflareNeuronCandidateQualified, "neuron candidate remains gated by default");
}
finally { File.Delete(configPath); }
using (var legacyHttp = new HttpClient(new Handler((r, i) =>
{
    Check(r.RequestUri!.Query.Contains("serviceruntime"), "legacy snapshot retains original request metric");
    return Handler.Response(new { });
})))
using (var selector = new GeminiCloudQuotaAdapter(legacyHttp, new Handler((r, i) => throw new Exception("unverified project selected model reader")), clock))
{
    var result = await selector.FetchAsync(account, new("", monitor: new("access-token-snapshot", "fixture-access", "project-one", now.AddHours(1))));
    Check(result.Readings.Count == 0 && result.Support is not null, "legacy missing series stays unknown");
}
Console.WriteLine($"PASS {checks} cloud connection and quota assertions; synthetic HTTP and isolated loopback only.");

static Dictionary<string, string> Query(string query) => query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));
sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
sealed class Handler(Func<HttpRequestMessage, int, HttpResponseMessage> response) : HttpMessageHandler
{
    public int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request, Calls++));
    public static HttpResponseMessage Response(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json") };
}
