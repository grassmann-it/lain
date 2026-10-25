using System.Text.Json;
using Lain.Core;
using Lain.Host.Models;
using Lain.Host.Operations;

internal static class PickerCases
{
    public static int Run()
    {
        var assertions = 0;
        void Check(bool condition, string name) { assertions++; if (!condition) throw new Exception(name); }
        var directory = Path.Combine(Path.GetTempPath(), "lain-picker-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            var repository = new ConfigurationRepository(new OwnStatePaths(directory));
            var state = repository.Update(0, configuration => configuration with
            {
                Accounts = [new("codex-account", "Private", "codex", true, "oauth", "unverified"),
                    new("router-account", "Private", "openrouter", true, "api-key", "unverified"),
                    new("unknown-account", "Private", "commandcode", true, "api-key", "unverified")],
                Routes = [new("a", "alias-a", "ordinary-model", "codex", ["codex-account"]),
                    new("b", "alias-b", "qwen/qwen3-coder:free", "openrouter", ["router-account"], ProviderRouteId: "openrouter-qwen3-coder-free"),
                    new("review", "review-alias", "codex-auto-review", "codex", ["codex-account"]),
                    new("hidden", "hidden-alias", "hidden-model", "codex", ["codex-account"], Visible: false),
                    new("disabled", "disabled-alias", "disabled-model", "codex", ["codex-account"], Enabled: false),
                    new("offprofile", "offprofile-alias", "outside-model", "codex", ["codex-account"]),
                    new("unknown", "unknown-alias", "unknown:free", "commandcode", ["unknown-account"])],
                // The Host profile ID differs from the generated Codex --profile lain.
                Profiles = [new("host-work", "Work", ["a", "review", "b", "hidden", "disabled", "unknown"], "a")],
                Collections = [new("security", "IT Sec", ["b", "a", "hidden", "disabled", "offprofile", "review", "unknown"]),
                    new("overlap", "Overlap", ["a", "b"]), new("empty", "Empty here", ["hidden", "offprofile"])]
            });
            var adapter = new CodexProfileAdapter(new ModelsService(repository));
            CodexProfileArtifacts Export(bool enabled = false) => adapter.Generate("host-work", state.Revision,
                "/opt/lain/lain", "/tmp/lain.models.json", enableLainModelPicker: enabled);
            var unchanged = JsonSerializer.Serialize(repository.Snapshot);
            var standard = Export();
            using var standardCatalog = JsonDocument.Parse(standard.Catalog);
            Check(standardCatalog.RootElement.EnumerateObject().Select(property => property.Name).SequenceEqual(new[] { "models" }), "The standard export retains the native models-only catalog");
            var grouped = Export(true);
            using var catalog = JsonDocument.Parse(grouped.Catalog);
            var picker = catalog.RootElement.GetProperty("lain_model_picker");
            Check(picker.GetProperty("schema_version").GetInt32() == 1 && picker.EnumerateObject().Count() == 2, "The opt-in extension uses the exact versioned lain schema");
            var groups = picker.GetProperty("groups").EnumerateArray().ToDictionary(group => group.GetProperty("id").GetString()!);
            string[] Members(string id) => groups[id].GetProperty("models").EnumerateArray().Select(model => model.GetString()!).ToArray();
            Check(groups.Keys.SequenceEqual(new[] { "collection:security", "collection:overlap", "provider:codex", "provider:commandcode", "provider:openrouter" }), "Custom group order is preserved before provider groups; empty views are omitted");
            Check(Members("collection:security").SequenceEqual(new[] { "alias-b", "alias-a", "unknown-alias" }), "Custom membership preserves order while excluding hidden, disabled, off-profile and internal review routes");
            Check(Members("collection:overlap").SequenceEqual(new[] { "alias-a", "alias-b" }) && Members("provider:openrouter").SequenceEqual(new[] { "alias-b" }), "Overlapping custom and service groups reference the same existing inference aliases");
            Check(groups["provider:commandcode"].GetProperty("name").GetString() == "Command Code" && Members("provider:codex").SequenceEqual(new[] { "alias-a" }), "Provider group names are readable and omit the internal review model");
            Check(!groups.ContainsKey("policy:free"), "A hypothetical free_only override cannot advertise configured-policy routes as free");
            Check(groups.Values.All(group => group.GetProperty("router_model").ValueKind == JsonValueKind.Null &&
                group.EnumerateObject().Select(property => property.Name).SequenceEqual(new[] { "id", "name", "router_model", "models" })), "Every group has a disabled router target and the exact patched-client group shape");
            Check(groups.Values.All(group => group.GetProperty("models").EnumerateArray().All(model =>
                catalog.RootElement.GetProperty("models").EnumerateArray().Any(entry => entry.GetProperty("slug").GetString() == model.GetString() && entry.GetProperty("visibility").GetString() == "list"))), "Every group member names one visible catalog model");
            Check(catalog.RootElement.GetProperty("models").GetRawText() == standardCatalog.RootElement.GetProperty("models").GetRawText(), "Grouping does not duplicate or mutate the native model entries");
            Check(grouped.Configuration == standard.Configuration && grouped.ConfigFileName == "lain.config.toml" &&
                grouped.LaunchArguments.SequenceEqual(new[] { "--no-daemon", "--profile", "lain" }) && grouped.Configuration.Contains("model = \"alias-a\""), "Opt-in changes only the catalog extension and preserves the default and separate Codex profile");
            Check(grouped.GenerationId != standard.GenerationId && grouped.GenerationId == Export(true).GenerationId, "The deterministic artifact generation includes the opt-in extension");
            Check(JsonSerializer.Serialize(repository.Snapshot) == unchanged && !Directory.EnumerateFiles(directory, "*.toml").Any(), "Grouped export is pure and does not activate, install or mutate configuration");
            state = repository.Update(state.Revision, configuration => configuration with
            {
                Routes = configuration.Routes.Select(route => route.Id == "b" ? route with { Policy = new RoutingPolicyDto("free_only") } : route).ToArray()
            });
            using var unbound = JsonDocument.Parse(Export(true).Catalog);
            Check(!unbound.RootElement.GetProperty("lain_model_picker").GetProperty("groups").EnumerateArray().Any(group => group.GetProperty("id").GetString() == "policy:free"), "Effective free_only without a source-qualified transport binding still cannot advertise a derived Free group");
            state = repository.Update(state.Revision, configuration => configuration with
            {
                Collections = configuration.Collections!.Select(collection => collection.Id == "security" ? collection with { Name = "Security renamed" } : collection).ToArray()
            });
            using var renamed = JsonDocument.Parse(Export(true).Catalog);
            var renamedGroup = renamed.RootElement.GetProperty("lain_model_picker").GetProperty("groups").EnumerateArray().Single(group => group.GetProperty("id").GetString() == "collection:security");
            Check(renamedGroup.GetProperty("name").GetString() == "Security renamed" && renamedGroup.GetProperty("models").EnumerateArray().Select(model => model.GetString()).SequenceEqual(new[] { "alias-b", "alias-a", "unknown-alias" }), "Renaming a collection preserves its stable group identity and existing inference aliases");
            var unicodeName = new string('界', 256);
            state = repository.Update(state.Revision, configuration => configuration with
            {
                Collections = Enumerable.Range(0, 125).Select(index => new ModelCollectionDto("group-" + index,
                    index == 0 ? unicodeName : "Group " + index, ["a"])).ToArray()
            });
            using var bounded = JsonDocument.Parse(Export(true).Catalog);
            var boundedGroups = bounded.RootElement.GetProperty("lain_model_picker").GetProperty("groups");
            Check(boundedGroups.GetArrayLength() == 128 && boundedGroups[0].GetProperty("name").GetString() == unicodeName,
                "Exactly 128 non-empty groups and the complete 256-character Unicode name fit the aligned client limits");
            state = repository.Update(state.Revision, configuration => configuration with
            {
                Collections = [.. configuration.Collections!, new("overflow", "Overflow", ["a"])]
            });
            try { Export(true); throw new Exception("Too many groups must fail clearly"); }
            catch (ArgumentException error) when (error.Message.Contains("at most 128")) { assertions++; }
            using var ordinaryOverflow = JsonDocument.Parse(Export().Catalog);
            Check(!ordinaryOverflow.RootElement.TryGetProperty("lain_model_picker", out _) && repository.Snapshot.Collections!.Length == 126,
                "The group limit never truncates stored collections or prevents a normal native export");
            var importedProvider = new string('界', 100);
            state = repository.Update(state.Revision, configuration => configuration with
            {
                Collections = [],
                Accounts = configuration.Accounts.Select(account => account.Provider == "codex" ? account with { Provider = importedProvider } : account).ToArray(),
                Routes = configuration.Routes.Select(route => route.Provider == "codex" ? route with { Provider = importedProvider } : route).ToArray()
            });
            try { Export(true); throw new Exception("Overlong UTF-8 provider group identity must fail clearly"); }
            catch (ArgumentException error) when (error.Message.Contains("256 UTF-8 bytes")) { assertions++; }
            Check(repository.Snapshot.Routes.First().Provider == importedProvider && !string.IsNullOrWhiteSpace(Export().Catalog),
                "An unsupported imported provider group identity is never truncated and does not prevent the ordinary export");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        return assertions;
    }
}
