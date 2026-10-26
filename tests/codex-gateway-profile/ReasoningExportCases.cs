using System.Text.Json;
using Lain.Core;
using Lain.Host.Models;
using Lain.Host.Operations;
using Lain.Host.Providers;

internal static class ReasoningExportCases
{
    public static int Run()
    {
        var checks = 0;
        void Check(bool value, string description) { checks++; if (!value) throw new Exception(description); }
        void Reject(Action action, string fragment)
        {
            checks++;
            try { action(); } catch (ArgumentException error) when (error.Message.Contains(fragment)) { return; }
            throw new Exception("Expected reasoning export rejection: " + fragment);
        }
        var root = Path.Combine(Path.GetTempPath(), "lain-reasoning-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            var repository = new ConfigurationRepository(new OwnStatePaths(root));
            var state = repository.Update(0, configuration => configuration with {
                Accounts = [new("native", "Native", "codex", true, "oauth", "unverified"), new("gateway", "Gateway", "commandcode", true, "api-key", "unverified")],
                Routes = [new("astra", "renamed-astra", "gpt-6-astra", "codex", ["native"]),
                    new("sol", "renamed-sol", "gpt-6-sol", "codex", ["native"]), new("luna", "renamed-luna", "gpt-6-luna", "codex", ["native"]),
                    new("thirdparty", "cmd-astra", "gpt-6-astra", "commandcode", ["gateway"])],
                Profiles = [new("profile", "Profile", ["astra", "sol", "luna", "thirdparty"], "luna")]
            });
            var transports = state.Routes.ToDictionary(route => route.Id, route => new ModelCompatibilityTransportBinding(route.Provider, route.UpstreamModel,
                route.Provider == "codex" ? "responses" : "openai", route.Provider == "codex" ? "compiled:codex-owned" : "compiled:import"));
            var compatibility = new ModelCompatibilityCatalog(transportBindings: transports);
            var adapter = new CodexProfileAdapter(new ModelsService(repository), compatibility);
            CodexProfileArtifacts Export(string? effort = null) => adapter.Generate("profile", repository.Snapshot.Revision, "/opt/lain/lain", "/tmp/lain.models.json", reasoningEffort: effort);
            var original = JsonSerializer.Serialize(repository.Snapshot);
            var artifacts = Export();
            Check(artifacts.Configuration.Contains("model = \"renamed-luna\"") && artifacts.Configuration.Contains("model_reasoning_effort = \"medium\""), "Selected route supplies both exact alias and its supported default, overriding stale base none/ultra in a generated profile");
            using var catalog = JsonDocument.Parse(artifacts.Catalog);
            var models = catalog.RootElement.GetProperty("models").EnumerateArray().ToDictionary(model => model.GetProperty("slug").GetString()!);
            foreach (var name in new[] { "renamed-astra", "renamed-sol", "renamed-luna" })
            {
                var model = models[name];
                Check(model.GetProperty("supported_reasoning_levels").EnumerateArray().Any(level => level.GetProperty("effort").GetString() == model.GetProperty("default_reasoning_level").GetString()), "Every qualified worker model default is in its supported effort list");
            }
            Check(models["cmd-astra"].GetProperty("default_reasoning_level").ValueKind == JsonValueKind.Null &&
                models["cmd-astra"].GetProperty("supported_reasoning_levels").GetArrayLength() == 0, "Third-party Astra is unknown and never inherits native capabilities or a fabricated none");
            Reject(() => Export("ultra"), "not advertised");
            Check(Export("high").Configuration.Contains("model_reasoning_effort = \"high\""), "Supported user effort choice is preserved exactly");
            Check(JsonSerializer.Serialize(repository.Snapshot) == original, "Effort export and validation never change stored profiles or installed client configuration");
            state = repository.Update(state.Revision, configuration => configuration with {
                Collections = [new("pair", "Pair", ["astra", "luna"])],
                CollectionRouters = [new("router", "auto-pair", "collection:pair", "profile")]
            });
            var registry = new ProviderCatalogRegistry();
            var description = new ProxyTransportDescription(state.Accounts.ToDictionary(account => account.Id, account => "fixture/" + account.Id),
                new Dictionary<string, IReadOnlyDictionary<string, string>>(), new HashSet<string>(), transports);
            var proof = CollectionRouterCompiler.CompileForExport(state, "profile", description, registry);
            var grouped = new CodexProfileAdapter(new ModelsService(repository), compatibility, registry, proof)
                .Generate("profile", state.Revision, "/opt/lain/lain", "/tmp/lain.models.json", enableLainModelPicker: true);
            using var groupedCatalog = JsonDocument.Parse(grouped.Catalog);
            var router = groupedCatalog.RootElement.GetProperty("models").EnumerateArray().Single(model => model.GetProperty("slug").GetString() == "auto-pair");
            Check(router.GetProperty("default_reasoning_level").GetString() == "medium" &&
                !router.GetProperty("supported_reasoning_levels").EnumerateArray().Any(level => level.GetProperty("effort").GetString() == "ultra"), "Router advertises only effort choices shared by its actual compiled Astra/Luna members");
            state = repository.Update(state.Revision, configuration => configuration with { Profiles = [configuration.Profiles[0] with { DefaultRouteId = "astra" }] });
            Check(Export("ultra").Configuration.Contains("model_reasoning_effort = \"ultra\""), "Native Astra retains legitimate client ultra selection");
            Reject(() => Export("none"), "not advertised");
            checks++;
            try { adapter.Generate("profile", state.Revision - 1, "/opt/lain/lain", "/tmp/lain.models.json"); throw new Exception("Stale revision exported"); }
            catch (RevisionConflictException) { }
            state = repository.Update(state.Revision, configuration => configuration with { Routes = configuration.Routes.Select(route => route.Id == "astra" ? route with { UpstreamModel = "replacement-model" } : route).ToArray() });
            var changed = Export();
            using var changedCatalog = JsonDocument.Parse(changed.Catalog);
            Check(changedCatalog.RootElement.GetProperty("models")[0].GetProperty("default_reasoning_level").ValueKind == JsonValueKind.Null &&
                changed.Configuration.Contains("Explicitly remove inherited effort overrides"), "Stale model transport cannot supply reasoning defaults after route identity changes; unknown inheritance needs explicit handling");
            Reject(() => Export("ultra"), "unqualified");
            Check(changed.GenerationId != artifacts.GenerationId, "Model/revision changes produce a new coherent export generation");
            var updatedTransports = transports.ToDictionary(pair => pair.Key, pair => pair.Key == "astra" ? pair.Value with { UpstreamModel = "replacement-model" } : pair.Value);
            var updatedDescription = description with { TransportBindings = updatedTransports };
            var updatedProof = CollectionRouterCompiler.CompileForExport(state, "profile", updatedDescription, registry);
            var unknownRouter = new CodexProfileAdapter(new ModelsService(repository), new ModelCompatibilityCatalog(transportBindings: updatedTransports), registry, updatedProof)
                .Generate("profile", state.Revision, "/opt/lain/lain", "/tmp/lain.models.json", enableLainModelPicker: true);
            using var unknownRouterCatalog = JsonDocument.Parse(unknownRouter.Catalog);
            var unknownEntry = unknownRouterCatalog.RootElement.GetProperty("models").EnumerateArray().Single(model => model.GetProperty("slug").GetString() == "auto-pair");
            Check(unknownEntry.GetProperty("default_reasoning_level").ValueKind == JsonValueKind.Null && unknownEntry.GetProperty("supported_reasoning_levels").GetArrayLength() == 0,
                "One unqualified current router member prevents fabricated shared effort support");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        return checks;
    }
}
