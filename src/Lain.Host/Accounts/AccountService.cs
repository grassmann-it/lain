using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lain.Core;
using Lain.Host.Operations;
using Lain.Host.Providers;
using Lain.Host.Accounts.Credentials;

namespace Lain.Host.Accounts;

public sealed record AddApiAccountRequest(string Name, string Provider, string ApiKey, string BaseUrl, string? ProviderRouteId = null, Dictionary<string, string>? Settings = null,
    string? ProviderConnectionId = null, string[]? SelectedModelRouteIds = null)
{
    public override string ToString() => "AddApiAccountRequest [redacted]";
}

public sealed class AccountService(ConfigurationRepository configuration, SecretStore secrets, ProviderCatalogRegistry? catalog = null)
{
    public LoginOperationContext ReserveConnect(long revision, ConnectAccountRequest request, string operationId, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(request);
        request = request with { Provider = CanonicalProviderId(request.Provider) };
        var state = configuration.Snapshot;
        if (state.Revision != revision) throw new RevisionConflictException(revision, state.Revision);
        if (string.IsNullOrWhiteSpace(request.Provider) || request.Provider.Length > 80 || request.Provider.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Invalid provider.");
        if (string.IsNullOrWhiteSpace(request.Method) || request.Method.Length > 100 || request.Method.Any(char.IsControl))
            throw new ArgumentException("A connection method is required.");
        if (request.Purposes.Length == 0 || request.Purposes.Any(p => p is not ("inference" or "quota" or "cost" or "models")))
            throw new ArgumentException("Choose supported connection purposes.");
        if (request.Name is { } name && (string.IsNullOrWhiteSpace(name) || name.Length > 120 || name.Any(char.IsControl)))
            throw new ArgumentException("Invalid account name.");
        if (request.Binding is { } binding && CanonicalProviderId(binding.Provider) != request.Provider) throw new ArgumentException("Resource provider does not match the connection.");
        var target = request.AccountId is null ? null : state.Accounts.SingleOrDefault(a => a.Id == request.AccountId)
            ?? throw new KeyNotFoundException("Account not found.");
        if (target is not null)
        {
            if (CanonicalProviderId(target.Provider) != request.Provider) throw new ArgumentException("Reconnect provider does not match the account.");
            if (request.ExpectedGeneration is { } generation && generation != target.Generation)
                throw new InvalidOperationException("Account generation changed; reconnect again.");
            EnsureCompatibleBinding(target.Binding, request.Binding);
        }
        return new(operationId, target?.Id ?? "acct-" + Guid.NewGuid().ToString("N"), revision, target?.Generation ?? 0,
            request with { Binding = request.Binding ?? target?.Binding, Purposes = request.Purposes.Distinct(StringComparer.Ordinal).ToArray() }, expiresAt);
    }

    public ConfigurationDto CommitPrepared(LoginOperationContext context, CredentialHandleDto handle, Func<bool>? mayCommit = null)
    {
        // The immutable secret already exists. Only this short local CAS publishes it.
        return configuration.Update(context.Revision, state =>
        {
            if (mayCommit is not null && !mayCommit()) throw new OperationCanceledException("Connect operation is no longer active.");
            var existing = state.Accounts.SingleOrDefault(a => a.Id == context.AccountId);
            if (context.Request.AccountId is not null && (existing is null || existing.Generation != context.AccountGeneration))
                throw new InvalidOperationException("Account was removed or changed during connection.");
            if (context.Request.AccountId is null && existing is not null) throw new InvalidOperationException("Reserved account identity is already in use.");
            if (CanonicalProviderId(handle.Binding.Provider) != CanonicalProviderId(context.Request.Provider) || context.Request.Purposes.Any(p => !handle.Purposes.Contains(p, StringComparer.Ordinal)))
                throw new InvalidDataException("Prepared credential does not grant the requested account purposes.");
            EnsureBoundResult(context.Request.Binding, handle.Binding);
            EnsureBoundResult(existing?.Binding, handle.Binding);
            var capabilities = EffectiveCapabilities(existing).Where(c => !context.Request.Purposes.Contains(c.Purpose, StringComparer.Ordinal))
                .Concat(context.Request.Purposes.Select(p => new AccountCapabilityDto(p, "unverified", handle.Id))).ToArray();
            var handles = (existing?.Credentials ?? []).Where(h => h.Id != handle.Id).Append(handle).Where(h => capabilities.Any(c => c.CredentialId == h.Id)).ToArray();
            var account = (existing ?? new AccountDto(context.AccountId, context.Request.Name?.Trim() ?? context.Request.Provider,
                context.Request.Provider, true, handle.Owner == "static-key" ? "api-key" : handle.Owner, "unverified")) with
            {
                Generation = checked((existing?.Generation ?? 0) + 1), Binding = MergeBinding(existing?.Binding, handle.Binding),
                Credentials = handles, Capabilities = capabilities, State = "unverified",
                ExpiresAt = capabilities.Any(c => c.Purpose == "inference" && c.CredentialId == handle.Id) ? handle.ExpiresAt : existing?.ExpiresAt
            };
            return state with { Accounts = existing is null ? [.. state.Accounts, account] : state.Accounts.Select(a => a.Id == account.Id ? account : a).ToArray() };
        });
    }

    public ConfigurationDto Connect(long revision, ConnectAccountRequest request, PreparedLoginCredential credential)
    {
        var context = ReserveConnect(revision, request, "connect-" + Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddMinutes(10));
        var handle = secrets.Generations.Prepare(context.AccountId, credential, context.OperationId);
        try { return CommitPrepared(context, handle); }
        catch { secrets.Generations.DiscardStaticPreparation(handle); throw; }
    }

    public ConfigurationDto ConnectCredential(long revision, ConnectCredentialRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Secret) || request.Secret.Length > 65536 || request.Secret.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
            throw new ArgumentException("A valid access credential is required.");
        if (request.Kind is not ("api-key" or "access-token-snapshot")) throw new ArgumentException("Unsupported static credential kind.");
        if (request.Kind == "access-token-snapshot" && (request.ExpiresAt is null || request.ExpiresAt <= DateTimeOffset.UtcNow))
            throw new ArgumentException("An access snapshot requires a future expiry.");
        if (request.BaseUrl is { } endpoint) ValidateEndpoint(endpoint);
        if (request.Settings?.Any(p => p.Key is not ("accountId" or "region" or "workspaceId" or "workspace_id" or "serviceDeploymentScope" or "service_deployment_scope" or "billingPlan" or "allowTrainingData")) == true)
            throw new ArgumentException("Unsupported provider setting. Submit secrets only in the private credential field.");
        var binding = (request.Connection.Binding ?? new ResourceBindingDto(request.Connection.Provider)) with { Provenance = "declared" };
        var json = request.Kind == "api-key"
            ? JsonSerializer.Serialize(new { kind = "api-key", apiKey = request.Secret, provider = request.Connection.Provider,
                baseUrl = request.BaseUrl, settings = request.Settings, sourceSection = "openai-compatibility", coreProvider = request.Connection.Provider })
            : JsonSerializer.Serialize(new { kind = "access-token-snapshot", accessToken = request.Secret, expiresAt = request.ExpiresAt,
                provider = request.Connection.Provider, accountId = binding.Subject, baseUrl = request.BaseUrl, settings = request.Settings });
        return Connect(revision, request.Connection, new(request.Kind == "api-key" ? "static-key" : "external-snapshot",
            request.Connection.Purposes, binding, json, request.ExpiresAt));
    }

