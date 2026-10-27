using Lain.Cli;
using Lain.Cli.Diagnostics;

static class ParserCases
{
    public static async Task RunAsync()
    {
        var fixture = new ParserFixture();
        static void Require(DoctorReport report, string code, string state)
        {
            if (!report.Findings.Any(f => f.Code == code && f.State == state)) throw new Exception("parser case " + code + " " + state);
        }
        var engine = new DoctorEngine(fixture);
        var report = await engine.DiagnoseAsync(new("codex-cli"));
        Require(report, "client.provider", "ok");
        Require(report, "client.helper", "unknown");
        Require(report, "client.catalog", "warning");
        Require(report, "client.effort", "ok");
        if (report.Findings.Any(f => f.Code == "client.local_read")) throw new Exception("unrelated tables suppressed valid observations");
        report = await engine.DiagnoseAsync(new("codex-cli", Profile: "named.profile"));
        Require(report, "client.provider", "blocked");
        report = await engine.DiagnoseAsync(new("codex-cli", Profile: "missing"));
        Require(report, "client.profile", "unknown");
        if (report.Findings.Any(f => f.Code == "client.provider" && f.State == "ok")) throw new Exception("missing profile fabricated model");
        fixture.Configuration = "model_provider = \"lain\"\n[profiles.test]\n'model_provider' = \"other\"\n";
        report = await engine.DiagnoseAsync(new("codex-cli", Profile: "test"));
        Require(report, "client.provider", "blocked");
        fixture.Configuration = "[projects.\"/tmp/path\"]\nmodel_provider = \"lain\"\nmodel = \"auto\"\n";
        report = await engine.DiagnoseAsync(new("codex-cli"));
        Require(report, "client.provider", "blocked");
        foreach (var multiline in new[] {
            "[plugins.\"x@y\"]\ntext = \"\"\"\n[profiles.fake]\nmodel_provider = \"lain\"\n\"\"\"",
            "[plugins.\"x@y\"]\nargs = [\n\"spoof\",\n]\n" })
        {
            fixture.Configuration = multiline;
            report = await engine.DiagnoseAsync(new("codex-cli", Profile: "fake"));
            Require(report, "client.local_read", "unknown");
            if (report.Findings.Any(f => f.Code == "client.provider" && f.State == "ok")) throw new Exception("multiline spoof");
        }
        Console.WriteLine("PASS parser common unrelated tables, quoted profiles, inline auth isolation, independent catalog and multiline guards");
    }

    private sealed class ParserFixture : IDoctorEnvironment
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
        public string CodexHome => "/fixture-parser";
        public string Configuration = """
            model_provider = "lain"
            model = "auto"
            model_reasoning_effort = "low"
            model_catalog_json = "/fixture-parser/selected.json"
            [model_providers."lain"]
            name = "lain"
            base_url = "http://127.0.0.1:18419/v1"
            wire_api = "responses"
            auth = { command = "/helper", args = ["auth", "token", "--audience", "codex"], timeout_ms = 5000, refresh_interval_ms = 300000 }
            [projects."/some/path.with.dots"]
            model_provider = "other"
            trust_level = "trusted"
            [plugins."x@y"]
            model = "spoof"
            args = ["a", "b"]
            [marketplaces."user@host"]
            model_reasoning_effort = "none"
            [mcp_servers."some.server"]
            command = "/unrelated"
            [[skills.config]]
            path = "/skills/test"
            model_provider = "other"
            [[skills.config]]
            enabled = false
            [profiles."named.profile"]
            model_provider = "other"
            """;
        public Task<DoctorHost> ReadHostAsync(CancellationToken ct) => Task.FromResult(new DoctorHost(null, null));
        public string? ReadText(string path) => path == "/fixture-parser/config.toml" ? Configuration : path == "/fixture-parser/selected.json" ?
            "{\"models\":[{\"slug\":\"auto\",\"supported_reasoning_levels\":[{\"effort\":\"low\"}]}]}" : null;
        public bool IsFile(string path) => false;
        public CodexInstallationStatus Inspect(string directory, string? profile, long? revision) => new("recovery-required");
    }
}
