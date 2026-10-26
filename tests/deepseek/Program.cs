using System.Net;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Accounts.Monitoring;
using Lain.Host.Operations;
using Lain.Host.Providers;
using Lain.Host.Quota;

var checks = 0;
void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
JsonElement Json(string body) { using var doc = JsonDocument.Parse(body); return doc.RootElement.Clone(); }
IReadOnlyList<QuotaReading> Read(string body) => DeepSeekQuotaAdapter.Normalize(Json(body));
const string valid = """{"is_available":true,"balance_infos":[{"currency":"CNY","total_balance":"110.00","granted_balance":"10.00","topped_up_balance":"100.00"},{"currency":"USD","total_balance":"0.00000000000000000001","granted_balance":"0.00","topped_up_balance":"0.00"}]}""";
var balances = Read(valid);
Check(balances.Count == 2 && balances.Single(r => r.Unit == "CNY").ExactRemaining == "110.00"
    && balances.Single(r => r.Unit == "USD").ExactRemaining == "0.00000000000000000001", "native currencies preserve exact decimal strings");
Check(balances.All(r => r is { Remaining: null, Used: null, Limit: null, UsedPercent: null, ResetsAt: null,
    ExactUsed: null, ExactLimit: null, BudgetPoolId: null, InheritAccountPool: false, MeasurementKind: "balance", Scope: "account", ScopeId: null }),
    "balance never becomes a limit, percent, reset, aggregate pool or invented identity");
Check(balances.Select(r => r.WindowId).Distinct().Count() == 2, "currency window identities stay separate");
Check(balances.All(r => r.SourceUrl == DeepSeekQuotaAdapter.Endpoint), "readings carry the fixed documented source");
Check(Read("""{"is_available":false,"balance_infos":[{"currency":"USD","total_balance":"0.00"}]}""").Single().ExactRemaining == "0.00",
    "insufficient API-call availability does not erase measured zero");
Check(Read("""{"balance_infos":[{"currency":"USD","total_balance":"-0.01"}]}""").Single().ExactRemaining == "-0.01", "signed provider monetary facts survive");
Check(Read("""{"balance_infos":[{"currency":"CNY","total_balance":"99999999999999999999999999999999999999999999999999999.12345678901234567890"}]}""")
    .Single().ExactRemaining!.EndsWith(".12345678901234567890"), "exact balance is not constrained by floating-point or decimal precision");
foreach (var malformedBody in new[] { "null", "[]", "{}", """{"balance_infos":null}""", """{"balance_infos":{}}""", """{"balance_infos":[]}""" })
    Check(Read(malformedBody).Count == 0, "malformed/missing/empty balances stay unknown: " + malformedBody);
foreach (var value in new[] { "null", "true", "{}", "[]", "12.34", "\"\"", "\"NaN\"", "\"Infinity\"", "\"1e2\"", "\" 1.00\"", "\"1.00 \"", "\"+1.00\"", "\"1,000\"", "\".1\"", "\"1.\"", "\"-\"", "\"١.٠٠\"" })
    Check(Read("{\"balance_infos\":[{\"currency\":\"USD\",\"total_balance\":" + value + "}]}").Count == 0,
        "invalid/non-string decimal stays unknown: " + value);
Check(Read("{\"balance_infos\":[{\"currency\":\"USD\",\"total_balance\":\"" + new string('1', 257) + "\"}]}").Count == 0,
    "oversized decimal is bounded");
Check(Read("""{"balance_infos":[{"currency":"EUR","total_balance":"4"},{"currency":"usd","total_balance":"5"},{"currency":"USD","granted_balance":"4","topped_up_balance":"5"}]}""").Count == 0,
    "unknown currency or missing total never causes conversion, relabeling or inferred addition");
var partial = """{"balance_infos":[null,{"currency":"CNY","total_balance":false},{"currency":"USD","total_balance":"7.00"}]}""";
Check(Read(partial).Single() is { Unit: "USD", ExactRemaining: "7.00" }, "usable currency survives a malformed independent currency");
Check(Read("""{"balance_infos":[{"currency":"USD","total_balance":"7"},{"currency":"USD","total_balance":"8"},{"currency":"CNY","total_balance":"9"}]}""")
    .Single().Unit == "CNY", "duplicate currency invalidates that currency rather than selecting by response order");
var encoded = JsonSerializer.Serialize(balances, new JsonSerializerOptions(JsonSerializerDefaults.Web));
Check(encoded.Contains("0.00000000000000000001") && encoded.Contains("110.00"), "serialized contract retains exact money");

