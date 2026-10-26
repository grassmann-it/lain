using System.Text.Json;
using Lain.Cli;
using Lain.Cli.Diagnostics;

static class RegressionCases
{
    public static async Task RunAsync()
    {
        if (DoctorEnvironment.MaximumHashBytes < 418673216) throw new Exception("real qualified CLI exceeds hash budget");
        var sparse = Path.Combine(Path.GetTempPath(), "lain-doctor-size-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var stream = File.Create(sparse)) stream.SetLength(DoctorEnvironment.MaximumHashBytes + 1);
            var refused = false;
            try { _ = new DoctorEnvironment().Sha256(sparse); } catch (IOException) { refused = true; }
            if (!refused) throw new Exception("over-budget binary accepted");
        }
        finally { File.Delete(sparse); }
        if (OperatingSystem.IsLinux())
        {
            var fifo = Path.Combine(Path.GetTempPath(), "lain-doctor-fifo-" + Guid.NewGuid().ToString("N"));
            using var maker = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/usr/bin/mkfifo") { ArgumentList = { fifo } })!;
            maker.WaitForExit();
            if (maker.ExitCode != 0) throw new Exception("FIFO fixture creation failed");
            try
            {
                var env = new DoctorEnvironment();
                foreach (var action in new Action[] { () => env.ReadText(fifo), () => env.Sha256(fifo) })
                {
                    var refused = false;
                    try { action(); } catch (IOException) { refused = true; }
                    if (!refused) throw new Exception("special file read was not refused");
                }
                Console.WriteLine("PASS special files refused before blocking read");
            }
            finally { File.Delete(fifo); }
        }
        var fixture = new MetadataFixture();
        async Task<DoctorReport> Read(string target = "codex-cli", string? project = null) => await new DoctorEngine(fixture).DiagnoseAsync(new(target, project));
        static void Check(DoctorReport report, string code, string state)
        { if (!report.Findings.Any(f => f.Code == code && f.State == state)) throw new Exception("regression " + code + " " + state); Console.WriteLine("PASS regression " + code + " " + state); }
        Check(await Read(), "client.version", "warning");
        Check(await Read(), "client.binary", "blocked");
        Check(await Read(), "client.runtime_helper", "blocked");
        fixture.Files["/metadata/codex-lain"] = "exists";
        fixture.Hashes["/metadata/codex-lain"] = "0d45eb864e1773cdd692194f3d1e7b2056614560444df5ed48e027733aab349a";
        fixture.Files["/metadata/manifest.json"] = fixture.Manifest("0.164.0", fixture.Hashes["/metadata/codex-lain"]);
        fixture.Files["/metadata/codex-code-mode-host"] = "exists";
        fixture.Hashes["/metadata/codex-code-mode-host"] = "wrong";
        Check(await Read(), "client.binary", "ok");
        Check(await Read(), "client.runtime_helper", "blocked");
        fixture.Hashes["/metadata/codex-code-mode-host"] = "8ee4f472e92ac226a189e98998633b2d07f989f208515750764db660369f2926";
        Check(await Read(), "client.runtime_helper", "ok");
        Check(await Read(), "client.provider", "unknown");
        fixture.Files["/codex/config.toml"] = "model_provider = \"lain\" # comment\n[model_providers.lain.auth]\ncommand = \"/helper\"\nargs = [\"login\"]\n";
        fixture.Files["/helper"] = "exists";
        Check(await Read(), "client.helper", "blocked");
        fixture.Files["/project/.codex/config.toml"] = "unrelated = true";
        Check(await Read(project: "/project"), "client.helper", "blocked");
        fixture.Files["/codex/config.toml"] = "model_provider = \"lain\"\n[model_providers.lain]\nauth = { command = \"/wrong\" }";
        var inline = await Read();
        Check(inline, "client.local_read", "unknown");
        if (inline.Findings.Any(f => f.Code == "client.provider" && f.State == "ok")) throw new Exception("inline auth false positive");
        Check(await Read("codex-vscode"), "client.version", "warning");
        Check(await Read("codex-vscode"), "client.vsix_selection", "ok");
        Check(await Read("codex-vscode"), "client.activation", "unknown");
    }

    private sealed class MetadataFixture : IDoctorEnvironment
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
        public string CodexHome => "/codex";
        public string MaintenanceDirectory => "/metadata";
        public string ExtensionsDirectory => "/extensions";
        public Dictionary<string, string> Files { get; } = new();
        public Dictionary<string, string> Hashes { get; } = new();
        public string Manifest(string version, string hash) => JsonSerializer.Serialize(new { schema = 3, patchedVersion = version, patchedSha256 = hash, patchedRelativePath = "codex-lain" });
        public MetadataFixture()
        {
            Files["/metadata/manifest.json"] = Manifest("0.163.0", "expected");
            Hashes["/metadata/codex-lain"] = "different";
            Files["/extensions/extensions.json"] = "[{\"identifier\":{\"id\":\"openai.chatgpt\"},\"version\":\"26.1021.22027\",\"relativeLocation\":\"selected\",\"location\":{\"scheme\":\"file\",\"path\":\"/extensions/selected\"}}]";
            Files["/extensions/selected/package.json"] = "{\"publisher\":\"openai\",\"name\":\"chatgpt\",\"version\":\"26.1021.22027\"}";
        }
        public Task<DoctorHost> ReadHostAsync(CancellationToken ct) => Task.FromResult(new DoctorHost(null, null));
        public string? ReadText(string path) => Files.GetValueOrDefault(path);
        public bool IsFile(string path) => Files.ContainsKey(path);
        public string? Sha256(string path) => Hashes.GetValueOrDefault(path);
        public CodexInstallationStatus Inspect(string directory, string? profile, long? revision) => new("unknown");
    }
}
