using System.Text.Json;
using Lain.Core;
using Lain.Host.Providers;

internal static class Program
{
    private static int assertions;
    private static readonly string[] RequiredProviders = [
        "nvidia-build", "stepfun", "poe", "opencode-go", "gemini-api", "inception", "openrouter", "requesty",
        "cloudflare-workers-ai", "groq", "mistral", "alibaba-model-studio", "nebius", "synthetic", "xai"
    ];

    public static void Main()
    {
        CheckEmbeddedCatalog();
        CheckFallbackGraphAndPolicyStrength();
        CheckMalformedMetadataCannotAuthorizeCost();
        Console.WriteLine($"Independent provider/routing integration: {assertions} assertions passed (synthetic only; no provider calls).");
    }

    private static void CheckEmbeddedCatalog()
    {
        var catalog = new ProviderCatalogRegistry();
        var snapshot = catalog.Snapshot();
        Check(RequiredProviders.All(id => snapshot.Providers.Any(provider => provider.Id == id)), "all fifteen prioritized providers are embedded and parseable");
        Check(snapshot.Providers.Any(provider => provider.Id == "groq") && snapshot.Providers.Any(provider => provider.Id == "xai"), "Groq and xAI/Grok retain separate provider identities");
        var policy = new ProviderCostPolicy(catalog);
        foreach (var provider in snapshot.Providers)
        {
            Check(provider.LiveVerification == "not_tested", $"{provider.Id}: catalog documentation does not claim a live credential test");
            Check(provider.Credentials.Any(field => field.Required) && provider.SetupSteps.Length > 0 && provider.Sources.Length > 0,
                $"{provider.Id}: no-key onboarding includes required credentials, setup and sources");
            Check(provider.Sources.All(source => source.CheckedAt.HasValue), $"{provider.Id}: official documentation carries a check date");
            foreach (var descriptor in provider.Routes)
            {
                var route = new RouteDto("route", "client-model", descriptor.Model, provider.Id, ["account"], ProviderRouteId: descriptor.Id, Policy: new("free_only"));
                var config = Configuration([route], provider.Id);
                var decision = policy.Evaluate(config, route);
                if (descriptor.PricingClass != "free" || !descriptor.FreeOnlyEligible)
                    Check(!decision.Allowed, $"{provider.Id}/{descriptor.Id}: paid, trial, subscription and unknown routes cannot satisfy free_only");
                else
                    Check(!decision.Allowed || descriptor.FreeOnlyEnforcement is { Length: > 0 }, $"{provider.Id}/{descriptor.Id}: a free-only approval requires a concrete enforcement mechanism");
                if (decision.Allowed)
                    Check(provider.Id == "openrouter" && descriptor.Model is "meta-llama/llama-3.3-70b-instruct:free" or "qwen/qwen3-coder:free" or "liquid/lfm-2.5-2.6b:free"
                        && descriptor.Protocol == "openai-chat" && descriptor.BaseUrl.TrimEnd('/') == "https://openrouter.ai/api/v1"
                        && policy.RuntimeEnforcement(route) == "openrouter_zero_cap_v1",
                        $"{provider.Id}/{descriptor.Id}: free-only intent is bounded to the concrete OpenRouter zero-cap model allowlist");
                Check(decision.RouteId == route.Id && decision.CostPolicy == "free_only", $"{provider.Id}/{descriptor.Id}: decision retains the requested route and strict policy");
            }
        }
        Check(catalog.Find("cloudflare-workers-ai")!.BillingPlans.All(plan => plan.Unit == "neurons"), "Cloudflare neurons are never relabeled as tokens");
        Check(catalog.Find("poe")!.BillingPlans.All(plan => plan.Unit == "points"), "Poe points retain their billing unit");
        Check(catalog.Find("stepfun")!.BillingPlans.Where(plan => plan.Kind == "subscription").All(plan => plan.Unit == "credits"), "Step Plan credits are not token allowances");
    }

