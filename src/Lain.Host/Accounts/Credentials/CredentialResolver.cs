using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lain.Core;
using Lain.Host.Operations;

namespace Lain.Host.Accounts.Credentials;

/// <summary>Purpose-bound access lease. There is deliberately no refresh capability.</summary>
public sealed record CredentialAccess(string AccountId, string Purpose, string BindingVersion, string PrivateJson,
    CredentialHandleDto? Handle = null)
{
    public override string ToString() => "CredentialAccess [redacted]";
}

public sealed class CredentialResolver(ConfigurationRepository configuration, SecretStore secrets, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public CredentialAccess? Resolve(string accountId, string purpose)
    {
        var account = configuration.Snapshot.Accounts.SingleOrDefault(a => a.Id == accountId);
        if (account is null || !account.Enabled) return null;
        var capability = account.Capabilities?.LastOrDefault(c => c.Purpose == purpose);
        if (account.Capabilities is not null && capability is null) return null;
        var handle = capability?.CredentialId is { } credentialId ? account.Credentials?.SingleOrDefault(h => h.Id == credentialId)
            : account.Credentials?.LastOrDefault(h => h.Purposes.Contains(purpose, StringComparer.Ordinal));
        if (capability?.CredentialId is not null && handle is null)
            throw new InvalidDataException("Referenced credential generation is missing.");
        string json;
        if (handle is not null)
        {
            if (!handle.Purposes.Contains(purpose, StringComparer.Ordinal)) throw new InvalidDataException("Credential does not grant this purpose.");
            AccountService.EnsureCompatibleBinding(account.Binding, handle.Binding);
            if (secrets.Generations.IsRetired(handle)) throw new InvalidDataException("Credential owner recovery is required.");
            json = secrets.Generations.Read(account.Id, handle).PrivateJson;
            if (handle.Owner == "core") json = secrets.ProjectAccessCredential(json);
            else json = SecretStore.AccessProjection(JsonNode.Parse(json)!.AsObject());
        }
        else
        {
            if (!secrets.HasAccount(accountId)) return null;
            json = secrets.ReadAccount(accountId);
        }
        if (purpose is "inference" or "models") InferenceCredentialPolicy.ValidatePrivateJson(json);
        return new(accountId, purpose, Version(account, purpose, handle, json), json, handle);
    }

    public string? BindingVersion(string accountId, string purpose)
    {
        var access = Resolve(accountId, purpose);
        return access?.BindingVersion;
    }
    public bool IsCurrent(CredentialAccess lease) => BindingVersion(lease.AccountId, lease.Purpose) == lease.BindingVersion;

    private static string Version(AccountDto account, string purpose, CredentialHandleDto? handle, string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            account.Id, account.Generation, account.Enabled, account.Binding, purpose, handle,
            AccessDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
        })))).ToLowerInvariant();
}
