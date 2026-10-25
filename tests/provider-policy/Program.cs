using System.Text.Json;
using Lain.Core;
using Lain.Host.Models;
using Lain.Host.Operations;
using Lain.Host.Providers;

var assertions = 0;
var descriptor = new ProviderDescriptorDto("test-provider", "Test provider", "Synthetic policy fixture", "https://example.test/account", "https://example.test/docs",
    [new("apiKey", "API key", "api-key")], [], [new("free", "Free", "free", "account", "requests"), new("paid", "Paid", "payg", "account", "tokens")],
    [new("free-route", "free-model", "Free model", "https://example.test/v1", "openai-chat", ["free"], new(),
        "test-family", "test-manufacturer", "test-inference", "test-gateway", "free", true, "Documented free route", FreeOnlyEnforcementKind: "free_model_pin"),
     new("paid-route", "paid-model", "Paid model", "https://example.test/v1", "openai-chat", ["paid"], new(), PricingClass: "payg")],
    [new("https://example.test/docs", "Synthetic source", DateTimeOffset.UtcNow)], []);
var catalog = new ProviderCatalogRegistry([descriptor]);
var policy = new ProviderCostPolicy(catalog);
var account = new AccountDto("account", "Fixture", "test-provider", true, "api-key", "unverified");
var free = new RouteDto("free", "free-alias", "free-model", "test-provider", ["account"], ProviderRouteId: "free-route");
var paid = new RouteDto("paid", "paid-alias", "paid-model", "test-provider", ["account"], ProviderRouteId: "paid-route");
var legacy = new RouteDto("legacy", "legacy-alias", "legacy-model", "test-provider", ["account"]);
var config = new ConfigurationDto(0, 0, [account], [free, paid, legacy], []);
Check(policy.Evaluate(config, legacy).Allowed, "legacy configured behavior preserved");
Check(policy.Evaluate(config, paid).Allowed, "explicit configured paid primary allowed");
var strict = new RoutingPolicyDto("free_only");
Check(!policy.Evaluate(config, legacy, strict).Allowed, "unknown legacy model cannot become free");
Check(policy.Evaluate(config, paid, strict).Reasons.Contains("route_not_permanently_free"), "paid primary denied");
Check(policy.Evaluate(config, free, strict).Reasons.Contains("upstream_cost_controls_unavailable"), "free label and model pin cannot impersonate runtime enforcement");
foreach (var pricing in new[] { "trial", "subscription", "payg", "unknown" })
{
    var alternative = descriptor with { Routes = [descriptor.Routes[0] with { PricingClass = pricing }] };
    var evaluator = new ProviderCostPolicy(new([alternative]));
    Check(evaluator.Evaluate(config, free, strict).Reasons.Contains("route_not_permanently_free"), pricing + " cannot masquerade as permanent free");
}
var fallbacks = free with { Policy = new("configured", [paid.Id]) };
Check(policy.Evaluate(config, fallbacks).Reasons.Contains("paid_or_unknown_fallback_requires_opt_in"), "paid fallback requires explicit opt-in");
fallbacks = fallbacks with { Policy = fallbacks.Policy! with { AllowPaidFallback = true } };
Check(policy.Evaluate(config, fallbacks).Allowed, "explicit paid fallback configured policy accepted for storage");
Check(!policy.Evaluate(config, fallbacks, strict).Allowed, "paid fallback opt-in never weakens free-only");
Check(policy.Evaluate(config, free with { Policy = new("configured", [legacy.Id]) }).Reasons.Contains("paid_or_unknown_fallback_requires_opt_in"), "unknown fallback denied without opt-in");
Check(policy.Evaluate(config, free with { Policy = new("configured", ["missing"]) }).Reasons.Contains("unknown_fallback_route"), "unknown fallback identity rejected");
Check(!policy.Evaluate(config, free with { Policy = new("typo") }).Allowed, "unrecognized policy rejected");
Check(!policy.Evaluate(config, free with { ProviderRouteId = "missing" }).Allowed, "missing descriptor cannot fabricate dimensions");
Check(policy.Evaluate(config, free with { UpstreamModel = "paid-model" }).Reasons.Contains("provider_model_mismatch"), "free descriptor cannot be attached to a paid model");
var cyclic = config with { Routes = [free with { Policy = new("configured", [paid.Id], true) }, paid with { Policy = new("configured", [free.Id], true) }] };
Check(policy.Evaluate(cyclic, cyclic.Routes[0]).Reasons.Contains("fallback_cycle"), "fallback cycle rejected");
var fallbackConfig = config with { Routes = [fallbacks, paid] };
Throws<ArgumentException>(() => policy.ResolveProfile(fallbackConfig, new("p", "Profile", [free.Id])), "unsupported fallback execution cannot silently disappear");
Throws<ArgumentException>(() => policy.ResolveProfile(config, new("p", "Profile", [paid.Id], Policy: strict)), "profile free-only enforced");
Throws<ArgumentException>(() => policy.ValidateRuntime(config with { Routes = [free with { Policy = strict }] }), "runtime policy catches direct writer callers");
Throws<ArgumentException>(() => RouteBindings.Compile(config with { Routes = [free with { Policy = strict }] }, new Dictionary<string, string>(), catalog: catalog), "bindings cannot bypass free-only");

