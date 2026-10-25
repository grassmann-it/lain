using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Accounts.Credentials;
using Lain.Host.Accounts.Kilo;
using Lain.Host.Accounts.Monitoring;
using Lain.Host.Operations;
using Lain.Host.Providers;
using Lain.Host.Quota;
using Lain.Host.Usage;

var root = Path.Combine(Path.GetTempPath(), "lain-kilo-device-fixtures-" + Guid.NewGuid().ToString("N"));
var assertions = 0;
const string NewToken = "new-kilo-device-fixture-token";
const string OldToken = "old-kilo-fixture-token";
try
{
    await ContractAndCadence();
    await InvalidInitiation();
    await TerminalStates();
    await ApprovalAndIdentity();
    await LateApproval();
    await ActualLifecycle();
    await ActualLifecycle(alias: true);
    await UnverifiedRecoveryCannotPublish();
    await UnsafeReconnect();
    Console.WriteLine($"Kilo device login: {assertions} assertions passed (offline fake HTTP; no real provider/login/inference).");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }

void Check(bool value, string reason) { assertions++; if (!value) throw new Exception(reason); }
async Task Throws<T>(Func<Task> work, string reason) where T : Exception
{ try { await work(); } catch (T) { assertions++; return; } throw new Exception(reason); }
HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(json) };
HttpResponseMessage Initiation() => Json("""{"code":"ABCD-1234","verificationUrl":"https://app.kilo.ai/device","expiresIn":300}""");
HttpResponseMessage Approval() => Json(JsonSerializer.Serialize(new { status = "approved", token = NewToken, userEmail = "Same@Example.invalid" }));
LoginOperationContext Context(Clock clock, string id = "device-fixture") => new(id, "acct-fixture", 0, 0,
    new("kilo", "device", ["inference", "models", "quota"]), clock.GetUtcNow().AddMinutes(10))
    { CheckpointCredentialAsync = (_, _) => Task.CompletedTask };

async Task ContractAndCadence()
{
    var clock = new Clock();
    var pollCount = 0;
    var handler = new Handler(async (request, ct) =>
    {
        Check(request.Headers.UserAgent.ToString() == "lain/0.1.0", "own user agent");
        Check(!request.Headers.Contains("x-kilocode-organizationid"), "no organization scope");
        if (request.Method == HttpMethod.Post)
        {
            Check(request.RequestUri!.AbsoluteUri == KiloDeviceLoginAdapter.DeviceEndpoint && request.Content is null
                && request.Headers.Authorization is null && request.RequestUri.Query == "", "exact bodyless unauthenticated POST without client ID or scope");
            return Initiation();
        }
        Check(request.Method == HttpMethod.Get && request.RequestUri!.AbsoluteUri == KiloDeviceLoginAdapter.DeviceEndpoint + "/ABCD-1234"
            && request.Headers.Authorization is null && request.Content is null, "exact unauthenticated poll endpoint");
        pollCount++;
        await Task.CompletedTask;
        if (pollCount == 2)
        {
            var response = Json("{}", HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new(System.TimeSpan.FromSeconds(120)); return response;
        }
        return Json("{}", HttpStatusCode.Accepted);
    });
    await using var adapter = new KiloDeviceLoginAdapter(handler, clock);
    var context = Context(clock);
    var begins = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => adapter.BeginAsync(context, default)));
    Check(handler.Calls == 1 && begins.All(x => x.State == "waiting" && x.UserCode == "ABCD-1234"
        && x.VerificationUri == "https://app.kilo.ai/device" && x.Url == x.VerificationUri), "idempotent Begin reserves one code and exposes both instructions");
    await adapter.StatusAsync(context, default); await adapter.StatusAsync(context, default);
    Check(pollCount == 1, "repeated status cannot bypass minimum 3 second cadence");
    clock.Advance(TimeSpan.FromSeconds(3)); await adapter.StatusAsync(context, default);
    clock.Advance(TimeSpan.FromSeconds(59)); await adapter.StatusAsync(context, default);
    Check(pollCount == 2, "429 backoff bounded to 60 seconds");
    clock.Advance(TimeSpan.FromSeconds(1)); await adapter.StatusAsync(context, default);
    Check(pollCount == 3, "poll resumes after bounded RetryAfter");
    await Throws<InvalidOperationException>(() => adapter.StatusAsync(context with { AccountGeneration = 1 }, default), "changed context cannot reuse operation");
    await adapter.CancelAsync(context, default);
    Check((await adapter.StatusAsync(context, default)).State == "cancelled" && pollCount == 3, "local cancel sends no remote request");
    await adapter.ReleaseAsync(context, default);
    Check((await adapter.StatusAsync(context, default)).State == "recovery-required", "released code is not reused");

    var never = new Handler((_, _) => throw new Exception("unexpected HTTP"));
    await using var noCheckpoint = new KiloDeviceLoginAdapter(never, clock);
    await Throws<ArgumentException>(() => noCheckpoint.BeginAsync(context with { CheckpointCredentialAsync = null }, default), "checkpoint must exist before authorization");
    Check(never.Calls == 0, "missing checkpoint performs no HTTP");
}

