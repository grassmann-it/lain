using System.Text.Json;
using Lain.Core;
using Lain.Host.Models;
using Lain.Host.Operations;
using Lain.Host.Providers;

internal static class InputModalityCases
{
    public static int Run()
    {
        var checks = 0;
        void Check(bool value, string description) { checks++; if (!value) throw new Exception(description); }
        var root = Path.Combine(Path.GetTempPath(), "lain-image-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            var repository = new ConfigurationRepository(new OwnStatePaths(root));
            var state = repository.Update(0, c => c with {
                Accounts = [new("native", "Fixture", "codex", true, "oauth", "unverified"), new("other", "Other", "commandcode", true, "api-key", "unverified")],
                Routes = [new("native", "renamed-sol", "gpt-6.1-sol", "codex", ["native"]),
                    new("snapshot", "snapshot-luna", "gpt-6-luna", "codex", ["native"]),
                    new("unknown", "looks-like-astra", "future-unknown-model", "codex", ["native"]),
                    new("other", "other-sol", "gpt-6.1-sol", "commandcode", ["other"])],
                Profiles = [new("test", "Test", ["native", "snapshot", "unknown", "other"], "native")],
                Collections = [new("images", "Native images", ["native", "snapshot"]), new("mixed", "Unknown capability", ["native", "unknown"])],
                CollectionRouters = [new("images", "auto-images", "collection:images", "test"), new("mixed", "auto-mixed", "collection:mixed", "test")]
            });
            var transports = state.Routes.ToDictionary(r => r.Id, r => new ModelCompatibilityTransportBinding(r.Provider, r.UpstreamModel,
                r.Provider == "codex" ? "responses" : "openai", r.Id == "snapshot" ? "compiled:codex-access-snapshot" : r.Provider == "codex" ? "compiled:codex-owned" : "compiled:import"));
            var compatibility = new ModelCompatibilityCatalog(transportBindings: transports);
            Check(compatibility.ResolveInputModalities(state.Routes[0]).SequenceEqual(new[] { "text", "image" }), "Native Sol image metadata comes from actual upstream model/transport, even when alias differs");
            Check(compatibility.ResolveInputModalities(state.Routes[1]).Contains("image"), "Native externally imported access snapshot retains image inputs");
            Check(compatibility.ResolveInputModalities(state.Routes[2]).SequenceEqual(new[] { "text" }), "Unknown future native model doesn't borrow vision from a name/alias");
            Check(!compatibility.ResolveInputModalities(state.Routes[3]).Contains("image"), "Third-party identically named model doesn't inherit native vision");
            Check(!compatibility.ResolveInputModalities(state.Routes[0] with { UpstreamModel = "changed-model" }).Contains("image"), "Stale compiled transport cannot certify changed route");
            Check(!compatibility.ResolveInputModalities(state.Routes[0], "unqualified-client").Contains("image"), "Unknown client version does not borrow native capability fixture");
            Check(!new ModelCompatibilityCatalog().ResolveInputModalities(state.Routes[0]).Contains("image"), "Missing compiled transport remains conservative");
            var registry = new ProviderCatalogRegistry();
            var description = new ProxyTransportDescription(state.Accounts.ToDictionary(a => a.Id, a => "fixture/" + a.Id),
                new Dictionary<string, IReadOnlyDictionary<string, string>>(), new HashSet<string>(), transports);
            var proof = CollectionRouterCompiler.CompileForExport(state, "test", description, registry);
            var templateProvider = registry.Snapshot().Providers.First(p => p.Routes.Any(r => r.Protocol == "openai-chat"));
            var template = templateProvider.Routes.First(r => r.Protocol == "openai-chat");
            var documentedTemplate = template with { Capabilities = template.Capabilities with { Images = true },
                Sources = [new("https://fixture.invalid/images", "Synthetic route image proof", DateTimeOffset.UnixEpoch)] };
            var fixtureRegistry = new ProviderCatalogRegistry([templateProvider with { Routes = templateProvider.Routes.Select(r => r.Id == template.Id ? documentedTemplate : r).ToArray() }]);
            var documentedRoute = new RouteDto("documented", "documented-alias", template.Model, templateProvider.Id, ["fixture"], ProviderRouteId: template.Id);
            var documentedTransport = new ModelCompatibilityTransportBinding(templateProvider.Id, template.Model, "openai", "synthetic:compiled");
            ModelCompatibilityCatalog Documented(bool qualified, ModelCompatibilityTransportBinding binding) => new(fixtureRegistry,
                qualified ? new HashSet<string> { "documented" } : new HashSet<string>(), new Dictionary<string, ModelCompatibilityTransportBinding> { ["documented"] = binding });
            Check(Documented(true, documentedTransport).ResolveInputModalities(documentedRoute).Contains("image"), "Qualified documented third-party image route advertises image input");
            Check(!Documented(false, documentedTransport).ResolveInputModalities(documentedRoute).Contains("image"), "Unqualified route cannot borrow documented image support");
            Check(!Documented(true, documentedTransport with { UpstreamProtocol = "claude" }).ResolveInputModalities(documentedRoute).Contains("image"), "Incompatible compiled protocol cannot borrow image support");
            Check(!Documented(true, documentedTransport).ResolveInputModalities(documentedRoute with { UpstreamModel = "another-model" }).Contains("image"), "Changed third-party model loses template qualification");
            var before = JsonSerializer.Serialize(repository.Snapshot);
            var export = new CodexProfileAdapter(new ModelsService(repository), compatibility, registry, proof)
                .Generate("test", state.Revision, "/opt/lain/lain", "/tmp/models.json", enableLainModelPicker: true);
            using var doc = JsonDocument.Parse(export.Catalog);
            var models = doc.RootElement.GetProperty("models").EnumerateArray().ToDictionary(m => m.GetProperty("slug").GetString()!);
            bool Image(string alias) => models[alias].GetProperty("input_modalities").EnumerateArray().Any(v => v.GetString() == "image");
            Check(Image("renamed-sol") && Image("snapshot-luna"), "Exported native concrete entries permit image input");
            Check(Image("auto-images"), "Router permits images when every concrete target qualifies");
            Check(!Image("auto-mixed") && !Image("other-sol") && !Image("looks-like-astra"), "Router uses member intersection; unknown/third-party entries stay text-only");
            Check(models["renamed-sol"].GetProperty("context_window").GetInt64() == 272000 &&
                models["renamed-sol"].GetProperty("max_context_window").GetInt64() == 872000,
                "Native context defaults preserve pinned client bounds and allow the derived auto-compaction threshold");
            Check(models["auto-images"].GetProperty("context_window").GetInt64() == 272000 &&
                models["auto-mixed"].GetProperty("context_window").ValueKind == JsonValueKind.Null,
                "Router context is the known member intersection; an unknown target cannot borrow a native window");
            Check(models["other-sol"].GetProperty("context_window").ValueKind == JsonValueKind.Null &&
                compatibility.ResolveContextWindow(state.Routes[0] with { UpstreamModel = "changed" }).ContextWindow is null,
                "Third-party aliases and stale transport never borrow a native context window");
            Check(compatibility.ResolveContextWindow(state.Routes[0], "unknown-client").ContextWindow is null,
                "Unqualified client version cannot borrow pinned context defaults");
            Check(JsonSerializer.Serialize(repository.Snapshot) == before, "Modality export preserves configuration, routes, profile and revision");
            return checks;
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
