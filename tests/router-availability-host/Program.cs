using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Accounts.Monitoring;
using Lain.Host.Operations;
using Lain.Host.Quota;

var checks = 0;
void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
    checks++;
}
var now = DateTimeOffset.Parse("2026-10-26T10:00:00Z");
var clock = new FakeClock(now);
var kimi = new AccountDto("kimi-account", "Kimi", "kimi", true, "api-key", "available");
var zai = new AccountDto("zai-account", "Z.ai", "zai", true, "api-key", "available");
var configuration = new ConfigurationDto(7, 7, [kimi, zai],
    [new("kimi-route", "kimi", "kimi-k2.5", "kimi", [kimi.Id]), new("zai-route", "glm", "glm-4.7", "zai", [zai.Id])],
    [new("profile", "Test", ["kimi-route", "zai-route"])], CollectionRouters:
    [new("router", "auto", "collection:test", "profile", "configured", true)]);
RouterAvailabilityMember Member(string provider) => new("router", "profile", provider + "-route", new string('a', 64), provider + "-account",
    provider, provider == "kimi" ? "kimi-k2.5" : "glm-4.7");
var members = new[] { Member("kimi"), Member("zai") };
RouterAvailabilityQuotaProof Proof(string provider) => new(provider + "-account", 0, provider,
    provider == "kimi" ? "https://api.kimi.com/coding/v1" : "https://api.z.ai/api/coding/paas/v4", "binding-v1");
QuotaDto Reading(string provider) => new(provider + "-account", null,
    provider == "kimi" ? KimiQuotaAdapter.Endpoint + "#limit_5h" : ZaiQuotaAdapter.Endpoint + "#TOKENS_LIMIT/unit/3/number/5",
    "ok", now, now, now.AddHours(2), provider == "kimi" ? "percent" : "tokens", 100, 100, 0, false, UsedPercent: 100);
RouterAvailabilityQuotaSnapshot Snapshot(string provider) => new(Reading(provider), Proof(provider));
var snapshots = new[] { Snapshot("kimi"), Snapshot("zai") };
var projector = new RouterAvailabilityProjector(clock);
var projected = projector.Project(configuration, members, snapshots);
Check(projected.Length == 2 && projected.All(e => e.BlockedUntil == now.AddMinutes(5)), "legacy Kimi/Zai coding exhaustion projected with capped original freshness");
Check(projected[1] is { AccountId: "zai-account", Model: "glm-4.7", RouterId: "router", ProfileId: "profile", Reason: "quota_exhausted" }, "GLM coding exhaustion retains exact identity");
Check(projector.Project(configuration, members, [new(Reading("zai") with {
    Source = ZaiQuotaAdapter.Endpoint + "#CREDIT_LIMIT/unit/3/number/5", Unit = "credits"
}, Proof("zai"))]).Single().RouteId == "zai-route", "deployed Zai CREDIT_LIMIT coding source suppresses only its GLM route");

void Unknown(QuotaDto reading, string message) => Check(projector.Project(configuration, members,
    [new(reading, Proof("zai"))]).Length == 0, message);
foreach (var state in new[] { "stale", "unknown", "error", "refreshing", "disabled", "unsupported" })
    Unknown(Reading("zai") with { State = state }, "non-ok " + state + " remains unknown");
