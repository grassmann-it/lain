using System.Net;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Models;
using Lain.Host.Operations;
using YamlDotNet.RepresentationModel;

string? Option(string name)
{
    var index = Array.IndexOf(args, name);
    if (index < 0) return null;
    if (index + 1 == args.Length) throw new ArgumentException($"Missing {name} value.");
    return args[index + 1];
}
var export = Option("--export");
var endpoint = Option("--provider-url") ?? "http://127.0.0.1:19999/v1";
var upstreamProtocol = Option("--upstream-protocol") ?? "openai-chat";
if (upstreamProtocol is not ("openai-chat" or "openai-responses")) throw new ArgumentException("Unsupported synthetic upstream protocol.");
var proxyPort = int.Parse(Option("--proxy-port") ?? OwnStatePaths.ProxyPort.ToString());
if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) || !endpointUri.IsLoopback || endpointUri.Scheme != "http")
    throw new ArgumentException("The synthetic provider must use HTTP loopback.");
if (proxyPort is < 1 or > 65535) throw new ArgumentException("Invalid synthetic proxy port.");
var directory = Path.GetFullPath(export ?? Path.Combine(Path.GetTempPath(), "lain-host-contract-" + Guid.NewGuid().ToString("N")));
if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
    throw new ArgumentException("Fixture destination must be empty.");
