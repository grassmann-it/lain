using Lain.Core;
using Lain.Host.Accounts;
using System.Text.Json;

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
        await InvalidateChangedAsync(current, ct);
        lock (gate) return current.SelectMany(Project).ToArray();
    }

    // Same cached projection/invalidation as the public read; never refreshes a provider.
    public async Task<RouterAvailabilityQuotaSnapshot[]> GetRouterAvailabilitySnapshotsAsync(CancellationToken ct = default)
    {
        var current = await accounts.GetAccountsAsync(ct);
        await InvalidateChangedAsync(current, ct);
        lock (gate) return current.SelectMany(account => Project(account).Select(row =>
            new RouterAvailabilityQuotaSnapshot(row, snapshots.GetValueOrDefault(account.Id)?.RouterProof))).ToArray();
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
            current = await accounts.GetAccountsAsync(ct);
            await InvalidateChangedAsync(current, ct);
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
            previous = snapshots.GetValueOrDefault(account.Id) ?? new(null, null, [], null, false, AccountVersion: AccountVersion(account));
            snapshots[account.Id] = previous with { Refreshing = true };
        }
        string? bindingVersion = null;
        var validateCredentialVersion = false;
        var accountVersion = AccountVersion(account);
        try
        {
            var adapter = Reader(account);
            if (adapter is null) { SaveUnavailable(account.Id, previous, Unsupported(account.Provider)); return; }
            // Passive sources already have authenticated account identity at capture time.
            // Reading their snapshots must never load or forward provider credentials.
            QuotaCredential? credential;
            string? accessDiagnostic = null;
            try
            {
                credential = adapter.RequiresCredential ? await credentials.GetCredentialAsync(account.Id, ct) : new QuotaCredential("");
                validateCredentialVersion = adapter.RequiresCredential && credential is not null;
            }
            catch (Exception error) when (adapter.AllowsMissingCredential && error is not OutOfMemoryException
                && !(error is OperationCanceledException && ct.IsCancellationRequested))
            {
                credential = null;
                accessDiagnostic = QuotaFetchException.SafeDiagnostic(error);
            }
            bindingVersion = credential?.BindingVersion;
            var routerProof = (credentials as IRouterAvailabilityQuotaProofSource)?.GetRouterAvailabilityProof(account.Id, bindingVersion);
            if (previous.AccountVersion != accountVersion || previous.BindingVersion != bindingVersion)
                previous = new(null, null, [], null, true, BindingVersion: bindingVersion, AccountVersion: accountVersion);
            var unavailable = credential is null || adapter.RequiresCredential && string.IsNullOrWhiteSpace(credential.BearerToken) && string.IsNullOrWhiteSpace(credential.MonitorBearerToken)
                ? "Quota credential unavailable."
                : adapter.RequiresCredential && credential.Purposes is { } purposes && !purposes.Contains(adapter.CredentialPurpose, StringComparer.Ordinal)
                    ? "Credential does not grant the required read purpose."
                : credential.Monitor is { Kind: not "inference-key" } monitor && monitor.ExpiresAt <= clock.GetUtcNow()
                    ? "Monitor access expired; reconnect monitoring."
                : credential.ExpiresAt <= clock.GetUtcNow() && (credential.Monitor is null || credential.Monitor.Kind == "inference-key")
                    ? "Access snapshot expired; reconnect account." : null;
            if (unavailable is not null)
            {
                if (!adapter.AllowsMissingCredential) { SaveFailure(account.Id, previous, unavailable); return; }
                // A missing purpose is a denial of direct access. A composite may still read
                // its local passive source, with no token, monitor or resource carried over.
                if (credential?.ExpiresAt <= clock.GetUtcNow() || credential?.Monitor?.ExpiresAt <= clock.GetUtcNow())
                    accessDiagnostic ??= unavailable;
                credential = new QuotaCredential("", bindingVersion: bindingVersion);
            }
            var result = await adapter.FetchAsync(ReaderAccount(account), credential!, ct);
            if (accessDiagnostic is not null) result = result with { Error = result.Error ?? accessDiagnostic };
            if (!await IsCurrentAsync(account, bindingVersion, validateCredentialVersion, ct))
            { lock (gate) snapshots.Remove(account.Id); return; }
            var now = clock.GetUtcNow();
            var support = result.Support ?? new QuotaSupportDto(adapter.RequiresCredential ? "api" : "passive",
                adapter.RequiresCredential ? "Provider quota source." : "Previously observed provider response.");
            if (!result.Readings.Any(HasQuantity) && result.KiloPass is null && result.RequestyKey is null && result.Error is null && IsUnavailable(support))
            { SaveUnavailable(account.Id, previous, support); return; }
            // A successful explicit absence is still a completed metadata read.
            var metadataSucceeded = result.KiloPassOutcome?.Succeeded == true || result.RequestyKeyOutcome?.Succeeded == true;
            // An unknown placeholder is not evidence of a successful quota read.
            if (!result.Readings.Any(HasQuantity) && result.KiloPass is null && result.RequestyKey is null && !metadataSucceeded)
            { SaveFailure(account.Id, previous, result.Error ?? "Remaining budget unknown.", result.Readings, support, result); return; }
            var readings = result.Readings.Select(r => new CachedReading(r, r.ObservedAt ?? now,
                HasQuantity(r) ? r.ObservedAt ?? now : null, null)).ToList();
            for (var index = 0; index < readings.Count; index++)
            {
                if (HasQuantity(readings[index].Reading)) continue;
                var old = previous.Readings.FirstOrDefault(r => Identity(r.Reading) == Identity(readings[index].Reading) && r.LastSuccessAt.HasValue);
                if (old is not null) readings[index] = old with { Error = "Quota window unavailable; previous observation retained." };
            }
            // A partial response must not silently drop the previous successful windows.
            if (result.Error is not null)
                readings.AddRange(previous.Readings.Where(old => !result.Readings.Any(r => Identity(r) == Identity(old.Reading)))
                    .Select(old => old with { Error = result.Error }));
            lock (gate) snapshots[account.Id] = new(now, now, readings, result.Error, false, support, bindingVersion,
                accountVersion,
                MergeMetadata(previous.KiloPass, result.KiloPass, result.KiloPassOutcome, result.Error, now),
                MergeMetadata(previous.RequestyKey, result.RequestyKey, result.RequestyKeyOutcome, result.Error, now), routerProof);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            lock (gate) snapshots[account.Id] = previous with { Refreshing = false };
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Credential/parser/transport exception messages may expose raw secrets.
            SaveFailure(account.Id, previous, QuotaFetchException.SafeDiagnostic(ex));
        }
    }

    private void SaveUnavailable(string id, AccountSnapshot previous, QuotaSupportDto support)
    {
        lock (gate) snapshots[id] = previous with { Refreshing = false, Error = null, Support = support,
            Readings = previous.Readings.Select(r => r with { Error = null }).ToArray() };
    }

    private void SaveFailure(string id, AccountSnapshot previous, string error, IReadOnlyList<QuotaReading>? unknown = null, QuotaSupportDto? support = null, QuotaFetchResult? metadataResult = null)
    {
        lock (gate) snapshots[id] = previous with { ObservedAt = clock.GetUtcNow(), Error = error, Refreshing = false,
            Support = support ?? previous.Support,
            KiloPass = metadataResult is null ? FailMetadata(previous.KiloPass, error)
                : MergeMetadata(previous.KiloPass, metadataResult.KiloPass, metadataResult.KiloPassOutcome, error, clock.GetUtcNow()),
            RequestyKey = metadataResult is null ? FailMetadata(previous.RequestyKey, error)
                : MergeMetadata(previous.RequestyKey, metadataResult.RequestyKey, metadataResult.RequestyKeyOutcome, error, clock.GetUtcNow()),
            Readings = previous.Readings.Count > 0 ? previous.Readings.Select(r => r with { Error = error }).ToArray()
                : (unknown ?? []).Select(r => new CachedReading(r, r.ObservedAt ?? clock.GetUtcNow(), null, error)).ToArray() };
    }

    private IEnumerable<QuotaDto> Project(AccountDto account)
    {
        var snapshot = snapshots.GetValueOrDefault(account.Id);
        if (Reader(account) is null)
        {
            var support = Unsupported(account.Provider);
            yield return new(account.Id, account.BudgetPoolId, "unavailable", account.Enabled ? "unsupported" : "disabled",
                null, null, null, "unknown", null, null, null, account.SharedWithLegacy,
                WindowLabel: "Quota unavailable", Support: support);
            yield break;
        }
        var unavailable = IsUnavailable(snapshot?.Support);
        var rows = snapshot?.Readings.Count > 0 ? snapshot.Readings : [new CachedReading(new QuotaReading(Reader(account)?.Source ?? "unavailable", "unknown"), snapshot?.ObservedAt, snapshot?.KiloPass?.Value is not null || snapshot?.RequestyKey?.Value is not null ? snapshot?.LastSuccessAt : null, snapshot?.Error)];
        foreach (var cached in rows)
        {
            var row = cached.Reading;
            var error = cached.Error ?? snapshot?.Error;
            // This observation belongs to a completed UTC Free day. A cached read
            // cannot establish the new day's remaining allowance. Keep historical
            // usage/limit and timestamps, without manufacturing a reset balance.
            var elapsedFreeDay = row.WindowId == "free-model-daily-requests" && row.Scope == "account"
                && row.Source == "https://openrouter.ai/api/v1/key#free-model-daily-requests"
                && row.WindowEnd is { } end && clock.GetUtcNow() >= end;
            var rowState = !account.Enabled ? "disabled" : snapshot?.Refreshing == true ? "refreshing"
                : cached.LastSuccessAt is null ? error is not null ? "error" : unavailable ? snapshot!.Support!.Kind : "unknown"
                : cached.Error is not null || unavailable || elapsedFreeDay || clock.GetUtcNow() >= (row.FreshUntil ?? cached.LastSuccessAt.Value + RefreshInterval) ? "stale" : "ok";
            yield return new(account.Id, row.BudgetPoolId ?? (row.InheritAccountPool ? account.BudgetPoolId : null), row.Source, rowState,
                cached.ObservedAt, cached.LastSuccessAt, row.ResetsAt, row.Unit, row.Used, row.Limit, elapsedFreeDay ? null : row.Remaining,
                account.SharedWithLegacy, error, row.WindowLabel, elapsedFreeDay ? null : row.UsedPercent, snapshot?.Support,
                row.WindowId, row.MeasurementKind, row.Scope, row.ScopeId, row.Model, row.SourceUrl,
                row.DataAsOf, row.WindowStart, row.WindowEnd, row.FreshUntil, row.ProviderUpdatedAt, row.ApprovedLimit,
                row.ExactUsed, row.ExactLimit, elapsedFreeDay ? null : row.ExactRemaining, row.NextRegenerationAt, row.NextRegenerationPercent,
                row.ExactNextRegeneration, snapshot?.KiloPass?.Value, snapshot?.RequestyKey?.Value,
                ProjectMetadata(snapshot?.KiloPass, account, snapshot), ProjectMetadata(snapshot?.RequestyKey, account, snapshot));
        }
    }

    private static CachedMetadata<T>? MergeMetadata<T>(CachedMetadata<T>? previous, T? value,
        QuotaPartOutcome? outcome, string? aggregateError, DateTimeOffset now) where T : class
    {
        // Legacy adapters with a value imply successful metadata observation even
        // when an independent numeric request failed. Explicit outcomes also cover absence.
        if (outcome?.Succeeded == true || outcome is null && value is not null)
            return new(value, now, now, null);
        if (outcome is null && previous is null) return null;
        var error = outcome?.Error ?? aggregateError ?? "Metadata unavailable; previous observation retained.";
        return previous is null ? new(null, null, null, error) : previous with { Error = error };
    }

    private static CachedMetadata<T>? FailMetadata<T>(CachedMetadata<T>? previous, string error) where T : class =>
        previous is null ? null : previous with { Error = error };

    private QuotaMetadataObservationDto? ProjectMetadata<T>(CachedMetadata<T>? metadata, AccountDto account,
        AccountSnapshot? snapshot) where T : class
    {
        if (metadata is null) return null;
        var freshUntil = metadata.LastSuccessAt + RefreshInterval;
        var state = !account.Enabled ? "disabled" : snapshot?.Refreshing == true ? "refreshing"
            : metadata.LastSuccessAt is null ? metadata.Error is not null ? "error" : "unknown"
            : metadata.Error is not null || IsUnavailable(snapshot?.Support) || clock.GetUtcNow() >= freshUntil ? "stale" : "ok";
        return new(state, metadata.ObservedAt, metadata.LastSuccessAt, freshUntil, metadata.Error);
    }

    private static bool IsUnavailable(QuotaSupportDto? support) => support?.Kind is
        "unsupported" or "unimplemented" or "console" or "setup-required" or "awaiting-observation" or "unavailable";
    private static (string Source, string? Window, string? Scope, string? ScopeId, string? Model) Identity(QuotaReading row) =>
        (row.Source, row.WindowId, row.Scope, row.ScopeId, row.Model);
    private static QuotaSupportDto Unsupported(string provider) => AccountService.CanonicalProviderId(provider) switch
    {
        "cloudflare-workers-ai" or "cloudflare" => new("unimplemented",
            "Workers AI usage reporting is not yet implemented; a suitable dataset and reader still need qualification.",
            "https://dash.cloudflare.com/"),
        "nvidia-build" or "nvidia" => NvidiaBuildPublicReference.Support(),
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
    private static bool HasQuantity(QuotaReading row) => row.Used.HasValue || row.Limit.HasValue || row.Remaining.HasValue ||
        row.ExactUsed is not null || row.ExactLimit is not null || row.ExactRemaining is not null
        || row.UsedPercent is { } percent && double.IsFinite(percent) && percent >= 0;

    private IQuotaAdapter? Reader(AccountDto account) => providers.FirstOrDefault(a => a.Supports(AccountService.CanonicalProviderId(account.Provider)));
    private static AccountDto ReaderAccount(AccountDto account) => account with
    {
        Provider = AccountService.CanonicalProviderId(account.Provider),
        Binding = account.Binding is { } binding ? binding with { Provider = AccountService.CanonicalProviderId(binding.Provider) } : null
    };

    private static string AccountVersion(AccountDto account) => JsonSerializer.Serialize(new { account.Id, account.Provider, account.Enabled,
        account.Generation, account.Binding, account.Credentials, account.Capabilities });

    private async Task<bool> IsCurrentAsync(AccountDto account, string? bindingVersion, bool requiresCredential, CancellationToken ct)
    {
        var current = (await accounts.GetAccountsAsync(ct)).SingleOrDefault(a => a.Id == account.Id);
        if (current is null || AccountVersion(current) != AccountVersion(account)) return false;
        return !requiresCredential || await credentials.GetBindingVersionAsync(account.Id, ct) == bindingVersion;
    }

    private async Task InvalidateChangedAsync(IReadOnlyList<AccountDto> current, CancellationToken ct)
    {
        foreach (var account in current)
        {
            AccountSnapshot? snapshot;
            lock (gate) snapshot = snapshots.GetValueOrDefault(account.Id);
            if (snapshot is null) continue;
            var adapter = Reader(account);
            string? version = null;
            try { if (adapter?.RequiresCredential == true) version = await credentials.GetBindingVersionAsync(account.Id, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch when (adapter?.AllowsMissingCredential == true) { version = null; }
            catch { lock (gate) snapshots.Remove(account.Id); continue; }
            if (snapshot.AccountVersion != AccountVersion(account) || snapshot.BindingVersion != version)
                lock (gate) snapshots.Remove(account.Id);
        }
        lock (gate)
            foreach (var removed in snapshots.Keys.Except(current.Select(a => a.Id)).ToArray()) snapshots.Remove(removed);
    }
    private sealed record CachedReading(QuotaReading Reading, DateTimeOffset? ObservedAt, DateTimeOffset? LastSuccessAt, string? Error);
    private sealed record CachedMetadata<T>(T? Value, DateTimeOffset? ObservedAt, DateTimeOffset? LastSuccessAt, string? Error) where T : class;
    private sealed record AccountSnapshot(DateTimeOffset? ObservedAt, DateTimeOffset? LastSuccessAt,
        IReadOnlyList<CachedReading> Readings, string? Error, bool Refreshing, QuotaSupportDto? Support = null,
        string? BindingVersion = null, string? AccountVersion = null, CachedMetadata<KiloPassState>? KiloPass = null, CachedMetadata<RequestyKeyMetadata>? RequestyKey = null,
        RouterAvailabilityQuotaProof? RouterProof = null);
}
