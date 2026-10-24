using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Operations;
using Lain.Host.Models;
using Lain.Host.Providers;
using YamlDotNet.RepresentationModel;

string? Option(string name) { var i = Array.IndexOf(args, name); return i < 0 ? null : args[i + 1]; }
var export = Option("--export");
var selected = Option("--provider") ?? "sambanova";
var loopback = Option("--provider-url") ?? "http://127.0.0.1:19999/v1";
var port = int.Parse(Option("--proxy-port") ?? "19998");
if (!Uri.TryCreate(loopback, UriKind.Absolute, out var uri) || uri.Scheme != "http" || !uri.IsLoopback || uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "" || uri.AbsolutePath != "/v1")
    throw new ArgumentException("Fixture endpoint must be HTTP loopback /v1.");
if (port is < 1 or > 65535) throw new ArgumentException("Invalid fixture port.");
if (selected is not ("sambanova" or "upstage")) throw new ArgumentException("Unknown fixture provider.");
var count = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); count++; }
void Reject(Action action) { try { action(); } catch (ArgumentException) { count++; return; } catch (InvalidDataException) { count++; return; } throw new Exception("Expected transport rejection."); }
var catalog = new ProviderCatalogRegistry();
var cases = new[] { (Id: "sambanova", Model: "Meta-Llama-3.3-70B-Instruct", Url: "https://api.sambanova.ai/v1"), (Id: "upstage", Model: "solar-mini4-260922", Url: "https://api.upstage.ai/v1") };
foreach (var item in cases)
{
    var provider = catalog.Find(item.Id) ?? throw new Exception("Provider manifest missing.");
    var descriptor = provider.Routes.Single();
    Check(provider.DefaultAccountName == provider.Name, "Short provider default account name.");
    Check(provider.Connections is [{ Protocol: "openai-chat" }] && provider.Connections[0].BaseUrl == item.Url && provider.Connections[0].ProviderRouteIds.SequenceEqual(new[] { descriptor.Id }), "Connection metadata retains exact compatible model scope.");
    Check(provider.MonitorAccessOptions is [], "No unqualified monitor login advertised.");
    Check(provider.AdapterState == "available" && provider.LiveVerification == "not_tested" && provider.QuotaSupport == "unsupported", "Capability and live state stay separate.");
    Check(descriptor.Model == item.Model && descriptor.BaseUrl == item.Url && descriptor.Protocol == "openai-chat", "Official model and endpoint pinned.");
    Check(descriptor.Capabilities.Tools == true && descriptor.Capabilities.Streaming == true && descriptor.Capabilities.Images == false, "Only model-specific documented capabilities advertised.");
    Check(descriptor.Capabilities.Reasoning is null && descriptor.Capabilities.StructuredOutput is null, "Unqualified features stay unknown.");
    Check(!descriptor.FreeOnlyEligible && descriptor.FreeOnlyEnforcementKind == "unsupported", "No invented spend enforcement.");
    Check(provider.Credentials is [{ Required: true, Secret: true, Kind: "api-key" }], "One private key uses generic setup.");
    Check(provider.BillingPlans.All(p => p.IncludedAmount is null && p.ExpiresAt is null), "No account balance or expiry inferred from offer.");
    var plan = ProviderTransportValidation.Prepare(catalog, item.Id, descriptor.Id, item.Url)!;
    Check(plan.SourceSection == "openai-compatibility" && plan.CoreProvider == item.Id + "-openai-chat" && plan.Headers.Count == 0, "Standard private Chat transport selected.");
    Reject(() => ProviderTransportValidation.Prepare(catalog, item.Id, null, item.Url));
    Reject(() => ProviderTransportValidation.Prepare(catalog, item.Id, descriptor.Id, item.Url + ".evil"));
    Reject(() => ProviderTransportValidation.Prepare(catalog, item.Id, descriptor.Id, "https://other.invalid/v1"));
    Reject(() => ProviderTransportValidation.Prepare(catalog, item.Id, descriptor.Id, item.Url, new Dictionary<string, string> { ["apiKey"] = "synthetic-only" }));
    var transport = JsonSerializer.SerializeToElement(new { coreProvider = plan.CoreProvider, providerRouteId = descriptor.Id });
    var route = new RouteDto("route-a", "fixture-a", descriptor.Model, item.Id, [], ProviderRouteId: descriptor.Id);
    Check(ProviderTransportValidation.ValidateCompiled(catalog, route, transport, plan.SourceSection, plan.BaseUrl, new()), "Catalog route compiles.");
    Reject(() => ProviderTransportValidation.ValidateCompiled(catalog, route with { UpstreamModel = "unknown-model" }, transport, plan.SourceSection, plan.BaseUrl, new()));
    Check(!new ProviderCostPolicy(catalog).Evaluate(new(7, 0, [], [route], []), route with { Policy = new("free_only") }).Allowed, "Free-only rejects trial or tier assertions without enforcement.");
}
var directory = Path.GetFullPath(export ?? Path.Combine(Path.GetTempPath(), "lain-samba-upstage-" + Guid.NewGuid().ToString("N")));
if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new ArgumentException("Fixture destination must be empty.");
try
{
    // First compile the real catalog-bound setup. Only the resulting synthetic YAML
    // is redirected to loopback below; production endpoint validation is untouched.
    var paths = new OwnStatePaths(directory);
    var secrets = new SecretStore(paths);
    var repository = new ConfigurationRepository(paths);
    var accounts = new AccountService(repository, secrets, catalog);
    var descriptor = catalog.Find(selected)!.Routes.Single();
    var state = accounts.AddApi(repository.Snapshot.Revision, new("Fixture A", selected, "acct-fixture-a-key", descriptor.BaseUrl, descriptor.Id));
    var first = state.Accounts.Single();
    state = accounts.AddApi(state.Revision, new("Fixture B", selected, "acct-fixture-b-key", descriptor.BaseUrl, descriptor.Id));
    var second = state.Accounts.Single(a => a.Id != first.Id);
    Check(state.Accounts.All(a => a.State == "unverified"), "Saving key never claims working inference.");
    Check(!JsonSerializer.Serialize(state).Contains("acct-fixture-a-key"), "Public configuration excludes private keys.");
    state = state with { Routes = [new("route-a", "fixture-a", descriptor.Model, selected, [first.Id], ProviderRouteId: descriptor.Id), new("route-b", "fixture-b", descriptor.Model, selected, [second.Id], ProviderRouteId: descriptor.Id)] };
    var writer = new ProxyConfigurationWriter(paths, secrets, new(catalog));
    var credentials = writer.Write(state);
    Check(writer.QualifiedProviderRouteIds.SetEquals(new[] { "route-a", "route-b" }), "Both catalog-bound routes compile.");
    var bindingsWriter = new RouteBindingsWriter(paths);
    bindingsWriter.Write(state, credentials, writer.RouteCredentials, qualifiedProviderRouteIds: writer.QualifiedProviderRouteIds);
    var text = File.ReadAllText(paths.ProxyConfig);
    Check(text.Contains(descriptor.BaseUrl) && text.Contains(descriptor.Model) && text.Contains("upstream-protocol: openai-chat"), "Actual Host writer retains pinned URL/model/protocol.");
    var transport = ProviderTransportValidation.Prepare(catalog, selected, descriptor.Id, descriptor.BaseUrl)!;
    var keys = new Dictionary<string, string> { [first.Id] = "acct-fixture-a-key", [second.Id] = "acct-fixture-b-key" };
    Check(credentials.All(p => p.Value == ProxyConfigurationWriter.CoreCredentialId("openai-compatibility", transport.CoreProvider, keys[p.Key], descriptor.BaseUrl, "")), "Account identity matches selected private transport.");
    Reject(() => writer.Write(state with { Routes = [state.Routes[0] with { UpstreamModel = "unknown-model" }] }));
    var yaml = new YamlStream(); using (var input = new StringReader(text)) yaml.Load(input);
    var config = (YamlMappingNode)yaml.Documents.Single().RootNode;
    config.Children[new YamlScalarNode("port")] = new YamlScalarNode(port.ToString());
    config.Children[new YamlScalarNode("request-retry")] = new YamlScalarNode("0");
    foreach (var entry in ((YamlSequenceNode)config.Children[new YamlScalarNode("openai-compatibility")]).Children.Cast<YamlMappingNode>())
        entry.Children[new YamlScalarNode("base-url")] = new YamlScalarNode(loopback);
    using (var output = new StringWriter()) { yaml.Save(output, false); OwnStatePaths.WritePrivate(paths.ProxyConfig, output.ToString()); }
    // Credential hashes include the endpoint. Remap only the private synthetic
    // binding file so Core sees the same identities as its loopback transport.
    var bindings = File.ReadAllText(bindingsWriter.BindingsPath);
    var redirected = credentials.ToDictionary(p => p.Key, p => ProxyConfigurationWriter.CoreCredentialId("openai-compatibility", transport.CoreProvider, keys[p.Key], loopback, ""));
    foreach (var pair in credentials) bindings = bindings.Replace(pair.Value, redirected[pair.Key], StringComparison.Ordinal);
    OwnStatePaths.WritePrivate(bindingsWriter.BindingsPath, bindings);
    if (export is not null) OwnStatePaths.WritePrivate(Path.Combine(directory, "fixture.json"), JsonSerializer.Serialize(new { syntheticOnly = true, providerId = selected, providerUrl = loopback, officialUrl = descriptor.BaseUrl, model = descriptor.Model, config = paths.ProxyConfig, bindings = bindingsWriter.BindingsPath, journal = Path.Combine(paths.Proxy, "events.jsonl"), ownedAuthDir = paths.OwnedAuth, managementTokenFile = paths.ManagementToken, inferenceTokenFile = paths.InferenceToken, accounts = redirected, accountA = first.Id, accountB = second.Id }));
    Console.WriteLine($"PASS: {count} SambaNova/Upstage setup and Host transport assertions; synthetic keys only.");
}
finally { if (export is null && Directory.Exists(directory)) Directory.Delete(directory, true); }
