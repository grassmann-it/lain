using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Accounts.Credentials;
using Lain.Host.Accounts.Monitoring;
using Lain.Host.Operations;
using Lain.Host.Quota;
using Lain.Host.Providers;

var root = Path.Combine(Path.GetTempPath(), "lain-foundation-fixtures-" + Guid.NewGuid().ToString("N"));
var assertions = 0;
try
{
    await MonitorAndStatic();
    await LoginLifecycle();
    assertions += await DurableRemovalChecks.Run(Path.Combine(root, "durable-removal"));
    await RotationAndRestore();
    await ReviewRegressions();
    await LegacyPreservation();
    await ModelAttachment();
    await PublicationGate();
    await AutomaticRenewal();
    await QuotaGeneration();
    await QuotaBoundaryRegressions();
    Console.WriteLine($"Account connect foundation: {assertions} assertions passed (offline synthetic state only).");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }

void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
async Task Throws<T>(Func<Task> work, string message) where T : Exception
{
    try { await work(); } catch (T) { assertions++; return; }
    throw new Exception(message);
}
void ThrowsSync<T>(Action work, string message) where T : Exception => Throws<T>(() => { work(); return Task.CompletedTask; }, message).GetAwaiter().GetResult();
Fixture New(string name) => new(Path.Combine(root, name));
PreparedLoginCredential Key(string token, string provider = "test", string[]? purposes = null, ResourceBindingDto? binding = null,
    string owner = "static-key", bool recovery = false) => new(owner, purposes ?? ["quota"], binding ?? new(provider),
        JsonSerializer.Serialize(new { kind = "api-key", apiKey = token, provider }), PreserveForRecovery: recovery);

async Task MonitorAndStatic()
{
    var f = New("monitor");
    var config = f.Monitor.Create(0, "Google usage", "gemini-api", new("access-token-snapshot", "monitor-fixture-token", "my-project", DateTimeOffset.UtcNow.AddHours(1)));
    var account = config.Accounts.Single();
    Check(config.Routes.Length == 0 && config.Profiles.Length == 0, "monitor creates no model or profile");
    Check(!f.Secrets.HasAccount(account.Id), "monitor account has no inference credential");
    Check(account.Credentials?.Single().Purposes.SequenceEqual(new[] { "quota" }) == true, "monitor has explicit quota purpose");
    var credential = await f.Source.GetCredentialAsync(account.Id);
    Check(credential?.BearerToken == "" && credential.MonitorBearerToken == "monitor-fixture-token", "monitor source needs no dummy inference token");
    Check(!JsonSerializer.Serialize(config).Contains("monitor-fixture-token"), "public configuration contains no secret");
    var monitorReader = new FakeQuota("gemini-api", (_, c, _) => Task.FromResult(new QuotaFetchResult([new("fixture", "requests", Used: 2)])));
    var quota = new QuotaService(f.Source, f.Source, [monitorReader]);
    Check((await quota.RefreshAsync()).Single().Used == 2, "independent monitor reads without inference");
    var originalId = account.Id;
    var next = f.Accounts.Connect(config.Revision, new("gemini-api", "manual-key", ["inference"], AccountId: account.Id),
        Key("inference-fixture-token", "gemini-api", ["inference"], account.Binding));
    account = next.Accounts.Single();
    Check(account.Id == originalId && account.Name == "Google usage", "later inference preserves identity and chosen name");
    Check(f.Secrets.ReadAccount(account.Id).Contains("inference-fixture-token"), "inference resolves selected generation");
    Check((await f.Source.GetCredentialAsync(account.Id))?.MonitorBearerToken == "monitor-fixture-token", "inference attach keeps independent monitor");
    var monitorHandle = account.Credentials!.Single(h => h.Purposes.Contains("quota"));
    Check(f.Secrets.Generations.Read(account.Id, monitorHandle).PrivateJson.Contains("monitor-fixture-token"), "monitor generation remains intact");
    var removed = f.Monitor.Remove(next.Revision, account.Id);
    Check(f.Secrets.HasAccount(account.Id) && await f.Source.GetCredentialAsync(account.Id) is null, "monitor disconnect leaves inference and denies quota fallback");
    Check(removed.Accounts.Single().Id == originalId, "monitor disconnect preserves account");

    var k = New("static");
    var first = k.Accounts.AddApi(0, new("Saved key", "test", "old-fixture-key", "https://provider.example.test/v1"));
    var id = first.Accounts.Single().Id;
    Check(first.Routes.Length == 0, "legacy add API works without model or tariff");
    var oldJson = k.Secrets.ReadAccount(id);
    File.Move(k.Paths.Configuration, k.Paths.Configuration + ".original");
    Directory.CreateDirectory(k.Paths.Configuration);
    try { ThrowsSync<IOException>(() => k.Accounts.ReplaceApiCredential(first.Revision, id, "new-fixture-key"), "save failure must surface"); }
    finally { Directory.Delete(k.Paths.Configuration); File.Move(k.Paths.Configuration + ".original", k.Paths.Configuration); }
    Check(k.Secrets.ReadAccount(id) == oldJson, "failed publication leaves old generation active");
    var changed = k.Accounts.ReplaceApiCredential(first.Revision, id, "new-fixture-key");
    Check(changed.Accounts.Single().Id == id && k.Secrets.ReadAccount(id).Contains("new-fixture-key"), "successful key rotation retains opaque account ID");
    Check(!id.Contains("old-fixture-key"), "identity does not contain credential");
    ThrowsSync<ArgumentException>(() => k.Accounts.Connect(changed.Revision, new("test", "manual", ["quota"], AccountId: id,
        Binding: new("test", ResourceType: "project", ResourceId: "one")), Key("different-key", binding: new("test", ResourceType: "project", ResourceId: "two"))), "wrong resource rejected");
    if (!OperatingSystem.IsWindows())
        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            Check((File.GetUnixFileMode(file) & (UnixFileMode.OtherRead | UnixFileMode.GroupRead)) == 0, "private credential/config permissions");
}

async Task LoginLifecycle()
{
    var f = New("login");
    var adapter = new FakeLogin();
    await using var service = f.Logins(adapter);
    var request = new ConnectAccountRequest("test", "fake", ["quota"], Name: "Connected", IdempotencyKey: "once");
    var started = await service.BeginAsync(0, request);
    var repeated = await service.BeginAsync(0, request);
    Check(started.Id == repeated.Id && adapter.Begins == 1, "repeated begin is idempotent");
    await Throws<RevisionConflictException>(() => service.BeginAsync(10, request with { IdempotencyKey = "stale" }), "stale revision rejected before provider");
    Check(adapter.Begins == 1, "stale begin has no remote action");
    adapter.Next = new("prepared", Credential: Key("login-fixture-key", recovery: true));
    var completed = await service.StatusAsync(started.Id);
    Check(completed.State == "completed" && f.Repository.Snapshot.Accounts.Single().Id == completed.AccountId, "central completion publishes reserved account");
    await service.StatusAsync(started.Id);
    await service.CancelAsync(started.Id);
    Check(adapter.Polls == 1 && (await service.StatusAsync(started.Id)).State == "completed", "terminal replay cannot duplicate or cancel committed credential");
    await using var restarted = f.Logins(adapter);
    Check((await restarted.BeginAsync(0, request)).State == "completed" && adapter.Begins == 1, "durable idempotency survives host restart");

    var conflict = New("conflict");
    var conflictAdapter = new FakeLogin();
    await using var conflicted = conflict.Logins(conflictAdapter);
    var pending = await conflicted.BeginAsync(0, request with { IdempotencyKey = "conflict" });
    conflict.Repository.Update(0, state => state);
    conflictAdapter.Next = new("prepared", Credential: Key("recoverable-fixture", recovery: true));
    var recovery = await conflicted.StatusAsync(pending.Id);
    Check(recovery.State == "recovery-required" && conflict.Repository.Snapshot.Accounts.Length == 0, "revision changed during remote result prevents adoption");
    Check(CredentialRecoveryService.Read(conflict.Secrets.Generations, pending.Id).Handle is not null, "remote result durably recoverable");
    var adopted = new CredentialRecoveryService(conflict.Repository, conflict.Accounts, conflict.Secrets.Generations).Adopt(1, pending.Id);
    Check(adopted.Accounts.Length == 1 && conflictAdapter.Polls == 1, "explicit recovery adopts without second remote create");

    var cancel = New("cancel");
    var delayed = new TaskCompletionSource<LoginAdapterResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var cancelAdapter = new FakeLogin { Poll = _ => { entered.SetResult(); return delayed.Task; } };
    await using var cancelling = cancel.Logins(cancelAdapter);
    var cancelStart = await cancelling.BeginAsync(0, request with { IdempotencyKey = "cancel" });
    var poll = cancelling.StatusAsync(cancelStart.Id);
    await entered.Task;
    var cancelled = cancelling.CancelAsync(cancelStart.Id);
    delayed.SetResult(new("prepared", Credential: Key("late-fixture-key", recovery: true)));
    Check((await poll).State == "cancelled" && (await cancelled).State == "cancelled", "cancel fences late completion");
    Check(cancel.Repository.Snapshot.Accounts.Length == 0, "late provider result cannot resurrect cancelled account");
    Check(CredentialRecoveryService.Read(cancel.Secrets.Generations, cancelStart.Id).Handle is not null, "late remote result still privately retained");

    var expiry = New("expiry");
    var clock = new FakeClock();
    var expiryAdapter = new FakeLogin();
    await using var expires = expiry.Logins(expiryAdapter, clock);
    var expiryStart = await expires.BeginAsync(0, request with { IdempotencyKey = "expires" });
    clock.Advance(TimeSpan.FromMinutes(11));
    Check((await expires.StatusAsync(expiryStart.Id)).State == "expired", "host TTL expires operation with injected time");
    Check(expiryAdapter.Cancels > 0, "expiry cleans provider helper");
    var bounded = New("bounded");
    await using var bounds = bounded.Logins(new FakeLogin());
    for (var i = 0; i < 8; i++) await bounds.BeginAsync(0, request with { IdempotencyKey = "active-" + i });
    await Throws<InvalidOperationException>(() => bounds.BeginAsync(0, request with { IdempotencyKey = "ninth" }), "active capacity is bounded");
    var failure = New("begin-failed");
    var failingAdapter = new FakeLogin { FailBegin = true };
    await using var failures = failure.Logins(failingAdapter);
    Check((await failures.BeginAsync(0, request)).State == "failed" && failingAdapter.Cancels == 1, "begin failure cleans operation resources");
}

