using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lain.Core;
using Lain.Host.Operations;
using Lain.Host.Providers;

namespace Lain.Host.Accounts;

public sealed record AddApiAccountRequest(string Name, string Provider, string ApiKey, string BaseUrl, string? ProviderRouteId = null, Dictionary<string, string>? Settings = null,
    string? ProviderConnectionId = null, string[]? SelectedModelRouteIds = null)
{
    public override string ToString() => "AddApiAccountRequest [redacted]";
}

public sealed class AccountService(ConfigurationRepository configuration, SecretStore secrets, ProviderCatalogRegistry? catalog = null)
{
    public AccountDto[] List() => configuration.Snapshot.Accounts.Select(a => a.CredentialKind == "codex-owned" ? Check(a.Id) : a).ToArray();
    public ConfigurationDto AddApi(long revision, AddApiAccountRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 120 || input.Name.Any(char.IsControl))
            throw new ArgumentException("Account name is required (maximum 120 characters).");
        if (string.IsNullOrWhiteSpace(input.Provider) || input.Provider.Length > 80 || input.Provider.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Invalid provider.");
        if (string.IsNullOrWhiteSpace(input.ApiKey) || input.ApiKey.Any(char.IsWhiteSpace) || input.ApiKey.Any(char.IsControl))
            throw new ArgumentException("A valid API key is required.");
        if (input.ProviderConnectionId is not null && input.ProviderRouteId is not null)
            throw new ArgumentException("Choose a provider connection or the legacy model route, not both.");
        if (input.SelectedModelRouteIds is not null && input.ProviderConnectionId is null)
            throw new ArgumentException("Model selection requires a provider connection.");
        ValidateEndpoint(input.BaseUrl);
        var registry = catalog ?? new ProviderCatalogRegistry();
        var settings = input.Settings is null ? null : new Dictionary<string, string>(input.Settings, StringComparer.Ordinal);
        var selectedIds = input.SelectedModelRouteIds?.ToArray() ?? [];
        if (selectedIds.Distinct(StringComparer.Ordinal).Count() != selectedIds.Length)
            throw new ArgumentException("Select each model template only once.");
        ProviderTransportPlan? plan;
        string[] permittedIds;
        ProviderRouteDescriptorDto[] selectedTemplates = [];
        if (input.ProviderConnectionId is { } connectionId)
        {
            var connection = registry.FindConnection(input.Provider, connectionId)
                ?? throw new ArgumentException("Unknown provider connection.");
            settings ??= new(StringComparer.Ordinal);
            if (!settings.TryGetValue("billingPlan", out var billing))
            {
                if (connection.BillingPlanIds.Length != 1) throw new ArgumentException("Choose the connection's billing plan.");
                settings["billingPlan"] = billing = connection.BillingPlanIds[0];
            }
            if (!connection.BillingPlanIds.Contains(billing, StringComparer.Ordinal))
                throw new ArgumentException("Billing plan does not belong to the selected connection.");
            var prepared = new List<(ProviderRouteDescriptorDto Route, ProviderTransportPlan Plan)>();
            foreach (var templateId in connection.ProviderRouteIds)
            {
                var template = registry.FindRoute(input.Provider, templateId)!;
                if (!template.BillingPlanIds.Contains(billing, StringComparer.Ordinal)) continue;
                try
                {
                    var candidate = ProviderTransportValidation.Prepare(registry, input.Provider, templateId, input.BaseUrl, settings)!;
                    prepared.Add((template, candidate));
                }
                catch (ArgumentException)
                {
                    // A shared endpoint can contain templates requiring additional
                    // privacy consent or a different entitlement. Never broaden access.
                    if (selectedIds.Contains(templateId, StringComparer.Ordinal))
                        throw new ArgumentException("A selected model is not permitted by the connection settings.");
                }
            }
            if (prepared.Count == 0) throw new ArgumentException("No model template is permitted by this endpoint, billing plan and settings.");
            var chosen = prepared[0].Plan;
            plan = chosen;
            if (prepared.Any(p => p.Plan.SourceSection != chosen.SourceSection || p.Plan.CoreProvider != chosen.CoreProvider ||
                p.Plan.BaseUrl != chosen.BaseUrl || p.Plan.Protocol != chosen.Protocol ||
                p.Plan.Headers.Count != chosen.Headers.Count || p.Plan.Headers.Any(h => !chosen.Headers.TryGetValue(h.Key, out var value) || value != h.Value)))
                throw new ArgumentException("The connection contains incompatible private transports.");
            permittedIds = prepared.Select(p => p.Route.Id).Order(StringComparer.Ordinal).ToArray();
            if (selectedIds.Any(id => !permittedIds.Contains(id, StringComparer.Ordinal)))
                throw new ArgumentException("A selected model does not belong to the permitted connection scope.");
            selectedTemplates = selectedIds.Select(id => prepared.Single(p => p.Route.Id == id).Route).ToArray();
        }
        else
        {
            plan = ProviderTransportValidation.Prepare(registry, input.Provider, input.ProviderRouteId, input.BaseUrl, settings);
            permittedIds = input.ProviderRouteId is { } routeId ? [routeId] : [];
        }
        var provider = registry.Find(input.Provider) is not null ? input.Provider : CanonicalProvider(input.Provider, input.BaseUrl);
        var id = CredentialId(provider, input.ApiKey);
        var sourceSection = plan?.SourceSection ?? (input.Provider is "codex" or "claude" or "gemini" or "vertex" ? input.Provider + "-api-key" : "openai-compatibility");
        var credential = JsonSerializer.Serialize(new { kind = "api-key", sourceSection, provider, coreProvider = plan?.CoreProvider ?? input.Provider,
            apiKey = input.ApiKey, baseUrl = plan?.BaseUrl ?? input.BaseUrl, providerRouteId = permittedIds.FirstOrDefault(),
            providerRouteIds = permittedIds, providerConnectionId = input.ProviderConnectionId,
            transportId = input.ProviderConnectionId ?? input.ProviderRouteId, settings });
        return configuration.Update(revision, state =>
        {
            var duplicate = FindDuplicate(state, provider, input.ApiKey, null);
            // Key replacement preserves account IDs, so an old key's derived ID
            // can still be occupied by a different current key. Retained private
            // credentials also remain reserved after public account removal.
            if (duplicate is null && (state.Accounts.Any(a => a.Id == id) || secrets.HasAccount(id)))
                throw new ArgumentException("The derived account identity is already in use. Existing credentials were not changed.");
            var account = duplicate ?? new AccountDto(id, input.Name.Trim(), provider, true, "api-key", "unverified");
            var privateJson = duplicate is null ? credential : MergeCredential(secrets.ReadAccount(account.Id), credential, plan, input.ProviderConnectionId is not null);
            var next = state with { Accounts = duplicate is null ? [.. state.Accounts, account]
                : state.Accounts.Select(a => a.Id == account.Id ? a with { State = "unverified" } : a).ToArray() };
            var routes = next.Routes.ToList();
            foreach (var template in selectedTemplates)
            {
                if (routes.Any(r => r.Provider == provider && r.ProviderRouteId == template.Id && r.AccountIds.SequenceEqual(new[] { account.Id }))) continue;
                var routeId = "route-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account.Id + "\0" + template.Id))).ToLowerInvariant()[..32];
                if (routes.Any(r => r.Id == routeId)) throw new ArgumentException("Model route identity is already in use.");
                var alias = template.Model;
                for (var suffix = 2; routes.Any(r => string.Equals(r.Alias, alias, StringComparison.OrdinalIgnoreCase)); suffix++)
                {
                    var tail = "-" + suffix;
                    alias = template.Model[..Math.Min(template.Model.Length, 256 - tail.Length)] + tail;
                }
                routes.Add(new(routeId, alias, template.Model, provider, [account.Id], Enabled: account.Enabled, ProviderRouteId: template.Id));
            }
            next = next with { Routes = routes.ToArray() };
            new ProviderCostPolicy(registry).ValidateConfiguration(next);
            // Every shape, entitlement and route check above precedes the private
            // write and executes under the same revision gate as public persistence.
            secrets.WriteAccount(account.Id, privateJson);
            return next;
        });
    }

