using Lain.Core;

namespace Lain.Host.Models;

/// <summary>Client context defaults, not an assertion of account entitlement or live provider acceptance.</summary>
public sealed record ModelContextCompatibility(long? ContextWindow, long? MaximumContextWindow);

public sealed partial class ModelCompatibilityCatalog
{
    // Same pinned Codex 0.164.0 client model source as ReasoningSource. Missing
    // context metadata disables the client's derived automatic compaction threshold.
    private static readonly IReadOnlyDictionary<string, (long Window, long Maximum)> NativeContextWindows =
        new Dictionary<string, (long, long)>(StringComparer.Ordinal)
        {
            ["gpt-6-astra"] = (272000, 872000), ["gpt-6.1-sol"] = (272000, 872000),
            ["gpt-6-sol"] = (272000, 872000), ["gpt-6-luna"] = (272000, 872000),
            ["gpt-5.6-sol"] = (272000, 872000), ["gpt-5.6-terra"] = (272000, 872000),
            ["gpt-5.6-luna"] = (272000, 872000), ["gpt-daybreak-blue-latest"] = (272000, 872000),
            ["gpt-daybreak-red-latest"] = (372000, 372000), ["gpt-5.5"] = (272000, 272000),
            ["codex-auto-review"] = (272000, 872000)
        };

    public ModelContextCompatibility ResolveContextWindow(RouteDto route, string clientVersion = ClientVersion)
    {
        if (clientVersion != ClientVersion) return new(null, null);
        transports.TryGetValue(route.Id, out var transport);
        if (transport is not null && (transport.Provider != route.Provider || transport.UpstreamModel != route.UpstreamModel ||
            string.IsNullOrWhiteSpace(transport.Source))) return new(null, null);
        if (route.Provider == "codex" && transport?.UpstreamProtocol == "responses" &&
            transport.Source is "compiled:codex-owned" or "compiled:codex-access-snapshot" &&
            NativeContextWindows.TryGetValue(route.UpstreamModel, out var native))
            return new(native.Window, native.Maximum);
        var documented = Resolve(route, clientVersion).Capabilities.ContextWindow.Value;
        return new(documented, null);
    }
}
