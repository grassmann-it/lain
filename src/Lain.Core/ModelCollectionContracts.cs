namespace Lain.Core;

/// <summary>An ordered view of existing routes. Membership neither activates routes nor changes profile selection.
/// RouterTargetId optionally references an explicit profile-bound collection router.</summary>
public sealed record ModelCollectionDto(string Id, string Name, string[] RouteIds, string? RouterTargetId = null);

/// <summary>Explicit profile-bound ordered/sticky selector; no classifier or retry policy.</summary>
public sealed record CollectionRouterDto(string Id, string Alias, string GroupId, string ProfileId,
    string CostPolicy = "configured", bool Enabled = true);

/// <summary>A read-only group over the same route identities. Kind is provider or custom.
/// Group names, including Free, make no assertion about cost or model suitability.</summary>
public sealed record ModelCollectionGroupDto(string Id, string Name, string Kind, string[] RouteIds,
    string? Provider = null, string? RouterTargetId = null);

public sealed record ModelCollectionGroupsDto(int ContractVersion, long Revision, ModelCollectionGroupDto[] Groups);
