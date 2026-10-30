using System.Text.Json;
using Lain.Cli.Diagnostics;
using Lain.Core;

static class MetadataCases
{
    public static async Task RunAsync()
    {
        var fixture = new MetadataFixture();
        async Task<DoctorReport> Read(string? profile = null) => await new DoctorEngine(fixture).DiagnoseAsync(new("codex-cli", Profile: profile));
        static void Check(DoctorReport report, string code, string state)
        {
            if (!report.Findings.Any(f => f.Code == code && f.State == state))
                throw new Exception("metadata case " + code + " " + state);
            Console.WriteLine("PASS metadata " + code + " " + state);
        }

        await fixture.SelectNative();
        var complete = await Read();
        Check(complete, "client.input_modalities", "ok");
        Check(complete, "client.context_window", "ok");
        Check(complete, "client.auto_compact", "ok");

        await fixture.SelectNative("text", omitContext: true);
        var regression = await Read();
        Check(regression, "client.input_modalities", "warning");
        Check(regression, "client.context_window", "warning");
        Check(regression, "client.auto_compact", "warning");

        await fixture.SelectGeneric("openrouter/free", "text", omitContext: true);
        var generic = await Read();
        Check(generic, "client.input_modalities", "unknown");
        Check(generic, "client.context_window", "unknown");
        Check(generic, "client.auto_compact", "unknown");
        if (generic.Findings.Any(f => f.State == "blocked" && f.Code.StartsWith("client.", StringComparison.Ordinal) &&
                f.Code is "client.input_modalities" or "client.context_window" or "client.auto_compact"))
            throw new Exception("generic free endpoint blocked");

        await fixture.SelectGeneric("gpt-6.1-sol-lookalike", "text", omitContext: true, provider: "other");
        Check(await Read(), "client.input_modalities", "unknown");

        await fixture.SelectNative("text", omitContext: true);
        fixture.Route = fixture.Route with { Enabled = false };
        fixture.ConfigText = fixture.ConfigText.Replace("model = \"native-sol\"", "model = \"disabled-sol\"");
        fixture.CatalogText = fixture.CatalogJson("disabled-sol", "[\"text\"]", null, null, null);
        Check(await Read(), "client.input_modalities", "unknown");

        await fixture.SelectNative();
        fixture.CatalogText = fixture.CatalogJson(fixture.Route.Alias, "[\"text\"]", "0", "1", "0");
        var malformed = await Read();
        Check(malformed, "client.input_modalities", "warning");
        Check(malformed, "client.context_window", "blocked");
        Check(malformed, "client.auto_compact", "blocked");

        fixture.CatalogText = fixture.CatalogJson(fixture.Route.Alias, "[\"text\"]", "0", "872000", "1000");
        var explicitCompact = await Read();
        Check(explicitCompact, "client.context_window", "blocked");
        Check(explicitCompact, "client.auto_compact", "ok");

        await fixture.SelectNative("text", omitContext: true);
        fixture.ConfigText = fixture.ConfigText.Replace("model_catalog_json", "model_auto_compact_token_limit = 1000\nmodel_catalog_json");
        Check(await Read(), "client.auto_compact", "ok");

        await fixture.SelectNative("text", omitContext: true);
        fixture.ConfigText = "profile = \"lain\"\n" + fixture.ConfigText + "\n[profiles.lain]\nmodel_context_window = 272000\nmodel_max_context_window = 300000\n";
        var overridden = await Read("lain");
        Check(overridden, "client.context_window", "ok");
        Check(overridden, "client.auto_compact", "ok");

        var serialized = JsonSerializer.Serialize(overridden);
        if (serialized.Contains("SECRET-TEST") || serialized.Contains("/metadata-case") || serialized.Contains("gpt-6.1-sol"))
            throw new Exception("unsafe metadata value leaked");
        Console.WriteLine("PASS metadata report privacy and scoped overrides");
    }

    private sealed class MetadataFixture : IDoctorEnvironment
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
        public string CodexHome => "/metadata-case/codex";
        public string Root => CodexHome + "/config.toml";
        public string Catalog => CodexHome + "/selected.json";
        public string ConfigText { get; set; } = "";
        public string CatalogText { get; set; } = "";
        public RouteDto Route { get; set; } = new("route", "native-sol", "gpt-6.1-sol", "codex", ["account"]);
        public StatusDto Status { get; set; } = new(1, "1.2.3", "ready", true, 1, 1, DateTimeOffset.UnixEpoch,
            ActiveProfileId: "active", ActiveProfileRevision: 1);
        public ConfigurationDto Configuration => new(1, 1,
            [new("account", "SECRET-TEST", "fixture", true, "api_key", "configured")],
            [Route], [new("active", "Lain", [Route.Id], Route.Id)]);
        public Dictionary<string, string> Files { get; } = new();
        public Task SelectNative(string modalities = "text+image", bool omitContext = false)
        {
            Route = Route with { Alias = "native-sol", Provider = "codex", Enabled = true, Visible = true };
            ConfigText = "model = \"native-sol\"\nmodel_provider = \"lain\"\nmodel_catalog_json = " + JsonSerializer.Serialize(Catalog) + "\napi_key = \"SECRET-TEST\"\n";
            CatalogText = CatalogJson(Route.Alias, modalities == "text+image" ? "[\"text\",\"image\"]" : "[\"text\"]",
                omitContext ? null : "272000", omitContext ? null : "872000", null);
            return Task.CompletedTask;
        }
        public Task SelectGeneric(string alias, string modalities = "text+image", bool omitContext = false,
            string provider = "other", bool enabled = true)
        {
            Route = Route with { Alias = alias, Provider = provider, Enabled = enabled, Visible = true };
            ConfigText = "model = " + JsonSerializer.Serialize(alias) + "\nmodel_provider = \"lain\"\nmodel_catalog_json = " + JsonSerializer.Serialize(Catalog) + "\n";
            CatalogText = CatalogJson(Route.Alias, modalities == "text+image" ? "[\"text\",\"image\"]" : "[\"text\"]",
                omitContext ? null : "272000", omitContext ? null : "872000", null);
            return Task.CompletedTask;
        }
        public string CatalogJson(string alias, string modalities, string? context, string? maximum, string? compact) => JsonSerializer.Serialize(new
        {
            models = new[] { BuildModel(alias, modalities, context, maximum, compact) }
        });
        private static Dictionary<string, object?> BuildModel(string alias, string modalities, string? context, string? maximum, string? compact)
        {
            var model = new Dictionary<string, object?> { ["slug"] = alias, ["input_modalities"] = JsonSerializer.Deserialize<string[]>(modalities) };
            if (context is not null) model["context_window"] = long.Parse(context);
            if (maximum is not null) model["max_context_window"] = long.Parse(maximum);
            if (compact is not null) model["auto_compact_token_limit"] = long.Parse(compact);
            return model;
        }
        public Task<DoctorHost> ReadHostAsync(CancellationToken ct) => Task.FromResult(new DoctorHost(Status, Configuration));
        public string? ReadText(string path) => path == Root ? ConfigText : path == Catalog ? CatalogText : null;
        public bool IsFile(string path) => false;
        public CodexInstallationStatus Inspect(string directory, string? profile, long? revision) => new("unknown");
    }
}
