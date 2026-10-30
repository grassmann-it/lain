using Lain.Core;

namespace Lain.Host.Models;

public sealed partial class ModelCompatibilityCatalog
{
    // Input modalities from the same pinned native Codex 0.164.0 model source as
    // NativeReasoning. Never infer them from a user-controlled alias/model prefix.
    private static readonly HashSet<string> NativeImageModels = new(StringComparer.Ordinal)
    {
        "gpt-6-astra", "gpt-6.1-sol", "gpt-6-sol", "gpt-6-luna",
        "gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna",
        "gpt-daybreak-blue-latest", "gpt-daybreak-red-latest", "gpt-5.5", "codex-auto-review"
    };

    public string[] ResolveInputModalities(RouteDto route, string clientVersion = ClientVersion)
    {
        if (clientVersion != ClientVersion) return ["text"];
        transports.TryGetValue(route.Id, out var transport);
        if (transport is not null && (transport.Provider != route.Provider || transport.UpstreamModel != route.UpstreamModel ||
            string.IsNullOrWhiteSpace(transport.Source))) return ["text"];
        if (route.Provider == "codex" && NativeImageModels.Contains(route.UpstreamModel) &&
            transport?.UpstreamProtocol == "responses" &&
            transport.Source is "compiled:codex-owned" or "compiled:codex-access-snapshot")
            return ["text", "image"];

        var descriptor = qualifiedRoutes?.Contains(route.Id) == true ? registry?.FindRoute(route.Provider, route.ProviderRouteId) : null;
        if (descriptor is not null && descriptor.Model == route.UpstreamModel && descriptor.Capabilities.Images == true &&
            descriptor.Sources?.Any(source => source.CheckedAt is not null) == true &&
            NormalizeProtocol(descriptor.Protocol) is "openai" or "responses" &&
            (transport is null || transport.UpstreamProtocol == NormalizeProtocol(descriptor.Protocol)))
            return ["text", "image"];
        return ["text"];
    }
}