    public ConfigurationDto ObserveBinding(string accountId, long expectedGeneration, ResourceBindingDto binding)
    {
        var snapshot = configuration.Snapshot;
        return configuration.Update(snapshot.Revision, state =>
        {
            var account = state.Accounts.SingleOrDefault(a => a.Id == accountId) ?? throw new KeyNotFoundException("Account not found.");
            if (account.Generation != expectedGeneration) throw new InvalidOperationException("Credential generation changed before identity observation.");
            EnsureCompatibleBinding(account.Binding, binding);
            return state with { Accounts = state.Accounts.Select(a => a.Id == accountId ? a with {
                Binding = MergeBinding(a.Binding, binding), Generation = checked(a.Generation + 1) } : a).ToArray() };
        });
    }

    internal static AccountCapabilityDto[] EffectiveCapabilities(AccountDto? account) => account?.Capabilities ??
        account?.Credentials?.SelectMany(h => h.Purposes.Select(p => new AccountCapabilityDto(p, "unverified", h.Id))).ToArray() ??
        (account?.CredentialKind is "api-key" or "codex-owned" or "codex-access-snapshot"
            ? [new("inference", "unverified"), new("quota", "unverified")] : []);

    internal static void EnsureCompatibleBinding(ResourceBindingDto? expected, ResourceBindingDto? actual)
    {
        if (expected is null || actual is null) return;
        if (CanonicalProviderId(expected.Provider) != CanonicalProviderId(actual.Provider) || Different(expected.Subject, actual.Subject) || Different(expected.Product, actual.Product) ||
            Different(expected.Region, actual.Region) || Different(expected.ResourceType, actual.ResourceType) || Different(expected.ResourceId, actual.ResourceId))
            throw new ArgumentException("The credential belongs to a different provider, identity, product, region or resource.");
        static bool Different(string? a, string? b) => a is not null && b is not null && !StringComparer.Ordinal.Equals(a, b);
    }
    internal static void EnsureBoundResult(ResourceBindingDto? expected, ResourceBindingDto actual)
    {
        EnsureCompatibleBinding(expected, actual);
        if (expected is null) return;
        if (Missing(expected.Subject, actual.Subject) || Missing(expected.Product, actual.Product) || Missing(expected.Region, actual.Region) ||
            Missing(expected.ResourceType, actual.ResourceType) || Missing(expected.ResourceId, actual.ResourceId))
            throw new ArgumentException("The new credential has not established the existing account identity and resource binding.");
        if (VerifiedProvenance(expected.Provenance) && !VerifiedProvenance(actual.Provenance))
            throw new ArgumentException("Reconnect must verify the existing account identity before replacing its credential.");
        static bool Missing(string? previous, string? current) => previous is not null && current is null;
    }
    private static bool VerifiedProvenance(string provenance) => provenance is "verified" or "observed" or "provider-discovery";
    private static ResourceBindingDto MergeBinding(ResourceBindingDto? prior, ResourceBindingDto next) => prior is null ? next :
        next with { Provider = prior.Provider, Subject = next.Subject ?? prior.Subject, Product = next.Product ?? prior.Product, Region = next.Region ?? prior.Region,
            ResourceType = next.ResourceType ?? prior.ResourceType, ResourceId = next.ResourceId ?? prior.ResourceId,
            Provenance = next.Provenance };