    private static void CheckFallbackGraphAndPolicyStrength()
    {
        var policy = new ProviderCostPolicy(new([FixtureProvider()]));
        var free = Route("free", "free");
        var paid = Route("paid", "payg");
        var trial = Route("trial", "trial");
        var unknown = Route("unknown", "unknown");
        var config = Configuration([free, paid, trial, unknown]);
        Check(policy.Evaluate(config, paid).Allowed, "explicit configured PAYG primary route remains usable");
        foreach (var target in new[] { paid, trial, unknown })
        {
            var primary = free with { Policy = new("configured", [target.Id]) };
            var graph = config with { Routes = [primary, paid, trial, unknown] };
            var decision = policy.Evaluate(graph, primary);
            Check(!decision.Allowed && decision.Reasons.Contains("paid_or_unknown_fallback_requires_opt_in"), $"{target.Id}: fallback needs conscious paid/unknown opt-in");
            primary = primary with { Policy = new("configured", [target.Id], AllowPaidFallback: true) };
            graph = graph with { Routes = [primary, paid, trial, unknown] };
            Check(policy.Evaluate(graph, primary).Allowed, $"{target.Id}: explicit fallback authorization is represented in validation");
            Rejects(() => policy.ResolveProfile(graph, new("profile", "Fixture", [primary.Id])), $"{target.Id}: unqualified cross-route execution is not silently enabled by an allowed decision");
            primary = primary with { Policy = new("free_only", [target.Id], AllowPaidFallback: true) };
            graph = graph with { Routes = [primary, paid, trial, unknown] };
            Check(!policy.Evaluate(graph, primary).Allowed, $"{target.Id}: paid opt-in cannot weaken free_only across a fallback edge");
            Rejects(() => policy.ValidateRuntime(graph), $"{target.Id}: strict rejected graph cannot become a runtime snapshot");
        }
        var strict = free with { Policy = new("free_only") };
        var strictConfig = config with { Routes = [strict, paid, trial, unknown] };
        Check(!policy.Evaluate(strictConfig, strict, new("configured", AllowPaidFallback: true)).Allowed, "permissive profile override cannot weaken a strict route");
        Check(!policy.Evaluate(config, paid, new("free_only")).Allowed, "strict profile override rejects a configured paid route");
        Rejects(() => policy.ResolveProfile(config, new("profile", "Strict profile", [paid.Id], Policy: new("free_only"))), "profile resolution applies the strict profile policy");

        var cycleA = free with { Policy = new("configured", ["paid"], true) };
        var cycleB = paid with { Policy = new("configured", ["free"], true) };
        var cycleConfig = config with { Routes = [cycleA, cycleB] };
        Check(policy.Evaluate(cycleConfig, cycleA).Reasons.Contains("fallback_cycle"), "multi-route cycle is rejected instead of recursing forever");
        var missing = free with { Policy = new("configured", ["missing"], true) };
        Check(policy.Evaluate(config with { Routes = [missing] }, missing).Reasons.Contains("unknown_fallback_route"), "unknown fallback cannot disappear from validation");
        var disabled = paid with { Enabled = false };
        var toDisabled = free with { Policy = new("configured", ["paid"], true) };
        Check(policy.Evaluate(config with { Routes = [toDisabled, disabled] }, toDisabled).Reasons.Contains("disabled_fallback_route"), "disabled fallback cannot be routed through a valid primary");
        var forgedFree = Route("forged", "forged-free") with { Policy = new("free_only") };
        var forgedConfig = Configuration([forgedFree]);
        Check(!policy.Evaluate(forgedConfig, forgedFree).Allowed, "a forged documented-free label and enforcement string cannot authorize upstream cost");
        var inactiveStrict = forgedFree with { Enabled = false };
        policy.ValidateRuntime(Configuration([paid, inactiveStrict]));
        Check(true, "disabled strict route does not block an unrelated enabled configured route");
    }

