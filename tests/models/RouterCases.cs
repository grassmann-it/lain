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
            var transports = bindings.Values.ToDictionary(route => route.Id, route => new ModelCompatibilityTransportBinding(route.Provider!, route.Model, route.UpstreamProtocol!, "synthetic:compiled"));
            var compiled = CollectionRouterCompiler.Compile(configuration, "profile", bindings, qualified, transports)!;
            Check(compiled[router.Alias].RouteIds.SequenceEqual(new[] { "b", "a" }), "Ordered membership/visibility changed");
            Check(CollectionRouterCompiler.Compile(configuration, "other", bindings, qualified, transports) is null, "Routers leaked across profiles");
            Check(CollectionRouterCompiler.Compile(configuration, "profile", bindings, new HashSet<string>(), transports)![router.Alias].RouteIds.Length == 2, "Actual configured transports do not require catalog IDs");
            Reject(() => CollectionRouterCompiler.Compile(configuration, "profile", bindings, qualified));
            var originalFingerprint = compiled[router.Alias].MemberFingerprints["a"];
            bindings["alias-a"] = bindings["alias-a"] with { Credentials = new Dictionary<string, string> { ["account"] = "rotated-credential-identity" } };
            Check(CollectionRouterCompiler.Compile(configuration, "profile", bindings, qualified, transports)![router.Alias].MemberFingerprints["a"] == originalFingerprint, "Credential rotation must not change logical conversation identity");
            bindings["alias-a"] = bindings["alias-a"] with { Accounts = ["different-account"] };
            Check(CollectionRouterCompiler.Compile(configuration, "profile", bindings, qualified, transports)![router.Alias].MemberFingerprints["a"] != originalFingerprint, "Account replacement must invalidate pins");
            bindings["alias-a"] = bindings["alias-a"] with { Accounts = ["account"] };
            bindings["alias-a"] = bindings["alias-a"] with { Model = "replacement-model" };
            transports["a"] = transports["a"] with { UpstreamModel = "replacement-model" };
            Check(CollectionRouterCompiler.Compile(configuration, "profile", bindings, qualified, transports)![router.Alias].MemberFingerprints["a"] != originalFingerprint, "Changed upstream must invalidate the conversation pin");
            bindings["alias-a"] = bindings["alias-a"] with { Model = "model-a" };
            transports["a"] = transports["a"] with { UpstreamModel = "model-a" };
            var before = JsonSerializer.Serialize(repository.Snapshot);
            var description = new ProxyTransportDescription(new Dictionary<string, string> { ["account"] = "credential" },
                new Dictionary<string, IReadOnlyDictionary<string, string>>(), qualified, transports);
            var proof = CollectionRouterCompiler.CompileForExport(configuration, "profile", description);
            var export = new CodexProfileAdapter(service, routerProof:proof).Generate("profile", configuration.Revision, "/opt/lain/lain", "/tmp/models.json", enableLainModelPicker:true);
            using var doc = JsonDocument.Parse(export.Catalog);
            Check(doc.RootElement.GetProperty("models").EnumerateArray().Count(m => m.GetProperty("slug").GetString() == router.Alias) == 1, "Router must be one real virtual model");
            Check(doc.RootElement.GetProperty("lain_model_picker").GetProperty("groups").EnumerateArray().Single(group => group.GetProperty("id").GetString() == "collection:security").GetProperty("router_model").GetString() == router.Alias, "Collection must reference compiled router slug");
            Check(export.Configuration.Contains("model = \"alias-a\""), "Router export changed pinned default");
            Check(before == JsonSerializer.Serialize(repository.Snapshot), "Export mutated state");
            Reject(() => new CodexProfileAdapter(service).Generate("profile", configuration.Revision, "/opt/lain/lain", "/tmp/models.json", enableLainModelPicker:true));
            configuration = service.PutCollection(configuration.Revision, configuration.Collections![0] with { RouteIds = ["a", "b"] });
            Reject(() => new CodexProfileAdapter(service, routerProof:proof).Generate("profile", configuration.Revision, "/opt/lain/lain", "/tmp/models.json", enableLainModelPicker:true));
            var strictConfig = configuration with { CollectionRouters = [router with { CostPolicy = "free_only" }] };
            Reject(() => CollectionRouterCompiler.Compile(strictConfig, "profile", bindings, qualified, transports));
            configuration = service.DeleteCollectionRouter(configuration.Revision, router.Id);
            Check(configuration.CollectionRouters!.Length == 0 && configuration.Routes.Length == 3, "Router deletion changed real routes");
            CheckAccountRouters();
            CheckNativeCodexTransport(Path.Combine(path, "codex"));
        }
        finally { Directory.Delete(path, true); }
    }

    private static void CheckAccountRouters()
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Invalid account router accepted"); }
        var first = new AccountDto("private", "Private", "fixture", true, "api-key", "ready");
        var second = first with { Id = "work", Name = "Work" };
        var routes = new RouteDto[] {
            new("a", "alias-a", "same-model", "fixture", [first.Id]),
            new("b", "alias-b", "same-model", "fixture", [second.Id]),
            new("pool", "alias-pool", "same-model", "fixture", [first.Id, second.Id], Pooled:true)
        };
        var profile = new ProfileDto("profile", "Fixture", ["b", "pool", "a"]);
        var router = new CollectionRouterDto("router", "auto-private", "account:private", profile.Id);
        var configuration = new ConfigurationDto(1, 0, [first, second], routes, [profile], CollectionRouters:[router]);
        CollectionRouterCompiler.Validate(configuration);
        Check(CollectionRouterCompiler.MemberIds(configuration, profile, router).SequenceEqual(new[] { "a" }), "Account groups must not include another account or pooled route");
        Reject(() => CollectionRouterCompiler.MemberIds(configuration, profile, router with { GroupId = "account:missing" }));
        var bindings = routes.ToDictionary(route => route.Alias, route => new CoreRouteBinding(route.Id, route.UpstreamModel, route.AccountIds,
            route.Pooled ? "pool" : "strict", Provider:route.Provider, UpstreamProtocol:"responses"));
        var transports = routes.ToDictionary(route => route.Id, route => new ModelCompatibilityTransportBinding(route.Provider, route.UpstreamModel, "responses", "synthetic:account-router"));
        Check(CollectionRouterCompiler.Compile(configuration, profile.Id, bindings, null, transports)![router.Alias].RouteIds.SequenceEqual(new[] { "a" }), "Account router preserves existing strict route identity and transport proof");
        Reject(() => CollectionRouterCompiler.Compile(configuration, profile.Id, bindings, null));
        Reject(() => CollectionRouterCompiler.Compile(configuration with { Accounts = [first with { Enabled = false }, second] }, profile.Id, bindings, null, transports));
        var providerRouter = router with { GroupId = "provider:fixture" };
        var ambiguous = configuration with { CollectionRouters = [providerRouter] };
        CollectionRouterCompiler.Validate(ambiguous); // Structural writes/account creation remain allowed.
        Reject(() => CollectionRouterCompiler.Compile(ambiguous, profile.Id, bindings, null, transports));
        var singleProfile = profile with { RouteIds = ["a"] };
        Check(CollectionRouterCompiler.Compile(ambiguous with { Profiles = [singleProfile] }, profile.Id, bindings, null, transports)![router.Alias].RouteIds.SequenceEqual(new[] { "a" }), "An account outside the export profile cannot make its provider router ambiguous");
        var hiddenSecond = ambiguous with { Routes = routes.Select(route => route.Id is "b" or "pool" ? route with { Visible = false } : route).ToArray() };
        Check(CollectionRouterCompiler.Compile(hiddenSecond, profile.Id, bindings.Where(pair => pair.Value.Id == "a").ToDictionary(pair => pair.Key, pair => pair.Value), null, transports)![router.Alias].RouteIds.SequenceEqual(new[] { "a" }), "Hidden accounts do not create provider ambiguity");
    }

    private static void CheckNativeCodexTransport(string path)
    {
        var paths = new OwnStatePaths(path);
        var secrets = new SecretStore(paths);
        var account = new AccountDto("codex-account", "Synthetic Codex", "codex", true, "oauth", "unverified");
        var route = new RouteDto("codex-route", "codex-alias", "codex-fixture-model", "codex", [account.Id]);
        var router = new CollectionRouterDto("codex-router", "auto-codex", "provider:codex", "codex-profile");
        var configuration = new ConfigurationDto(1, 0, [account], [route], [new("codex-profile", "Fixture", [route.Id])], CollectionRouters:[router]);
        OwnStatePaths.WritePrivate(Path.Combine(paths.OwnedAuth, "fixture.json"), JsonSerializer.Serialize(new {
            type = "codex", account_id = "synthetic-subject", access_token = "synthetic-access-token" }));
        foreach (var kind in new[] { "codex-owned", "codex-access-snapshot" })
        {
            configuration = configuration with { Accounts = [account with { CredentialKind = kind }] };
            secrets.WriteAccount(account.Id, JsonSerializer.Serialize(new { kind, accountId = "synthetic-subject", accessToken = "synthetic-access-token", ownedAuthFile = "fixture.json" }));
            var transport = new ProxyConfigurationWriter(paths, secrets).Describe(configuration);
            if (transport.QualifiedProviderRouteIds.Count != 0 || transport.TransportBindings[route.Id].UpstreamProtocol != "responses")
                throw new Exception("Native Codex must use real compiled Responses transport without a catalog ID");
            _ = CollectionRouterCompiler.CompileForExport(configuration, "codex-profile", transport);
            try {
                _ = CollectionRouterCompiler.CompileForExport(configuration, "codex-profile", transport with { TransportBindings = new Dictionary<string, ModelCompatibilityTransportBinding>() });
                throw new Exception("Codex router accepted without compiled transport proof");
            } catch (ArgumentException) { }
        }
    }
}
