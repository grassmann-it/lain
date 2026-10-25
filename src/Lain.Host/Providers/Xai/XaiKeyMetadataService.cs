using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Accounts.Credentials;
using Lain.Host.Operations;
using Lain.Host.Quota;

namespace Lain.Host.Providers.Xai;

/// <summary>Explicit identity discovery using a saved key; never grants quota or billing access.</summary>
public sealed class XaiKeyMetadataService(ConfigurationRepository repository, SecretStore secrets,
    AccountService accounts, HttpClient http, SemaphoreSlim publicationGate, TimeProvider? timeProvider = null)
{
    private readonly CredentialResolver resolver = new(repository, secrets, timeProvider);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<ConfigurationDto> ReadAndObserveAsync(string accountId, long revision,
        long expectedGeneration, CancellationToken ct = default)
    {
        AccountDto captured;
        CredentialAccess access;
        QuotaCredential credential;
        await publicationGate.WaitAsync(ct);
        try
        {
            captured = RequireTarget(accountId, revision, expectedGeneration);
            access = resolver.Resolve(accountId, "inference") ?? resolver.Resolve(accountId, "models")
                ?? throw new InvalidOperationException("A saved xAI API key is required.");
            using var document = JsonDocument.Parse(access.PrivateJson);
            var root = document.RootElement;
            if (root.TryGetProperty("provider", out var provider) &&
                (provider.ValueKind != JsonValueKind.String || AccountService.CanonicalProviderId(provider.GetString()!) != "xai")
                || root.TryGetProperty("sourceEnabled", out var enabled) && enabled.ValueKind != JsonValueKind.True)
                throw new InvalidOperationException("The saved credential does not target xAI.");
            credential = QuotaCredential.FromSecretJson(access.PrivateJson)
                ?? throw new InvalidOperationException("A saved xAI API key is required.");
            if (credential.CredentialKind != "api-key" || credential.ExpiresAt is { } expiry && expiry <= clock.GetUtcNow()
                || access.Handle?.ExpiresAt is { } handleExpiry && handleExpiry <= clock.GetUtcNow()
                || access.Purpose == "inference" && captured.ExpiresAt is { } accountExpiry && accountExpiry <= clock.GetUtcNow())
                throw new InvalidOperationException("An unexpired xAI API key is required.");
        }
        finally { publicationGate.Release(); }

        // The request is bounded by XaiKeyMetadataReader. Disconnect/reconnect can proceed
        // while it is in flight; its captured lease has no right to publish by itself.
        var metadata = await new XaiKeyMetadataReader(http).ReadAsync(
            captured with { CredentialKind = credential.CredentialKind! }, credential, ct);
        if (metadata?.TeamId is not { } team)
            throw new InvalidOperationException("xAI did not return a team identity. The existing binding was preserved.");

        await publicationGate.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            var current = RequireTarget(accountId, revision, expectedGeneration);
            bool currentCredential;
            try { currentCredential = resolver.IsCurrent(access); }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or InvalidOperationException)
            { currentCredential = false; }
            if (!currentCredential)
                throw new InvalidOperationException("The selected xAI credential changed during discovery.");
            var binding = (current.Binding ?? new ResourceBindingDto("xai")) with
            { ResourceType = "team", ResourceId = team, Provenance = "provider-discovery" };
            if (current.Binding is { ResourceType: "team", ResourceId: { } existing,
                Provenance: "provider-discovery" or "observed" or "verified" } && existing == team)
                return repository.Snapshot;
            return accounts.ObserveBinding(accountId, expectedGeneration, binding);
        }
        finally { publicationGate.Release(); }
    }

    private AccountDto RequireTarget(string accountId, long revision, long expectedGeneration)
    {
        var snapshot = repository.Snapshot;
        if (snapshot.Revision != revision) throw new RevisionConflictException(revision, snapshot.Revision);
        var account = snapshot.Accounts.SingleOrDefault(a => a.Id == accountId)
            ?? throw new KeyNotFoundException("Account not found.");
        if (!account.Enabled || account.Generation != expectedGeneration || AccountService.CanonicalProviderId(account.Provider) != "xai")
            throw new InvalidOperationException("An enabled, unchanged xAI account is required.");
        return account;
    }
}
