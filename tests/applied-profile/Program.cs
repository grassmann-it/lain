using System.Text.Json;
using Lain.Host.Models;

var assertions = 0;
var root = Path.Combine(Path.GetTempPath(), "lain-applied-profile-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var activePath = Path.Combine(root, "active-profile.json");
var runtimePath = Path.Combine(root, "runtime.json");
try
{
    CheckUnknown(AppliedProfileStatus.Read(activePath, runtimePath, 42), "missing metadata is unknown");
    WriteActive("profile-a", 42);
    CheckUnknown(AppliedProfileStatus.Read(activePath, runtimePath, 42), "missing runtime is unknown");
    WriteRuntime(42);
    File.Delete(activePath);
    CheckUnknown(AppliedProfileStatus.Read(activePath, runtimePath, 42), "missing active profile is unknown");

    foreach (var revision in new[] { 0L, 42L, long.MaxValue })
    {
        WriteActive("profile-a", revision);
        WriteRuntime(revision);
        var result = AppliedProfileStatus.Read(activePath, runtimePath, revision);
        Check(result.Id == "profile-a" && result.Revision == revision, "matching valid metadata exposes applied profile and revision");
    }

    WriteActive("profile-a", 42);
    WriteRuntime(42);
    var configurationPath = Path.Combine(root, "configuration.json");
    File.WriteAllText(configurationPath, "{\"revision\":99,\"appliedRevision\":42}");
    using (var configuration = JsonDocument.Parse(File.ReadAllText(configurationPath)))
    {
        var applied = configuration.RootElement.GetProperty("appliedRevision").GetInt64();
        var result = AppliedProfileStatus.Read(activePath, runtimePath, applied);
        Check(result.Id == "profile-a" && result.Revision == 42 &&
              configuration.RootElement.GetProperty("revision").GetInt64() != applied,
            "pending desired revision does not hide the reliably applied profile");
    }
    CheckUnknown(AppliedProfileStatus.Read(activePath, runtimePath, 99), "desired revision cannot substitute for applied revision");
    CheckUnknown(AppliedProfileStatus.Read(activePath, runtimePath, -1), "negative applied revision is unknown");

    foreach (var json in new[]
    {
        "{broken", "null", "[]", "42", "true", "{}",
        "{\"revision\":42}", "{\"revision\":42,\"id\":null}", "{\"revision\":42,\"id\":7}",
        "{\"revision\":42,\"id\":\"\"}", "{\"revision\":42,\"id\":\" \\t\\n\"}",
        "{\"revision\":41,\"id\":\"profile-a\"}", "{\"revision\":-1,\"id\":\"profile-a\"}",
        "{\"revision\":\"42\",\"id\":\"profile-a\"}", "{\"revision\":42.5,\"id\":\"profile-a\"}",
        "{\"revision\":9223372036854775808,\"id\":\"profile-a\"}",
        "{\"revision\":null,\"id\":\"profile-a\"}", "{\"id\":\"profile-a\"}"
    })
    {
        File.WriteAllText(activePath, json);
        CheckUnknown(AppliedProfileStatus.Read(activePath, runtimePath, 42), "invalid active metadata never fabricates an applied profile");
        Check(File.ReadAllText(activePath) == json, "reading invalid metadata does not modify it");
    }

    WriteActive("profile-a", 42);
    foreach (var json in new[]
    {
        "{broken", "null", "[]", "42", "true", "{}",
        "{\"revision\":42,\"profileId\":\"profile-a\"}",
        "{\"revision\":42,\"valid\":false,\"profileId\":\"profile-a\"}",
        "{\"revision\":42,\"valid\":\"true\",\"profileId\":\"profile-a\"}",
        "{\"revision\":42,\"valid\":1,\"profileId\":\"profile-a\"}",
        "{\"revision\":42,\"valid\":null,\"profileId\":\"profile-a\"}",
        "{\"revision\":41,\"valid\":true,\"profileId\":\"profile-a\"}",
        "{\"revision\":-1,\"valid\":true,\"profileId\":\"profile-a\"}",
        "{\"revision\":\"42\",\"valid\":true,\"profileId\":\"profile-a\"}",
        "{\"revision\":42.5,\"valid\":true,\"profileId\":\"profile-a\"}",
        "{\"revision\":9223372036854775808,\"valid\":true,\"profileId\":\"profile-a\"}",
        "{\"revision\":null,\"valid\":true,\"profileId\":\"profile-a\"}",
        "{\"valid\":true,\"profileId\":\"profile-a\"}",
        "{\"revision\":42,\"valid\":true}",
        "{\"revision\":42,\"valid\":true,\"profileId\":null}",
        "{\"revision\":42,\"valid\":true,\"profileId\":42}",
        "{\"revision\":42,\"valid\":true,\"profileId\":\"\"}",
        "{\"revision\":42,\"valid\":true,\"profileId\":\"profile-b\"}",
        "{\"revision\":42,\"valid\":true,\"profileId\":\"PROFILE-A\"}"
    })
    {
        File.WriteAllText(runtimePath, json);
        CheckUnknown(AppliedProfileStatus.Read(activePath, runtimePath, 42), "invalid runtime metadata never fabricates an applied profile");
        Check(File.ReadAllText(runtimePath) == json, "reading invalid runtime does not modify it");
    }

    WriteRuntime(42, new string('p', 200));
    WriteActive(new string('p', 200), 42);
    Check(AppliedProfileStatus.Read(activePath, runtimePath, 42).Id?.Length == 200, "bounded profile ID is accepted at its documented maximum");
    WriteActive(new string('p', 201), 42);
    CheckUnknown(AppliedProfileStatus.Read(activePath, runtimePath, 42), "oversized profile ID is unknown");

    WriteActive("profile-a", 42);
    WriteRuntime(42);
    var validActive = File.ReadAllText(activePath);
    var validRuntime = File.ReadAllText(runtimePath);
    File.WriteAllText(activePath, validActive.PadRight(16 * 1024));
    Check(AppliedProfileStatus.Read(activePath, runtimePath, 42).Id == "profile-a", "metadata at file size boundary remains readable");
    File.WriteAllText(activePath, validActive.PadRight(16 * 1024 + 1));
    CheckUnknown(AppliedProfileStatus.Read(activePath, runtimePath, 42), "oversized active metadata is unknown");
    File.WriteAllText(activePath, validActive);
    File.WriteAllText(runtimePath, validRuntime.PadRight(16 * 1024 + 1));
    CheckUnknown(AppliedProfileStatus.Read(activePath, runtimePath, 42), "oversized runtime metadata is unknown");

    // Invalid documents must not leak file handles or poison a subsequent repair.
    WriteActive("profile-b", 43);
    WriteRuntime(43, "profile-b");
    var repaired = AppliedProfileStatus.Read(activePath, runtimePath, 43);
    Check(repaired.Id == "profile-b" && repaired.Revision == 43, "repaired metadata can be read after all invalid cases");

    // Revision equality alone cannot identify a profile switched at the same revision.
    WriteRuntime(43, "profile-c");
    CheckUnknown(AppliedProfileStatus.Read(activePath, runtimePath, 43), "new runtime with previous active profile cannot claim the previous profile");
    WriteRuntime(43, "profile-b");
    WriteActive("profile-c", 43);
    CheckUnknown(AppliedProfileStatus.Read(activePath, runtimePath, 43), "new active profile with previous runtime cannot claim the next profile");
    File.WriteAllText(runtimePath, "{\"valid\":false,\"revision\":43,\"profileId\":\"profile-c\"}");
    CheckUnknown(AppliedProfileStatus.Read(activePath, runtimePath, 43), "incomplete runtime with matching profile is still unknown");
    WriteRuntime(43, "profile-c");
    Check(AppliedProfileStatus.Read(activePath, runtimePath, 43) == new AppliedProfileStatus("profile-c", 43),
        "completed same-revision switch proves the next profile");
    File.Delete(activePath);
    Directory.CreateDirectory(activePath);
    CheckUnknown(AppliedProfileStatus.Read(activePath, runtimePath, 43), "directory in place of metadata is unknown");
    Directory.Delete(activePath);
    Console.WriteLine($"Applied profile metadata: {assertions} checks passed.");
}
finally { Directory.Delete(root, recursive: true); }

void WriteActive(string id, long revision) => File.WriteAllText(activePath, JsonSerializer.Serialize(new { id, revision }));
void WriteRuntime(long revision, string profileId = "profile-a") =>
    File.WriteAllText(runtimePath, JsonSerializer.Serialize(new { valid = true, revision, profileId }));
void CheckUnknown(AppliedProfileStatus result, string description) => Check(result.Id is null && result.Revision is null, description);
void Check(bool condition, string description)
{
    assertions++;
    if (!condition) throw new InvalidOperationException("FAILED: " + description);
}
