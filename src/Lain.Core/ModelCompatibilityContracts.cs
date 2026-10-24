namespace Lain.Core;

public sealed record ModelCapabilityEvidenceDto(string State, string Qualification, string? Source = null,
    DateTimeOffset? ObservedAt = null, string? ClientVersion = null, string? Scope = null);
public sealed record ModelCapabilityLimitDto(long? Value, ModelCapabilityEvidenceDto Evidence);
public sealed record ModelCapabilitySetDto(ModelCapabilityEvidenceDto Functions,
    ModelCapabilityEvidenceDto NamespaceTools, ModelCapabilityEvidenceDto CustomTools,
    ModelCapabilityEvidenceDto NativeSearch, ModelCapabilityEvidenceDto Streaming,
    ModelCapabilityEvidenceDto ToolContinuation, ModelCapabilityLimitDto MaximumTools,
    ModelCapabilityLimitDto ContextWindow, ModelCapabilityLimitDto MaximumOutput);
public sealed record ModelCompatibilityDto(string RouteId, string Provider, string UpstreamModel,
    string? ProviderRouteId, string Client, string ClientVersion, string IngressProtocol,
    string? UpstreamProtocol, string ClientQualification, ModelCapabilitySetDto Capabilities, string[] Notes);
public sealed record ModelCompatibilityManifestDto(int ContractVersion, string Client, string ClientVersion,
    ModelCompatibilityDto[] Routes, string[] Notes);