async Task RotationAndRestore()
{
    var f = New("rotation");
    var account = f.Accounts.Connect(0, new("test", "session", ["quota"]), Key("old-access", owner: "session-owner", recovery: true)).Accounts.Single();
    var handle = account.Credentials!.Single();
    var release = new TaskCompletionSource<PreparedLoginCredential>(TaskCreationOptions.RunContinuationsAsynchronously);
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var owner = new FakeOwner { Refresh = _ => { entered.SetResult(); return release.Task; } };
    var owners = new CredentialOwnerService(f.Repository, f.Accounts, f.Secrets.Generations, [owner]);
    var first = owners.RefreshAsync(account.Id, handle.Id);
    await entered.Task;
    var second = owners.RefreshAsync(account.Id, handle.Id);
    ThrowsSync<InvalidDataException>(() => new CredentialResolver(f.Repository, f.Secrets).Resolve(account.Id, "quota"), "in-progress rotation retires previous grant");
    release.SetResult(Key("new-access", owner: "session-owner", recovery: true));
    await Task.WhenAll(first, second);
    Check(owner.Calls == 1 && f.Repository.Snapshot.Accounts.Single().Credentials!.Single().Generation == 2, "concurrent refresh consumes generation once");
    Check(new CredentialResolver(f.Repository, f.Secrets).Resolve(account.Id, "quota")!.PrivateJson.Contains("new-access"), "new owner generation published");
    OwnStatePaths.WritePrivate(Path.Combine(f.Paths.Root, "restore-requires-reconnect"), "{\"restoreId\":\"synthetic-unique-restore\"}");
    ThrowsSync<InvalidDataException>(() => new CredentialResolver(f.Repository, f.Secrets).Resolve(account.Id, "quota"), "restore quarantines spent rotating grants");
    var beforeReconnect = f.Repository.Snapshot;
    f.Accounts.Connect(beforeReconnect.Revision, new("test", "session", ["quota"], AccountId: account.Id), Key("reauthorized-access", owner: "session-owner", recovery: true));
    Check(new CredentialResolver(f.Repository, f.Secrets).Resolve(account.Id, "quota")!.PrivateJson.Contains("reauthorized-access"), "explicit reconnect repairs restored owner with current epoch");
    var statics = New("restore-static");
    var staticAccount = statics.Accounts.Connect(0, new("test", "key", ["quota"]), Key("static-access")).Accounts.Single();
    OwnStatePaths.WritePrivate(Path.Combine(statics.Paths.Root, "restore-requires-reconnect"), "fixture-restore");
    Check(new CredentialResolver(statics.Repository, statics.Secrets).Resolve(staticAccount.Id, "quota") is not null, "restore leaves static access usable");
    var external = New("external");
    var snapshot = external.Accounts.Connect(0, new("test", "snapshot", ["quota"]), new("external-snapshot", ["quota"], new("test"),
        "{\"kind\":\"access-token-snapshot\",\"accessToken\":\"external-access\"}", DateTimeOffset.UtcNow.AddHours(1))).Accounts.Single();
    var noOwner = new CredentialOwnerService(external.Repository, external.Accounts, external.Secrets.Generations, [owner]);
    await Throws<InvalidOperationException>(() => noOwner.RefreshAsync(snapshot.Id, snapshot.Credentials!.Single().Id), "external snapshot is never refreshed");
}

async Task QuotaGeneration()
{
    var f = New("quota-generation");
    var account = f.Accounts.Connect(0, new("test", "key", ["quota"]), Key("quota-a")).Accounts.Single();
    var release = new TaskCompletionSource<QuotaFetchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var adapter = new FakeQuota("test", (_, _, _) => { entered.SetResult(); return release.Task; });
    var quotaClock = new FakeClock();
    var service = new QuotaService(f.Source, f.Source, [adapter], quotaClock);
    var read = service.RefreshAsync();
    await entered.Task;
    f.Accounts.Connect(f.Repository.Snapshot.Revision, new("test", "key", ["quota"], AccountId: account.Id), Key("quota-b"));
    release.SetResult(new([new("fixture", "USD", Remaining: 99, ObservedAt: DateTimeOffset.UtcNow.AddHours(-1))]));
    Check((await read).All(q => q.Remaining is null), "late quota for replaced generation discarded");
    var timestamp = quotaClock.GetUtcNow().AddMinutes(-2);
    adapter.Fetch = (_, _, _) => Task.FromResult(new QuotaFetchResult([new("fixture", "USD", Remaining: 8, ObservedAt: timestamp)]));
    Check((await service.RefreshAsync()).Single().Remaining == 8, "current generation quota published");
    adapter.Fetch = (_, _, _) => throw new IOException("raw synthetic secret must not leak");
    var stale = (await service.RefreshAsync()).Single();
    Check(stale.Remaining == 8 && stale.ObservedAt == timestamp && stale.LastSuccessAt == timestamp, "failed read retains original observation time");
    Check(stale.Error == "Quota refresh failed (unavailable).", "exception text never exposes private material");
    adapter.Fetch = (_, _, _) => Task.FromResult(new QuotaFetchResult([new("fixture", "USD", ExactRemaining: "123456789.123456789123456789")],
        KiloPass: new("active", "plus", null, null, null, null, null, true)));
    var precise = (await service.RefreshAsync()).Single();
    Check(precise.ExactRemaining == "123456789.123456789123456789" && precise.KiloPass?.Status == "active", "precise decimals and subscription metadata survive projection");
    var passObservation = precise.KiloPassObservation;
    Check(passObservation is { State: "ok" } && passObservation.ObservedAt == quotaClock.GetUtcNow() && passObservation.LastSuccessAt == quotaClock.GetUtcNow(),
        "successful Pass read has its own observed and successful timestamps");
    quotaClock.Advance(TimeSpan.FromMinutes(1));
    adapter.Fetch = (_, _, _) => Task.FromResult(new QuotaFetchResult([new("fixture", "USD", Remaining: 5)], Error: "Pass metadata unavailable."));
    var partial = (await service.RefreshAsync()).Single();
    Check(partial.Remaining == 5 && partial.State == "ok" && partial.ObservedAt == quotaClock.GetUtcNow() && partial.LastSuccessAt == quotaClock.GetUtcNow(),
        "independent wallet read advances its own successful observation while Pass fails");
    Check(partial.KiloPass == precise.KiloPass && partial.KiloPassObservation is { State: "stale", Error: "Pass metadata unavailable." },
        "partial wallet read retains prior Pass value with its independent stale state");
    Check(partial.KiloPassObservation!.ObservedAt == passObservation!.ObservedAt &&
        partial.KiloPassObservation.LastSuccessAt == passObservation.LastSuccessAt &&
        partial.KiloPassObservation.FreshUntil == passObservation.FreshUntil && partial.KiloPassObservation.ObservedAt < partial.ObservedAt,
        "fresh wallet observation cannot relabel retained Pass timestamps or extend its freshness");
    adapter.Fetch = (_, _, _) => Task.FromResult(new QuotaFetchResult([], KiloPass: new("active", "plus", null, null, null, null, null, true)));
    Check((await service.RefreshAsync()).Single().State == "ok", "pass-only observation is useful without fake wallet quota");
    f.Accounts.Remove(f.Repository.Snapshot.Revision, account.Id);
    Check((await service.GetSnapshotsAsync()).Length == 0, "removed account cannot publish cached quota");
}

