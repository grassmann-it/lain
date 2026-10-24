namespace Lain.Core;

/// <summary>Personal Kilo Pass metadata. Billing/renewal dates do not imply wallet quota resets.</summary>
public sealed record KiloPassState(string? Status, string? Tier, double? CurrentPeriodBaseCreditsUsd,
    double? CurrentPeriodUsageUsd, double? CurrentPeriodBonusCreditsUsd,
    DateTimeOffset? NextBillingAt, DateTimeOffset? NextRenewalAt, bool? IsConsumable);

/// <summary>Facts about the same Requesty key; not organization-wide entitlements.</summary>
public sealed record RequestyKeyMetadata(string? KeyId, string? GroupId, string? ManagePermission,
    string? CompletionsPermission, DateTimeOffset? ExpiresAt, bool? IsUncapped);

/// <summary>Independent freshness for metadata returned separately from a quota or wallet measurement.</summary>
public sealed record QuotaMetadataObservationDto(string State, DateTimeOffset? ObservedAt,
    DateTimeOffset? LastSuccessAt, DateTimeOffset? FreshUntil, string? Error = null);
