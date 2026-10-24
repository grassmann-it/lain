using System.Collections.Concurrent;
using Lain.Core;
using Lain.Host.Operations;

namespace Lain.Host.Accounts.Credentials;

/// <summary>Closes detached owned access only after a new public selection has been persisted.</summary>
public sealed class QuotaConnectionCleanup(ConfigurationRepository configuration,
    IEnumerable<Func<AccountDto, CancellationToken, Task>> callbacks)
{
    private readonly Func<AccountDto, CancellationToken, Task>[] cleanup = callbacks.ToArray();
    private readonly ConcurrentDictionary<(string Account, string Credential, long Generation), AccountDto> pending = new();
    private readonly SemaphoreSlim draining = new(1, 1);
    public int PendingCount => pending.Count;

    // Call after an account request, including login status/resource selection and recovery.
    // A rejected CAS leaves the same selected generation and never schedules its cleanup.
    public async Task AfterPublicationAsync(ConfigurationDto before)
    {
        var current = configuration.Snapshot;
        foreach (var account in before.Accounts)
        {
            var previous = SelectedQuota(account);
            if (previous is null || Same(previous, SelectedQuota(current.Accounts.SingleOrDefault(a => a.Id == account.Id)))) continue;
            pending[(account.Id, previous.Id, previous.Generation)] = account;
        }
        if (pending.IsEmpty || !await draining.WaitAsync(0)) return;
        try
        {
            // Cleanup is bounded and independent of a disconnected HTTP caller. Failures
            // remain queued for another account request; they cannot undo publication.
            foreach (var (key, account) in pending.Take(8).ToArray())
            {
                if (Same(SelectedQuota(account), SelectedQuota(configuration.Snapshot.Accounts.SingleOrDefault(a => a.Id == account.Id))))
                { pending.TryRemove(key, out _); continue; }
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    foreach (var callback in cleanup) await callback(account, timeout.Token);
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
