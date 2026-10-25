using System.Net;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Accounts.Credentials;
using Lain.Host.Accounts.Managed;
using Lain.Host.Accounts.Monitoring;
using Lain.Host.Operations;
using Lain.Host.Quota;

var checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
async Task Reject(Func<Task> operation, string message)
{
    try { await operation(); throw new Exception("Expected rejection: " + message); }
    catch (Exception error) when (error is InvalidDataException or ArgumentException or InvalidOperationException || error.GetType().Name == "QuotaFetchException") { checks++; }
}
var now = DateTimeOffset.UtcNow; // HostQuotaSource intentionally uses the production resolver clock.
const string deviceJson = """{"user_code":"ABCD-EFGH","device_code":"fixture-device-secret","verification_uri":"https://auth.kimi.ai/device","verification_uri_complete":"https://auth.kimi.ai/device?user_code=ABCD-EFGH","expires_in":600,"interval":5}""";
const string meJson = """{"user_id":"fixture-subject","region":"REGION_OVERSEA"}""";
const string usageJson = """{"usages":{"limit_5h":{"used_ratio":0.25,"reset_time":"2026-10-26T03:00:00Z"},"limit_7d":{"used_ratio":0},"limit_month_code":{"used_ratio":2}}}""";
string Token(string suffix) => JsonSerializer.Serialize(new { access_token = "fixture-access-" + suffix, refresh_token = "fixture-refresh-" + suffix, expires_in = 3600, token_type = "Bearer", scope = "coding" });
LoginOperationContext Context(FakeClock clock, ResourceBindingDto? binding = null, Action<PreparedLoginCredential>? checkpoint = null) =>
    new("fixture-" + Guid.NewGuid().ToString("N"), "fixture-account", 0, 0,
        new("kimi", "kimi-device", ["quota"], Binding: binding), clock.GetUtcNow().AddMinutes(10))
    { CheckpointCredentialAsync = (credential, _) => { checkpoint?.Invoke(credential); return Task.CompletedTask; } };

// Real request construction, local poll scheduling, checkpoint-before-identity and public instruction contract.
{
    var clock = new FakeClock(now); var saved = new List<PreparedLoginCredential>();
    using var handler = new Handler();
    handler.Add("/api/oauth/device_authorization", deviceJson);
    handler.Add("/api/oauth/token", """{"error":"authorization_pending","error_description":"fixture-raw-secret"}""", HttpStatusCode.BadRequest);
    handler.Add("/api/oauth/token", """{"error":"slow_down"}""", HttpStatusCode.BadRequest);
    handler.Add("/api/oauth/token", Token("first"));
    handler.Add("/coding/v1/me", meJson, inspect: _ => Check(saved.Count == 1 && saved[0].Purposes.Length == 0, "Issued grant is checkpointed privately before profile reads"));
    using var adapter = new KimiDeviceLoginAdapter(handler, clock);
    var context = Context(clock, checkpoint: saved.Add);
    var regions = await adapter.BeginAsync(context, default);
    Check(regions.State == "resource-required" && regions.Resources!.Select(r => r.Region).SequenceEqual(new[] { "global", "mainland-cn" }) && handler.Calls.Count == 0,
        "Explicit region selection precedes every provider call");
    context = context with { Request = context.Request with { Binding = regions.Resources![0] } };
    var authorization = await adapter.StatusAsync(context, default);
    Check(authorization.State == "authorizing" && authorization.UserCode == "ABCD-EFGH" && authorization.VerificationUri == "https://auth.kimi.ai/device", "Device instructions use bounded public fields");
    await adapter.StatusAsync(context, default);
    Check(handler.Calls.Count == 1, "Early polling performs no request");
    clock.Advance(5); var pending = await adapter.StatusAsync(context, default);
    Check(pending.State == "authorizing" && pending.Error is null, "Pending descriptions never expose raw provider text");
    clock.Advance(5); await adapter.StatusAsync(context, default);
    clock.Advance(9); await adapter.StatusAsync(context, default);
    Check(handler.Calls.Count == 3, "slow_down extends the server poll interval by five seconds");
    clock.Advance(1); var done = await adapter.StatusAsync(context, default);
    Check(done.State == "prepared" && done.Credential?.Owner == "kimi-oauth" && done.Credential.Purposes.SequenceEqual(new[] { "quota" }), "Device flow prepares one own quota-only grant");
    Check(done.Credential!.Binding is { Subject: "fixture-subject", Region: "global", Product: "coding", ResourceId: "fixture-subject" }, "Profile subject and chosen regional endpoint bind the generation");
    Check(handler.Calls.All(call => call.Host is "auth.kimi.ai" or "api.kimi.ai" && call.Platform == "lain" && call.UserAgent.StartsWith("lain/", StringComparison.Ordinal)), "Published client reports honest lain identity on fixed regional hosts");
    Check(handler.Calls[0].Body == "client_id=" + KimiDeviceProtocol.ClientId && handler.Calls[1].Body.Contains("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code"), "Manufacturer form endpoints and grant type are used");
    var count = handler.Calls.Count; await adapter.StatusAsync(context, default);
    Check(handler.Calls.Count == count, "Completed operation does not repeat token issuance");
}

