using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Accounts.Credentials;
using Lain.Host.Accounts.Monitoring;
using Lain.Host.Models;
using Lain.Host.Operations;
using Lain.Host.Quota;
using Lain.Host.Usage;
using Microsoft.Data.Sqlite;

// Independently authored adversarial checks. All material is synthetic and all HTTP is in-memory.
var directory = Path.Combine(Path.GetTempPath(), "lain-independent-" + Guid.NewGuid().ToString("N"));
var failures = new List<string>();
Console.WriteLine("Host assembly SHA256: " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(AccountService).Assembly.Location))).ToLowerInvariant());
try
{
    await Run("bound Requesty cannot use usage after failed identity read", RequestyBinding);
    await Run("verified identity cannot survive unverified manual reconnect", VerifiedReconnect);
    await Run("inference replacement retains independent quota generation", IndependentQuota);
    await Run("durable remote result recovers across recovery-record crash gap", PreparedCrashGap);
    await Run("restored rotating generation is quarantined", RestoredGrant);
    await Run("quota-only core ownership resolves selected purpose", QuotaOnlyCore);
    await Run("missing explicit handle never falls back to inference", MissingExplicitHandle);
    await Run("failed newer generation write retries newer payload", PersistenceRetry);
    await Run("legacy preservation rejects contradictory profile identities", LegacyContradictions);
    await Run("new Codex snapshot reaches qualified quota read", CodexSnapshotQuota);
    await Run("historical canonical journal digest survives replay", HistoricalJournal);
}
finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
Console.WriteLine($"Independent scenarios: {11 - failures.Count}/11 passed.");
Environment.ExitCode = failures.Count == 0 ? 0 : 1;

async Task Run(string label, Func<Task> test)
{
    try { await test(); Console.WriteLine("PASS " + label); }
    catch (Exception error) { failures.Add(label); Console.WriteLine("FAIL " + label + " [" + error.GetType().Name + "]"); }
}
void Require(bool condition) { if (!condition) throw new InvalidOperationException("Independent invariant failed."); }
Fixture Fresh(string name) => new(Path.Combine(directory, name));
PreparedLoginCredential Credential(string owner, ResourceBindingDto binding, string marker = "synthetic-a") =>
    new(owner, ["quota"], binding, JsonSerializer.Serialize(new { kind = "api-key", apiKey = marker }), PreserveForRecovery: true);

async Task RequestyBinding()
{
    foreach (var response in new[] { "http-failure", "{}", "not-json" })
    {
        var calls = 0;
        using var client = new HttpClient(new Handler(request =>
        {
            calls++;
            if (request.RequestUri!.AbsolutePath.EndsWith("/usage"))
                return new(HttpStatusCode.OK) { Content = new StringContent("{\"usage\":{\"day\":{\"spend\":1,\"total_requests\":1,\"total_tokens\":2}}}") };
            return new(response == "http-failure" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK) { Content = new StringContent(response) };
        }));
        var account = new AccountDto("a", "Synthetic", "requesty", true, "api-key", "unverified",
            Binding: new("requesty", ResourceType: "api_key", ResourceId: "bound-key", Provenance: "verified"));
        var result = await new RequestyQuotaAdapter(client).FetchAsync(account, new("synthetic-key"));
        Require(result.Readings.Count == 0 && result.Error is not null && calls == 1);
    }
}

Task VerifiedReconnect()
{
    var f = Fresh("identity");
    var binding = new ResourceBindingDto("test", Subject: "subject-a", ResourceType: "project", ResourceId: "project-a", Provenance: "verified");
    var before = f.Accounts.Connect(0, new("test", "qualified-fixture", ["quota"]), Credential("session-owner", binding));
    var account = before.Accounts.Single();
    var refused = false;
    try { f.Accounts.ConnectCredential(before.Revision, new(new("test", "manual-key", ["quota"], AccountId: account.Id), "synthetic-b")); }
    catch (Exception error) when (error is ArgumentException or InvalidDataException or InvalidOperationException) { refused = true; }
    Require(refused && f.Repository.Snapshot.Revision == before.Revision && f.Repository.Snapshot.Accounts.Single().Generation == account.Generation);
    var direct = Fresh("direct-identity");
    var directState = direct.Accounts.Connect(0, new("test", "qualified-fixture", ["inference", "quota"]),
        Credential("static-key", binding) with { Purposes = ["inference", "quota"] });
    refused = false;
    try { direct.Accounts.ReplaceApiCredential(directState.Revision, directState.Accounts.Single().Id, "synthetic-replacement"); }
    catch (Exception error) when (error is ArgumentException or InvalidDataException or InvalidOperationException) { refused = true; }
    Require(refused && direct.Repository.Snapshot.Revision == directState.Revision);
    return Task.CompletedTask;
}

