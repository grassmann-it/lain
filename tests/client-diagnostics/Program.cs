using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lain.Cli;
using Lain.Cli.Diagnostics;
using Lain.Client;
using Lain.Core;

static void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
static bool Finding(DoctorReport report, string code, string state) => report.Findings.Any(f => f.Code == code && f.State == state);
var fixture = new Fixture();
var engine = new DoctorEngine(fixture);
var good = await engine.DiagnoseAsync(new("codex-cli"));
Check(Finding(good, "client.provider", "ok") && Finding(good, "client.catalog", "ok") && Finding(good, "client.effort", "ok"), "matching static root binding");
Check(Finding(good, "client.activation", "unknown") && Finding(good, "provider.acceptance", "unknown"), "static files never prove activation or authentication");
Check(good.ObservedAt == fixture.UtcNow && good.SchemaVersion == 1, "deterministic clock and schema");
Check(Finding(good, "accounts.routes", "warning"), "saved account without model route is visible setup gap");
fixture.Configuration = fixture.Configuration with { Routes = [new("route", "alias", "model", "fixture", ["account"])] };
Check(Finding(await engine.DiagnoseAsync(new("codex-cli")), "accounts.routes", "ok"), "configured visible route resolves account setup gap");
fixture.Configuration = fixture.Configuration with { Routes = [] };
fixture.HostUnavailable = true;
var offline = await engine.DiagnoseAsync(new("codex-cli"));
Check(Finding(offline, "host.reachable", "unknown") && Finding(offline, "client.provider", "ok"), "host failure retains local diagnosis");
fixture.HostUnavailable = false;
fixture.Status = fixture.Status with { RuntimeMode = "monitor", Revision = 2 };
fixture.Configuration = fixture.Configuration with { Accounts = [] };
var monitor = await engine.DiagnoseAsync(new("codex-cli"));
Check(Finding(monitor, "host.gateway", "blocked") && Finding(monitor, "accounts.setup", "blocked") && Finding(monitor, "profile.revision", "warning"), "monitor missing setup and unapplied revision differ");
fixture.Install = fixture.Install with { Stale = true };
Check(Finding(await engine.DiagnoseAsync(new("codex-cli")), "export.installed", "warning"), "stale export");
fixture.Install = fixture.Install with { State = "recovery-required" };
Check(Finding(await engine.DiagnoseAsync(new("codex-cli")), "export.installed", "blocked"), "interrupted export");
fixture.Install = new("unknown");
Check(Finding(await engine.DiagnoseAsync(new("codex-cli")), "export.installed", "unknown"), "missing export identity is unknown");
fixture.Install = new("installed", "lain", 1, "generation", false);
fixture.Files[fixture.Root] = fixture.Config.Replace("\"lain\"", "\"other\"").Replace("\"low\"", "\"none\"");
var wrong = await engine.DiagnoseAsync(new("codex-cli"));
Check(Finding(wrong, "client.provider", "blocked") && Finding(wrong, "client.effort", "blocked"), "wrong provider and invalid effort");
fixture.Files[fixture.Root] = fixture.Config.Replace(fixture.Catalog, "/different/catalog.json");
Check(Finding(await engine.DiagnoseAsync(new("codex-cli")), "client.catalog", "warning"), "wrong catalog");
fixture.Files[fixture.Root] = fixture.Config;
fixture.Files[fixture.Catalog] = "{\"models\":[{\"slug\":\"auto\",\"supported_reasoning_levels\":[]}]}";
Check(Finding(await engine.DiagnoseAsync(new("codex-cli")), "client.effort", "unknown"), "auto without qualified effort list");
fixture.Files[fixture.Root] = fixture.Config + "\n[model_providers.lain.auth]\ncommand = \"/missing/helper\"\n";
Check(Finding(await engine.DiagnoseAsync(new("codex-cli")), "client.helper", "blocked"), "missing declared helper");
fixture.Files[fixture.Root] = fixture.Config + "\n[profiles.other]\nmodel_provider = \"other\"\n";
Check(Finding(await engine.DiagnoseAsync(new("codex-cli", Profile: "other")), "client.provider", "blocked"), "explicit profile overrides root");
fixture.Files["/fixture/project/.codex/config.toml"] = "model_provider = \"other\"";
Check(Finding(await engine.DiagnoseAsync(new("codex-cli", "/fixture/project")), "workspace.binding", "unknown"), "workspace trust and precedence not fabricated");
var vscode = await engine.DiagnoseAsync(new("codex-vscode"));
Check(Finding(vscode, "client.activation", "unknown") && Finding(vscode, "client.version", "unknown"), "VSIX activation and client version unobserved");
fixture.Files[fixture.Root] = "model_provider = 'unsupported' # SECRET-TEST\n";
var malformed = await engine.DiagnoseAsync(new("codex-cli"));
Check(Finding(malformed, "client.local_read", "unknown") && !JsonSerializer.Serialize(malformed).Contains("SECRET-TEST"), "unsupported syntax and secret error sanitized");
fixture.Files[fixture.Root] = fixture.Config + "\napi_key = \"SECRET-TEST\"";
Check(!JsonSerializer.Serialize(await engine.DiagnoseAsync(new("codex-cli"))).Contains("SECRET-TEST"), "unrelated secret values never enter report");
var calls = 0;
LainApiClient NoHost() { calls++; throw new IOException("SECRET-TEST"); }
var output = new StringWriter(); var error = new StringWriter();
Check(await DoctorCommand.RunAsync(["--help"], output, error, NoHost) == 0 && calls == 0, "help independent of Host discovery");
foreach (var arguments in new[] { new[] { "--target", "other" }, new[] { "--json", "--json" }, new[] { "--project", "/fixture" }, new[] { "--target" }, new[] { "--full" } })
    Check(await DoctorCommand.RunAsync(arguments, new StringWriter(), new StringWriter(), NoHost) == 2, "strict usage parser");
