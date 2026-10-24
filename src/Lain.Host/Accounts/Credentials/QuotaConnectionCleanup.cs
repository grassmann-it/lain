using System.Collections.Concurrent;
using Lain.Core;
using Lain.Host.Operations;

namespace Lain.Host.Accounts.Credentials;

/// <summary>Closes detached owned access only after a new public selection has been persisted.</summary>
public sealed class QuotaConnectionCleanup(ConfigurationRepository configuration,
    IEnumerable<Func<AccountDto, CancellationToken, Task>> callbacks)
{
    private readonly Func<AccountDto, CancellationToken, Task>[] cleanup = callbacks.ToArray();
    private sealed record Retry(AccountDto Account, long LastAttempt = 0);
    private readonly ConcurrentDictionary<(string Account, string Credential, long Generation), Retry> pending = new();
    private long attemptOrder;
    private readonly SemaphoreSlim draining = new(1, 1);
    public static readonly TimeSpan CleanupBudget = TimeSpan.FromSeconds(5);
    public int PendingCount => pending.Count;

    // Call after an account request, including login status/resource selection and recovery.
    // A rejected CAS leaves the same selected generation and never schedules its cleanup.
    public async Task AfterPublicationAsync(ConfigurationDto before, CancellationToken? budget = null)
    {
        var current = configuration.Snapshot;
        foreach (var account in before.Accounts)
        {
            var previous = SelectedQuota(account);
            if (previous is null || Same(previous, SelectedQuota(current.Accounts.SingleOrDefault(a => a.Id == account.Id)))) continue;
            pending.TryAdd((account.Id, previous.Id, previous.Generation), new(account));
        }
        if (pending.IsEmpty || !await draining.WaitAsync(0)) return;
        try
        {
            // Cleanup is bounded and independent of a disconnected HTTP caller. Failures
            // remain queued for another account request; they cannot undo publication.
            using var timeout = budget is null ? new CancellationTokenSource(CleanupBudget) : null;
            var ct = budget ?? timeout!.Token;
            foreach (var (key, retry) in pending.OrderBy(item => item.Value.LastAttempt)
                .ThenBy(item => item.Key.Account, StringComparer.Ordinal).Take(8).ToArray())
            {
                if (ct.IsCancellationRequested) break;
                pending.TryUpdate(key, retry with { LastAttempt = Interlocked.Increment(ref attemptOrder) }, retry);
                var account = retry.Account;
                if (Same(SelectedQuota(account), SelectedQuota(configuration.Snapshot.Accounts.SingleOrDefault(a => a.Id == account.Id))))
                { pending.TryRemove(key, out _); continue; }
                try
                {
                    foreach (var callback in cleanup)
                    {
                        ct.ThrowIfCancellationRequested();
                        await callback(account, ct);
                    }
                    pending.TryRemove(key, out _);
                }
                catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
                { /* Keep only the local retry reference; no provider/credential exception text escapes. */ }
            }
        }
        finally { draining.Release(); }
    }

    public static CredentialHandleDto? SelectedQuota(AccountDto? account)
    {
        var selected = account?.Capabilities?.SingleOrDefault(c => c.Purpose == "quota")?.CredentialId;
        return selected is null ? null : account?.Credentials?.SingleOrDefault(h => h.Id == selected && h.Purposes.Contains("quota", StringComparer.Ordinal));
    }
    private static bool Same(CredentialHandleDto? left, CredentialHandleDto? right) => left is not null && right is not null
        && left.Id == right.Id && left.Generation == right.Generation && left.Owner == right.Owner;
}
