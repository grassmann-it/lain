using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Models;
using Lain.Host.Operations;
using Lain.Host.Providers;
using Lain.Host.Quota;

// Independent behavioral acceptance. No sockets, environment credentials or installed state.
var cases = new (string Name, Func<Task> Run)[]
{
    ("monitor projection and redaction", Checks.Credentials),
    ("passive original age and no gateway credential", Checks.PassiveAge),
    ("partial windows preserve model identity", Checks.PartialWindows),
    ("metadata and unknown quantities survive projection", Checks.Metadata),
    ("unsupported capability is neutral", Checks.NeutralCapability),
    ("actual refresh failures retain stale last-good", Checks.RealFailure),
    ("one connection supports independent model routes", Checks.Connections),
    ("neutral presentation versus genuine failure", PresentationChecks.Run)
};
var failures = 0;
foreach (var (name, run) in cases)
{
    try { await run(); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failures++; Console.WriteLine("FAIL " + name + ": " + error.GetType().Name + ": " + error.Message); }
}
Console.WriteLine($"Independent acceptance: {cases.Length - failures}/{cases.Length} groups passed; {Checks.Assertions} assertions; no live requests.");
return failures == 0 ? 0 : 1;

static class Checks
{
    internal static int Assertions;
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-10-24T12:00:00Z");
    private static readonly AccountDto Account = new("review-a", "My own name", "review-provider", true, "api-key", "unverified");
    private static void Check(bool condition, string label)
    { Assertions++; if (!condition) throw new Exception(label); }
    private static void Reject<T>(Action action, string label) where T : Exception
    { try { action(); } catch (T) { Assertions++; return; } throw new Exception(label); }

    internal static Task Credentials()
    {
        var settings = new Dictionary<string, string> { ["accountId"] = "resource-a" };
        var monitor = new QuotaMonitorCredential("external-access-snapshot", "synthetic-monitor", "resource-a", T0.AddHours(1));
        var credential = new QuotaCredential("synthetic-gateway", settings: settings, monitor: monitor);
        settings["accountId"] = "wrong-resource";
        Check(credential.Settings["accountId"] == "resource-a", "credential settings must be an isolated projection");
        Check(credential.BearerToken == "synthetic-gateway" && credential.MonitorBearerToken == "synthetic-monitor", "gateway and monitor are distinct");
        Check(!credential.ToString().Contains("synthetic-") && !monitor.ToString().Contains("synthetic-"), "private projections redact ToString");
        Check(!new MonitorAccessRequest("external-access-snapshot", "synthetic-monitor").ToString().Contains("synthetic-monitor"), "monitor request redacts ToString");
        var imported = QuotaCredential.FromSecretJson("""{"kind":"codex-access-snapshot","accessToken":"synthetic-access","accountId":"provider-a","refreshToken":"never-used"}""");
        Check(imported?.ProviderAccountId == "provider-a" && imported.Monitor is null, "import preserves identity without inventing monitor access");
        Check(QuotaCredential.FromSecretJson("""{"kind":"codex-access-snapshot","refreshToken":"never-used"}""") is null, "refresh token cannot substitute for access");
        Check(typeof(MonitorAccessDto).GetProperties().All(p => p.Name is not ("Secret" or "AccessToken" or "RefreshToken")), "public monitor status has no secret fields");
        return Task.CompletedTask;
    }

    internal static async Task PassiveAge()
    {
        var clock = new Clock(T0.AddMinutes(10));
        var credentials = new CredentialsSource { Forbid = true };
        var row = Reading("tokens", "model-a", 18) with { ObservedAt = T0, DataAsOf = T0, FreshUntil = T0.AddMinutes(1) };
        var adapter = new Adapter { RequiresCredential = false, Result = new([row]) };
        var service = new QuotaService(new AccountsSource(Account), credentials, [adapter], clock);
        var first = (await service.RefreshAsync()).Single();
        Check(adapter.Calls == 1 && credentials.Calls == 0, "passive refresh must not access gateway credentials");
        Check(adapter.ReceivedCredential?.BearerToken is null or "", "passive adapter must not receive inference key");
        Check(first.ObservedAt == T0 && first.DataAsOf == T0 && first.LastSuccessAt == T0, "passive observation age survives refresh");
        Check(first.State == "stale" && first.Remaining == 18, "already-old passive data is immediately stale");
        clock.Advance(TimeSpan.FromMinutes(10));
        var next = (await service.RefreshAsync()).Single();
        Check(next.ObservedAt == first.ObservedAt && next.LastSuccessAt == first.LastSuccessAt && next.State == "stale", "polling the same event cannot renew freshness");
        Check(credentials.Calls == 0, "repeated passive refresh still never reads credentials");
    }

