using System.Globalization;
using System.Net;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Accounts.ConsoleConnections;
using Lain.Host.Accounts.ConsoleConnections.MiMo;
using Lain.Host.Accounts.Credentials;
using Lain.Host.Accounts.Monitoring;
using Lain.Host.Operations;
using Lain.Host.Providers;
using Lain.Host.Quota;

var root = Path.Combine(Path.GetTempPath(), "lain-quota-host-fixture-" + Guid.NewGuid().ToString("N"));
var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
try
{
    var paths = new OwnStatePaths(root); var secrets = new SecretStore(paths); var repository = new ConfigurationRepository(paths);
    var accounts = new AccountService(repository, secrets); var monitors = new MonitorAccessService(repository, secrets, new(paths));
    var source = new HostQuotaSource(repository, secrets, monitors);
    await using var browser = new FixtureBrowser();
    var login = new MiMoConsoleLoginAdapter(browser);
    var mimo = new MiMoQuotaAdapter(browser, secrets, repository);
    var mimoCleanup = new MiMoSessionCleanup(browser, secrets, repository);
    var notified = new List<string>();
    var cleanup = new QuotaConnectionCleanup(repository, [mimoCleanup.CloseDetachedAsync, (account, _) => { notified.Add(account.Id); return Task.CompletedTask; }]);
    var methods = new ConnectionMethodCatalog(new ProviderCatalogRegistry(), () => false, () => false, () => false, mimoAvailable: () => login.IsAvailable);
    var unavailable = methods.Snapshot("xiaomi-mimo").Methods;
    Check(unavailable.Single(m => m.Id == "console-browser").Availability == "setup-required" && unavailable.All(m => m.Id != "console"), "one MiMo browser method with honest local prerequisite");
    Check(browser.Opens == 0, "catalog availability does not open a browser");
    browser.Available = true;
    var request = new ConnectAccountRequest("xiaomi-mimo", "console-browser", ["quota"], AccountId: "acct-mimo");
    Check(methods.RequireAvailable(request, false) is { RequiresCore: false, SecretRequired: false, Availability: "available" }, "MiMo quota login available in Monitor mode without key/Core");
    var rejected = false;
    try { methods.RequireAvailable(request with { Purposes = ["inference"] }, false); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "MiMo browser does not advertise inference access");
    Check(methods.Snapshot("groq").Methods.Single(m => m.Id == "monitor-api-token").RequiredSettings!.Single().Id == "projectId", "Groq monitor form asks for project identity");
    var legacy = new AccountDto("acct-mimo", "Chosen MiMo", "xiaomi-mimo", true, "api-key", "unverified");
    secrets.WriteAccount(legacy.Id, "{\"kind\":\"api-key\",\"apiKey\":\"unchanged-fixture-inference\"}");
    repository.Update(0, state => state with { Accounts = [legacy] });
    var quota = new QuotaService(source, source, [mimo]);
    await quota.RefreshAsync(legacy.Id);
    Check(browser.Opens == 0 && browser.Reads == 0, "ordinary quota refresh never opens browser for an inference-only account");
    await using var operations = new LoginOperationService(configuration: repository, accounts: accounts, secrets: secrets, accountAdapters: [login]);
    async Task<LoginDto> FinishLogin(LoginDto operation)
    {
        var before = repository.Snapshot;
        try
        {
            var status = await operations.StatusAsync(operation.Id);
            if (status.State == "resource-required")
            {
                await operations.SelectResourceAsync(operation.Id, status.Resources!.Single());
                status = await operations.StatusAsync(operation.Id);
            }
            return status;
        }
        finally { await cleanup.AfterPublicationAsync(before); }
    }
    var first = await operations.BeginAsync(repository.Snapshot.Revision, request);
    Check((await FinishLogin(first)).State == "completed", "MiMo login publishes selected console quota capability");
    var firstProfile = browser.LastOpened;
    Check(browser.Active.Contains(firstProfile) && browser.Closed.Count == 0, "initial publication retains its owned browser");
    var measured = (await quota.RefreshAsync(legacy.Id)).Single();
    Check(measured.Remaining == 75 && browser.Opens == 1, "registered reader uses saved console session without a new browser");
    Check(secrets.ReadAccount(legacy.Id).Contains("unchanged-fixture-inference"), "monitor attachment preserves saved inference key");
    var beforeInference = repository.Snapshot;
    accounts.SetEnabled(beforeInference.Revision, legacy.Id, true);
    await cleanup.AfterPublicationAsync(beforeInference);
    Check(browser.Active.Contains(firstProfile) && notified.Count == 0, "unrelated account update leaves quota session selected");

    var prior = repository.Snapshot;
    var conflict = await operations.BeginAsync(prior.Revision, request with { ExpectedGeneration = prior.Accounts.Single().Generation });
    var recoveryProfile = browser.LastOpened;
    accounts.SetEnabled(repository.Snapshot.Revision, legacy.Id, true); // Late unrelated publication wins the original CAS.
    Check((await FinishLogin(conflict)).State == "recovery-required", "late CAS conflict retains prepared authorization for recovery");
    Check(browser.Active.Contains(firstProfile) && browser.Active.Contains(recoveryProfile) && browser.Closed.Count == 0, "late conflict closes neither previous selected profile nor recoverable result");
    var beforeRecovery = repository.Snapshot;
    new CredentialRecoveryService(repository, accounts, secrets.Generations).Adopt(beforeRecovery.Revision, conflict.Id);
    await cleanup.AfterPublicationAsync(beforeRecovery);
    Check(!browser.Active.Contains(firstProfile) && browser.Active.Contains(recoveryProfile) && notified.SequenceEqual([legacy.Id]), "successful recovery publication closes only prior profile and invokes generic callbacks");

    var beforeSameProfile = repository.Snapshot;
    var sameAccount = beforeSameProfile.Accounts.Single();
    var sameHandle = QuotaConnectionCleanup.SelectedQuota(sameAccount)!;
    var samePrivate = secrets.Generations.Read(sameAccount.Id, sameHandle).PrivateJson;
    accounts.Connect(beforeSameProfile.Revision, request with { Binding = sameHandle.Binding }, new("console-browser", ["quota"], sameHandle.Binding, samePrivate));
    await cleanup.AfterPublicationAsync(beforeSameProfile);
    Check(browser.Active.Contains(recoveryProfile), "new generation still selecting same profile does not close it");

    var reconnect = await operations.BeginAsync(repository.Snapshot.Revision, request);
    var replacementProfile = browser.LastOpened;
    browser.FailCloseOnce = true;
    Check((await FinishLogin(reconnect)).State == "completed" && cleanup.PendingCount == 1, "cleanup failure cannot undo successful reconnect");
    Check(browser.Active.Contains(recoveryProfile) && browser.Active.Contains(replacementProfile), "failed local closure retains retry reference");
    await cleanup.AfterPublicationAsync(repository.Snapshot);
    Check(cleanup.PendingCount == 0 && !browser.Active.Contains(recoveryProfile) && browser.Active.Contains(replacementProfile), "later request retries only detached profile closure");
    var beforeRemove = repository.Snapshot;
    monitors.Remove(beforeRemove.Revision, legacy.Id);
    await cleanup.AfterPublicationAsync(beforeRemove);
    Check(!browser.Active.Contains(replacementProfile) && secrets.ReadAccount(legacy.Id).Contains("unchanged-fixture-inference"), "monitor removal closes owned session and preserves inference");
    var onlyLogin = await operations.BeginAsync(repository.Snapshot.Revision, request with { AccountId = null, Name = "MiMo monitor" });
    Check((await FinishLogin(onlyLogin)).State == "completed", "monitor-only session published through same login service");
    var only = repository.Snapshot.Accounts.Single(a => a.Id != legacy.Id);
    var onlyProfile = browser.LastOpened;
    var beforeDelete = repository.Snapshot;
    accounts.Remove(beforeDelete.Revision, only.Id);
    await cleanup.AfterPublicationAsync(beforeDelete);
    Check(!browser.Active.Contains(onlyProfile), "account removal closes selected owned session");

    // Same composition used by Host: passive source can be unavailable while the explicit
    // Enterprise monitor reads independently, and missing opt-in never calls the provider.
    var now = DateTimeOffset.UtcNow;
    using var providerHandler = new FixtureHttp(http =>
    {
        Check(http.Headers.Authorization?.Parameter == "fixture-groq-monitor", "Groq uses selected monitor token");
        Check(Uri.UnescapeDataString(http.RequestUri!.Query).Contains("project_id=\"fixture-project\""), "Groq Enterprise query is project-bound");
        object Sample(string lane, string value) => new { metric = new Dictionary<string, string> {
            ["lain_metric"] = GroqEnterpriseUsageReader.RequestsMetric, ["lain_sample_kind"] = lane,
            ["project_id"] = "fixture-project", ["model"] = "fixture-model", ["status_code"] = "200" }, value = new object[] { now.ToUnixTimeSeconds(), value } };
        return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { status = "success", data = new { resultType = "vector",
            result = new[] { Sample("value", "2"), Sample("timestamp", now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)) } } })) };
    });
    using var providerHttp = new HttpClient(providerHandler);
    using var coreHandler = new FixtureHttp(_ => throw new Exception("disabled Core must not be called"));
    using var coreHttp = new HttpClient(coreHandler);
    var groq = new GroqAccountQuotaAdapter(new CoreDependentQuotaAdapter(new GroqQuotaAdapter(coreHttp), () => false), new GroqEnterpriseUsageReader(providerHttp));
    var created = methods.Connect(repository.Snapshot.Revision, new(new("groq", "monitor-api-token", ["quota"], Name: "Groq Enterprise"),
        "fixture-groq-monitor", "api-token", Settings: new() { ["projectId"] = "fixture-project" }), accounts, monitors, repository);
    var groqAccount = created.Accounts.Single(a => a.Provider == "groq");
    Check(groqAccount.Binding is { ResourceType: "project", ResourceId: "fixture-project" }, "generic monitor connect binds Groq project instead of account");
    var combined = new QuotaService(source, source, [mimo, groq]);
    var rates = (await combined.RefreshAsync(groqAccount.Id)).Where(q => q.AccountId == groqAccount.Id).ToArray();
    Check(rates.Single().Used == 2 && rates.Single().MeasurementKind == "rate" && rates.Single().Remaining is null && providerHandler.Calls == 1 && coreHandler.Calls == 0,
        "Groq Enterprise reads in Monitor mode without treating rates as remaining budget");
    var beforeGroqRemove = repository.Snapshot;
    monitors.Remove(beforeGroqRemove.Revision, groqAccount.Id);
    await cleanup.AfterPublicationAsync(beforeGroqRemove);
    await combined.RefreshAsync(groqAccount.Id);
    Check(providerHandler.Calls == 1 && coreHandler.Calls == 0, "missing monitor opt-in remains passive without provider request");
    repository.Update(repository.Snapshot.Revision, state => state with { Accounts = [.. state.Accounts, new("acct-nvidia", "NVIDIA", "nvidia-build", true, "api-key", "unverified")] });
    var nvidia = (await combined.RefreshAsync("acct-nvidia")).Single(q => q.AccountId == "acct-nvidia");
    Check(nvidia.Support == NvidiaBuildPublicReference.Support() && nvidia.Used is null && nvidia.Limit is null && nvidia.Remaining is null && nvidia.LastSuccessAt is null,
        "NVIDIA public reference remains dated guidance with no fake measured quantities");
    Check(methods.Snapshot("nvidia-build").Methods.All(m => m.Kind == "api-key"), "NVIDIA has no fabricated monitoring login");
    await browser.OpenAsync("fixture-host-stop", CancellationToken.None);
    await browser.DisposeAsync();
    Check(browser.Active.Count == 0 && browser.Disposed, "Host-owned browser lifetime closes remaining owned windows");
    Console.WriteLine($"Quota Host integration: {checks} synthetic checks passed.");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }

