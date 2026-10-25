using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Accounts.Credentials;
using Lain.Host.Operations;

internal static class DurableRemovalChecks
{
    public static async Task<int> Run(string root)
    {
        var checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        void Reject(Action action, string message)
        { try { action(); } catch (Exception error) when (error is IOException or InvalidOperationException) { checks++; return; } throw new Exception(message); }

        // A retained remote result falls out of the bounded interactive registry while its own
        // profile is still available. Removing its target must find and retire that disk result.
        var pruned = new Fixture(Path.Combine(root, "pruned"));
        var account = pruned.Create();
        var clock = new Clock();
        var adapter = new Adapter();
        await using (var service = pruned.Logins(adapter, clock))
        {
            var started = await service.BeginAsync(pruned.Repository.Snapshot.Revision,
                new("test", "durable", ["quota"], AccountId: account.Id, IdempotencyKey: "pruned-login"));
            pruned.Repository.Update(pruned.Repository.Snapshot.Revision, s => s);
            Check((await service.StatusAsync(started.Id)).State == "recovery-required", "fixture keeps an unpublished owned result");
            clock.Advance(LoginOperationService.TerminalLifetime + TimeSpan.FromSeconds(1));
            try { await service.StatusAsync(started.Id); throw new Exception("terminal operation was not pruned"); }
            catch (KeyNotFoundException) { checks++; }
            Check(adapter.Profiles.Count == 1, "pruning does not silently destroy recoverable authorization");
            pruned.Accounts.Remove(pruned.Repository.Snapshot.Revision, account.Id);
            var record = CredentialRecoveryService.Read(pruned.Secrets.Generations, started.Id);
            Check(record.State == "target-removed", "durable target fence applies before disk cleanup");
            await service.CancelRemovedAccountAsync(account.Id);
            Check(adapter.Profiles.Count == 0 && adapter.Removals == 1, "pruned result releases its unselected owned profile");
            Check(CredentialRecoveryService.Read(pruned.Secrets.Generations, started.Id).TargetCleanupCompleted,
                "successful retirement is durable");
            await service.CleanupRemovedRecoveriesAsync();
            Check(adapter.Removals == 1, "completed disk cleanup is not repeated");
        }

        // Disk-only records survive a service restart, including an exact same-ID/generation
        // recreation. An explicit new operation captures the new fence and remains usable.
        var persisted = new Fixture(Path.Combine(root, "restart"));
        account = persisted.Create();
        var other = persisted.Create();
        var saved = persisted.Prepare(account.Id, "saved", "profile-saved");
        var unaffected = persisted.Prepare(other.Id, "other", "profile-other");
        persisted.Accounts.Remove(persisted.Repository.Snapshot.Revision, account.Id);
        persisted.Repository.Update(persisted.Repository.Snapshot.Revision, s => s with { Accounts = [.. s.Accounts, account] });
        var restarted = new Fixture(persisted.Paths.Root);
        var recovery = new CredentialRecoveryService(restarted.Repository, restarted.Accounts, restarted.Secrets.Generations);
        Reject(() => recovery.Adopt(restarted.Repository.Snapshot.Revision, saved.Context.OperationId), "same-ID recreation revived saved authorization");
        CredentialRecoveryService.SaveStarted(restarted.Secrets.Generations, saved.Context);
        CredentialRecoveryService.SavePrepared(restarted.Secrets.Generations, saved.Context, saved.Handle, null);
        CredentialRecoveryService.MarkPublished(restarted.Secrets.Generations, saved.Context.OperationId);
        Check(CredentialRecoveryService.Read(restarted.Secrets.Generations, saved.Context.OperationId).State == "target-removed",
            "late started/prepared/published writes cannot revive a removed target");
        adapter = new Adapter(); adapter.Profiles.UnionWith(["profile-saved", "profile-other"]);
        await using (var service = restarted.Logins(adapter))
        {
            await service.CleanupRemovedRecoveriesAsync();
            Check(adapter.Profiles.SetEquals(["profile-other"]) && adapter.Cancels == 0, "disk cleanup affects only removed target and never calls revoke/cancel");
            Check(CredentialRecoveryService.Read(restarted.Secrets.Generations, unaffected.Context.OperationId).State == "prepared",
                "another account's recoverable result survives");
            var next = await service.BeginAsync(restarted.Repository.Snapshot.Revision, new("test", "durable", ["quota"], AccountId: account.Id));
            Check((await service.StatusAsync(next.Id)).State == "completed", "new explicit operation on recreated ID uses current removal epoch");

            var lateHandle = restarted.Secrets.Generations.Prepare(account.Id, Credential("profile-late"), saved.Context.OperationId, saved.Handle.Id, 2);
            adapter.Profiles.Add("profile-late");
            CredentialRecoveryService.SavePrepared(restarted.Secrets.Generations, saved.Context, lateHandle, null);
            Check(!CredentialRecoveryService.Read(restarted.Secrets.Generations, saved.Context.OperationId).TargetCleanupCompleted,
                "later private generation reopens local cleanup without reopening adoption");
            for (var i = 0; i < 3; i++) await service.CleanupRemovedRecoveriesAsync();
            Check(!adapter.Profiles.Contains("profile-late"), "late generation is retired on a later bounded pass");
            Reject(() => recovery.Adopt(restarted.Repository.Snapshot.Revision, saved.Context.OperationId), "late result regained adoption rights");
        }

        // The removal journal is written before config publication. Failed publication preserves
        // existing access and old recovery; a crash after config publication completes on restart.
        var failure = new Fixture(Path.Combine(root, "failed-save"));
        account = failure.Create(); saved = failure.Prepare(account.Id, "failed-remove", "profile-failed-remove");
        File.Move(failure.Paths.Configuration, failure.Paths.Configuration + ".original");
        Directory.CreateDirectory(failure.Paths.Configuration);
        try { Reject(() => failure.Accounts.Remove(failure.Repository.Snapshot.Revision, account.Id), "removal accepted failed config save"); }
        finally { Directory.Delete(failure.Paths.Configuration); File.Move(failure.Paths.Configuration + ".original", failure.Paths.Configuration); }
        Check(failure.Repository.Snapshot.Accounts.Single().Id == account.Id
            && CredentialRecoveryService.Read(failure.Secrets.Generations, saved.Context.OperationId).State == "prepared",
            "failed config publication retains account and recovery adoption rights");
        new CredentialRecoveryService(failure.Repository, failure.Accounts, failure.Secrets.Generations).Adopt(failure.Repository.Snapshot.Revision, saved.Context.OperationId);
        Check(failure.Repository.Snapshot.Accounts.Single().Id == account.Id, "retained recovery remains usable after failed removal");

        foreach (var published in new[] { false, true })
        {
            var crash = new Fixture(Path.Combine(root, "crash-" + published));
            account = crash.Create(); saved = crash.Prepare(account.Id, "crash-remove", "profile-crash");
            var epoch = Guid.NewGuid().ToString("N");
            var marker = Path.Combine(crash.Paths.Credentials, "removed-targets", account.Id + ".json");
            OwnStatePaths.WritePrivate(marker, JsonSerializer.Serialize(new { AccountId = account.Id, Epoch = (string?)null,
                Pending = new { Id = epoch, Revision = crash.Repository.Snapshot.Revision, AccountGeneration = account.Generation } }));
            if (published) crash.Repository.Update(crash.Repository.Snapshot.Revision, s => s with { Accounts = [] });
            var afterCrash = new Fixture(crash.Paths.Root);
            var record = CredentialRecoveryService.Read(afterCrash.Secrets.Generations, saved.Context.OperationId, afterCrash.Repository.Snapshot);
            Check(record.State == (published ? "target-removed" : "prepared"), "restart resolves removal publication boundary from durable configuration");
            if (published)
            {
                afterCrash.Repository.Update(afterCrash.Repository.Snapshot.Revision, s => s with { Accounts = [account] });
                Reject(() => new CredentialRecoveryService(afterCrash.Repository, afterCrash.Accounts, afterCrash.Secrets.Generations)
                    .Adopt(afterCrash.Repository.Snapshot.Revision, saved.Context.OperationId), "restart completion failed to fence recreated target");
            }
        }

        // More poisoned records than the per-pass cleanup limit cannot starve later healthy
        // profiles. Every record stays on disk; no unbounded account/profile dictionary is built.
        var bounded = new Fixture(Path.Combine(root, "bounded"));
        account = bounded.Create(); adapter = new Adapter();
        for (var i = 0; i < 20; i++)
        {
            var profile = "profile-" + i;
            bounded.Prepare(account.Id, "bounded-" + i, profile);
            adapter.Profiles.Add(profile);
            if (i < 12) adapter.Fail.Add(profile);
        }
        // Unknown malformed metadata must not suppress later valid entries or authorize cleanup.
        OwnStatePaths.WritePrivate(bounded.Secrets.Generations.RecoveryPath("malformed"), "{broken");
        bounded.Accounts.Remove(bounded.Repository.Snapshot.Revision, account.Id);
        await using (var service = bounded.Logins(adapter))
        {
            for (var pass = 0; pass < 5; pass++)
            {
                var before = adapter.Removals;
                await service.CleanupRemovedRecoveriesAsync();
                Check(adapter.Removals - before <= LoginOperationService.MaximumRecoveryCleanupAttempts, "disk cleanup attempts are bounded per pass");
            }
            Check(adapter.Profiles.SetEquals(adapter.Fail), "poisoned retries do not starve healthy disk-only profiles");
        }
        adapter.Fail.Clear();
        await using (var service = new Fixture(bounded.Paths.Root).Logins(adapter))
        {
            for (var pass = 0; pass < 4; pass++) await service.CleanupRemovedRecoveriesAsync();
            Check(adapter.Profiles.Count == 0, "pending cleanup survives service restart");
        }
        return checks;
    }