Unknown(Reading("zai") with { Error = "sanitized fixture" }, "partial/error observation remains unknown");
Unknown(Reading("zai") with { LastSuccessAt = null }, "missing successful observation remains unknown");
Unknown(Reading("zai") with { ObservedAt = null }, "missing observation time remains unknown");
Unknown(Reading("zai") with { ObservedAt = now.AddSeconds(1) }, "future observation rejected");
Unknown(Reading("zai") with { LastSuccessAt = now.AddSeconds(1) }, "future success rejected");
Unknown(Reading("zai") with { DataAsOf = now.AddSeconds(1) }, "future data timestamp rejected");
Unknown(Reading("zai") with { FreshUntil = now }, "expired explicit freshness rejected");
Unknown(Reading("zai") with { ResetsAt = null }, "unknown reset remains unknown without short speculative TTL");
Unknown(Reading("zai") with { ResetsAt = now }, "reset immediately releases block");
Unknown(Reading("zai") with { WindowEnd = now.AddSeconds(-1) }, "elapsed window overrides future reset");
Unknown(Reading("zai") with { Source = ZaiQuotaAdapter.Endpoint + "#TIME_LIMIT/unit/3/number/5", Unit = "requests" }, "MCP quota does not suppress GLM inference");
Unknown(Reading("zai") with { MeasurementKind = "rate" }, "rate is not quota");
Unknown(Reading("zai") with { MeasurementKind = "balance" }, "balance is not quota");
Unknown(Reading("zai") with { Unit = "usd" }, "money is not coding allowance");
Unknown(Reading("zai") with { Scope = "project", ScopeId = "other-project" }, "foreign resource remains unknown");
Unknown(Reading("zai") with { ScopeId = "other-account" }, "unmatched resource id remains unknown");
Unknown(Reading("zai") with { Model = "different-model" }, "other model remains unknown");
Unknown(Reading("zai") with { Source = "https://unknown.test/quota" }, "generic exhausted source remains unknown");
Unknown(Reading("zai") with { Used = 0, Limit = 0, Remaining = null, UsedPercent = null }, "zero limit alone is not exhaustion");
Unknown(Reading("zai") with { Used = 0, Limit = 0, Remaining = 0, UsedPercent = null }, "zero allocation is not a proven exhausted subscription");
Unknown(Reading("zai") with { Remaining = 10 }, "conflicting remaining keeps availability unknown");
Unknown(Reading("zai") with { UsedPercent = double.NaN }, "nonfinite quantities rejected");
Unknown(Reading("zai") with { UsedPercent = 101 }, "invalid percentage rejected");
Check(projector.Project(configuration, members, [new(Reading("zai"), null)]).Length == 0, "missing resource proof unknown");
Check(projector.Project(configuration, members, [new(Reading("zai"), Proof("zai") with { AccountGeneration = 1 })]).Length == 0, "account generation mismatch unknown");
Check(projector.Project(configuration with { AppliedRevision = 6 }, members, snapshots).Length == 0, "pending apply never attributed to active bindings");
Check(projector.Project(configuration with { RuntimeMode = "monitor" }, members, snapshots).Length == 0, "monitor mode publishes no suppression");
Check(projector.Project(configuration with { Accounts = [kimi with { Enabled = false }, zai with { Enabled = false }] }, members, snapshots).Length == 0, "disabled accounts never projected");
Check(projector.Project(configuration with { Routes = configuration.Routes.Select(r => r with { Pooled = true }).ToArray() }, members, snapshots).Length == 0, "pooled routes never assigned one account quota");
var earlier = projector.Project(configuration, members, [new(Reading("zai") with { FreshUntil = now.AddSeconds(20), ResetsAt = now.AddSeconds(10) }, Proof("zai"))]);
Check(earlier.Single().BlockedUntil == now.AddSeconds(10), "reset bounds explicit freshness");
Check(projector.Project(configuration, members, [new(Reading("zai") with { FreshUntil = now.AddDays(2) }, Proof("zai"))]).Single().BlockedUntil == now.AddMinutes(5), "overlong provider freshness cannot extend cap");
foreach (var row in new[] {
    Reading("zai") with { Used = null, Limit = null, Remaining = 0, UsedPercent = null },
    Reading("zai") with { Used = 200, Limit = 100, Remaining = null, UsedPercent = null },
    Reading("zai") with { Used = null, Limit = null, Remaining = null, UsedPercent = 100 } })
    Check(projector.Project(configuration, members, [new(row, Proof("zai"))]).Length == 1, "independent strong exhaustion signal supported");
clock.Now = now.AddMinutes(4);
Check(projector.Project(configuration, members, snapshots).All(e => e.BlockedUntil == now.AddMinutes(5)), "republication cannot slide original observation TTL");
clock.Now = now.AddMinutes(5);
Check(projector.Project(configuration, members, snapshots).Length == 0, "cache expires without provider refresh");
clock.Now = now;

