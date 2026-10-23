using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lain.Client;
using Lain.Cli;
using Lain.Core;

static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
var assertions = 0;
var handler = new FakeHandler();
using var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:18418/") };
using var client = new LainApiClient(http, "HOST-TEST-TOKEN");
var status = new StatusDto(1, "test", "ready", true, 2, 2, DateTimeOffset.UtcNow);
handler.Handle = request =>
{
    Require(request.Headers.Authorization?.Parameter == "HOST-TEST-TOKEN", "host bearer is required");
    Require(request.RequestUri!.AbsolutePath == "/api/v1/status", "versioned status path");
    return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(status) };
};
Require((await client.StatusAsync()).Revision == 2, "typed status deserialize"); assertions++;

var query = new UsageQuery(DateTimeOffset.Parse("2026-10-23T00:00:00+02:00"), DateTimeOffset.Parse("2026-10-24T00:00:00+02:00"),
    "Europe/Berlin", "account &1", "a/b+c", 429, 20, 10);
var queryString = LainApiClient.QueryString(query);
Require(queryString.Contains("timeZone=Europe%2FBerlin") && queryString.Contains("accountId=account%20%261") &&
    queryString.Contains("model=a%2Fb%2Bc") && queryString.Contains("%2B02%3A00") && queryString.Contains("statusCode=429") &&
    queryString.Contains("offset=20&limit=10"), "query preserves all dimensions and escapes offset/model/account"); assertions++;

var configuration = new ConfigurationDto(3, 2, [], [], []);
handler.Handle = request =>
{
    Require(request.Method == HttpMethod.Put && request.RequestUri!.AbsolutePath == "/api/v1/routes/route-1", "route mutation uses typed action endpoint");
    using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
    Require(body.RootElement.GetProperty("revision").GetInt64() == 2 && body.RootElement.GetProperty("value").GetProperty("alias").GetString() == "test-alias", "revision envelope and route payload");
    return new(HttpStatusCode.OK) { Content = JsonContent.Create(configuration) };
};
Require((await client.SaveRouteAsync(2, new RouteDto("route-1", "test-alias", "test-model", "test", ["acct-1"]))).Revision == 3, "mutation response preserves applied revision difference"); assertions++;
handler.Handle = request =>
{
    Require(request.Method == HttpMethod.Delete && request.RequestUri!.PathAndQuery == "/api/v1/accounts/acct-1?revision=3", "account delete carries revision");
    return new(HttpStatusCode.OK) { Content = JsonContent.Create(configuration) };
};
await client.RemoveAccountAsync(3, "acct-1"); assertions++;
handler.Handle = request =>
{
    Require(request.RequestUri!.AbsolutePath == "/api/v1/status", "status path");
    return new(HttpStatusCode.OK) { Content = JsonContent.Create(status) };
};

var output = new StringWriter(); var error = new StringWriter();
var exit = await CliApplication.RunAsync(["status", "--json"], output, error, () => client);
Require(exit == 0 && error.ToString() == "", "JSON status succeeds without diagnostics");
Require(JsonDocument.Parse(output.ToString()).RootElement.GetProperty("revision").GetInt64() == 2, "JSON stdout is machine readable"); assertions++;

handler.Handle = request =>
{
    Require(request.RequestUri!.PathAndQuery == "/api/v1/auth/token?audience=codex", "dedicated inference token endpoint");
    return new(HttpStatusCode.OK) { Content = JsonContent.Create(new TokenResponse("INFERENCE-TEST-ONLY")) };
};
output = new(); error = new();
exit = await CliApplication.RunAsync(["auth", "token", "--audience", "codex"], output, error, () => client);
Require(exit == 0 && output.ToString() == "INFERENCE-TEST-ONLY" + Environment.NewLine && error.ToString() == "", "auth stdout contains only inference secret"); assertions++;

handler.Handle = _ => new(HttpStatusCode.Conflict) { Content = JsonContent.Create(new ApiError("revision_conflict", "Reload first.")) };
output = new(); error = new();
exit = await CliApplication.RunAsync(["accounts", "--json"], output, error, () => client);
Require(exit == 5 && output.ToString() == "" && error.ToString().Contains("revision_conflict"), "rejected operation keeps JSON stdout empty"); assertions++;

handler.Handle = _ => new(HttpStatusCode.Unauthorized) { Content = new StringContent("not-json HOST-TEST-TOKEN") };
output = new(); error = new();
exit = await CliApplication.RunAsync(["status", "--json"], output, error, () => client);
Require(exit == 4 && output.ToString() == "" && !error.ToString().Contains("HOST-TEST-TOKEN"), "non-JSON authorization failure never echoes raw response"); assertions++;

handler.Handle = _ => throw new HttpRequestException("secret exception detail");
output = new(); error = new();
exit = await CliApplication.RunAsync(["usage", "--today", "--json"], output, error, () => client);
Require(exit == 3 && output.ToString() == "" && error.ToString().Contains("host is unavailable") && !error.ToString().Contains("secret exception"), "host-down diagnostic is distinct and sanitized"); assertions++;