async Task ReviewRegressions()
{
    var proof = New("identity-proof");
    var verified = new ResourceBindingDto("test", Subject: "subject-a", ResourceType: "project", ResourceId: "project-a", Provenance: "verified");
    var account = proof.Accounts.Connect(0, new("test", "verified-login", ["quota"]), Key("subject-a-key", binding: verified)).Accounts.Single();
    ThrowsSync<ArgumentException>(() => proof.Accounts.ConnectCredential(1, new(new("test", "key", ["quota"], AccountId: account.Id), "unknown-key")),
        "reconnect cannot assign unknown key to previously verified subject");
    ThrowsSync<ArgumentException>(() => proof.Accounts.ConnectCredential(1, new(new("test", "key", ["quota"], AccountId: account.Id, Binding: verified), "unknown-key")),
        "manual binding cannot claim provider verification");
    Check(proof.Repository.Snapshot.Accounts.Single().Generation == 1, "rejected identity adoption leaves prior account generation");
    var confirmedInference = New("confirmed-inference");
    var confirmed = confirmedInference.Accounts.Connect(0, new("test", "verified-key", ["inference"]), Key("verified-key", purposes: ["inference"], binding: verified)).Accounts.Single();
    ThrowsSync<InvalidOperationException>(() => confirmedInference.Accounts.ReplaceApiCredential(1, confirmed.Id, "unverified-replacement"), "legacy replacement cannot bypass verified identity binding");

    var separate = New("separate-monitor");
    var api = separate.Accounts.AddApi(0, new("Fireworks", "fireworks", "inference-old", "https://api.fireworks.ai/inference/v1")).Accounts.Single();
    separate.Monitor.Set(1, api.Id, new("api-token", "monitor-independent", "fixture-account"));
    var monitorId = separate.Repository.Snapshot.Accounts.Single().Capabilities!.Single(c => c.Purpose == "quota").CredentialId;
    separate.Accounts.ReplaceApiCredential(2, api.Id, "inference-new");
    Check(separate.Repository.Snapshot.Accounts.Single().Capabilities!.Single(c => c.Purpose == "quota").CredentialId == monitorId,
        "inference key replacement preserves independent monitor handle");
    Check((await separate.Source.GetCredentialAsync(api.Id))?.MonitorBearerToken == "monitor-independent", "independent monitor remains readable after inference rotation");

    var crash = New("prepared-crash");
    var context = crash.Accounts.ReserveConnect(0, new("test", "fake", ["quota"]), "crash-after-secret", DateTimeOffset.UtcNow.AddMinutes(10));
    CredentialRecoveryService.SaveStarted(crash.Secrets.Generations, context);
    var durable = crash.Secrets.Generations.Prepare(context.AccountId, Key("crash-durable-result", owner: "session-owner", recovery: true), context.OperationId);
    // Simulated crash before writing the prepared-reference recovery record.
    Check(CredentialRecoveryService.Read(crash.Secrets.Generations, context.OperationId).Handle?.Id == durable.Id,
        "recovery reconciles durable generation after checkpoint record crash");
    Check(new CredentialRecoveryService(crash.Repository, crash.Accounts, crash.Secrets.Generations).Adopt(0, context.OperationId).Accounts.Length == 1,
        "reconciled result adopts without repeating authorization");
    var stalePointer = New("prepared-pointer-crash");
    var staleContext = stalePointer.Accounts.ReserveConnect(0, new("test", "fake", ["quota"]), "crash-after-enrichment", DateTimeOffset.UtcNow.AddMinutes(10));
    CredentialRecoveryService.SaveStarted(stalePointer.Secrets.Generations, staleContext);
    var initial = stalePointer.Secrets.Generations.Prepare(staleContext.AccountId, Key("checkpoint-key", recovery: true), staleContext.OperationId);
    CredentialRecoveryService.SavePrepared(stalePointer.Secrets.Generations, staleContext, initial, null);
    stalePointer.Secrets.Generations.Prepare(staleContext.AccountId, Key("checkpoint-key", binding: verified, recovery: true), staleContext.OperationId, initial.Id, 2);
    Check(CredentialRecoveryService.Read(stalePointer.Secrets.Generations, staleContext.OperationId).Handle?.Generation == 2,
        "recovery reconciles verified final generation after stale checkpoint pointer");

    var checkpoint = New("login-checkpoint");
    var verifiedAdapter = new CheckpointLogin();
    await using var operations = checkpoint.Logins(verifiedAdapter);
    var started = await operations.BeginAsync(0, new("test", "fake", ["quota"]));
    Check((await operations.StatusAsync(started.Id)).State == "completed", "issued key checkpoint can be enriched before central publication");
    Check(checkpoint.Repository.Snapshot.Accounts.Single().Credentials!.Single().Generation == 2, "verified metadata creates next immutable generation");
    Check(verifiedAdapter.Releases == 1, "completed adapter releases local operation resources");
    var storage = New("storage-failure");
    var storageAdapter = new FakeLogin { Next = new("prepared", Credential: Key("memory-retained-key", recovery: true)) };
    await using var storageOperations = storage.Logins(storageAdapter);
    var storageStart = await storageOperations.BeginAsync(0, new("test", "fake", ["quota"]));
    var blockedDirectory = Path.Combine(storage.Paths.Credentials, "generations");
    OwnStatePaths.WritePrivate(blockedDirectory, "synthetic storage obstruction");
    Check((await storageOperations.StatusAsync(storageStart.Id)).State == "recovery-required", "received key storage failure is recovery-required rather than failed login");
    File.Delete(blockedDirectory);
    Check((await storageOperations.StatusAsync(storageStart.Id)).State == "recovery-required" && storageAdapter.Polls == 1,
        "retry saves retained in-memory result without provider reissuance");
    Check(new CredentialRecoveryService(storage.Repository, storage.Accounts, storage.Secrets.Generations).Adopt(0, storageStart.Id).Accounts.Length == 1,
        "locally retried credential is durably adoptable");
    var enrichedFailure = New("enriched-storage-failure");
    string? obstruction = null;
    var enrichment = new CheckpointLogin { AfterCheckpoint = context =>
    {
        var saved = CredentialRecoveryService.Read(enrichedFailure.Secrets.Generations, context.OperationId);
        obstruction = Path.Combine(enrichedFailure.Paths.Credentials, "generations", saved.Handle!.Id, "2.json");
        Directory.CreateDirectory(obstruction);
    } };
    await using var enrichedOperations = enrichedFailure.Logins(enrichment);
    var enrichedStart = await enrichedOperations.BeginAsync(0, new("test", "fake", ["quota"]));
    Check((await enrichedOperations.StatusAsync(enrichedStart.Id)).State == "recovery-required", "final enrichment storage failure retains received result");
    Directory.Delete(obstruction!);
    await enrichedOperations.StatusAsync(enrichedStart.Id);
    Check(CredentialRecoveryService.Read(enrichedFailure.Secrets.Generations, enrichedStart.Id).Handle is { Generation: 2, Binding.Provenance: "verified" },
        "local retry writes enriched generation instead of reusing earlier unverified checkpoint");

    var quarantine = New("quarantine");
    OwnStatePaths.WritePrivate(Path.Combine(quarantine.Paths.OwnedAuth, "unreferenced.json"), "{\"refresh_token\":\"synthetic-spent\"}");
    OwnStatePaths.WritePrivate(Path.Combine(quarantine.Paths.Root, "restore-requires-reconnect"), "unique-restore-fixture");
    RestoredCredentialQuarantine.PrepareForRestore(quarantine.Paths);
    Check(!Directory.EnumerateFileSystemEntries(quarantine.Paths.OwnedAuth).Any(), "restore quarantines even unreferenced core grants");
    Check(Directory.EnumerateFiles(Path.Combine(quarantine.Paths.Credentials, "quarantine"), "unreferenced.json", SearchOption.AllDirectories).Count() == 1,
        "quarantine preserves old credentials privately");
    OwnStatePaths.WritePrivate(Path.Combine(quarantine.Paths.OwnedAuth, "new-login.json"), "{\"access_token\":\"new-fixture\"}");
    RestoredCredentialQuarantine.PrepareForRestore(quarantine.Paths);
    Check(File.Exists(Path.Combine(quarantine.Paths.OwnedAuth, "new-login.json")), "idempotent restore preparation preserves new login files");
    var core = New("core-quota-only");
    OwnStatePaths.WritePrivate(Path.Combine(core.Paths.OwnedAuth, "owned-fixture.json"), "{\"type\":\"codex\",\"account_id\":\"owned-subject\",\"access_token\":\"owned-access\",\"refresh_token\":\"must-stay-owned\"}");
    var coreAccount = core.Accounts.Connect(0, new("codex", "codex-owned", ["quota"]), new("core", ["quota"], new("codex", Subject: "owned-subject", Provenance: "verified"),
        "{\"kind\":\"codex-owned\",\"ownedAuthFile\":\"owned-fixture.json\",\"accountId\":\"owned-subject\"}", PreserveForRecovery: true)).Accounts.Single();
    var coreLease = new CredentialResolver(core.Repository, core.Secrets).Resolve(coreAccount.Id, "quota")!;
    Check(coreLease.PrivateJson.Contains("owned-access") && !coreLease.PrivateJson.Contains("must-stay-owned") && !core.Secrets.HasAccount(coreAccount.Id),
        "quota-only core handle resolves its own descriptor without inference or refresh exposure");
    var badReference = core.Repository.Update(core.Repository.Snapshot.Revision, state => state with { Accounts = state.Accounts.Select(a => a with {
        Capabilities = [new("quota", "unverified", "cred-missing")] }).ToArray() });
    ThrowsSync<InvalidDataException>(() => new CredentialResolver(core.Repository, core.Secrets).Resolve(coreAccount.Id, "quota"), "missing explicit generation cannot fall back to another purpose");
    var shared = New("shared-material");
    var sharedAccount = shared.Accounts.ConnectCredential(0, new(new("test", "manual", ["quota"]), "same-suitable-fixture-key")).Accounts.Single();
    shared.Accounts.ConnectCredential(1, new(new("test", "manual", ["inference"], AccountId: sharedAccount.Id), "same-suitable-fixture-key", BaseUrl: "https://provider.example.test/v1"));
    var materialFiles = Directory.EnumerateFiles(Path.Combine(shared.Paths.Credentials, "material", sharedAccount.Id)).ToArray();
    Check(materialFiles.Length == 1, "same suitable key is stored once across inference and monitoring descriptors");
    Check(shared.Repository.Snapshot.Accounts.Single().Credentials!.Length == 2 &&
        new CredentialResolver(shared.Repository, shared.Secrets).Resolve(sharedAccount.Id, "quota")!.PrivateJson.Contains("same-suitable-fixture-key"),
        "each purpose keeps its own immutable binding to shared private material");
    Check(!Directory.EnumerateFiles(Path.Combine(shared.Paths.Credentials, "generations"), "*.json", SearchOption.AllDirectories)
        .Any(path => File.ReadAllText(path).Contains("same-suitable-fixture-key")), "generation envelopes do not duplicate shared key material");
    var cloud = New("cloud-owner");
    var cloudBinding = new ResourceBindingDto("gemini-api", Subject: "123456", ResourceType: "project", ResourceId: "my-project", Provenance: "provider-discovery");
    var cloudAccount = cloud.Accounts.Connect(0, new("gemini-api", "oauth", ["quota"]), new("google-oauth", ["quota"], cloudBinding,
        "{\"kind\":\"cloud-oauth\",\"accessToken\":\"cloud-access\",\"refreshToken\":\"private-refresh\",\"clientSecret\":\"private-client\",\"settings\":{\"projectNumber\":\"123456\"}}",
        DateTimeOffset.UtcNow.AddHours(1), PreserveForRecovery: true)).Accounts.Single();
    var cloudQuota = await cloud.Source.GetCredentialAsync(cloudAccount.Id);
    Check(cloudQuota?.MonitorBearerToken == "cloud-access" && cloudQuota.Monitor!.ResourceId == "my-project" && cloudQuota.Settings["projectNumber"] == "123456",
        "provider-specific cloud owner projects scoped monitor access and safe settings");
    var cloudLease = new CredentialResolver(cloud.Repository, cloud.Secrets).Resolve(cloudAccount.Id, "quota")!;
    Check(!cloudLease.PrivateJson.Contains("private-refresh") && !cloudLease.PrivateJson.Contains("private-client"), "monitor lease excludes refresh and client secrets");
    ThrowsSync<ArgumentException>(() => cloud.Accounts.ConnectCredential(1, new(new("gemini-api", "manual", ["quota"], AccountId: cloudAccount.Id, Binding: cloudBinding), "unverified-replacement")),
        "provider-discovered identity cannot be replaced by declared static credentials");
    Check(cloud.Monitor.Get(cloudAccount.Id).Guidance is { } cloudGuidance && cloudGuidance.Contains("Google OAuth") && !cloudGuidance.Contains("never refreshed"),
        "managed cloud monitor guidance identifies its rotating owner instead of external snapshot rules");
    var incomplete = New("unqualified-grant");
    var incompleteContext = incomplete.Accounts.ReserveConnect(0, new("gemini-api", "oauth", ["quota"]), "unqualified-cloud-result", DateTimeOffset.UtcNow.AddMinutes(10));
    var incompleteHandle = incomplete.Secrets.Generations.Prepare(incompleteContext.AccountId,
        new("google-oauth", [], new("gemini-api"), "{\"kind\":\"cloud-oauth\",\"accessToken\":\"unqualified-access\",\"refreshToken\":\"retained-refresh\"}", PreserveForRecovery: true), incompleteContext.OperationId);
    CredentialRecoveryService.SavePrepared(incomplete.Secrets.Generations, incompleteContext, incompleteHandle, null);
    ThrowsSync<InvalidDataException>(() => new CredentialRecoveryService(incomplete.Repository, incomplete.Accounts, incomplete.Secrets.Generations).Adopt(0, incompleteContext.OperationId),
        "unqualified intermediate cloud checkpoint cannot publish requested quota capability");
    Check(incomplete.Repository.Snapshot.Accounts.Length == 0, "intermediate grant remains private until resource and rights qualification");
    foreach (var (alias, canonical) in new[] { ("command-code", "commandcode"), ("mimo", "xiaomi-mimo") })
    {
        var legacyAlias = New("alias-" + alias);
        legacyAlias.Repository.Update(0, state => state with { Accounts = [new("legacy-account", "Chosen legacy name", alias, true, "monitor", "unverified", Binding: new(alias))] });
        var connected = legacyAlias.Accounts.Connect(1, new(canonical, "key", ["quota"], AccountId: "legacy-account"), Key("alias-key", provider: canonical)).Accounts.Single();
        Check(connected.Provider == alias && connected.Binding?.Provider == alias && connected.Name == "Chosen legacy name", "canonical reconnect preserves legacy provider and binding identity");
    }
    var aliasLogin = New("alias-login");
    var aliasAdapter = new FakeLogin("commandcode") { Next = new("prepared", Credential: Key("login-alias-key", provider: "commandcode", recovery: true)) };
    await using var aliasOperations = aliasLogin.Logins(aliasAdapter);
    var aliasStart = await aliasOperations.BeginAsync(0, new("command-code", "fake", ["quota"]));
    Check((await aliasOperations.StatusAsync(aliasStart.Id)).State == "completed", "legacy alias uses canonical qualified adapter lookup");
}

