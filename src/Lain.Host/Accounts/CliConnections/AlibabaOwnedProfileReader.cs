using System.Text.Json;
using System.Text.Json.Serialization;
using Lain.Core;

namespace Lain.Host.Accounts.CliConnections;

public sealed record AlibabaOwnedProfile(string ProfileReference, string Site, string Region, string WorkspaceId, string BaseUrl,
    [property: JsonIgnore] string? ApiKey, [property: JsonIgnore] string ConsoleToken)
{
    public ResourceBindingDto Binding => new("alibaba-model-studio", Product: "ordinary-api", Region: Region,
        ResourceType: "workspace", ResourceId: WorkspaceId, Provenance: "observed-cli-callback");
    public override string ToString() => "Alibaba CLI profile [redacted]";
}

/// <summary>Pinned config.json named block, never config show, global cache or arbitrary path discovery.</summary>
public sealed class AlibabaOwnedProfileReader(CliConnectionStateStore store)
{
    public AlibabaOwnedProfile Read(string profileReference)
    {
        var directory = store.ProfileDirectory(profileReference);
        using var json = JsonDocument.Parse(CliPrivatePaths.Read(Path.Combine(directory, "config.json"), 65536), new JsonDocumentOptions { MaxDepth = 12 });
        var root = json.RootElement;
        if (String(root, "active_config") != "lain" || !root.TryGetProperty("lain", out var profile) || profile.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Alibaba CLI did not create the selected owned profile.");
        if (new[] { "access_key_id", "access_key_secret", "security_token", "openapi_access_key_id", "openapi_access_key_secret" }
            .Any(name => profile.TryGetProperty(name, out _))) throw new InvalidDataException("Alibaba CLI profile contains unrequested cloud credentials.");
        var site = String(profile, "console_site");
        var region = String(profile, "console_region");
        var workspace = Identifier(String(profile, "workspace_id"));
        var baseUrl = String(profile, "base_url")?.TrimEnd('/');
        var token = Secret(String(profile, "access_token"));
        var key = Secret(String(profile, "api_key"));
        if (site is not ("international" or "domestic") || region is not ("cn-beijing" or "ap-southeast-1") || workspace is null || token is null
            || baseUrl != (region == "ap-southeast-1" ? "https://dashscope-intl.aliyuncs.com/compatible-mode/v1" : "https://dashscope.aliyuncs.com/compatible-mode/v1")
            || key?.StartsWith("sk-sp-", StringComparison.Ordinal) == true)
            throw new InvalidDataException("Alibaba CLI profile lacks a qualified ordinary API site, region, workspace or session.");
        // Same callback supplies the region, workspace and API host; never select
        // an arbitrary workspace or inherit the manufacturer's Beijing fallback.
        return new(profileReference, site, region, workspace, baseUrl, key, token);
    }
    internal static string? String(JsonElement node, string name) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    internal static string? Identifier(string? value) => value is { Length: > 0 and <= 200 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') ? value : null;
    internal static string? Secret(string? value) => value is { Length: > 0 and <= 16384 } && !value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)) ? value : null;
}
