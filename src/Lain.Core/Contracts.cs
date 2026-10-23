namespace Lain.Core;

public static class Api { public const string Prefix = "/api/v1"; public const int Version = 1; }
public record AccountDto(string Id, string Name, string Provider, bool Enabled, string CredentialKind, string State, string? BudgetPoolId = null, DateTimeOffset? ExpiresAt = null, bool SharedWithLegacy = false);
public record RouteDto(string Id, string Alias, string UpstreamModel, string Provider, string[] AccountIds, bool Pooled = false, bool Visible = true, bool Enabled = true);
public record ProfileDto(string Id, string Name, string[] RouteIds, string? DefaultRouteId = null);
public record ConfigurationDto(long Revision, long AppliedRevision, AccountDto[] Accounts, RouteDto[] Routes, ProfileDto[] Profiles);
public record RevisionRequest<T>(long Revision, T Value);
public record StatusDto(int ContractVersion, string Version, string State, bool ProxyReady, long Revision, long AppliedRevision, DateTimeOffset ObservedAt, string? Error = null);
public record ApiError(string Code, string Message);
public record PageDto<T>(T[] Items, int Offset, int Limit, long Total);
public record UsageQuery(DateTimeOffset? From = null, DateTimeOffset? To = null, string TimeZone = "UTC", string? AccountId = null, string? Model = null, int? StatusCode = null, int Offset = 0, int Limit = 100);
public record TokenCounts(long? Input, long? Output, long? Cached, long? Reasoning, long? Total);
public record UsageTotals(long Requests, long Attempts, long Errors, TokenCounts Tokens, long UnknownUsageAttempts);
public record UsageGroup(string Key, UsageTotals Totals);
public record UsageSummaryDto(int ContractVersion, DateTimeOffset From, DateTimeOffset To, string TimeZone, DateTimeOffset ObservedAt, long Cursor, bool Complete, UsageTotals Totals, UsageGroup[] Daily, UsageGroup[] Accounts, UsageGroup[] Models);
public record AttemptDto(string Id, string RequestId, string AccountId, string? RouteId, string Provider, string Model, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, int? StatusCode, string State, TokenCounts Tokens, double? DurationMs = null, double? FirstTokenMs = null, string? Error = null);
public record QuotaDto(string AccountId, string? BudgetPoolId, string Source, string State, DateTimeOffset? ObservedAt, DateTimeOffset? LastSuccessAt, DateTimeOffset? ResetsAt, string Unit, double? Used, double? Limit, double? Remaining, bool SharedWithLegacy, string? Error = null, string? WindowLabel = null, double? UsedPercent = null);
public record LoginDto(string Id, string Provider, string State, string? Url = null, string? Error = null);
public record ImportRequest(long Revision, string ConfigPath, string? AuthDirectory = null);
public record ImportResult(long Revision, int Added, int Updated, string[] Warnings);

public interface IUsageStore {
 Task<UsageSummaryDto> SummaryAsync(UsageQuery query, CancellationToken ct = default);
 Task<PageDto<AttemptDto>> AttemptsAsync(UsageQuery query, CancellationToken ct = default);
}
