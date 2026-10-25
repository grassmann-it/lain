using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lain.Client;
using Lain.Cli;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Accounts.Credentials;
using Lain.Host.Accounts.Monitoring;
using Lain.Host.Operations;
using Lain.Host.Providers.Xai;
using Lain.Host.Quota;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

var checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
var root = Path.Combine(Path.GetTempPath(), "lain-xai-metadata-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var paths = new OwnStatePaths(root);
    var repository = new ConfigurationRepository(paths);
    var secrets = new SecretStore(paths);
    var accounts = new AccountService(repository, secrets);
    using var gate = new SemaphoreSlim(1, 1);
    using var provider = new ProviderHandler();
    using var providerHttp = new HttpClient(provider);
    var service = new XaiKeyMetadataService(repository, secrets, accounts, providerHttp, gate);
    var monitors = new MonitorAccessService(repository, secrets, new(paths));
    var methods = new ConnectionMethodCatalog(new Lain.Host.Providers.ProviderCatalogRegistry(), () => false, () => false, () => false);
    var reconnect = new XaiCredentialReconnectService(repository, secrets, accounts, methods, monitors, providerHttp, gate);
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Production" });
    builder.Logging.ClearProviders();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Services.AddSingleton(service);
    builder.Services.AddSingleton(reconnect);
    await using var app = builder.Build();
    app.MapXaiKeyMetadata();
    app.MapPost(Api.Prefix + "/accounts/connect", async Task<IResult> (RevisionRequest<ConnectCredentialRequest> request,
        XaiCredentialReconnectService connector, CancellationToken ct) =>
    {
        try { return Results.Ok(await connector.ConnectAsync(request.Revision, request.Value, ct)); }
        catch (RevisionConflictException) { return Results.Conflict(new ApiError("revision_conflict", "Synthetic revision conflict")); }
        catch (KeyNotFoundException) { return Results.NotFound(new ApiError("not_found", "Synthetic missing target")); }
        catch (ArgumentException) { return Results.BadRequest(new ApiError("validation", "Synthetic validation failure")); }
        catch (InvalidOperationException) { return Results.Conflict(new ApiError("operation_unavailable", "Synthetic unavailable reconnect")); }
    });
    app.MapGet(Api.Prefix + "/configuration", () => repository.Snapshot);
    await app.StartAsync();
    var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    using var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { BaseAddress = new(address) };
    using var client = new LainApiClient(http);

    AccountDto Add(string name = "xAI fixture", string providerId = "xai", string endpoint = "https://api.x.ai/v1",
        string[]? purposes = null, ResourceBindingDto? binding = null, string kind = "api-key")
    {
        var before = repository.Snapshot;
        var request = new ConnectAccountRequest(providerId, "api-key", purposes ?? ["inference", "models"], Name: name, Binding: binding);
        var saved = accounts.Connect(before.Revision, request, new("static-key", request.Purposes, binding ?? new(providerId),
            JsonSerializer.Serialize(new { kind, provider = providerId, apiKey = ProviderHandler.Secret, accessToken = ProviderHandler.Secret, baseUrl = endpoint })));
        return saved.Accounts.Last();
    }
    Task<HttpResponseMessage> Read(AccountDto account, long? revision = null, long? generation = null) =>
        http.PostAsJsonAsync(Api.Prefix + "/accounts/" + Uri.EscapeDataString(account.Id) + "/metadata/xai",
            new RevisionRequest<AccountMetadataDiscoveryRequest>(revision ?? repository.Snapshot.Revision, new(generation ?? account.Generation)));
    async Task Rejected(AccountDto account, HttpStatusCode expected, string name, long? revision = null, long? generation = null)
    {
        var before = JsonSerializer.Serialize(repository.Snapshot);
        using var result = await Read(account, revision, generation);
        var body = await result.Content.ReadAsStringAsync();
        Check(result.StatusCode == expected && before == JsonSerializer.Serialize(repository.Snapshot), name + " preserves configuration");
        Check(!body.Contains(ProviderHandler.Secret) && !body.Contains("remote-body"), name + " redacts credentials and provider bodies");
    }

    var original = Add();
    var before = repository.Snapshot;
    var privateBefore = new CredentialResolver(repository, secrets).Resolve(original.Id, "inference")!.PrivateJson;
    var discovered = await client.DiscoverXaiTeamAsync(before.Revision, original.Id, original.Generation);
    var observed = discovered.Accounts.Single();
    Check(provider.Calls == 1 && observed.Binding is { ResourceType: "team", ResourceId: "actual-team", Provenance: "provider-discovery" }, "typed client reaches real Host HTTP endpoint and binds actual returned team");
    Check(discovered.Revision == before.Revision + 1 && observed.Generation == original.Generation + 1, "discovery publishes one guarded observation");
    Check(observed.Id == original.Id && observed.Name == original.Name && JsonSerializer.Serialize(observed.Credentials) == JsonSerializer.Serialize(original.Credentials)
        && observed.Capabilities!.SequenceEqual(original.Capabilities!) && observed.Capabilities!.All(c => c.Purpose != "quota"), "discovery preserves credentials/capabilities and creates no quota ability");
    Check(discovered.Routes.Length == 0 && discovered.Profiles.Length == 0 && discovered.AppliedRevision == before.AppliedRevision, "discovery creates no model route, profile or Apply");
    Check(new CredentialResolver(repository, secrets).Resolve(original.Id, "inference")!.PrivateJson == privateBefore, "saved key is unchanged");
    var unchanged = await client.DiscoverXaiTeamAsync(discovered.Revision, observed.Id, observed.Generation);
    Check(unchanged.Revision == discovered.Revision && unchanged.Accounts.Single().Generation == observed.Generation, "already confirmed same team is an identity no-op");

    var calls = provider.Calls;
    await Rejected(observed, HttpStatusCode.Conflict, "stale revision", revision: discovered.Revision - 1);
    await Rejected(observed, HttpStatusCode.Conflict, "stale generation", generation: observed.Generation - 1);
    var nonXai = Add(providerId: "openrouter");
    await Rejected(nonXai, HttpStatusCode.Conflict, "wrong provider");
    var wrongTarget = Add(endpoint: "https://untrusted.invalid/v1");
    await Rejected(wrongTarget, HttpStatusCode.BadGateway, "wrong transport host");
    var quotaOnly = Add(purposes: ["quota"]);
    await Rejected(quotaOnly, HttpStatusCode.Conflict, "quota-only token cannot be repurposed as metadata key");
    var wrongKind = Add(kind: "access-token-snapshot");
    await Rejected(wrongKind, HttpStatusCode.Conflict, "wrong credential kind");
    var expired = Add();
    repository.Update(repository.Snapshot.Revision, config => config with
    {
        Accounts = config.Accounts.Select(a => a.Id == expired.Id ? a with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) } : a).ToArray()
    });
    await Rejected(expired, HttpStatusCode.Conflict, "expired selected inference key");
    Check(provider.Calls == calls, "preflight failures send no provider requests");

    var modelsOnly = Add(purposes: ["models"]);
    using (var result = await Read(modelsOnly)) Check(result.IsSuccessStatusCode, "saved model API key can discover its team");
    provider.Response = _ => Task.FromResult(ProviderHandler.Json("{\"team_id\":\"other-team\"}"));
    await Rejected(observed, HttpStatusCode.BadGateway, "different returned team");
    var unknown = Add();
    foreach (var payload in new[] { "{}", "{\"api_key_id\":\"actual-key\"}", "{\"team_id\":null}", "{\"team_id\":\"\"}" })
    {
        provider.Response = _ => Task.FromResult(ProviderHandler.Json(payload));
        await Rejected(unknown, HttpStatusCode.Conflict, "unknown team");
    }
    provider.Response = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent(ProviderHandler.Secret + "remote-body") });
    await Rejected(unknown, HttpStatusCode.BadGateway, "provider authentication failure");

    async Task Late(string name, Func<AccountDto, Task> mutate)
    {
        var target = Add(name);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.Response = async _ => { entered.SetResult(); await release.Task; return ProviderHandler.Team(); };
        var pending = Read(target);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Exactly the gate used by the production discovery service: mutation can
        // complete while its provider request is held, before late publication.
        await gate.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        try { await mutate(target); }
        finally { gate.Release(); }
        var after = JsonSerializer.Serialize(repository.Snapshot);
        release.SetResult();
        using var response = await pending;
        Check(response.StatusCode == HttpStatusCode.Conflict && JsonSerializer.Serialize(repository.Snapshot) == after, name + " refuses late publication");
    }
    await Late("late disconnect", target => { accounts.Remove(repository.Snapshot.Revision, target.Id); return Task.CompletedTask; });
    await Late("late disable", target => { accounts.SetEnabled(repository.Snapshot.Revision, target.Id, false); return Task.CompletedTask; });
    await Late("late unrelated revision", _ => { repository.Update(repository.Snapshot.Revision, config => config); return Task.CompletedTask; });
    await Late("late reconnect", target =>
    {
        var request = new ConnectAccountRequest("xai", "api-key", ["inference"], AccountId: target.Id, ExpectedGeneration: target.Generation);
        accounts.Connect(repository.Snapshot.Revision, request, new("static-key", request.Purposes, new("xai"),
            JsonSerializer.Serialize(new { kind = "api-key", provider = "xai", apiKey = "replacement-fixture-key", baseUrl = "https://api.x.ai/v1" })));
        return Task.CompletedTask;
    });
    await Late("late selected credential replacement", target =>
    {
        // Retirement changes the selected credential's availability without a
        // public configuration edit. The old read must still be refused.
        var handle = target.Credentials!.Single();
        secrets.Generations.RetireForRotation(handle, "fixture-rotation");
        return Task.CompletedTask;
    });

    provider.Response = _ => Task.FromResult(ProviderHandler.Team());
    var cliTarget = Add("CLI xAI fixture");
    var revision = repository.Snapshot.Revision;
    var output = new StringWriter(); var error = new StringWriter();
    var exit = await CliApplication.RunAsync(["accounts", "discover", cliTarget.Id, "--revision", revision.ToString(), "--json"], output, error,
        () => new LainApiClient(http));
    Check(exit == 0 && error.ToString() == "", "CLI discover completes over Host HTTP");
    using (var json = JsonDocument.Parse(output.ToString()))
        Check(json.RootElement.GetProperty("accounts").EnumerateArray().Single(a => a.GetProperty("id").GetString() == cliTarget.Id)
            .GetProperty("binding").GetProperty("resourceId").GetString() == "actual-team", "CLI JSON returns the observed team on stable account ID");
    calls = provider.Calls;
    output = new(); error = new();
    exit = await CliApplication.RunAsync(["accounts", "discover", cliTarget.Id, "--revision", revision.ToString(), "--json"], output, error,
        () => new LainApiClient(http));
    Check(exit == 5 && provider.Calls == calls, "CLI stale explicit revision rejects before provider request");
    output = new(); error = new();
    exit = await CliApplication.RunAsync(["accounts", "discover", nonXai.Id, "--json"], output, error, () => new LainApiClient(http));
    Check(exit == 2 && provider.Calls == calls, "CLI refuses discovery for other providers");

    var reconnectTarget = Add("Confirmed reconnect target");
    var proven = await client.DiscoverXaiTeamAsync(repository.Snapshot.Revision, reconnectTarget.Id, reconnectTarget.Generation);
    reconnectTarget = proven.Accounts.Single(a => a.Id == reconnectTarget.Id);
    const string replacement = "replacement-explicit-fixture-key";
    provider.ExpectedSecret = replacement;
    var replacementRequest = new ConnectCredentialRequest(new("xai", "api-key", ["inference", "models"], AccountId: reconnectTarget.Id,
        Binding: reconnectTarget.Binding, ExpectedGeneration: reconnectTarget.Generation), replacement, BaseUrl: "https://api.x.ai/v1");
    var reconnected = await client.ConnectAccountAsync(proven.Revision, replacementRequest);
    var afterReconnect = reconnected.Accounts.Single(a => a.Id == reconnectTarget.Id);
    Check(afterReconnect.Id == reconnectTarget.Id && afterReconnect.Name == reconnectTarget.Name
        && afterReconnect.Binding is { ResourceType: "team", ResourceId: "actual-team", Provenance: "provider-discovery" },
        "ordinary typed ConnectAccount reconnect preserves confirmed target after proving NEW key team");
    Check(afterReconnect.Generation == reconnectTarget.Generation + 1 && reconnected.Revision == proven.Revision + 1
        && afterReconnect.Capabilities!.Select(c => c.Purpose).Order().SequenceEqual(reconnectTarget.Capabilities!.Select(c => c.Purpose).Order()),
        "verified reconnect advances one generation without expanding capability purposes");
    using (var json = JsonDocument.Parse(new CredentialResolver(repository, secrets).Resolve(afterReconnect.Id, "inference")!.PrivateJson))
        Check(json.RootElement.GetProperty("apiKey").GetString() == replacement, "new supplied credential becomes selected only after independent metadata proof");
    async Task ReconnectRejected(string name, string payload, HttpStatusCode expected)
    {
        provider.Response = _ => Task.FromResult(ProviderHandler.Json(payload));
        var current = repository.Snapshot;
        var account = current.Accounts.Single(a => a.Id == afterReconnect.Id);
        var oldKey = new CredentialResolver(repository, secrets).Resolve(account.Id, "inference")!.PrivateJson;
        using var result = await http.PostAsJsonAsync(Api.Prefix + "/accounts/connect", new RevisionRequest<ConnectCredentialRequest>(current.Revision,
            replacementRequest with { Connection = replacementRequest.Connection with { ExpectedGeneration = account.Generation } }));
        Check(result.StatusCode == expected && JsonSerializer.Serialize(current) == JsonSerializer.Serialize(repository.Snapshot)
            && oldKey == new CredentialResolver(repository, secrets).Resolve(account.Id, "inference")!.PrivateJson,
            name + " keeps old key, binding, routes and revisions unchanged");
    }
    await ReconnectRejected("replacement key with unknown team", "{\"api_key_id\":\"new-key-id\"}", HttpStatusCode.Conflict);
    await ReconnectRejected("replacement key with different team despite claimed verified binding", "{\"team_id\":\"foreign-team\"}", HttpStatusCode.Conflict);
    provider.Response = _ => Task.FromResult(ProviderHandler.Team());
    var reconnectCalls = provider.Calls;
    var beforeInvalid = repository.Snapshot;
    using (var result = await http.PostAsJsonAsync(Api.Prefix + "/accounts/connect", new RevisionRequest<ConnectCredentialRequest>(beforeInvalid.Revision,
        replacementRequest with { BaseUrl = "https://untrusted.invalid/v1", Connection = replacementRequest.Connection with { ExpectedGeneration = afterReconnect.Generation } })))
        Check(result.StatusCode == HttpStatusCode.BadRequest && provider.Calls == reconnectCalls, "reconnect uses unchanged catalog endpoint validation before provider HTTP");
    var legacyRejected = false;
    try { accounts.ReplaceApiCredential(beforeInvalid.Revision, afterReconnect.Id, "unproved-legacy-key"); }
    catch (InvalidOperationException) { legacyRejected = true; }
    Check(legacyRejected && repository.Snapshot.Revision == beforeInvalid.Revision, "legacy replacement entry remains fail-closed for confirmed xAI identity");

    var reconnectEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var reconnectRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    provider.Response = async _ => { reconnectEntered.SetResult(); await reconnectRelease.Task; return ProviderHandler.Team(); };
    var heldRevision = repository.Snapshot.Revision;
    var held = http.PostAsJsonAsync(Api.Prefix + "/accounts/connect", new RevisionRequest<ConnectCredentialRequest>(heldRevision,
        replacementRequest with { Connection = replacementRequest.Connection with { ExpectedGeneration = afterReconnect.Generation } }));
    await reconnectEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await gate.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    try { accounts.Remove(repository.Snapshot.Revision, afterReconnect.Id); }
    finally { gate.Release(); }
    var removedState = JsonSerializer.Serialize(repository.Snapshot);
    reconnectRelease.SetResult();
    using (var result = await held)
        Check(result.StatusCode == HttpStatusCode.Conflict && JsonSerializer.Serialize(repository.Snapshot) == removedState,
            "ordinary reconnect HTTP releases gate during provider wait and cannot resurrect removed target");
    provider.ExpectedSecret = ProviderHandler.Secret;
    provider.Response = _ => Task.FromResult(ProviderHandler.Team());

    var google = MonitorAccessCatalog.Options("gemini-api").Single().Description;
    var cloudflare = MonitorAccessCatalog.Options("cloudflare-workers-ai");
    Check(google.Contains("Google Cloud CLI") && google.Contains("broad permissions") && !google.Contains("not implemented"), "Google monitor instructions expose current renewable access and grant breadth");
    Check(cloudflare.All(o => o.Description.Contains("estimated") && !o.Description.Contains("not yet qualified")), "Cloudflare instructions describe implemented estimated neuron reads");
    var cfId = "0123456789abcdef0123456789abcdef";
    var cf = Add(providerId: "cloudflare-workers-ai", endpoint: "https://api.cloudflare.com/client/v4/accounts/" + cfId + "/ai/v1");
    var beforeMonitor = repository.Snapshot;
    monitors.Set(beforeMonitor.Revision, cf.Id, new("inference-key", null, cfId));
    var credential = await new HostQuotaSource(repository, secrets, monitors).GetCredentialAsync(cf.Id);
    Check(credential is { Monitor.Kind: "inference-key", Monitor.Secret: null } && credential.BearerToken == ProviderHandler.Secret,
        "Cloudflare saved inference key is reused through real monitor service and HostQuotaSource without copying a second secret");
    using var cfHandler = new CloudflareHandler();
    using var adapter = new CloudflareQuotaAdapter(handler: cfHandler);
    var resultCf = await adapter.FetchAsync(repository.Snapshot.Accounts.Single(a => a.Id == cf.Id), credential!);
    Check(cfHandler.Calls == 1 && resultCf.Readings.Single() is { Used: 17, Remaining: null, Limit: null }, "same-key Cloudflare path reads usage with no invented budget");

    await app.StopAsync();
    Console.WriteLine($"PASS {checks} xAI metadata/CLI and monitor-source checks; real loopback Host HTTP, fake provider HTTP only.");
}
finally { Directory.Delete(root, true); }