    internal static async Task PartialWindows()
    {
        var clock = new Clock(T0);
        var adapter = new Adapter { Result = new([Reading("tokens-a", "model-a", 11), Reading("tokens-b", "model-b", 77)]) };
        var service = new QuotaService(new AccountsSource(Account), new CredentialsSource(), [adapter], clock);
        var first = await service.RefreshAsync();
        Check(first.Length == 2 && first.Select(r => r.WindowId).Distinct().Count() == 2, "same source can carry distinct windows");
        clock.Advance(TimeSpan.FromSeconds(15));
        adapter.Result = new([Reading("tokens-a", "model-a", 9), Reading("tokens-b", "model-b", null)], "One model window unavailable.");
        var partial = await service.RefreshAsync();
        var a = partial.Single(r => r.WindowId == "tokens-a");
        var b = partial.Single(r => r.WindowId == "tokens-b");
        Check(a.Remaining == 9 && a.Model == "model-a", "updated model retains its own quantity");
        Check(b.Remaining == 77 && b.Model == "model-b" && b.ScopeId == "model-b", "last-good must not copy first same-source model");
        Check(b.State == "stale" && b.LastSuccessAt == T0, "partial last-good retains original age");
    }

    internal static async Task Metadata()
    {
        var row = new QuotaReading("https://official.example/usage", "neurons", Used: 0,
            WindowId: "utc-day", MeasurementKind: "usage", Scope: "account", ScopeId: "provider-a",
            SourceUrl: "https://official.example/usage", ObservedAt: T0, DataAsOf: T0.AddMinutes(-2),
            WindowStart: T0.Date, WindowEnd: T0.Date.AddDays(1), FreshUntil: T0.AddMinutes(5),
            ProviderUpdatedAt: T0.AddMinutes(-3), ApprovedLimit: 10000);
        var service = new QuotaService(new AccountsSource(Account), new CredentialsSource(), [new Adapter { Result = new([row]) }], new Clock(T0));
        var value = (await service.RefreshAsync()).Single();
        Check(value.Used == 0 && value.State == "ok", "observed zero usage is valid evidence");
        Check(value.Limit is null && value.Remaining is null && value.UsedPercent is null && value.ResetsAt is null, "usage/approved ceiling/update time do not invent quota, percent or reset");
        Check(value.MeasurementKind == "usage" && value.Scope == "account" && value.ScopeId == "provider-a", "measurement and identity preserved");
        Check(value.DataAsOf == row.DataAsOf && value.SourceUrl == row.SourceUrl && value.ProviderUpdatedAt == row.ProviderUpdatedAt && value.ApprovedLimit == 10000, "source timestamps and approved ceiling preserved separately");
        Check(value.WindowStart == row.WindowStart && value.WindowEnd == row.WindowEnd && value.FreshUntil == row.FreshUntil, "window bounds and freshness survive");
    }

    internal static async Task NeutralCapability()
    {
        var credentials = new CredentialsSource { Forbid = true };
        var service = new QuotaService(new AccountsSource(Account), credentials, [], new Clock(T0));
        foreach (var rows in new[] { await service.GetSnapshotsAsync(), await service.RefreshAsync() })
        {
            var value = rows.Single();
            Check(value.Error is null && value.Support is not null, "missing capability must be neutral guidance, not an error");
            Check(value.Remaining is null && value.Used is null && value.Limit is null && value.LastSuccessAt is null, "unsupported never fabricates zero or success");
        }
        Check(credentials.Calls == 0, "unsupported source must not read credentials");
    }

    internal static async Task RealFailure()
    {
        var clock = new Clock(T0);
        var adapter = new Adapter { Result = new([Reading("tokens", "model-a", 0)]) };
        var service = new QuotaService(new AccountsSource(Account), new CredentialsSource(), [adapter], clock);
        Check((await service.RefreshAsync()).Single().Remaining == 0, "observed exhaustion is preserved");
        clock.Advance(TimeSpan.FromMinutes(1));
        adapter.Failure = new HttpRequestException("synthetic-private-token and raw provider error");
        var row = (await service.RefreshAsync()).Single();
        Check(row.State == "stale" && row.Remaining == 0 && row.LastSuccessAt == T0 && row.Error is not null, "real failure retains last-good and explicit stale error");
        Check(!JsonSerializer.Serialize(row).Contains("synthetic-private-token"), "raw transport error must be sanitized");
    }

