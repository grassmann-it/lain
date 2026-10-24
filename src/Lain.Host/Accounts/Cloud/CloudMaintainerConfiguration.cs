using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lain.Host.Accounts.Cloud;

public sealed record CloudMaintainerOptions(CloudOAuthClient[] Clients, bool CloudflareNeuronCandidateQualified = false);

/// <summary>Optional public client registrations, not credentials. Absence keeps every browser method gated.</summary>
public static class CloudMaintainerConfiguration
{
    public const string RelativePath = "maintainer/cloud-oauth.json";
    public static CloudMaintainerOptions Load(string path)
    {
        if (!File.Exists(path)) return new([]);
        if (new FileInfo(path).LinkTarget is not null || new FileInfo(path).Length > 65536)
            throw new InvalidDataException("Invalid maintainer cloud configuration file.");
        try
        {
            var options = JsonSerializer.Deserialize<CloudMaintainerOptions>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { MaxDepth = 8, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow });
            if (options?.Clients is null || options.Clients.Length > 2 || options.Clients.Any(c => c is null)
                || options.Clients.Select(c => c.Provider).Distinct(StringComparer.Ordinal).Count() != options.Clients.Length)
                throw new InvalidDataException("Invalid maintainer cloud registration set.");
            // Validate registration shape without starting callbacks or making provider requests.
            foreach (var client in options.Clients) { using var validation = new CloudOAuthAdapter(client); }
            return options;
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        { throw new InvalidDataException("Invalid maintainer cloud registration. Client secrets, tokens and unknown fields are not accepted."); }
    }
}