async Task LegacyPreservation()
{
    var source = Path.Combine(root, "legacy-preservation-source");
    Directory.CreateDirectory(source);
    var sourceConfig = Path.Combine(source, "legacy.yaml");
    File.WriteAllText(sourceConfig, """
        openai-compatibility:
          - name: test
            base-url: https://provider.example.test/v1
            api-key-entries:
              - api-key: original-import-key
                label: Source name
            models:
              - name: test-model
                alias: original-alias
        """);
    var f = New("legacy-preservation");
    var importer = new LegacyImportService(f.Paths, f.Repository, f.Secrets);
    importer.Import(new(0, sourceConfig));
    var original = f.Repository.Snapshot.Accounts.Single();
    var routeId = f.Repository.Snapshot.Routes.Single().Id;
    f.Accounts.ReplaceApiCredential(1, original.Id, "locally-rotated-key");
    f.Accounts.Connect(2, new("test", "separate-monitor", ["quota"], AccountId: original.Id), Key("separate-monitor-key"));
    f.Repository.Update(3, state => state with { Accounts = state.Accounts.Select(a => a with { Name = "My chosen account name", BudgetPoolId = "keep-pool" }).ToArray() });
    var before = f.Repository.Snapshot.Accounts.Single();
    File.WriteAllText(sourceConfig, File.ReadAllText(sourceConfig).Replace("original-alias", "updated-alias").Replace("Source name", "Changed source label"));
    importer.Import(new(4, sourceConfig));
    var after = f.Repository.Snapshot.Accounts.Single();
    Check(after.Id == before.Id && after.Name == before.Name && after.Generation == before.Generation && after.BudgetPoolId == "keep-pool",
        "old-source update preserves established identity, custom name, generation and pool");
    Check(JsonSerializer.Serialize(after.Credentials) == JsonSerializer.Serialize(before.Credentials) && JsonSerializer.Serialize(after.Capabilities) == JsonSerializer.Serialize(before.Capabilities),
        "reimport keeps all purpose bindings and generation handles");
    Check(f.Secrets.ReadAccount(after.Id).Contains("locally-rotated-key") && (await f.Source.GetCredentialAsync(after.Id))!.BearerToken == "separate-monitor-key",
        "old source cannot roll back local key or independent monitor");
    Check(f.Repository.Snapshot.Routes.Single(r => r.Id == routeId).Alias == "updated-alias", "mapped source route update retains route ID");
    var replay = importer.Import(new(f.Repository.Snapshot.Revision, sourceConfig));
    Check(replay.Revision == f.Repository.Snapshot.Revision && replay.Added == 0 && replay.Updated == 0, "modern metadata preservation remains import-idempotent");

    var separateIdentity = New("legacy-no-key-merge");
    var opaque = separateIdentity.Accounts.ConnectCredential(0, new(new("test", "manual", ["inference"]), "original-import-key", BaseUrl: "https://provider.example.test/v1")).Accounts.Single();
    new LegacyImportService(separateIdentity.Paths, separateIdentity.Repository, separateIdentity.Secrets).Import(new(1, sourceConfig));
    Check(separateIdentity.Repository.Snapshot.Accounts.Length == 2 && separateIdentity.Repository.Snapshot.Accounts.Any(a => a.Id == opaque.Id && a.Credentials is not null),
        "key equality alone cannot merge an established modern identity into imported history");

    var subjects = New("legacy-subjects");
    var known = subjects.Accounts.Connect(0, new("codex", "snapshot", ["inference", "quota"], Name: "Chosen subject name"),
        new("external-snapshot", ["inference", "quota"], new("codex", Subject: "provider-subject-a", Provenance: "verified"),
            "{\"kind\":\"codex-access-snapshot\",\"accessToken\":\"new-local-access\",\"accountId\":\"provider-subject-a\"}", DateTimeOffset.UtcNow.AddHours(1))).Accounts.Single();
    var empty = Path.Combine(source, "empty.yaml"); File.WriteAllText(empty, "{}\n");
    var auth = Path.Combine(source, "auth"); Directory.CreateDirectory(auth);
    File.WriteAllText(Path.Combine(auth, "subject-a.json"), "{\"type\":\"codex\",\"account_id\":\"provider-subject-a\",\"email\":\"changed-name@example.test\",\"access_token\":\"old-source-access\",\"refresh_token\":\"never-copy-refresh\"}");
    var subjectImport = new LegacyImportService(subjects.Paths, subjects.Repository, subjects.Secrets);
    subjectImport.Import(new(1, empty, auth));
    Check(subjects.Repository.Snapshot.Accounts.Single().Id == known.Id && subjects.Repository.Snapshot.Accounts.Single().Name == "Chosen subject name" &&
        subjects.Secrets.ReadAccount(known.Id).Contains("new-local-access"), "verified provider subject can identify source while changed email cannot replace name or owner");
    File.WriteAllText(Path.Combine(auth, "subject-b.json"), "{\"type\":\"codex\",\"account_id\":\"provider-subject-b\",\"email\":\"changed-name@example.test\",\"access_token\":\"other-subject-access\"}");
    subjectImport.Import(new(subjects.Repository.Snapshot.Revision, empty, auth));
    Check(subjects.Repository.Snapshot.Accounts.Length == 2, "same email with different verified subject never merges accounts");
    Check(!Directory.EnumerateFiles(subjects.Paths.Root, "*", SearchOption.AllDirectories).Any(p => File.ReadAllText(p).Contains("never-copy-refresh")),
        "legacy import never copies or refreshes external refresh material");
}