var bindings = RouteBindings.Compile(config with { Routes = [free] }, new Dictionary<string, string> { [account.Id] = "private-core-credential" }, catalog: catalog, profileId: "profile", qualifiedProviderRouteIds: new HashSet<string> { free.Id });
Check(bindings.Routes[free.Alias].Dimensions?.Manufacturer == "test-manufacturer", "proven route dimensions carried");
Check(bindings.Routes[free.Alias].Dimensions?.ProfileId == "profile", "applied profile identity carried");
Check(bindings.Routes[free.Alias].CostPolicy == "configured", "runtime policy explicit");
var unqualified = RouteBindings.Compile(config with { Routes = [free] }, new Dictionary<string, string> { [account.Id] = "private-core-credential" }, catalog: catalog);
Check(unqualified.Routes[free.Alias].Dimensions is null && unqualified.Routes[free.Alias].PricingClass == "unknown", "catalog label alone cannot assert runtime provenance or price");
var legacyBinding = JsonSerializer.Deserialize<CoreRouteBinding>("{\"id\":\"old\",\"model\":\"model\",\"accounts\":[\"account\"],\"selection\":\"strict\"}");
Check(legacyBinding?.CostPolicy == "configured", "legacy bindings default configured");
Check(ProxyConfigurationWriter.CoreCredentialId("claude-api-key", "provider", "key", "https://example.test", "") !=
    ProxyConfigurationWriter.CoreCredentialId("claude-api-key", "provider", "key", "https://example.test", "", sortedHeaders: "User-Agent\0lain/1.0\0"),
    "qualified direct headers participate in core credential identity");
var guardedDescriptor = descriptor with
{
    Id = "openrouter",
    Routes = [descriptor.Routes[0] with { Id = "guarded-route", Model = "qwen/qwen3-coder:free", BaseUrl = "https://openrouter.ai/api/v1" }]
};
var guardedCatalog = new ProviderCatalogRegistry([guardedDescriptor]);
var guardedPolicy = new ProviderCostPolicy(guardedCatalog);
var guardedRoute = free with { Provider = "openrouter", ProviderRouteId = "guarded-route", UpstreamModel = "qwen/qwen3-coder:free", Policy = strict };
var guardedConfig = config with { Accounts = [account with { Provider = "openrouter" }], Routes = [guardedRoute] };
Check(guardedPolicy.Evaluate(guardedConfig, guardedRoute).Allowed, "guarded OpenRouter configuration intent accepted pending fresh runtime key proof");
Check(guardedPolicy.RuntimeEnforcement(guardedRoute) == "openrouter_zero_cap_v1", "only named runtime guard enables the concrete free route");
Throws<ArgumentException>(() => RouteBindings.Compile(guardedConfig, new Dictionary<string, string> { [account.Id] = "core" }, catalog: guardedCatalog),
    "supported free intent still needs actual transport qualification");
var guardedBindings = RouteBindings.Compile(guardedConfig, new Dictionary<string, string> { [account.Id] = "core" }, catalog: guardedCatalog,
    qualifiedProviderRouteIds: new HashSet<string> { guardedRoute.Id });