sealed class FixtureHttp(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
{
    public int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Calls++; return Task.FromResult(send(request)); }
}
sealed class FixtureBrowser : IMiMoConsoleBrowser, IAsyncDisposable
{
    public bool Available, FailCloseOnce, Disposed;
    public bool IsAvailable => Available;
    public int Opens, Reads;
    public string LastOpened = "";
    public HashSet<string> Active = [];
    public List<string> Closed = [];
    public Task<string> OpenAsync(string accountId, CancellationToken ct)
    { LastOpened = (++Opens).ToString("x32", CultureInfo.InvariantCulture); Active.Add(LastOpened); return Task.FromResult(LastOpened); }
    public Task CloseAsync(string profile, bool erase, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (FailCloseOnce) { FailCloseOnce = false; throw new IOException("fixture-private-failure"); }
        if (Active.Remove(profile)) Closed.Add(profile);
        return Task.CompletedTask;
    }
    public Task<JsonDocument> ReadAsync(string profile, ConsoleReadOperation operation, CancellationToken ct)
    {
        if (!Active.Contains(profile)) throw new InvalidOperationException("Fixture session closed.");
        Reads++;
        return Task.FromResult(JsonDocument.Parse(operation switch {
            ConsoleReadOperation.MiMoIdentity => "{\"code\":0,\"data\":{\"userId\":\"fixture-user\",\"idc\":310}}",
            ConsoleReadOperation.MiMoIndividualUsage => "{\"code\":0,\"data\":{\"usage\":{\"items\":[{\"name\":\"yearly\",\"used\":25,\"limit\":100,\"percent\":0.25}]}}}",
            ConsoleReadOperation.MiMoIndividualDetail => "{\"code\":0,\"data\":{\"currentPeriodEnd\":\"2027-10-24T12:00:00Z\"}}",
            _ => throw new InvalidOperationException("Unexpected fixture operation.") }));
    }
    public async ValueTask DisposeAsync() { foreach (var profile in Active.ToArray()) await CloseAsync(profile, false, CancellationToken.None); Disposed = true; }
}