async Task ModelAttachment()
{
    var f = New("model-attachment");
    var catalog = new ProviderCatalogRegistry();
    var provider = catalog.Find("kilo")!;
    var connection = provider.Connections!.Single();
    var key = "same-key-distinct-accounts";
    var other = f.Accounts.ConnectCredential(0, new(new("kilo", "manual", ["inference"], Name: "Other account"), key, BaseUrl: connection.BaseUrl)).Accounts.Single();
    var target = f.Accounts.ConnectCredential(1, new(new("kilo", "manual", ["inference"], Name: "Chosen target"), key,
        BaseUrl: connection.BaseUrl, Settings: new() { ["billingPlan"] = "older-connection-plan" })).Accounts.Single(a => a.Id != other.Id);
    f.Accounts.Connect(2, new("kilo", "separate-monitor", ["quota"], AccountId: target.Id), Key("target-monitor-key", provider: "kilo"));
    var before = f.Repository.Snapshot.Accounts.Single(a => a.Id == target.Id);
    var quotaId = before.Capabilities!.Single(c => c.Purpose == "quota").CredentialId;
    var configured = f.Accounts.AttachModels(3, target.Id, before.Generation, connection.Id, [connection.ProviderRouteIds[0]]);
    var attached = configured.Accounts.Single(a => a.Id == target.Id);
    Check(configured.Accounts.Length == 2 && configured.Routes.Single().AccountIds.SequenceEqual(new[] { target.Id }),
        "model attachment targets exact account even when another identity shares its key");
    Check(attached.Name == "Chosen target" && attached.Capabilities!.Single(c => c.Purpose == "quota").CredentialId == quotaId &&
        configured.Accounts.Single(a => a.Id == other.Id).Generation == other.Generation, "model attachment preserves names, independent monitor and other accounts");
    Check(f.Secrets.ReadAccount(target.Id).Contains(connection.ProviderRouteIds[0]), "qualified model transport is published through immutable current inference reference");
    Check((await f.Source.GetCredentialAsync(target.Id))?.BearerToken == "target-monitor-key", "model attachment leaves separate quota source unchanged");
    ThrowsSync<InvalidOperationException>(() => f.Accounts.AttachModels(configured.Revision, target.Id, before.Generation, connection.Id, [connection.ProviderRouteIds[0]]),
        "stale account generation cannot attach model routes");
    new ProxyConfigurationWriter(f.Paths, f.Secrets).Write(configured);
    Check(File.ReadAllText(f.Paths.ProxyConfig).Contains(provider.Routes[0].Model), "later optional model compiles using selected qualified transport");
}

async Task PublicationGate()
{
    var f = New("publication-gate");
    using var publication = new SemaphoreSlim(0, 1);
    var adapter = new FakeLogin { Next = new("prepared", Credential: Key("publication-fixture-key", recovery: true)) };
    await using var operations = new LoginOperationService(configuration: f.Repository, accounts: f.Accounts, secrets: f.Secrets,
        accountAdapters: [adapter], publicationGate: publication);
    var begun = await operations.BeginAsync(0, new("test", "fake", ["quota"])).WaitAsync(TimeSpan.FromSeconds(2));
    Check(adapter.Begins == 1 && begun.State == "waiting", "provider begin remains independent of held apply publication gate");
    var publishing = operations.StatusAsync(begun.Id);
    Check(!publishing.IsCompleted && f.Repository.Snapshot.Accounts.Length == 0, "completed remote authorization waits before local configuration publication");
    Check(CredentialRecoveryService.Read(f.Secrets.Generations, begun.Id).Handle is not null, "held publication gate does not delay durable credential checkpoint");
    var cancelled = await operations.CancelAsync(begun.Id).WaitAsync(TimeSpan.FromSeconds(2));
    Check(cancelled.State == "cancelled" && (await publishing).State == "cancelled", "cancel remains responsive while publication waits behind apply");
    Check(publication.CurrentCount == 0 && f.Repository.Snapshot.Accounts.Length == 0, "cancelled waiter never releases another owner's gate or publishes");
    publication.Release();
    Check((await operations.StatusAsync(begun.Id)).State == "cancelled" && f.Repository.Snapshot.Accounts.Length == 0,
        "later gate release cannot resurrect cancelled publication");
    await publication.WaitAsync();
    var next = await operations.BeginAsync(0, new("test", "fake", ["quota"]));
    var nextPublication = operations.StatusAsync(next.Id);
    Check(!nextPublication.IsCompleted, "uncancelled prepared result also waits for apply completion");
    publication.Release();
    Check((await nextPublication).State == "completed" && publication.CurrentCount == 1 && f.Repository.Snapshot.Accounts.Length == 1,
        "final local CAS publishes after apply releases shared gate and releases its own lease");
}