async Task IndependentQuota()
{
    var f = Fresh("separate");
    var initial = f.Accounts.AddApi(0, new("Synthetic", "gemini-api", "synthetic-inference-a", "https://generativelanguage.googleapis.com/v1beta/openai"));
    var id = initial.Accounts.Single().Id;
    var configured = f.Monitor.Set(initial.Revision, id, new("access-token-snapshot", "synthetic-monitor", "fixture-project", DateTimeOffset.UtcNow.AddHours(1)));
    var originalHandle = configured.Accounts.Single().Credentials!.Single(h => h.Purposes.Contains("quota") && !h.Purposes.Contains("inference"));
    var changed = f.Accounts.ReplaceApiCredential(configured.Revision, id, "synthetic-inference-b");
    var quota = await new HostQuotaSource(f.Repository, f.Secrets, f.Monitor).GetCredentialAsync(id);
    Require(changed.Accounts.Single().Credentials!.Any(h => h.Id == originalHandle.Id && h.Generation == originalHandle.Generation));
    Require(quota?.MonitorBearerToken == "synthetic-monitor");
}

Task PreparedCrashGap()
{
    var f = Fresh("crash-gap");
    var context = f.Accounts.ReserveConnect(0, new("test", "qualified-fixture", ["quota"]), "fixture-operation", DateTimeOffset.UtcNow.AddMinutes(1));
    CredentialRecoveryService.SaveStarted(f.Secrets.Generations, context);
    var handle = f.Secrets.Generations.Prepare(context.AccountId, Credential("session-owner", new("test", Subject: "subject-a", Provenance: "verified")), context.OperationId);
    // Simulated process death here: immutable private result exists, recovery pointer was not yet written.
    var restarted = new Fixture(f.Paths.Root);
    var recovered = new CredentialRecoveryService(restarted.Repository, restarted.Accounts, restarted.Secrets.Generations).Adopt(0, context.OperationId);
    Require(recovered.Accounts.Single().Credentials!.Single().Id == handle.Id);

    var next = Fresh("crash-newer-generation");
    context = next.Accounts.ReserveConnect(0, new("test", "qualified-fixture", ["quota"]), "fixture-newer-operation", DateTimeOffset.UtcNow.AddMinutes(1));
    CredentialRecoveryService.SaveStarted(next.Secrets.Generations, context);
    var initial = next.Secrets.Generations.Prepare(context.AccountId, Credential("static-key", new("test")), context.OperationId);
    CredentialRecoveryService.SavePrepared(next.Secrets.Generations, context, initial, null);
    var updated = next.Secrets.Generations.Prepare(context.AccountId, Credential("static-key", new("test", Subject: "subject-a", Provenance: "observed")), context.OperationId, initial.Id, 2);
    // The metadata-complete generation was durable; pointer still names the first checkpoint.
    var reconciled = CredentialRecoveryService.Read(new Fixture(next.Paths.Root).Secrets.Generations, context.OperationId);
    Require(reconciled.Handle?.Id == updated.Id && reconciled.Handle.Generation == 2);
    return Task.CompletedTask;
}

Task RestoredGrant()
{
    var f = Fresh("restore");
    var state = f.Accounts.Connect(0, new("test", "qualified-fixture", ["quota"]), Credential("session-owner", new("test")));
    var account = state.Accounts.Single();
    OwnStatePaths.WritePrivate(Path.Combine(f.Paths.Root, "restore-requires-reconnect"), "synthetic-distinct-restore-epoch");
    var restarted = new Fixture(f.Paths.Root);
    var refused = false;
    try { new CredentialResolver(restarted.Repository, restarted.Secrets).Resolve(account.Id, "quota"); }
    catch (InvalidDataException) { refused = true; }
    Require(refused);
    return Task.CompletedTask;
}

