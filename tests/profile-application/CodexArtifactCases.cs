using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lain.Cli;
using Artifacts = Lain.Client.CodexProfileArtifacts;

internal static class CodexArtifactCases
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string description) { checks++; if (!value) throw new Exception(description); }
        string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        Artifacts Sign(Artifacts value) => value with { GenerationId = Hash(value.ProfileId + "\n" + value.Revision.ToString(CultureInfo.InvariantCulture) + "\n" + value.Configuration + "\n" + value.Catalog) };
        var configuration = "model = \"native-astra\"\nmodel_reasoning_effort = \"low\"\nmodel_catalog_json = \"/ignored/lain.models.json\"\n[model_providers.lain]\nmodel_catalog_json = \"table key is not the catalog binding\"\n";
        var catalog = """{"models":[{"slug":"native-astra","visibility":"list","supported_in_api":true,"default_reasoning_level":"low","supported_reasoning_levels":[{"effort":"low","description":"Low"},{"effort":"ultra","description":"Client choice"}]}]}""";
        var export = Sign(new("lain.config.toml", configuration, "lain.models.json", catalog, [], 7, ProfileId: "profile"));
        Directory.CreateDirectory(root);
        var baseConfig = Path.Combine(root, "config.toml");
        File.WriteAllText(baseConfig, "model_reasoning_effort = \"ultra\"\n# Existing user settings\n");
        var installed = CodexArtifactInstaller.Install(export, root, "profile", 7);
        var before = File.ReadAllText(installed.ConfigurationPath);
        Check(before.Contains(JsonSerializer.Serialize(installed.CatalogPath)) && before.Contains("table key is not the catalog binding"), "Only the top-level binding is rewritten to the immutable generation");
        Check(CodexArtifactInstaller.Inspect(root, "profile", 7) is { State: "installed", Stale: false }, "A coherent pair installs and inspects successfully");

        void Reject(Artifacts value, string message, string? expectedProfile = null, long? expectedRevision = null)
        {
            var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
            checks++;
            try { CodexArtifactInstaller.Install(value, root, expectedProfile, expectedRevision); }
            catch (InvalidDataException error) when (error.Message.Contains(message))
            {
                Check(File.ReadAllText(installed.ConfigurationPath) == before &&
                    Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).SequenceEqual(files) &&
                    CodexArtifactInstaller.Inspect(root, "profile", 7).State == "installed", "Rejected artifacts leave the committed pair and file inventory unchanged");
                return;
            }
            throw new Exception("Expected artifact rejection: " + message);
        }
        Reject(export with { Revision = 8 }, "generation");
        Reject(export with { ProfileId = "other" }, "generation");
        Reject(export with { Catalog = catalog + " " }, "generation");
        Reject(export with { Configuration = configuration.Replace("native-astra", "renamed-astra") }, "generation");
        Reject(export, "stale", expectedProfile: "other");
        Reject(export, "stale", expectedRevision: 8);
        Reject(Sign(export with { Configuration = configuration.Replace("model_catalog_json = \"/ignored/lain.models.json\"\n", "") }), "top-level model_catalog_json");
        Reject(Sign(export with { Configuration = configuration.Replace("model_catalog_json = \"/ignored/lain.models.json\"", "model_catalog_json = \"first\"\n  model_catalog_json = \"second\"") }), "top-level model_catalog_json");
        Reject(Sign(export with { Configuration = configuration.Replace("model = \"native-astra\"", "model = \"absent\"") }), "absent");
        Reject(Sign(export with { Configuration = configuration.Replace("model_reasoning_effort = \"low\"", "model_reasoning_effort = \"none\"") }), "not advertised");
        Reject(Sign(export with { Configuration = configuration.Replace("model_reasoning_effort = \"low\"\n", "") }), "bind a supported default");
        Reject(Sign(export with { Catalog = """{"models":[{"slug":"native-astra","default_reasoning_level":"none","supported_reasoning_levels":[]}]}""" }), "default reasoning");
        Reject(Sign(export with { Catalog = catalog.Replace("\"low\",\"supported_reasoning_levels\"", "\"none\",\"supported_reasoning_levels\"") }), "default reasoning");
        Reject(Sign(export with { Catalog = """{"models":[{"slug":"native-astra","default_reasoning_level":null,"supported_reasoning_levels":[]},{"slug":"native-astra"}]}""",
            Configuration = configuration.Replace("model_reasoning_effort = \"low\"\n", "") }), "duplicate model");
        Reject(Sign(export with { Catalog = catalog.Replace("\"effort\":\"ultra\"", "\"effort\":\"low\"") }), "duplicate reasoning");
        Reject(Sign(export with { Catalog = catalog.Replace("\"list\"", "\"hide\"") }), "hidden");
        Reject(Sign(export with { Catalog = catalog.Replace("\"supported_in_api\":true", "\"supported_in_api\":false") }), "unavailable");
        Reject(Sign(export with { Catalog = "{\"models\":[]}" }), "contain models");
        Reject(Sign(export with { Catalog = "{" }), "invalid JSON");
        Reject(Sign(export with { Catalog = catalog.Replace("\"models\":", "\"models\":[],\"models\":") }), "duplicate models");
        var unknown = Sign(export with { Configuration = configuration.Replace("model_reasoning_effort = \"low\"\n", ""),
            Catalog = """{"models":[{"slug":"native-astra","default_reasoning_level":null,"supported_reasoning_levels":[]}]}""" });
        Reject(unknown, "inherited reasoning effort");
        Check(File.ReadAllText(baseConfig) == "model_reasoning_effort = \"ultra\"\n# Existing user settings\n", "Unsupported inherited controls require explicit handling without rewriting the user's config");
        var selectedUltra = Sign(export with { Configuration = configuration.Replace("model_reasoning_effort = \"low\"", "model_reasoning_effort = \"ultra\"") });
        var ultra = CodexArtifactInstaller.Install(selectedUltra, Path.Combine(root, "explicit-ultra"));
        Check(File.ReadAllText(ultra.ConfigurationPath).Contains("model_reasoning_effort = \"ultra\""), "A legitimate explicit ultra selection remains intact");
        var noBase = CodexArtifactInstaller.Install(unknown, Path.Combine(root, "unknown-no-override"));
        Check(CodexArtifactInstaller.Inspect(Path.GetDirectoryName(noBase.ConfigurationPath)!).State == "installed", "Unknown reasoning remains null when no base effort can leak into the overlay");
        Check(CodexArtifactInstaller.Inspect(root, "profile", 8).Stale == true && CodexArtifactInstaller.Inspect(root, "other", 7).Stale == true, "Revision and profile identity remain separate stale checks");
        Check(File.ReadAllText(installed.CatalogPath) == catalog, "Rejected updates preserve old immutable catalogs");
        return checks;
    }
}