var body = valid;
var status = HttpStatusCode.OK;
var calls = 0;
var expectedKey = "fixture-deepseek-key";
using var handler = new FakeHandler(request =>
{
    calls++;
    Check(request.Method == HttpMethod.Get && request.RequestUri?.AbsoluteUri == DeepSeekQuotaAdapter.Endpoint,
        "only the fixed read-only balance request is sent");
    Check(request.Headers.Authorization?.ToString() == "Bearer " + expectedKey, "selected account credential is used");
    Check(request.Headers.UserAgent.ToString() == "lain/1.0" && !request.Headers.Contains("ChatGPT-Account-Id"), "honest client identity without foreign account headers");
    return new(status) { Content = new StringContent(body) };
});
using var client = new HttpClient(handler);
var adapter = new DeepSeekQuotaAdapter(client);
var account = new AccountDto("fixture-local-account", "Chosen name", "deepseek", true, "api-key", "unverified", Binding: new("deepseek"));
QuotaCredential Credential(string? endpoint = DeepSeekQuotaAdapter.ApiBaseUrl) => new(expectedKey, baseUrl: endpoint, credentialKind: "api-key");
var fetched = await adapter.FetchAsync(account, Credential());
Check(fetched.Error is null && fetched.Readings.Count == 2 && calls == 1, "complete same-key read succeeds without retries");
body = partial;
var partialResult = await adapter.FetchAsync(account, Credential());
Check(partialResult.Readings.Count == 1 && partialResult.Error?.Contains("partially unknown") == true, "partial read carries usable facts and fixed diagnostic");
foreach (var malformed in new[] { "{", "null", "{}", """{"balance_infos":[]}""" })
{
    body = malformed;
    try { var result = await adapter.FetchAsync(account, Credential()); Check(result.Readings.Count == 0 && result.Error is not null, "schema failure stays unknown"); }
    catch (Exception ex) { Check(ex.Message == "Quota response failed (invalid-response).", "invalid JSON has sanitized diagnostic"); }
}
status = HttpStatusCode.Unauthorized;
body = "private-provider-response-fixture";
try { await adapter.FetchAsync(account, Credential()); throw new Exception("Expected 401"); }
catch (Exception ex) { Check(ex.Message == "Quota HTTP 401 (authentication)." && !ex.ToString().Contains(body), "401 never leaks provider text or credential"); }
status = HttpStatusCode.OK;
body = valid;
var beforeRejected = calls;
foreach (var endpoint in new string?[] { null, "https://other.example.test", "http://api.deepseek.com", "https://api.deepseek.com.evil.example", "https://api.deepseek.com/v1", "https://api.deepseek.com/beta", "https://api.deepseek.com?account=x", "https://api.deepseek.com/#x", "https://user@api.deepseek.com", "https://api.deepseek.com:444" })
    await Rejected(account, Credential(endpoint));
foreach (var binding in new[] { new ResourceBindingDto("other"), new("deepseek", Subject: "asserted-user"), new("deepseek", ResourceId: "asserted-user"), new("deepseek", Product: "other"), new("deepseek", Region: "cn"), new("deepseek", ResourceType: "project") })
    await Rejected(account with { Binding = binding }, Credential());
await Rejected(account with { Provider = "other" }, Credential());
await Rejected(account, new("fixture-key", baseUrl: DeepSeekQuotaAdapter.ApiBaseUrl, credentialKind: "access-token-snapshot"));
await Rejected(account, new("", baseUrl: DeepSeekQuotaAdapter.ApiBaseUrl));
await Rejected(account, new("fixture-key", providerAccountId: "asserted-id", baseUrl: DeepSeekQuotaAdapter.ApiBaseUrl));
await Rejected(account, new("fixture-key", baseUrl: DeepSeekQuotaAdapter.ApiBaseUrl, settings: new Dictionary<string, string> { ["accountId"] = "other" }));
Check(calls == beforeRejected, "invalid endpoint/type/provider/resource fails before sending credentials");
await adapter.FetchAsync(account, new(expectedKey, baseUrl: DeepSeekQuotaAdapter.ApiBaseUrl + "/", settings: new Dictionary<string, string> { ["billingPlan"] = "payg" }));
Check(adapter.Supports("DEEPSEEK") && !adapter.Supports("deepseek-via-gateway"), "provider boundary is explicit");
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    try { await adapter.FetchAsync(account, Credential(), cancelled.Token); throw new Exception("Expected cancellation"); }
    catch (OperationCanceledException) { Check(true, "caller cancellation propagates"); }
}

