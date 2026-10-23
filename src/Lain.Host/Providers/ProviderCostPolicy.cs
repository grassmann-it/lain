using Lain.Core;

namespace Lain.Host.Providers;

/// <summary>Policy validation is independent of provider marketing and never infers safety from a remaining balance.</summary>
public sealed class ProviderCostPolicy(ProviderCatalogRegistry catalog)
{
    public const string OpenRouterZeroCapEnforcement = "openrouter_zero_cap_v1";
    public static ProviderCostPolicy Embedded() => new(new ProviderCatalogRegistry());

    /// <summary>Names an implemented dispatch guard, not a stored assertion that the account has budget or access.
    /// The Core must obtain fresh same-key zero-cap proof on every dispatch; missing proof rejects inference.</summary>
    public string? RuntimeEnforcement(RouteDto route) => SupportsOpenRouterZeroCap(route, catalog.FindRoute(route.Provider, route.ProviderRouteId))
        ? OpenRouterZeroCapEnforcement : null;

    public RouteCostDecisionDto Evaluate(ConfigurationDto configuration, RouteDto route, RoutingPolicyDto? policyOverride = null)
    {
        var reasons = new List<string>();
        var policy = EffectivePolicy(route.Policy, policyOverride);
        var descriptor = catalog.FindRoute(route.Provider, route.ProviderRouteId);
        EvaluateRoute(route, descriptor, policy, reasons);
        VisitFallbacks(configuration, route, policy, new HashSet<string>(StringComparer.Ordinal), reasons);
        return new(route.Id, reasons.Count == 0, policy.CostPolicy, descriptor?.PricingClass ?? "unknown", reasons.Distinct().ToArray());
    }

    public void ValidateConfiguration(ConfigurationDto configuration)
    {
        foreach (var route in configuration.Routes) RequireAllowed(Evaluate(configuration, route));
        foreach (var profile in configuration.Profiles)
        {
            ValidatePolicy(profile.Policy);
            foreach (var id in profile.RouteIds)
            {
                var route = configuration.Routes.SingleOrDefault(r => r.Id == id) ?? throw new ArgumentException("Profile references an unknown route.");
                RequireAllowed(Evaluate(configuration, route, profile.Policy));
            }
        }
    }

    public RouteDto[] ResolveProfile(ConfigurationDto configuration, ProfileDto profile)
    {
        var routes = profile.RouteIds.Select(id => configuration.Routes.SingleOrDefault(r => r.Id == id)
            ?? throw new ArgumentException("Profile references an unknown route.")).ToArray();
        foreach (var route in routes)
        {
            RequireAllowed(Evaluate(configuration, route, profile.Policy));
            // Route fallbacks are stored explicitly, but the current qualified Core has no cross-route execution contract.
            // Reject rather than silently dropping them or permitting implicit provider overflow.
            if ((route.Policy?.FallbackRouteIds?.Length ?? 0) > 0 || (profile.Policy?.FallbackRouteIds?.Length ?? 0) > 0)
                throw new ArgumentException("Explicit route fallback execution is not yet qualified.");
        }
        return routes.Select(route => route with { Policy = EffectivePolicy(route.Policy, profile.Policy) }).ToArray();
    }

    public void ValidateRuntime(ConfigurationDto configuration)
    {
        // Runtime snapshots contain only selected routes; persisted profiles may legitimately refer to other routes.
        foreach (var route in configuration.Routes.Where(r => r.Enabled))
        {
            RequireAllowed(Evaluate(configuration, route));
            if ((route.Policy?.FallbackRouteIds?.Length ?? 0) > 0)
                throw new ArgumentException("Explicit route fallback execution is not yet qualified.");
        }
    }

