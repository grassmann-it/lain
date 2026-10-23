using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Providers.NvidiaStep;
using YamlDotNet.RepresentationModel;

namespace Lain.Host.Providers;

public sealed record ProviderTransportPlan(string SourceSection, string CoreProvider, string BaseUrl,
    string Protocol, IReadOnlyDictionary<string, string> Headers);

/// <summary>The same provider boundary is used before secret persistence and before runtime compilation.
/// It validates configuration only; no successful connection or available budget is implied.</summary>
public static class ProviderTransportValidation
{
    public static ProviderTransportPlan? Prepare(ProviderCatalogRegistry catalog, string providerId,
        string? routeId, string endpoint, IReadOnlyDictionary<string, string>? settings = null)
    {
        var provider = catalog.Find(providerId);
        if (provider is null)
        {
            if (routeId is not null) throw new ArgumentException("Unknown provider catalog route.");
            return null;
        }
        var route = catalog.FindRoute(providerId, routeId)
            ?? throw new ArgumentException("Select a concrete provider route before adding credentials.");
        var section = route.Protocol switch
        {
            "openai-chat" or "openai-responses" => "openai-compatibility",
            "anthropic-messages" => "claude-api-key",
            "gemini" => "gemini-api-key",
            _ => throw new ArgumentException("This provider protocol is not prepared for runtime use.")
        };
        var supplied = settings ?? new Dictionary<string, string>();
        string? Setting(string name, string? alias = null) => supplied.GetValueOrDefault(name) ?? (alias is null ? null : supplied.GetValueOrDefault(alias));
        foreach (var key in supplied.Keys)
            if (key is not ("accountId" or "region" or "workspaceId" or "workspace_id" or "serviceDeploymentScope" or "service_deployment_scope" or "billingPlan" or "allowTrainingData"))
                throw new ArgumentException("Unsupported provider setting. Credentials must use the credential field.");
        if (providerId == "opencode-go" && route.Model == "gemini-4-argon-contributor" && Setting("allowTrainingData") != "true")
            throw new ArgumentException("This Contributor route requires explicit workspace permission for endpoints that train on request data; confirm allowTrainingData only after reviewing that setting.");
        var expected = route.BaseUrl.TrimEnd('/');
        var billingPlan = Setting("billingPlan");
        if (billingPlan is not null && !route.BillingPlanIds.Contains(billingPlan, StringComparer.Ordinal))
            throw new ArgumentException("Billing plan does not belong to the selected provider route.");
        if (providerId == "cloudflare-workers-ai")
            expected = GatewayRouteValidation.CloudflareBaseUrl(Setting("accountId") ?? "");
        if (providerId is "inception" or "nebius" or "alibaba-model-studio")
            expected = TrialProviderConfigurationValidator.Validate(new(providerId, route.Model,
                billingPlan ?? (route.BillingPlanIds.Contains("payg") ? "payg" : route.BillingPlanIds.Single()),
                Region: Setting("region"), WorkspaceId: Setting("workspaceId", "workspace_id"),
                ServiceDeploymentScope: Setting("serviceDeploymentScope", "service_deployment_scope"))).AbsoluteUri.TrimEnd('/');
        if (providerId is "nvidia-build" or "stepfun")
        {
            billingPlan ??= route.BillingPlanIds.Length == 1 ? route.BillingPlanIds[0] : null;
            Reject(NvidiaStepConfiguration.Validate(providerId, billingPlan ?? "", route.Protocol, expected));
        }
        if (providerId is "synthetic" or "xai")
            Reject(SyntheticXaiConfiguration.ValidateConfiguration(providerId, expected, route.Protocol, route.Model));
        // Native executors append their version segments, unlike the Chat/Responses executor.
        var coreBase = route.Protocol switch
        {
            "anthropic-messages" when expected.EndsWith("/v1", StringComparison.Ordinal) => expected[..^3],
            "gemini" when expected.EndsWith("/v1beta", StringComparison.Ordinal) => expected[..^7],
            _ => expected
        };
        AccountService.ValidateEndpoint(endpoint);
        if (endpoint.TrimEnd('/') != expected && endpoint.TrimEnd('/') != coreBase)
            throw new ArgumentException("Endpoint does not match the selected provider route and account settings.");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (providerId == "opencode-go")
        {
            headers["User-Agent"] = "lain/1.0";
            headers["x-opencode-session"] = "$CPA-SESSION-ID";
        }
        // Protocol is part of the compatibility provider identity: the same key/model/base may
        // expose Chat and Responses, and must never collapse into one core credential.
        var coreProvider = section == "openai-compatibility" ? providerId + "-" + route.Protocol : providerId;
        return new(section, coreProvider, coreBase, route.Protocol, headers);
    }

    public static bool ValidateCompiled(ProviderCatalogRegistry catalog, RouteDto route, JsonElement transport,
        string section, string endpoint, YamlMappingNode provider)
    {
        if (route.ProviderRouteId is null) return false;
        var descriptor = catalog.FindRoute(route.Provider, route.ProviderRouteId)
            ?? throw new InvalidDataException("Unknown provider route.");
        if (descriptor.Model != route.UpstreamModel) throw new InvalidDataException("Selected model does not match provider route.");
        var settings = transport.TryGetProperty("settings", out var node) && node.ValueKind == JsonValueKind.Object
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(node.GetRawText()) : null;
        var plan = Prepare(catalog, route.Provider, route.ProviderRouteId, endpoint, settings)!;
        if (plan.SourceSection != section) throw new InvalidDataException("Credential transport protocol does not match provider route.");
        if (transport.TryGetProperty("providerRouteId", out var selected) && selected.ValueKind == JsonValueKind.String
            && selected.GetString() != route.ProviderRouteId &&
            !(transport.TryGetProperty("providerRouteIds", out var additional) && additional.ValueKind == JsonValueKind.Array &&
              additional.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String && value.GetString() == route.ProviderRouteId)))
            throw new InvalidDataException("Credential was prepared for a different provider route.");
        if (section == "openai-compatibility" && (!transport.TryGetProperty("coreProvider", out var core) || core.GetString() != plan.CoreProvider))
            throw new InvalidDataException("Credential provider identity does not match its protocol.");
        if (endpoint.TrimEnd('/') != plan.BaseUrl)
            throw new InvalidDataException("Credential endpoint must use the prepared native protocol base.");
        var key = new YamlScalarNode("headers");
        if (provider.Children.TryGetValue(key, out var existing))
        {
            if (existing is not YamlMappingNode existingHeaders || existingHeaders.Children.Any(pair =>
                pair.Key is not YamlScalarNode name || pair.Value is not YamlScalarNode value ||
                !plan.Headers.TryGetValue(name.Value ?? "", out var expected) || expected != value.Value))
                throw new InvalidDataException("Unqualified custom headers on a catalog-bound provider route.");
        }
        if (plan.Headers.Count > 0)
        {
            var headers = new YamlMappingNode();
            foreach (var header in plan.Headers) headers.Add(header.Key, header.Value);
            provider.Children[key] = headers;
        }
        if (section == "openai-compatibility")
            provider.Children[new YamlScalarNode("upstream-protocol")] = new YamlScalarNode(plan.Protocol);
        if (route.Provider == "opencode-go" && section == "gemini-api-key")
            provider.Children[new YamlScalarNode("auth-header")] = new YamlScalarNode("bearer");
        return true;
    }

    private static void Reject(IEnumerable<string> reasons)
    {
        var errors = reasons.ToArray();
        if (errors.Length > 0) throw new ArgumentException(string.Join(" ", errors));
    }
}