// Capture uses only fixture JSON already in memory. No fixture key is sent anywhere.
var proofs = new RouterAvailabilityProofStore();
string Private(string endpoint, object[]? transports = null) => JsonSerializer.Serialize(new {
    kind = "api-key", apiKey = "fixture-key", provider = "zai", baseUrl = endpoint, transports });
var codeEndpoint = "https://api.z.ai/api/coding/paas/v4";
var plain = "{\"kind\":\"api-key\",\"apiKey\":\"fixture-key\",\"baseUrl\":\"https://api.z.ai/api/coding/paas/v4/\"}";
proofs.CaptureLegacy(zai, plain, plain, "v1");
Check(proofs.Get(zai.Id, "v1")?.Endpoint == codeEndpoint, "single primary coding endpoint normalized");
Check(proofs.Get(zai.Id, "v2") is null, "resource proof cannot cross credential binding version");
var variant = new { kind = "api-key", apiKey = "fixture-key", provider = "zai", baseUrl = codeEndpoint };
var single = Private("https://custom.test/v1", [variant]);
proofs.CaptureLegacy(zai, single, plain, "v1");
Check(proofs.Get(zai.Id, "v1") is not null, "one selected official coding variant can be proven");
proofs.CaptureLegacy(zai, Private(codeEndpoint, [variant, variant]), plain, "v1");
Check(proofs.Get(zai.Id, "v1") is null, "multiple variants ambiguous even if coding endpoint repeats");
proofs.CaptureLegacy(zai, Private("https://api.z.ai/api/paas/v4", [variant]), Private("https://api.z.ai/api/paas/v4"), "v1");
Check(proofs.Get(zai.Id, "v1") is null, "payg primary cannot borrow coding variant quota");
proofs.CaptureLegacy(zai, plain, plain.Replace("fixture-key", "other-fixture-key"), "v1");
Check(proofs.Get(zai.Id, "v1") is null, "different monitor key cannot prove inference key");
proofs.CaptureLegacy(zai with { Credentials = [new("monitor", 1, "fixture", ["quota"], new("zai"))] }, plain, plain, "v1");
Check(proofs.Get(zai.Id, "v1") is null, "separate handle requires qualified inference/monitor join");
proofs.CaptureLegacy(zai, "invalid fixture JSON", plain, "v1");
Check(proofs.Get(zai.Id, "v1") is null, "optional evidence parsing fails open");
Check(RouterAvailabilityProofStore.CodingEndpoint("zai", "https://api.z.ai/api/paas/v4") is null, "payg endpoint unsupported");
Check(RouterAvailabilityProofStore.CodingEndpoint("kimi", "https://api.kimi.ai/coding/v1/") is not null, "official Kimi coding host alias supported");
Check(RouterAvailabilityProofStore.CodingEndpoint("zai", codeEndpoint + "?resource=other") is null, "query-scoped endpoint not normalized into proof");
var boundedProofs = new RouterAvailabilityProofStore();
for (var index = 0; index < RouterAvailabilityProofStore.MaximumEntries; index++)
    boundedProofs.CaptureLegacy(zai with { Id = "bounded-" + index }, plain, plain, "v1");
boundedProofs.CaptureLegacy(zai, plain, plain, "v1");
Check(boundedProofs.Get(zai.Id, "v1") is null && boundedProofs.Get("bounded-0", "v1") is not null,
    "proof store capacity leaves new accounts unknown and retains existing account proof");
boundedProofs.CaptureLegacy(zai with { Id = "bounded-0", Generation = 1 }, plain, plain, "v2");
Check(boundedProofs.Get("bounded-0", "v2")?.AccountGeneration == 1 && boundedProofs.Get("bounded-0", "v1") is null,
    "retained current account can replace its generation at full capacity");
boundedProofs.Remove("bounded-0");
boundedProofs.CaptureLegacy(zai, plain, plain, "v1");
Check(boundedProofs.Get(zai.Id, "v1") is not null, "removed proof releases capacity for a new current account");