async Task InvalidInitiation()
{
    foreach (var body in new[] {
        "{}", "[]", "broken", new string('x', 65537),
        """{"code":"bad/code","verificationUrl":"https://app.kilo.ai/device","expiresIn":300}""",
        """{"code":"ABCD","verificationUrl":"http://app.kilo.ai/device","expiresIn":300}""",
        """{"code":"ABCD","verificationUrl":"https://attacker.invalid/device","expiresIn":300}""",
        """{"code":"ABCD","verificationUrl":"https://x@app.kilo.ai/device","expiresIn":300}""",
        """{"code":"ABCD","verificationUrl":"https://app.kilo.ai/device#fragment","expiresIn":300}""",
        """{"code":"ABCD","verificationUrl":"https://app.kilo.ai/device","expiresIn":0}""" })
    {
        var clock = new Clock(); var handler = new Handler((_, _) => Task.FromResult(Json(body)));
        await using var adapter = new KiloDeviceLoginAdapter(handler, clock);
        var result = await adapter.BeginAsync(Context(clock), default);
        Check(result.State == "failed" && result.UserCode is null && result.Credential is null, "malformed/untrusted initiation rejected");
        await adapter.StatusAsync(Context(clock), default); Check(handler.Calls == 1, "failed initiation never silently retried");
    }
}

async Task TerminalStates()
{
    foreach (var fixture in new[] { ("{}", HttpStatusCode.Forbidden, "failed"), ("{}", HttpStatusCode.Gone, "expired"),
        ("""{"status":"denied"}""", HttpStatusCode.OK, "failed"), ("""{"status":"expired"}""", HttpStatusCode.OK, "expired"),
        ("{}", HttpStatusCode.BadGateway, "recovery-required"), ("not-json", HttpStatusCode.OK, "recovery-required"),
        ("""{"status":"approved"}""", HttpStatusCode.OK, "recovery-required") })
    {
        var clock = new Clock();
        var handler = new Handler((request, _) => Task.FromResult(request.Method == HttpMethod.Post ? Initiation() : Json(fixture.Item1, fixture.Item2)));
        await using var adapter = new KiloDeviceLoginAdapter(handler, clock); var context = Context(clock);
        await adapter.BeginAsync(context, default);
        Check((await adapter.StatusAsync(context, default)).State == fixture.Item3, "provider terminal/uncertain state preserved");
        clock.Advance(TimeSpan.FromSeconds(5)); await adapter.StatusAsync(context, default);
        Check(handler.Calls == 2, "terminal approval poll never replayed");
    }
    var time = new Clock(); var count = new Handler((_, _) => Task.FromResult(Initiation()));
    await using var expired = new KiloDeviceLoginAdapter(count, time); var ctx = Context(time);
    await expired.BeginAsync(ctx, default); time.Advance(TimeSpan.FromSeconds(300));
    Check((await expired.StatusAsync(ctx, default)).State == "expired" && count.Calls == 1, "provider lifetime bounds host lifetime without another poll");
}