Task QuotaOnlyCore()
{
    var f = Fresh("quota-core");
    OwnStatePaths.WritePrivate(Path.Combine(f.Paths.OwnedAuth, "fixture.json"),
        "{\"type\":\"codex\",\"account_id\":\"subject-a\",\"access_token\":\"synthetic-core-access\",\"refresh_token\":\"synthetic-core-refresh\"}");
    var state = f.Accounts.Connect(0, new("codex", "codex-owned", ["quota"]),
        new("core", ["quota"], new("codex", Subject: "subject-a", Provenance: "verified"),
            "{\"kind\":\"codex-owned\",\"provider\":\"codex\",\"ownedAuthFile\":\"fixture.json\",\"accountId\":\"subject-a\"}", PreserveForRecovery: true));
    var access = new CredentialResolver(f.Repository, f.Secrets).Resolve(state.Accounts.Single().Id, "quota");
    Require(access is not null && access.PrivateJson.Contains("synthetic-core-access") && !access.PrivateJson.Contains("synthetic-core-refresh"));
    Require(!f.Secrets.HasAccount(state.Accounts.Single().Id));
    return Task.CompletedTask;
}

Task MissingExplicitHandle()
{
    var f = Fresh("missing-reference");
    var state = f.Accounts.AddApi(0, new("Synthetic", "test", "synthetic-inference", "https://fixture.invalid/v1"));
    var account = state.Accounts.Single();
    f.Repository.Update(state.Revision, current => current with
    {
        Accounts = [account with { Capabilities = account.Capabilities!.Select(c => c.Purpose == "quota" ? c with { CredentialId = "missing-handle" } : c).ToArray() }]
    });
    var refused = false;
    try { refused = new CredentialResolver(f.Repository, f.Secrets).Resolve(account.Id, "quota") is null; }
    catch (Exception error) when (error is InvalidDataException or InvalidOperationException) { refused = true; }
    Require(refused);
    return Task.CompletedTask;
}

async Task PersistenceRetry()
{
    var f = Fresh("retry-newer-payload");
    var initial = Credential("static-key", new("test"));
    var updated = initial with { Binding = new("test", Subject: "subject-a", Provenance: "observed") };
    await using var service = new LoginOperationService(configuration: f.Repository, accounts: f.Accounts, secrets: f.Secrets,
        accountAdapters: [new CheckpointAdapter(initial, updated)]);
    var operation = await service.BeginAsync(0, new("test", "checkpoint-fixture", ["quota"]));
    var checkpoint = CredentialRecoveryService.Read(f.Secrets.Generations, operation.Id);
    Require(checkpoint.Handle?.Generation == 1);
    var blockedPath = Path.Combine(f.Paths.Credentials, "generations", checkpoint.Handle!.Id, "2.json");
    Directory.CreateDirectory(blockedPath);
    try { Require((await service.StatusAsync(operation.Id)).State == "recovery-required"); }
    finally { Directory.Delete(blockedPath); }
    await service.StatusAsync(operation.Id);
    var recovered = CredentialRecoveryService.Read(f.Secrets.Generations, operation.Id);
    Require(recovered.Handle?.Generation == 2 && recovered.Handle.Binding.Subject == "subject-a");
    Require(f.Repository.Snapshot.Accounts.Length == 0); // Recovery retry persists; adoption remains explicit.
}

Task LegacyContradictions()
{
    var f = Fresh("legacy-runtime");
    var active = Path.Combine(f.Paths.Root, "active-profile.json");
    var runtime = Path.Combine(f.Paths.Proxy, "runtime.json");
    OwnStatePaths.WritePrivate(active, "{\"id\":\"profile-a\",\"revision\":7}");
    OwnStatePaths.WritePrivate(runtime, "{\"valid\":true,\"revision\":7,\"profileId\":\"profile-b\"}");
    Require(!AppliedProfileStatus.CanPreserveLegacyRuntime(active, runtime, 7));
    OwnStatePaths.WritePrivate(runtime, "{\"valid\":true,\"revision\":7}");
    Require(AppliedProfileStatus.CanPreserveLegacyRuntime(active, runtime, 7));
    return Task.CompletedTask;
}