sealed class ProviderHandler : HttpMessageHandler
{
    public const string Secret = "synthetic-xai-fixture-key";
    public string ExpectedSecret = Secret;
    public int Calls;
    public Func<HttpRequestMessage, Task<HttpResponseMessage>> Response = _ => Task.FromResult(Team());
    public static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    public static HttpResponseMessage Team() => Json("{\"team_id\":\"actual-team\",\"api_key_id\":\"actual-key\",\"acls\":[\"api-key:model:*\"]}");
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Method != HttpMethod.Get || request.RequestUri?.AbsoluteUri != XaiKeyMetadataReader.Endpoint || request.Headers.Authorization?.Parameter != ExpectedSecret)
            throw new Exception("Metadata escaped its fixed endpoint or selected synthetic key.");
        Interlocked.Increment(ref Calls);
        return Response(request);
    }
}

sealed class CloudflareHandler : HttpMessageHandler
{
    public int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Headers.Authorization?.Parameter != ProviderHandler.Secret || request.Method != HttpMethod.Post || request.RequestUri?.AbsoluteUri != CloudflareWorkersAiReader.Endpoint)
            throw new Exception("Cloudflare did not reuse the saved synthetic key at its fixed endpoint.");
        Calls++;
        return Task.FromResult(ProviderHandler.Json("{\"data\":{\"viewer\":{\"accounts\":[{\"accountTag\":\"0123456789abcdef0123456789abcdef\",\"aiInferenceAdaptiveGroups\":[{\"sum\":{\"totalNeurons\":17}}]}]}}}"));
    }
}