Check(guardedBindings.Routes[guardedRoute.Alias].FreeOnlyEnforcement == "openrouter_zero_cap_v1", "free runtime binding requires named key-proof and final-request guard");
foreach (var model in new[] { "meta-llama/llama-3.3-70b-instruct:free", "qwen/qwen3-coder:free", "liquid/lfm-2.5-2.6b:free" })
{
    var modelDescriptor = guardedDescriptor with { Routes = [guardedDescriptor.Routes[0] with { Model = model }] };
    var modelPolicy = new ProviderCostPolicy(new([modelDescriptor]));
    var modelRoute = guardedRoute with { UpstreamModel = model };
    Check(modelPolicy.Evaluate(guardedConfig with { Routes = [modelRoute] }, modelRoute).Allowed && modelPolicy.RuntimeEnforcement(modelRoute) == "openrouter_zero_cap_v1",
        "exact allowlisted model retains mandatory zero-cap guard: " + model);
    var kiloDescriptor = modelDescriptor with { Id = "kilo" };
    var kiloPolicy = new ProviderCostPolicy(new([kiloDescriptor]));
    var kiloRoute = modelRoute with { Provider = "kilo" };
    var kiloConfig = guardedConfig with { Accounts = [account with { Provider = "kilo" }], Routes = [kiloRoute] };
    Check(!kiloPolicy.Evaluate(kiloConfig, kiloRoute).Allowed && kiloPolicy.RuntimeEnforcement(kiloRoute) is null,
        "same model on Kilo cannot inherit OpenRouter enforcement: " + model);
}
foreach (var invalid in new[]
{
    guardedDescriptor.Routes[0] with { BaseUrl = "https://other.example.test/v1" },
    guardedDescriptor.Routes[0] with { Model = "openrouter/free" },
    guardedDescriptor.Routes[0] with { Model = "unqualified/new-model:free" },
    guardedDescriptor.Routes[0] with { Model = "liquid/lfm-2.5-2.6b" },
    guardedDescriptor.Routes[0] with { Model = "liquid/lfm-2.6b:free" },
    guardedDescriptor.Routes[0] with { Protocol = "openai-responses" }
})
{
    var invalidPolicy = new ProviderCostPolicy(new([guardedDescriptor with { Routes = [invalid] }]));
    Check(!invalidPolicy.Evaluate(guardedConfig, guardedRoute with { UpstreamModel = invalid.Model }).Allowed,
        "unqualified gateway/variant/protocol cannot inherit OpenRouter guard eligibility");
}
descriptor.Routes[0] = descriptor.Routes[0] with { PricingClass = "payg" };
Check(catalog.FindRoute("test-provider", "free-route")?.PricingClass == "free", "catalog constructor copies mutable arrays");
var snapshot = catalog.Snapshot(); snapshot.Providers[0].Routes[0] = snapshot.Providers[0].Routes[0] with { Model = "edited" };
Check(catalog.FindRoute("test-provider", "free-route")?.Model == "free-model", "catalog snapshot cannot mutate registry");
Throws<InvalidDataException>(() => new ProviderCatalogRegistry([descriptor, descriptor]), "duplicate provider rejected");
Throws<InvalidDataException>(() => ProviderCatalogRegistry.Parse("{}"), "missing manifest properties rejected");
Throws<InvalidDataException>(() => new ProviderCatalogRegistry([descriptor with { AccountUrl = "https://secret@example.test" }]), "credential-bearing URL rejected");
Check(new ProviderCatalogRegistry([descriptor with { Routes = [descriptor.Routes[0] with { BaseUrl = "https://{WorkspaceId}.example.test/v1" }] }]).Snapshot().Providers.Length == 1,
    "documented workspace hostname template can be loaded");
Throws<InvalidDataException>(() => new ProviderCatalogRegistry([descriptor with { Routes = [descriptor.Routes[0] with { BaseUrl = "https://{unknown}.example.test/v1" }] }]),
    "unrecognized endpoint templates rejected");

var directory = Path.Combine(Path.GetTempPath(), "lain-policy-" + Guid.NewGuid().ToString("N"));
try
{
    var paths = new OwnStatePaths(directory);
    var repository = new ConfigurationRepository(paths);
    var saved = repository.Update(0, _ => config);
    var service = new ModelsService(repository, policy);
    Throws<RevisionConflictException>(() => service.PutRoute(0, free with { Policy = strict }), "revision conflict precedes policy validation");
    Throws<ArgumentException>(() => service.PutRoute(saved.Revision, free with { Policy = strict }), "unsafe policy mutation rejected");
    Check(repository.Snapshot.Revision == saved.Revision, "rejected policy leaves revision unchanged");
    saved = service.PutRoute(saved.Revision, fallbacks);
    var mutable = repository.Snapshot;
    mutable.Routes.Single(r => r.Id == free.Id).Policy!.FallbackRouteIds![0] = "changed";
    Check(repository.Snapshot.Routes.Single(r => r.Id == free.Id).Policy!.FallbackRouteIds![0] == paid.Id, "policy arrays do not alias repository state");
    Throws<ArgumentException>(() => service.DeleteRoute(saved.Revision, paid.Id), "referenced fallback cannot be removed");
    var writer = new ProxyConfigurationWriter(paths, new SecretStore(paths), policy);
    Throws<ArgumentException>(() => writer.Write(config with { Routes = [free with { Policy = strict }] }), "proxy writer rejects before accessing credentials");
    Check(!File.Exists(paths.ProxyConfig), "rejected policy does not emit a runtime configuration");
}
finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
Console.WriteLine($"{assertions} provider policy assertions passed.");

void Check(bool condition, string label) { assertions++; if (!condition) throw new Exception(label); }
void Throws<T>(Action action, string label) where T : Exception
{
    assertions++;
    try { action(); }
    catch (T) { return; }
    throw new Exception(label);
}
