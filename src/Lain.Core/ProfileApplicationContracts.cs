namespace Lain.Core;

public sealed record ProfileApplyPreviewDto(int ContractVersion, string ProfileId, long Revision,
    string? ActiveProfileId, long? ActiveRevision, string[] AddedRouteIds, string[] ChangedRouteIds,
    string[] RemovedRouteIds, string[] AffectedAccountIds, string[] AffectedClients,
    string StreamBehavior, bool BaselineKnown, string? DefaultRouteId = null,
    string? ActiveDefaultRouteId = null, bool ProfileChanged = false);

public sealed record ProfileApplicationStatusDto(string State, string? OperationId, string? ProfileId,
    long? Revision, string? RecoveryAction = null);
