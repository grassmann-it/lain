using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Accounts.Credentials;
using Lain.Host.Accounts.Monitoring;
using Lain.Host.Operations;
using Lain.Host.Quota;

namespace Lain.Host.Providers.Xai;

/// <summary>The ordinary credential-connect path, with independent xAI identity proof before reconnect publication.</summary>
public sealed class XaiCredentialReconnectService(ConfigurationRepository repository, SecretStore secrets,
    AccountService accounts, ConnectionMethodCatalog methods, MonitorAccessService monitors,
    HttpClient http, SemaphoreSlim publicationGate, TimeProvider? timeProvider = null)
{
    private readonly CredentialResolver resolver = new(repository, secrets, timeProvider);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<ConfigurationDto> ConnectAsync(long revision, ConnectCredentialRequest request, CancellationToken ct = default)
    {
        LoginOperationContext context;
        AccountDto target;
        string? previousCredential;
        await publicationGate.WaitAsync(ct);
        try
        {
            if (repository.Snapshot.Revision != revision) throw new RevisionConflictException(revision, repository.Snapshot.Revision);
            // All normal methods retain the same catalog validation and synchronous publication.
            // New xAI accounts remain explicit key connects; only reconnect verifies remotely here.
            if (AccountService.CanonicalProviderId(request.Connection.Provider) != "xai" || request.Connection.AccountId is null)
                return methods.Connect(revision, request, accounts, monitors, repository);
            request = methods.PrepareCredentialRequest(request);
            if (request.Kind != "api-key" || !request.Connection.Purposes.Any(p => p is "inference" or "models")
                || string.IsNullOrWhiteSpace(request.Secret) || request.Secret.Length > 65536 || request.Secret.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
                || request.ExpiresAt is { } expiry && expiry <= clock.GetUtcNow())
                throw new ArgumentException("Reconnect requires a valid xAI inference or model API key.");
            context = accounts.ReserveConnect(revision, request.Connection, "xai-reconnect-" + Guid.NewGuid().ToString("N"), clock.GetUtcNow().AddMinutes(1));
            target = repository.Snapshot.Accounts.Single(a => a.Id == context.AccountId);
            previousCredential = SelectedVersion(target.Id);
        }
        finally { publicationGate.Release(); }

        // Never reuse the old key's team or the caller's claimed provenance. This
        // request carries the NEW supplied API key and proves only its returned team.
        XaiKeyMetadata? metadata;
        try
        {
            metadata = await new XaiKeyMetadataReader(http).ReadAsync(target with { CredentialKind = "api-key" },
                new(request.Secret, expiresAt: request.ExpiresAt, baseUrl: request.BaseUrl), ct);
        }
        catch (QuotaFetchException)
        { throw new InvalidOperationException("The replacement xAI key could not confirm the existing team. The existing credential was preserved."); }
        if (metadata?.TeamId is not { } team)
            throw new InvalidOperationException("The replacement xAI key did not establish a team identity. The existing credential was preserved.");
        var binding = new ResourceBindingDto("xai", ResourceType: "team", ResourceId: team, Provenance: "provider-discovery");
        var credential = new PreparedLoginCredential("static-key", request.Connection.Purposes, binding,
            JsonSerializer.Serialize(new
            {
                kind = "api-key", apiKey = request.Secret, provider = "xai", baseUrl = request.BaseUrl,
                settings = request.Settings, sourceSection = "openai-compatibility", coreProvider = "xai"
            }), request.ExpiresAt);

        await publicationGate.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            var current = repository.Snapshot;
            if (current.Revision != revision) throw new RevisionConflictException(revision, current.Revision);
            var account = current.Accounts.SingleOrDefault(a => a.Id == context.AccountId)
                ?? throw new KeyNotFoundException("The reconnect target was removed.");
            if (account.Generation != context.AccountGeneration || clock.GetUtcNow() >= context.ExpiresAt
                || SelectedVersion(account.Id) != previousCredential)
                throw new InvalidOperationException("The xAI reconnect target or selected credential changed.");
            // The common publication authority checks every pre-existing identity
            // dimension and the captured target-removal fence before adopting this proof.
            var handle = secrets.Generations.Prepare(account.Id, credential, context.OperationId);
            try { return accounts.CommitPrepared(context, handle); }
            catch { secrets.Generations.DiscardStaticPreparation(handle); throw; }
        }
        finally { publicationGate.Release(); }
    }

    private string? SelectedVersion(string accountId)
    {
        try { return resolver.BindingVersion(accountId, "inference") ?? resolver.BindingVersion(accountId, "models"); }
        // A missing/retired old key can be repaired with independently verified new
        // credentials. Public generation/revision and the removal fence still apply.
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or InvalidOperationException)
        { return null; }
    }
}