    public AccountDto[] List() => configuration.Snapshot.Accounts.Select(a => Check(a.Id)).ToArray();
    public ConfigurationDto AddApi(long revision, AddApiAccountRequest input) => AddApiCore(revision, input, null, null);

    public ConfigurationDto AttachModels(long revision, string accountId, long expectedGeneration, string connectionId,
        string[] templateIds, Dictionary<string, string>? settings = null)
    {
        var state = configuration.Snapshot;
        if (state.Revision != revision) throw new RevisionConflictException(revision, state.Revision);
        var account = state.Accounts.SingleOrDefault(a => a.Id == accountId) ?? throw new KeyNotFoundException("Account not found.");
        if (account.Generation != expectedGeneration) throw new InvalidOperationException("Account changed before model attachment.");
        if (templateIds.Length == 0) throw new ArgumentException("Choose at least one model template.");
        var provider = CanonicalProviderId(account.Provider);
        var registry = catalog ?? new ProviderCatalogRegistry();
        var connection = registry.FindConnection(provider, connectionId) ?? throw new ArgumentException("Unknown provider connection.");
        using var document = JsonDocument.Parse(secrets.ReadAccount(accountId));
        if (document.RootElement.GetProperty("kind").GetString() != "api-key") throw new InvalidOperationException("This model attachment requires an API-key inference capability.");
        var apiKey = document.RootElement.GetProperty("apiKey").GetString() ?? throw new InvalidDataException("Inference key is unavailable.");
        var combined = document.RootElement.TryGetProperty("settings", out var saved) && saved.ValueKind == JsonValueKind.Object
            ? saved.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String).ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var setting in settings ?? []) combined[setting.Key] = setting.Value;
        if (settings?.ContainsKey("billingPlan") != true)
        {
            if (connection.BillingPlanIds.Length == 1) combined["billingPlan"] = connection.BillingPlanIds[0];
            else if (combined.TryGetValue("billingPlan", out var previousPlan) && !connection.BillingPlanIds.Contains(previousPlan, StringComparer.Ordinal))
                combined.Remove("billingPlan");
        }
        var endpoint = document.RootElement.TryGetProperty("baseUrl", out var url) && url.ValueKind == JsonValueKind.String
            ? url.GetString()! : connection.BaseUrl;
        if (provider == "cloudflare-workers-ai" && combined.TryGetValue("accountId", out var cloudflareId))
            endpoint = GatewayRouteValidation.CloudflareBaseUrl(cloudflareId);
        return AddApiCore(revision, new(account.Name, provider, apiKey, endpoint, Settings: combined,
            ProviderConnectionId: connectionId, SelectedModelRouteIds: templateIds), accountId, expectedGeneration);
    }

    private ConfigurationDto AddApiCore(long revision, AddApiAccountRequest input, string? targetAccountId, long? expectedGeneration)
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
        if (input.ProviderConnectionId is { } emptyConnectionId && selectedIds.Length == 0)
        {
            var connection = registry.FindConnection(input.Provider, emptyConnectionId)
                ?? throw new ArgumentException("Unknown provider connection.");
            // Credentials can be saved before choosing models or a commercial plan.
            // A concrete model selection still runs the full transport/entitlement validator below.
            var section = connection.Protocol switch { "anthropic-messages" => "claude-api-key", "gemini" => "gemini-api-key", _ => "openai-compatibility" };
            plan = new(section, input.Provider, input.BaseUrl, connection.Protocol, new Dictionary<string, string>());
            permittedIds = [];
        }
        else if (input.ProviderConnectionId is { } connectionId)
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
            plan = input.ProviderRouteId is null ? null : ProviderTransportValidation.Prepare(registry, input.Provider, input.ProviderRouteId, input.BaseUrl, settings);
            permittedIds = input.ProviderRouteId is { } routeId ? [routeId] : [];
        }
        var provider = registry.Find(input.Provider) is not null ? input.Provider : CanonicalProvider(input.Provider, input.BaseUrl);
        var id = "acct-" + Guid.NewGuid().ToString("N");
        var sourceSection = plan?.SourceSection ?? (input.Provider is "codex" or "claude" or "gemini" or "vertex" ? input.Provider + "-api-key" : "openai-compatibility");
        var credential = JsonSerializer.Serialize(new { kind = "api-key", sourceSection, provider, coreProvider = plan?.CoreProvider ?? input.Provider,
            apiKey = input.ApiKey, baseUrl = plan?.BaseUrl ?? input.BaseUrl, providerRouteId = permittedIds.FirstOrDefault(),
            providerRouteIds = permittedIds, providerConnectionId = input.ProviderConnectionId,
            transportId = input.ProviderConnectionId ?? input.ProviderRouteId, settings });
        return configuration.Update(revision, state =>
        {
            var duplicate = targetAccountId is null ? FindDuplicate(state, provider, input.ApiKey, null)
                : state.Accounts.SingleOrDefault(a => a.Id == targetAccountId) ?? throw new KeyNotFoundException("Account not found.");
            if (targetAccountId is not null)
            {
                if (duplicate!.Generation != expectedGeneration || CanonicalProviderId(duplicate.Provider) != CanonicalProviderId(provider))
                    throw new InvalidOperationException("Target account changed before model attachment.");
                using var current = JsonDocument.Parse(secrets.ReadAccount(duplicate.Id));
                if (!current.RootElement.TryGetProperty("apiKey", out var currentKey) || currentKey.GetString() != input.ApiKey)
                    throw new InvalidOperationException("Inference credential changed before model attachment.");
            }
            if (duplicate is not null && input.ProviderConnectionId is null && input.ProviderRouteId is null)
                throw new ArgumentException("This credential already belongs to an account.");
            // Key replacement preserves account IDs, so an old key's derived ID
            // can still be occupied by a different current key. Retained private
            // credentials also remain reserved after public account removal.
            if (duplicate is null && (state.Accounts.Any(a => a.Id == id) || secrets.HasAccount(id)))
                throw new ArgumentException("The derived account identity is already in use. Existing credentials were not changed.");
            var account = duplicate ?? new AccountDto(id, input.Name.Trim(), provider, true, "api-key", "unverified");
            var privateJson = duplicate is null ? credential : permittedIds.Length == 0 ? secrets.ReadAccount(account.Id) : MergeCredential(secrets.ReadAccount(account.Id), credential, plan, input.ProviderConnectionId is not null);
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
            return PublishStaticReference(next, account.Id, privateJson, sameCredential: duplicate is not null);
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
        var unconfigured = endpointMatch.FirstOrDefault(t => Routes(t).Length == 0 && t["sourceConfig"] is null &&
            (t["settings"] is not JsonObject previousSettings || previousSettings.All(p => p.Key == "billingPlan" || JsonNode.DeepEquals(p.Value, added["settings"]?[p.Key]))));
        if (compatible is null && unconfigured is not null)
        {
            // Optional model setup qualifies the generic saved endpoint in place within this new immutable generation.
            foreach (var property in added.ToArray()) unconfigured[property.Key] = property.Value?.DeepClone();
            compatible = unconfigured;
        }
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
            return PublishStaticReference(state, id, json.ToJsonString());
        });
    }
    private ConfigurationDto PublishStaticReference(ConfigurationDto state, string id, string privateJson, bool sameCredential = false)
    {
        var account = state.Accounts.Single(a => a.Id == id);
        var binding = account.Binding ?? new ResourceBindingDto(account.Provider);
        if (!sameCredential && VerifiedProvenance(binding.Provenance))
            throw new InvalidOperationException("Reconnect through an identity-verifying method before replacing this confirmed credential.");
        var currentCapabilities = EffectiveCapabilities(account);
        var inference = currentCapabilities.SingleOrDefault(c => c.Purpose == "inference");
        var quota = currentCapabilities.SingleOrDefault(c => c.Purpose == "quota");
        var replaceQuota = quota is null && account.Capabilities is null || quota is not null && quota.CredentialId == inference?.CredentialId;
        var purposes = replaceQuota ? new[] { "inference", "quota" } : ["inference"];
        var handle = secrets.Generations.Prepare(id, new("static-key", purposes, binding, privateJson));
        var capabilities = currentCapabilities.Where(c => !purposes.Contains(c.Purpose, StringComparer.Ordinal))
            .Concat(purposes.Select(p => new AccountCapabilityDto(p, "unverified", handle.Id))).ToArray();
        var handles = (account.Credentials ?? []).Append(handle).Where(h => capabilities.Any(c => c.CredentialId == h.Id)).ToArray();
        return state with { Accounts = state.Accounts.Select(a => a.Id == id ? a with { Generation = checked(a.Generation + 1),
            Binding = binding, Credentials = handles, Capabilities = capabilities, State = "unverified" } : a).ToArray() };
    }

    private AccountDto? FindDuplicate(ConfigurationDto state, string provider, string key, string? except)
    {
        foreach (var account in state.Accounts.Where(a => a.Id != except && a.CredentialKind == "api-key" && secrets.HasAccount(a.Id)))
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
        if (account.Credentials is { Length: > 0 })
        {
            var capabilities = EffectiveCapabilities(account).Select(capability =>
            {
                var handle = account.Credentials.SingleOrDefault(h => h.Id == capability.CredentialId && h.Purposes.Contains(capability.Purpose));
                if (handle is null)
                {
                    if (capability.CredentialId is null && secrets.HasAccount(account.Id))
                        return capability with { State = account.ExpiresAt <= DateTimeOffset.UtcNow ? "expired" : "unverified" };
                    return capability with { State = "missing-credential" };
                }
                try
                {
                    if (secrets.Generations.IsRetired(handle)) return capability with { State = "recovery-required" };
                    var stored = secrets.Generations.Read(account.Id, handle);
                    var expiresAt = handle.ExpiresAt;
                    if (handle.Owner == "core")
                    {
                        using var projection = JsonDocument.Parse(secrets.ProjectAccessCredential(stored.PrivateJson));
                        if (projection.RootElement.TryGetProperty("expiresAt", out var expiry) && DateTimeOffset.TryParse(expiry.GetString(), out var currentExpiry))
                            expiresAt = currentExpiry;
                    }
                    return capability with { State = expiresAt <= DateTimeOffset.UtcNow ? "expired" : capability.State };
                }
                catch (Exception error) when (error is IOException or JsonException or InvalidDataException)
                { return capability with { State = secrets.Generations.RestoreEpoch is not null && handle.Owner is not ("static-key" or "external-snapshot")
                    ? "recovery-required" : "missing-credential" }; }
            }).ToArray();
            return account with { Capabilities = capabilities, State = capabilities.Any(c => c.State is "unverified" or "available")
                ? "unverified" : capabilities.FirstOrDefault()?.State ?? "missing-credential" };
        }
        if (!secrets.HasAccount(id)) return account with { State = "missing-credential" };
        if (account.CredentialKind == "codex-owned")
        {
            try
            {
                using var own = JsonDocument.Parse(secrets.ReadAccount(id));
                if (own.RootElement.TryGetProperty("expiresAt", out var expiry) && DateTimeOffset.TryParse(expiry.GetString(), out var expires)) account = account with { ExpiresAt = expires };
            }
            catch (Exception error) when (error is IOException or JsonException or InvalidDataException)
            { return account with { State = secrets.Generations.RestoreEpoch is not null ? "recovery-required" : "missing-credential" }; }
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
    public static string CanonicalProviderId(string provider) => provider switch
    {
        "command-code" => "commandcode",
        "mimo" => "xiaomi-mimo",
        _ => provider
    };
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
