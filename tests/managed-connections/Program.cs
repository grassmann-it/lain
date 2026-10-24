using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Accounts.Managed;
using Lain.Host.Operations;
using Lain.Host.Quota;

var assertions = 0;
void Check(bool condition, string name) { assertions++; if (!condition) throw new Exception(name); }
void Reject(Action action, string name)
{
    try { action(); } catch (Exception e) when (e is ArgumentException or InvalidDataException) { assertions++; return; }
    throw new Exception(name);
}
JsonElement Json(string text) { using var document = JsonDocument.Parse(text); return document.RootElement.Clone(); }
JsonElement Fixture(string name) => Json(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name)));
var now = new DateTimeOffset(2026, 10, 24, 20, 0, 0, TimeSpan.Zero);
var methods = ManagedLoginQualifications.Methods();
Check(methods.Length == 3 && methods.All(m => m.Availability == "unavailable" && !m.RequiresCore && !m.SecretRequired), "missing evidence disables specific managed methods");
Check(methods.All(m => m.Purposes.SequenceEqual(new[] { "quota" }) && !string.IsNullOrWhiteSpace(m.Guidance)), "no inference parity claimed and actionable fallback retained");
IAccountLoginAdapter[] adapters = [new KimiConnectAdapter(), new ZaiConnectAdapter(), new CodexStandaloneConnectAdapter()];
foreach (var adapter in adapters)
{
    var context = new LoginOperationContext("fixture-operation", "fixture-account", 0, 0,
        new(adapter.Provider, adapter.Method, ["quota"]), now.AddMinutes(10));
    var begin = await adapter.BeginAsync(context, default);
    var status = await adapter.StatusAsync(context, default);
    Check(begin.State == "failed" && begin.Credential is null && begin.Url is null && begin.NextAction == "reconnect", adapter.Provider + " cannot launch unqualified login");
    Check(status.State == "failed" && status.Credential is null && !adapter.RequiresCore, adapter.Provider + " cannot adopt or start Core");
    await adapter.CancelAsync(context, default); await adapter.CancelAsync(context, default); await adapter.ReleaseAsync(context, default);
    Check(true, adapter.Provider + " cleanup is idempotent without global logout");
    Reject(() => adapter.BeginAsync(context with { Request = context.Request with { Provider = "another-provider" } }, default).GetAwaiter().GetResult(), "cross-provider contexts rejected");
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    try { await adapter.BeginAsync(context, cancellation.Token); throw new Exception("cancelled operation began"); }
    catch (OperationCanceledException) { assertions++; }
}
var kimi = KimiManagedUsageParser.Parse(Fixture("kimi-usage.json"));
Check(kimi.Readings.Length == 3 && kimi.Readings[0].UsedPercent == 25 && kimi.Readings[1].UsedPercent == 0, "Kimi native windows preserve valid zero and missing month total");
Check(kimi.Readings[0].Remaining == 75 && kimi.Readings[0].ResetsAt?.Offset == TimeSpan.Zero, "Kimi ratio and reset are separate native facts");
Check(kimi.BoosterState == "present" && kimi.Booster?.BalanceCents == 1250 && kimi.Booster.TotalCents == 2000 && kimi.Booster.Currency == "CNY", "nullable booster preserves integer cents and currency without USD conversion");
Check(kimi.Booster?.MonthlyChargeLimitEnabled == true && kimi.Booster.MonthlyUsedCents == 750, "booster monthly charge controls remain distinct");
var absent = KimiManagedUsageParser.Parse(Json("""{"code":0,"data":{"kind":"ok","quota":{"usages":{},"extraUsage":null}}}"""));
Check(absent.Readings.Length == 0 && absent.BoosterState == "absent" && absent.Booster is null, "absent booster is not zero wallet");
var unknown = KimiManagedUsageParser.Parse(Json("""{"code":0,"data":{"kind":"ok","quota":{"usages":{"limit5h":{"usedRatio":2}}}}}"""));
Check(unknown.Readings.Single().UsedPercent is null && unknown.BoosterState == "unknown", "invalid ratio and omitted wallet remain unknown");
foreach (var raw in new[] { """{"code":0,"data":{"kind":"error","message":"synthetic-secret-must-not-leak"}}""", """{"code":401,"data":{"kind":"ok","quota":{"usages":{}}}}""", """{"code":0,"code":1,"data":{}}""", """{"code":0,"data":{"kind":"ok","quota":{"usages":[],"extraUsage":null}}}""" })
    Reject(() => KimiManagedUsageParser.Parse(Json(raw)), "Kimi checks business envelope and kind");
