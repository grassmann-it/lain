using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Providers.NanoGpt;
using Lain.Host.Quota;

var checks = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
Dictionary<string,string> Query(string uri) => new Uri(uri).Query.TrimStart('?').Split('&').Select(x => x.Split('=', 2))
    .ToDictionary(x => Uri.UnescapeDataString(x[0]), x => Uri.UnescapeDataString(x[1]));
LoginOperationContext Context(ResourceBindingDto? binding = null) => new(Guid.NewGuid().ToString("N"), "fixture-account", 4, 2,
    new("nanogpt", "browser", ["inference", "models"], Binding: binding), DateTimeOffset.UtcNow.AddMinutes(2));
async Task Callback(LoginAdapterResult begin, string? state = null, string outcome = "code=fixture-code", bool valid = true)
{
    var query = Query(begin.Url!);
    using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });
    using var response = await client.GetAsync(query["callback_url"] + "?state=" + (state ?? query["state"]) + "&" + outcome);
    Check(response.StatusCode == (valid ? HttpStatusCode.OK : HttpStatusCode.BadRequest), "callback state validation");
    var page = await response.Content.ReadAsStringAsync();
    Check(!page.Contains("fixture-code") && !page.Contains(query["state"]), "callback secrets redacted");
}
async Task<LoginAdapterResult> Poll(NanoGptLoginAdapter adapter, LoginOperationContext context)
{
    for (var i = 0; i < 100; i++)
    {
        var result = await adapter.StatusAsync(context, default);
        if (result.State != "waiting") return result;
        await Task.Delay(5);
    }
    throw new Exception("callback did not settle");
}
var issued = """{"key":"sk-nano-fixture","access_token":"sk-nano-fixture","token_type":"Bearer","scope":"models.read api.use","user_id":"user-fixture"}""";
var provider = new FakeHttp(issued);
var saved = 0;
using (var adapter = new NanoGptLoginAdapter(provider))
{
    var context = Context(new("nanogpt", Subject: "user-fixture")) with
    { CheckpointCredentialAsync = (value, _) => { saved++; Check(value.PreserveForRecovery && value.Purposes.Length == 0, "durable zero-purpose recovery checkpoint"); return Task.CompletedTask; } };
    var begin = await adapter.BeginAsync(context, default);
    var query = Query(begin.Url!);
    Check(begin.Url!.StartsWith(NanoGptLoginAdapter.AuthorizationEndpoint + "?"), "official consent endpoint");
    Check(query["client_name"] == "lain" && !query.ContainsKey("client_id") && !query.ContainsKey("invitation_code"), "honest public shortcut identity, no referral");
    Check(query["scope"] == "api.use models.read" && query["code_challenge_method"] == "S256", "documented scopes and PKCE");
    var callback = new Uri(query["callback_url"]);
    Check(callback.Host == "127.0.0.1" && callback.Port > 1023, "dynamic explicit loopback port");
    await Callback(begin, "invalid", valid: false);
    Check(provider.Calls == 0 && (await adapter.StatusAsync(context, default)).State == "waiting", "invalid state cannot exchange");
    await Callback(begin);
    var result = await Poll(adapter, context);
    Check(result.State == "prepared" && saved == 1, "issued key checkpoint and preparation");
    Check(result.Credential!.Binding.Subject == "user-fixture" && result.Credential.Binding.Provenance == "observed", "documented optional user identity");
    Check(result.Credential.Purposes.SequenceEqual(new[] { "inference", "models" }) && result.Credential.Owner == "static-key", "no quota or refresh entitlement");
    Check(result.Credential.ExpiresAt is null, "no invented expiry");
    using var body = JsonDocument.Parse(provider.Body!);
    var verifier = body.RootElement.GetProperty("code_verifier").GetString()!;
    var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    Check(verifier.Length is >= 43 and <= 128 && challenge == query["code_challenge"], "S256 verifier matches");
    Check(body.RootElement.GetProperty("grant_type").GetString() == "authorization_code", "correct shortcut exchange");
    Check(provider.Method == HttpMethod.Post && provider.Uri == NanoGptLoginAdapter.TokenEndpoint && provider.Bearer is null, "exchange never uses management or user secrets");
    await adapter.StatusAsync(context, default);
    Check(provider.Calls == 1, "poll never repeats one-time issuance");
    try { await adapter.StatusAsync(context with { AccountGeneration = 3 }, default); throw new Exception("generation accepted"); }
    catch (InvalidOperationException) { checks++; }
}
foreach (var item in new[] {
    ("{\"access_token\":\"sk-nano-fixture\"}", (ResourceBindingDto?)null, "prepared", false),
    ("{\"access_token\":\"sk-nano-fixture\"}", new ResourceBindingDto("nanogpt", Subject: "known"), "recovery-required", false),
    (issued, new ResourceBindingDto("nanogpt", Subject: "different"), "recovery-required", false),
    (issued, (ResourceBindingDto?)null, "recovery-required", true),
    ("{\"access_token\":\"sk-nano-mgmt-fixture\"}", (ResourceBindingDto?)null, "recovery-required", false),
    ("{\"access_token\":\"sk-nano-fixture\",\"scope\":\"usage:read\"}", (ResourceBindingDto?)null, "recovery-required", false),
    ("{\"key\":\"sk-nano-fixture\",\"access_token\":\"sk-nano-other\"}", (ResourceBindingDto?)null, "recovery-required", false),
    ("{malformed", (ResourceBindingDto?)null, "recovery-required", false) })
{
    var fake = new FakeHttp(item.Item1);
    using var adapter = new NanoGptLoginAdapter(fake);
    var context = Context(item.Item2) with { CheckpointCredentialAsync = (_, _) => item.Item4 ? throw new IOException("private secret detail") : Task.CompletedTask };
    var begin = await adapter.BeginAsync(context, default); await Callback(begin);
    var result = await Poll(adapter, context);
    Check(result.State == item.Item3, "identity/scope/checkpoint boundary");
    if (result.State == "recovery-required" && result.Credential is not null) Check(result.Credential.Purposes.Length == 0, "recovery cannot bypass metadata or identity checks");
    if (item.Item1.Contains("mgmt")) Check(result.Credential is null, "management token never adopted for inference");
    if (result.State == "prepared") Check(result.Credential!.Binding.Subject is null && result.Credential.Binding.Provenance == "unverified", "missing identity stays unknown");
    Check(result.Error?.Contains("private secret detail") != true, "no checkpoint exception leakage");
    await adapter.StatusAsync(context, default);
    Check(fake.Calls == 1, "error never retries issuance");
}
foreach (var code in new[] { 302, 401, 429, 503 })
{
    var fake = new FakeHttp("secret provider failure", code);
    using var adapter = new NanoGptLoginAdapter(fake);
    var context = Context(); var begin = await adapter.BeginAsync(context, default); await Callback(begin);
    var result = await Poll(adapter, context);
    Check(result.State == "recovery-required" && !result.Error!.Contains("secret provider"), "exchange error redacted");
}
using (var adapter = new NanoGptLoginAdapter(new FakeHttp(issued)))
{
    var context = Context(); var begin = await adapter.BeginAsync(context, default); await Callback(begin, outcome: "error=access_denied");
    Check((await Poll(adapter, context)).State == "failed", "denied consent");
    var invalid = Context();
    try { await adapter.BeginAsync(invalid with { Request = invalid.Request with { Purposes = ["quota"] } }, default); throw new Exception("quota purpose accepted"); }
    catch (ArgumentException) { checks++; }
}