async Task AutomaticRenewal()
{
    var clock = new FakeClock();
    var f = New("automatic-renewal");
    AccountDto Add(Fixture fixture, string owner, string[] purposes, DateTimeOffset expiry, bool enabled = true)
    {
        var account = fixture.Accounts.Connect(fixture.Repository.Snapshot.Revision, new("test", "fixture", purposes),
            Key("renewal-initial-" + Guid.NewGuid().ToString("N"), owner: owner, purposes: purposes, recovery: owner != "static-key") with { ExpiresAt = expiry }).Accounts.Last();
        if (!enabled) fixture.Accounts.SetEnabled(fixture.Repository.Snapshot.Revision, account.Id, false);
        return account;
    }
    PreparedLoginCredential Renewed(CredentialRefreshContext context) => new(context.Handle.Owner, context.Handle.Purposes, context.Handle.Binding,
        "{\"kind\":\"api-key\",\"apiKey\":\"renewed-synthetic-access\"}", clock.GetUtcNow().AddHours(1), PreserveForRecovery: true);
    var owner = new ScheduledOwner("scheduled-oauth", (context, _) => Task.FromResult(Renewed(context)));
    var due = Add(f, owner.Owner, ["quota"], clock.GetUtcNow().AddMinutes(1));
    Add(f, owner.Owner, ["quota"], clock.GetUtcNow().AddHours(1));
    Add(f, owner.Owner, ["quota"], clock.GetUtcNow().AddMinutes(1), enabled: false);
    Add(f, owner.Owner, ["inference"], clock.GetUtcNow().AddMinutes(1));
    Add(f, "unregistered-oauth", ["quota"], clock.GetUtcNow().AddMinutes(1));
    Add(f, "static-key", ["quota"], clock.GetUtcNow().AddMinutes(1));
    Add(f, "external-snapshot", ["quota"], clock.GetUtcNow().AddMinutes(1));
    Add(f, "core", ["quota"], clock.GetUtcNow().AddMinutes(1));
    var oldUnselected = Add(f, owner.Owner, ["inference", "quota"], clock.GetUtcNow().AddMinutes(-1));
    f.Accounts.Connect(f.Repository.Snapshot.Revision, new("test", "fresh-quota", ["quota"], AccountId: oldUnselected.Id),
        Key("fresh-selected-quota", owner: owner.Owner, recovery: true) with { ExpiresAt = clock.GetUtcNow().AddHours(1) });
    var ownerService = new CredentialOwnerService(f.Repository, f.Accounts, f.Secrets.Generations, [owner]);
    var automatic = new OwnedCredentialRefreshService(f.Repository, ownerService, f.Secrets.Generations, [owner], clock);
    var observations = await automatic.RefreshDueAsync();
    Check(owner.Calls == 1 && observations.Single().State == "renewed", "automatic renewal selects only near-expiry enabled quota capability with registered owner");
    Check(f.Repository.Snapshot.Accounts.Single(a => a.Id == oldUnselected.Id).Credentials!.Any(h => h.ExpiresAt < clock.GetUtcNow()),
        "expired nonselected inference owner remains untouched while selected quota is fresh");
    Check(f.Repository.Snapshot.Accounts.Single(a => a.Id == due.Id).Credentials!.Single().Generation == 2, "automatic owner publishes renewed immutable generation");
    await automatic.RefreshDueAsync();
    Check(owner.Calls == 1, "fresh renewed credential is not renewed again on next pass");

    var failed = New("automatic-failed");
    var failingOwner = new ScheduledOwner("failing-oauth", (_, _) => throw new IOException("synthetic-private-response-must-not-leak"));
    Add(failed, failingOwner.Owner, ["quota"], clock.GetUtcNow());
    var failures = new CredentialOwnerService(failed.Repository, failed.Accounts, failed.Secrets.Generations, [failingOwner]);
    var failedLoop = new OwnedCredentialRefreshService(failed.Repository, failures, failed.Secrets.Generations, [failingOwner], clock);
    var failedStatus = await failedLoop.RefreshDueAsync();
    await failedLoop.RefreshDueAsync();
    var restartedLoop = new OwnedCredentialRefreshService(failed.Repository, failures, failed.Secrets.Generations, [failingOwner], clock);
    await restartedLoop.RefreshDueAsync();
    Check(failingOwner.Calls == 1 && failedStatus.Single().State == "recovery-required", "failed generation is not blindly rotated again across ticks or helper restart");
    Check(!JsonSerializer.Serialize(failedStatus).Contains("synthetic-private-response-must-not-leak"), "automatic renewal diagnostics never contain raw provider exceptions");
    var failedAccount = failed.Repository.Snapshot.Accounts.Single();
    failed.Accounts.Connect(failed.Repository.Snapshot.Revision, new("test", "explicit-reconnect", ["quota"], AccountId: failedAccount.Id),
        Key("explicit-new-grant", owner: failingOwner.Owner, recovery: true) with { ExpiresAt = clock.GetUtcNow() });
    await failedLoop.RefreshDueAsync();
    Check(failingOwner.Calls == 2, "failure suppression is generation-scoped and permits an explicitly reconnected new grant");

    var bounded = New("automatic-bounds");
    var firstTwo = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var active = 0; var maximumActive = 0; var concurrentGate = new object();
    var boundedOwner = new ScheduledOwner("bounded-oauth", async (context, token) =>
    {
        lock (concurrentGate) { active++; maximumActive = Math.Max(active, maximumActive); if (active == 2) firstTwo.TrySetResult(); }
        try { await release.Task.WaitAsync(token); return Renewed(context); }
        finally { lock (concurrentGate) active--; }
    });
    for (var index = 0; index < 10; index++) Add(bounded, boundedOwner.Owner, ["quota"], clock.GetUtcNow().AddMinutes(1));
    var boundedService = new CredentialOwnerService(bounded.Repository, bounded.Accounts, bounded.Secrets.Generations, [boundedOwner]);
    var boundedLoop = new OwnedCredentialRefreshService(bounded.Repository, boundedService, bounded.Secrets.Generations, [boundedOwner], clock);
    var pass = boundedLoop.RefreshDueAsync();
    await firstTwo.Task.WaitAsync(TimeSpan.FromSeconds(2));
    Check(boundedOwner.Calls == 2 && !pass.IsCompleted, "automatic renewal limits simultaneous provider operations");
    release.SetResult();
    var completed = await pass;
    Check(boundedOwner.Calls == 8, $"one pass attempts exactly eight independent owners (observed {boundedOwner.Calls})");
    Check(maximumActive == 2, $"remote owners retain two-way concurrency (observed {maximumActive})");
    Check(completed.Length == 8 && completed.All(o => o.State == "renewed"),
        "unrelated account publications preserve all renewal results: " + string.Join(", ", completed.GroupBy(o => o.State).Select(group => group.Key + "=" + group.Count())));
    await boundedLoop.RefreshDueAsync();
    Check(boundedOwner.Calls == 10, "later pass handles remaining due accounts without repeating completed generations");

    var changed = New("automatic-selection-change");
    var firstSelection = Add(changed, "changing-oauth", ["quota"], clock.GetUtcNow());
    Add(changed, "changing-oauth", ["quota"], clock.GetUtcNow());
    var superseded = Add(changed, "changing-oauth", ["quota"], clock.GetUtcNow());
    using var changedMutations = new SemaphoreSlim(1, 1);
    var changingOwner = new ScheduledOwner("changing-oauth", (context, _) =>
    {
        if (context.AccountId == firstSelection.Id)
        {
            changedMutations.Wait();
            try
            {
                changed.Accounts.Connect(changed.Repository.Snapshot.Revision, new("test", "replace-selection", ["quota"], AccountId: superseded.Id),
                    Key("new-static-quota"));
            }
            finally { changedMutations.Release(); }
        }
        return Task.FromResult(Renewed(context));
    });
    var changedOwners = new CredentialOwnerService(changed.Repository, changed.Accounts, changed.Secrets.Generations, [changingOwner], changedMutations);
    var changedLoop = new OwnedCredentialRefreshService(changed.Repository, changedOwners, changed.Secrets.Generations, [changingOwner], clock);
    var changedStatus = await changedLoop.RefreshDueAsync();
    Check(changingOwner.Calls == 2 && changedStatus.Single(status => status.AccountId == superseded.Id).State == "superseded",
        "stale pass selection cannot renew a credential that lost its quota capability");

    var blocked = New("owner-publication-gate");
    var blockedAccount = Add(blocked, owner.Owner, ["quota"], clock.GetUtcNow());
    using var publication = new SemaphoreSlim(1, 1);
    using var cancellation = new CancellationTokenSource();
    var remoteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var remoteResult = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var blockedOwner = new ScheduledOwner(owner.Owner, async (context, token) =>
    {
        remoteStarted.SetResult();
        await remoteResult.Task.WaitAsync(token);
        return Renewed(context);
    });
    var blockedService = new CredentialOwnerService(blocked.Repository, blocked.Accounts, blocked.Secrets.Generations, [blockedOwner], publication);
    var blockedHandle = blockedAccount.Credentials!.Single();
    var pending = blockedService.RefreshAsync(blockedAccount.Id, blockedHandle.Id, cancellation.Token);
    await remoteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await publication.WaitAsync().WaitAsync(TimeSpan.FromSeconds(2)); // Apply can acquire the gate during the provider wait.
    remoteResult.SetResult();
    Check(!pending.IsCompleted && blocked.Secrets.Generations.IsRetired(blockedHandle), "owner refresh runs and retires old material outside blocked publication gate");
    var recoveryFile = Directory.EnumerateFiles(Path.Combine(blocked.Paths.Credentials, "recovery"), "rotation-*.json").Single();
    var recoveryId = Path.GetFileNameWithoutExtension(recoveryFile);
    for (var attempt = 0; attempt < 200 && CredentialRecoveryService.Read(blocked.Secrets.Generations, recoveryId).Handle?.Generation != 2; attempt++)
        await Task.Delay(10);
    Check(CredentialRecoveryService.Read(blocked.Secrets.Generations, recoveryId).Handle?.Generation == 2, "new rotation result is durable before waiting for apply gate");
    cancellation.Cancel();
    await Throws<OperationCanceledException>(() => pending, "cancelled publication wait must not publish owner result");
    Check(publication.CurrentCount == 0 && blocked.Repository.Snapshot.Accounts.Single().Credentials!.Single().Generation == 1,
        "cancelled owner wait preserves references and cannot release another gate lease");
    ThrowsSync<InvalidDataException>(() => new CredentialResolver(blocked.Repository, blocked.Secrets).Resolve(blockedAccount.Id, "quota"), "cancellation never resurrects consumed old refresh material");
    new CredentialRecoveryService(blocked.Repository, blocked.Accounts, blocked.Secrets.Generations).Adopt(blocked.Repository.Snapshot.Revision, recoveryId);
    Check(blocked.Repository.Snapshot.Accounts.Single().Credentials!.Single().Generation == 2, "saved cancelled owner result remains explicitly recoverable");
}