try { KimiManagedUsageParser.Parse(Json("""{"code":0,"data":{"kind":"error","message":"synthetic-secret-must-not-leak"}}""")); }
catch (InvalidDataException e) { Check(!e.Message.Contains("synthetic-secret"), "raw provider errors are not returned"); }
var codex = CodexAppServerQuotaParser.ParseSnapshot(CodexAppServerProtocol.Result(Fixture("codex-rate-limits.json"), 7));
Check(codex.Readings.Length == 3 && codex.Readings[0].UsedPercent == 25, "Codex multibucket view replaces single view without duplicate quotas");
Check(codex.Readings[1].UsedPercent == 0 && codex.Readings[1].Remaining == 100, "Codex zero remains a measured quantity");
Check(codex.Credits.Single().Balance == "12.50" && codex.Credits[0].Unlimited == false, "Codex workspace credits preserve raw balance without invented currency or limit");
Check(codex.Readings.Select(r => r.Source).Distinct().Count() == 3, "Codex bucket/window identity is stable");
var empty = CodexAppServerQuotaParser.Parse(Json("""{"rateLimits":{"primary":{"usedPercent":80}},"rateLimitsByLimitId":{}}"""));
Check(empty.Length == 0, "explicit empty buckets do not revive stale single view");
Check(CodexAppServerQuotaParser.Parse(Json("""{"rateLimits":{"primary":{"usedPercent":101},"secondary":null}}""")).Single().UsedPercent is null, "invalid Codex percent stays unknown");
Reject(() => CodexAppServerQuotaParser.Parse(Json("""{"rateLimitsByLimitId":{"a":{"limitId":"b"}}}""")), "mismatched bucket identity rejected");
Reject(() => CodexAppServerQuotaParser.Parse(Json("""{"rateLimitsByLimitId":{"a":{},"a":{}}}""")), "duplicate bucket identity rejected");
foreach (var method in new[] { "account/logout", "turn/start", "account/login/start", "account/chatgptAuthTokens/refresh", "account/rateLimitResetCredit/consume" })
    Reject(() => CodexAppServerProtocol.Request(1, method), "no unqualified login/inference/write/refresh RPC " + method);
var read = Json(CodexAppServerProtocol.Request(1, "account/read"));
Check(!read.GetProperty("params").GetProperty("refreshToken").GetBoolean(), "account display read never forces token refresh");
Reject(() => CodexAppServerProtocol.Request(1, "account/read", new { refreshToken = true }), "caller cannot escalate read parameters");
Reject(() => CodexAppServerProtocol.Result(Json("""{"id":2,"result":{}}"""), 1), "RPC response correlation required");
Reject(() => CodexAppServerProtocol.Result(Json("""{"id":1,"id":2,"result":{}}"""), 1), "duplicate RPC identity rejected");
Reject(() => CodexAppServerProtocol.Result(Json("""{"id":1,"error":{"message":"synthetic-secret"}}"""), 1), "RPC error not interpreted as success");
Check(CodexAppServerProtocol.HasManagedChatGptAccount(Json("""{"account":{"type":"chatgpt","email":null,"planType":"plus"}}""")), "managed mode recognized without invented subject identity");
Check(!CodexAppServerProtocol.HasManagedChatGptAccount(Json("""{"account":{"type":"apiKey"}}""")), "API key auth is not managed ChatGPT monitoring");
var scope = new ZaiPersonalProjectScope("fixture-account", "fixture-subject", "organization", "project", "generation-1");
using (var request = ZaiKeyDiscovery.CreateReadRequest(scope))
    Check(request.Method == HttpMethod.Get && request.Content is null && request.RequestUri?.AbsolutePath.EndsWith("/api_keys") == true && request.Headers.Authorization is null, "Z.ai discovery constructs only bound GET and has no owner/token side effects");
