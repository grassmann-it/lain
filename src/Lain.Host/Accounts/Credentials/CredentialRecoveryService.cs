using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Lain.Core;
using Lain.Host.Operations;

namespace Lain.Host.Accounts.Credentials;

/// <summary>Private recovery metadata; secrets remain in immutable generation files.</summary>
public sealed record CredentialRecoveryRecord(string OperationId, string AccountId, long Revision, long AccountGeneration,
    ConnectAccountRequest Request, string State, CredentialHandleDto? Handle = null, string? RemoteId = null);

public sealed class CredentialRecoveryService(ConfigurationRepository configuration, AccountService accounts,
    CredentialGenerationStore generations)
{
    public static CredentialRecoveryRecord? FindRequest(CredentialGenerationStore store, string key, string fingerprint)
    {
        var path = store.RecoveryPath("request-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant());
        if (!File.Exists(path)) return null;
        CredentialGenerationStore.RefuseLink(path);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.GetProperty("Fingerprint").GetString() != fingerprint)
            throw new ArgumentException("The idempotency key belongs to another connection request.");
        return Read(store, document.RootElement.GetProperty("OperationId").GetString()!);
    }
    public static void ReserveRequest(CredentialGenerationStore store, string key, string fingerprint, LoginOperationContext context)
    {
        SaveStarted(store, context);
        var path = store.RecoveryPath("request-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant());
        OwnStatePaths.WritePrivate(path, JsonSerializer.Serialize(new { Fingerprint = fingerprint, context.OperationId }));
    }

    public static void SaveStarted(CredentialGenerationStore store, LoginOperationContext context) =>
        Write(store, new(context.OperationId, context.AccountId, context.Revision, context.AccountGeneration, context.Request, "remote-pending"));

    public static void SavePrepared(CredentialGenerationStore store, LoginOperationContext context, CredentialHandleDto handle, string? remoteId) =>
        Write(store, new(context.OperationId, context.AccountId, context.Revision, context.AccountGeneration, context.Request, "prepared", handle, remoteId));

    public static void MarkPublished(CredentialGenerationStore store, string operationId)
    {
        var path = store.RecoveryPath(operationId);
        if (!File.Exists(path)) return;
        var record = Read(store, operationId);
        Write(store, record with { State = "published" });
    }

    public static CredentialRecoveryRecord Read(CredentialGenerationStore store, string operationId)
    {
        var path = store.RecoveryPath(operationId);
        CredentialGenerationStore.RefuseLink(path);
        if (new FileInfo(path).Length > 128 * 1024) throw new InvalidDataException("Invalid credential recovery record.");
        var record = JsonSerializer.Deserialize<CredentialRecoveryRecord>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid recovery record.");
        if (record.OperationId != operationId) throw new InvalidDataException("Recovery operation identity mismatch.");
        if (record.State != "published" && store.FindPrepared(record.OperationId, record.AccountId) is { } prepared &&
            (record.Handle is null || prepared.Handle.Generation > record.Handle.Generation))
        {
            if (record.Handle is { } previous && (previous.Id != prepared.Handle.Id || previous.Owner != prepared.Handle.Owner))
                throw new InvalidDataException("Recovery generation ownership changed unexpectedly.");
            record = record with { State = "prepared", Handle = prepared.Handle, RemoteId = prepared.RemoteId };
            Write(store, record);
        }
        return record;
    }

    // Explicit local action: revalidate the saved result against a fresh revision, never invoke the provider again.
    public ConfigurationDto Adopt(long revision, string operationId)
    {
        var record = Read(generations, operationId);
        var state = configuration.Snapshot;
        if (state.Revision != revision) throw new RevisionConflictException(revision, state.Revision);
        if (record.Handle is null || record.State == "remote-pending")
            throw new InvalidOperationException("Remote authorization result is unknown. Reconnect or inspect the provider; do not repeat key creation blindly.");
        if (record.State == "published") return state;
        var existing = state.Accounts.SingleOrDefault(a => a.Id == record.AccountId);
        if (existing?.Credentials?.Any(h => h.Id == record.Handle.Id && h.Generation == record.Handle.Generation) == true)
        {
            MarkPublished(generations, operationId);
            return state;
        }
        if (record.Request.AccountId is not null && (existing is null || existing.Generation != record.AccountGeneration))
            throw new InvalidOperationException("Recovery target was removed or changed. The saved credential cannot be silently attached.");
        generations.Read(record.AccountId, record.Handle);
        var context = new LoginOperationContext(record.OperationId, record.AccountId, revision, record.AccountGeneration,
            record.Request, DateTimeOffset.MaxValue);
        var result = accounts.CommitPrepared(context, record.Handle);
        MarkPublished(generations, operationId);
        return result;
    }

    private static void Write(CredentialGenerationStore store, CredentialRecoveryRecord record) =>
        OwnStatePaths.WritePrivate(store.RecoveryPath(record.OperationId), JsonSerializer.Serialize(record));
}

public sealed record CredentialRefreshContext(string AccountId, CredentialHandleDto Handle, string PrivateJson)
{
    public override string ToString() => "CredentialRefreshContext [redacted]";
}
public interface ICredentialOwner
{
    string Owner { get; }
    Task<PreparedLoginCredential> RefreshAsync(CredentialRefreshContext context, CancellationToken ct);
}

/// <summary>One rotating owner consumes a generation once. A failed or lost result requires explicit recovery.</summary>
public sealed class CredentialOwnerService(ConfigurationRepository configuration, AccountService accounts,
    CredentialGenerationStore generations, IEnumerable<ICredentialOwner> owners, SemaphoreSlim? publicationGate = null)
{
    private readonly Dictionary<string, ICredentialOwner> implementations = owners.ToDictionary(o => o.Owner, StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> gates = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim localPublicationGate = new(1, 1);

    public Task<ConfigurationDto> RefreshAsync(string accountId, string credentialId, CancellationToken ct = default) =>
        RefreshCoreAsync(accountId, credentialId, null, ct);

    internal Task<ConfigurationDto> RefreshSelectedQuotaAsync(string accountId, string credentialId, long accountGeneration,
        long credentialGeneration, CancellationToken ct) =>
        RefreshCoreAsync(accountId, credentialId, new(accountGeneration, credentialGeneration), ct);

    private sealed record QuotaSelection(long AccountGeneration, long CredentialGeneration);
    private static bool MatchesSelection(AccountDto? account, string credentialId, QuotaSelection selection) =>
        account is { Enabled: true } && account.Generation == selection.AccountGeneration &&
        AccountService.EffectiveCapabilities(account).Any(value => value.Purpose == "quota" && value.CredentialId == credentialId) &&
        account.Credentials?.Any(value => value.Id == credentialId && value.Generation == selection.CredentialGeneration && value.Purposes.Contains("quota")) == true;

    private async Task<ConfigurationDto> RefreshCoreAsync(string accountId, string credentialId, QuotaSelection? selection, CancellationToken ct)
    {
        var initial = configuration.Snapshot;
        var requestedAccount = initial.Accounts.SingleOrDefault(a => a.Id == accountId);
        if (selection is not null && !MatchesSelection(requestedAccount, credentialId, selection)) return initial;
        var requested = requestedAccount?.Credentials?.SingleOrDefault(h => h.Id == credentialId)
            ?? throw new KeyNotFoundException("Credential not found.");
        var gate = gates.GetOrAdd(credentialId, _ => new(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var snapshot = configuration.Snapshot;
            var currentAccount = snapshot.Accounts.SingleOrDefault(a => a.Id == accountId);
            if (selection is not null && !MatchesSelection(currentAccount, credentialId, selection)) return snapshot;
            var account = currentAccount ?? throw new KeyNotFoundException("Account not found.");
            var handle = account.Credentials?.SingleOrDefault(h => h.Id == credentialId) ?? throw new KeyNotFoundException("Credential not found.");
            if (handle.Generation != requested.Generation) return snapshot; // A concurrent owner already rotated this generation.
            if (handle.Owner is "static-key" or "external-snapshot" or "core")
                throw new InvalidOperationException("This credential is not refreshed by the host.");
            if (!implementations.TryGetValue(handle.Owner, out var owner)) throw new InvalidOperationException("Credential owner is not available.");
            var stored = generations.Read(account.Id, handle);
            var operationId = "rotation-" + Guid.NewGuid().ToString("N");
            var purposes = AccountService.EffectiveCapabilities(account).Where(c => c.CredentialId == handle.Id).Select(c => c.Purpose).ToArray();
            var context = accounts.ReserveConnect(snapshot.Revision, new(account.Provider, "owner-refresh", purposes,
                AccountId: account.Id, Binding: handle.Binding, ExpectedGeneration: account.Generation), operationId, DateTimeOffset.MaxValue);
            CredentialRecoveryService.SaveStarted(generations, context);
            // This marker is durable before the remote call. Backups cannot reactivate consumed refresh material.
            generations.RetireForRotation(handle, operationId);
            var refreshed = await owner.RefreshAsync(new(account.Id, handle, stored.PrivateJson), ct);
            var next = generations.Prepare(account.Id, refreshed with { Owner = handle.Owner, PreserveForRecovery = true }, operationId,
                handle.Id, checked(handle.Generation + 1));
            CredentialRecoveryService.SavePrepared(generations, context, next, refreshed.RemoteId);
            if (refreshed.Owner != handle.Owner) throw new InvalidDataException("Credential refresh changed its owner.");
            AccountService.EnsureCompatibleBinding(handle.Binding, refreshed.Binding);
            // Remote rotation may have consumed the old token; no error path restores it.
            var acquired = false;
            var commitGate = publicationGate ?? localPublicationGate;
            try
            {
                await commitGate.WaitAsync(ct);
                acquired = true;
                var latest = configuration.Snapshot;
                var current = latest.Accounts.SingleOrDefault(value => value.Id == account.Id);
                if (current is null || current.Generation != account.Generation || current.Enabled != account.Enabled ||
                    current.Credentials?.Any(value => value.Id == handle.Id && value.Generation == handle.Generation) != true ||
                    !AccountService.EffectiveCapabilities(current).Where(value => value.CredentialId == handle.Id).Select(value => value.Purpose)
                        .Order(StringComparer.Ordinal).SequenceEqual(purposes.Order(StringComparer.Ordinal)))
                    throw new InvalidOperationException("Account or credential binding changed during renewal. Review its saved result.");
                // An unrelated account/configuration mutation may advance revision while this owner waits.
                // Revalidate the target under the publication gate, then adopt the already saved result locally.
                var result = accounts.CommitPrepared(context with { Revision = latest.Revision }, next, () => !ct.IsCancellationRequested);
                try { CredentialRecoveryService.MarkPublished(generations, operationId); }
                catch (IOException) { } // Configuration is authoritative; recovery reconciles a matching published handle.
                return result;
            }
            finally { if (acquired) commitGate.Release(); }
        }
        finally { gate.Release(); }
    }
}