output = new();
Check(await DoctorCommand.RunAsync(["--json"], output, error, NoHost) == 0 && !output.ToString().Contains("SECRET-TEST"), "offline service summary sanitized");
using var http = new HttpClient(new ReadOnlyHandler()) { BaseAddress = new Uri("http://fixture.invalid/") };
output = new();
Check(await DoctorCommand.RunAsync(["--json"], output, error, () => new LainApiClient(http)) == 0, "handler public metadata GETs only");
await RegressionCases.RunAsync();
Console.WriteLine("Doctor fixture suite complete.");

sealed class Fixture : IDoctorEnvironment
{
    public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-10-26T12:00:00Z");
    public string CodexHome => "/fixture/codex";
    public string Root => CodexHome + "/config.toml";
    public string Catalog => CodexHome + "/.lain-artifacts/generation/lain.models.json";
    public string Config => "model_provider = \"lain\"\nmodel = \"auto\"\nmodel_reasoning_effort = \"low\"\nmodel_catalog_json = " + JsonSerializer.Serialize(Catalog) + "\n";
    public Dictionary<string, string> Files { get; } = new();
    public bool HostUnavailable;
    public StatusDto Status = new(1, "1.2.3", "ready", true, 1, 1, DateTimeOffset.UnixEpoch, ActiveProfileId: "lain", ActiveProfileRevision: 1);
    public ConfigurationDto Configuration = new(1, 1, [new("account", "SECRET-TEST", "fixture", true, "api_key", "configured")], [], []);
    public CodexInstallationStatus Install = new("installed", "lain", 1, "generation", false);
    public Fixture() { Files[Root] = Config; Files[Catalog] = "{\"models\":[{\"slug\":\"auto\",\"supported_reasoning_levels\":[{\"effort\":\"low\"}]}]}"; }
    public Task<DoctorHost> ReadHostAsync(CancellationToken ct) => HostUnavailable ? throw new IOException("SECRET-TEST") : Task.FromResult(new DoctorHost(Status, Configuration));
    public string? ReadText(string path) => Files.GetValueOrDefault(path);
    public bool IsFile(string path) => Files.ContainsKey(path);
    public CodexInstallationStatus Inspect(string directory, string? profile, long? revision) => Install;
}
sealed class ReadOnlyHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Method != HttpMethod.Get) throw new Exception("mutation attempted");
        var fixture = new Fixture();
        object body = request.RequestUri!.AbsolutePath.EndsWith("/status") ? fixture.Status :
            request.RequestUri.AbsolutePath.EndsWith("/configuration") ? fixture.Configuration : throw new Exception("unexpected endpoint");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body, body.GetType()) });
    }
}
