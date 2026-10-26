using Lain.Core;
using Lain.Host.Accounts.CliConnections;
using Lain.Host.Accounts.Monitoring;
using Lain.Host.Operations;
using Lain.Host.Providers;

namespace Lain.Host.Accounts;

/// <summary>Installed connection capabilities. Documentation never enables an unimplemented login.</summary>
public sealed class ConnectionMethodCatalog(ProviderCatalogRegistry catalog, Func<bool> gateway,
    Func<bool> coreAvailable, Func<bool> poeAvailable, Func<string, bool>? cloudAvailable = null,
    Func<bool>? mimoAvailable = null, Func<bool>? googleCliAvailable = null,
    Func<bool>? fireworksCliAvailable = null)
{
    private static readonly HashSet<string> DirectQuota = new(StringComparer.Ordinal)
        { "kilo", "requesty", "synthetic", "stepfun", "openrouter", "poe", "fireworks", "commandcode", "kimi", "zai", "deepseek" };

    public ConnectionMethodCatalogDto Snapshot(string? provider = null)
    {
        var result = new List<ProviderConnectionMethodDto>();
        foreach (var descriptor in catalog.Snapshot().Providers)
        {
            var connections = descriptor.Connections ?? [];
            for (var index = 0; index < connections.Length; index++)
            {
                var connection = connections[index];
                var fields = descriptor.Credentials.Where(c => !c.Secret &&
                    c.Id is "accountId" or "workspaceId" or "workspace_id" or "region" or "serviceDeploymentScope" or "service_deployment_scope")
                    .Select(c => new ConnectionResourceFieldDto(c.Id switch { "workspace_id" => "workspaceId", "service_deployment_scope" => "serviceDeploymentScope", _ => c.Id }, c.Label, c.Required, Help: c.Help)).ToArray();
                var hasDirectQuota = DirectQuota.Contains(descriptor.Id) &&
                    (descriptor.Id != "stepfun" || connection.BaseUrl.TrimEnd('/') == Quota.StepFunQuotaAdapter.ApiBaseUrl);
                var purposes = hasDirectQuota ? new[] { "inference", "models", "quota" } : ["inference", "models"];
                if (descriptor.Id == "groq" && gateway()) purposes = ["inference", "models", "quota"];
                result.Add(new(descriptor.Id, index == 0 ? "api-key" : "api-key-" + connection.Id,
                    connections.Length == 1 ? "API key" : connection.Name, "api-key", purposes, "available", false, true,
                    "api-key", hasDirectQuota
                        ? "Connect an existing key. Reported limits depend on this account and product; models are optional later."
                        : "Connect an existing inference key. This does not provide a measured remaining budget.",
                    connection.BaseUrl, fields, descriptor.DocumentationUrl, Recommended: descriptor.Id is not ("openrouter" or "kilo" or "nanogpt") && index == 0));
            }
            foreach (var option in MonitorAccessCatalog.Options(descriptor.Id).Where(o => o.SecretRequired))
            {
                var project = descriptor.Id is "gemini-api" or "groq";
                result.Add(new(descriptor.Id, "monitor-" + option.Kind, option.Name, "monitor-token", ["quota"], "available",
                    false, true, option.Kind, option.Description, RequiredSettings:
                    option.ResourceRequired ? [new(project ? "projectId" : "accountId", option.ResourceLabel, Help: option.ResourceHelp)] : [],
                    DocumentationUrl: option.DocumentationUrl));
            }
        }
        AddKey("commandcode", "https://api.commandcode.ai/v1", "https://commandcode.ai/docs");
        AddKey("kimi", "https://api.kimi.com/coding/v1", "https://www.kimi.com/code/docs/");
        AddKey("zai", "https://api.z.ai/api/coding/paas/v4", "https://docs.z.ai/");
        AddKey("xiaomi-mimo", "https://api.xiaomimimo.com/v1", "https://platform.xiaomimimo.com/");
        result.Add(new("codex", "codex-owned", "Sign in with ChatGPT", "browser", ["inference", "quota"],
            gateway() && coreAvailable() ? "available" : "setup-required", true, false,
            Description: "A separate lain login owned by its Gateway. Existing external sessions remain untouched.",
            Guidance: gateway() ? "Install the qualified Gateway component to use this login." : "This login needs Gateway mode. Standalone managed login is not yet qualified.",
            Recommended: true));
        result.Add(new("codex", "snapshot", "Access-token snapshot", "snapshot", ["quota"], "available", false, true,
            "access-token-snapshot", "Read-only external snapshot with account identity and expiry. lain never refreshes or revokes it.",
            RequiredSettings: [new("accountId", "ChatGPT account ID")]));
        result.Add(new("openrouter", "browser", "Connect in browser", "browser", ["inference", "models", "quota"], "available",
            false, false, Description: "Authorize OpenRouter to issue a dedicated key to lain. No purchase or inference is performed.",
            DefaultBaseUrl: OpenRouterLoginAdapter.DefaultBaseUrl, DocumentationUrl: "https://openrouter.ai/docs/use-cases/oauth-pkce", Recommended: true));
        result.Add(NanoGptLoginAdapter.ConnectionMethod());
        result.Add(FireworksCliLoginAdapter.ConnectionMethod(fireworksCliAvailable?.Invoke() == true));
        result.Add(new("poe", "browser", "Connect in browser", "browser", ["inference", "models", "quota"],
            poeAvailable() ? "available" : "setup-required", false, false,
            Description: "Authorize a dedicated Poe API key using the registered lain client.",
            Guidance: "The application maintainer must first qualify the Poe client and callback registration. An existing API key remains usable."));
        result.Add(Lain.Host.Accounts.Kilo.KiloDeviceLoginAdapter.ConnectionMethod());
        result.Add(new("kimi", "kimi-device", "Sign in with Kimi", "device", ["quota"], "available", false, false,
            Description: "Connect Kimi Code monitoring in your browser using the displayed code. Choose the account region; existing inference keys and model routes are kept.",
            Guidance: "This login reads quota only. It does not add inference access or purchase a plan. Lain owns renewal of its separate Kimi session.",
            DocumentationUrl: "https://github.com/MoonshotAI/kimi-code/tree/main/packages/oauth", Recommended: true));
        result.AddRange(Lain.Host.Accounts.Managed.ManagedLoginQualifications.Methods().Where(method => method.Provider != "kimi"));
        foreach (var id in new[] { "requesty", "alibaba-model-studio", "commandcode", "mistral" })
            result.Add(new(id, "official-cli", "Connect with official CLI", "cli", ["inference", "models"], "unavailable", false, false,
                Guidance: "This provider's executable, isolated credential storage and identity handoff are not yet fully qualified. Use the existing key method; lain does not inspect another CLI's credentials."));
        result.Add(new("gemini-api", "official-cli", "Connect with Google Cloud CLI", "cli", ["quota"],
            googleCliAvailable?.Invoke() == true ? "available" : "setup-required", false, false,
            Description: "Sign in through the installed Google Cloud CLI with its standard Cloud Platform, Compute, App Engine and Cloud SQL permissions, account identity and reauthentication. Lain reads account, project and Monitoring data using a separate private CLI profile.",
            Guidance: "Install the qualified Google Cloud CLI version 584.0.0. This is the CLI's standard Google Cloud grant, not a read-only OAuth grant. Choose the project and ensure its Monitoring permissions are available.",
            DocumentationUrl: "https://cloud.google.com/sdk/gcloud/reference/auth/login", Recommended: true));
        result.Add(new("xiaomi-mimo", "console-browser", "Connect MiMo monitoring", "browser", ["quota"],
            mimoAvailable?.Invoke() == true ? "available" : "setup-required", false, false,
            Description: "Read Individual Token Plan credits through a separate lain-owned MiMo console window. Select the verified CN/EU identity before attaching monitoring.",
            Guidance: "MiMo monitoring requires Linux, an installed Chromium or Chrome browser, and a desktop session. Reconnect after the browser closes or lain restarts.",
            DocumentationUrl: "https://platform.xiaomimimo.com/"));
        foreach (var id in new[] { "sambanova", "upstage", "cohere" })
            result.Add(new(id, "console", "Connect provider console", "browser", ["quota"], "unavailable", false, false,
                Guidance: "A supported, identity-bound console session and its reporting scope are not yet qualified. No browser cookies or existing profiles are imported."));
        foreach (var id in new[] { "gemini-api", "cloudflare-workers-ai" })
            result.Add(new(id, "browser", "Connect monitoring in browser", "browser", ["quota"], cloudAvailable?.Invoke(id) == true ? "available" : "setup-required", false, false,
                Description: "A separate provider grant for project or account monitoring.",
                Guidance: "The application maintainer must qualify its own public client, allowed scopes and callback registration. Existing token-based monitoring remains available."));
        var all = provider is null ? result : result.Where(m => m.Provider == Canonical(provider));
        return new(Api.Version, all.OrderBy(m => m.Provider, StringComparer.Ordinal).ThenByDescending(m => m.Recommended).ToArray());

        void AddKey(string id, string endpoint, string docs) => result.Add(new(id, "api-key", "API key", "api-key",
            DirectQuota.Contains(id) ? ["inference", "models", "quota"] : ["inference", "models"], "available", false, true, "api-key",
            "Use an existing key; connecting does not create model routes or apply a profile.", endpoint, DocumentationUrl: docs, Recommended: id != "kimi"));
    }

    public ProviderConnectionMethodDto RequireAvailable(ConnectAccountRequest request, bool secret)
    {
        var method = Snapshot(request.Provider).Methods.SingleOrDefault(m => m.Id == request.Method)
            ?? throw new ArgumentException("Unknown connection method.");
        if (method.Availability != "available" || method.RequiresCore && (!gateway() || !coreAvailable()))
            throw new InvalidOperationException("This connection method is unavailable on this host.");
        if (method.SecretRequired != secret || request.Purposes is null || request.Purposes.Length == 0 ||
            request.Purposes.Any(p => !method.Purposes.Contains(p, StringComparer.Ordinal)))
            throw new ArgumentException("Unsupported connection purpose or credential submission.");
        return method;
    }

    public ConfigurationDto Connect(long revision, ConnectCredentialRequest request, AccountService accounts,
        MonitorAccessService monitors, ConfigurationRepository repository)
    {
        request = PrepareCredentialRequest(request);
        var connection = request.Connection;
        var binding = connection.Binding!;
        if (RequireAvailable(connection, secret: true).Kind == "monitor-token")
        {
            var input = new MonitorAccessRequest(request.Kind, request.Secret, binding.ResourceId, request.ExpiresAt);
            if (connection.AccountId is { } id)
            {
                var target = repository.Snapshot.Accounts.SingleOrDefault(a => a.Id == id) ?? throw new KeyNotFoundException();
                if (Canonical(target.Provider) != Canonical(connection.Provider) || connection.ExpectedGeneration is { } generation && generation != target.Generation)
                    throw new InvalidOperationException("Account connection changed.");
                return monitors.Set(revision, id, input);
            }
            return monitors.Create(revision, connection.Name!, connection.Provider, input);
        }
        return accounts.ConnectCredential(revision, request);
    }

    /// <summary>Validate method/resource/endpoint without publishing a credential or changing state.</summary>
    public ConnectCredentialRequest PrepareCredentialRequest(ConnectCredentialRequest request)
    {
        var method = RequireAvailable(request.Connection, secret: true);
        if (request.Kind != method.SecretKind) throw new ArgumentException("Credential kind does not match the method.");
        if (method.Kind != "monitor-token") InferenceCredentialPolicy.RejectManagementToken(request.Secret);
        var settings = request.Settings ?? [];
        if (settings.Count > 12 || settings.Any(p => p.Value is null || p.Value.Length > 512 || p.Value.Any(char.IsControl)))
            throw new ArgumentException("Invalid connection resource settings.");
        var resourceLessMonitor = method.Kind == "monitor-token" && MonitorAccessCatalog.Options(request.Connection.Provider)
            .Any(option => option.Kind == request.Kind && !option.ResourceRequired);
        if (resourceLessMonitor)
        {
            if (settings.Count != 0 || request.Connection.Binding is { ResourceId: not null } or { ResourceType: not null })
                throw new ArgumentException("This monitor credential does not accept a resource selector.");
            if (request.Connection.Binding is { } supplied && Canonical(supplied.Provider) != Canonical(request.Connection.Provider))
                throw new ArgumentException("Provider binding mismatch.");
            if (request.BaseUrl is not null) throw new ArgumentException("This monitor credential does not accept an inference endpoint.");
            // A personal management token selects its own owner. Inference-account
            // identity assertions must not become an invented monitoring selector.
            return request with { Connection = request.Connection with {
                Binding = new ResourceBindingDto(request.Connection.Provider),
                Name = request.Connection.Name ?? Name(Canonical(request.Connection.Provider)) }, Settings = [] };
        }
        foreach (var field in method.RequiredSettings ?? [])
            if (field.Required && !settings.ContainsKey(field.Id) && !BindingValue(request.Connection.Binding, field.Id).HasValue())
                throw new ArgumentException("A required provider resource is missing.");
        var binding = request.Connection.Binding ?? new ResourceBindingDto(request.Connection.Provider);
        var resourceField = settings.ContainsKey("projectId") ? "projectId" : settings.ContainsKey("accountId") ? "accountId" : settings.ContainsKey("workspaceId") ? "workspaceId" : null;
        if (resourceField is not null)
        {
            var resource = settings[resourceField].Trim();
            var type = resourceField == "projectId" ? "project" : resourceField == "workspaceId" ? "workspace" : "account";
            if (binding.ResourceId is not null && binding.ResourceId != resource) throw new ArgumentException("Resource settings disagree with the binding.");
            binding = binding with { ResourceId = resource, ResourceType = type };
        }
        if (Canonical(binding.Provider) != Canonical(request.Connection.Provider)) throw new ArgumentException("Provider binding mismatch.");
        if (Canonical(request.Connection.Provider) == "codex") binding = binding with { Subject = binding.Subject ?? binding.ResourceId };
        var connection = request.Connection with { Binding = binding, Name = request.Connection.Name ?? Name(Canonical(request.Connection.Provider)) };
        if (method.Kind == "monitor-token")
            return request with { Connection = connection };
        var endpoint = method.DefaultBaseUrl;
        foreach (var field in method.RequiredSettings ?? [])
            if (endpoint is not null)
                endpoint = endpoint.Replace("{" + field.Id + "}", Uri.EscapeDataString(settings.GetValueOrDefault(field.Id) ?? BindingValue(binding, field.Id) ?? ""), StringComparison.OrdinalIgnoreCase);
        if (request.BaseUrl is not null && request.BaseUrl != method.DefaultBaseUrl && request.BaseUrl != endpoint)
            throw new ArgumentException("Endpoint differs from the selected connection.");
        var privateSettings = settings.Where(p => p.Key is "accountId" or "region" or "workspaceId" or "serviceDeploymentScope").ToDictionary(p => p.Key, p => p.Value);
        return request with { Connection = connection, BaseUrl = endpoint, Settings = privateSettings };
    }

    public static string Canonical(string value) => AccountService.CanonicalProviderId(value);
    private static string? BindingValue(ResourceBindingDto? binding, string field) => field switch
    {
        "projectId" or "accountId" or "workspaceId" or "resourceId" => binding?.ResourceId,
        "subject" => binding?.Subject, "product" => binding?.Product, "region" => binding?.Region, _ => null
    };
    private string Name(string provider) => catalog.Find(provider)?.DefaultAccountName ?? provider switch
        { "codex" => "Codex", "commandcode" => "Command Code", "kimi" => "Kimi", "zai" => "Z.ai", "xiaomi-mimo" => "MiMo", _ => provider };
}

internal static class ConnectionValue
{
    internal static bool HasValue(this string? value) => !string.IsNullOrWhiteSpace(value);
}
