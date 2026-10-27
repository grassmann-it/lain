using Lain.Core;
using Lain.Host.Providers;

namespace Lain.Host.Models;

/// <summary>A source-qualified fact from the actual compiled route transport, including configured imports.</summary>
public sealed record ModelCompatibilityTransportBinding(string Provider, string UpstreamModel, string UpstreamProtocol,
    string Source, DateTimeOffset? ObservedAt = null);

/// <summary>Bounded compatibility matrix. Reported failures are useful restrictions, not a live provider certification.</summary>
public sealed partial class ModelCompatibilityCatalog(ProviderCatalogRegistry? registry = null, IReadOnlySet<string>? qualifiedProviderRouteIds = null,
    IReadOnlyDictionary<string, ModelCompatibilityTransportBinding>? transportBindings = null)
{
    private readonly HashSet<string>? qualifiedRoutes = qualifiedProviderRouteIds?.ToHashSet(StringComparer.Ordinal);
    private readonly Dictionary<string, ModelCompatibilityTransportBinding> transports = transportBindings?.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal) ?? new(StringComparer.Ordinal);
    public const string ClientVersion = "0.164.0";
    public const string FailureSource = "docs/providers/codex-gateway-tools.md#distinct-failures";
    private static readonly DateTimeOffset Observation = new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
    private static ModelCapabilityEvidenceDto Unknown() => new("unknown", "unqualified");
    private static ModelCapabilityEvidenceDto ReportedUnsupported(string scope = "provider_route") => new("unsupported", "reported_failure", FailureSource, Observation, ClientVersion, scope);
    private static ModelCapabilityLimitDto UnknownLimit() => new(null, Unknown());

    public ModelCompatibilityDto Resolve(RouteDto route, string clientVersion = ClientVersion, ModelCompatibilityTransportBinding? transport = null, string ingressProtocol = "responses")
    {
        var functions = Unknown(); var namespaces = Unknown(); var custom = Unknown(); var search = Unknown();
        var streaming = Unknown(); var continuation = Unknown();
        var maximumTools = UnknownLimit(); var context = UnknownLimit(); var output = UnknownLimit();
        var notes = new List<string>();
        var descriptor = qualifiedRoutes?.Contains(route.Id) == true
            ? registry?.FindRoute(route.Provider, route.ProviderRouteId) : null;
        // An imported model or a changed model name cannot borrow a template's capabilities.
        if (descriptor is not null && descriptor.Model != route.UpstreamModel) descriptor = null;
        if (transport is null) transports.TryGetValue(route.Id, out transport);
        if (transport is not null && (transport.Provider != route.Provider || transport.UpstreamModel != route.UpstreamModel || string.IsNullOrWhiteSpace(transport.Source)))
            transport = null;
        if (transport is not null && descriptor is not null && transport.UpstreamProtocol != NormalizeProtocol(descriptor.Protocol))
        {
            descriptor = null;
            notes.Add("Compiled protocol differs from the documented model template; template capabilities remain unknown.");
        }
        var upstreamProtocol = transport?.UpstreamProtocol ?? NormalizeProtocol(descriptor?.Protocol);
        if (transport is not null) notes.Add($"Compiled transport source: {transport.Source}");
        if (descriptor is not null)
        {
            // Only route-specific checked sources qualify a documented fact. General provider sources do not.
            var source = descriptor.Sources?.FirstOrDefault(s => s.CheckedAt is not null);
            if (source is not null)
            {
                ModelCapabilityEvidenceDto Documented(bool? value) => value is null ? Unknown() :
                    new(value.Value ? "supported" : "unsupported", "documentation", source.Url, source.CheckedAt, Scope: "provider_route");
                ModelCapabilityLimitDto Limit(long? value) => value is > 0 ?
                    new(value, new("supported", "documentation", source.Url, source.CheckedAt, Scope: "provider_route")) : UnknownLimit();
                functions = Documented(descriptor.Capabilities.Tools);
                streaming = Documented(descriptor.Capabilities.Streaming);
                context = Limit(descriptor.Capabilities.ContextWindow); output = Limit(descriptor.Capabilities.MaximumOutput);
            }
            else notes.Add("Model template has no checked route-specific source; its capabilities remain unknown.");
        }
        if (ingressProtocol == "responses" && upstreamProtocol == "openai")
        {
            if (route.Provider == "xiaomi-mimo" && (route.UpstreamModel is "mimo-v2.6-flash" or "mimo-v2.6-pro"))
            {
                search = new("unsupported", "translator_constraint", FailureSource, Observation, ClientVersion, "translator");
                notes.Add("The local Responses-to-Chat bridge rejects native web search. This is a translator constraint, not a native upstream model capability claim. Keep web_search disabled in the separate lain profile.");
            }
            if (route.Provider == "commandcode")
            {
                switch (route.UpstreamModel)
                {
                    case "stealth/glyph-cluster:free":
                        namespaces = ReportedUnsupported(); custom = ReportedUnsupported();
                        notes.Add("Provider-defined tools were rejected. Codex 0.164.0 emits a namespace by default; explicitly disabling multi_agent removes delegation. Null patch metadata alone does not remove namespaces. Real Glyph acceptance remains unverified.");
                        break;
                    case "reflection/beam:free":
                        maximumTools = new(128, new("supported", "reported_failure", FailureSource, Observation, ClientVersion, "provider_route"));
                        notes.Add("A request exceeding 128 transmitted top-level tools was rejected. Select needed MCP/plugin tools explicitly using allow/deny lists; never truncate the request.");
                        break;
                }
            }
        }
        notes.Add("HTTP 429 alone does not establish exhausted subscription quota; no paid fallback is permitted.");
        var reasoning = ResolveReasoning(route, clientVersion);
        notes.Add(reasoning.DefaultEffort is null
            ? "Reasoning effort controls are unqualified. No default effort (including none) is advertised; inherited overrides require explicit removal or a qualified route."
            : $"Codex reasoning default: {reasoning.DefaultEffort}; advertised choices: {string.Join(", ", reasoning.SupportedEfforts)}. Source: {reasoning.Evidence.Source}; scope: {reasoning.Evidence.Scope}. Client choices do not certify provider inference.");
        notes.Add("Unknown means unqualified, not unsupported. Documentation and local client fixtures do not establish real provider acceptance.");
        if (clientVersion != ClientVersion) notes.Add("This Codex version has no qualified catalog schema or local tool capture. Known compiled route restrictions still apply independently; qualify this client before relying on catalog metadata.");
        return new(route.Id, route.Provider, route.UpstreamModel, route.ProviderRouteId, "codex", clientVersion, ingressProtocol,
            upstreamProtocol, clientVersion == ClientVersion ? "local_fixture" : "unqualified",
            new(functions, namespaces, custom, search, streaming, continuation, maximumTools, context, output), notes.ToArray());
    }

    private static string? NormalizeProtocol(string? protocol) => protocol switch
    {
        "openai" or "openai-chat" => "openai", "responses" or "openai-responses" => "responses", "anthropic" or "anthropic-messages" => "anthropic",
        "gemini" => "gemini", _ => null
    };

    public ModelCompatibilityManifestDto Manifest(IEnumerable<RouteDto> routes, string clientVersion = ClientVersion) =>
        new(1, "codex", clientVersion, routes.Select(route => Resolve(route, clientVersion)).ToArray(),
            ["Client catalog parser and clean tool capture qualified only for the stated client version; no real provider inference is implied.",
             "Native web search remains disabled by default. No tool removal, client feature restriction or paid fallback is applied automatically.",
             "The catalog truncation_policy is a client execution budget, not a measured provider context limit."]);
}
