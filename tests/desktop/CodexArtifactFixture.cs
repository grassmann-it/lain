using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lain.Client;

internal static class CodexArtifactFixture
{
    internal static CodexProfileArtifacts Create(string profileId, long revision, string? catalog = null)
    {
        const string configuration = "model = \"fixture-model\"\nmodel_catalog_json = \"/fixture/lain.models.json\"\n";
        catalog ??= "{\"models\":[{\"slug\":\"fixture-model\",\"default_reasoning_level\":null,\"supported_reasoning_levels\":[]}]}";
        var generation = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profileId + "\n" + revision.ToString(CultureInfo.InvariantCulture) + "\n" + configuration + "\n" + catalog))).ToLowerInvariant();
        return new("lain.config.toml", configuration, "lain.models.json", catalog, ["--no-daemon", "--profile", "lain"], revision,
            ProfileId: profileId, GenerationId: generation);
    }
}