async Task QuotaBoundaryRegressions()
{
    var f = New("quota-boundaries");
    var account = new AccountDto("legacy-stable", "Chosen name", "api.kimi.com", true, "api-key", "unverified", Binding: new("api.kimi.com"));
    f.Repository.Update(0, state => state with { Accounts = [account] });
    string Descriptor(string endpoint, string key = "saved-fixture-key", string? accountId = null, bool enabled = true) =>
        JsonSerializer.Serialize(new { kind = "api-key", provider = "kimi", apiKey = key, baseUrl = endpoint,
            settings = accountId is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["accountId"] = accountId }, sourceEnabled = enabled });
    void SaveTransports(params string[] transports)
    {
        var rootJson = System.Text.Json.Nodes.JsonNode.Parse(Descriptor("https://gateway.example.test/v1"))!.AsObject();
        rootJson["transports"] = new System.Text.Json.Nodes.JsonArray(transports.Select(s => System.Text.Json.Nodes.JsonNode.Parse(s)).ToArray());
        f.Secrets.WriteAccount(account.Id, rootJson.ToJsonString());
    }
    SaveTransports(Descriptor("https://gateway.example.test/v1"), Descriptor("https://api.kimi.com/coding/v1"));
    var lease = await f.Source.GetCredentialAsync(account.Id);
    Check(lease is { BaseUrl: "https://api.kimi.com/coding/v1", CredentialKind: "api-key", BearerToken: "saved-fixture-key" },
        "legacy selected credential reuses its own qualified official transport");
    Check(f.Secrets.ReadAccount(account.Id).Contains("gateway.example.test"), "quota transport projection does not rewrite saved descriptors");
    SaveTransports(Descriptor("https://api.kimi.com/coding/v1"), Descriptor("https://gateway.example.test/v1"));
    Check((await f.Source.GetCredentialAsync(account.Id))?.BaseUrl == lease!.BaseUrl, "transport qualification is independent of import order");
    SaveTransports(Descriptor("https://api.kimi.com/coding/v1", key: "other-fixture-key"));
    Check((await f.Source.GetCredentialAsync(account.Id))?.BaseUrl == "https://gateway.example.test/v1", "quota cannot borrow another transport credential");
    SaveTransports(Descriptor("https://api.kimi.com/coding/v1", enabled: false));
    Check((await f.Source.GetCredentialAsync(account.Id))?.BaseUrl == "https://gateway.example.test/v1", "disabled official transport is not selected");
    SaveTransports(Descriptor("https://api.kimi.com/coding/v1", accountId: "scope-a"), Descriptor("https://api.kimi.ai/coding/v1", accountId: "scope-b"));
    Check((await f.Source.GetCredentialAsync(account.Id))?.BaseUrl == "https://gateway.example.test/v1", "ambiguous saved resource scopes are not selected by order");
    SaveTransports(Descriptor("https://api.kimi.com.evil.test/coding/v1"));
    Check((await f.Source.GetCredentialAsync(account.Id))?.BaseUrl == "https://gateway.example.test/v1", "lookalike origin never qualifies");
    f.Repository.Update(f.Repository.Snapshot.Revision, state => state with { Accounts = [account with { Binding = new(account.Provider, ResourceType: "account", ResourceId: "scope-a") }] });
    SaveTransports(Descriptor("https://api.kimi.com/coding/v1", accountId: "scope-b"));
    Check((await f.Source.GetCredentialAsync(account.Id))?.BaseUrl == "https://gateway.example.test/v1", "transport cannot cross the account's pinned resource binding");
    f.Repository.Update(f.Repository.Snapshot.Revision, state => state with { Accounts = [account] });
    SaveTransports(Descriptor("https://api.kimi.com/coding/v1"));
    var calls = 0;
    var reader = new FakeQuota("kimi", (a, c, _) =>
    {
        calls++;
        Check(a.Id == account.Id && a.Provider == "kimi" && a.Binding?.Provider == "kimi", "reader receives canonical boundary view with unchanged identity");
        return Task.FromResult(new QuotaFetchResult([new("fixture", "units", Remaining: 7)]));
    });
    var service = new QuotaService(f.Source, f.Source, [reader]);
    Check((await service.RefreshAsync()).Single().Remaining == 7, "official host alias dispatches existing reader");
    Check(f.Repository.Snapshot.Accounts.Single().Provider == "api.kimi.com", "dispatch preserves persisted provider and account ID");
    f.Repository.Update(f.Repository.Snapshot.Revision, state => state with { Accounts = [state.Accounts.Single() with { Capabilities = [new("inference", "unverified")] }] });
    Check(await f.Source.GetCredentialAsync(account.Id) is null, "explicit quota denial cannot reuse legacy inference access");
    await service.RefreshAsync();
    Check(calls == 1, "denied quota purpose never reaches direct reader");
    var reconnected = f.Accounts.ConnectCredential(f.Repository.Snapshot.Revision,
        new(new("KIMI", "api-key", ["quota"], AccountId: account.Id), "reconnected-fixture-key", BaseUrl: "https://api.kimi.com/coding/v1")).Accounts.Single();
    Check(reconnected.Id == account.Id && reconnected.Name == account.Name && reconnected.Provider == account.Provider,
        "canonical reconnect preserves legacy identity and chosen name");

    var aliasMonitor = New("monitor-host-alias");
    var monitorAccount = new AccountDto("monitor-legacy-stable", "Fireworks", "api.fireworks.ai", true, "api-key", "unverified");
    aliasMonitor.Repository.Update(0, state => state with { Accounts = [monitorAccount] });
    aliasMonitor.Secrets.WriteAccount(monitorAccount.Id, "{\"kind\":\"api-key\",\"apiKey\":\"legacy-inference\",\"baseUrl\":\"https://api.fireworks.ai/inference/v1\"}");
    aliasMonitor.Monitor.Set(1, monitorAccount.Id, new("api-token", "separate-monitor", "fixture-account"));
    var monitorLease = await aliasMonitor.Source.GetCredentialAsync(monitorAccount.Id);
    Check(monitorLease is { BearerToken: "", CredentialKind: "api-token", MonitorBearerToken: "separate-monitor" }, "host-alias reconnect retains selected independent monitor metadata");
    var created = New("new-monitor-alias");
    Check(created.Monitor.Create(0, "Google", "generativelanguage.googleapis.com", new("access-token-snapshot", "snapshot", "my-project", DateTimeOffset.UtcNow.AddHours(1))).Accounts.Single().Provider == "gemini-api",
        "new monitor alias uses consistent public and private provider identity");

    var diagnostics = New("quota-safe-diagnostics");
    diagnostics.Accounts.ConnectCredential(0, new(new("kimi", "api-key", ["quota"]), "diagnostic-private-key", BaseUrl: "https://api.kimi.com/coding/v1"));
    using var handler = new QuotaStatusHandler();
    using var client = new HttpClient(handler);
    var diagnosticService = new QuotaService(diagnostics.Source, diagnostics.Source, [new KimiQuotaAdapter(client)]);
    Check((await diagnosticService.RefreshAsync()).Single().UsedPercent == 20, "diagnostic fixture starts with provider quantity");
    foreach (var (status, category) in new[] { (401, "authentication"), (403, "permission"), (429, "rate-limit"), (503, "provider-unavailable") })
    {
        handler.Status = (System.Net.HttpStatusCode)status;
        var row = (await diagnosticService.RefreshAsync()).Single();
        Check(row.State == "stale" && row.UsedPercent == 20 && row.Error == $"Quota HTTP {status} ({category}).", "safe HTTP status retains stale provider quantities");
        Check(!JsonSerializer.Serialize(row).Contains("sensitive-fixture"), "provider body and reason phrase never enter diagnostics");
    }

    var optional = New("optional-passive-access");
    var passiveAccount = optional.Accounts.Connect(0, new("groq", "key", ["inference"]), Key("inference-only", "groq", ["inference"])).Accounts.Single();
    var passiveReader = new FakeQuota("groq", (_, c, _) =>
    {
        Check(c.BearerToken == "" && c.Monitor is null && c.ProviderAccountId is null, "optional fallback carries no direct credential or resource identity");
        return Task.FromResult(new QuotaFetchResult([new("passive", "requests", Remaining: 4)]));
    }) { AllowsMissingCredential = true };
    var passiveService = new QuotaService(optional.Source, optional.Source, [passiveReader]);
    Check((await passiveService.RefreshAsync()).Single().Remaining == 4, "passive composite works with explicitly absent quota purpose");
    optional.Accounts.Connect(optional.Repository.Snapshot.Revision, new("groq", "expired", ["quota"], AccountId: passiveAccount.Id),
        new("static-key", ["quota"], new("groq"), JsonSerializer.Serialize(new { kind = "api-key", apiKey = "expired-private", expiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) })));
    var expired = (await passiveService.RefreshAsync()).Single();
    Check(expired.Remaining == 4 && expired.Error == "Access snapshot expired; reconnect account.", "expired optional access preserves passive observation and expiry diagnosis");
    optional.Repository.Update(optional.Repository.Snapshot.Revision, state => state with { Accounts = [state.Accounts.Single() with { Capabilities = [new("quota", "unverified", "missing-generation")] }] });
    var unreadable = (await passiveService.RefreshAsync()).Single();
    Check(unreadable.Remaining == 4 && unreadable.Error == "Quota access unavailable (credential).", "unreadable optional generation preserves passive observation with safe diagnosis");
    Check((await passiveService.GetSnapshotsAsync()).Single().Remaining == 4, "passive fallback cache survives unavailable direct generation");
}

