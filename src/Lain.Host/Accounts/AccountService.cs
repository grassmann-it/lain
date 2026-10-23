using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Operations;
using Lain.Host.Providers;

namespace Lain.Host.Accounts;

public sealed record AddApiAccountRequest(string Name, string Provider, string ApiKey, string BaseUrl, string? ProviderRouteId = null, Dictionary<string, string>? Settings = null);

public sealed class AccountService(ConfigurationRepository configuration, SecretStore secrets, ProviderCatalogRegistry? catalog = null)
{
    public AccountDto[] List() => configuration.Snapshot.Accounts.Select(a => a.CredentialKind == "codex-owned" ? Check(a.Id) : a).ToArray();
    public ConfigurationDto AddApi(long revision, AddApiAccountRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 120) throw new ArgumentException("Account name is required (maximum 120 characters).");
        if (string.IsNullOrWhiteSpace(input.Provider) || input.Provider.Length > 80 || input.Provider.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Invalid provider.");
        if (string.IsNullOrWhiteSpace(input.ApiKey)) throw new ArgumentException("API key is required.");
        ValidateEndpoint(input.BaseUrl);
        var registry = catalog ?? new ProviderCatalogRegistry();
        var plan = ProviderTransportValidation.Prepare(registry, input.Provider, input.ProviderRouteId, input.BaseUrl, input.Settings);
        var provider = registry.Find(input.Provider) is not null ? input.Provider : CanonicalProvider(input.Provider, input.BaseUrl);
        var id = CredentialId(provider, input.ApiKey);
        var sourceSection = input.Provider is "codex" or "claude" or "gemini" or "vertex" ? input.Provider + "-api-key" : "openai-compatibility";
        sourceSection = plan?.SourceSection ?? sourceSection;
        var credential = JsonSerializer.Serialize(new { kind = "api-key", sourceSection, provider, coreProvider = plan?.CoreProvider ?? input.Provider, apiKey = input.ApiKey, baseUrl = plan?.BaseUrl ?? input.BaseUrl, providerRouteId = input.ProviderRouteId, transportId = input.ProviderRouteId, settings = input.Settings });
        return configuration.Update(revision, state =>
        {
            var duplicate = FindDuplicate(state, provider, input.ApiKey, null);
            if (duplicate is not null)
            {
                if (plan is null) throw new ArgumentException("This credential already belongs to an account.");
                var existing = System.Text.Json.Nodes.JsonNode.Parse(secrets.ReadAccount(duplicate.Id))!.AsObject();
                var transports = existing["transports"] as System.Text.Json.Nodes.JsonArray;
                if (transports is null)
                {
                    transports = new System.Text.Json.Nodes.JsonArray(existing.DeepClone());
                    existing["transports"] = transports;
                }
                if (transports.Any(t => t?["providerRouteId"]?.GetValue<string>() == input.ProviderRouteId ||
                    (t?["providerRouteIds"] as System.Text.Json.Nodes.JsonArray)?.Any(value => value?.GetValue<string>() == input.ProviderRouteId) == true))
                    throw new ArgumentException("This credential already has the selected provider route.");
                var compatible = transports.OfType<System.Text.Json.Nodes.JsonObject>().FirstOrDefault(t =>
                    t["sourceSection"]?.GetValue<string>() == plan.SourceSection && t["coreProvider"]?.GetValue<string>() == plan.CoreProvider &&
                    t["baseUrl"]?.GetValue<string>() == plan.BaseUrl && System.Text.Json.Nodes.JsonNode.DeepEquals(t["settings"], System.Text.Json.Nodes.JsonNode.Parse(credential)?["settings"]));
                if (compatible is not null)
                {
                    var ids = compatible["providerRouteIds"] as System.Text.Json.Nodes.JsonArray ?? new System.Text.Json.Nodes.JsonArray();
                    if (compatible["providerRouteIds"] is null) compatible["providerRouteIds"] = ids;
                    ids.Add(input.ProviderRouteId);
                }
                else
                {
                    if (transports.Any(t => t?["sourceSection"]?.GetValue<string>() == plan.SourceSection &&
                        t?["coreProvider"]?.GetValue<string>() == plan.CoreProvider && t?["baseUrl"]?.GetValue<string>() == plan.BaseUrl))
                        throw new ArgumentException("This credential already has the endpoint with different provider settings.");
                    transports.Add(System.Text.Json.Nodes.JsonNode.Parse(credential));
                }
                secrets.WriteAccount(duplicate.Id, existing.ToJsonString());
                return state with { Accounts = state.Accounts.Select(a => a.Id == duplicate.Id ? a with { State = "unverified" } : a).ToArray() };
            }
            secrets.WriteAccount(id, credential);
            return state with { Accounts = [.. state.Accounts, new(id, input.Name.Trim(), provider, true, "api-key", "unverified")] };
        });
    }
    public ConfigurationDto ReplaceApiCredential(long revision, string id, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("API key is required.");
        return configuration.Update(revision, state =>
        {
            var account = state.Accounts.SingleOrDefault(a => a.Id == id) ?? throw new KeyNotFoundException("Account not found.");
            if (account.CredentialKind != "api-key") throw new InvalidOperationException("Only API key accounts support this operation.");
            if (FindDuplicate(state, account.Provider, apiKey, id) is not null) throw new ArgumentException("This credential already belongs to an account.");
            var json = System.Text.Json.Nodes.JsonNode.Parse(secrets.ReadAccount(id))!.AsObject();
            json["apiKey"] = apiKey;
            if (json["transports"] is System.Text.Json.Nodes.JsonArray transports)
                foreach (var transport in transports.OfType<System.Text.Json.Nodes.JsonObject>()) transport["apiKey"] = apiKey;
            secrets.WriteAccount(id, json.ToJsonString());
            return state with { Accounts = state.Accounts.Select(a => a.Id == id ? a with { State = "unverified" } : a).ToArray() };
        });
    }
    private AccountDto? FindDuplicate(ConfigurationDto state, string provider, string key, string? except)
    {
        foreach (var account in state.Accounts.Where(a => a.Id != except && a.CredentialKind == "api-key"))
        {
            using var document = JsonDocument.Parse(secrets.ReadAccount(account.Id));
            if (CanonicalProvider(account.Provider, document.RootElement.TryGetProperty("baseUrl", out var endpoint) ? endpoint.GetString() : null) == provider && document.RootElement.GetProperty("apiKey").GetString() == key) return account;
        }
        return null;
    }
    public ConfigurationDto SetEnabled(long revision, string id, bool enabled) => configuration.Update(revision, state =>
    {
        RequireAccount(state, id);
        return state with { Accounts = state.Accounts.Select(a => a.Id == id ? a with { Enabled = enabled } : a).ToArray() };
    });
    public ConfigurationDto Remove(long revision, string id)
    {
        // Historical usage keeps the stable identity; active routes lose this account.
        return configuration.Update(revision, state =>
        {
            RequireAccount(state, id);
            if (state.Routes.Any(r => r.AccountIds.Contains(id)))
                throw new InvalidOperationException("Remove this account from active routes before removing it.");
            // Credentials are retained privately for in-flight requests and explicit cleanup.
            return state with { Accounts = state.Accounts.Where(a => a.Id != id).ToArray() };
        });
    }
    public AccountDto Check(string id)
    {
        var account = configuration.Snapshot.Accounts.SingleOrDefault(a => a.Id == id) ?? throw new KeyNotFoundException("Account not found.");
        if (!secrets.HasAccount(id)) return account with { State = "missing-credential" };
        if (account.CredentialKind == "codex-owned")
        {
            try
            {
                using var own = JsonDocument.Parse(secrets.ReadAccount(id));
                if (own.RootElement.TryGetProperty("expiresAt", out var expiry) && DateTimeOffset.TryParse(expiry.GetString(), out var expires)) account = account with { ExpiresAt = expires };
            }
            catch (Exception error) when (error is IOException or JsonException or InvalidDataException) { return account with { State = "missing-credential" }; }
        }
        return account with { State = account.ExpiresAt <= DateTimeOffset.UtcNow ? "expired" : "unverified" };
    }
    internal static string CanonicalProvider(string provider, string? endpoint)
    {
        if (provider.Equals("commandcode", StringComparison.OrdinalIgnoreCase) || provider.Equals("command-code", StringComparison.OrdinalIgnoreCase)) return "commandcode";
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            var host = uri.IdnHost.ToLowerInvariant();
            if (host == "commandcode.ai" || host.EndsWith(".commandcode.ai", StringComparison.Ordinal)) return "commandcode";
            if (host == "kimi.ai" || host.EndsWith(".kimi.ai", StringComparison.Ordinal)) return "kimi";
            if (host == "z.ai" || host.EndsWith(".z.ai", StringComparison.Ordinal)) return "zai";
            if (host == "xiaomimimo.com" || host.EndsWith(".xiaomimimo.com", StringComparison.Ordinal)) return "xiaomi-mimo";
            // Direct API section names describe the protocol, not the upstream company.
            if (provider is "codex" or "claude" or "gemini" or "vertex")
            {
                if (provider == "codex" && (host == "api.openai.com" || host == "chatgpt.com")) return "openai";
                if (provider == "claude" && host == "api.anthropic.com") return "anthropic";
                return host;
            }
        }
        return provider;
    }
    internal static string CredentialId(string provider, string credential) => "acct-" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(provider + "\0" + credential))).ToLowerInvariant()[..32];
    internal static void ValidateEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Endpoint must be an HTTP(S) URL without credentials, query or fragment.");
        if (uri.Scheme == "http" && !uri.IsLoopback) throw new ArgumentException("Remote endpoints require HTTPS.");
    }
    private static void RequireAccount(ConfigurationDto state, string id)
    { if (!state.Accounts.Any(a => a.Id == id)) throw new KeyNotFoundException("Account not found."); }
}

