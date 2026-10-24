using System.Text.Json;

namespace Lain.Host.Operations;

/// <summary>Optional Core files are read only when a Gateway operation actually needs the Core.</summary>
public static class ProxyBuildMetadata
{
    public static ProxyLaunchOptions Read(OwnStatePaths paths, string binary)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(binary))!;
        var manifestPath = Path.Combine(directory, "binary-manifest.json");
        var qualificationPath = Path.Combine(directory, "qualification.json");
        if (!File.Exists(manifestPath) || !File.Exists(qualificationPath)) return new(binary, "", false);
        using var manifest = ReadSmall(manifestPath);
        using var qualification = ReadSmall(qualificationPath);
        var hash = manifest.RootElement.GetProperty("sha256").GetString() ?? "";
        if (hash.Length != 64 || !hash.All(char.IsAsciiHexDigit)) throw new InvalidDataException("Invalid Core integrity metadata.");
        var source = qualification.RootElement;
        var hashMatches = source.TryGetProperty("sha256", out var digest) && digest.GetString() == hash;
        var passed = source.TryGetProperty("passed", out var pass) && pass.ValueKind == JsonValueKind.True;
        var isolatedReference = Environment.GetEnvironmentVariable("LAIN_REFERENCE_QUALIFICATION") == "1"
            && paths.Root.StartsWith(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        if (!passed && isolatedReference)
            passed = source.TryGetProperty("syntheticPassed", out var synthetic) && synthetic.ValueKind == JsonValueKind.True;
        return new(binary, hash, hashMatches && passed);
    }

    private static JsonDocument ReadSmall(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 128 * 1024) throw new InvalidDataException("Core integrity metadata exceeds its bound.");
        return JsonDocument.Parse(stream);
    }
}