async Task ApprovalAndIdentity()
{
    foreach (var profile in new[] { """{"user":{"email":"same@example.invalid"},"hasPersonalAccount":true}""",
        """{"email":"same@example.invalid"}""", """{"user":{"name":"Fixture"},"email":"same@example.invalid","hasPersonalAccount":null}""",
        """{"user":{"email":null},"email":"same@example.invalid"}""", """{"email":"other@example.invalid"}""",
        """{"email":"same@example.invalid","hasPersonalAccount":false}""", "{}", "invalid" })
    {
        var clock = new Clock(); PreparedLoginCredential? saved = null;
        var handler = new Handler((request, _) =>
        {
            if (request.Method == HttpMethod.Post) return Task.FromResult(Initiation());
            if (request.RequestUri!.AbsoluteUri != KiloDeviceLoginAdapter.ProfileEndpoint) return Task.FromResult(Approval());
            Check(saved is not null, "issued token checkpoint precedes profile read");
            Check(request.Headers.Authorization?.ToString() == "Bearer " + NewToken
                && !request.Headers.Contains("x-kilocode-organizationid"), "personal profile uses new token without org header");
            return Task.FromResult(Json(profile));
        });
        await using var adapter = new KiloDeviceLoginAdapter(handler, clock);
        var context = Context(clock) with { CheckpointCredentialAsync = (credential, _) => { saved = credential; return Task.CompletedTask; } };
        await adapter.BeginAsync(context, default); var result = await adapter.StatusAsync(context, default);
        var success = profile.Contains("same@example.invalid") && !profile.Contains("false");
        Check(result.State == (success ? "prepared" : "recovery-required"), "personal identity profile shape handled");
        var credential = result.Credential!;
        Check(saved?.PreserveForRecovery == true && credential.Owner == "static-key" && credential.ExpiresAt is null
            && credential.Purposes.SequenceEqual(success ? context.Request.Purposes : []) && saved.Purposes.Length == 0,
            "static owner gates purpose until personal identity; durable recovery and unknown token lifetime");
        Check(credential.PrivateJson.Contains(NewToken) && !credential.PrivateJson.Contains("refresh", StringComparison.OrdinalIgnoreCase)
            && !credential.PrivateJson.Contains("expires", StringComparison.OrdinalIgnoreCase), "no synthetic refresh grant or annual expiry");
        if (success) Check(credential.Binding == new ResourceBindingDto("kilo", "same@example.invalid", Product: "gateway", ResourceType: "personal", Provenance: "observed"), "observed personal binding");
    }
    var time = new Clock(); var handler2 = new Handler((r, _) => Task.FromResult(r.Method == HttpMethod.Post ? Initiation() : Approval()));
    await using var checkpointFailure = new KiloDeviceLoginAdapter(handler2, time);
    var failed = Context(time) with { CheckpointCredentialAsync = (_, _) => throw new IOException("private-fixture-diagnostic") };
    await checkpointFailure.BeginAsync(failed, default); var recovery = await checkpointFailure.StatusAsync(failed, default);
    Check(recovery.State == "recovery-required" && recovery.Credential?.PrivateJson.Contains(NewToken) == true && handler2.Calls == 2
        && !recovery.Error!.Contains("private-fixture-diagnostic"), "checkpoint failure retains issued token without metadata/diagnostic leak");
}

async Task LateApproval()
{
    foreach (var local in new[] { false, true })
    {
        var time = new Clock(); var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); PreparedLoginCredential? saved = null;
        var handler = new Handler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Post) return Initiation();
            sent.SetResult(); await release.Task.WaitAsync(ct); return Approval();
        });
        await using var adapter = new KiloDeviceLoginAdapter(handler, time);
        var context = Context(time) with { CheckpointCredentialAsync = (value, _) => { saved = value; return Task.CompletedTask; } };
        await adapter.BeginAsync(context, default); using var cancel = new CancellationTokenSource();
        var polling = adapter.StatusAsync(context, cancel.Token); await sent.Task;
        if (local) await adapter.CancelAsync(context, default); else cancel.Cancel(); release.SetResult();
        var result = await polling;
        Check(result.State == "recovery-required" && saved?.PrivateJson.Contains(NewToken) == true && handler.Calls == 2,
            "late approved token survives caller/local cancel and skips optional profile");
    }
}

