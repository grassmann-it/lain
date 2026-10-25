using System.Text.Json;
using Lain.Core;
using Lain.Host.Operations;

namespace Lain.Host.Accounts.Credentials;

/// <summary>A private removal fence, prepared before configuration publication and recoverable after a crash.</summary>
internal static class CredentialTargetRemoval
{
    private sealed record Pending(string Id, long Revision, long AccountGeneration);
    private sealed record Journal(string AccountId, string? Epoch = null, Pending? Pending = null);

    public static string Prepare(CredentialGenerationStore store, AccountDto account, long revision)
    {
        lock (store)
        {
            var journal = Read(store, account.Id);
            var id = Guid.NewGuid().ToString("N");
            Write(store, journal with { Pending = new(id, revision, account.Generation) });
            return id;
        }
    }

    public static void Complete(CredentialGenerationStore store, string accountId, string pendingId)
    {
        lock (store)
        {
            var journal = Read(store, accountId);
            if (journal.Epoch == pendingId && journal.Pending is null) return;
            if (journal.Pending?.Id != pendingId) throw new InvalidDataException("Account removal identity changed.");
            Write(store, journal with { Epoch = pendingId, Pending = null });
        }
    }

    public static void Abort(CredentialGenerationStore store, string accountId, string pendingId)
    {
        lock (store)
        {
            var journal = Read(store, accountId);
            if (journal.Pending?.Id == pendingId) Write(store, journal with { Pending = null });
        }
    }

    public static string? Epoch(CredentialGenerationStore store, string accountId, ConfigurationDto? state = null)
    {
        lock (store)
        {
            var journal = Read(store, accountId);
            // Config is authoritative after a crash between its save and completion of this journal.
            // A failed pre-publication removal leaves its still-existing account and old epoch intact.
            if (journal.Pending is { } pending && state is not null && !state.Accounts.Any(a => a.Id == accountId))
            {
                journal = journal with { Epoch = pending.Id, Pending = null };
                Write(store, journal);
            }
            return journal.Epoch;
        }
    }

    public static bool IsRemoved(CredentialGenerationStore store, LoginOperationContext context, ConfigurationDto? state = null)
        => context.Request.AccountId == context.AccountId && context.Request.AccountId is not null
            && (Epoch(store, context.AccountId, state) != context.TargetRemovalEpoch
                || state is not null && !state.Accounts.Any(a => a.Id == context.AccountId));

    private static Journal Read(CredentialGenerationStore store, string accountId)
    {
        var path = store.RemovalPath(accountId);
        CredentialGenerationStore.RefuseLink(path);
        if (!File.Exists(path)) return new(accountId);
        if (new FileInfo(path).Length > 4096) throw new InvalidDataException("Invalid account removal journal.");
        var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid account removal journal.");
        if (journal.AccountId != accountId || journal.Epoch is { } epoch && !ValidEpoch(epoch)
            || journal.Pending is { } pending && (!ValidEpoch(pending.Id) || pending.Revision < 0 || pending.AccountGeneration < 0))
            throw new InvalidDataException("Invalid account removal identity.");
        return journal;
    }
    private static bool ValidEpoch(string epoch) => epoch.Length == 32 && epoch.All(char.IsAsciiHexDigit);
    private static void Write(CredentialGenerationStore store, Journal journal) =>
        OwnStatePaths.WritePrivate(store.RemovalPath(journal.AccountId), JsonSerializer.Serialize(journal));
}