var descriptors = new RouterAvailabilityBindings("fixture-descriptors-v1", members);
var bindingsJson = JsonSerializer.SerializeToUtf8Bytes(new { routes = new Dictionary<string, object> {
    ["glm"] = new { id = "zai-route", model = "glm-4.7", accounts = new[] { "zai-account" }, selection = "strict", provider = "zai", upstreamProtocol = "openai", credentials = new { ignored = "opaque-id" } }
}, routers = new Dictionary<string, object> { ["auto"] = new { id = "router", profileId = "profile", selection = "ordered_sticky_v1",
    routeIds = new[] { "zai-route" }, memberFingerprints = new Dictionary<string, string> { ["zai-route"] = new('a', 64) } } } });
var parsedBindings = RouterAvailabilityBindings.Parse(bindingsJson);
Check(parsedBindings.Members.Single() == members[1], "generated bindings project exact nonsecret member identity");
Check(RouterAvailabilityBindings.Parse(Encoding.UTF8.GetBytes("{\"routes\":{}}" )).Members.Length == 0, "no routers produces empty replacement");

var fake = new FakeCore();
using var http = new HttpClient(fake) { BaseAddress = new Uri("http://127.0.0.1:12345/") };
http.DefaultRequestHeaders.Authorization = new("Bearer", "synthetic-auth");
var currentConfiguration = configuration;
var currentBindings = descriptors;
var currentSnapshots = snapshots;
var snapshotReads = 0;
var bindingReads = 0;
Func<Task>? onSnapshot = null;
var coreReady = true;
var publisher = new RouterAvailabilityPublisher(http, () => currentConfiguration, async _ => {
    Check(fake.HasReadGeneration, "generation bound before reading quota observations");
    snapshotReads++;
    if (onSnapshot is not null) await onSnapshot();
    return currentSnapshots;
}, _ => { bindingReads++; return Task.FromResult(currentBindings); }, clock, () => coreReady);
currentConfiguration = configuration with { RuntimeMode = "monitor" };
Check(!await publisher.PublishOnceAsync() && fake.Requests == 0 && snapshotReads == 0 && bindingReads == 0,
    "monitor mode makes no Core requests or quota credential reads");
currentConfiguration = configuration;
coreReady = false;
Check(!await publisher.PublishOnceAsync() && fake.Requests == 0,
    "stopped or login-only Core is not contacted");
