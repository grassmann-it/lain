using System.Net;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Quota;

static class MetadataRetention
{
    public static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
        var clock = new MetadataClock();
        var accounts = new MetadataAccounts(new("one", "Fixture", "kilo", true, "api-key", "connected"));
        var credentials = new MetadataCredentials();
        var phase = 0;
        using var kiloClient = new HttpClient(new MetadataHttp(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("balance"))
                return phase >= 5 ? Denied() : Reply("{\"balance\":" + (10 + phase) + "}");
            return phase switch
            {
                1 or 6 => Denied(),
                2 => Reply("{}"), // A valid JSON document is not evidence of no subscription.
                3 => Reply("{\"subscription\":{\"status\":\"canceled\"}}"),
                4 => Reply("[{\"result\":{\"data\":{\"json\":{\"subscription\":null}}}}]"),
                _ => Reply("{\"subscription\":{\"status\":\"active\",\"currentPeriodUsageUsd\":3}}")
            };
        }));
        var service = new QuotaService(accounts, credentials, [new KiloQuotaAdapter(kiloClient)], clock);
        var original = (await service.RefreshAsync()).Single();
        var t0 = clock.GetUtcNow();
        Check(original.KiloPass is { Status: "active" } && original.KiloPassObservation is { State: "ok", Error: null }
            && original.KiloPassObservation.LastSuccessAt == t0, "Kilo successful metadata keeps existing wallet projection with independent freshness");
        Check(original.KiloPassObservation!.FreshUntil == t0 + QuotaService.RefreshInterval, "Pass freshness bound belongs to its own successful read");
        clock.Advance(); phase = 1;
        var partial = (await service.RefreshAsync()).Single();
        Check(partial.State == "ok" && partial.Remaining == 11 && partial.LastSuccessAt == clock.GetUtcNow(), "Fresh Kilo wallet survives Pass failure");
        Check(partial.KiloPass == original.KiloPass && partial.KiloPassObservation is { State: "stale", Error: not null }
            && partial.KiloPassObservation.ObservedAt == t0 && partial.KiloPassObservation.LastSuccessAt == t0
            && partial.KiloPassObservation.FreshUntil == original.KiloPassObservation!.FreshUntil, "Failed Pass retains value and original observation/success/freshness");
        clock.Advance(); phase = 2;
        var repeated = (await service.RefreshAsync()).Single();
        Check(repeated.Remaining == 12 && repeated.KiloPass == original.KiloPass
            && repeated.KiloPassObservation is { State: "stale" } && repeated.KiloPassObservation.LastSuccessAt == t0,
            "Unknown Pass schema retains prior metadata without renewing success");
        clock.Advance(); phase = 3;
        var recovered = (await service.RefreshAsync()).Single();
        Check(recovered.KiloPass is { Status: "canceled", IsConsumable: false } && recovered.KiloPassObservation is { State: "ok", Error: null }
            && recovered.KiloPassObservation.LastSuccessAt == clock.GetUtcNow(), "Successful canceled Pass updates status and recovers freshness");
        clock.Advance(); phase = 4;
        var absent = (await service.RefreshAsync()).Single();
        Check(absent.KiloPass is null && absent.KiloPassObservation is { State: "ok", Error: null }
            && absent.KiloPassObservation.LastSuccessAt == clock.GetUtcNow(), "Explicit successful no-subscription clears old Pass");
        clock.Advance(); phase = 5;
        var passOnly = (await service.RefreshAsync()).Single();
        var passOnlyAt = clock.GetUtcNow();
        Check(passOnly.State == "stale" && passOnly.Remaining == 14 && passOnly.KiloPass is { Status: "active" }
            && passOnly.KiloPassObservation is { State: "ok", Error: null } && passOnly.KiloPassObservation.LastSuccessAt == passOnlyAt,
            "Wallet failure leaves fresh successful Pass independently fresh");
        clock.Advance(); phase = 6;
        var failed = (await service.RefreshAsync()).Single();
        Check(failed.KiloPass == passOnly.KiloPass && failed.KiloPassObservation is { State: "stale", Error: not null }
            && failed.KiloPassObservation.ObservedAt == passOnlyAt && failed.KiloPassObservation.LastSuccessAt == passOnlyAt,
            "Whole fetch failure preserves original metadata observation");
        Check(!JsonSerializer.Serialize(failed).Contains("fixture-private-key"), "Retained metadata diagnostics do not expose provider error bodies");
        accounts.Current = accounts.Current with { Generation = 1 };
        var reset = (await service.GetSnapshotsAsync()).Single();
        Check(reset.KiloPass is null && reset.KiloPassObservation is null && reset.LastSuccessAt is null,
            "Account generation invalidates metadata and numeric cache together");
        phase = 0;
        await service.RefreshAsync();
        credentials.Version = "binding-2";
        reset = (await service.GetSnapshotsAsync()).Single();
        Check(reset.KiloPass is null && reset.KiloPassObservation is null, "Credential binding invalidation cannot retain old Pass");

        // Distinct account IDs remain separate even with the same provider and generation.
        var twoAccounts = new MetadataAccountList([
            accounts.Current with { Id = "one" }, accounts.Current with { Id = "two" }]);
        using var partitionClient = new HttpClient(new MetadataHttp(request => request.RequestUri!.AbsolutePath.EndsWith("balance")
            ? Reply("{\"balance\":1}") : request.Headers.Authorization!.Parameter == "one"
                ? Reply("{\"subscription\":{\"status\":\"active\"}}") : Denied()));
        var partitioned = await new QuotaService(twoAccounts, credentials, [new KiloQuotaAdapter(partitionClient)], clock).RefreshAsync();
        Check(partitioned.Single(r => r.AccountId == "one").KiloPass is { Status: "active" }
            && partitioned.Single(r => r.AccountId == "two").KiloPass is null, "Same-provider accounts do not share metadata cache");

        accounts.Current = accounts.Current with { Provider = "requesty", Generation = 0 };
        phase = 0;
        using var requestyClient = new HttpClient(new MetadataHttp(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/usage")) return phase == 2 ? Denied()
                : Reply("{\"usage\":{\"a\":{\"spend\":1,\"total_requests\":2,\"total_tokens\":3}}}");
            return phase is 1 or 3 ? Denied()
                : Reply("{\"id\":\"key-one\",\"monthly_limit\":20,\"monthly_spend\":4,\"permissions\":{\"manage\":\"none\",\"completions\":\"write\"}}");
        }));
        var requestyService = new QuotaService(accounts, credentials, [new RequestyQuotaAdapter(requestyClient)], clock);
        var initial = await requestyService.RefreshAsync();
        var requestyAt = clock.GetUtcNow();
        Check(initial.Length == 4 && initial.All(r => r.RequestyKey is { KeyId: "key-one" }
            && r.RequestyKeyObservation is { State: "ok", Error: null } && r.RequestyKeyObservation.LastSuccessAt == requestyAt),
            "Requesty Self metadata retains existing numeric display without extra quota rows");
        clock.Advance(); phase = 1;
        var requestyPartial = await requestyService.RefreshAsync();
        Check(requestyPartial.Any(r => r.ScopeId is null) && requestyPartial.Where(r => r.ScopeId is null).All(r => r.State == "ok" && r.LastSuccessAt == clock.GetUtcNow()),
            "Requesty independent unbound usage remains fresh after Self failure");
        Check(requestyPartial.All(r => r.RequestyKey is { KeyId: "key-one" } && r.RequestyKeyObservation is { State: "stale", Error: not null }
            && r.RequestyKeyObservation.ObservedAt == requestyAt && r.RequestyKeyObservation.LastSuccessAt == requestyAt
            && r.RequestyKeyObservation.FreshUntil == requestyAt + QuotaService.RefreshInterval),
            "Requesty Self failure retains metadata with original timestamps alongside fresh usage");
        clock.Advance(); phase = 3;
        var repeatRequesty = await requestyService.RefreshAsync();
        Check(repeatRequesty.All(r => r.RequestyKeyObservation is { State: "stale", LastSuccessAt: var stamp }
            && stamp == requestyAt), "Repeated Requesty Self failure cannot renew metadata success");
        clock.Advance(); phase = 2;
        var usageFailed = await requestyService.RefreshAsync();
        Check(usageFailed.Any(r => r.MeasurementKind == "spend-cap" && r.State == "ok")
            && usageFailed.Any(r => r.MeasurementKind == "usage" && r.State == "stale"), "Requesty usage failure retains numeric windows independently");
        Check(usageFailed.All(r => r.RequestyKeyObservation is { State: "ok", Error: null }
            && r.RequestyKeyObservation.LastSuccessAt == clock.GetUtcNow()), "Requesty successful Self metadata remains fresh despite usage failure");
        clock.Advance(TimeSpan.FromMinutes(6));
        Check((await requestyService.GetSnapshotsAsync()).All(r => r.RequestyKeyObservation is { State: "stale", Error: null }),
            "Metadata expires by its own freshness bound even without another fetch");
        accounts.Current = accounts.Current with { Generation = 2 };
        Check((await requestyService.GetSnapshotsAsync()).All(r => r.RequestyKey is null && r.RequestyKeyObservation is null),
            "Requesty generation change discards retained metadata");
        return checks;
    }

    private static HttpResponseMessage Reply(string value) => new(HttpStatusCode.OK)
        { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Denied() => new(HttpStatusCode.Forbidden)
        { Content = new StringContent("fixture-private-key") };
}
sealed class MetadataClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.Parse("2026-10-24T10:00:00Z");
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan? step = null) => now += step ?? TimeSpan.FromMinutes(1);
}
sealed class MetadataAccounts(AccountDto initial) : IQuotaAccountSource
{
    public AccountDto Current = initial;
    public Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AccountDto>>([Current]);
}
sealed class MetadataAccountList(AccountDto[] accounts) : IQuotaAccountSource
{
    public Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AccountDto>>(accounts);
}
sealed class MetadataCredentials : IQuotaCredentialSource
{
    public string Version = "binding-1";
    public Task<QuotaCredential?> GetCredentialAsync(string accountId, CancellationToken ct = default) => Task.FromResult<QuotaCredential?>(new(accountId, bindingVersion: Version));
    public Task<string?> GetBindingVersionAsync(string accountId, CancellationToken ct = default) => Task.FromResult<string?>(Version);
}
sealed class MetadataHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request));
}
