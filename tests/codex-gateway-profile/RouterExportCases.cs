using System.Text.Json;
using Lain.Core;
using Lain.Host.Models;
using Lain.Host.Operations;
using Lain.Host.Providers;

internal static class RouterExportCases
{
    public static int Run()
    {
        var assertions = 0;
        void Check(bool condition, string name) { assertions++; if (!condition) throw new Exception(name); }
        void Reject(Action action, string name)
        {
            assertions++;
            try { action(); } catch (ArgumentException) { return; }
            throw new Exception(name);
        }
        var directory = Path.Combine(Path.GetTempPath(), "lain-router-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            var repository = new ConfigurationRepository(new OwnStatePaths(directory));
            var state = repository.Update(0, configuration => configuration with
            {
                Accounts = [new("codex-one", "Fixture", "codex", true, "oauth", "ready"), new("codex-two", "Fixture", "codex", true, "oauth", "ready"),
                    new("openrouter-one", "Fixture", "openrouter", true, "api-key", "ready")],
                Routes = [new("a", "alias-a", "model-a", "codex", ["codex-one"]), new("b", "alias-b", "model-b", "codex", ["codex-one"]),
                    new("free", "alias-free", "qwen/qwen3-coder:free", "openrouter", ["openrouter-one"], ProviderRouteId: "openrouter-qwen3-coder-free", Policy: new("free_only")),
                    new("paid", "alias-configured", "qwen/qwen3-coder:free", "openrouter", ["openrouter-one"], ProviderRouteId: "openrouter-qwen3-coder-free"),
                    new("manual", "alias-manual", "manual-model", "codex", ["codex-one"]),
                    new("hidden", "alias-hidden", "hidden-model", "codex", ["codex-one"], Visible: false),
                    new("disabled", "alias-disabled", "disabled-model", "codex", ["codex-one"], Enabled: false),
                    new("pool", "alias-pool", "pool-model", "codex", ["codex-one", "codex-two"], Pooled: true),
                    new("review", "alias-review", "codex-auto-review", "codex", ["codex-one"]),
                    new("offprofile", "alias-offprofile", "outside-model", "codex", ["codex-one"])],
                Profiles = [new("host-work", "Work", ["a", "b", "free", "paid", "manual", "hidden", "disabled", "pool", "review"], "a"),
                    new("host-other", "Other", ["offprofile"], "offprofile")],
                Collections = [new("security", "IT Sec", ["b", "offprofile", "hidden", "pool", "review", "disabled", "a"]),
                    new("other", "Other", ["offprofile"]), new("inactive", "Inactive", ["a"])],
                CollectionRouters = [new("security-router", "auto-security", "collection:security", "host-work"),
                    new("free-router", "auto-free", "policy:free", "host-work", "free_only"),
                    new("other-router", "auto-other", "collection:other", "host-other"),
                    new("inactive-router", "auto-inactive", "collection:inactive", "host-work", Enabled: false)]
            });
            var service = new ModelsService(repository);
            var catalog = new ProviderCatalogRegistry();
            ProxyTransportDescription Transport(ConfigurationDto configuration, string profileId = "host-work")
            {
                var profile = configuration.Profiles.Single(profile => profile.Id == profileId);
                var active = configuration.Routes.Where(route => route.Enabled && profile.RouteIds.Contains(route.Id)).ToArray();
                return new(configuration.Accounts.ToDictionary(account => account.Id, account => "synthetic-core-" + account.Id),
                    new Dictionary<string, IReadOnlyDictionary<string, string>>(),
                    active.Where(route => route.ProviderRouteId is not null).Select(route => route.Id).ToHashSet(StringComparer.Ordinal),
                    active.ToDictionary(route => route.Id, route => new ModelCompatibilityTransportBinding(route.Provider, route.UpstreamModel,
                        route.Id == "manual" ? "gemini" : route.Provider == "openrouter" ? "openai" : "responses", "synthetic:compiled-export-transport"), StringComparer.Ordinal));
            }
            CollectionRouterExportProof Proof(ConfigurationDto configuration, string profileId = "host-work") =>
                CollectionRouterCompiler.CompileForExport(configuration, profileId, Transport(configuration, profileId), catalog);
            CodexProfileArtifacts Export(bool enabled, CollectionRouterExportProof? proof = null, string profileId = "host-work") =>
                new CodexProfileAdapter(service, registry: catalog, routerProof: proof).Generate(profileId, repository.Snapshot.Revision,
                    "/opt/lain/lain", "/tmp/lain.models.json", enableLainModelPicker: enabled);
            var original = JsonSerializer.Serialize(repository.Snapshot);
            var proof = Proof(state);
            using var grouped = JsonDocument.Parse(Export(true, proof).Catalog);
            var models = grouped.RootElement.GetProperty("models").EnumerateArray().ToDictionary(model => model.GetProperty("slug").GetString()!);
            var groups = grouped.RootElement.GetProperty("lain_model_picker").GetProperty("groups").EnumerateArray().ToDictionary(group => group.GetProperty("id").GetString()!);
            Check(models.Keys.Count(alias => alias == "auto-security") == 1 && models.Keys.Count(alias => alias == "auto-free") == 1 &&
                groups["collection:security"].GetProperty("router_model").GetString() == "auto-security" &&
                groups["policy:free"].GetProperty("router_model").GetString() == "auto-free", "Each enabled profile router is one executable model referenced by its actual group");
            Check(!models.ContainsKey("auto-other") && !models.ContainsKey("auto-inactive") && !models.ContainsKey("alias-offprofile") &&
                !models.ContainsKey("alias-hidden") && !models.ContainsKey("alias-disabled"), "Foreign-profile and inactive routers or routes cannot leak into the selected catalog");
            Check(groups["policy:free"].GetProperty("models").EnumerateArray().Select(model => model.GetString()).SequenceEqual(new[] { "alias-free" }) &&
                models.ContainsKey("alias-configured") && models.ContainsKey("alias-manual"), "Free lists only an effective free_only qualified route while ordinary configured/manual choices remain individual models");
            var resolved = service.ResolveForApply("host-work", state.Revision);
            var transport = Transport(state);
            var runtime = RouteBindings.Compile(state with { Routes = resolved.Routes }, transport.CredentialIds, transport.RouteCredentials, catalog,
                "host-work", transport.QualifiedProviderRouteIds, transport.TransportBindings);
            Check(runtime.Routers!["auto-security"].RouteIds.SequenceEqual(new[] { "b", "a" }) && runtime.Routers["auto-free"].RouteIds.SequenceEqual(new[] { "free" }),
                "Real ordered router bindings exclude off-profile, hidden, disabled, pooled, review and unrelated manual routes");
            var artifacts = Export(true, proof);
            Check(artifacts.Configuration.Contains("model = \"alias-a\"") && artifacts.ConfigFileName == "lain.config.toml" &&
                artifacts.LaunchArguments.SequenceEqual(new[] { "--no-daemon", "--profile", "lain" }), "Executable routers preserve the explicit individual default and the separate lain Codex profile");
            using var ordinary = JsonDocument.Parse(Export(false).Catalog);
            Check(!ordinary.RootElement.TryGetProperty("lain_model_picker", out _) &&
                !ordinary.RootElement.GetProperty("models").EnumerateArray().Any(model => model.GetProperty("slug").GetString()!.StartsWith("auto-", StringComparison.Ordinal)),
                "The ordinary native export needs no router proof and exposes no virtual router model");
            Check(JsonSerializer.Serialize(repository.Snapshot) == original, "Proof creation and export never apply or mutate the saved configuration");
            Reject(() => Export(true), "An executable router requires a compiler-created export proof");
            Reject(() => Export(true, Proof(state, "host-other")), "A proof from another profile cannot authorize this profile's router aliases");

            foreach (var change in new Func<ConfigurationDto, ConfigurationDto>[]
            {
                configuration => configuration with { Collections = configuration.Collections!.Select(collection => collection.Id == "security" ? collection with { RouteIds = collection.RouteIds.Reverse().ToArray() } : collection).ToArray() },
                configuration => configuration with { Routes = configuration.Routes.Select(route => route.Id == "a" ? route with { UpstreamModel = "replacement-model" } : route).ToArray() },
                configuration => configuration with { Routes = configuration.Routes.Select(route => route.Id == "a" ? route with { AccountIds = ["codex-two"] } : route).ToArray() },
                configuration => configuration with { Routes = configuration.Routes.Select(route => route.Id == "free" ? route with { Policy = new("configured") } : route).ToArray() },
                configuration => configuration with { Profiles = configuration.Profiles.Select(profile => profile.Id == "host-work" ? profile with { RouteIds = profile.RouteIds.Where(id => id != "b").ToArray() } : profile).ToArray() },
                configuration => configuration with { CollectionRouters = configuration.CollectionRouters!.Select(router => router.Id == "security-router" ? router with { Alias = "changed-auto-alias" } : router).ToArray() }
            })
            {
                var current = repository.Snapshot;
                var beforeChange = Proof(current);
                state = repository.Update(current.Revision, change);
                Reject(() => Export(true, beforeChange), "A stale binding proof cannot authorize changed order, model, account, cost policy, profile membership or router alias");
                state = repository.Update(state.Revision, _ => current);
            }
            var mismatched = Transport(state);
            var bindingMap = mismatched.TransportBindings.ToDictionary(pair => pair.Key, pair => pair.Value);
            bindingMap["a"] = bindingMap["a"] with { UpstreamModel = "unmatched-upstream" };
            Reject(() => CollectionRouterCompiler.CompileForExport(state, "host-work", mismatched with { TransportBindings = bindingMap }, catalog), "Mismatched compiled upstream transport cannot create router proof");
            bindingMap["a"] = mismatched.TransportBindings["a"] with { Source = "" };
            Reject(() => CollectionRouterCompiler.CompileForExport(state, "host-work", mismatched with { TransportBindings = bindingMap }, catalog), "Unsourced transport metadata cannot create router proof");
            bindingMap.Remove("a");
            Reject(() => CollectionRouterCompiler.CompileForExport(state, "host-work", mismatched with { TransportBindings = bindingMap }, catalog), "Missing active-route transport cannot create router proof");
            Reject(() => CollectionRouterCompiler.CompileForExport(state, "host-work", mismatched with { QualifiedProviderRouteIds = new HashSet<string>() }, catalog), "A Free route without qualified catalog transport and enforcement cannot create executable proof");
            var relabeled = state with { Routes = state.Routes.Select(route => route.Id == "free" ? route with { Policy = new("configured") } : route).ToArray() };
            Reject(() => Proof(relabeled), "A Free router cannot strengthen a configured alias merely by relabeling its group");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        return assertions;
    }
}
