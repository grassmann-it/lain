using System.Text.Json;

namespace Lain.Host.Models;

/// <summary>Only a matching applied runtime proves which profile the proxy uses.</summary>
public sealed record AppliedProfileStatus(string? Id = null, long? Revision = null)
{
    public static AppliedProfileStatus Read(string activeProfilePath, string runtimePath, long appliedRevision)
    {
        try
        {
            if (appliedRevision < 0 || !File.Exists(activeProfilePath) || !File.Exists(runtimePath)) return new();
            using var active = ReadSmall(activeProfilePath);
            using var runtime = ReadSmall(runtimePath);
            if (!runtime.RootElement.TryGetProperty("valid", out var valid) || valid.ValueKind != JsonValueKind.True
                || !runtime.RootElement.TryGetProperty("revision", out var runtimeRevision) || !runtimeRevision.TryGetInt64(out var r)
                || r != appliedRevision || !active.RootElement.TryGetProperty("revision", out var revision)
                || !revision.TryGetInt64(out var a) || a != appliedRevision
                || !active.RootElement.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(id.GetString()) || id.GetString()!.Length > 200
                || !runtime.RootElement.TryGetProperty("profileId", out var profileId)
                || profileId.ValueKind != JsonValueKind.String
                || !string.Equals(profileId.GetString(), id.GetString(), StringComparison.Ordinal)) return new();
            return new(id.GetString(), a);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return new();
        }
    }

    private static JsonDocument ReadSmall(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > 16 * 1024) throw new IOException("Applied-profile metadata is too large.");
        return JsonDocument.Parse(stream);
    }
}