/// <summary>Adapters must be explicitly qualified; an external snapshot is never a login adapter.</summary>
public interface IQualifiedLoginAdapter
{
    string Provider { get; }
    Task<LoginDto> BeginAsync(string operationId, CancellationToken ct);
    Task<LoginDto> StatusAsync(string operationId, CancellationToken ct);
    Task CancelAsync(string operationId, CancellationToken ct);
}

public sealed class LoginOperationService(IEnumerable<IQualifiedLoginAdapter>? qualifiedAdapters = null)
{
    private readonly Dictionary<string, IQualifiedLoginAdapter> adapters = (qualifiedAdapters ?? []).ToDictionary(a => a.Provider, StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, LoginDto> operations = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> operationGates = new();
    public async Task<LoginDto> BeginAsync(string provider, CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString("N");
        operationGates[id] = new(1, 1);
        if (!adapters.TryGetValue(provider, out var adapter))
        {
            var unsupported = new LoginDto(id, provider, "unsupported", Error: "New login is unavailable until this provider is qualified.");
            operations[id] = unsupported;
            return unsupported;
        }
        var login = await adapter.BeginAsync(id, ct);
        Validate(login, id, provider);
        operations[id] = login;
        return login;
    }
    public async Task<LoginDto> StatusAsync(string id, CancellationToken ct = default)
    {

        if (!operationGates.TryGetValue(id, out var gate)) throw new KeyNotFoundException("Login operation not found.");
        await gate.WaitAsync(ct);
        try
        {
            if (!operations.TryGetValue(id, out var operation)) throw new KeyNotFoundException("Login operation not found.");
            if (operation.State is "unsupported" or "cancelled" or "completed" or "failed") return operation;
            var login = await adapters[operation.Provider].StatusAsync(id, ct);
            Validate(login, id, operation.Provider);
            operations[id] = login;
            return login;
        }
        finally { gate.Release(); }
    }
    public async Task<LoginDto> CancelAsync(string id, CancellationToken ct = default)
    {

        if (!operationGates.TryGetValue(id, out var gate)) throw new KeyNotFoundException("Login operation not found.");
        await gate.WaitAsync(ct);
        try
        {
            if (!operations.TryGetValue(id, out var operation)) throw new KeyNotFoundException("Login operation not found.");
            if (operation.State is "unsupported" or "cancelled" or "completed" or "failed") return operation;
            await adapters[operation.Provider].CancelAsync(id, ct);
            var cancelled = operation with { State = "cancelled", Url = null };
            operations[id] = cancelled;
            return cancelled;
        }
        finally { gate.Release(); }
    }
    private static void Validate(LoginDto login, string id, string provider)
    {
        if (login.Id != id || login.Provider != provider) throw new InvalidDataException("Invalid login operation identity.");
        if (login.Url is { } url && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)))
            throw new InvalidDataException("Invalid login authorization URL.");
    }
}
