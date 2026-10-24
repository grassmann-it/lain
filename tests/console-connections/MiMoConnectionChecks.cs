using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Accounts.ConsoleConnections;
using Lain.Host.Accounts.ConsoleConnections.MiMo;
using Lain.Host.Operations;
using Lain.Host.Quota;

internal static class MiMoConnectionChecks
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "lain-mimo-fixture-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new OwnStatePaths(root); var secrets = new SecretStore(paths); var configuration = new ConfigurationRepository(paths);
            var accounts = new AccountService(configuration, secrets); var browser = new Browser(); var login = new MiMoConsoleLoginAdapter(browser);
            var inference = new AccountDto("acct-mimo", "MiMo", "xiaomi-mimo", true, "api-key", "unverified");
            secrets.WriteAccount(inference.Id, "{\"kind\":\"api-key\",\"apiKey\":\"synthetic-unchanged\"}");
            configuration.Update(0, s => s with { Accounts = [inference] });
            await using var operations = new LoginOperationService(configuration: configuration, accounts: accounts, secrets: secrets, accountAdapters: [login]);
            var request = new ConnectAccountRequest("xiaomi-mimo", "console-browser", ["quota"], AccountId: inference.Id, ExpectedGeneration: 0);
            var operation = await operations.BeginAsync(configuration.Snapshot.Revision, request);
            Check(operation.State == "waiting" && browser.Opens == 1, "isolated browser started once");
            var selected = await operations.StatusAsync(operation.Id);
            Check(selected.State == "resource-required" && selected.Resources is { Length: 1 } && selected.Resources[0].Subject == "fixture-user", "unbound legacy account requires observed identity selection");
            Check(configuration.Snapshot.Accounts.Single().Generation == 0, "identity discovery does not publish");
            await operations.SelectResourceAsync(operation.Id, selected.Resources![0]);
            var completed = await operations.StatusAsync(operation.Id);
            Check(completed.State == "completed", "browser usage establishes monitor capability");
            var connected = configuration.Snapshot.Accounts.Single();
            Check(connected.Id == inference.Id && connected.Generation == 1 && connected.Capabilities!.Any(c => c.Purpose == "inference"), "monitor attaches to existing account preserving inference");
            Check(secrets.ReadAccount(inference.Id).Contains("synthetic-unchanged"), "existing inference key unchanged");
            Check(browser.Closed == 0 && browser.Reads.All(x => x is ConsoleReadOperation.MiMoIdentity or ConsoleReadOperation.MiMoIndividualUsage), "monitor connect has only fixed identity/usage reads and keeps session alive");
            var quota = new MiMoQuotaAdapter(browser, secrets, configuration);
            var reading = await quota.FetchAsync(connected, new QuotaCredential("never-forward-this-key"));
            Check(reading.Readings.Single().Remaining == 75 && reading.Readings.Single().UsedPercent == 25, "quota uses browser capability without API key");
            Check(reading.Readings.Single().WindowEnd == DateTimeOffset.Parse("2027-10-24T12:00:00Z") && reading.Readings.Single().ResetsAt is null, "annual validity is not a reset");
            browser.FailDetail = true;
            var partial = await quota.FetchAsync(connected, new QuotaCredential(""));
            Check(partial.Readings.Count == 1 && partial.Error is not null, "detail failure keeps measured usage");
            browser.Identity = "other-user";
            Check((await quota.FetchAsync(connected, new QuotaCredential(""))).Readings.Count == 0, "changed console identity never publishes usage");
            browser.Identity = "fixture-user"; browser.Expired = true;
            Check((await quota.FetchAsync(connected, new QuotaCredential(""))).Readings.Count == 0, "expired session no key fallback");
            browser.Expired = false;
            var current = configuration.Snapshot;
            var pending = await operations.BeginAsync(current.Revision, request with { ExpectedGeneration = 1 });
            await operations.CancelAsync(pending.Id);
            Check((await operations.StatusAsync(pending.Id)).State == "cancelled" && configuration.Snapshot.Revision == current.Revision && browser.Closed == 1, "cancel invalidates browser and cannot publish late");
            await quota.DisconnectAsync(connected);
            Check(browser.Closed == 2 && secrets.ReadAccount(inference.Id).Contains("synthetic-unchanged"), "local session disconnect preserves inference");
            browser.Identity = "wrong-user";
            var mismatch = await operations.BeginAsync(current.Revision, request with { ExpectedGeneration = 1 });
            Check((await operations.StatusAsync(mismatch.Id)).State == "failed" && configuration.Snapshot.Revision == current.Revision, "wrong UID reconnect preserves account generation");
            browser.Identity = "fixture-user";
            var monitor = await operations.BeginAsync(configuration.Snapshot.Revision, new("xiaomi-mimo", "console-browser", ["quota"], Name: "MiMo monitor"));
            var discovered = await operations.StatusAsync(monitor.Id);
            await operations.SelectResourceAsync(monitor.Id, discovered.Resources![0]);
            Check((await operations.StatusAsync(monitor.Id)).State == "completed", "monitor-only creation requires no key/core/route");
            var only = configuration.Snapshot.Accounts.Single(a => a.Id != inference.Id);
            Check(!secrets.HasAccount(only.Id) && (await quota.FetchAsync(only, new QuotaCredential(""))).Readings.Count == 1, "monitor-only session can read quota without fake inference credential");
            Check(!JsonSerializer.Serialize(completed).Contains(new string('a', 32)), "profile reference absent from public login result");
            Check(configuration.Snapshot.Routes.Length == 0 && configuration.Snapshot.Profiles.Length == 0, "monitor does not add routes/profiles");
            browser.OnUsage = () => configuration.Update(configuration.Snapshot.Revision, s => s with {
                Accounts = s.Accounts.Select(a => a.Id == only.Id ? a with { Generation = a.Generation + 1 } : a).ToArray() });
            Check((await quota.FetchAsync(only, new QuotaCredential(""))).Readings.Count == 0, "in-flight generation change invalidates measured result");
            browser.OnUsage = null;
            var beforeStale = browser.Reads.Count;
            Check((await quota.FetchAsync(only, new QuotaCredential(""))).Readings.Count == 0 && browser.Reads.Count == beforeStale, "stale selected handle stops before browser access");
            Console.WriteLine("PASS MiMo isolated-browser fake: discovery, same-account attach, monitor-only, cancellation, wrong UID, expiry, partial detail and private capability");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); }
    private sealed class Browser : IMiMoConsoleBrowser
    {
        public bool IsAvailable => true;
        public int Opens, Closed;
        public string Identity = "fixture-user";
        public bool Expired, FailDetail;
        public List<ConsoleReadOperation> Reads = [];
        public Action? OnUsage;
        public Task<string> OpenAsync(string accountId, CancellationToken ct) { Opens++; return Task.FromResult(new string('a', 32)); }
        public Task CloseAsync(string profile, bool erase, CancellationToken ct) { Check(erase, "cancel/disconnect erases owned profile"); Closed++; return Task.CompletedTask; }
        public Task<JsonDocument> ReadAsync(string profile, ConsoleReadOperation operation, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Reads.Add(operation); Check(profile == new string('a', 32), "opaque owned profile");
            if (Expired) return Task.FromResult(JsonDocument.Parse("{\"code\":401,\"data\":{}}"));
            if (FailDetail && operation == ConsoleReadOperation.MiMoIndividualDetail) throw new InvalidOperationException("synthetic-failure");
            if (operation == ConsoleReadOperation.MiMoIndividualUsage) OnUsage?.Invoke();
            return Task.FromResult(JsonDocument.Parse(operation switch
            {
                ConsoleReadOperation.MiMoIdentity => JsonSerializer.Serialize(new { code = 0, data = new { userId = Identity, idc = 310 } }),
                ConsoleReadOperation.MiMoIndividualUsage => "{\"code\":0,\"data\":{\"usage\":{\"items\":[{\"name\":\"yearly\",\"used\":25,\"limit\":100,\"percent\":0.25}]}}}",
                ConsoleReadOperation.MiMoIndividualDetail => "{\"code\":0,\"data\":{\"currentPeriodEnd\":\"2027-10-24T12:00:00Z\"}}",
                _ => throw new Exception("unlisted browser operation")
            }));
        }
    }
}
