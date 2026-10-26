using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts.Credentials;
using Lain.Host.Accounts.Monitoring;
using Lain.Host.Operations;

namespace Lain.Host.Quota;

/// <summary>Direct quota reads resolve their own capability and never start an inference runtime.</summary>
public sealed class HostQuotaSource(ConfigurationRepository repository, SecretStore secrets, MonitorAccessService monitorAccess)
    : IQuotaAccountSource, IQuotaCredentialSource, IRouterAvailabilityQuotaProofSource
{
    private readonly CredentialResolver resolver = new(repository, secrets);
    private readonly RouterAvailabilityProofStore routerProofs = new();
    public RouterAvailabilityQuotaProof? GetRouterAvailabilityProof(string accountId, string? bindingVersion) => routerProofs.Get(accountId, bindingVersion);
    public Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AccountDto>>(repository.Snapshot.Accounts);

    public Task<QuotaCredential?> GetCredentialAsync(string accountId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        routerProofs.Remove(accountId);
        var account = repository.Snapshot.Accounts.SingleOrDefault(a => a.Id == accountId);
        if (account is null || !account.Enabled) return Task.FromResult<QuotaCredential?>(null);
        var access = resolver.Resolve(accountId, "quota");
        var monitor = monitorAccess.GetCredential(accountId);
        if (access is null && monitor is null) return Task.FromResult<QuotaCredential?>(null);
        var version = Fingerprint(accountId, access, monitor);
        var settings = AccessSettings(access?.PrivateJson);
        if (monitor is not null)
        {
            if (monitor.Kind == "inference-key")
            {
                var inference = resolver.Resolve(accountId, "inference");
                if (inference is null) return Task.FromResult<QuotaCredential?>(null);
                var inferenceJson = QuotaTransportProjection.Select(account, inference.PrivateJson);
                return Task.FromResult(inferenceJson is null ? null : QuotaCredential.FromSecretJson(inferenceJson, monitor, version, ["quota"]));
            }
            // Separate monitoring is valid without an inference file, or with expired inference.
            return Task.FromResult<QuotaCredential?>(new("", providerAccountId: access?.Handle?.Binding.Subject,
                expiresAt: monitor.ExpiresAt, settings: settings, monitor: monitor,
                bindingVersion: version, purposes: ["quota"], credentialKind: monitor.Kind));
        }
        if (access is null) return Task.FromResult<QuotaCredential?>(null);
        var projectedJson = QuotaTransportProjection.Select(account, access.PrivateJson);
        if (projectedJson is null) return Task.FromResult<QuotaCredential?>(null);
        routerProofs.CaptureLegacy(account, access.PrivateJson, projectedJson, version);
        var credential = QuotaCredential.FromSecretJson(projectedJson, bindingVersion: version, purposes: ["quota"]);
        if (credential is null)
        {
            using var document = JsonDocument.Parse(access.PrivateJson);
            var token = JsonFields.String(document.RootElement, "accessToken");
            if (!string.IsNullOrWhiteSpace(token)) credential = new(token, access.Handle?.Binding.Subject,
                access.Handle?.ExpiresAt, JsonFields.String(document.RootElement, "baseUrl"), settings, bindingVersion: version, purposes: ["quota"],
                credentialKind: JsonFields.String(document.RootElement, "kind"));
        }
        return Task.FromResult(credential);
    }

    public async Task<string?> GetBindingVersionAsync(string accountId, CancellationToken ct = default) =>
        (await GetCredentialAsync(accountId, ct))?.BindingVersion;

    private static IReadOnlyDictionary<string, string>? AccessSettings(string? json)
    {
        if (json is null) return null;
        using var document = JsonDocument.Parse(json);
        var settings = JsonFields.Object(document.RootElement, "settings");
        return settings.ValueKind == JsonValueKind.Object ? settings.EnumerateObject()
            .Where(p => p.Value.ValueKind == JsonValueKind.String).ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal) : null;
    }

    private string Fingerprint(string accountId, CredentialAccess? access, QuotaMonitorCredential? monitor)
    {
        var account = repository.Snapshot.Accounts.SingleOrDefault(a => a.Id == accountId);
        // Legacy monitorfiles are included too, so old credential replacement never reuses an unrelated cached observation.
        var input = JsonSerializer.Serialize(new { accountId, account?.Generation, account?.Binding, access?.BindingVersion,
            monitor?.Kind, monitor?.ResourceId, monitor?.ExpiresAt, SecretDigest = monitor?.Secret is { } token
                ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))) : null,
            Inference = monitor?.Kind == "inference-key" ? resolver.BindingVersion(accountId, "inference") : null });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }
}
