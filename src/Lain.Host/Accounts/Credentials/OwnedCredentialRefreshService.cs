using Lain.Core;
using Lain.Host.Operations;

namespace Lain.Host.Accounts.Credentials;

/// <summary>Safe local renewal status; provider responses and authentication material never enter it.</summary>
public sealed record OwnedCredentialRefreshObservation(string AccountId, string CredentialId, long Generation,
    string State, DateTimeOffset ObservedAt, string? Error = null);

/// <summary>Renews only explicitly registered owners of currently selected quota access.</summary>
public sealed class OwnedCredentialRefreshService(ConfigurationRepository configuration, CredentialOwnerService owners,
    CredentialGenerationStore generations, IEnumerable<ICredentialOwner> registeredOwners, TimeProvider? timeProvider = null)
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan RenewBeforeExpiry = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan MaximumRefreshDuration = TimeSpan.FromSeconds(30);
    public const int MaximumConcurrentRefreshes = 2;
    public const int MaximumRefreshesPerPass = 8;
    public const int MaximumObservations = 64;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly HashSet<string> qualifiedOwners = registeredOwners.Select(owner => owner.Owner)
        .Where(IsRotatingOwner).ToHashSet(StringComparer.Ordinal);
    private readonly SemaphoreSlim passGate = new(1, 1);
    private readonly object gate = new();
    private readonly HashSet<GenerationKey> attempted = [];
    private readonly Dictionary<GenerationKey, OwnedCredentialRefreshObservation> observations = [];
    private readonly record struct GenerationKey(string AccountId, string CredentialId, long Generation);

    public OwnedCredentialRefreshObservation[] GetStatus()
    {
        lock (gate) return observations.Values.OrderByDescending(value => value.ObservedAt).ToArray();
    }

    public async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        do { await RefreshDueAsync(ct); }
        while (await timer.WaitForNextTickAsync(ct));
    }

    public async Task<OwnedCredentialRefreshObservation[]> RefreshDueAsync(CancellationToken ct = default)
    {
        await passGate.WaitAsync(ct);
        try
        {
            var candidates = new List<(AccountDto Account, CredentialHandleDto Handle)>();
            var current = new HashSet<GenerationKey>();
            var threshold = clock.GetUtcNow() + RenewBeforeExpiry;
            foreach (var account in configuration.Snapshot.Accounts)
            {
                var capability = AccountService.EffectiveCapabilities(account).LastOrDefault(value => value.Purpose == "quota");
                if (capability?.CredentialId is not { } id) continue;
                var handles = account.Credentials?.Where(value => value.Id == id && value.Purposes.Contains("quota", StringComparer.Ordinal)).ToArray() ?? [];
                if (handles.Length != 1) continue;
                var handle = handles[0];
                var key = new GenerationKey(account.Id, handle.Id, handle.Generation);
                current.Add(key);
                if (!account.Enabled || !IsRotatingOwner(handle.Owner) || !qualifiedOwners.Contains(handle.Owner) ||
                    handle.ExpiresAt is null || handle.ExpiresAt > threshold) continue;
                lock (gate) if (attempted.Contains(key)) continue;
                candidates.Add((account, handle));
            }
            lock (gate)
            {
                // Retain failed generation suppression only while that selected handle exists.
                attempted.RemoveWhere(key => !current.Contains(key));
            }
            await Parallel.ForEachAsync(candidates.OrderBy(candidate => candidate.Handle.ExpiresAt).Take(MaximumRefreshesPerPass),
                new ParallelOptions { MaxDegreeOfParallelism = MaximumConcurrentRefreshes, CancellationToken = ct },
                async (candidate, token) =>
                {
                    var key = new GenerationKey(candidate.Account.Id, candidate.Handle.Id, candidate.Handle.Generation);
                    lock (gate) if (!attempted.Add(key)) return;
                    try
                    {
                        if (generations.IsRetired(candidate.Handle))
                        {
                            Record(key, "recovery-required", "This credential generation was already consumed. Review its saved recovery result.");
                            return;
                        }
                        // Validate restore epoch and immutable account/purpose binding before any provider action.
                        generations.Read(candidate.Account.Id, candidate.Handle);
                        using var timeout = new CancellationTokenSource(MaximumRefreshDuration, clock);
                        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
                        await owners.RefreshAsync(candidate.Account.Id, candidate.Handle.Id, lifetime.Token);
                        Record(key, "renewed");
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        Record(key, "interrupted", "Credential renewal was interrupted. A consumed generation requires recovery before another attempt.");
                        throw;
                    }
                    catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
                    {
                        // Never classify by, log or persist the owner's raw exception message.
                        Record(key, "recovery-required", "Credential renewal could not be completed. Review recovery or reconnect this access.");
                    }
                });
            return GetStatus();
        }
        finally { passGate.Release(); }
    }

    private static bool IsRotatingOwner(string owner) => owner is not ("static-key" or "external-snapshot" or "core");
    private void Record(GenerationKey key, string state, string? error = null)
    {
        lock (gate)
        {
            observations[key] = new(key.AccountId, key.CredentialId, key.Generation, state, clock.GetUtcNow(), error);
            foreach (var stale in observations.OrderByDescending(pair => pair.Value.ObservedAt).Skip(MaximumObservations).Select(pair => pair.Key).ToArray())
                observations.Remove(stale);
        }
    }
}
