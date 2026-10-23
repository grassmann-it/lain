using Lain.Core;

namespace Lain.Host.Quota;

public sealed class QuotaService(IQuotaAccountSource accounts, IQuotaCredentialSource credentials,
    IEnumerable<IQuotaAdapter> adapters, TimeProvider? timeProvider = null)
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly IQuotaAdapter[] providers = adapters.ToArray();
    private readonly object gate = new();
    private readonly Dictionary<string, AccountSnapshot> snapshots = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim refreshLock = new(1, 1);

    public async Task<QuotaDto[]> GetSnapshotsAsync(CancellationToken ct = default)
    {
        var current = await accounts.GetAccountsAsync(ct);
        lock (gate) return current.SelectMany(Project).ToArray();
    }

    public async Task<QuotaDto[]> RefreshAsync(string? accountId = null, CancellationToken ct = default)
    {
        await refreshLock.WaitAsync(ct);
        try
        {
            var current = await accounts.GetAccountsAsync(ct);
            if (accountId is not null && !current.Any(a => a.Id == accountId)) throw new KeyNotFoundException("Quota account not found.");
            // Bound parallel calls while retaining per-account isolation.
            await Parallel.ForEachAsync(current.Where(a => a.Enabled && (accountId is null || a.Id == accountId)),
                new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, RefreshAccountAsync);
            lock (gate)
            {
                foreach (var removed in snapshots.Keys.Except(current.Select(a => a.Id)).ToArray()) snapshots.Remove(removed);
                return current.SelectMany(Project).ToArray();
            }
        }
        finally { refreshLock.Release(); }
    }

    // Host owns cancellation/lifetime. The first read is immediate, then every five minutes.
    public async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(RefreshInterval, clock);
        do
        {
            await RefreshAsync(ct: ct);
        } while (await timer.WaitForNextTickAsync(ct));
    }

    private async ValueTask RefreshAccountAsync(AccountDto account, CancellationToken ct)
    {
        AccountSnapshot previous;
        lock (gate)
        {
            previous = snapshots.GetValueOrDefault(account.Id) ?? new(null, null, [], null, false);
            snapshots[account.Id] = previous with { Refreshing = true };
        }
        try
        {
            var adapter = providers.FirstOrDefault(a => a.Supports(account.Provider));
            if (adapter is null) { SaveFailure(account.Id, previous, "Quota provider not supported."); return; }
            var credential = await credentials.GetCredentialAsync(account.Id, ct);
            if (credential is null || string.IsNullOrWhiteSpace(credential.BearerToken)) { SaveFailure(account.Id, previous, "Quota credential unavailable."); return; }
            if (credential.ExpiresAt <= clock.GetUtcNow()) { SaveFailure(account.Id, previous, "Access snapshot expired; reconnect account."); return; }
            var result = await adapter.FetchAsync(account, credential, ct);
            var now = clock.GetUtcNow();
            // An unknown placeholder is not evidence of a successful quota read.
            if (!result.Readings.Any(HasQuantity))
            { SaveFailure(account.Id, previous, result.Error ?? "Remaining budget unknown.", result.Readings); return; }
            var readings = result.Readings.Select(r => new CachedReading(r, now,
                HasQuantity(r) ? now : null, null)).ToList();
            for (var index = 0; index < readings.Count; index++)
            {
                if (HasQuantity(readings[index].Reading)) continue;
                var old = previous.Readings.FirstOrDefault(r => r.Reading.Source == readings[index].Reading.Source && r.LastSuccessAt.HasValue);
                if (old is not null) readings[index] = old with { ObservedAt = now, Error = "Quota window unavailable; previous observation retained." };
            }
            // A partial response must not silently drop the previous successful windows.
            if (result.Error is not null)
                readings.AddRange(previous.Readings.Where(old => !result.Readings.Any(r => r.Source == old.Reading.Source))
                    .Select(old => old with { ObservedAt = now, Error = result.Error }));
            lock (gate) snapshots[account.Id] = new(now, now, readings, result.Error, false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            lock (gate) snapshots[account.Id] = previous;
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Credential/parser/transport exception messages may expose raw secrets.
            SaveFailure(account.Id, previous, ex is QuotaFetchException ? ex.Message : "Quota refresh failed.");
        }
    }

    private void SaveFailure(string id, AccountSnapshot previous, string error, IReadOnlyList<QuotaReading>? unknown = null)
    {
        lock (gate) snapshots[id] = previous with { ObservedAt = clock.GetUtcNow(), Error = error, Refreshing = false,
            Readings = previous.Readings.Count > 0 ? previous.Readings.Select(r => r with { ObservedAt = clock.GetUtcNow(), Error = error }).ToArray()
                : (unknown ?? []).Select(r => new CachedReading(r, clock.GetUtcNow(), null, error)).ToArray() };
    }

    private IEnumerable<QuotaDto> Project(AccountDto account)
    {
        var snapshot = snapshots.GetValueOrDefault(account.Id);
        if (!providers.Any(adapter => adapter.Supports(account.Provider)))
        {
            var note = account.Provider.Equals("xiaomi-mimo", StringComparison.OrdinalIgnoreCase)
                ? "Direct MiMo quota is not available through a verified API-key endpoint. Check the provider console."
                : "Quota lookup is not available for this provider.";
            yield return new(account.Id, account.BudgetPoolId, "unavailable", account.Enabled ? "unsupported" : "disabled",
                snapshot?.ObservedAt, null, null, "unknown", null, null, null, account.SharedWithLegacy, note,
                WindowLabel: "Quota unavailable");
            yield break;
        }
        var state = !account.Enabled ? "disabled" : snapshot?.Refreshing == true ? "refreshing"
            : snapshot?.LastSuccessAt is null ? snapshot?.Error is null ? "unknown" : "error"
            : snapshot.Error is not null || clock.GetUtcNow() - snapshot.LastSuccessAt >= RefreshInterval ? "stale" : "ok";
        var rows = snapshot?.Readings.Count > 0 ? snapshot.Readings : [new CachedReading(new QuotaReading(providers.FirstOrDefault(a => a.Supports(account.Provider))?.Source ?? "unavailable", "unknown"), snapshot?.ObservedAt, null, snapshot?.Error)];
        foreach (var cached in rows)
        {
            var row = cached.Reading;
            var rowState = state;
            if (account.Enabled && snapshot?.Refreshing != true)
                rowState = cached.LastSuccessAt is null ? cached.Error is null ? "unknown" : "error"
                    : cached.Error is not null || clock.GetUtcNow() - cached.LastSuccessAt >= RefreshInterval ? "stale" : "ok";
            yield return new(account.Id, row.BudgetPoolId ?? (row.InheritAccountPool ? account.BudgetPoolId : null), row.Source, rowState,
                cached.ObservedAt, cached.LastSuccessAt, row.ResetsAt, row.Unit, row.Used, row.Limit, row.Remaining,
                account.SharedWithLegacy, cached.Error ?? snapshot?.Error, row.WindowLabel, row.UsedPercent);
        }
    }

    private static bool HasQuantity(QuotaReading row) => row.Used.HasValue || row.Limit.HasValue || row.Remaining.HasValue || (row.UsedPercent is >= 0 and <= 100);
    private sealed record CachedReading(QuotaReading Reading, DateTimeOffset? ObservedAt, DateTimeOffset? LastSuccessAt, string? Error);
    private sealed record AccountSnapshot(DateTimeOffset? ObservedAt, DateTimeOffset? LastSuccessAt,
        IReadOnlyList<CachedReading> Readings, string? Error, bool Refreshing);
}
