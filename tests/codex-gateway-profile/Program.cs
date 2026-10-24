using System.Text.Json;
using Lain.Core;
using Lain.Host.Models;
using Lain.Host.Operations;

var temporary = Path.Combine(Path.GetTempPath(), "lain-codex-gateway-" + Guid.NewGuid().ToString("N"));
var assertions = 0;
void Check(bool condition, string name) { assertions++; if (!condition) throw new Exception(name); }
try
{
    var repository = new ConfigurationRepository(new OwnStatePaths(temporary));
    var state = repository.Update(0, c => c with {
        Accounts = [new("synthetic-account", "Fixture", "commandcode", true, "api-key", "unverified")],
        Routes = [new("flash", "mimo-v2.6-flash", "mimo-v2.6-flash", "commandcode", ["synthetic-account"]),
                  new("pro", "mimo-v2.6-pro", "mimo-v2.6-pro", "commandcode", ["synthetic-account"])],
        Profiles = [new("fixture", "Fixture", ["flash", "pro"], "pro")]
    });
    var adapter = new CodexProfileAdapter(new ModelsService(repository));
    var result = adapter.Generate("fixture", state.Revision, "/opt/lain/lain", "/tmp/lain.models.json");
    Check(result.ConfigFileName == "lain.config.toml" && result.LaunchArguments.SequenceEqual(new[] { "--no-daemon", "--profile", "lain" }), "Existing separate lain profile is preserved");
    Check(result.Configuration.Contains("model = \"mimo-v2.6-pro\""), "User-selected model is preserved");
    var search = result.Configuration.IndexOf("web_search = \"disabled\"", StringComparison.Ordinal);
    Check(search >= 0 && search < result.Configuration.IndexOf("[model_providers.lain]", StringComparison.Ordinal), "Native search disable is a top-level profile option");
    Check(!result.Configuration.Contains("mcp_servers") && !result.Configuration.Contains("plugins.") && !result.Configuration.Contains("multi_agent = false"), "MCP/plugins/agent capabilities are not silently removed");
    Check(result.Configuration.Contains("wire_api = \"responses\"") && result.Configuration.Contains("[model_providers.lain.auth]"), "Existing endpoint protocol and command auth remain intact");
    using var catalog = JsonDocument.Parse(result.Catalog);
    var entries = catalog.RootElement.GetProperty("models").EnumerateArray().ToArray();
    Check(entries.Select(m => m.GetProperty("slug").GetString()).SequenceEqual(new[] { "mimo-v2.6-flash", "mimo-v2.6-pro" }), "All selected routes remain in the model menu");
    foreach (var model in entries)
    {
        Check(!model.GetProperty("supports_search_tool").GetBoolean(), "No unsupported provider-hosted tool search claim");
        Check(model.GetProperty("tool_mode").ValueKind == JsonValueKind.Null, "No implicit code_mode_only requirement");
        Check(!model.TryGetProperty("apply_patch_tool_type", out var patch) || patch.ValueKind == JsonValueKind.Null, "No invented function enum or advertised custom patch capability");
    }
    Check(repository.Snapshot.Revision == state.Revision && repository.Snapshot.Routes.Length == 2, "Pure export does not modify model/runtime configuration");
    Check(!File.Exists(Path.Combine(temporary, "config.toml")), "No Codex base config is written");
}
finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
Console.WriteLine($"{assertions} Codex Gateway profile export assertions passed; no installed config or provider access.");