    private void VisitFallbacks(ConfigurationDto configuration, RouteDto route, RoutingPolicyDto policy, HashSet<string> path, List<string> reasons)
    {
        if (!path.Add(route.Id)) { reasons.Add("fallback_cycle"); return; }
        foreach (var id in policy.FallbackRouteIds ?? [])
        {
            var fallback = configuration.Routes.SingleOrDefault(r => r.Id == id);
            if (fallback is null) { reasons.Add("unknown_fallback_route"); continue; }
            if (!fallback.Enabled) reasons.Add("disabled_fallback_route");
            var descriptor = catalog.FindRoute(fallback.Provider, fallback.ProviderRouteId);
            if (policy.CostPolicy != "free_only" && descriptor?.PricingClass != "free" && !policy.AllowPaidFallback)
                reasons.Add("paid_or_unknown_fallback_requires_opt_in");
            var inherited = new RoutingPolicyDto(policy.CostPolicy, fallback.Policy?.FallbackRouteIds, policy.AllowPaidFallback);
            var effective = EffectivePolicy(fallback.Policy, inherited);
            EvaluateRoute(fallback, descriptor, effective, reasons);
            VisitFallbacks(configuration, fallback, effective, path, reasons);
        }
        path.Remove(route.Id);
    }

    private static void EvaluateRoute(RouteDto route, ProviderRouteDescriptorDto? descriptor, RoutingPolicyDto policy, List<string> reasons)
    {
        try { ValidatePolicy(policy); }
        catch (ArgumentException) { reasons.Add("invalid_cost_policy"); return; }
        if (route.ProviderRouteId is not null && descriptor is null) reasons.Add("unknown_provider_route");
        if (descriptor is not null && !string.Equals(descriptor.Model, route.UpstreamModel, StringComparison.Ordinal))
            reasons.Add("provider_model_mismatch");
        if (policy.CostPolicy != "free_only") return;
        if (descriptor is null) reasons.Add("unclassified_route");
        if (descriptor?.PricingClass != "free") reasons.Add("route_not_permanently_free");
        if (descriptor?.FreeOnlyEligible != true) reasons.Add("free_only_not_documented");
        // A manifest is documentation, not a runtime cost guarantee. The narrow OpenRouter implementation
        // requires a separately validated transport plus a fresh server-side zero-USD key cap on EACH dispatch,
        // and a final outgoing payload/endpoint guard. It does not trust an account flag or the model suffix alone.
        if (!SupportsOpenRouterZeroCap(route, descriptor)) reasons.Add("upstream_cost_controls_unavailable");
    }

    private static bool SupportsOpenRouterZeroCap(RouteDto route, ProviderRouteDescriptorDto? descriptor) =>
        route.Provider == "openrouter" && descriptor is not null && descriptor.Model == route.UpstreamModel &&
        descriptor.PricingClass == "free" && descriptor.FreeOnlyEligible && descriptor.FreeOnlyEnforcementKind == "free_model_pin" &&
        descriptor.Protocol == "openai-chat" && descriptor.BaseUrl.TrimEnd('/') == "https://openrouter.ai/api/v1" &&
        route.UpstreamModel is "meta-llama/llama-3.3-70b-instruct:free" or "qwen/qwen3-coder:free";

    private static RoutingPolicyDto EffectivePolicy(RoutingPolicyDto? route, RoutingPolicyDto? profile)
    {
        route ??= new();
        if (profile is null) return route;
        // Never allow a profile's permissive policy to weaken an explicitly free-only route.
        var cost = route.CostPolicy == "free_only" || profile.CostPolicy == "free_only" ? "free_only" : profile.CostPolicy;
        if (route.CostPolicy is not ("configured" or "free_only")) cost = route.CostPolicy;
        return new(cost, (route.FallbackRouteIds ?? []).Concat(profile.FallbackRouteIds ?? []).Distinct(StringComparer.Ordinal).ToArray(),
            route.AllowPaidFallback || profile.AllowPaidFallback);
    }

    private static void ValidatePolicy(RoutingPolicyDto? policy)
    {
        if (policy is null) return;
        if (policy.CostPolicy is not ("configured" or "free_only")) throw new ArgumentException("Unknown cost policy.");
        var ids = policy.FallbackRouteIds ?? [];
        if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new ArgumentException("Fallback route IDs must be distinct and nonempty.");
    }

    private static void RequireAllowed(RouteCostDecisionDto decision)
    {
        if (!decision.Allowed) throw new ArgumentException("Route cost policy rejected: " + string.Join(", ", decision.Reasons) + ".");
    }
}
