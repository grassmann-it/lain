using System.Text.Json;
using Lain.Cli;
using Lain.Client;
using Lain.Core;

// Explicit isolated-harness entry point only. Never run against the real user home.
internal static class NativeRunnerCase
{
    public static async Task RunAsync(string root, string executable)
    {
        if (root != "/mnt" || !File.Exists("/mnt/native-runner-sentinel.json"))
            throw new IOException("The native runner case requires its isolated outer harness.");
        var project = Path.Combine(root, "project");
        var contextFixture = File.Exists(Path.Combine(root, "context-mcp.py"));
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        var configuration = """
            model = "fixture-read"
            model_reasoning_effort = "low"
            model_provider = "lain"
            model_catalog_json = "/mnt/unused.models.json"
            web_search = "disabled"
            [model_providers.lain]
            name = "lain"
            base_url = "http://127.0.0.1:18580/v1"
            wire_api = "responses"
            [model_providers.lain.auth]
            command = "/mnt/auth"
            args = ["auth", "token", "--audience", "codex"]
            timeout_ms = 5000
            refresh_interval_ms = 300000
            """ + "\n";
        // Match CodexProfileAdapter's minimal exported model schema, rather than
        // borrowing extra native catalog fields that product exports do not contain.
        var catalog = JsonSerializer.Serialize(new {
            models = (contextFixture ? new[] { "fixture-read", "fixture-write", "fixture-inherit" } : new[] { "fixture-read", "fixture-write" }).Select((alias, index) => new {
                slug = alias, display_name = alias, description = "Synthetic worker fixture",
                default_reasoning_level = "low",
                supported_reasoning_levels = new[] { new { effort = "low", description = "Fixture low" } },
                shell_type = "unified_exec", visibility = "list", supported_in_api = true, priority = index,
                base_instructions = "Complete the requested fixture shell action, then return its result.",
                supports_reasoning_summaries = false, support_verbosity = false,
                truncation_policy = new { mode = "tokens", limit = 10000 }, supports_search_tool = false,
                tool_mode = (string?)null, experimental_supported_tools = Array.Empty<string>(), input_modalities = new[] { "text" }
            }).ToArray()
        });
        CodexArtifactInstaller.Install(new("lain.config.toml", configuration, "lain.models.json", catalog,
            [], 1, ProfileId: "fixture-profile"), codexHome, "fixture-profile", 1);
        var binding = WorkerNativeConfiguration.ReadInstalledBinding(codexHome, "fixture-profile", 1);
        var unknown = new ModelCapabilityEvidenceDto("unknown", "synthetic_native_fixture");
        WorkerResolvedTask TaskFor(string id, string[] writes)
        {
            var compatibility = new ModelCompatibilityDto("route-" + id, "codex", "upstream-" + id, null,
                "codex", "0.164.0", "responses", "responses", "synthetic_native_fixture",
                new(unknown, unknown, unknown, unknown, unknown, unknown, new(null, unknown), new(null, unknown), new(null, unknown)), []);
            return new(id, id, "Perform the synthetic " + id + " shell proof, then return the fixture final message.",
                "route-" + id, "fixture-" + id, "codex", ["synthetic-account"], "low", writes, compatibility);
        }
        if (contextFixture)
        {
            var baseline = new WorkerPlan(1, 1, "fixture-profile", 1, [TaskFor("inherit", [])], []);
            var inherited = await WorkerBatchRunner.RunAsync(project, baseline, executable, 60, Path.Combine(root, "runs-inherit"),
                _ => Task.CompletedTask, nativeBinding: binding);
            WorkerProjectFiles.WritePrivate(Path.Combine(root, "native-runner-inherit-result.json"), inherited);
            if (inherited.State != "succeeded" || inherited.Tasks.Single().NativeConfiguration is not { ContextMode: "inherit", ContextOverrides: null })
                throw new IOException("Inherited-context baseline or its configuration proof failed.");
        }
        var plan = new WorkerPlan(1, 1, "fixture-profile", 2, [
            TaskFor("read", []) with { ContextMode = contextFixture ? "native" : "inherit" },
            TaskFor("write", ["result.txt"]) with { ContextMode = contextFixture ? "native" : "inherit" }], []);
        var result = await WorkerBatchRunner.RunAsync(project, plan, executable, 60, Path.Combine(root, "runs"),
            _ => Task.CompletedTask, nativeBinding: binding);
        WorkerProjectFiles.WritePrivate(Path.Combine(root, "native-runner-result.json"), result);
        if (result.State != "succeeded" || result.Tasks.Any(task => task.NativeConfiguration is null))
            throw new IOException("Native workers or their configuration gates failed; inspect private receipts.");
        if (contextFixture && result.Tasks.Any(task => task.NativeConfiguration is not { ContextMode: "native", ContextOverrides: { } values } ||
                !values.Contains("mcp_servers.context_fixture.enabled=false") || !values.Contains("skills.include_instructions=false")))
            throw new IOException("Native-context receipts lack the verified per-server and skill overrides.");
        var write = result.Tasks.Single(task => task.Id == "write");
        if (write.VerifiedWorktree is not { } checkout || checkout.Path == project ||
            File.ReadAllText(Path.Combine(checkout.Path, "result.txt")).Trim() != "NATIVE_WRITE_OK" ||
            File.Exists(Path.Combine(project, "result.txt")) || File.Exists(Path.Combine(project, "read-only-denied.txt")))
            throw new IOException("Native sandbox/worktree side effects did not match the verified checkout.");
        if (WorkerBatchRunner.Status(Path.Combine(root, "runs"))?.State != "succeeded")
            throw new IOException("Native worker durable status disagrees with completed results.");
        Console.WriteLine(contextFixture
            ? "PASS native runner: inherited-context baseline, native-context receipts, read-only shell, isolated write worktree, tool continuations and durable results."
            : "PASS native runner: two config gates, read-only shell, isolated write worktree, tool continuations and durable results.");
    }
}
