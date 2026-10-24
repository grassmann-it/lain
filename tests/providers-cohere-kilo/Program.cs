using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Operations;
using Lain.Host.Providers;

var catalog = new ProviderCatalogRegistry();
var checks = 0;
void Check(bool value, string boundary) { checks++; if (!value) throw new Exception(boundary); }
void Reject(Action action) { try { action(); } catch (ArgumentException) { checks++; return; } catch (InvalidDataException) { checks++; return; } throw new Exception("Invalid provider boundary accepted"); }
var temporary = Path.Combine(Path.GetTempPath(), "lain-cohere-kilo-" + Guid.NewGuid().ToString("N"));
try
{
    var paths = new OwnStatePaths(temporary);
    var repository = new ConfigurationRepository(paths);
    var secrets = new SecretStore(paths);
    var accounts = new AccountService(repository, secrets, catalog);
    var cost = new ProviderCostPolicy(catalog);
    foreach (var id in new[] { "cohere", "kilo" })
    {
        var provider = catalog.Find(id)!;
        Check(provider is not null, "Provider is embedded and discoverable");
        var route = provider!.Routes.Single();
        Check(provider.LiveVerification == "not_tested" && provider.QuotaSupport == "unsupported", "No inferred live/quota success");
        Check(provider.Credentials is [{ Id: "apiKey", Required: true, Secret: true }], "One private personal inference credential");
        Check(!route.FreeOnlyEligible && route.FreeOnlyEnforcementKind == "unsupported", "Free marketing does not enable strict policy");
        Check(route.Protocol == "openai-chat", "Exact documented wire protocol");
        Check(provider.Sources.All(s => s.CheckedAt?.Date == new DateTime(2026, 10, 24)), "Dated official sources");
        var plan = ProviderTransportValidation.Prepare(catalog, id, route.Id, route.BaseUrl)!;
        Check(plan.SourceSection == "openai-compatibility" && plan.CoreProvider == id + "-openai-chat" && plan.Headers.Count == 0, "Provider/protocol identity and personal Bearer transport");
        Reject(() => ProviderTransportValidation.Prepare(catalog, id, null, route.BaseUrl));
        Reject(() => ProviderTransportValidation.Prepare(catalog, id, route.Id, "https://other.invalid/v1"));
        Reject(() => ProviderTransportValidation.Prepare(catalog, id, route.Id, route.BaseUrl + "/chat/completions"));
        Reject(() => ProviderTransportValidation.Prepare(catalog, id, route.Id, route.BaseUrl, new Dictionary<string, string> { ["billingPlan"] = "payg" }));
        Reject(() => accounts.AddApi(repository.Snapshot.Revision, new(id, id, "synthetic-" + id, "https://other.invalid/v1", route.Id)));
        Check(repository.Snapshot.Accounts.Length == (id == "cohere" ? 0 : 1), "Rejected enrollment has no partial account");
        var state = accounts.AddApi(repository.Snapshot.Revision, new(id, id, "synthetic-" + id, route.BaseUrl, route.Id));
        var account = state.Accounts.Single(a => a.Provider == id);
        Check(account.State == "unverified", "Saving is not live verification");
        using var credential = JsonDocument.Parse(secrets.ReadAccount(account.Id));
        Check(credential.RootElement.GetProperty("baseUrl").GetString() == route.BaseUrl && credential.RootElement.GetProperty("coreProvider").GetString() == plan.CoreProvider, "Private credential binds exact official endpoint");
        var configured = new RouteDto(id + "-route", id + "-alias", route.Model, id, [account.Id], ProviderRouteId: route.Id);
        var configuration = state with { Routes = [configured] };
        Check(cost.Evaluate(configuration, configured).Allowed, "Configured route can execute");
        Check(!cost.Evaluate(configuration, configured with { Policy = new("free_only") }).Allowed && cost.RuntimeEnforcement(configured) is null, "No unqualified free_only escape");
        Check(!cost.Evaluate(configuration, configured with { UpstreamModel = route.Model + "-wrong" }).Allowed, "Model pin is exact");
    }
    var snapshot = repository.Snapshot;
    var routes = snapshot.Accounts.Select(a => {
        var r = catalog.Find(a.Provider)!.Routes.Single();
        return new RouteDto(a.Provider + "-route", a.Provider + "-alias", r.Model, a.Provider, [a.Id], ProviderRouteId: r.Id);
    }).ToArray();
    var writer = new ProxyConfigurationWriter(paths, secrets, cost);
    writer.Write(snapshot with { Routes = routes });
    Check(writer.QualifiedProviderRouteIds.Count == 2, "Both routes compile through real Host writer");
    Check(writer.RouteCredentials[routes[0].Id].Values.Single() != writer.RouteCredentials[routes[1].Id].Values.Single(), "Provider credentials stay isolated");
    var yaml = File.ReadAllText(paths.ProxyConfig);
    Check(yaml.Contains("https://api.cohere.ai/compatibility/v1") && yaml.Contains("https://ai-gateway.kilo.ai/api/v1"), "Official paths survive compilation");
    Check(yaml.Contains("command-a-plus-05-2026") && yaml.Contains("google/gemma-4-26b-a4b-it:free") && !yaml.Contains("kilo-auto/"), "Pinned models survive compilation without auto fallback");
    var kilo = catalog.Find("kilo")!;
    Check(kilo.Routes.Single().Capabilities.Tools is null && kilo.Routes.Single().Capabilities.Streaming is null, "Generic gateway support is not model-specific proof");
    Check(kilo.Routes.Single().PricingClass == "free" && kilo.BillingPlans.Single().Scope == "ip", "Free pricing and IP limits retain separate meaning");
    var cohere = catalog.Find("cohere")!;
    Check(cohere.BillingPlans.Single().Kind == "trial" && cohere.Routes.Single().PricingClass == "trial", "Evaluation is not permanent free/production access");
}
finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
Console.WriteLine($"{checks} Cohere/Kilo synthetic catalog, account, policy and compiler assertions passed; no vendor requests.");