    private static void CheckMalformedMetadataCannotAuthorizeCost()
    {
        var policy = new ProviderCostPolicy(new([FixtureProvider()]));
        var mismatch = Route("mismatch", "free") with { UpstreamModel = "same-family-different-route" };
        var decision = policy.Evaluate(Configuration([mismatch]), mismatch);
        Check(!decision.Allowed && decision.Reasons.Contains("provider_model_mismatch"), "matching model family cannot substitute a different concrete provider route");
        var missing = Route("missing", "not-in-catalog");
        Check(policy.Evaluate(Configuration([missing]), missing).Reasons.Contains("unknown_provider_route"), "unknown explicit provider route is not treated as legacy configured routing");
        foreach (var bad in new[] { "FREE_ONLY", "free", "", "payg" })
        {
            var route = Route("bad-policy", "payg") with { Policy = new(bad) };
            Check(!policy.Evaluate(Configuration([route]), route).Allowed, $"invalid cost policy '{bad}' fails closed");
        }
        var legacy = new RouteDto("legacy", "legacy", "unknown-model", "custom", ["account"]);
        Check(policy.Evaluate(Configuration([legacy], "custom"), legacy).Allowed, "existing explicitly configured custom route stays compatible");
        legacy = legacy with { Policy = new("free_only") };
        Check(!policy.Evaluate(Configuration([legacy], "custom"), legacy).Allowed, "unclassified legacy custom route cannot become free through a policy toggle");
        var duplicate = FixtureProvider() with { Routes = [FixtureProvider().Routes[0], FixtureProvider().Routes[0]] };
        Rejects(() => new ProviderCatalogRegistry([duplicate]), "duplicate route identities are rejected before policy lookup");
        var credentialUrl = FixtureProvider() with { AccountUrl = "https://user:password@fixture.invalid/account" };
        Rejects(() => new ProviderCatalogRegistry([credentialUrl]), "manifest links cannot carry credentials");
        var zeroContext = FixtureProvider() with { Routes = [FixtureProvider().Routes[0] with { Capabilities = new(ContextWindow: 0) }] };
        Rejects(() => new ProviderCatalogRegistry([zeroContext]), "unknown context capacity stays null instead of a fabricated zero");
    }

    private static RouteDto Route(string id, string descriptor) => new(id, "client-" + id, "fixture-" + descriptor, "fixture", ["account"], ProviderRouteId: descriptor);
    private static ConfigurationDto Configuration(RouteDto[] routes, string provider = "fixture") => new(1, 1,
        [new("account", "Synthetic account", provider, true, "api-key", "ready")], routes, []);

    private static ProviderDescriptorDto FixtureProvider()
    {
        var modes = new[] { "free", "trial", "subscription", "payg", "unknown" };
        var source = new ProviderSourceDto("https://fixture.invalid/docs", "Synthetic source", DateTimeOffset.Parse("2026-10-24T00:00:00Z"));
        var routes = modes.Select(kind => new ProviderRouteDescriptorDto(kind, "fixture-" + kind, "Synthetic " + kind,
            "https://fixture.invalid/v1", "openai-chat", [kind], new(Tools: true, Streaming: true),
            ModelFamily: "fixture-family", Manufacturer: "fixture-manufacturer", InferenceProvider: "fixture-inference", Gateway: "fixture",
            PricingClass: kind, FreeOnlyEligible: kind == "free", FreeOnlyEnforcement: kind == "free" ? "unqualified-fixture-claim" : null)).ToList();
        routes.Add(routes[0] with { Id = "forged-free", Model = "fixture-forged-free", FreeOnlyEnforcement = "forged-qualified-control" });
        return new("fixture", "Synthetic Provider", "No network access", "https://fixture.invalid/account", source.Url,
            [new("api-key", "API key", "api-key")], [new("Add a key", "Synthetic setup")],
            modes.Select(kind => new ProviderBillingPlanDto(kind, "Synthetic " + kind, kind, "account", "credits")).ToArray(),
            routes.ToArray(), [source], ["Synthetic test data only"]);
    }

    private static void Rejects(Action operation, string label)
    {
        var rejected = false;
        try { operation(); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or JsonException) { rejected = true; }
        Check(rejected, label);
    }
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        assertions++;
    }
}