var assertions = 0;
void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
Check(ProxyConfigurationWriter.CoreCredentialId("openai-compatibility", "fixture", "fixture-key", "http://127.0.0.1:19001/v1", "") == "openai-compatibility:fixture:951c56217412", "Historical compatibility credential IDs must retain their pinned hash.");
Check(ProxyConfigurationWriter.CoreCredentialId("claude-api-key", "fixture", "fixture-key", "https://example.invalid", "", "tenant", "User-Agent\0lain/1.0\0") == "claude:apikey:b9d93a4be279", "Historical direct credential IDs must retain prefix/header inputs.");
try
{
    var paths = new OwnStatePaths(directory);
    var secrets = new SecretStore(paths);
    foreach (var id in new[] { "acct-fixture-a", "acct-fixture-b", "acct-fixture-disabled" })
        secrets.WriteAccount(id, JsonSerializer.Serialize(new { kind = "api-key", sourceSection = "openai-compatibility", coreProvider = "fixture", apiKey = id + "-key", baseUrl = endpoint, sourceConfig = "upstream-protocol: " + upstreamProtocol + "\n" }));
    var state = new ConfigurationDto(7, 6,
        [new("acct-fixture-a", "First", "fixture", true, "api-key", "unverified"),
         new("acct-fixture-b", "Second", "fixture", true, "api-key", "unverified"),
         new("acct-fixture-disabled", "Disabled", "fixture", false, "api-key", "unverified")],
        [new("route-a", "fixture-strict-a", "fixture-model", "fixture", ["acct-fixture-a"]),
         new("route-b", "fixture-strict-b", "fixture-model", "fixture", ["acct-fixture-b"]),
         new("route-pool", "fixture-pool", "fixture-model", "fixture", ["acct-fixture-a", "acct-fixture-b"], Pooled: true),
         new("route-hidden", "fixture-hidden", "fixture-model", "fixture", ["acct-fixture-a"], Visible: false)], []);
    var writer = new ProxyConfigurationWriter(paths, secrets);
    var credentials = writer.Write(state);
    var expectedA = ProxyConfigurationWriter.CoreCredentialId("openai-compatibility", "fixture", "acct-fixture-a-key", endpoint, "");
    Check(credentials.Count == 2 && credentials["acct-fixture-a"] == expectedA, "Disabled credentials must not enter the core identity map.");
    var renamed = writer.Write(state with { Accounts = state.Accounts.Select(a => a with { Name = "Renamed" }).ToArray() });
    Check(renamed["acct-fixture-a"] == expectedA, "Display names must not change core identity.");
    var bindingsWriter = new RouteBindingsWriter(paths);
    bindingsWriter.Write(state, credentials, writer.RouteCredentials);
    using var bindings = JsonDocument.Parse(File.ReadAllText(bindingsWriter.BindingsPath));
    var routes = bindings.RootElement.GetProperty("routes");
    Check(routes.GetProperty("fixture-strict-a").GetProperty("credentials").GetProperty("acct-fixture-a").GetString() == expectedA, "Startup membership must use the generated credential identity.");
    Check(routes.GetProperty("fixture-pool").GetProperty("selection").GetString() == "pool" && routes.GetProperty("fixture-pool").GetProperty("accounts").GetArrayLength() == 2, "Pool membership must remain explicit.");
    Check(!routes.GetProperty("fixture-hidden").GetProperty("visible").GetBoolean(), "Hidden aliases must remain callable bindings.");
    var yaml = new YamlStream();
    using (var reader = File.OpenText(paths.ProxyConfig)) yaml.Load(reader);
    var configuration = (YamlMappingNode)yaml.Documents.Single().RootNode;
    var text = File.ReadAllText(paths.ProxyConfig);
    Check(text.Contains("upstream-protocol: " + upstreamProtocol), "The explicit upstream transport must survive Host rendering.");
    Check(text.Contains("openai-compatibility") && !text.Contains("acct-fixture-disabled-key"), "Only selected synthetic credentials may enter generated YAML.");
    // Only the fixture changes the fixed product listener. This is not a Host runtime setting.
    configuration.Children[new YamlScalarNode("port")] = new YamlScalarNode(proxyPort.ToString());
    using (var output = new StringWriter()) { yaml.Save(output, false); OwnStatePaths.WritePrivate(paths.ProxyConfig, output.ToString()); }
    var managementToken = secrets.GetOrCreateToken(paths.ManagementToken);
    using var handler = new LoginFixtureHandler(managementToken);
    using var login = new HttpCodexOwnedLoginBackend(paths, secrets, handler);
    var started = await login.BeginAsync(CancellationToken.None);
    Check(started.State == "fixture-state" && started.Url == "https://auth.example.invalid/fixture", "Login URL/state wire fields must remain compatible.");
    Check((await login.StatusAsync(started.State, CancellationToken.None)).State == "waiting", "Exchanging state maps to the existing Host waiting state.");
    var completed = await login.StatusAsync(started.State, CancellationToken.None);
    Check(completed.State == "completed" && completed.OwnedAuthFile == "fixture-owned.json", "Completed login must adopt only an owned filename reference.");
    await login.CancelAsync("cancel-state", CancellationToken.None);
    Check(handler.Calls == 4, "Every login operation must use the adapter-owned surface.");
    if (export is not null)
        OwnStatePaths.WritePrivate(Path.Combine(directory, "fixture.json"), JsonSerializer.Serialize(new { schemaVersion = 1, syntheticOnly = true, providerUrl = endpoint, upstreamProtocol, proxyPort, config = paths.ProxyConfig, bindings = bindingsWriter.BindingsPath, journal = Path.Combine(paths.Proxy, "events.jsonl"), ownedAuthDir = paths.OwnedAuth, managementTokenFile = paths.ManagementToken, accounts = credentials, model = "fixture-model", aliases = new[] { "fixture-strict-a", "fixture-strict-b", "fixture-pool", "fixture-hidden" } }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"PASS host/core compatibility: {assertions} assertions; synthetic files and in-memory HTTP only.");
}
finally { if (export is null && Directory.Exists(directory)) Directory.Delete(directory, true); }

sealed class LoginFixtureHandler(string token) : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization?.Scheme != "Bearer" || request.Headers.Authorization.Parameter != token)
            throw new Exception("Login must use the separate management bearer.");
        var expected = Calls++ switch
        {
            0 => (HttpMethod.Post, "/lain/login/codex", "{\"state\":\"fixture-state\",\"authorizationUrl\":\"https://auth.example.invalid/fixture\"}"),
            1 => (HttpMethod.Get, "/lain/login/fixture-state", "{\"status\":\"exchanging\"}"),
            2 => (HttpMethod.Get, "/lain/login/fixture-state", "{\"status\":\"completed\",\"ownedAuthFile\":\"fixture-owned.json\",\"accountId\":\"fixture-account\"}"),
            3 => (HttpMethod.Delete, "/lain/login/cancel-state", "{\"status\":\"canceled\"}"),
            _ => throw new Exception("Unexpected login operation.")
        };
        if (request.Method != expected.Item1 || request.RequestUri?.AbsolutePath != expected.Item2)
            throw new Exception("Login must retain the adapter-owned HTTP contract.");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(expected.Item3) });
    }
}
