using System.Text.Json;
using Lain.Core;
using Lain.Host.Models;
using Lain.Host.Operations;
using Lain.Host.Providers;

internal static class AccountPickerCases
{
    public static int Run()
    {
        var assertions = 0;
        void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
        var directory = Path.Combine(Path.GetTempPath(), "lain-account-picker-" + Guid.NewGuid().ToString("N"));
        try
        {
            var repository = new ConfigurationRepository(new OwnStatePaths(directory));
            var state = repository.Update(0, configuration => configuration with {
                Accounts = [new("private", "Private", "codex", true, "oauth", "ready"),
                    new("work", "Work", "codex", true, "oauth", "ready"),
                    new("outside", "Outside", "codex", true, "oauth", "ready"),
                    new("disabled", "Disabled", "codex", false, "oauth", "ready"),
                    new("command", "Do not duplicate this label", "commandcode", true, "api-key", "ready")],
                Routes = [new("a", "private-model", "same-model", "codex", ["private"]),
                    new("a2", "private-second", "second-model", "codex", ["private"]),
                    new("b", "work-model", "same-model", "codex", ["work"]),
                    new("pool", "pool-model", "same-model", "codex", ["private", "work"], Pooled:true),
                    new("review", "review-alias", "codex-auto-review", "codex", ["outside"]),
                    new("hidden", "hidden-alias", "same-model", "codex", ["outside"], Visible:false),
                    new("offprofile", "outside-alias", "same-model", "codex", ["outside"]),
                    new("inactive", "inactive-alias", "same-model", "codex", ["disabled"], Enabled:false),
                    new("c", "command-model", "another-model", "commandcode", ["command"])],
                Profiles = [new("profile", "Fixture", ["b", "a2", "a", "pool", "review", "hidden", "inactive", "c"], "a")],
                Collections = [new("million", "1M", ["c", "b", "a"]), new("security", "ITSec", ["a", "c"])]
            });
            var service = new ModelsService(repository);
            CodexProfileArtifacts Export(bool grouped = true) => new CodexProfileAdapter(service).Generate("profile", state.Revision,
                "/opt/lain/lain", "/tmp/models.json", enableLainModelPicker:grouped);
            var original = JsonSerializer.Serialize(repository.Snapshot);
            using var document = JsonDocument.Parse(Export().Catalog);
            var groups = document.RootElement.GetProperty("lain_model_picker").GetProperty("groups").EnumerateArray().ToDictionary(group => group.GetProperty("id").GetString()!);
            string[] Members(string id) => groups[id].GetProperty("models").EnumerateArray().Select(model => model.GetString()!).ToArray();
            var models = document.RootElement.GetProperty("models").EnumerateArray().ToDictionary(model => model.GetProperty("slug").GetString()!);
            Check(groups.Keys.SequenceEqual(new[] { "account:private", "account:work", "provider:codex", "provider:commandcode", "collection:million", "collection:security" }), "Automatic provider/account groups precede custom collections in saved order");
            Check(groups["account:private"].GetProperty("name").GetString() == "Codex (Private)" &&
                groups["account:work"].GetProperty("name").GetString() == "Codex (Work)", "Separate accounts have readable stable group labels");
            Check(Members("account:private").SequenceEqual(new[] { "private-second", "private-model" }) &&
                Members("account:work").SequenceEqual(new[] { "work-model" }), "Strict account views retain profile order and existing wire aliases");
            Check(groups["provider:codex"].GetProperty("name").GetString() == "Codex (Pool)" && Members("provider:codex").SequenceEqual(new[] { "pool-model" }), "Pooled routes stay visible without being attributed to a single account");
            Check(Members("collection:million").SequenceEqual(new[] { "command-model", "work-model", "private-model" }) &&
                groups["collection:million"].GetProperty("name").GetString() == "1M", "Custom collections can span providers while preserving their label and member order");
            Check(models["private-model"].GetProperty("display_name").GetString() == "same-model · Codex (Private)" &&
                models["work-model"].GetProperty("display_name").GetString() == "same-model · Codex (Work)" &&
                models["pool-model"].GetProperty("display_name").GetString() == "same-model · Codex (Pool)", "All-model labels disambiguate same-provider accounts without changing slugs");
            Check(groups["provider:commandcode"].GetProperty("name").GetString() == "Command Code" &&
                !models["command-model"].GetProperty("display_name").GetString()!.Contains("Do not duplicate"), "One-account providers retain short labels");
            Check(!groups.ContainsKey("account:outside") && !groups.ContainsKey("account:disabled") &&
                models["review-alias"].GetProperty("visibility").GetString() == "hide", "Foreign-profile, hidden, disabled and internal-review routes do not create account groups");
            Check(JsonSerializer.Serialize(repository.Snapshot) == original, "Derived groups require no saved routers and never copy or mutate state");

            state = repository.Update(state.Revision, configuration => configuration with {
                Routes = [.. configuration.Routes, new("a-million", "custom-1m", "same-model", "codex", ["private"])],
                Profiles = [configuration.Profiles[0] with { RouteIds = [.. configuration.Profiles[0].RouteIds, "a-million"] }]
            });
            using var duplicate = JsonDocument.Parse(Export().Catalog);
            var duplicateModels = duplicate.RootElement.GetProperty("models").EnumerateArray().ToDictionary(model => model.GetProperty("slug").GetString()!);
            Check(duplicateModels["custom-1m"].GetProperty("display_name").GetString() == "same-model (custom-1m) · Codex (Private)" &&
                duplicateModels["private-model"].GetProperty("display_name").GetString() == "same-model (private-model) · Codex (Private)", "Aliases disambiguate same-account same-model custom variants such as 1M");

            // Saving an old provider router remains structural; its execution/export is refused.
            state = service.PutCollectionRouter(state.Revision, new("old-provider", "auto-codex", "provider:codex", "profile"));
            try { Export(); throw new Exception("Ambiguous provider router export accepted"); }
            catch (ArgumentException error) when (error.Message.Contains("multiple exported accounts")) { assertions++; }
            using var ordinary = JsonDocument.Parse(Export(false).Catalog);
            Check(!ordinary.RootElement.TryGetProperty("lain_model_picker", out _), "An ambiguous saved provider router does not block ordinary export");
            state = service.DeleteCollectionRouter(state.Revision, "old-provider");
            state = service.PutCollectionRouter(state.Revision, new("account-router", "auto-private", "account:private", "profile"));
            var active = state.Routes.Where(route => route.Enabled && state.Profiles[0].RouteIds.Contains(route.Id)).ToArray();
            var transport = new ProxyTransportDescription(state.Accounts.Where(account => account.Enabled).ToDictionary(account => account.Id, account => "synthetic-" + account.Id),
                new Dictionary<string, IReadOnlyDictionary<string, string>>(), new HashSet<string>(),
                active.ToDictionary(route => route.Id, route => new ModelCompatibilityTransportBinding(route.Provider, route.UpstreamModel, "responses", "synthetic:account-export")));
            var proof = CollectionRouterCompiler.CompileForExport(state, "profile", transport);
            using var routed = JsonDocument.Parse(new CodexProfileAdapter(service, routerProof:proof).Generate("profile", state.Revision,
                "/opt/lain/lain", "/tmp/models.json", enableLainModelPicker:true).Catalog);
            Check(routed.RootElement.GetProperty("lain_model_picker").GetProperty("groups").EnumerateArray().Single(group => group.GetProperty("id").GetString() == "account:private").GetProperty("router_model").GetString() == "auto-private" &&
                routed.RootElement.GetProperty("models").EnumerateArray().Single(model => model.GetProperty("slug").GetString() == "auto-private").GetProperty("display_name").GetString() == "Automatisch · Codex (Private)", "Compiled account routers export their actual alias with the readable account group label");
            state = service.DeleteCollectionRouter(state.Revision, "account-router");
            state = repository.Update(state.Revision, configuration => configuration with {
                Profiles = [configuration.Profiles[0] with { RouteIds = ["a", "review", "hidden", "inactive", "c"] }]
            });
            using var single = JsonDocument.Parse(Export().Catalog);
            var singleGroups = single.RootElement.GetProperty("lain_model_picker").GetProperty("groups").EnumerateArray().ToDictionary(group => group.GetProperty("id").GetString()!);
            Check(singleGroups.ContainsKey("provider:codex") && !singleGroups.ContainsKey("account:private") &&
                singleGroups["provider:codex"].GetProperty("name").GetString() == "Codex", "Accounts outside the exported profile do not split a one-account provider");
            state = repository.Update(state.Revision, configuration => configuration with {
                Routes = [.. configuration.Routes, new("single-pool", "single-pool-alias", "same-model", "codex", ["private"], Pooled:true)],
                Profiles = [configuration.Profiles[0] with { RouteIds = ["single-pool"] , DefaultRouteId = "single-pool" }]
            });
            using var singlePool = JsonDocument.Parse(Export().Catalog);
            Check(singlePool.RootElement.GetProperty("models")[0].GetProperty("display_name").GetString() == "same-model · Codex (Pool)" &&
                singlePool.RootElement.GetProperty("lain_model_picker").GetProperty("groups")[0].GetProperty("id").GetString() == "provider:codex", "Explicit pools stay marked even when only one account participates");

        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        return assertions;
    }
}