// No ordinary Kimi store/import, no implicit default region, no secret or code through errors.
foreach (var device in new[]
{
    deviceJson.Replace("https://auth.kimi.ai/device", "https://auth.kimi.com/device", StringComparison.Ordinal),
    deviceJson.Replace("ABCD-EFGH", "bad\\ncode", StringComparison.Ordinal),
    deviceJson.Replace("\"interval\":5", "\"interval\":0", StringComparison.Ordinal),
    deviceJson.Replace("\"device_code\":", "\"device_code\":\"duplicate\",\"device_code\":", StringComparison.Ordinal),
    deviceJson.Replace("https://auth.kimi.ai/device", "https://auth.kimi.ai.evil.invalid/device", StringComparison.Ordinal)
})
{
    using var handler = new Handler(); handler.Add("/api/oauth/device_authorization", device);
    using var adapter = new KimiDeviceLoginAdapter(handler, new FakeClock(now));
    var result = await adapter.BeginAsync(Context(new FakeClock(now), new("kimi", Region: "global")), default);
    Check(result.State == "failed" && result.Url is null && result.UserCode is null && !result.Error!.Contains("fixture-device"), "Malformed/cross-region/duplicate device controls fail safely");
}
{
    using var handler = new Handler();
    using var adapter = new KimiDeviceLoginAdapter(handler, new FakeClock(now));
    var context = Context(new FakeClock(now), new("kimi", Region: "global"));
    await Reject(() => adapter.BeginAsync(context with { Request = context.Request with { Purposes = ["inference"] } }, default), "No OAuth inference capability");
    await Reject(() => adapter.BeginAsync(context with { Request = context.Request with { Binding = new("kimi", Region: "another") } }, default), "Unknown regions rejected before network");
    Check(handler.Calls.Count == 0, "Rejected contexts cannot forward tokens or change account state");
}
foreach (var code in new[] { "access_denied", "expired_token" })
{
    var clock = new FakeClock(now); using var handler = new Handler();
    handler.Add("/api/oauth/device_authorization", deviceJson);
    handler.Add("/api/oauth/token", JsonSerializer.Serialize(new { error = code, error_description = "fixture-raw-secret" }), HttpStatusCode.BadRequest);
    using var adapter = new KimiDeviceLoginAdapter(handler, clock);
    var context = Context(clock, new("kimi", Region: "global"));
    await adapter.BeginAsync(context, default); clock.Advance(5);
    var denied = await adapter.StatusAsync(context, default); await adapter.StatusAsync(context, default);
    Check(denied.State == (code == "expired_token" ? "expired" : "failed") && denied.Url is null && denied.UserCode is null
        && !denied.Error!.Contains("fixture-raw-secret") && handler.Calls.Count == 2,
        "Denied/expired grants clear instructions and cannot be polled again");
}
foreach (var seconds in new[] { 10, 600 })
{
    var clock = new FakeClock(now); using var handler = new Handler();
    handler.Add("/api/oauth/device_authorization", deviceJson.Replace("\"expires_in\":600", "\"expires_in\":" + seconds, StringComparison.Ordinal));
    using var adapter = new KimiDeviceLoginAdapter(handler, clock);
    var context = Context(clock, new("kimi", Region: "global"));
    await adapter.BeginAsync(context, default); clock.Advance(seconds);
    var expired = await adapter.StatusAsync(context, default); await adapter.StatusAsync(context, default);
    await adapter.ReleaseAsync(context, default);
    Check(expired.State == "expired" && expired.Url is null && expired.UserCode is null && handler.Calls.Count == 1,
        "Provider TTL and operation deadline expire without another token request");
}
foreach (var malformed in new[] { Token("bad").Replace("3600", "0", StringComparison.Ordinal), Token("bad").Replace("Bearer", "Basic", StringComparison.Ordinal), "{}" })
{
    var clock = new FakeClock(now); using var handler = new Handler();
    handler.Add("/api/oauth/device_authorization", deviceJson); handler.Add("/api/oauth/token", malformed);
    using var adapter = new KimiDeviceLoginAdapter(handler, clock);
    var saved = new List<PreparedLoginCredential>(); var context = Context(clock, new("kimi", Region: "global"), saved.Add);
    await adapter.BeginAsync(context, default); clock.Advance(5);
    var invalid = await adapter.StatusAsync(context, default); await adapter.StatusAsync(context, default);
    Check(invalid.State == "failed" && saved.Count == 0 && handler.Calls.Count == 2, "Invalid token grants are never published or retried");
}
{
    var clock = new FakeClock(now); using var handler = new Handler();
    handler.Add("/api/oauth/device_authorization", deviceJson); handler.Add("/api/oauth/token", Token("wrong-user")); handler.Add("/coding/v1/me", meJson);
    using var adapter = new KimiDeviceLoginAdapter(handler, clock);
    var context = Context(clock, new("kimi", Subject: "existing-other-user", Region: "global"));
    await adapter.BeginAsync(context, default); clock.Advance(5); var changed = await adapter.StatusAsync(context, default);
    Check(changed.State == "recovery-required" && changed.Credential!.Purposes.Length == 0, "Reconnect subject mismatch retains private evidence without replacing existing access");
}
{
    var clock = new FakeClock(now); using var handler = new Handler();
    handler.Add("/api/oauth/device_authorization", deviceJson.Replace("kimi.ai", "kimi.com", StringComparison.Ordinal));
    using var adapter = new KimiDeviceLoginAdapter(handler, clock);
    var waiting = await adapter.BeginAsync(Context(clock, new("kimi", Region: "mainland-cn")), default);
    Check(waiting.State == "authorizing" && handler.Calls.Single().Host == "auth.kimi.com", "Explicit mainland selection uses only its own regional auth host");
}
// The composite preserves the existing API-key adapter and its native results.
{
    using var handler = new Handler(); handler.Add("/coding/v1/usages", usageJson);
    using var legacyHttp = new HttpClient(handler); using var quota = new KimiDeviceQuotaAdapter(legacyHttp);
    var measured = await quota.FetchAsync(new("fixture-key-account", "key", "kimi", true, "api-key", "unverified"),
        new("fixture-api-key", baseUrl: "https://api.kimi.com/coding/v1", credentialKind: "api-key"));
    Check(measured.Readings.Count == 3 && measured.Readings[0].UsedPercent == 25 && handler.Calls.Single().Path == "/coding/v1/usages", "Existing key quotas retain their direct usage path and normalization");
}
// Caller cancellation after an issuing poll cannot discard the late grant or publish it.
{
    var clock = new FakeClock(now); var checkpoint = new List<PreparedLoginCredential>();
    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    using var handler = new Handler(); handler.Add("/api/oauth/device_authorization", deviceJson);
    handler.Add("/api/oauth/token", Token("late"), wait: async () => { started.SetResult(); await release.Task; });
    using var adapter = new KimiDeviceLoginAdapter(handler, clock);
    var context = Context(clock, new("kimi", Region: "global"), checkpoint.Add);
    await adapter.BeginAsync(context, default); clock.Advance(5);
    var finishing = adapter.StatusAsync(context, default); await started.Task;
    await adapter.CancelAsync(context, default); release.SetResult(); var result = await finishing;
    Check(result.State == "cancelled" && checkpoint.Count == 1 && checkpoint[0].Purposes.Length == 0 && handler.Calls.Count == 2,
        "Late grant is privately retained after cancellation without profile read, usage read, logout or publication");
}
// Shared persistence, redacted access lease, genuine usage HTTP, restart, serialized generation renewal.
// A quota reconnect refines a known alias/subject/resource binding and preserves inference material.
var reconnectRoot = Path.Combine(Path.GetTempPath(), "lain-kimi-reconnect-fixture-" + Guid.NewGuid().ToString("N"));
try
{
    var paths = new OwnStatePaths(reconnectRoot); var repository = new ConfigurationRepository(paths); var secrets = new SecretStore(paths);
    var accounts = new AccountService(repository, secrets); var clock = new FakeClock(now);
    var known = new ResourceBindingDto("api.kimi.com", Subject: "fixture-subject", ResourceType: "user", ResourceId: "fixture-subject");
    accounts.Connect(repository.Snapshot.Revision, new("kimi", "api-key", ["inference"], Binding: known),
        new("static-key", ["inference"], known, """{"kind":"api-key","apiKey":"fixture-existing-inference","provider":"kimi","baseUrl":"https://api.kimi.com/coding/v1"}"""));
    var existing = repository.Snapshot.Accounts.Single(); var inference = existing.Credentials!.Single();
    using var handler = new Handler(); handler.Add("/api/oauth/device_authorization", deviceJson); handler.Add("/api/oauth/device_authorization", deviceJson);
    handler.Add("/api/oauth/token", Token("reconnect")); handler.Add("/coding/v1/me", meJson);
    using var adapter = new KimiDeviceLoginAdapter(handler, clock);
    await using var service = new LoginOperationService(configuration: repository, accounts: accounts, secrets: secrets, accountAdapters: [adapter], timeProvider: clock);
    var request = new ConnectAccountRequest("kimi", "kimi-device", ["quota"], AccountId: existing.Id, ExpectedGeneration: existing.Generation);
    var offered = await service.BeginAsync(repository.Snapshot.Revision, request);
    Check(offered.State == "resource-required" && offered.Resources!.All(candidate => candidate == (known with { Product = "coding", Region = candidate.Region }))
        && handler.Calls.Count == 0, "Reconnect region candidates retain known subject, resource, alias and provenance");
    await service.SelectResourceAsync(offered.Id, offered.Resources![0]);
    Check((await service.StatusAsync(offered.Id)).State == "authorizing", "Selected refined binding retains operation account and generation guards");
    Check((await service.CancelAsync(offered.Id)).State == "cancelled" && repository.Snapshot.Accounts.Single().Generation == existing.Generation,
        "Cancel and adapter cleanup accept the refined reconnect binding without account mutation");
    var login = await service.BeginAsync(repository.Snapshot.Revision, request);
    await service.SelectResourceAsync(login.Id, login.Resources![0]); await service.StatusAsync(login.Id); clock.Advance(5);
    Check((await service.StatusAsync(login.Id)).State == "completed", "Known subject/resource with missing region can reconnect through exact shared resource selection");
    var connected = repository.Snapshot.Accounts.Single();
    Check(connected.Id == existing.Id && connected.Provider == existing.Provider && connected.Binding!.Provider == known.Provider
        && connected.Generation == existing.Generation + 1 && connected.Credentials!.Single(handle => handle.Id == inference.Id).Generation == inference.Generation
        && connected.Capabilities!.Single(capability => capability.Purpose == "inference").CredentialId == inference.Id
        && new CredentialResolver(repository, secrets, clock).Resolve(connected.Id, "inference")!.PrivateJson.Contains("fixture-existing-inference"),
        "Quota reconnect retains local account, alias display and existing inference credential/capability");
    var source = new HostQuotaSource(repository, secrets, new MonitorAccessService(repository, secrets, new MonitorCredentialStore(paths)));
    using var quotaHandler = new Handler(); quotaHandler.Add("/coding/v1/me", meJson); quotaHandler.Add("/coding/v1/usages", usageJson);
    using var legacyHttp = new HttpClient(new Handler()); using var quota = new KimiDeviceQuotaAdapter(legacyHttp, quotaHandler, clock);
    var measured = await quota.FetchAsync(connected, (await source.GetCredentialAsync(connected.Id))!);
    Check(measured.Readings.All(row => row.Source == "https://api.kimi.ai/coding/v1/usages#" + row.WindowId),
        "Supported legacy provider aliases retain actual global OAuth usage source");
}
finally { if (Directory.Exists(reconnectRoot)) Directory.Delete(reconnectRoot, true); }
var root = Path.Combine(Path.GetTempPath(), "lain-kimi-device-fixture-" + Guid.NewGuid().ToString("N"));
try
{
    var paths = new OwnStatePaths(root); var repository = new ConfigurationRepository(paths); var secrets = new SecretStore(paths);
    var accounts = new AccountService(repository, secrets); var clock = new FakeClock(now);
    using var handler = new Handler(); handler.Add("/api/oauth/device_authorization", deviceJson); handler.Add("/api/oauth/token", Token("published")); handler.Add("/coding/v1/me", meJson);
    using var adapter = new KimiDeviceLoginAdapter(handler, clock);
    await using var service = new LoginOperationService(configuration: repository, accounts: accounts, secrets: secrets, accountAdapters: [adapter], timeProvider: clock);
    var login = await service.BeginAsync(repository.Snapshot.Revision, new("kimi", "kimi-device", ["quota"], Binding: new("kimi", Region: "global")));
    Check(login.State == "authorizing" && login.UserCode is not null, "Shared lifecycle propagates device instructions");
    clock.Advance(5); var completed = await service.StatusAsync(login.Id);
    Check(completed.State == "completed" && completed.UserCode is null && completed.Url is null, "Published login drops public authorization instructions");
    var account = repository.Snapshot.Accounts.Single(); var handle = account.Credentials!.Single();
    Check(account.Capabilities!.Single().Purpose == "quota" && repository.Snapshot.Routes.Length == 0 && repository.Snapshot.Profiles.Length == 0, "Login creates no model, inference or profile and preserves quota-only purpose");
    var lease = new CredentialResolver(repository, secrets, clock).Resolve(account.Id, "quota")!;
    Check(!lease.PrivateJson.Contains("refreshToken", StringComparison.OrdinalIgnoreCase) && lease.PrivateJson.Contains("fixture-access-published"), "Reader receives access-only projection, never refresh material");
    var source = new HostQuotaSource(repository, secrets, new MonitorAccessService(repository, secrets, new MonitorCredentialStore(paths)));
    var credential = (await source.GetCredentialAsync(account.Id))!;
    Check(credential.CredentialKind == "kimi-oauth" && credential.ProviderAccountId == "fixture-subject" && credential.Settings["region"] == "global", "Existing HostQuotaSource projects the real grant without a foreign credential store");
    using var quotaHandler = new Handler(); quotaHandler.Add("/coding/v1/me", meJson); quotaHandler.Add("/coding/v1/usages", usageJson);
    using var legacyHttp = new HttpClient(new Handler());
    using var quota = new KimiDeviceQuotaAdapter(legacyHttp, quotaHandler, clock);
    var measured = await quota.FetchAsync(account, credential);
    Check(measured.Support?.Kind == "api" && measured.Readings.Count == 3 && measured.Readings[0].UsedPercent == 25 && measured.Readings[1].UsedPercent == 0 && measured.Readings[2].UsedPercent is null,
        "Actual OAuth HTTP path reads independent native windows and preserves invalid versus measured zero");
    Check(measured.Readings.All(row => row.ScopeId == "fixture-subject" && row.Scope == "kimi-code-user" && row.BudgetPoolId == "kimi-code:global:fixture-subject"), "Quota observations retain region/subject pool identity");
    Check(measured.Readings.All(row => row.Source == "https://api.kimi.ai/coding/v1/usages#" + row.WindowId
        && row.SourceUrl == "https://api.kimi.ai/coding/v1/usages"), "Global observation source and URL report the actual regional API");
    using var mainlandHandler = new Handler(); mainlandHandler.Add("/coding/v1/me", meJson.Replace("REGION_OVERSEA", "REGION_CN", StringComparison.Ordinal)); mainlandHandler.Add("/coding/v1/usages", usageJson);
    using var mainlandQuota = new KimiDeviceQuotaAdapter(legacyHttp, mainlandHandler, clock);
    var mainlandSettings = new Dictionary<string, string>(credential.Settings) { ["region"] = "mainland-cn", ["providerRegion"] = "REGION_CN" };
    var mainland = await mainlandQuota.FetchAsync(account with { Binding = account.Binding! with { Region = "mainland-cn" } },
        new(credential.BearerToken, credential.ProviderAccountId, credential.ExpiresAt, "https://api.kimi.com/coding/v1", mainlandSettings, credentialKind: "kimi-oauth"));
    Check(mainland.Readings.All(row => row.Source == "https://api.kimi.com/coding/v1/usages#" + row.WindowId
        && row.SourceUrl == "https://api.kimi.com/coding/v1/usages") && mainlandHandler.Calls.All(call => call.Host == "api.kimi.com"),
        "Mainland observation source and requests remain on the selected regional API");
    await Reject(() => quota.FetchAsync(account with { Binding = account.Binding! with { Region = "mainland-cn" } }, credential), "Cross-region lease cannot leave process");
    Check(quotaHandler.Calls.Count == 2, "Mismatched binding sends no provider call");
    var restarted = new ConfigurationRepository(paths);
    Check(restarted.Snapshot.Accounts.Single().Id == account.Id && new CredentialResolver(restarted, new SecretStore(paths), clock).Resolve(account.Id, "quota") is not null, "Own shared generation survives restart with stable local account identity");
    var owners = new CredentialOwnerService(repository, accounts, secrets.Generations, [adapter]);
    var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var refreshRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    handler.Add("/api/oauth/token", Token("renewed"), wait: async () => { refreshStarted.SetResult(); await refreshRelease.Task; });
    handler.Add("/coding/v1/me", meJson);
    var first = owners.RefreshAsync(account.Id, handle.Id); await refreshStarted.Task;
    var second = owners.RefreshAsync(account.Id, handle.Id); refreshRelease.SetResult(); await Task.WhenAll(first, second);
    var renewed = repository.Snapshot.Accounts.Single();
    Check(renewed.Id == account.Id && renewed.Credentials!.Single().Generation == handle.Generation + 1 && handler.Calls.Count(call => call.Body.Contains("grant_type=refresh_token")) == 1,
        "Shared owner serializes concurrent renewal, consumes generation once and retains stable local identity");
    Check(secrets.Generations.IsRetired(handle) && new CredentialResolver(repository, secrets, clock).Resolve(account.Id, "quota")!.PrivateJson.Contains("fixture-access-renewed"), "Old refresh material is durably retired and reader resolves only new access");
    // A rotated result with missing identity must survive in recovery; never reactivate old material.
    var currentHandle = renewed.Credentials!.Single(); var privateGrant = secrets.Generations.Read(account.Id, currentHandle).PrivateJson;
    await Reject(() => adapter.RefreshAsync(new(account.Id, currentHandle with { Binding = currentHandle.Binding with { Subject = "another-subject" } }, privateGrant), default), "Renewal refuses changed subject before network");
    handler.Add("/api/oauth/token", Token("durable-recovery")); handler.Add("/coding/v1/me", "{}");
    await Reject(() => owners.RefreshAsync(account.Id, currentHandle.Id), "Unverified rotated generation cannot be published by the shared owner");
    var recoveryRecords = Directory.EnumerateFiles(Path.GetDirectoryName(secrets.Generations.RecoveryPath("fixture"))!, "rotation-*.json")
        .Select(path => CredentialRecoveryService.Read(secrets.Generations, Path.GetFileNameWithoutExtension(path))).Where(record => record.State == "prepared").ToArray();
    var retained = recoveryRecords.Single(record => record.Handle?.Generation == currentHandle.Generation + 1);
    Check(retained.Handle!.Purposes.Length == 0 && secrets.Generations.Read(account.Id, retained.Handle).PrivateJson.Contains("fixture-refresh-durable-recovery")
        && secrets.Generations.IsRetired(currentHandle) && repository.Snapshot.Accounts.Single().Credentials!.Single().Generation == currentHandle.Generation,
        "Shared owner durably preserves failed postrotation result, retires consumed material, and leaves account publication unchanged");
    await Reject(() => { new CredentialRecoveryService(repository, accounts, secrets.Generations).Adopt(repository.Snapshot.Revision, retained.OperationId); return Task.CompletedTask; },
        "Empty-purpose recovery cannot silently become a quota credential");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
Console.WriteLine($"PASS {checks} Kimi device assertions; synthetic HTTP and private temporary generation storage only.");

sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(int seconds) => now = now.AddSeconds(seconds);
}
sealed class Handler : HttpMessageHandler
{
    private sealed record Reply(string Path, string Json, HttpStatusCode Status, Action<Call>? Inspect, Func<Task>? Wait);
    public sealed record Call(string Host, string Path, string Body, string Platform, string UserAgent);
    private readonly Queue<Reply> replies = new();
    public List<Call> Calls { get; } = [];
    public void Add(string path, string json, HttpStatusCode status = HttpStatusCode.OK, Action<Call>? inspect = null, Func<Task>? wait = null) => replies.Enqueue(new(path, json, status, inspect, wait));
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var call = new Call(request.RequestUri!.Host, request.RequestUri.AbsolutePath, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken),
            request.Headers.TryGetValues("X-Msh-Platform", out var platform) ? platform.Single() : "", request.Headers.UserAgent.ToString());
        Calls.Add(call);
        if (replies.Count == 0) throw new InvalidOperationException("Unexpected synthetic Kimi request: " + call.Path);
        var reply = replies.Dequeue();
        if (reply.Path != call.Path) throw new InvalidOperationException("Unexpected synthetic Kimi path.");
        reply.Inspect?.Invoke(call);
        if (reply.Wait is not null) await reply.Wait();
        return new(reply.Status) { Content = new StringContent(reply.Json) };
    }
}
