using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Operations;
using Lain.Host.Providers;
using YamlDotNet.RepresentationModel;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var catalog = new ProviderCatalogRegistry(Directory.GetFiles(Path.Combine(root, "src/Lain.Host/Providers/Catalog"), "*.json").Select(p => ProviderCatalogRegistry.Parse(File.ReadAllText(p))));
var assertions = 0;
void Check(bool condition) { assertions++; if (!condition) throw new Exception("Provider transport assertion failed."); }
void Reject(Action action) { try { action(); } catch (ArgumentException) { assertions++; return; } catch (InvalidDataException) { assertions++; return; } throw new Exception("Expected invalid transport to fail."); }
var nv = catalog.Find("nvidia-build")!.Routes[0];
Reject(() => ProviderTransportValidation.Prepare(catalog,"nvidia-build",null,nv.BaseUrl));
Reject(() => ProviderTransportValidation.Prepare(catalog,"nvidia-build",nv.Id,"https://other.invalid/v1"));
Reject(() => ProviderTransportValidation.Prepare(catalog,"nvidia-build",nv.Id,nv.BaseUrl,new Dictionary<string,string>{{"apiKey","must-never-be-a-setting"}}));
var nvidia = ProviderTransportValidation.Prepare(catalog,"nvidia-build",nv.Id,nv.BaseUrl)!;
Check(nvidia.SourceSection == "openai-compatibility" && nvidia.CoreProvider == "nvidia-build-openai-chat");
foreach (var descriptor in catalog.Find("opencode-go")!.Routes)
{
    var options = descriptor.Protocol == "gemini" ? new Dictionary<string,string>{{"allowTrainingData","true"}} : null;
    if (options is not null) Reject(() => ProviderTransportValidation.Prepare(catalog,"opencode-go",descriptor.Id,descriptor.BaseUrl));
    var plan = ProviderTransportValidation.Prepare(catalog,"opencode-go",descriptor.Id,descriptor.BaseUrl,options)!;
    Check(plan.Headers["x-opencode-session"] == "$CPA-SESSION-ID" && plan.Headers["User-Agent"] == "lain/1.0");
    Check(descriptor.Protocol switch { "anthropic-messages" => plan.BaseUrl.EndsWith("/anthropic"), "gemini" => plan.BaseUrl.EndsWith("/google"), _ => plan.BaseUrl.EndsWith("/openai/v1") });
    var transport = JsonSerializer.SerializeToElement(new { coreProvider = plan.CoreProvider, providerRouteId = descriptor.Id, settings = options });
    var route = new RouteDto("fixture","fixture",descriptor.Model,"opencode-go",[],ProviderRouteId:descriptor.Id);
    var yaml = new YamlMappingNode();
    Check(ProviderTransportValidation.ValidateCompiled(catalog,route,transport,plan.SourceSection,plan.BaseUrl,yaml));
    Reject(() => ProviderTransportValidation.ValidateCompiled(catalog,route with { UpstreamModel = "wrong" },transport,plan.SourceSection,plan.BaseUrl,new()));
    Reject(() => ProviderTransportValidation.ValidateCompiled(catalog,route,transport,plan.SourceSection,plan.BaseUrl,new YamlMappingNode{{"headers",new YamlMappingNode{{"Authorization","Bearer untrusted"}}}}));
}
var cloudflare = catalog.Find("cloudflare-workers-ai")!.Routes[0];
Reject(() => ProviderTransportValidation.Prepare(catalog,"cloudflare-workers-ai",cloudflare.Id,cloudflare.BaseUrl));
var accountId = new string('a',32);
Check(ProviderTransportValidation.Prepare(catalog,"cloudflare-workers-ai",cloudflare.Id,GatewayRouteValidation.CloudflareBaseUrl(accountId),new Dictionary<string,string>{{"accountId",accountId}})!.BaseUrl.Contains(accountId));
var alibaba = catalog.Find("alibaba-model-studio")!.Routes[0];
var settings = new Dictionary<string,string>{{"region","ap-southeast-1"},{"workspaceId","workspace-demo"},{"serviceDeploymentScope","International"},{"billingPlan","payg"}};
Check(ProviderTransportValidation.Prepare(catalog,"alibaba-model-studio",alibaba.Id,"https://workspace-demo.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1",settings)!.BaseUrl.Contains("workspace-demo"));
Reject(() => ProviderTransportValidation.Prepare(catalog,"alibaba-model-studio",alibaba.Id,"https://workspace-demo.cn-beijing.maas.aliyuncs.com/compatible-mode/v1",settings));
var temporary = Path.Combine(Path.GetTempPath(),"lain-transport-"+Guid.NewGuid().ToString("N"));
try
{
    var paths = new OwnStatePaths(temporary); var secrets = new SecretStore(paths); var repository = new ConfigurationRepository(paths);
    var accounts = new AccountService(repository,secrets,catalog);
    var state = accounts.AddApi(repository.Snapshot.Revision,new("Fixture","nvidia-build","fake-provider-key",nv.BaseUrl,nv.Id));
    var account = state.Accounts.Single(); using var secret = JsonDocument.Parse(secrets.ReadAccount(account.Id));
    Check(secret.RootElement.GetProperty("providerRouteId").GetString() == nv.Id);
    Check(secret.RootElement.GetProperty("coreProvider").GetString() == nvidia.CoreProvider);
    Check(account.State == "unverified");
    var descriptor = catalog.Find("opencode-go")!.Routes.First(r => r.Protocol == "anthropic-messages");
    state = accounts.AddApi(state.Revision,new("OpenCode fixture","opencode-go","fake-second-key",descriptor.BaseUrl,descriptor.Id));
    var directAccount = state.Accounts.Single(a => a.Provider == "opencode-go");
    state = state with { Routes = [new("fixture-route","fixture-alias",descriptor.Model,"opencode-go",[directAccount.Id],ProviderRouteId:descriptor.Id)] };
    var writer = new ProxyConfigurationWriter(paths,secrets,new ProviderCostPolicy(catalog));
    writer.Write(state);
    Check(writer.QualifiedProviderRouteIds.Contains("fixture-route"));
    var yaml = File.ReadAllText(paths.ProxyConfig);
    Check(yaml.Contains("x-opencode-session") && yaml.Contains("$CPA-SESSION-ID") && yaml.Contains("/inference/go/anthropic"));
    var headers = "User-Agent\0lain/1.0\0x-opencode-session\0$CPA-SESSION-ID\0";
    Check(writer.RouteCredentials["fixture-route"][directAccount.Id] == ProxyConfigurationWriter.CoreCredentialId("claude-api-key","opencode-go","fake-second-key","https://opencode.ai/inference/go/anthropic","","",headers));
}
finally { if (Directory.Exists(temporary)) Directory.Delete(temporary,true); }
Console.WriteLine($"{assertions} synthetic provider transport assertions passed; no live requests.");
