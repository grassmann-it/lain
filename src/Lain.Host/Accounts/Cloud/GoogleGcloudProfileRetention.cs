using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts.CliConnections;
using Lain.Host.Accounts.Credentials;
using Lain.Host.Operations;

namespace Lain.Host.Accounts.Cloud;

/// <summary>Post-publication cleanup only. Uncertain recovery metadata always retains CLI-owned grants.</summary>
public sealed class GoogleGcloudProfileRetention(ConfigurationRepository configuration, SecretStore secrets,
    OwnStatePaths paths, GoogleGcloudLoginAdapter adapter)
{
    private const int MaximumRecords = 1024;
    private sealed record RetentionState(long Revision, HashSet<string> Profiles);

    /// <summary>Call under the publication gate before accepting requests. Never activates or refreshes a grant.</summary>
    public async Task StartupReconcileAsync(CancellationToken ct = default)
    {
        try
        {
            var state = ReadRetentionState(ct);
            if (state is null) return;
            // Validate the complete bounded receipt set before deleting anything.
            var profiles = adapter.ReadOwnedProfileReceipts();
            foreach (var profile in profiles)
            {
                ct.ThrowIfCancellationRequested();
                if (configuration.Snapshot.Revision != state.Revision) return;
                await adapter.RetireOwnedProfileAsync(profile, state.Profiles, ct);
            }
        }
        catch (Exception error) when (Uncertain(error)) { }
    }

    public async Task DisconnectAsync(AccountDto oldAccount, CancellationToken ct = default)
    {
        var candidates = oldAccount.Credentials?.Where(h => h.Owner == "google-gcloud").ToArray() ?? [];
        if (candidates.Length == 0) return;
        try
        {
            if (candidates.Length > MaximumRecords) return;
            var state = ReadRetentionState(ct);
            if (state is null) return;
            // The foundation invokes this under its publication gate. Also defer if the observed revision changed.
            foreach (var handle in candidates)
            {
                if (configuration.Snapshot.Revision != state.Revision) return;
                var json = secrets.Generations.Read(oldAccount.Id, handle).PrivateJson;
                await adapter.DisconnectAsync(json, state.Profiles, ct);
            }
        }
        catch (Exception error) when (Uncertain(error))
        { /* Malformed/missing/restore-quarantined state is not permission to erase an OAuth grant. */ }
    }
    private RetentionState? ReadRetentionState(CancellationToken ct)
    {
        var restoreMarker = Path.Combine(paths.Root, "restore-requires-reconnect");
        CliPrivatePaths.RefuseLinks(restoreMarker);
        if (File.Exists(restoreMarker)) return null;
        var snapshot = configuration.Snapshot;
        if (snapshot.Accounts.Length > MaximumRecords) return null;
        var retained = new HashSet<string>(StringComparer.Ordinal);
        foreach (var account in snapshot.Accounts)
        {
            if (account.Credentials?.Length > MaximumRecords) return null;
            var selected = AccountService.EffectiveCapabilities(account).Select(c => c.CredentialId).Where(id => id is not null).ToHashSet(StringComparer.Ordinal);
            if (selected.Any(id => account.Credentials?.Count(h => h.Id == id) != 1)) return null;
            foreach (var handle in account.Credentials?.Where(h => h.Owner == "google-gcloud" && selected.Contains(h.Id)) ?? [])
                retained.Add(GoogleGcloudLoginAdapter.GetProfileReference(secrets.Generations.Read(account.Id, handle).PrivateJson));
        }
        var recoveryRoot = Path.Combine(paths.Credentials, "recovery");
        CliPrivatePaths.RefuseLinks(recoveryRoot);
        if (Directory.Exists(recoveryRoot))
        {
            var files = Directory.EnumerateFiles(recoveryRoot, "*.json", SearchOption.TopDirectoryOnly).Take(MaximumRecords + 1).ToArray();
            if (files.Length > MaximumRecords) return null;
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                using var document = JsonDocument.Parse(CliPrivatePaths.Read(file, 128 * 1024));
                if (Path.GetFileName(file).StartsWith("request-", StringComparison.Ordinal))
                {
                    // Idempotency pointers contain no grant; missing/ambiguous pointers defer cleanup.
                    if (document.RootElement.EnumerateObject().Count() != 2 || !document.RootElement.TryGetProperty("Fingerprint", out var fingerprint)
                        || fingerprint.ValueKind != JsonValueKind.String || !document.RootElement.TryGetProperty("OperationId", out var op)
                        || op.ValueKind != JsonValueKind.String || !File.Exists(secrets.Generations.RecoveryPath(op.GetString()!))) return null;
                    continue;
                }
                var record = document.RootElement.Deserialize<CredentialRecoveryRecord>();
                if (record is null || record.OperationId != Path.GetFileNameWithoutExtension(file) || string.IsNullOrWhiteSpace(record.AccountId)
                    || record.Request is null || string.IsNullOrWhiteSpace(record.Request.Provider) || record.Revision < 0 || record.AccountGeneration < 0) return null;
                if (record.State == "target-removed") continue;
                if (record.State == "published") { if (record.Handle is null) return null; continue; }
                if (record.State != "prepared" || record.Handle is null) return null;
                if (record.Handle.Owner == "google-gcloud")
                    retained.Add(GoogleGcloudLoginAdapter.GetProfileReference(secrets.Generations.Read(record.AccountId, record.Handle).PrivateJson));
            }
        }
        return new(snapshot.Revision, retained);
    }
    private static bool Uncertain(Exception error) => error is IOException or InvalidDataException or JsonException
        or ArgumentException or InvalidOperationException or UnauthorizedAccessException;

}
