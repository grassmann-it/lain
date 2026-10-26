using Lain.Core;

namespace Lain.Host.Models;

/// <summary>Client effort choices, separately scoped from provider inference qualification.</summary>
public sealed record ModelReasoningCompatibility(string? DefaultEffort, string[] SupportedEfforts, ModelCapabilityEvidenceDto Evidence)
{
    /// <summary>An explicit or inherited setting is never silently replaced or normalized.</summary>
    public string? Select(string? requestedEffort = null, bool inherited = false)
    {
        if (requestedEffort is null) return DefaultEffort;
        if (SupportedEfforts.Contains(requestedEffort, StringComparer.Ordinal)) return requestedEffort;
        var origin = inherited ? "Inherited reasoning effort" : "Reasoning effort";
        var action = SupportedEfforts.Length == 0
            ? "Reasoning controls are unqualified for this route; select a source-qualified route or explicitly remove the effort override."
            : $"Choose one of: {string.Join(", ", SupportedEfforts)}. The model default is {DefaultEffort}.";
        throw new ArgumentException($"{origin} '{requestedEffort}' is not advertised for this route. {action}");
    }
}

public sealed partial class ModelCompatibilityCatalog
{
    // Extracted from the public rust-v0.164.0 models-manager/models.json, pinned in
    // clients/codex/upstream.json (commit 6aaac694803004a63c6f393d153c96d22521a6b9).
    // These are native Codex CLIENT choices, including the client's ultra alias.
    // They are not facts about third-party gateways serving similarly named models.
    public const string ReasoningSource = "https://github.com/openai/codex/blob/6aaac694803004a63c6f393d153c96d22521a6b9/codex-rs/models-manager/models.json";
    private static readonly DateTimeOffset ReasoningObservation = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<string, (string Default, bool Ultra)> NativeReasoning =
        new Dictionary<string, (string, bool)>(StringComparer.Ordinal)
        {
            ["gpt-6-astra"] = ("low", true), ["gpt-6.1-sol"] = ("low", true),
            ["gpt-6-sol"] = ("medium", true), ["gpt-6-luna"] = ("medium", false),
            ["gpt-5.6-sol"] = ("low", true), ["gpt-5.6-terra"] = ("medium", true),
            ["gpt-5.6-luna"] = ("medium", false), ["gpt-daybreak-blue-latest"] = ("low", true),
            ["gpt-daybreak-red-latest"] = ("medium", true), ["codex-auto-review"] = ("medium", false)
        };
    private static readonly HashSet<string> WireEfforts = new(StringComparer.Ordinal) { "none", "minimal", "low", "medium", "high", "xhigh", "max" };

    public ModelReasoningCompatibility ResolveReasoning(RouteDto route, string clientVersion = ClientVersion)
    {
        transports.TryGetValue(route.Id, out var transport);
        ModelReasoningCompatibility UnknownReasoning() => new(null, [], new("unknown", "unqualified"));
        if (clientVersion != ClientVersion || transport is not null && (transport.Provider != route.Provider || transport.UpstreamModel != route.UpstreamModel ||
            string.IsNullOrWhiteSpace(transport.Source))) return UnknownReasoning();
        if (route.Provider == "codex" && transport?.UpstreamProtocol == "responses" &&
            transport.Source is "compiled:codex-owned" or "compiled:codex-access-snapshot")
        {
            if (NativeReasoning.TryGetValue(route.UpstreamModel, out var native))
                return new(native.Default, native.Ultra ? ["low", "medium", "high", "xhigh", "max", "ultra"] : ["low", "medium", "high", "xhigh", "max"],
                    new("supported", "local_fixture", ReasoningSource, ReasoningObservation, ClientVersion, "client_model"));
            if (route.UpstreamModel == "gpt-5.5")
                return new("medium", ["low", "medium", "high", "xhigh"],
                    new("supported", "local_fixture", ReasoningSource, ReasoningObservation, ClientVersion, "client_model"));
        }

        var descriptor = qualifiedRoutes?.Contains(route.Id) == true ? registry?.FindRoute(route.Provider, route.ProviderRouteId) : null;
        if (descriptor is not null && descriptor.Model == route.UpstreamModel &&
            NormalizeProtocol(descriptor.Protocol) is "openai" or "responses" &&
            (transport is null || transport.UpstreamProtocol == NormalizeProtocol(descriptor.Protocol)) &&
            descriptor.Sources?.FirstOrDefault(source => source.CheckedAt is not null) is { } source &&
            descriptor.Capabilities.Reasoning != false &&
            descriptor.Capabilities.ReasoningModes is { Length: > 0 } modes &&
            modes.All(mode => WireEfforts.Contains(mode) || mode == "instant" && route.Provider == "inception" && route.UpstreamModel == "mercury-3") &&
            modes.Distinct(StringComparer.Ordinal).Count() == modes.Length)
        {
            // The descriptor has no upstream-default contract. Pick a supported client
            // default without claiming it is the provider default. ReasoningModes
            // can also describe states (e.g. NVIDIA's "always-on"), so only actual
            // wire effort controls qualify. Mercury's "instant" is also enforced
            // by TrialProviderRequestAdapter. No enum conversion is applied.
            var defaultEffort = modes.Contains("medium", StringComparer.Ordinal) ? "medium" : modes[0];
            return new(defaultEffort, modes.ToArray(), new("supported", "documentation", source.Url, source.CheckedAt, Scope: "provider_route"));
        }
        // Unknown is not support for `none`: native worker validation treats an empty
        // list as no advertised choices, and the default must not fabricate one.
        return UnknownReasoning();
    }
}