async Task ActualLifecycle(bool alias = false)
{
    // This exercises the actual immutable-generation publication and runtime compiler.
    var paths = new OwnStatePaths(Path.Combine(root, alias ? "actual-alias" : "actual")); var secrets = new SecretStore(paths);
    var repository = new ConfigurationRepository(paths); var catalog = new ProviderCatalogRegistry();
    var accounts = new AccountService(repository, secrets, catalog); var clock = new Clock();
    var connection = catalog.Find("kilo")!.Connections!.Single();
    var routeId = "kilo/liquid/lfm-2.5-2.6b:free";
    var first = accounts.AddApi(0, new("Keep my Kilo name", "kilo", OldToken, connection.BaseUrl,
        Settings: new() { ["billingPlan"] = "free-models" }, ProviderConnectionId: connection.Id, SelectedModelRouteIds: [routeId]));
    var accountId = first.Accounts.Single().Id;
    // Model a previously saved own import with two descriptors and embedded old
    // YAML key fields. The selected catalogue route remains the AddApi route.
    var imported = JsonNode.Parse(secrets.ReadAccount(accountId))!.AsObject();
    if (alias) imported["provider"] = "ai-gateway.kilo.ai";
    imported["sourceConfig"] = "name: kilo\nbase-url: https://ai-gateway.kilo.ai/api/v1\nmodels:\n- name: liquid/lfm-2.5-2.6b:free\n  alias: saved-alias\napi-key-entries:\n- api-key: " + OldToken + "\n";
    imported["entryConfig"] = "api-key: " + OldToken + "\npriority: 2\n";
    var second = imported.DeepClone().AsObject(); second.Remove("providerRouteId"); second.Remove("providerRouteIds"); second.Remove("providerConnectionId");
    second["transportId"] = "own-imported-secondary"; second["sourceConfig"] = "models:\n- name: historical-imported-model\n  alias: historical-alias\napi-key: " + OldToken + "\n";
    imported["transports"] = new JsonArray(imported.DeepClone(), second);
    first = accounts.Connect(first.Revision, new("kilo", "manual-fixture", ["inference", "quota"], AccountId: accountId),
        new("static-key", ["inference", "quota"], new(alias ? "ai-gateway.kilo.ai" : "kilo"), imported.ToJsonString()));
    first = repository.Update(first.Revision, state => state with {
        Accounts = alias ? state.Accounts.Select(a => a with { Provider = "ai-gateway.kilo.ai", Binding = a.Binding! with { Provider = "ai-gateway.kilo.ai" } }).ToArray() : state.Accounts,
        Profiles = [new("profile-keep", "Keep my profile", [state.Routes.Single().Id], state.Routes.Single().Id)] });
    var oldLease = new CredentialResolver(repository, secrets).Resolve(accountId, "inference")!;
    var writer = new ProxyConfigurationWriter(paths, secrets); writer.Write(first);
    var usage = new SqliteUsageStore(Path.Combine(paths.Root, "history.sqlite"), clock);
    var item = new JournalEvent(1, "source-fixture", 1, "event-start", "attempt_started", "request-fixture", "attempt-fixture", accountId,
        "credential-fixture", first.Routes.Single().Id, clock.GetUtcNow(), "kilo", "liquid/lfm-2.5-2.6b:free", "liquid/lfm-2.5-2.6b:free");
    await usage.IngestAsync(new("source-fixture", [item, item with { Sequence = 2, EventId = "event-usage", Kind = "usage",
        Outcome = "success", InputTokens = 1, OutputTokens = 2, TotalTokens = 3, UsageReported = true }], 2, 2));
    var query = new UsageQuery(From: clock.GetUtcNow().AddHours(-1), To: clock.GetUtcNow().AddHours(1), AccountId: accountId);
    var beforeHistory = await usage.AttemptsAsync(query);
    Check(beforeHistory.Total == 1 && beforeHistory.Items.Single().AccountId == accountId, "History fixture contains a real existing attempt");
    var history = JsonSerializer.Serialize(beforeHistory);
    var routes = JsonSerializer.Serialize(first.Routes); var profiles = JsonSerializer.Serialize(first.Profiles);
    var handler = new Handler((request, _) => Task.FromResult(request.Method == HttpMethod.Post ? Initiation()
        : request.RequestUri!.AbsoluteUri == KiloDeviceLoginAdapter.ProfileEndpoint ? Json("""{"email":"same@example.invalid"}""") : Approval()));
    await using var adapter = new KiloDeviceLoginAdapter(handler, clock, repository, secrets);
    await using var service = new LoginOperationService(configuration: repository, accounts: accounts, secrets: secrets, accountAdapters: [adapter], timeProvider: clock);
    var login = await service.BeginAsync(first.Revision, new("kilo", "device", ["inference", "models", "quota"], AccountId: accountId, ExpectedGeneration: first.Accounts.Single().Generation));
    Check(login.State == "waiting" && login.UserCode == "ABCD-1234", "actual lifecycle displays device code");
    login = await service.StatusAsync(login.Id);
    Check(login.State == "completed" && login.Url is null && login.UserCode is null && login.VerificationUri is null, "actual lifecycle publishes and clears device instructions");
    var after = repository.Snapshot;
    Check(after.Accounts.Single().Id == accountId && after.Accounts.Single().Name == "Keep my Kilo name"
        && after.Accounts.Single().Provider == first.Accounts.Single().Provider
        && after.Accounts.Single().Binding!.Provider == first.Accounts.Single().Binding!.Provider
        && after.Accounts.Single().Generation == first.Accounts.Single().Generation + 1, "reconnect preserves opaque account/name/provider/binding alias and publishes next generation");
    Check(JsonSerializer.Serialize(after.Routes) == routes && JsonSerializer.Serialize(after.Profiles) == profiles, "route/profile identities and selections preserved");
    Check(!new CredentialResolver(repository, secrets).IsCurrent(oldLease), "old lease is no longer current");
    var current = JsonNode.Parse(secrets.ReadAccount(accountId))!.AsObject();
    Check(current["providerConnectionId"]!.GetValue<string>() == connection.Id && current["transportId"]!.GetValue<string>() == connection.Id
        && current["providerRouteIds"]!.AsArray().Any(x => x!.GetValue<string>() == routeId) && !current.ToJsonString().Contains(OldToken), "selected connection and model metadata retained; old token removed");
    Check(current["transports"]!.AsArray().Count == 2 && current["transports"]!.AsArray().All(t => t!["apiKey"]!.GetValue<string>() == NewToken)
        && current["transports"]![1]!["transportId"]!.GetValue<string>() == "own-imported-secondary"
        && current["transports"]![1]!["sourceConfig"]!.GetValue<string>().Contains("historical-imported-model"),
        "all descriptor keys replaced; own imported transport/model mapping retained without embedded old secrets");
    writer.Write(after); var yaml = File.ReadAllText(paths.ProxyConfig);
    Check(yaml.Contains(NewToken) && !yaml.Contains(OldToken) && yaml.Contains("liquid/lfm-2.5-2.6b:free") && writer.RouteCredentials.ContainsKey(after.Routes.Single().Id), "actual runtime writer resolves old route through new credential");
    Check(JsonSerializer.Serialize(await usage.AttemptsAsync(query)) == history && await usage.GetCursorAsync("source-fixture") == 2, "actual SQLite History/cursor preserved across reconnect");
    Check(!JsonSerializer.Serialize(after).Contains(NewToken) && !JsonSerializer.Serialize(login).Contains(NewToken), "public config/login contains no token");
    var monitor = new MonitorAccessService(repository, secrets, new MonitorCredentialStore(paths));
    var quotaSource = new HostQuotaSource(repository, secrets, monitor); var quotaCredential = (await quotaSource.GetCredentialAsync(accountId))!;
    var quotaHandler = new Handler((request, _) =>
    {
        Check(request.Method == HttpMethod.Get && request.Headers.Authorization?.ToString() == "Bearer " + NewToken
            && !request.Headers.Contains("x-kilocode-organizationid"), "actual quota handoff uses current personal token only");
        if (request.RequestUri!.AbsoluteUri == KiloQuotaAdapter.Endpoint) return Task.FromResult(Json("""{"balance":0}"""));
        Check(request.RequestUri.AbsoluteUri == KiloPassReader.Endpoint, "quota reads only fixed wallet/Pass endpoints");
        return Task.FromResult(Json("""{"subscription":null}"""));
    });
    using var quotaClient = new HttpClient(quotaHandler);
    var quotaAdapter = new KiloQuotaAdapter(quotaClient);
    Check(quotaAdapter.Supports("ai-gateway.kilo.ai") && !quotaAdapter.Supports("foreign.invalid"), "quota shares only established canonical provider aliases");
    var quota = await quotaAdapter.FetchAsync(after.Accounts.Single(), quotaCredential);
    Check(quota.Readings.Single().Remaining == 0 && quota.Error is null && quotaHandler.Calls == 2, "new key reads true zero wallet with explicit no Pass");
    Check(KiloDeviceLoginAdapter.ConnectionMethod().Purposes.Contains("quota"), "method advertises available quota handoff");
}

