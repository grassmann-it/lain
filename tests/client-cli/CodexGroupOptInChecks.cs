using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lain.Client;
using Lain.Cli;

internal static class CodexGroupOptInChecks
{
    public static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool condition, string name) { checks++; if (!condition) throw new Exception(name); }
        var directory = Path.Combine(Path.GetTempPath(), "lain-codex-opt-in-" + Guid.NewGuid().ToString("N"));
        var handler = new Handler();
        using var http = new HttpClient(handler) { BaseAddress = new("http://fixture.invalid") };
        using var client = new LainApiClient(http);
        var calls = new List<string>();
        var expected = false;
        handler.Respond = request =>
        {
            calls.Add(request.Method + " " + request.RequestUri!.PathAndQuery);
            if (request.Method != HttpMethod.Post || request.RequestUri!.AbsolutePath != "/api/v1/profiles/host-work/codex/export")
                throw new Exception("Opt-in must never apply a profile or mutate configuration");
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var payload = body.RootElement;
            if (payload.GetProperty("enableLainModelPicker").GetBoolean() != expected || payload.GetProperty("revision").GetInt64() != 7)
                throw new Exception("Typed export must preserve explicit opt-in and revision");
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new CodexProfileArtifacts("lain.config.toml",
                "model = \"model-a\"\nmodel_provider = \"lain\"\nmodel_catalog_json = \"/tmp/lain.models.json\"\n", "lain.models.json",
                expected ? "{\"models\":[{\"slug\":\"model-a\"}],\"lain_model_picker\":{\"schema_version\":1,\"groups\":[]}}" : "{\"models\":[{\"slug\":\"model-a\"}]}",
                ["--no-daemon", "--profile", "lain"], 7, ProfileId: "host-work")) };
        };
        Check(!new CodexExportRequest(7, "/fixture/lain", "/fixture/models.json").EnableLainModelPicker, "Typed request defaults to the ordinary native export");
        await client.ExportCodexProfileAsync(7, "host-work", "/fixture/lain", "/fixture/models.json", CancellationToken.None);
        Check(calls.SequenceEqual(new[] { "POST /api/v1/profiles/host-work/codex/export" }), "Existing positional CancellationToken calls remain compatible and send opt-in false");
        calls.Clear(); expected = true;
        await client.ExportCodexProfileAsync(7, "host-work", "/fixture/lain", "/fixture/models.json", CancellationToken.None, enableLainModelPicker: true);
        Check(calls.SequenceEqual(new[] { "POST /api/v1/profiles/host-work/codex/export" }), "Explicit typed opt-in sends true through the existing export endpoint without applying");
        foreach (var action in new[] { "apply", "preview" })
        {
            var output = new StringWriter(); var error = new StringWriter();
            var exit = await CliApplication.RunAsync(["profiles", action, "host-work", "--model-groups"], output, error,
                () => throw new Exception("Invalid opt-in must fail before discovery"));
            Check(exit == 2 && error.ToString().Contains("only valid for profiles install-codex"), "Model groups are rejected for " + action + " before client discovery");
        }
        foreach (var arguments in new[] { new[] { "profiles", "--model-groups" }, new[] { "status", "--model-groups" },
            new[] { "profiles", "install-codex", "host-work", "--model-groups", "true" } })
        {
            var output = new StringWriter(); var error = new StringWriter();
            var exit = await CliApplication.RunAsync(arguments, output, error, () => throw new Exception("Invalid flag scope must fail before discovery"));
            Check(exit == 2 && output.ToString() == "", "The boolean opt-in is valid only on the install-codex action");
        }
        var helpOutput = new StringWriter();
        Check(await CliApplication.RunAsync(["help"], helpOutput, new StringWriter()) == 0 &&
            helpOutput.ToString().Contains("[--model-groups]") && helpOutput.ToString().Contains("lain-patched Codex") &&
            helpOutput.ToString().Contains("saved, qualified routers") && helpOutput.ToString().Contains("account:ID"), "Help explains the explicit patched-client opt-in and routing boundary");

        // The CLI installer resolves UserProfile; restrict the fixture to its disposable home
        // and fail before any install if the platform cannot honor that isolation.
        var previousHome = Environment.GetEnvironmentVariable("HOME");
        try
        {
            Directory.CreateDirectory(directory);
            Environment.SetEnvironmentVariable("HOME", directory);
            if (Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) != Path.GetFullPath(directory))
                throw new Exception("Codex install fixture requires an isolated user profile");
            var launchers = Path.Combine(directory, ".local", "bin");
            var codexDirectory = Path.Combine(directory, ".codex");
            Directory.CreateDirectory(launchers); Directory.CreateDirectory(codexDirectory);
            File.WriteAllText(Path.Combine(launchers, "lain"), "SYNTHETIC LAUNCHER");
            var mainLauncher = Path.Combine(launchers, "codex");
            File.WriteAllText(mainLauncher, "EXISTING MAIN CODEX LAUNCHER");
            var mainConfiguration = Path.Combine(codexDirectory, "config.toml");
            File.WriteAllText(mainConfiguration, "EXISTING MAIN CODEX CONFIG");
            foreach (var enabled in new[] { false, true })
            {
                calls.Clear(); expected = enabled;
                var output = new StringWriter(); var error = new StringWriter();
                var exit = await CliApplication.RunAsync(["profiles", "install-codex", "host-work", "--revision", "7", "--json",
                    .. (enabled ? new[] { "--model-groups" } : Array.Empty<string>())], output, error, () => client);
                Check(exit == 0 && error.ToString() == "" && calls.SequenceEqual(new[] { "POST /api/v1/profiles/host-work/codex/export" }),
                    "CLI install forwards only the explicit opt-in and performs no profile apply or configuration mutation");
                using var installedCatalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(codexDirectory, "lain.models.json")));
                Check(installedCatalog.RootElement.TryGetProperty("lain_model_picker", out _) == enabled &&
                    File.ReadAllText(mainConfiguration) == "EXISTING MAIN CODEX CONFIG" &&
                    File.ReadAllText(mainLauncher) == "EXISTING MAIN CODEX LAUNCHER", "Both installs preserve normal Codex config and main launcher; only the flag enables group metadata");
                using var result = JsonDocument.Parse(output.ToString());
                Check(result.RootElement.GetProperty("launchArguments").EnumerateArray().Select(value => value.GetString()).SequenceEqual(new[] { "--no-daemon", "--profile", "lain" }),
                    "The install result always launches only the separate lain Codex profile");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOME", previousHome);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        return checks;
    }

    private sealed class Handler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { private get; set; } = _ => throw new Exception("No fixture response");
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(Respond(request));
    }
}
