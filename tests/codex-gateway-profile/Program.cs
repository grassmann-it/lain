using System.Text.Json;
using Lain.Core;
using Lain.Host.Models;
using Lain.Host.Operations;
using Lain.Host.Providers;

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
    Check(catalog.RootElement.EnumerateObject().Select(property => property.Name).SequenceEqual(new[] { "models" }), "Native catalog contains only the qualified models field");
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

    var metadata = new ProviderCatalogRegistry([new("fixture-service", "Readable Service", "Synthetic catalog", "https://fixture.invalid/", "https://fixture.invalid/docs",
        [], [], [new("plan", "Unknown plan", "unknown", "model", "tokens")],
        [new("template", "raw-model-id", "Readable Model", "https://fixture.invalid/v1", "openai", ["plan"], new(),
            Manufacturer: "Model Maker", InferenceProvider: "Different Inference Operator")], [], [])]);
    state = repository.Update(state.Revision, c => c with {
        Accounts = c.Accounts.Concat(new AccountDto[] {
            new("codex-account", "Private Codex Account", "codex", true, "oauth", "unverified"),
            new("metadata-account", "Private Metadata Account", "fixture-service", true, "api-key", "unverified") }).ToArray(),
        Routes = [new("review-route", "private-review-alias", "codex-auto-review", "codex", ["codex-account"]),
            new("opaque-route", "preserved-alias", "raw-model-id", "fixture-service", ["metadata-account"], ProviderRouteId: "template"),
            new("alias-only-review", "codex-auto-review", "ordinary-model", "codex", ["codex-account"]),
            new("foreign-review", "foreign-review-alias", "codex-auto-review", "commandcode", ["synthetic-account"])],
        Profiles = [new("fixture", "Fixture", ["review-route", "opaque-route", "alias-only-review", "foreign-review"])]
    });
    var metadataAdapter = new CodexProfileAdapter(new ModelsService(repository, new ProviderCostPolicy(metadata)), registry: metadata);
    var presented = metadataAdapter.Generate("fixture", state.Revision, "/opt/lain/lain", "/tmp/lain.models.json");
    using var presentedCatalog = JsonDocument.Parse(presented.Catalog);
    var presentedModels = presentedCatalog.RootElement.GetProperty("models").EnumerateArray().ToDictionary(model => model.GetProperty("slug").GetString()!);
    var readable = presentedModels["preserved-alias"];
    Check(readable.GetProperty("display_name").GetString() == "Readable Model · Readable Service", "Display names use documented model and service while preserving the inference slug");
    var description = readable.GetProperty("description").GetString()!;
    Check(description.Contains("Model Maker") && description.Contains("Readable Service") && !description.Contains("opaque-route") &&
        !description.Contains("Different Inference Operator"), "Description identifies the documented manufacturer and service without mixing inference operator or raw route ID");
    Check(!presented.Catalog.Contains("Private Metadata Account") && !presented.Catalog.Contains("metadata-account"), "Readable metadata does not expose account names or identities");
    Check(presented.Compatibility!.Routes.Single(route => route.RouteId == "opaque-route").Capabilities.Functions.State == "unknown", "Readable template names do not qualify transport capabilities");
    Check(presentedModels["private-review-alias"].GetProperty("visibility").GetString() == "hide" &&
        presentedModels["private-review-alias"].GetProperty("description").GetString()!.Contains("approval review"), "Internal review identity stays hidden even under an arbitrary alias");
    Check(presentedModels["codex-auto-review"].GetProperty("visibility").GetString() == "list" &&
        presentedModels["foreign-review-alias"].GetProperty("visibility").GetString() == "list", "Neither display aliases nor foreign services inherit internal review metadata");
    Check(presented.Configuration.Contains("model = \"preserved-alias\""), "Implicit default skips the hidden internal review model");
    Check(repository.Snapshot.Routes.Select(route => route.Id).SequenceEqual(state.Routes.Select(route => route.Id)), "Presentation preserves all original route identities");
    state = repository.Update(state.Revision, c => c with { Profiles = [new("fixture", "Fixture", c.Routes.Select(route => route.Id).ToArray(), "review-route")] });
    Reject(() => metadataAdapter.Generate("fixture", state.Revision, "/opt/lain/lain", "/tmp/lain.models.json"), "Explicit hidden review default gives a clear validation error");
    state = repository.Update(state.Revision, c => c with { Profiles = [new("fixture", "Fixture", ["review-route"])] });
    Reject(() => metadataAdapter.Generate("fixture", state.Revision, "/opt/lain/lain", "/tmp/lain.models.json"), "A review-only profile cannot export an inconsistent hidden default");
}
finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
Console.WriteLine($"{assertions} Codex Gateway profile export assertions passed; no installed config or provider access.");

void Reject(Action action, string name)
{
    assertions++;
    try { action(); }
    catch (ArgumentException error) when (error.Message.Contains("internal Codex review model")) { return; }
    throw new Exception(name);
}