async Task UnverifiedRecoveryCannotPublish()
{
    foreach (var profile in new[] { """{"email":"same@example.invalid","hasPersonalAccount":false}""", "invalid",
        """{"email":"other@example.invalid"}""" })
    {
        var paths = new OwnStatePaths(Path.Combine(root, "recovery-" + Guid.NewGuid().ToString("N")));
        var secrets = new SecretStore(paths); var repository = new ConfigurationRepository(paths); var accounts = new AccountService(repository, secrets);
        var clock = new Clock(); var handler = new Handler((request, _) => Task.FromResult(request.Method == HttpMethod.Post ? Initiation()
            : request.RequestUri!.AbsoluteUri == KiloDeviceLoginAdapter.ProfileEndpoint ? Json(profile) : Approval()));
        await using var adapter = new KiloDeviceLoginAdapter(handler, clock, repository, secrets);
        await using var service = new LoginOperationService(configuration: repository, accounts: accounts, secrets: secrets, accountAdapters: [adapter], timeProvider: clock);
        var login = await service.BeginAsync(0, new("kilo", "device", ["inference", "quota"]));
        login = await service.StatusAsync(login.Id);
        Check(login.State == "recovery-required" && repository.Snapshot.Accounts.Length == 0, "failed identity retains token without account publication");
        var recovery = new CredentialRecoveryService(repository, accounts, secrets.Generations);
        await Throws<InvalidDataException>(() => Task.FromResult(recovery.Adopt(0, login.Id)), "Recovery.Adopt cannot bypass personal profile gate");
        Check(repository.Snapshot.Accounts.Length == 0 && handler.Calls == 3, "rejected adoption makes no provider call or account");
    }
}

