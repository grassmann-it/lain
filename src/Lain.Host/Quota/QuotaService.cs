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
            if (adapter is null) { SaveUnavailable(account.Id, previous, Unsupported(account.Provider)); return; }
            // Passive sources already have authenticated account identity at capture time.
            // Reading their snapshots must never load or forward provider credentials.
            var credential = adapter.RequiresCredential ? await credentials.GetCredentialAsync(account.Id, ct) : new QuotaCredential("");
            if (credential is null || (adapter.RequiresCredential && string.IsNullOrWhiteSpace(credential.BearerToken)))
            { SaveFailure(account.Id, previous, "Quota credential unavailable."); return; }
            if (credential.ExpiresAt <= clock.GetUtcNow() && (credential.Monitor is null || credential.Monitor.Kind == "inference-key"))
            { SaveFailure(account.Id, previous, "Access snapshot expired; reconnect account."); return; }
            var result = await adapter.FetchAsync(account, credential, ct);
            var now = clock.GetUtcNow();
            var support = result.Support ?? new QuotaSupportDto(adapter.RequiresCredential ? "api" : "passive",
                adapter.RequiresCredential ? "Provider quota source." : "Previously observed provider response.");
            if (!result.Readings.Any(HasQuantity) && result.Error is null && IsUnavailable(support))
            { SaveUnavailable(account.Id, previous, support); return; }
            // An unknown placeholder is not evidence of a successful quota read.
            if (!result.Readings.Any(HasQuantity))
            { SaveFailure(account.Id, previous, result.Error ?? "Remaining budget unknown.", result.Readings, support); return; }
            var readings = result.Readings.Select(r => new CachedReading(r, r.ObservedAt ?? now,
                HasQuantity(r) ? r.ObservedAt ?? now : null, null)).ToList();
            for (var index = 0; index < readings.Count; index++)
            {
                if (HasQuantity(readings[index].Reading)) continue;
                var old = previous.Readings.FirstOrDefault(r => Identity(r.Reading) == Identity(readings[index].Reading) && r.LastSuccessAt.HasValue);
                if (old is not null) readings[index] = old with { ObservedAt = old.Reading.ObservedAt ?? now, Error = "Quota window unavailable; previous observation retained." };
            }
            // A partial response must not silently drop the previous successful windows.
            if (result.Error is not null)
                readings.AddRange(previous.Readings.Where(old => !result.Readings.Any(r => Identity(r) == Identity(old.Reading)))
                    .Select(old => old with { ObservedAt = old.Reading.ObservedAt ?? now, Error = result.Error }));
            lock (gate) snapshots[account.Id] = new(now, now, readings, result.Error, false, support);
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

    private void SaveUnavailable(string id, AccountSnapshot previous, QuotaSupportDto support)
    {
        lock (gate) snapshots[id] = previous with { Refreshing = false, Error = null, Support = support,
            Readings = previous.Readings.Select(r => r with { Error = null }).ToArray() };
    }

    private void SaveFailure(string id, AccountSnapshot previous, string error, IReadOnlyList<QuotaReading>? unknown = null, QuotaSupportDto? support = null)
    {
        lock (gate) snapshots[id] = previous with { ObservedAt = clock.GetUtcNow(), Error = error, Refreshing = false,
            Support = support ?? previous.Support,
            Readings = previous.Readings.Count > 0 ? previous.Readings.Select(r => r with { ObservedAt = r.Reading.ObservedAt ?? clock.GetUtcNow(), Error = error }).ToArray()
                : (unknown ?? []).Select(r => new CachedReading(r, r.ObservedAt ?? clock.GetUtcNow(), null, error)).ToArray() };
    }

    private IEnumerable<QuotaDto> Project(AccountDto account)
    {
        var snapshot = snapshots.GetValueOrDefault(account.Id);
        if (!providers.Any(adapter => adapter.Supports(account.Provider)))
        {
            var support = Unsupported(account.Provider);
            yield return new(account.Id, account.BudgetPoolId, "unavailable", account.Enabled ? "unsupported" : "disabled",
                null, null, null, "unknown", null, null, null, account.SharedWithLegacy,
                WindowLabel: "Quota unavailable", Support: support);
            yield break;
        }
        var unavailable = IsUnavailable(snapshot?.Support);
        var rows = snapshot?.Readings.Count > 0 ? snapshot.Readings : [new CachedReading(new QuotaReading(providers.FirstOrDefault(a => a.Supports(account.Provider))?.Source ?? "unavailable", "unknown"), snapshot?.ObservedAt, null, snapshot?.Error)];
        foreach (var cached in rows)
        {
            var row = cached.Reading;
            var error = cached.Error ?? snapshot?.Error;
            var rowState = !account.Enabled ? "disabled" : snapshot?.Refreshing == true ? "refreshing"
                : cached.LastSuccessAt is null ? error is not null ? "error" : unavailable ? snapshot!.Support!.Kind : "unknown"
                : cached.Error is not null || unavailable || clock.GetUtcNow() >= (row.FreshUntil ?? cached.LastSuccessAt.Value + RefreshInterval) ? "stale" : "ok";
            yield return new(account.Id, row.BudgetPoolId ?? (row.InheritAccountPool ? account.BudgetPoolId : null), row.Source, rowState,
                cached.ObservedAt, cached.LastSuccessAt, row.ResetsAt, row.Unit, row.Used, row.Limit, row.Remaining,
                account.SharedWithLegacy, error, row.WindowLabel, row.UsedPercent, snapshot?.Support,
                row.WindowId, row.MeasurementKind, row.Scope, row.ScopeId, row.Model, row.SourceUrl,
                row.DataAsOf, row.WindowStart, row.WindowEnd, row.FreshUntil, row.ProviderUpdatedAt, row.ApprovedLimit);
        }
    }

    private static bool IsUnavailable(QuotaSupportDto? support) => support?.Kind is
        "unsupported" or "unimplemented" or "console" or "setup-required" or "awaiting-observation" or "unavailable";
    private static (string Source, string? Window, string? Scope, string? ScopeId, string? Model) Identity(QuotaReading row) =>
        (row.Source, row.WindowId, row.Scope, row.ScopeId, row.Model);
    private static QuotaSupportDto Unsupported(string provider) => provider.ToLowerInvariant() switch
    {
        "cloudflare-workers-ai" or "cloudflare" => new("unimplemented",
            "Workers AI usage reporting is not yet implemented; a suitable dataset and reader still need qualification.",
            "https://dash.cloudflare.com/"),
        "nvidia-build" or "nvidia" => new("console", "No suitable NVIDIA Build quota API is qualified. Check the provider console.",
            "https://build.nvidia.com/"),
        "xiaomi-mimo" => new("console", "No verified direct MiMo quota API is available. Check the provider console.",
            "https://platform.xiaomimimo.com/"),
        "sambanova" => new("console", "Quota lookup is not implemented for SambaNova. Check your account in the provider console.",
            "https://cloud.sambanova.ai/"),
        "cohere" => new("console", "Quota lookup is not implemented for Cohere. Check your account in the provider console.",
            "https://dashboard.cohere.com/"),
        "upstage" => new("console", "Quota lookup is not implemented for Upstage. Check your account in the provider console.",
            "https://console.upstage.ai/"),
        "kilo" => new("console", "Quota lookup is not implemented for Kilo. Check your account in the provider console.",
            "https://app.kilo.ai/"),
        _ => new("unsupported", "Quota lookup is not available for this provider.")
    };
    private static bool HasQuantity(QuotaReading row) => row.Used.HasValue || row.Limit.HasValue || row.Remaining.HasValue || (row.UsedPercent is >= 0 and <= 100);
    private sealed record CachedReading(QuotaReading Reading, DateTimeOffset? ObservedAt, DateTimeOffset? LastSuccessAt, string? Error);
    private sealed record AccountSnapshot(DateTimeOffset? ObservedAt, DateTimeOffset? LastSuccessAt,
        IReadOnlyList<CachedReading> Readings, string? Error, bool Refreshing, QuotaSupportDto? Support = null);
}