async Task CodexSnapshotQuota()
{
    var f = Fresh("codex-snapshot-read");
    var state = f.Accounts.ConnectCredential(0, new(new("codex", "snapshot", ["quota"],
        Binding: new("codex", Subject: "synthetic-subject")), "synthetic-access", "access-token-snapshot", DateTimeOffset.UtcNow.AddHours(1)));
    var account = state.Accounts.Single();
    var credential = await new HostQuotaSource(f.Repository, f.Secrets, f.Monitor).GetCredentialAsync(account.Id);
    Require(credential is not null && credential.ProviderAccountId == "synthetic-subject");
    var calls = 0;
    using var client = new HttpClient(new Handler(request =>
    {
        calls++;
        Require(request.Headers.GetValues("Chatgpt-Account-Id").Single() == "synthetic-subject");
        return new(HttpStatusCode.OK) { Content = new StringContent("{\"rate_limit\":{\"primary_window\":{\"used_percent\":0,\"limit_window_seconds\":18000}}}") };
    }));
    var result = await new CodexQuotaAdapter(client).FetchAsync(account, credential!);
    Require(calls == 1 && result.Readings.Single().UsedPercent == 0);
    Require(!f.Secrets.HasAccount(account.Id));
}

async Task HistoricalJournal()
{
    // This v1 canonical shape is literal, independent of the current JournalEvent serializer.
    const string historical = "{\"schemaVersion\":1,\"sourceId\":\"historical\",\"sequence\":1,\"eventId\":\"historical:1\",\"kind\":\"attempt_started\",\"requestId\":\"request\",\"attemptId\":\"attempt\",\"accountId\":\"account\",\"credentialId\":\"credential\",\"routeId\":null,\"observedAt\":\"2026-10-24T10:00:00+00:00\",\"provider\":\"fixture\",\"requestedModel\":\"alias\",\"model\":\"model\",\"outcome\":null,\"inputTokens\":null,\"outputTokens\":null,\"totalTokens\":null,\"cacheReadTokens\":null,\"cacheCreationTokens\":null,\"reasoningTokens\":null,\"usageReported\":false,\"statusCode\":null,\"latencyMs\":null,\"ttftMs\":null}";
    var item = JsonSerializer.Deserialize<JournalEvent>(historical, JournalJson.Options)!;
    var database = Path.Combine(Fresh("journal").Paths.Root, "usage.db");
    var store = new SqliteUsageStore(database);
    var batch = new JournalBatch("historical", [item], 1, 1);
    await store.IngestAsync(batch);
    using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
    {
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE journal_events SET digest=$digest WHERE source_id='historical'";
        command.Parameters.AddWithValue("$digest", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(historical))));
        command.ExecuteNonQuery();
    }
    await new SqliteUsageStore(database).IngestAsync(batch);
    Require(await store.GetCursorAsync("historical") == 1);
}

sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request));
}
sealed class CheckpointAdapter(PreparedLoginCredential initial, PreparedLoginCredential updated) : IAccountLoginAdapter
{
    public string Provider => "test";
    public string Method => "checkpoint-fixture";
    public async Task<LoginAdapterResult> BeginAsync(LoginOperationContext context, CancellationToken ct)
    {
        await context.CheckpointCredentialAsync!(initial, ct);
        return new("waiting");
    }
    public Task<LoginAdapterResult> StatusAsync(LoginOperationContext context, CancellationToken ct) => Task.FromResult(new LoginAdapterResult("prepared", Credential: updated));
    public Task CancelAsync(LoginOperationContext context, CancellationToken ct) => Task.CompletedTask;
}
sealed class Fixture
{
    public OwnStatePaths Paths { get; }
    public SecretStore Secrets { get; }
    public ConfigurationRepository Repository { get; }
    public AccountService Accounts { get; }
    public MonitorAccessService Monitor { get; }
    public Fixture(string path)
    {
        Paths = new(path); Secrets = new(Paths); Repository = new(Paths); Accounts = new(Repository, Secrets);
        Monitor = new(Repository, Secrets, new(Paths));
    }
}