Reject(() => ZaiKeyDiscovery.CreateReadRequest(scope with { OrganizationId = ".." }), "resource IDs cannot escape the bound request path");
var keys = Json("""{"code":200,"data":[{"apiKey":"existing-key-id","name":"existing","keyType":1}]}""");
var reference = ZaiKeyDiscovery.ParseExisting(keys, scope, "existing-key-id")!;
Check(reference.KeyId == "existing-key-id" && reference.Scope == scope, "apiKey discovery field is only a scoped nonsecret ID");
Check(ZaiKeyDiscovery.ParseExisting(keys, scope, "missing-id") is null, "missing ID does not create a key or choose another entry");
Reject(() => ZaiKeyDiscovery.ParseExisting(Json("""{"code":0,"data":[{"apiKey":"id","keyType":2}]}"""), scope, "id"), "team scope requires separate qualification");
Reject(() => ZaiKeyDiscovery.CreateReadRequest(scope with { Origin = "https://open.bigmodel.cn" }), "CN host cannot inherit international grant");
Reject(() => ZaiKeyDiscovery.ParseExisting(Json("""{"code":0,"data":[{"apiKey":"id"},{"apiKey":"id"}]}"""), scope, "id"), "duplicate ID not silently selected");
string Pat(string token = "synthetic-access", string type = "Bearer", double ttl = 60, double? expiry = null) =>
    JsonSerializer.Serialize(new { code = 0, data = new { enable = true, accessToken = token, tokenType = type, expiresIn = ttl, expiresAt = expiry ?? now.AddHours(1).ToUnixTimeSeconds() } });
Check(ZaiProjectTokenProjection.Parse(Json(Pat().Replace("\"enable\":true,", "", StringComparison.Ordinal)), reference, now, now) is not null, "omitted enable follows the published true default");
var projection = ZaiProjectTokenProjection.Parse(Json(Pat()), reference, now.AddSeconds(-30), now)!;
Check(projection.ExpiresAt == now.AddSeconds(30), "PAT expiry uses earliest absolute or request-start-relative deadline");
Check(projection.AccessToken == "synthetic-access" && !projection.ToString().Contains("synthetic-access"), "short-lived projection remains redacted");
Check(projection.IsCurrent(reference, now) && !projection.IsCurrent(reference with { Scope = scope with { LoginGeneration = "generation-2" } }, now), "projection cannot cross login generation");
Check(!projection.IsCurrent(reference with { Scope = scope with { ProjectId = "another-project" } }, now), "projection cannot cross project scope");
Check(!projection.IsCurrent(reference, now.AddSeconds(30)), "expired projection cannot be used");
Check(ZaiProjectTokenProjection.Parse(Json("""{"code":"200","data":{"enable":false}}"""), reference, now, now) is null, "disabled PAT response grants nothing");
foreach (var raw in new[] { Pat(type: "Basic"), Pat(token: "existing-key-id"), Pat(ttl: -1), Pat(expiry: now.AddSeconds(-1).ToUnixTimeSeconds()), """{"code":0,"data":{"accessToken":"a","accessToken":"b","tokenType":"Bearer","expiresIn":60,"expiresAt":1792886400}}""" })
    Reject(() => ZaiProjectTokenProjection.Parse(Json(raw), reference, now, now), "invalid PAT/key-ID/expiry controls rejected");
var temporary = Path.Combine(Path.GetTempPath(), "lain-managed-fixture-" + Guid.NewGuid().ToString("N"));
try
{
    var paths = new OwnStatePaths(temporary); var repository = new ConfigurationRepository(paths); var secrets = new SecretStore(paths);
    var accounts = new AccountService(repository, secrets);
    await using var service = new LoginOperationService(configuration: repository, accounts: accounts, secrets: secrets, accountAdapters: adapters);
    foreach (var adapter in adapters)
    {
        var state = await service.BeginAsync(repository.Snapshot.Revision, new(adapter.Provider, adapter.Method, ["quota"]));
        Check(state.State == "failed", "shared lifecycle publishes disabled " + adapter.Provider + " method as failed");
    }
    Check(repository.Snapshot.Revision == 0 && repository.Snapshot.Accounts.Length == 0 && repository.Snapshot.Routes.Length == 0, "disabled candidate creates no account, dummy route or credential publication");
    Check(!Directory.Exists(Path.Combine(paths.Credentials, "generations")), "no competing credential store or generation is created");
}
finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
Console.WriteLine($"{assertions} managed connection assertions passed; synthetic fixtures only, no CLI/provider/real cache access.");
