namespace Lain.Core;

/// <summary>Provider/resource identity and its provenance; never authentication material.</summary>
public sealed record ResourceBindingDto(string Provider, string? Subject = null, string? Product = null,
    string? Region = null, string? ResourceType = null, string? ResourceId = null, string Provenance = "declared");

/// <summary>Opaque identity of a private credential generation. Id is not a filesystem path.</summary>
public sealed record CredentialHandleDto(string Id, long Generation, string Owner, string[] Purposes,
    ResourceBindingDto Binding, DateTimeOffset? ExpiresAt = null);

public sealed record AccountCapabilityDto(string Purpose, string State, string? CredentialId = null, string? Error = null);

/// <summary>Safe operation metadata. Credential submission uses a separate redacted request.</summary>
public sealed record ConnectAccountRequest(string Provider, string Method, string[] Purposes,
    string? Name = null, string? AccountId = null, ResourceBindingDto? Binding = null,
    long? ExpectedGeneration = null, string? IdempotencyKey = null);

public sealed record ConnectCredentialRequest(ConnectAccountRequest Connection, string Secret,
    string Kind = "api-key", DateTimeOffset? ExpiresAt = null, string? BaseUrl = null,
    Dictionary<string, string>? Settings = null)
{
    public override string ToString() => "ConnectCredentialRequest [redacted]";
}

public sealed record ResourceSelectionRequest(long Revision, ResourceBindingDto Binding);

/// <summary>Attach documented models using an account's private existing key; no secret in this request.</summary>
public sealed record AttachAccountModelsRequest(long ExpectedGeneration, string ConnectionId,
    string[] ModelRouteIds, Dictionary<string, string>? Settings = null);

/// <summary>Configured intent is independent of process readiness and UI information density.</summary>
public sealed record HostCapabilitiesDto(bool QuotaMonitoring, bool Inference, bool ModelProfiles,
    bool LocalHistory, bool LiveInspector, bool CoreLogin);

public sealed record RuntimeModeRequest(long Revision, string Mode);

public sealed record ConnectionResourceFieldDto(string Id, string Label, bool Required = true,
    string[]? Choices = null, string? Help = null);

/// <summary>Availability describes the installed method, not a successful login or provider entitlement.</summary>
public sealed record ProviderConnectionMethodDto(string Provider, string Id, string Title, string Kind,
    string[] Purposes, string Availability, bool RequiresCore, bool SecretRequired,
    string? SecretKind = null, string? Description = null, string? DefaultBaseUrl = null,
    ConnectionResourceFieldDto[]? RequiredSettings = null, string? DocumentationUrl = null,
    string? Guidance = null, bool Recommended = false);

public sealed record ConnectionMethodCatalogDto(int ContractVersion, ProviderConnectionMethodDto[] Methods);