    private static PreparedLoginCredential Credential(string profile) => new("test-owner", ["quota"], new("test"),
        JsonSerializer.Serialize(new { kind = "test-owned-profile", profileReference = profile }), PreserveForRecovery: true);
    private sealed class Fixture
    {
        public OwnStatePaths Paths { get; }
        public SecretStore Secrets { get; }
        public ConfigurationRepository Repository { get; }
        public AccountService Accounts { get; }
        public Fixture(string path) { Paths = new(path); Secrets = new(Paths); Repository = new(Paths); Accounts = new(Repository, Secrets); }
        public AccountDto Create() => Accounts.Connect(Repository.Snapshot.Revision, new("test", "manual", ["quota"]),
            new("static-key", ["quota"], new("test"), "{\"kind\":\"api-key\",\"apiKey\":\"synthetic-base\"}")).Accounts.Last();
        public (LoginOperationContext Context, CredentialHandleDto Handle) Prepare(string account, string id, string profile)
        {
            var context = Accounts.ReserveConnect(Repository.Snapshot.Revision, new("test", "durable", ["quota"], AccountId: account), id, DateTimeOffset.UtcNow.AddMinutes(5));
            CredentialRecoveryService.SaveStarted(Secrets.Generations, context);
            var handle = Secrets.Generations.Prepare(account, Credential(profile), id);
            CredentialRecoveryService.SavePrepared(Secrets.Generations, context, handle, null);
            return (context, handle);
        }
        public LoginOperationService Logins(Adapter adapter, TimeProvider? clock = null) =>
            new(configuration: Repository, accounts: Accounts, secrets: Secrets, accountAdapters: [adapter], timeProvider: clock);
    }
    private sealed class Adapter : IAccountLoginAdapter
    {
        public string Provider => "test";
        public string Method => "durable";
        public readonly HashSet<string> Profiles = new(StringComparer.Ordinal);
        public readonly HashSet<string> Fail = new(StringComparer.Ordinal);
        public int Removals, Cancels;
        public Task<LoginAdapterResult> BeginAsync(LoginOperationContext context, CancellationToken ct)
        { Profiles.Add("profile-" + context.OperationId); return Task.FromResult(new LoginAdapterResult("waiting")); }
        public Task<LoginAdapterResult> StatusAsync(LoginOperationContext context, CancellationToken ct) =>
            Task.FromResult(new LoginAdapterResult("prepared", Credential: Credential("profile-" + context.OperationId)));
        public Task CancelAsync(LoginOperationContext context, CancellationToken ct) { Cancels++; return Task.CompletedTask; }
        public Task TargetRemovedAsync(LoginOperationContext context, PreparedLoginCredential? credential, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Removals++;
            if (credential is null) return Task.CompletedTask;
            using var json = JsonDocument.Parse(credential.PrivateJson);
            var profile = json.RootElement.GetProperty("profileReference").GetString()!;
            if (Fail.Contains(profile)) throw new IOException("synthetic cleanup failure");
            Profiles.Remove(profile); return Task.CompletedTask;
        }
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan elapsed) => now += elapsed;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new Timer();
        private sealed class Timer : ITimer
        { public bool Change(TimeSpan dueTime, TimeSpan period) => true; public void Dispose() { } public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }
}
