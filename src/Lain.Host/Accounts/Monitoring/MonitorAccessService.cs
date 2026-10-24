using System.Text.Json;
using System.Text.RegularExpressions;
using Lain.Core;
using Lain.Host.Operations;
using Lain.Host.Quota;

namespace Lain.Host.Accounts.Monitoring;

/// <summary>Local, revision-guarded monitor setup. Saving never calls an upstream API.</summary>
public sealed class MonitorAccessService(ConfigurationRepository configuration, SecretStore secrets,
    MonitorCredentialStore store, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public MonitorAccessDto Get(string accountId)
    {
        var account = RequireAccount(configuration.Snapshot, accountId);
        var options = MonitorAccessCatalog.Options(account.Provider);
        if (options.Length == 0)
            return new(account.Id, account.Provider, "unsupported", Guidance: "No additional monitor credential is supported for this provider.");
        StoredMonitorAccess? stored;
        try { stored = store.Read(account); }
        catch (Exception error) when (error is IOException or JsonException)
        {
            return new(account.Id, account.Provider, "invalid", Guidance: "The saved monitor access cannot be read. Configure it again.",
                DocumentationUrl: options[0].DocumentationUrl);
        }
        if (stored is null)
            return new(account.Id, account.Provider, "not-configured", Guidance: options[0].Description,
                DocumentationUrl: options[0].DocumentationUrl);
        try { Validate(account, stored.Kind, stored.Secret, stored.ResourceId, stored.ExpiresAt, requireFutureExpiry: false); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or JsonException)
        {
            return new(account.Id, account.Provider, "invalid", Guidance: "The saved monitor access does not match this account. Configure it again.",
                DocumentationUrl: options[0].DocumentationUrl);
        }
        var expired = stored.ExpiresAt <= clock.GetUtcNow();
        var selectedId = account.Capabilities?.SingleOrDefault(c => c.Purpose == "quota")?.CredentialId;
        var owner = account.Credentials?.SingleOrDefault(h => h.Id == selectedId)?.Owner;
        var managedName = owner switch { "google-oauth" => "Google OAuth", "cloudflare-oauth" => "Cloudflare OAuth", _ => null };
        var guidance = managedName is not null
            ? expired
                ? $"{managedName} monitor access expired. Refresh this connection or reconnect; its registered owner manages token rotation."
                : $"{managedName} manages this monitor credential. Permissions and reported values remain unverified until a supported read succeeds."
            : expired
                ? "Monitor token expired. Enter a new token locally; external tokens are never refreshed or revoked."
                : "Monitor access is saved locally. Permissions and reported values remain unverified until a supported read succeeds.";
        return new(account.Id, account.Provider, expired ? "expired" : "configured-unverified", stored.Kind,
            stored.ResourceId, stored.ExpiresAt, guidance,
            options.First(o => o.Kind == stored.Kind).DocumentationUrl);
    }

    public ConfigurationDto Set(long revision, string accountId, MonitorAccessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state = configuration.Snapshot;
        if (state.Revision != revision) throw new RevisionConflictException(revision, state.Revision);
        var account = RequireAccount(state, accountId);
        var resourceId = request.ResourceId?.Trim();
        Validate(account, request.Kind, request.Secret, resourceId, request.ExpiresAt, requireFutureExpiry: true);
        var resourceType = MonitorAccessCatalog.Provider(account.Provider) == "gemini-api" ? "project" : "account";
        var binding = (account.Binding ?? new ResourceBindingDto(account.Provider)) with { ResourceType = resourceType, ResourceId = resourceId };
        AccountService.EnsureCompatibleBinding(account.Binding, binding);
        var connection = new ConnectAccountRequest(account.Provider, "monitor-" + request.Kind, ["quota"], AccountId: account.Id,
            Binding: binding, ExpectedGeneration: account.Generation);
        var value = new StoredMonitorAccess(account.Id, account.Provider, request.Kind, request.Secret, resourceId!, request.ExpiresAt?.ToUniversalTime());
        var json = JsonSerializer.Serialize(new { kind = "monitor-access", monitorAccess = value });
        return new AccountService(configuration, secrets).Connect(revision, connection,
            new(request.Kind == "access-token-snapshot" ? "external-snapshot" : "static-key", ["quota"], binding, json, request.ExpiresAt));
    }

    public ConfigurationDto Create(long revision, string name, string provider, MonitorAccessRequest request)
    {
        provider = AccountService.CanonicalProviderId(provider);
        if (request.Kind == "inference-key") throw new ArgumentException("Create a monitor account with its own access token first.");
        var accounts = new AccountService(configuration, secrets);
        var resourceType = MonitorAccessCatalog.Provider(provider) == "gemini-api" ? "project" : "account";
        var binding = new ResourceBindingDto(provider, ResourceType: resourceType, ResourceId: request.ResourceId?.Trim());
        var connection = new ConnectAccountRequest(provider, "monitor-" + request.Kind, ["quota"], Name: name, Binding: binding);
        var context = accounts.ReserveConnect(revision, connection, "monitor-" + Guid.NewGuid().ToString("N"), clock.GetUtcNow().AddMinutes(10));
        var account = new AccountDto(context.AccountId, name, provider, true, "monitor", "unverified");
        Validate(account, request.Kind, request.Secret, binding.ResourceId, request.ExpiresAt, requireFutureExpiry: true);
        var value = new StoredMonitorAccess(account.Id, provider, request.Kind, request.Secret, binding.ResourceId!, request.ExpiresAt);
        var handle = secrets.Generations.Prepare(account.Id, new(request.Kind == "access-token-snapshot" ? "external-snapshot" : "static-key",
            ["quota"], binding, JsonSerializer.Serialize(new { kind = "monitor-access", monitorAccess = value }), request.ExpiresAt));
        try { return accounts.CommitPrepared(context, handle); }
        catch { secrets.Generations.DiscardStaticPreparation(handle); throw; }
    }

    public ConfigurationDto Remove(long revision, string accountId) => configuration.Update(revision, state =>
    {
        var account = RequireAccount(state, accountId);
        var capabilities = AccountService.EffectiveCapabilities(account).Where(c => c.Purpose != "quota").ToArray();
        var handles = (account.Credentials ?? []).Where(h => capabilities.Any(c => c.CredentialId == h.Id)).ToArray();
        // Removing the public capability invalidates reads first. Private generations remain recoverable and shared inference stays referenced.
        return state with { Accounts = state.Accounts.Select(a => a.Id == accountId ? a with {
            Generation = checked(a.Generation + 1), Capabilities = capabilities, Credentials = handles } : a).ToArray() };
    });

    /// <summary>Private access projection for quota readers only. No refresh or revoke can occur here.</summary>
    public QuotaMonitorCredential? GetCredential(string accountId)
    {
        var account = RequireAccount(configuration.Snapshot, accountId);
        var value = store.Read(account);
        if (value is null) return null;
        Validate(account, value.Kind, value.Secret, value.ResourceId, value.ExpiresAt, requireFutureExpiry: false);
        // Expiry is preserved for the adapter to classify; it must not fall back to an inference key.
        return new(value.Kind, value.Secret, value.ResourceId, value.ExpiresAt);
    }

    private void Validate(AccountDto account, string kind, string? secret, string? resourceId, DateTimeOffset? expiresAt, bool requireFutureExpiry)
    {
        var provider = MonitorAccessCatalog.Provider(account.Provider);
        if (!MonitorAccessCatalog.Options(provider).Any(o => o.Kind == kind))
            throw new ArgumentException("This monitor credential kind is not supported for the selected provider.");
        if (kind == "inference-key")
        {
            if (!secrets.HasAccount(account.Id)) throw new ArgumentException("No inference key is available for this account.");
            if (secret is not null || expiresAt is not null)
                throw new ArgumentException("Using the inference key requires no separate secret or expiry.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(secret) || secret.Length > 65536 || secret.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
                throw new ArgumentException("A valid monitor token is required.");
            if (kind == "access-token-snapshot" && expiresAt is null)
                throw new ArgumentException("The access-token snapshot requires its expiry.");
            if (requireFutureExpiry && expiresAt <= clock.GetUtcNow())
                throw new ArgumentException("The monitor token must not already be expired.");
        }
        if (resourceId is null || resourceId.Length > 128) throw new ArgumentException("A monitor resource ID is required.");
        if (provider == "gemini-api")
        {
            if (!Regex.IsMatch(resourceId, @"^[a-z][a-z0-9-]{4,28}[a-z0-9]$", RegexOptions.CultureInvariant))
                throw new ArgumentException("Use the textual Google project ID without a resource prefix.");
        }
        else if (provider == "cloudflare-workers-ai")
        {
            if (!Regex.IsMatch(resourceId, @"^[a-fA-F0-9]{32}$", RegexOptions.CultureInvariant))
                throw new ArgumentException("Use the 32-character Cloudflare account ID.");
            var boundIds = InferenceResourceIds(account, provider);
            if (boundIds.Any(id => !string.Equals(id, resourceId, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Cloudflare monitor and inference account IDs must match.");
        }
        else if (provider == "fireworks")
        {
            if (!Regex.IsMatch(resourceId, @"^[a-zA-Z0-9][a-zA-Z0-9_-]{0,127}$", RegexOptions.CultureInvariant))
                throw new ArgumentException("Use the Fireworks account ID without a resource prefix.");
            if (InferenceResourceIds(account, provider).Any(id => id != resourceId))
                throw new ArgumentException("Fireworks monitor and inference account IDs must match.");
        }
    }

    private HashSet<string> InferenceResourceIds(AccountDto account, string provider)
    {
        if (!secrets.HasAccount(account.Id)) return [];
        try
        {
            using var document = JsonDocument.Parse(secrets.ReadAccount(account.Id));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            Read(document.RootElement);
            if (document.RootElement.TryGetProperty("transports", out var transports) && transports.ValueKind == JsonValueKind.Array)
                foreach (var transport in transports.EnumerateArray()) Read(transport);
            return ids;

            void Read(JsonElement value)
            {
                if (value.ValueKind != JsonValueKind.Object) return;
                if (value.TryGetProperty("settings", out var settings) && settings.ValueKind == JsonValueKind.Object &&
                    settings.TryGetProperty("accountId", out var id) && id.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(id.GetString()))
                    ids.Add(id.GetString()!);
                if (provider == "cloudflare-workers-ai" && value.TryGetProperty("baseUrl", out var baseUrl) && baseUrl.ValueKind == JsonValueKind.String &&
                    Uri.TryCreate(baseUrl.GetString(), UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.IdnHost == "api.cloudflare.com")
                {
                    var match = Regex.Match(uri.AbsolutePath, @"^/client/v4/accounts/([a-fA-F0-9]{32})/ai(?:/|$)", RegexOptions.CultureInvariant);
                    if (match.Success) ids.Add(match.Groups[1].Value);
                }
            }
        }
        catch (Exception error) when (error is IOException or JsonException)
        { throw new InvalidOperationException("The inference credential's account binding cannot be read."); }
    }

    private static AccountDto RequireAccount(ConfigurationDto state, string id) =>
        state.Accounts.SingleOrDefault(a => a.Id == id) ?? throw new KeyNotFoundException("Account not found.");
}