async Task UnsafeReconnect()
{
    foreach (var organization in new[] { false, true })
    {
        var paths = new OwnStatePaths(Path.Combine(root, "unsafe-" + organization)); var secrets = new SecretStore(paths);
        var repository = new ConfigurationRepository(paths); var accounts = new AccountService(repository, secrets); var clock = new Clock();
        var binding = new ResourceBindingDto("kilo");
        var unsafeJson = JsonSerializer.Serialize(new { kind = "api-key", provider = "kilo", coreProvider = "kilo", sourceSection = "openai-compatibility",
            apiKey = OldToken, baseUrl = organization ? KiloDeviceLoginAdapter.DefaultBaseUrl : "https://foreign.invalid/v1",
            sourceConfig = organization ? "headers:\n  x-kilocode-organizationid: private-org\n" : "models: []\n" });
        var state = accounts.Connect(0, new("kilo", "manual", ["inference"]), new("static-key", ["inference"], binding, unsafeJson));
        var never = new Handler((_, _) => throw new Exception("unexpected HTTP"));
        await using var adapter = new KiloDeviceLoginAdapter(never, clock, repository, secrets);
        var context = accounts.ReserveConnect(state.Revision, new("kilo", "device", ["inference"], AccountId: state.Accounts.Single().Id), "device-unsafe", clock.GetUtcNow().AddMinutes(10))
            with { CheckpointCredentialAsync = (_, _) => Task.CompletedTask };
        await Throws<InvalidOperationException>(() => adapter.BeginAsync(context, default), "foreign/org transport rejected before authorization");
        Check(never.Calls == 0 && secrets.ReadAccount(state.Accounts.Single().Id).Contains(OldToken), "unsafe reconnect preserves prior key with zero HTTP");
    }
}

sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    public int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    { Interlocked.Increment(ref Calls); return send(request, ct); }
}
sealed class Clock : TimeProvider
{
    private DateTimeOffset now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan amount) => now += amount;
}
