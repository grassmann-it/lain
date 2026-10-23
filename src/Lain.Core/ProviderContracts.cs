namespace Lain.Core;

/// <summary>Declarative, secret-free provider documentation. Availability is separate from live account state.</summary>
public sealed record ProviderCatalogDto(int ContractVersion, DateTimeOffset ObservedAt, ProviderDescriptorDto[] Providers);

public sealed record ProviderDescriptorDto(
    string Id, string Name, string Summary, string AccountUrl, string DocumentationUrl,
    ProviderCredentialFieldDto[] Credentials, ProviderSetupStepDto[] SetupSteps,
    ProviderBillingPlanDto[] BillingPlans, ProviderRouteDescriptorDto[] Routes,
    ProviderSourceDto[] Sources, string[] Restrictions,
    string AdapterState = "available", string LiveVerification = "not_tested",
    string QuotaSupport = "unsupported");

public sealed record ProviderSourceDto(string Url, string Title, DateTimeOffset? CheckedAt = null, string? Notes = null);
public sealed record ProviderCredentialFieldDto(string Id, string Label, string Kind, bool Required = true, bool Secret = true, string? Help = null);
public sealed record ProviderSetupStepDto(string Title, string Description, string? Url = null);

/// <summary>Kind: free, trial, subscription, payg, or unknown. Scope and unit retain provider semantics.</summary>
public sealed record ProviderBillingPlanDto(
    string Id, string Name, string Kind, string Scope, string Unit,
    double? IncludedAmount = null, string? ResetDescription = null, DateTimeOffset? ExpiresAt = null,
    decimal? RecurringPrice = null, string? Currency = null, string? PriceInterval = null,
    ProviderLimitDto[]? Limits = null, string? Notes = null);

public sealed record ProviderLimitDto(string Kind, double? Value, string Unit, string? Window = null, string? Scope = null, string? Notes = null);

/// <summary>Null capabilities are unverified. A provider's general model name is not a route capability guarantee.</summary>
public sealed record RouteCapabilitiesDto(
    long? ContextWindow = null, long? MaximumOutput = null,
    bool? Tools = null, bool? Streaming = null, bool? Reasoning = null,
    bool? Images = null, bool? StructuredOutput = null, string[]? ReasoningModes = null);

public sealed record ProviderRouteDescriptorDto(
    string Id, string Model, string Name, string BaseUrl, string Protocol,
    string[] BillingPlanIds, RouteCapabilitiesDto Capabilities,
    string? ModelFamily = null, string? Manufacturer = null, string? InferenceProvider = null,
    string? Gateway = null, string PricingClass = "unknown", bool FreeOnlyEligible = false,
    string? FreeOnlyEnforcement = null, string? Publisher = null, string? PublisherType = null,
    ProviderSourceDto[]? Sources = null, string[]? Restrictions = null);

/// <summary>CostPolicy is configured or free_only. Fallback routes are explicit; unknown price never qualifies as free.</summary>
public sealed record RoutingPolicyDto(string CostPolicy = "configured", string[]? FallbackRouteIds = null);

public sealed record ProviderOnboardingDto(
    int ContractVersion, string ProviderId, string State, ProviderDescriptorDto Descriptor,
    string[] AccountIds, string[] MissingCredentialFields, string LiveVerification = "not_tested",
    string? Error = null);

public sealed record RouteCostDecisionDto(string RouteId, bool Allowed, string CostPolicy, string PricingClass, string[] Reasons);