coreReady = true;
Check(await publisher.PublishOnceAsync(), "publisher accepts core 204");
Check(fake.Last is { SchemaVersion: 1, Sequence: 1 } && fake.Last.Entries.Length == 2 && fake.Last.Generation == fake.Generation, "full wire contract and CAS sequence");
Check(fake.Last!.ExpiresAt - fake.Last.ObservedAt == TimeSpan.FromMinutes(5), "publisher envelope max 5 minutes");
currentSnapshots = [];
Check(await publisher.PublishOnceAsync() && fake.Last!.Entries.Length == 0 && fake.Sequence == 2, "empty snapshot replaces old blocks completely");
currentSnapshots = snapshots;
currentConfiguration = configuration with { Revision = 8 };
var readsBefore = snapshotReads;
Check(await publisher.PublishOnceAsync() && fake.Last!.Entries.Length == 0 && snapshotReads == readsBefore, "saved/unapplied config clears suppression without reading credentials");
currentConfiguration = configuration;
onSnapshot = () => { currentConfiguration = configuration with { Accounts = [kimi with { Generation = 1 }, zai] }; return Task.CompletedTask; };
var postsBefore = fake.Posts;
Check(!await publisher.PublishOnceAsync() && fake.Posts == postsBefore, "account generation changes during projection discard batch");
onSnapshot = null;
currentConfiguration = configuration;
onSnapshot = () => { currentConfiguration = configuration with { AppliedRevision = 6 }; return Task.CompletedTask; };
Check(!await publisher.PublishOnceAsync() && fake.Posts == postsBefore, "applied revision race discards batch");
onSnapshot = () => { currentBindings = descriptors with { Fingerprint = "changed" }; return Task.CompletedTask; };
currentConfiguration = configuration;
Check(!await publisher.PublishOnceAsync() && fake.Posts == postsBefore, "bindings replacement race discards batch");
currentBindings = descriptors;
onSnapshot = () => { fake.Generation = Guid.NewGuid().ToString(); return Task.CompletedTask; };
Check(!await publisher.PublishOnceAsync() && fake.Posts == postsBefore + 1, "core restart rejects old-generation batch");
onSnapshot = null;
Check(await publisher.PublishOnceAsync() && fake.Last!.Generation == fake.Generation, "next tick binds fresh core generation");
onSnapshot = () => { fake.Sequence++; return Task.CompletedTask; };
Check(!await publisher.PublishOnceAsync(), "competing newer sender sequence rejected by CAS");
onSnapshot = null;
Check(await publisher.PublishOnceAsync(), "sequence conflict retries only with new GET and observations");
fake.FailGet = true;
readsBefore = snapshotReads;
Check(!await publisher.PublishOnceAsync() && snapshotReads == readsBefore, "GET failure isolated before cache read");
fake.FailGet = false;
fake.FailPost = true;
Check(!await publisher.PublishOnceAsync(), "publisher POST failure isolated");
fake.FailPost = false;
fake.ThrowTransport = true;
Check(!await publisher.PublishOnceAsync(), "transport exception isolated without response logging");
fake.ThrowTransport = false;
onSnapshot = () => throw new IOException("synthetic cache failure");
Check(!await publisher.PublishOnceAsync(), "snapshot failure isolated");
onSnapshot = null;
Check((await Task.WhenAll(publisher.PublishOnceAsync(), publisher.PublishOnceAsync())).All(ok => ok), "concurrent publication serialized through one CAS sender");
clock.Now = now.AddMinutes(5);
Check(await publisher.PublishOnceAsync() && fake.Last!.Entries.Length == 0, "stale observation sends empty replacement");
clock.Now = now;
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    try { await publisher.PublishOnceAsync(cancelled.Token); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { checks++; }
}