var fixture = """{"active":true,"state":"active","limits":{"dailyInputTokens":1000000,"weeklyInputTokens":6000000,"dailyImages":10},"dailyInputTokens":{"used":250000,"remaining":750000,"percentUsed":0.25,"resetAt":1788652800000},"weeklyInputTokens":{"used":7500000,"remaining":0,"percentUsed":1.25,"resetAt":1788739200000},"dailyImages":{"used":2,"remaining":8,"percentUsed":0.2,"resetAt":1788652800000},"period":{"currentPeriodEnd":"2026-10-05T00:00:00Z"}}""";
QuotaFetchResult Parse(string json) { using var doc = JsonDocument.Parse(json); return NanoGptQuotaAdapter.Normalize(doc.RootElement); }
var normalized = Parse(fixture);
Check(normalized.Readings.Count == 3 && normalized.Readings[0].UsedPercent == 25 && normalized.Readings[1].UsedPercent == 125, "fraction to percent with overage");
Check(normalized.Readings[0].Remaining == 750000 && normalized.Readings[0].Limit == 1000000, "returned limits and remainder");
Check(normalized.Readings[0].ResetsAt == DateTimeOffset.FromUnixTimeMilliseconds(1788652800000), "milliseconds only");
Check(normalized.Readings.All(x => x.ScopeId is null && !x.InheritAccountPool && x.WindowStart is null), "no invented account ID or quota start");
var degraded = Parse(fixture.Replace("\"used\":250000", "\"degraded\":true,\"used\":250000")).Readings[0];
Check(degraded.Used is null && degraded.Remaining is null && degraded.UsedPercent is null && degraded.Limit == 1000000, "degraded counters unknown, known limit retained");
var nulls = Parse(fixture.Replace("\"used\":250000", "\"used\":null").Replace("\"remaining\":750000", "\"remaining\":null").Replace("\"percentUsed\":0.25", "\"percentUsed\":null")).Readings[0];
Check(nulls.Used is null && nulls.Remaining is null && nulls.UsedPercent is null, "null not inferred from limit");
var inactive = Parse("""{"active":false,"state":"inactive","limits":{},"dailyInputTokens":null,"weeklyInputTokens":null,"dailyImages":null}""");
Check(inactive.Readings.Count == 0 && inactive.Support!.Message.Contains("inactive"), "inactive response not allowance");
var trial = Parse("""{"active":true,"state":"active","limits":{},"usageUnits":"tokens","tokenLimits":{"total":100},"tokens":{"used":20,"remaining":80,"percentUsed":0.2,"resetAt":null}}""");
Check(trial.Readings.Single().WindowId == "tokens" && trial.Readings[0].Limit == 100 && trial.Readings[0].ResetsAt is null, "token trial contract");
foreach (var value in new[] { "\"250000\"", "-1", "1e309", "null" })
    Check(Parse(fixture.Replace("\"used\":250000", "\"used\":" + value)).Readings[0].Used is null, "invalid quantity unknown");