var root = Path.Combine(Environment.GetEnvironmentVariable("XDG_CACHE_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"), "lain-deepseek-fixtures-" + Guid.NewGuid().ToString("N"));
try
{
    var paths = new OwnStatePaths(root);
    var repository = new ConfigurationRepository(paths);
    var secrets = new SecretStore(paths);
    var catalog = new ProviderCatalogRegistry();
    var descriptor = catalog.Find("deepseek")!;
    Check(descriptor is { AdapterState: "available", LiveVerification: "not_tested" } && descriptor.Routes.Select(r => r.Model).SequenceEqual(new[] { "deepseek-flash", "deepseek-v4-pro" }), "catalog offers only documented current canonical models");
    var connection = descriptor.Connections!.Single();
    Check(connection.BaseUrl == DeepSeekQuotaAdapter.ApiBaseUrl && connection.Protocol == "openai-chat", "one existing OpenAI-compatible transport serves optional models");
    var accounts = new AccountService(repository, secrets, catalog);
    var created = accounts.ConnectCredential(repository.Snapshot.Revision, new(new("deepseek", "api-key", ["inference", "models", "quota"], Name: "My DeepSeek", Binding: new("deepseek")), expectedKey, BaseUrl: DeepSeekQuotaAdapter.ApiBaseUrl));
    var saved = created.Accounts.Single();
    Check(saved.Name == "My DeepSeek" && created.Routes.Length == 0 && created.Profiles.Length == 0, "private account-only connect needs no models or profile");
    Check(!JsonSerializer.Serialize(created).Contains(expectedKey) && !saved.Id.Contains(expectedKey), "public account/configuration never contains API key");
    var source = new HostQuotaSource(repository, secrets, new MonitorAccessService(repository, secrets, new(paths)));
    var resolved = (await source.GetCredentialAsync(saved.Id))!;
    Check(resolved.BindingVersion is not null && resolved.BearerToken == expectedKey, "quota resolves selected account generation");
    Check((await adapter.FetchAsync(saved, resolved)).Readings.Count == 2, "account-only saved key reads user balances");
    var attached = accounts.AttachModels(created.Revision, saved.Id, saved.Generation, connection.Id, [descriptor.Routes[0].Id]);
    var attachedAccount = attached.Accounts.Single();
    Check(attachedAccount.Id == saved.Id && attachedAccount.Name == saved.Name && attached.Routes.Single().UpstreamModel == "deepseek-flash", "optional model attachment preserves local account identity");
    var plan = ProviderTransportValidation.Prepare(catalog, "deepseek", descriptor.Routes[0].Id, DeepSeekQuotaAdapter.ApiBaseUrl)!;
    Check(plan.SourceSection == "openai-compatibility" && plan.Protocol == "openai-chat" && plan.Headers.Count == 0, "existing transport requires no new inference engine or foreign headers");
    var priorVersion = (await source.GetCredentialAsync(saved.Id))!.BindingVersion;
    expectedKey = "fixture-reconnected-key";
    var reconnected = accounts.ConnectCredential(attached.Revision, new(new("deepseek", "api-key", ["quota"], AccountId: saved.Id, ExpectedGeneration: attachedAccount.Generation, Binding: new("deepseek")), expectedKey, BaseUrl: DeepSeekQuotaAdapter.ApiBaseUrl));
    var same = reconnected.Accounts.Single();
    Check(same.Id == saved.Id && same.Name == saved.Name && reconnected.Routes.Single().Id == attached.Routes.Single().Id, "reconnect binds the same stable account and retains routes/name");
    var rotated = (await source.GetCredentialAsync(saved.Id))!;
    Check(rotated.BearerToken == expectedKey && rotated.BindingVersion != priorVersion, "new quota credential cannot reuse old-generation observations");
    await adapter.FetchAsync(same, rotated);
    var committedRevision = repository.Snapshot.Revision;
    try
    {
        accounts.ConnectCredential(committedRevision, new(new("deepseek", "api-key", ["quota"], AccountId: same.Id,
            ExpectedGeneration: same.Generation - 1, Binding: new("deepseek")), "fixture-stale-key", BaseUrl: DeepSeekQuotaAdapter.ApiBaseUrl));
        throw new Exception("Expected stale reconnect rejection");
    }
    catch (InvalidOperationException) { Check(repository.Snapshot.Revision == committedRevision, "stale generation cannot publish a replacement credential"); }
    try
    {
        accounts.ConnectCredential(committedRevision, new(new("other", "api-key", ["quota"], AccountId: same.Id,
            ExpectedGeneration: same.Generation, Binding: new("other")), "fixture-foreign-key", BaseUrl: DeepSeekQuotaAdapter.ApiBaseUrl));
        throw new Exception("Expected foreign provider rejection");
    }
    catch (ArgumentException) { Check(repository.Snapshot.Revision == committedRevision, "foreign-provider reconnect cannot overwrite this account"); }
    Check((await source.GetCredentialAsync(saved.Id))!.BearerToken == expectedKey, "rejected reconnects preserve the selected credential");
    var foreignBefore = calls;
    await Rejected(same with { Binding = new("deepseek", Subject: "unverified-remote-user") }, rotated);
    Check(calls == foreignBefore, "saved-account identity assertions are not silently accepted as verified");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
Console.WriteLine($"DeepSeek: {checks} assertions passed (fake HTTP/private disposable fixtures only).");

async Task Rejected(AccountDto selected, QuotaCredential credential)
{
    try { await adapter.FetchAsync(selected, credential); }
    catch (Exception ex) when (ex.Message.StartsWith("DeepSeek", StringComparison.Ordinal)) { Check(true, "unsupported credential/binding rejected"); return; }
    throw new Exception("Unsupported credential/binding accepted");
}

sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
}
