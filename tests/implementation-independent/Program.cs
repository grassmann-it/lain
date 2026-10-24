using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Accounts.Credentials;
using Lain.Host.Accounts.Monitoring;
using Lain.Host.Operations;
using Lain.Host.Quota;
using Lain.Host.Usage;
using Microsoft.Data.Sqlite;

// Independently authored adversarial checks. All material is synthetic and all HTTP is in-memory.
var directory = Path.Combine(Path.GetTempPath(), "lain-independent-" + Guid.NewGuid().ToString("N"));
var failures = new List<string>();
try
{
    await Run("bound Requesty cannot use usage after failed identity read", RequestyBinding);
    await Run("verified identity cannot survive unverified manual reconnect", VerifiedReconnect);
    await Run("inference replacement retains independent quota generation", IndependentQuota);
    await Run("durable remote result recovers across recovery-record crash gap", PreparedCrashGap);
    await Run("restored rotating generation is quarantined", RestoredGrant);
    await Run("historical canonical journal digest survives replay", HistoricalJournal);
}
finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
Console.WriteLine($"Independent scenarios: {6 - failures.Count}/6 passed.");
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