Check(Parse(fixture.Replace("1788652800000", "1e30")).Readings[0].ResetsAt is null, "invalid reset unknown");
var account = new AccountDto("fixture-account", "NanoGPT", "nanogpt", true, "api-key", "configured");
var monitor = new QuotaMonitorCredential("management-token", "sk-nano-mgmt-fixture", "");
var readerHttp = new FakeHttp(fixture);
using (var client = new HttpClient(readerHttp))
{
    var reader = new NanoGptQuotaAdapter(client);
    var missing = await reader.FetchAsync(account, new QuotaCredential("sk-nano-inference"));
    Check(missing.Support!.Kind == "setup-required" && missing.Error is null && readerHttp.Calls == 0, "missing setup no failed provider request");
    await reader.FetchAsync(account, new QuotaCredential("sk-nano-inference", expiresAt: DateTimeOffset.UtcNow.AddDays(-1), baseUrl: "https://foreign.invalid", monitor: monitor));
    Check(readerHttp.Bearer == "sk-nano-mgmt-fixture" && readerHttp.Uri == NanoGptQuotaAdapter.Endpoint && readerHttp.Method == HttpMethod.Get, "only separate management token to fixed selector-free endpoint");
    await reader.FetchAsync(account with { CredentialKind = "monitor-only" }, new QuotaCredential("", monitor: monitor));
    Check(readerHttp.Calls == 2, "standalone monitor");
    foreach (var bad in new[] { new QuotaMonitorCredential("management-token", "sk-nano-inference", ""), new QuotaMonitorCredential("management-token", "sk-nano-mgmt-fixture", "self"), new QuotaMonitorCredential("management-token", "sk-nano-mgmt-fixture", "", DateTimeOffset.UtcNow.AddDays(-1)) })
    {
        try { await reader.FetchAsync(account, new QuotaCredential("", monitor: bad)); throw new Exception("invalid monitor accepted"); }
        catch (QuotaFetchException) { checks++; }
    }
    Check(readerHttp.Calls == 2, "invalid monitor never reaches provider");
}
foreach (var code in new[] { 401, 403, 429, 503 })
{
    using var client = new HttpClient(new FakeHttp("sk-nano-mgmt-private failure", code));
    try { await new NanoGptQuotaAdapter(client).FetchAsync(account, new QuotaCredential("", monitor: monitor)); throw new Exception("HTTP failure accepted"); }
    catch (QuotaFetchException error) { Check(error.Message.Contains(code.ToString()) && !error.Message.Contains("private"), "HTTP error redacted"); }
}
Check(NanoGptCredentialPolicy.IsInferenceKey("sk-nano-fixture") && !NanoGptCredentialPolicy.IsInferenceKey("sk-nano-mgmt-fixture") && !NanoGptCredentialPolicy.IsManagementToken("sk-nano-fixture"), "credential namespaces disjoint");
Check(NanoGptCredentialPolicy.IsInferenceKey("00000000-0000-0000-0000-000000000001") && !NanoGptCredentialPolicy.IsOAuthInferenceKey("00000000-0000-0000-0000-000000000001"), "legacy private UUID keys accepted without relaxing OAuth contract");
using (var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "nanogpt.json"))))
{
    var root = catalog.RootElement;
    Check(root.GetProperty("billingPlans")[0].GetProperty("kind").GetString() == "unknown", "no invented subscription tier");
    Check(root.GetProperty("routes").EnumerateArray().All(x => !x.GetProperty("freeOnlyEligible").GetBoolean() && x.GetProperty("pricingClass").GetString() == "unknown"), "templates do not authorize free usage");
}
Console.WriteLine($"NanoGPT: {checks} checks passed (fake provider HTTP, local callbacks only).");

sealed class FakeHttp(string response, int status = 200) : HttpMessageHandler
{
    public int Calls; public string? Body, Uri, Bearer; public HttpMethod? Method;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++; Uri = request.RequestUri!.AbsoluteUri; Method = request.Method; Bearer = request.Headers.Authorization?.Parameter;
        Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        return new((HttpStatusCode)status) { Content = new StringContent(response) };
    }
}
