using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Accounts.ConsoleConnections.MiMo;
using Lain.Host.Accounts.Credentials;
using Lain.Host.Operations;

internal static class DurableMiMoCleanupChecks
{
    public static async Task<int> Run(string root)
    {
        var checks = 0;
        void Check(bool value, string text) { if (!value) throw new Exception(text); checks++; }
        foreach (var prune in new[] { false, true })
        {
            var paths = new OwnStatePaths(Path.Combine(root, prune.ToString()));
            var secrets = new SecretStore(paths); var repository = new ConfigurationRepository(paths); var accounts = new AccountService(repository, secrets);
            var target = new AccountDto("mimo-disk-target", "MiMo", "xiaomi-mimo", true, "api-key", "unverified");
            repository.Update(0, s => s with { Accounts = [target] });
            await using var browser = new FixtureBrowser { Available = true };
            var clock = new Clock();
            string operationId, profile;
            await using (var operations = new LoginOperationService(configuration: repository, accounts: accounts, secrets: secrets,
                accountAdapters: [new MiMoConsoleLoginAdapter(browser, clock)], timeProvider: clock))
            {
                var started = await operations.BeginAsync(repository.Snapshot.Revision, new("xiaomi-mimo", "console-browser", ["quota"], AccountId: target.Id));
                operationId = started.Id; profile = browser.LastOpened;
                repository.Update(repository.Snapshot.Revision, s => s);
                var choice = await operations.StatusAsync(operationId);
                await operations.SelectResourceAsync(operationId, choice.Resources!.Single());
                Check((await operations.StatusAsync(operationId)).State == "recovery-required", "MiMo fixture retains late-conflict browser profile");
                if (prune)
                {
                    clock.Advance(LoginOperationService.TerminalLifetime + TimeSpan.FromSeconds(1));
                    try { await operations.StatusAsync(operationId); throw new Exception("MiMo recovery was not pruned"); }
                    catch (KeyNotFoundException) { checks++; }
                }
            }
            Check(browser.Active.Contains(profile), "service release preserves unresolved MiMo profile before target removal");
            // New service has no in-memory operation or adapter attempt and no selected quota handle.
            var restartedRepository = new ConfigurationRepository(paths);
            var restartedAccounts = new AccountService(restartedRepository, secrets);
            restartedAccounts.Remove(restartedRepository.Snapshot.Revision, target.Id);
            await using var restarted = new LoginOperationService(configuration: restartedRepository, accounts: restartedAccounts, secrets: secrets,
                accountAdapters: [new MiMoConsoleLoginAdapter(browser)]);
            await restarted.CleanupRemovedRecoveriesAsync();
            Check(!browser.Active.Contains(profile), "disk-only unselected MiMo profile closes through its actual adapter");
            Check(CredentialRecoveryService.Read(secrets.Generations, operationId) is { State: "target-removed", TargetCleanupCompleted: true },
                "MiMo disk recovery remains permanently cancelled after owned profile cleanup");
        }

        // A failed ordinary Cancel is terminal to callers but still owns local cleanup.
        // The retained operation must survive normal terminal pruning and release the same profile.
        foreach (var retryWithStatus in new[] { false, true })
        {
            var paths = new OwnStatePaths(Path.Combine(root, "cancel-retry-" + retryWithStatus));
            var secrets = new SecretStore(paths); var repository = new ConfigurationRepository(paths); var accounts = new AccountService(repository, secrets);
            var target = new AccountDto("mimo-cancel-target", "MiMo", "xiaomi-mimo", true, "api-key", "unverified");
            repository.Update(0, s => s with { Accounts = [target] });
            await using var browser = new FixtureBrowser { Available = true };
            var clock = new Clock();
            await using var operations = new LoginOperationService(configuration: repository, accounts: accounts, secrets: secrets,
                accountAdapters: [new MiMoConsoleLoginAdapter(browser, clock)], timeProvider: clock);
            var started = await operations.BeginAsync(repository.Snapshot.Revision, new("xiaomi-mimo", "console-browser", ["quota"], AccountId: target.Id));
            var profile = browser.LastOpened;
            browser.FailCloseOnce = true;
            Check((await operations.CancelAsync(started.Id)).State == "cancelled" && browser.Active.Contains(profile),
                "failed ordinary cancellation retains its exact owned browser for local retry");
            clock.Advance(LoginOperationService.TerminalLifetime + TimeSpan.FromSeconds(1));
            var retried = retryWithStatus ? await operations.StatusAsync(started.Id) : await operations.CancelAsync(started.Id);
            Check(retried.State == "cancelled" && !browser.Active.Contains(profile) && browser.Closed.SequenceEqual([profile]),
                "terminal Cancel or Status retries same profile after the ordinary pruning deadline");
            Check(browser.Opens == 1 && browser.Reads == 0 && repository.Snapshot.Accounts.Single() == target,
                "cleanup retry never starts login, reads provider usage, or changes account");
        }

        var boundedPaths = new OwnStatePaths(Path.Combine(root, "cancel-bound"));
        var boundedSecrets = new SecretStore(boundedPaths); var boundedRepository = new ConfigurationRepository(boundedPaths);
        var boundedAccounts = new AccountService(boundedRepository, boundedSecrets);
        var boundedTarget = new AccountDto("mimo-bound-target", "MiMo", "xiaomi-mimo", true, "api-key", "unverified");
        boundedRepository.Update(0, s => s with { Accounts = [boundedTarget] });
        await using (var browser = new FixtureBrowser { Available = true })
        await using (var operations = new LoginOperationService(configuration: boundedRepository, accounts: boundedAccounts, secrets: boundedSecrets,
            accountAdapters: [new MiMoConsoleLoginAdapter(browser)]))
        {
            var request = new ConnectAccountRequest("xiaomi-mimo", "console-browser", ["quota"], AccountId: boundedTarget.Id);
            for (var i = 0; i < LoginOperationService.MaximumActiveOperations; i++)
            {
                var started = await operations.BeginAsync(boundedRepository.Snapshot.Revision, request);
                browser.FailCloseOnce = true;
                await operations.CancelAsync(started.Id);
            }
            Check(browser.Active.Count == LoginOperationService.MaximumActiveOperations, "failed cancellation cleanup retains bounded operation slots");
            try { await operations.BeginAsync(boundedRepository.Snapshot.Revision, request); throw new Exception("failed cleanup did not retain active capacity"); }
            catch (InvalidOperationException) { checks++; }
            var opens = browser.Opens;
            await operations.CleanupRemovedRecoveriesAsync();
            Check(browser.Active.Count == 0 && browser.Opens == opens && browser.Reads == 0,
                "periodic local cleanup retires terminal cancellations without reauthorization");
            var afterCleanup = await operations.BeginAsync(boundedRepository.Snapshot.Revision, request);
            Check(afterCleanup.State == "waiting", "successful local release makes the bounded slot reusable");
            await operations.CancelAsync(afterCleanup.Id);

            // Direct local Release has the same ownership rule as Cancel.
            var adapter = new MiMoConsoleLoginAdapter(browser);
            var context = boundedAccounts.ReserveConnect(boundedRepository.Snapshot.Revision, request, "release-retry", DateTimeOffset.UtcNow.AddMinutes(1));
            await adapter.BeginAsync(context, default);
            var profile = browser.LastOpened;
            browser.FailCloseOnce = true;
            try { await adapter.ReleaseAsync(context, default); throw new Exception("release fixture did not fail"); }
            catch (IOException) { checks++; }
            Check(browser.Active.Contains(profile), "failed Release retains its own profile reference");
            await adapter.ReleaseAsync(context, default);
            Check(!browser.Active.Contains(profile), "second Release closes the same profile");
        }
        return checks;
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan delta) => now += delta;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new Timer();
        private sealed class Timer : ITimer
        { public bool Change(TimeSpan dueTime, TimeSpan period) => true; public void Dispose() { } public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }
}
