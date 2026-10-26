using System.Text.Json.Nodes;
using Lain.Core;
using Lain.Host.Accounts;

namespace Lain.Host.Quota;

/// <summary>Selects an already saved official transport within the selected credential generation.</summary>
internal static class QuotaTransportProjection
{
    public static string? Select(AccountDto account, string json)
    {
        var root = JsonNode.Parse(json) as JsonObject;
        if (root is null || Text(root, "kind") != "api-key" || Text(root, "apiKey") is not { Length: > 0 } key
            || root["transports"] is not JsonArray transports || transports.Count > 128) return json;
        var provider = AccountService.CanonicalProviderId(account.Provider);
        // A qualified primary transport remains authoritative, including its saved scope.
        if (Enabled(root) && OfficialEndpoint(provider, Text(root, "baseUrl")) is not null) return json;
        var candidates = new List<(string Endpoint, JsonObject Settings)>();
        foreach (var transport in transports.OfType<JsonObject>())
        {
            if (!Enabled(transport) || Text(transport, "kind") != "api-key" || Text(transport, "apiKey") != key
                || Text(transport, "provider") is not { } transportProvider
                || AccountService.CanonicalProviderId(transportProvider) != provider
                || OfficialEndpoint(provider, Text(transport, "baseUrl")) is not { } endpoint) continue;
            var settings = (root["settings"] as JsonObject)?.DeepClone().AsObject() ?? new JsonObject();
            var conflict = false;
            foreach (var setting in (transport["settings"] as JsonObject) ?? new JsonObject())
            {
                if (settings.TryGetPropertyValue(setting.Key, out var current) && current is not null
                    && !JsonNode.DeepEquals(current, setting.Value)) { conflict = true; break; }
                settings[setting.Key] = setting.Value?.DeepClone();
            }
            if (conflict || !MatchesBinding(account.Binding, settings, endpoint)) continue;
            candidates.Add((endpoint, settings));
        }
        if (candidates.Count == 0) return Enabled(root) ? json : null;
        // Several official origins for the same saved key are fine; conflicting resource
        // selections are not. Never pick an account, workspace or product by array order.
        var selected = candidates.OrderBy(c => c.Endpoint, StringComparer.Ordinal).First();
        if (candidates.Any(c => !JsonNode.DeepEquals(selected.Settings, c.Settings))) return Enabled(root) ? json : null;
        root["baseUrl"] = selected.Endpoint;
        root["settings"] = selected.Settings;
        return root.ToJsonString();
    }

    private static bool Enabled(JsonObject value) => value["sourceEnabled"] is not JsonValue enabled
        || !enabled.TryGetValue<bool>(out var state) || state;
    private static string? Text(JsonObject value, string name) => value[name] is JsonValue node && node.TryGetValue<string>(out var text) ? text : null;

    private static bool MatchesBinding(ResourceBindingDto? binding, JsonObject settings, string endpoint)
    {
        if (binding is null) return true;
        var region = Text(settings, "region");
        if (binding.Region is not null && region is not null && binding.Region != region) return false;
        var resource = Text(settings, "accountId") ?? Text(settings, "projectId") ?? Text(settings, "workspaceId") ?? Text(settings, "workspace_id");
        if (AccountService.CanonicalProviderId(binding.Provider) == "cloudflare-workers-ai")
            resource ??= new Uri(endpoint).AbsolutePath.Split('/').ElementAtOrDefault(4);
        return binding.ResourceId is null || resource is null || binding.ResourceId == resource;
    }

    private static string? OfficialEndpoint(string provider, string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0) return null;
        var host = uri.IdnHost.ToLowerInvariant();
        var path = uri.AbsolutePath.TrimEnd('/');
        var qualified = provider switch
        {
            "kimi" => host is "api.kimi.com" or "api.kimi.ai" && path == "/coding/v1",
            "zai" => host == "api.z.ai" && path is "/api/v1" or "/v1" or "/api/paas/v4" or "/api/coding/paas/v4" or "/api/anthropic",
            "commandcode" => host == "api.commandcode.ai" && path is "/v1" or "/alpha",
            "openrouter" => host == "openrouter.ai" && path == "/api/v1",
            "kilo" => host == "ai-gateway.kilo.ai" && path == "/api/v1",
            "requesty" => host == "router.requesty.ai" && path == "/v1",
            "poe" => host == "api.poe.com" && path == "/v1",
            "synthetic" => host == "api.synthetic.new" && path is "/v1" or "/openai/v1" or "/anthropic/v1",
            "stepfun" => host == "api.stepfun.com" && path == "/v1",
            "fireworks" => host == "api.fireworks.ai" && path == "/inference/v1",
            "deepseek" => host == "api.deepseek.com" && path == "",
            "nanogpt" => host == "api.nano-gpt.com" && path == "/api/v1",
            "cloudflare-workers-ai" => host == "api.cloudflare.com" &&
                System.Text.RegularExpressions.Regex.IsMatch(path, "^/client/v4/accounts/[a-fA-F0-9]{32}/ai/v1$"),
            _ => false
        };
        return qualified ? "https://" + host + path : null;
    }
}
