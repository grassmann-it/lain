namespace Lain.Core;

public static class Api { public const string Prefix = "/api/v1"; public const int Version = 1; }
public record AccountDto(string Id, string Name, string Provider, bool Enabled, string CredentialKind, string State, string? BudgetPoolId = null, DateTimeOffset? ExpiresAt = null, bool SharedWithLegacy = false,
    long Generation = 0, ResourceBindingDto? Binding = null, CredentialHandleDto[]? Credentials = null, AccountCapabilityDto[]? Capabilities = null);
public record RouteDto(string Id, string Alias, string UpstreamModel, string Provider, string[] AccountIds, bool Pooled = false, bool Visible = true, bool Enabled = true, string? ProviderRouteId = null, RoutingPolicyDto? Policy = null);
public record ProfileDto(string Id, string Name, string[] RouteIds, string? DefaultRouteId = null, RoutingPolicyDto? Policy = null);
public record ConfigurationDto(long Revision, long AppliedRevision, AccountDto[] Accounts, RouteDto[] Routes, ProfileDto[] Profiles, string RuntimeMode = "gateway", ModelCollectionDto[]? Collections = null, CollectionRouterDto[]? CollectionRouters = null);
public record RevisionRequest<T>(long Revision, T Value);
public record AccountMetadataDiscoveryRequest(long ExpectedGeneration);
public record StatusDto(int ContractVersion, string Version, string State, bool ProxyReady, long Revision, long AppliedRevision, DateTimeOffset ObservedAt, string? Error = null,
    string? ActiveProfileId = null, long? ActiveProfileRevision = null,
    string RuntimeMode = "gateway", HostCapabilitiesDto? Capabilities = null, ProfileApplicationStatusDto? Application = null);
public record ApiError(string Code, string Message);
public record PageDto<T>(T[] Items, int Offset, int Limit, long Total);
public record UsageQuery(DateTimeOffset? From = null, DateTimeOffset? To = null, string TimeZone = "UTC", string? AccountId = null, string? Model = null, int? StatusCode = null, int Offset = 0, int Limit = 100,
    string? RouteId = null, string? ModelFamily = null, string? Manufacturer = null,
    string? InferenceProvider = null, string? Gateway = null, string? BillingAccountId = null,
    string? ReasoningEffort = null, string? Mode = null, string? ConversationId = null,
    string? TurnId = null, string? WorkerId = null, string? Harness = null,
    string? ProfileId = null, string? GroupBy = null, int GroupLimit = 100,
    string? EffectiveReasoningEffort = null);
public record TokenCounts(long? Input, long? Output, long? Cached, long? Reasoning, long? Total, long? CacheCreation = null);
public record UsageTotals(long Requests, long Attempts, long Errors, TokenCounts Tokens, long UnknownUsageAttempts, TokenMeasurementsDto? Measurements = null);
public record UsageGroup(string Key, UsageTotals Totals);
public record UsageSummaryDto(int ContractVersion, DateTimeOffset From, DateTimeOffset To, string TimeZone, DateTimeOffset ObservedAt, long Cursor, bool Complete, UsageTotals Totals, UsageGroup[] Daily, UsageGroup[] Accounts, UsageGroup[] Models);
public record AttemptDto(string Id, string RequestId, string AccountId, string? RouteId, string Provider, string Model, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, int? StatusCode, string State, TokenCounts Tokens, double? DurationMs = null, double? FirstTokenMs = null, string? Error = null,
    AttemptDimensionsDto? Dimensions = null, AttemptMeasurementsDto? Measurements = null,
    string? SourceId = null, string? RequestedModel = null, string? CompletionState = null, AttemptErrorDto? ErrorDetails = null);
public record QuotaSupportDto(string Kind, string Message, string? ActionUrl = null, string? CredentialPurpose = null);
public record QuotaDto(string AccountId, string? BudgetPoolId, string Source, string State, DateTimeOffset? ObservedAt, DateTimeOffset? LastSuccessAt, DateTimeOffset? ResetsAt, string Unit, double? Used, double? Limit, double? Remaining, bool SharedWithLegacy, string? Error = null, string? WindowLabel = null, double? UsedPercent = null,
    QuotaSupportDto? Support = null, string? WindowId = null, string? MeasurementKind = null,
    string? Scope = null, string? ScopeId = null, string? Model = null, string? SourceUrl = null,
    DateTimeOffset? DataAsOf = null, DateTimeOffset? WindowStart = null, DateTimeOffset? WindowEnd = null,
    DateTimeOffset? FreshUntil = null, DateTimeOffset? ProviderUpdatedAt = null, double? ApprovedLimit = null,
    string? ExactUsed = null, string? ExactLimit = null, string? ExactRemaining = null,
    DateTimeOffset? NextRegenerationAt = null, double? NextRegenerationPercent = null, string? ExactNextRegeneration = null,
    KiloPassState? KiloPass = null, RequestyKeyMetadata? RequestyKey = null,
    QuotaMetadataObservationDto? KiloPassObservation = null,
    QuotaMetadataObservationDto? RequestyKeyObservation = null);
public record LoginDto(string Id, string Provider, string State, string? Url = null, string? Error = null,
    string? Method = null, string[]? Purposes = null, string? AccountId = null, DateTimeOffset? ExpiresAt = null,
    string? NextAction = null, ResourceBindingDto[]? Resources = null,
    string? UserCode = null, string? VerificationUri = null);
public record ImportRequest(long Revision, string ConfigPath, string? AuthDirectory = null);
public record ImportResult(long Revision, int Added, int Updated, string[] Warnings);

public interface IUsageStore {
 Task<UsageSummaryDto> SummaryAsync(UsageQuery query, CancellationToken ct = default);
 Task<PageDto<AttemptDto>> AttemptsAsync(UsageQuery query, CancellationToken ct = default);
}