    private static string MergeCredential(string stored, string incoming, ProviderTransportPlan? plan, bool connection)
    {
        if (plan is null) throw new ArgumentException("This credential already belongs to an account.");
        var existing = JsonNode.Parse(stored)!.AsObject();
        var added = JsonNode.Parse(incoming)!.AsObject();
        var transports = existing["transports"] as JsonArray;
        if (transports is null)
        {
            transports = new JsonArray(existing.DeepClone());
            existing["transports"] = transports;
        }
        static string[] Routes(JsonNode node) => (node["providerRouteId"]?.GetValue<string>() is { } primary ? new[] { primary } : [])
            .Concat((node["providerRouteIds"] as JsonArray)?.Select(v => v!.GetValue<string>()) ?? []).Distinct(StringComparer.Ordinal).ToArray();
        var requested = Routes(added);
        if (!connection && transports.Any(t => requested.Intersect(Routes(t!), StringComparer.Ordinal).Any()))
            throw new ArgumentException("This credential already has the selected provider route.");
        var endpointMatch = transports.OfType<JsonObject>().Where(t => t["sourceSection"]?.GetValue<string>() == plan.SourceSection &&
            t["coreProvider"]?.GetValue<string>() == plan.CoreProvider && t["baseUrl"]?.GetValue<string>() == plan.BaseUrl).ToArray();
        var compatible = endpointMatch.FirstOrDefault(t => JsonNode.DeepEquals(t["settings"], added["settings"]));
        if (compatible is null)
        {
            if (endpointMatch.Length > 0) throw new ArgumentException("This credential already has the endpoint with different provider settings.");
            transports.Add(added);
        }
        else
        {
            compatible["providerRouteIds"] = new JsonArray(Routes(compatible).Concat(requested).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
            if (connection) compatible["providerConnectionId"] = added["providerConnectionId"]?.DeepClone();
        }
        return existing.ToJsonString();
    }
    public ConfigurationDto ReplaceApiCredential(long revision, string id, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Any(char.IsWhiteSpace) || apiKey.Any(char.IsControl))
            throw new ArgumentException("A valid API key is required.");
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
