using System.Text.Json;

namespace Lain.Host.Operations;

/// <summary>Only an explicit service Stop writes suppression; cleanup and internal profile stops never do.</summary>
public sealed class RuntimeStartIntent
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string path;
    private volatile bool suppressed;
    public bool AutomaticStartSuppressed => suppressed;

    public RuntimeStartIntent(OwnStatePaths paths)
    {
        path = Path.Combine(paths.Root, "runtime-start-intent.json");
        if (!File.Exists(path) && !Directory.Exists(path) && new FileInfo(path).LinkTarget is null) return;
        try
        {
            if (new FileInfo(path).LinkTarget is not null || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException();
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (input.Length > 4096) throw new IOException();
            using var saved = JsonDocument.Parse(input, new JsonDocumentOptions { MaxDepth = 4 });
            var value = saved.RootElement;
            if (!value.TryGetProperty("version", out var version) || !version.TryGetInt32(out var number) || number != 1 ||
                !value.TryGetProperty("automaticStartSuppressed", out var flag) || flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException();
            suppressed = flag.GetBoolean();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { suppressed = true; } // An unreadable Stop intention cannot authorize an automatic start.
    }

    public void SuppressAutomaticStart() => Write(true);
    public void AllowAutomaticStart() => Write(false);
    private void Write(bool value)
    {
        OwnStatePaths.WritePrivate(path, JsonSerializer.Serialize(new Intent(1, value, DateTimeOffset.UtcNow), Json));
        suppressed = value;
    }
    private sealed record Intent(int Version, bool AutomaticStartSuppressed, DateTimeOffset UpdatedAt);
}
