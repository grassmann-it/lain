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
    public async Task DisconnectAsync(AccountDto oldAccount, CancellationToken ct = default)
    {
        var candidates = oldAccount.Credentials?.Where(h => h.Owner == "google-gcloud").ToArray() ?? [];
        if (candidates.Length == 0) return;
        try
        {
            var snapshot = configuration.Snapshot;
            if (snapshot.Accounts.Length > MaximumRecords || candidates.Length > MaximumRecords) return;
            var retained = new HashSet<string>(StringComparer.Ordinal);
            foreach (var account in snapshot.Accounts)
            {
                if (account.Credentials?.Length > MaximumRecords) return;
                var selected = AccountService.EffectiveCapabilities(account).Select(c => c.CredentialId).Where(id => id is not null).ToHashSet(StringComparer.Ordinal);
                foreach (var handle in account.Credentials?.Where(h => h.Owner == "google-gcloud" && selected.Contains(h.Id)) ?? [])
                    retained.Add(GoogleGcloudLoginAdapter.GetProfileReference(secrets.Generations.Read(account.Id, handle).PrivateJson));
            }
            var recoveryRoot = Path.Combine(paths.Credentials, "recovery");
            CliPrivatePaths.RefuseLinks(recoveryRoot);
            if (Directory.Exists(recoveryRoot))
            {
                var files = Directory.EnumerateFiles(recoveryRoot, "*.json", SearchOption.TopDirectoryOnly).Take(MaximumRecords + 1).ToArray();
                if (files.Length > MaximumRecords) return;
                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    using var document = JsonDocument.Parse(CliPrivatePaths.Read(file, 128 * 1024));
                    if (Path.GetFileName(file).StartsWith("request-", StringComparison.Ordinal))
                    {
                        // Idempotency pointers contain no grant; missing/ambiguous pointers defer cleanup.
                        if (document.RootElement.EnumerateObject().Count() != 2 || !document.RootElement.TryGetProperty("Fingerprint", out var fingerprint)
                            || fingerprint.ValueKind != JsonValueKind.String || !document.RootElement.TryGetProperty("OperationId", out var op)
                            || op.ValueKind != JsonValueKind.String || !File.Exists(secrets.Generations.RecoveryPath(op.GetString()!))) return;
                        continue;
                    }
                    var record = document.RootElement.Deserialize<CredentialRecoveryRecord>();
                    if (record is null || record.OperationId != Path.GetFileNameWithoutExtension(file)) return;
                    if (record.State == "published") continue;
                    if (record.State != "prepared" || record.Handle is null) return;
                    if (record.Handle.Owner == "google-gcloud")
                        retained.Add(GoogleGcloudLoginAdapter.GetProfileReference(secrets.Generations.Read(record.AccountId, record.Handle).PrivateJson));
                }
            }
            // The foundation invokes this under its publication gate. Also defer if the observed revision changed.
            foreach (var handle in candidates)
            {
                if (configuration.Snapshot.Revision != snapshot.Revision) return;
                var json = secrets.Generations.Read(oldAccount.Id, handle).PrivateJson;
                await adapter.DisconnectAsync(json, retained, ct);
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        { /* Malformed/missing/restore-quarantined state is not permission to erase an OAuth grant. */ }
    }
}