    internal static Task Connections()
    {
        var descriptor = new ProviderDescriptorDto("review-provider", "Review", "Synthetic connection acceptance",
            "https://review.invalid/keys", "https://review.invalid/docs", [], [],
            [new("free", "Free", "free", "account", "tokens")],
            [Route("model-a", "openai-chat", "https://review.invalid/v1"), Route("model-b", "openai-chat", "https://review.invalid/v1"), Route("model-c", "anthropic-messages", "https://review.invalid/anthropic/v1")], [], []);
        var catalog = new ProviderCatalogRegistry([descriptor]);
        var connection = catalog.Find("review-provider")!.Connections?.Single(c => c.Protocol == "openai-chat");
        Check(connection is not null && connection.ProviderRouteIds.Length == 2, "Host groups compatible models into one connection");
        var temporary = Path.Combine(Path.GetTempPath(), "lain-independent-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new OwnStatePaths(temporary);
            var secrets = new SecretStore(paths);
            var repository = new ConfigurationRepository(paths);
            var accounts = new AccountService(repository, secrets, catalog);
            var input = new AddApiAccountRequest("My own name", "review-provider", "synthetic-only-key", connection!.BaseUrl,
                Settings: new() { ["billingPlan"] = "free" }, ProviderConnectionId: connection.Id);
            var state = accounts.AddApi(0, input);
            var account = state.Accounts.Single();
            Check(state.Revision == 1 && state.Routes.Length == 0 && account.State == "unverified", "connection-only setup saves once without activating models or claiming validation");
            Check(account.Name == "My own name", "custom account name survives setup");
            var models = new ModelsService(repository, new ProviderCostPolicy(catalog));
            foreach (var route in descriptor.Routes.Take(2))
                state = models.PutRoute(state.Revision, new(route.Id, route.Id, route.Model, descriptor.Id, [account.Id], ProviderRouteId: route.Id));
            var writer = new ProxyConfigurationWriter(paths, secrets, new ProviderCostPolicy(catalog));
            writer.Write(state);
            Check(state.Accounts.Length == 1 && state.Routes.Length == 2, "two model routes share stable account");
            Check(writer.RouteCredentials["model-a"][account.Id] == writer.RouteCredentials["model-b"][account.Id], "compatible routes share one compiled credential");
            var before = secrets.ReadAccount(account.Id);
            Reject<RevisionConflictException>(() => accounts.ReplaceApiCredential(0, account.Id, "synthetic-replacement"), "stale revision must fail");
            Check(secrets.ReadAccount(account.Id) == before && repository.Snapshot.Revision == state.Revision, "conflict has no private or public mutation");
            var fileCount = Directory.GetFiles(temporary, "*", SearchOption.AllDirectories).Length;
            Reject<ArgumentException>(() => accounts.AddApi(state.Revision, input with { ApiKey = "synthetic-rejected", SelectedModelRouteIds = ["model-c"] }), "model from another connection must fail");
            Check(repository.Snapshot.Revision == state.Revision && Directory.GetFiles(temporary, "*", SearchOption.AllDirectories).Length == fileCount, "invalid selected model leaves no half account or secret");
            state = accounts.ReplaceApiCredential(state.Revision, account.Id, "synthetic-replacement");
            Check(state.Accounts.Single().Id == account.Id && state.Routes.All(r => r.AccountIds.Single() == account.Id), "key replacement preserves account and route identities");
            Check(state.AppliedRevision == 0, "setup and route edits never auto-apply profile");
            // The account ID originally derives from key-A, but replacement must
            // preserve it. Re-adding key-A must never overwrite key-B on that ID,
            // even if public duplicate-identity validation rejects the new add.
            var replacement = secrets.ReadAccount(account.Id);
            var publicBeforeCollision = JsonSerializer.Serialize(repository.Snapshot);
            Reject<ArgumentException>(() => accounts.AddApi(state.Revision, input), "re-adding the old key must reject an existing stable-ID collision");
            Check(secrets.ReadAccount(account.Id) == replacement, "re-adding the old key after rotation must not overwrite the existing account secret");
            Check(JsonSerializer.Serialize(repository.Snapshot) == publicBeforeCollision, "rejected old-key re-add preserves exact revision, accounts, routes and profiles");
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
        return Task.CompletedTask;
    }

    private static ProviderRouteDescriptorDto Route(string id, string protocol, string endpoint) =>
        new(id, id, id, endpoint, protocol, ["free"], new(), PricingClass: "free");
    private static QuotaReading Reading(string window, string model, double? remaining) =>
        new("same-official-source", "tokens", Remaining: remaining, WindowId: window,
            MeasurementKind: "quota", Scope: "model", ScopeId: model, Model: model);
}

sealed class AccountsSource(params AccountDto[] accounts) : IQuotaAccountSource
{
    public Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AccountDto>>(accounts);
}
sealed class CredentialsSource : IQuotaCredentialSource
{
    public int Calls;
    public bool Forbid;
    public Task<QuotaCredential?> GetCredentialAsync(string accountId, CancellationToken ct = default)
    { Calls++; if (Forbid) throw new InvalidOperationException("Forbidden credential access"); return Task.FromResult<QuotaCredential?>(new("synthetic-gateway")); }
}
sealed class Adapter : IQuotaAdapter
{
    public bool Supports(string provider) => provider == "review-provider";
    public string Source => "same-official-source";
    public bool RequiresCredential { get; init; } = true;
    public QuotaFetchResult Result { get; set; } = new([]);
    public Exception? Failure;
    public int Calls;
    public QuotaCredential? ReceivedCredential;
    public Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    { Calls++; ReceivedCredential = credential; if (Failure is not null) throw Failure; return Task.FromResult(Result); }
}
sealed class Clock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan value) => now += value;
}