sealed class QuotaStatusHandler : HttpMessageHandler
{
    public System.Net.HttpStatusCode Status = System.Net.HttpStatusCode.OK;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(Status)
    {
        ReasonPhrase = "sensitive-fixture-reason",
        Content = new StringContent(Status == System.Net.HttpStatusCode.OK ? "{\"usages\":{\"limit_5h\":{\"used_ratio\":0.2}}}" : "sensitive-fixture-body")
    });
}

sealed class Fixture
{
    public OwnStatePaths Paths { get; }
    public SecretStore Secrets { get; }
    public ConfigurationRepository Repository { get; }
    public AccountService Accounts { get; }
    public MonitorAccessService Monitor { get; }
    public HostQuotaSource Source { get; }
    public Fixture(string path)
    {
        Paths = new(path); Secrets = new(Paths); Repository = new(Paths); Accounts = new(Repository, Secrets);
        Monitor = new(Repository, Secrets, new(Paths)); Source = new(Repository, Secrets, Monitor);
    }
    public LoginOperationService Logins(IAccountLoginAdapter adapter, TimeProvider? clock = null) =>
        new(configuration: Repository, accounts: Accounts, secrets: Secrets, accountAdapters: [adapter], timeProvider: clock);
}
sealed class FakeLogin(string provider = "test") : IAccountLoginAdapter
{
    public string Provider => provider; public string Method => "fake";
    public int Begins, Polls, Cancels; public bool FailBegin;
    public LoginAdapterResult Next = new("waiting");
    public Func<CancellationToken, Task<LoginAdapterResult>>? Poll;
    public Task<LoginAdapterResult> BeginAsync(LoginOperationContext context, CancellationToken ct)
    { Begins++; if (FailBegin) throw new IOException("secret must not leak"); return Task.FromResult(new LoginAdapterResult("waiting")); }
    public Task<LoginAdapterResult> StatusAsync(LoginOperationContext context, CancellationToken ct) { Polls++; return Poll?.Invoke(ct) ?? Task.FromResult(Next); }
    public Task CancelAsync(LoginOperationContext context, CancellationToken ct) { Cancels++; return Task.CompletedTask; }
}
sealed class FakeOwner : ICredentialOwner
{
    public string Owner => "session-owner"; public int Calls;
    public required Func<CancellationToken, Task<PreparedLoginCredential>> Refresh;
    public Task<PreparedLoginCredential> RefreshAsync(CredentialRefreshContext context, CancellationToken ct) { Calls++; return Refresh(ct); }
}
sealed class ScheduledOwner(string owner, Func<CredentialRefreshContext, CancellationToken, Task<PreparedLoginCredential>> refresh) : ICredentialOwner
{
    public string Owner => owner;
    private int calls;
    public int Calls => Volatile.Read(ref calls);
    public Task<PreparedLoginCredential> RefreshAsync(CredentialRefreshContext context, CancellationToken ct)
    { Interlocked.Increment(ref calls); return refresh(context, ct); }
}
sealed class FakeQuota(string provider, Func<AccountDto, QuotaCredential, CancellationToken, Task<QuotaFetchResult>> fetch) : IQuotaAdapter
{
    public string Source => "fixture";
    public bool AllowsMissingCredential { get; init; }
    public Func<AccountDto, QuotaCredential, CancellationToken, Task<QuotaFetchResult>> Fetch = fetch;
    public bool Supports(string value) => value == provider;
    public Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct) => Fetch(account, credential, ct);
}
sealed class FakeClock : TimeProvider
{
    private DateTimeOffset now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan elapsed) => now += elapsed;
    // Expiry is also checked synchronously by Status; timers don't wait on real time in fixtures.
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new NoTimer();
    private sealed class NoTimer : ITimer
    { public bool Change(TimeSpan dueTime, TimeSpan period) => true; public void Dispose() { } public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
}
sealed class CheckpointLogin : IAccountLoginAdapter
{
    public string Provider => "test"; public string Method => "fake"; public int Releases;
    public Action<LoginOperationContext>? AfterCheckpoint;
    public Task<LoginAdapterResult> BeginAsync(LoginOperationContext context, CancellationToken ct) => Task.FromResult(new LoginAdapterResult("waiting"));
    public async Task<LoginAdapterResult> StatusAsync(LoginOperationContext context, CancellationToken ct)
    {
        var credential = new PreparedLoginCredential("static-key", ["quota"], new("test"), "{\"kind\":\"api-key\",\"apiKey\":\"issued-key\"}", PreserveForRecovery: true);
        await context.CheckpointCredentialAsync!(credential, ct);
        AfterCheckpoint?.Invoke(context);
        return new("prepared", Credential: credential with { Binding = new("test", Subject: "provider-subject", Provenance: "verified") });
    }
    public Task CancelAsync(LoginOperationContext context, CancellationToken ct) => Task.CompletedTask;
    public Task ReleaseAsync(LoginOperationContext context, CancellationToken ct) { Releases++; return Task.CompletedTask; }
}