output = new(); error = new();
exit = await CliApplication.RunAsync(["usage", "--today", "--from", "2026-10-23T00:00:00Z"], output, error,
    () => throw new Exception("should validate before opening client"));
Require(exit == 2 && error.ToString().Contains("cannot be combined"), "ambiguous interval rejected before discovery"); assertions++;

handler.Handle = request =>
{
    var uri = Uri.UnescapeDataString(request.RequestUri!.Query);
    Require(uri.Contains("timeZone=Europe/Berlin") && uri.Contains("from=") && uri.Contains("to=") && uri.Contains("statusCode=429"), "today sends explicit timezone and bounds with status filter");
    return new(HttpStatusCode.OK) { Content = JsonContent.Create(new PageDto<AttemptDto>([], 0, 100, 0)) };
};
output = new(); error = new();
exit = await CliApplication.RunAsync(["attempts", "--today", "--timezone", "Europe/Berlin", "--status", "429", "--json"], output, error, () => client);
Require(exit == 0, "today attempts filters accepted"); assertions++;

handler.Handle = request =>
{
    Require(request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/v1/profiles/profile-1/apply", "apply profile endpoint");
    using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
    Require(payload.RootElement.GetProperty("revision").GetInt64() == 3, "apply uses optimistic revision");
    return new(HttpStatusCode.OK) { Content = JsonContent.Create(configuration with { AppliedRevision = 3 }) };
};
output = new(); error = new();
exit = await CliApplication.RunAsync(["profiles", "apply", "profile-1", "--revision", "3", "--json"], output, error, () => client);
Require(exit == 0 && JsonDocument.Parse(output.ToString()).RootElement.GetProperty("appliedRevision").GetInt64() == 3, "CLI apply emits effective configuration"); assertions++;

var codex = Path.Combine(Path.GetTempPath(), "lain-codex-" + Guid.NewGuid().ToString("N"));
var artifacts = new CodexProfileArtifacts("lain.config.toml", "synthetic generated config", "lain.models.json", "{\"models\":[]}", ["--no-daemon", "--profile", "lain"], 3);
try
{
    Directory.CreateDirectory(codex);
    File.WriteAllText(Path.Combine(codex, "config.toml"), "EXISTING-CONFIG");
    handler.Handle = request =>
    {
        Require(request.RequestUri!.AbsolutePath == "/api/v1/profiles/profile-1/codex/export", "Codex export endpoint");
        using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
        Require(payload.RootElement.GetProperty("executablePath").GetString() == "/absolute/lain" &&
            payload.RootElement.GetProperty("catalogPath").GetString() == Path.Combine(codex, "lain.models.json"), "Codex artifact request uses absolute helper/catalog paths");
        return new(HttpStatusCode.OK) { Content = JsonContent.Create(artifacts) };
    };
    var generated = await client.ExportCodexProfileAsync(3, "profile-1", "/absolute/lain", Path.Combine(codex, "lain.models.json"));
    var installed = CodexArtifactInstaller.Install(generated, codex);
    Require(File.ReadAllText(installed.ConfigurationPath) == artifacts.Configuration && File.ReadAllText(installed.CatalogPath) == artifacts.Catalog,
        "generated artifacts installed");
    Require(File.ReadAllText(Path.Combine(codex, "config.toml")) == "EXISTING-CONFIG" && Directory.GetFiles(codex).Length == 3,
        "ordinary config unchanged and temporary files removed");
    if (!OperatingSystem.IsWindows()) Require(File.GetUnixFileMode(installed.ConfigurationPath) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), "installed configuration is private");
    assertions++;
    try { CodexArtifactInstaller.Install(artifacts with { ConfigFileName = "config.toml" }, codex); throw new Exception("unexpected file must be rejected"); }
    catch (InvalidDataException) { assertions++; }
}
finally { if (Directory.Exists(codex)) Directory.Delete(codex, true); }

var state = Path.Combine(Path.GetTempPath(), "lain-discovery-" + Guid.NewGuid().ToString("N"));
var previous = Environment.GetEnvironmentVariable("LAIN_HOME");
try
{
    Environment.SetEnvironmentVariable("LAIN_HOME", state);
    using var offline = LainApiClient.CreateLocal();
    Require(LocalDiscovery.StateDirectory == state && !Directory.Exists(state), "offline discovery creates no directories or credentials"); assertions++;
    try { await offline.StatusAsync(); throw new Exception("missing token must fail"); }
    catch (IOException ex) { Require(ex.Message.Contains("Start the lain service"), "first-run token error"); }
    Directory.CreateDirectory(Path.Combine(state, "credentials"));
    File.WriteAllText(Path.Combine(state, "credentials", "host.token"), "test-token");
    Require(LocalDiscovery.ReadHostToken() == "test-token", "LAIN_HOME bearer discovery"); assertions++;
}
finally
{
    Environment.SetEnvironmentVariable("LAIN_HOME", previous);
    if (Directory.Exists(state)) Directory.Delete(state, true);
}
Console.WriteLine($"PASS: {assertions} client/CLI contract checks.");

sealed class FakeHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, HttpResponseMessage> Handle { private get; set; } = _ => throw new Exception("No test handler");
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(Handle(request));
}