// Real HostQuotaSource and QuotaService integration with private synthetic state only.
// All HTTP is intercepted; the adapter can run once and cache publication cannot invoke it.
var root = Path.Combine(Path.GetTempPath(), "lain-router-availability-" + Guid.NewGuid().ToString("N"));
try
{
    var paths = new OwnStatePaths(root);
    var repository = new ConfigurationRepository(paths);
    var secrets = new SecretStore(paths);
    var monitors = new MonitorCredentialStore(paths);
    var monitorAccess = new MonitorAccessService(repository, secrets, monitors);
    var source = new HostQuotaSource(repository, secrets, monitorAccess);
    secrets.WriteAccount(zai.Id, plain);
    var state = repository.Update(0, c => c with { Accounts = [zai] });
    repository.MarkApplied(state.Revision);
    using var quotaHandler = new FixtureQuotaHttp(now);
    using var quotaHttp = new HttpClient(quotaHandler);
    var adapter = new ZaiQuotaAdapter(quotaHttp);
    var quota = new QuotaService(source, source, [adapter], clock);
    await quota.RefreshAsync();
    var cache = await quota.GetRouterAvailabilitySnapshotsAsync();
    Check(cache.Single().Proof?.Endpoint == codeEndpoint && cache.Single().Reading.State == "ok", "actual legacy source proof captured with successful snapshot");
    await quota.GetRouterAvailabilitySnapshotsAsync();
    Check(quotaHandler.Calls == 1, "cached availability read never refreshes provider");
    Check(cache.Single().Reading.Source == Reading("zai").Source && cache.Single().Reading.Remaining == 0,
        "real Zai adapter normalizes GLM exhausted coding window through cache");
    secrets.WriteAccount(zai.Id, plain.Replace("fixture-key", "replaced-fixture-key"));
    cache = await quota.GetRouterAvailabilitySnapshotsAsync();
    Check(cache.Single().Proof is null && cache.Single().Reading.State == "unknown", "credential replacement invalidates both cached quota and proof");
    Check(quotaHandler.Calls == 1, "binding invalidation does not probe provider");
    await quota.RefreshAsync();
    repository.Update(repository.Snapshot.Revision, c => c with { Accounts = [zai with { Generation = 1 }] });
    Check((await quota.GetRouterAvailabilitySnapshotsAsync()).Single().Proof is null, "generation change invalidates cached proof");
    await quota.RefreshAsync();
    repository.Update(repository.Snapshot.Revision, c => c with { Accounts = [zai with { Generation = 1, Enabled = false }] });
    Check((await quota.GetRouterAvailabilitySnapshotsAsync()).Single().Reading.State == "disabled", "disabled account cache is not ok");

    var inferenceHandle = secrets.Generations.Prepare(zai.Id, new("static-key", ["inference"], new("zai"), plain));
    var quotaHandle = secrets.Generations.Prepare(zai.Id, new("static-key", ["quota"], new("zai"), plain.Replace("fixture-key", "unrelated-monitor-key")));
    var separate = zai with { Generation = 2, Credentials = [inferenceHandle, quotaHandle],
        Capabilities = [new("inference", "available", inferenceHandle.Id), new("quota", "available", quotaHandle.Id)] };
    repository.Update(repository.Snapshot.Revision, c => c with { Accounts = [separate] });
    var selectedMonitor = await source.GetCredentialAsync(zai.Id);
    Check(selectedMonitor is not null && source.GetRouterAvailabilityProof(zai.Id, selectedMonitor.BindingVersion) is null,
        "actual separate quota handle never borrows unrelated legacy inference proof");
    await quota.RefreshAsync();
    Check((await quota.GetRouterAvailabilitySnapshotsAsync()).Single().Proof is null,
        "separate monitoring snapshot remains displayable without inference suppression");
    quotaHandler.Fail = true;
    await quota.RefreshAsync();
    Check((await quota.GetRouterAvailabilitySnapshotsAsync()).Single().Reading.Error is not null,
        "provider failure stays a cached quota diagnostic");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
Console.WriteLine($"Router availability Host: {checks} offline checks passed; no real HTTP requests.");

sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

sealed class FakeCore : HttpMessageHandler
{
    public string Generation = "8d2170f9-a6bc-4380-9903-1f320c937c44";
    public ulong Sequence;
    public bool HasReadGeneration, FailGet, FailPost, ThrowTransport;
    public int Posts, Requests;
    public RouterAvailabilityReplacement? Last;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests++;
        if (request.RequestUri!.AbsolutePath != RouterAvailabilityPublisher.Endpoint || request.Headers.Authorization?.Parameter != "synthetic-auth")
            throw new InvalidOperationException("Unexpected or unauthenticated local request");
        if (ThrowTransport) throw new HttpRequestException("synthetic private transport text");
        if (request.Method == HttpMethod.Get)
        {
            HasReadGeneration = true;
            return FailGet ? new(HttpStatusCode.ServiceUnavailable) : new(HttpStatusCode.OK)
            { Content = JsonContent.Create(new { schemaVersion = 1, generation = Generation, sequence = Sequence }) };
        }
        Posts++;
        if (FailPost) return new(HttpStatusCode.ServiceUnavailable);
        var value = await request.Content!.ReadFromJsonAsync<RouterAvailabilityReplacement>(cancellationToken: ct) ?? throw new Exception("Missing batch");
        if (value.Generation != Generation || value.Sequence != Sequence + 1) return new(HttpStatusCode.Conflict);
        Sequence = value.Sequence;
        Last = value;
        return new(HttpStatusCode.NoContent);
    }
}

sealed class FixtureQuotaHttp(DateTimeOffset now) : HttpMessageHandler
{
    public int Calls;
    public bool Fail;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;
        if (request.RequestUri!.AbsoluteUri != ZaiQuotaAdapter.Endpoint) throw new Exception("Unexpected fixture provider request");
        return Task.FromResult(Fail ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : new(HttpStatusCode.OK)
        { Content = JsonContent.Create(new { success = true, data = new { limits = new[] {
            new { type = "TOKENS_LIMIT", unit = 3, number = 5, currentValue = 100, usage = 100, remaining = 0,
                percentage = 100, nextResetTime = now.AddHours(2) }
        } } }) });
    }
}
