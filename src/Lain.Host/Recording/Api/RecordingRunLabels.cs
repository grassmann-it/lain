using System.Text.Json;
using Lain.Core;
using Lain.Host.Recording.Policy;

namespace Lain.Host.Recording.Api;

/// <summary>Explicit user labels only. No filesystem discovery or inferred loaded-skill evidence.</summary>
public sealed class RecordingRunLabels(string directory)
{
    private readonly object gate = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record Entry(string ConversationId, RecordingRunManifest Manifest);
    public RecordingRunManifest[] List(string conversationId)
    { lock (gate) return Read().Where(e => e.ConversationId == conversationId).Select(e => e.Manifest).ToArray(); }
    public RecordingRunManifest[] Set(string conversationId, RecordingRunManifest manifest)
    {
        RecordingArguments.Identity(manifest.Id);
        if (manifest.Evidence != "configured") throw new ArgumentException("User labels require configured evidence.");
        foreach (var value in new[] { manifest.Variant, manifest.ConfiguredRevision })
            if (value is not null && (value.Length > 256 || value.Any(char.IsControl) || RecordingSanitizer.SanitizeText(value) != value)) throw new ArgumentException("Invalid run label.");
        foreach (var hash in new[] { manifest.PromptHash, manifest.SkillHash, manifest.ObservedInstructionHash })
            if (hash is not null && (hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigit(c)))) throw new ArgumentException("Hashes require SHA256 hex.");
        // A user-provided hash is configuration, never a claim of observed instruction content.
        if (manifest.ObservedInstructionHash is not null) throw new ArgumentException("Observed hashes require captured evidence.");
        lock (gate)
        {
            var entries = Read().Where(e => e.ConversationId != conversationId || e.Manifest.Id != manifest.Id).ToList();
            if (entries.Count >= 512) throw new ArgumentException("Run label limit reached.");
            entries.Add(new(conversationId, manifest)); Save(entries.ToArray());
            return entries.Where(e => e.ConversationId == conversationId).Select(e => e.Manifest).ToArray();
        }
    }
    public void Clear() { lock (gate) { RejectLinks(); var path = Path.Combine(directory, "run-labels.json"); if (File.Exists(path)) File.Delete(path); } }
    private Entry[] Read()
    {
        RejectLinks(); var path = Path.Combine(directory, "run-labels.json");
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > 1024 * 1024) throw new IOException("recording_labels_invalid");
        var values = JsonSerializer.Deserialize<Entry[]>(File.ReadAllBytes(path), Json) ?? [];
        if (values.Length > 512) throw new IOException("recording_labels_invalid");
        return values;
    }
    private void Save(Entry[] entries)
    {
        RejectLinks(); Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var path = Path.Combine(directory, "run-labels.json"); var temp = Path.Combine(directory, ".labels-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Options = FileOptions.WriteThrough };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temp, options)) { JsonSerializer.Serialize(stream, entries, Json); stream.Flush(true); }
            RejectLinks(); File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private void RejectLinks()
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(directory)); current is not null; current = current.Parent)
            RejectLink(current.FullName);
        RejectLink(Path.Combine(directory, "run-labels.json"));
    }
    private static void RejectLink(string path)
    {
        try { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("recording_path_link"); }
        catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
    }
}
