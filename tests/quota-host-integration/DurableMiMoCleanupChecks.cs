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
