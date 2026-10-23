using System.Text.Json;
using Lain.Core;
using Lain.Host.Models;
using Lain.Host.Operations;

var directory = Path.Combine(Path.GetTempPath(), "lain-models-" + Guid.NewGuid().ToString("N"));
try
{
    var repo = new ConfigurationRepository(new OwnStatePaths(directory));
    var config = repo.Update(0, c => c with { Accounts = [new("private-account-a", "Private label", "codex", true, "oauth", "ready"), new("private-account-b", "Second", "codex", true, "oauth", "ready")] });
    var models = new ModelsService(repo);
    var route = new RouteDto("route-a", "lain-test", "upstream-model", "codex", ["private-account-a"]);
    config = models.PutRoute(config.Revision, route);
    Check(config.AppliedRevision == 0, "saving does not claim applied");
    Throws<RevisionConflictException>(() => models.PutRoute(1, route));
    Throws<ArgumentException>(() => models.PutRoute(config.Revision, route with { AccountIds = ["private-account-a", "private-account-b"] }));
    Throws<ArgumentException>(() => models.PutRoute(config.Revision, route with { Provider = "other" }));
    Throws<ArgumentException>(() => models.PutRoute(config.Revision, route with { Id = "route-b", Alias = "LAIN-TEST" }));
    config = models.PutRoute(config.Revision, route with { Id = "pool", Alias = "pool-test", AccountIds = ["private-account-a", "private-account-b"], Pooled = true, Visible = false });
    config = models.PutProfile(config.Revision, new("profile-a", "Work", ["route-a", "pool"], "route-a"));
    Throws<ArgumentException>(() => models.DeleteRoute(config.Revision, "route-a"));
    var export = JsonSerializer.Serialize(models.Export("profile-a"));
    Check(!export.Contains("private-account") && !export.Contains("Private label") && export.Contains("account-1"), "export substitutes account identities");
    Check(models.ConfiguredModels().Models.All(x => x.Source == "configured"), "configured catalog source explicit");
    var adapter = new CodexProfileAdapter(models);
    Throws<ArgumentException>(() => adapter.Generate("profile-a", config.Revision, "lain", "/tmp/models.json"));
    Throws<ArgumentException>(() => adapter.Generate("profile-a", config.Revision, "/opt/lain/lain", "/tmp/models.json", "https://example.com/v1"));
    var artifacts = adapter.Generate("profile-a", config.Revision, "/opt/lain/lain", "/tmp/lain-models.json");
    Check(artifacts.ConfigFileName == "lain.config.toml" && artifacts.LaunchArguments.SequenceEqual(new[] { "--no-daemon", "--profile", "lain" }), "external profile launch");
    Check(artifacts.Configuration.Contains("[model_providers.lain.auth]") && artifacts.Configuration.Contains("\"--audience\", \"codex\"") && !artifacts.Configuration.Contains("requires_openai_auth"), "command auth export");
    Check(!artifacts.Catalog.Contains("pool-test") && !artifacts.Catalog.Contains("private-account"), "catalog only enabled visible aliases");
    using var doc = JsonDocument.Parse(artifacts.Catalog);
    Check(doc.RootElement.GetProperty("models").GetArrayLength() == 1, "catalog count");
    Throws<ArgumentException>(() => RouteBindings.Compile(config, new Dictionary<string, string>()));
    var bindings = RouteBindings.Compile(config, new Dictionary<string, string> { ["private-account-a"] = "core-a", ["private-account-b"] = "core-b" });
    Check(bindings.Routes["lain-test"].Selection == "strict" && bindings.Routes["pool-test"].Selection == "pool", "explicit routing selection");
    Check(bindings.Routes["lain-test"].Visible && !bindings.Routes["pool-test"].Visible, "hidden enabled routes remain bound with explicit visibility");
    using (var visibility = JsonDocument.Parse(JsonSerializer.Serialize(bindings)))
        Check(visibility.RootElement.GetProperty("routes").GetProperty("lain-test").GetProperty("visible").GetBoolean() &&
              !visibility.RootElement.GetProperty("routes").GetProperty("pool-test").GetProperty("visible").GetBoolean(), "launcher JSON preserves model menu visibility");
    var legacyRoute = JsonSerializer.Deserialize<CoreRouteBinding>("{\"id\":\"old\",\"model\":\"model\",\"accounts\":[\"account\"],\"selection\":\"strict\"}");
    Check(legacyRoute!.Visible, "legacy route bindings default to visible");
    Check(legacyRoute.Provider is null, "legacy route bindings leave provider unspecified");
    var commandConfig = config with
    {
        Accounts = config.Accounts.Select(account => account with { Provider = "command" }).ToArray(),
        Routes = config.Routes.Select(selectedRoute => selectedRoute with { Provider = "command" }).ToArray()
    };
    var commandBindings = RouteBindings.Compile(commandConfig, bindings.Accounts);
    Check(commandBindings.Routes["lain-test"].Provider == "command", "canonical route provider survives transport binding");
    using (var providerFile = JsonDocument.Parse(JsonSerializer.Serialize(commandBindings)))
        Check(providerFile.RootElement.GetProperty("routes").GetProperty("lain-test").GetProperty("provider").GetString() == "command", "launcher provider field is canonical");
    Check(JsonSerializer.Serialize(bindings).Contains("\"routes\"") && JsonSerializer.Serialize(bindings).Contains("\"accounts\""), "launcher JSON contract");
    var writer = new RouteBindingsWriter(new OwnStatePaths(directory));
    writer.Write(config, bindings.Accounts);
    Check(File.ReadAllText(writer.BindingsPath).Contains("core-a"), "private bindings artifact");
    Check(!JsonSerializer.Serialize(bindings).Contains("\"credentials\""), "legacy binding shape omits credentials");
    var transports = new Dictionary<string, IReadOnlyDictionary<string, string>>
    {
        ["route-a"] = new Dictionary<string, string> { ["private-account-a"] = "core-responses-a" },
        ["pool"] = new Dictionary<string, string> { ["private-account-a"] = "core-chat-a", ["private-account-b"] = "core-chat-b" }
    };
    var multi = RouteBindings.Compile(config, bindings.Accounts, transports);
    Check(multi.Accounts.Count == 2 && multi.Routes["lain-test"].Accounts.Single() == "private-account-a" &&
          multi.Routes["lain-test"].Credentials!["private-account-a"] == "core-responses-a" &&
          multi.Routes["pool-test"].Credentials!["private-account-a"] == "core-chat-a", "one logical account retains distinct route transport credentials");
    var overridesOnly = RouteBindings.Compile(config, new Dictionary<string, string>(), transports);
    Check(overridesOnly.Accounts.Count == 0, "complete route bindings do not need arbitrary account defaults");
    writer.Write(config, bindings.Accounts, transports);
    using (var transportFile = JsonDocument.Parse(File.ReadAllText(writer.BindingsPath)))
        Check(transportFile.RootElement.GetProperty("routes").GetProperty("lain-test").GetProperty("credentials").GetProperty("private-account-a").GetString() == "core-responses-a", "route credentials serialized with launcher field names");
    Throws<ArgumentException>(() => RouteBindings.Compile(config, bindings.Accounts,
        new Dictionary<string, IReadOnlyDictionary<string, string>> { ["pool"] = new Dictionary<string, string> { ["private-account-a"] = "core-chat-a" } }));
    Throws<ArgumentException>(() => RouteBindings.Compile(config, bindings.Accounts,
        new Dictionary<string, IReadOnlyDictionary<string, string>> { ["route-a"] = new Dictionary<string, string> { ["private-account-b"] = "core-b" } }));
    Throws<ArgumentException>(() => RouteBindings.Compile(config, bindings.Accounts,
        new Dictionary<string, IReadOnlyDictionary<string, string>> { ["route-a"] = new Dictionary<string, string> { ["private-account-a"] = " " } }));
    Throws<ArgumentException>(() => RouteBindings.Compile(config, bindings.Accounts,
        new Dictionary<string, IReadOnlyDictionary<string, string>> { ["route-a"] = new Dictionary<string, string> { ["private-account-a"] = "core-b" } }));
    var reread = new ConfigurationRepository(new OwnStatePaths(directory));
    Check(reread.Snapshot.Revision == config.Revision && reread.Snapshot.Profiles.Length == 1, "configuration survives restart");
    if (args.Length >= 1)
    {
        var destination = Path.GetFullPath(args[0]); Directory.CreateDirectory(destination);
        var generated = adapter.Generate("profile-a", config.Revision, args.Length > 1 ? Path.GetFullPath(args[1]) : "/usr/bin/printf", Path.Combine(destination, "lain.models.json"), args.Length > 2 ? args[2] : "http://127.0.0.1:18417/v1");
        File.WriteAllText(Path.Combine(destination, generated.ConfigFileName), generated.Configuration);
        File.WriteAllText(Path.Combine(destination, generated.CatalogFileName), generated.Catalog);
    }
    Console.WriteLine("PASS models: CAS, persistence, strict/pool validation, references, export privacy, Codex artifacts");
}
finally { Directory.Delete(directory, true); }

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
