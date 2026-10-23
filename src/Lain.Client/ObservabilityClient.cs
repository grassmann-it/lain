using Lain.Core;

namespace Lain.Client;

public sealed partial class LainApiClient
{
    public Task<UsageAnalysisDto> UsageAnalysisAsync(UsageQuery query, CancellationToken ct = default) =>
        Get<UsageAnalysisDto>("usage/analysis" + QueryString(query), ct);
    public Task<InspectorSnapshotDto> InspectorAsync(InspectorQuery? query = null, CancellationToken ct = default)
    {
        query ??= new InspectorQuery();
        ValidateInspectorLimit(query.Limit, 256);
        return Get<InspectorSnapshotDto>("inspector?limit=" + query.Limit.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            (query.RequestId is null ? "" : "&requestId=" + Id(query.RequestId)), ct);
    }
    public Task<InspectorEventPageDto> InspectorEventsAsync(InspectorEventQuery query, CancellationToken ct = default)
    {
        ValidateInspectorLimit(query.Limit, 500);
        if (query.After < 0) throw new ArgumentException("Inspector cursor must be nonnegative.", nameof(query));
        if (query.After > 0 && string.IsNullOrWhiteSpace(query.SourceId))
            throw new ArgumentException("A source ID is required when continuing an inspector cursor.", nameof(query));
        return Get<InspectorEventPageDto>("inspector/events?after=" + query.After.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            "&limit=" + query.Limit.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            (query.SourceId is null ? "" : "&sourceId=" + Id(query.SourceId)) +
            (query.RequestId is null ? "" : "&requestId=" + Id(query.RequestId)), ct);
    }
    private static void ValidateInspectorLimit(int limit, int maximum)
    {
        if (limit < 1 || limit > maximum) throw new ArgumentException($"Inspector limit must be between 1 and {maximum}.", nameof(limit));
    }
    public Task<ProviderCatalogDto> ProvidersAsync(CancellationToken ct = default) => Get<ProviderCatalogDto>("providers", ct);
    public Task<ProviderOnboardingDto> ProviderOnboardingAsync(string providerId, CancellationToken ct = default) =>
        Get<ProviderOnboardingDto>($"providers/{Id(providerId)}/onboarding", ct);
    public Task<RouteCostDecisionDto> RouteCostPolicyAsync(string routeId, string costPolicy = "free_only", CancellationToken ct = default)
    {
        if (costPolicy is not ("configured" or "free_only")) throw new ArgumentException("Cost policy must be configured or free_only.", nameof(costPolicy));
        return Get<RouteCostDecisionDto>($"routes/{Id(routeId)}/cost-policy?costPolicy={Id(costPolicy)}", ct);
    }
}
