using System.Text.Json;
using Lain.Core;
using Lain.Host.Models;
using Lain.Host.Operations;

internal static class RouterCases
{
    public static void Run()
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Invalid router accepted"); }
        var path = Path.Combine(Path.GetTempPath(), "lain-router-" + Guid.NewGuid().ToString("N"));
        try
        {
            var repository = new ConfigurationRepository(new OwnStatePaths(path));
            var configuration = repository.Update(0, state => state with {
                Accounts = [new("account", "Fixture", "fixture", true, "api-key", "ready")],
                Routes = [new("a", "alias-a", "model-a", "fixture", ["account"]), new("b", "alias-b", "model-b", "fixture", ["account"]), new("hidden", "alias-hidden", "model", "fixture", ["account"], Visible:false)],
                Profiles = [new("profile", "Fixture", ["a", "b", "hidden"], "a")],
                Collections = [new("security", "IT Sec", ["b", "hidden", "a"])]
            });
            var service = new ModelsService(repository);
            var router = new CollectionRouterDto("router", "auto-security", "collection:security", "profile");
            configuration = service.PutCollectionRouter(configuration.Revision, router);
            try { service.PutCollectionRouter(configuration.Revision - 1, router); throw new Exception("Stale router update accepted"); } catch (RevisionConflictException) { }
            Reject(() => service.PutCollectionRouter(configuration.Revision, router with { Alias = "alias-a" }));
            Reject(() => service.PutCollectionRouter(configuration.Revision, router with { GroupId = "missing" }));
            Reject(() => service.PutCollectionRouter(configuration.Revision, router with { GroupId = "policy:free" }));
            var bindings = new Dictionary<string, CoreRouteBinding> {
                ["alias-a"] = new("a", "model-a", ["account"], "strict", Provider:"fixture", UpstreamProtocol:"responses"),
                ["alias-b"] = new("b", "model-b", ["account"], "strict", Provider:"fixture", UpstreamProtocol:"openai"),
                ["alias-hidden"] = new("hidden", "model", ["account"], "strict", Visible:false, Provider:"fixture", UpstreamProtocol:"openai")
            };
            var qualified = new HashSet<string> { "a", "b", "hidden" };
            var compiled = CollectionRouterCompiler.Compile(configuration, "profile", bindings, qualified)!;
            Check(compiled[router.Alias].RouteIds.SequenceEqual(new[] { "b", "a" }), "Ordered membership/visibility changed");
            Check(CollectionRouterCompiler.Compile(configuration, "other", bindings, qualified) is null, "Routers leaked across profiles");
            Reject(() => CollectionRouterCompiler.Compile(configuration, "profile", bindings, new HashSet<string>()));
            var originalFingerprint = compiled[router.Alias].MemberFingerprints["a"];
            bindings["alias-a"] = bindings["alias-a"] with { Credentials = new Dictionary<string, string> { ["account"] = "rotated-credential-identity" } };
            Check(CollectionRouterCompiler.Compile(configuration, "profile", bindings, qualified)![router.Alias].MemberFingerprints["a"] == originalFingerprint, "Credential rotation must not change logical conversation identity");
            bindings["alias-a"] = bindings["alias-a"] with { Accounts = ["different-account"] };
            Check(CollectionRouterCompiler.Compile(configuration, "profile", bindings, qualified)![router.Alias].MemberFingerprints["a"] != originalFingerprint, "Account replacement must invalidate pins");
            bindings["alias-a"] = bindings["alias-a"] with { Accounts = ["account"] };
            bindings["alias-a"] = bindings["alias-a"] with { Model = "replacement-model" };
            Check(CollectionRouterCompiler.Compile(configuration, "profile", bindings, qualified)![router.Alias].MemberFingerprints["a"] != originalFingerprint, "Changed upstream must invalidate the conversation pin");
            bindings["alias-a"] = bindings["alias-a"] with { Model = "model-a" };
            var before = JsonSerializer.Serialize(repository.Snapshot);
            var export = new CodexProfileAdapter(service, compiledRouters:compiled).Generate("profile", configuration.Revision, "/opt/lain/lain", "/tmp/models.json", enableLainModelPicker:true);
            using var doc = JsonDocument.Parse(export.Catalog);
            Check(doc.RootElement.GetProperty("models").EnumerateArray().Count(m => m.GetProperty("slug").GetString() == router.Alias) == 1, "Router must be one real virtual model");
            Check(doc.RootElement.GetProperty("lain_model_picker").GetProperty("groups")[0].GetProperty("router_model").GetString() == router.Alias, "Collection must reference compiled router slug");
            Check(export.Configuration.Contains("model = \"alias-a\""), "Router export changed pinned default");
            Check(before == JsonSerializer.Serialize(repository.Snapshot), "Export mutated state");
            Reject(() => new CodexProfileAdapter(service).Generate("profile", configuration.Revision, "/opt/lain/lain", "/tmp/models.json", enableLainModelPicker:true));
            var strictConfig = configuration with { CollectionRouters = [router with { CostPolicy = "free_only" }] };
            Reject(() => CollectionRouterCompiler.Compile(strictConfig, "profile", bindings, qualified));
            configuration = service.DeleteCollectionRouter(configuration.Revision, router.Id);
            Check(configuration.CollectionRouters!.Length == 0 && configuration.Routes.Length == 3, "Router deletion changed real routes");
        }
        finally { Directory.Delete(path, true); }
    }
}
