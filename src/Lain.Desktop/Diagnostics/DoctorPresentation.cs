using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lain.Desktop.Diagnostics;

public enum DoctorTarget { Service, CodexCli, CodexVscode }
public sealed record DoctorDisplayFinding(string Code, string State, string Explanation, string Source, string NextStep);
public sealed record DoctorDisplayReport(int SchemaVersion, DateTimeOffset ObservedAt, string? Target, string Scope,
    IReadOnlyDictionary<string, string> Versions, IReadOnlyList<DoctorDisplayFinding> Findings, bool Truncated);
public sealed record DoctorPresentationResult(DoctorDisplayReport? Report, string? ErrorCode = null);

/// <summary>Presentation-only allowlist for existing DoctorReport JSON; no configuration or model validation.</summary>
public static class DoctorPresentation
{
    public const int MaximumOutputBytes = 256 * 1024;
    public const int MaximumFindings = 64;
    public const int MaximumSupportCharacters = 128 * 1024;
    private static readonly TimeSpan RegexBudget = TimeSpan.FromMilliseconds(50);
    private static readonly Regex UnsafeText = new(
        @"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?(?:-----END [A-Z ]*PRIVATE KEY-----|$)|\b(?:Bearer|Basic)\s+[^\s,;]+|\beyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+|\b(?:sk-|gh[pousr]_|github_pat_|xox[baprs]-|AKIA|AIza)[A-Za-z0-9_-]{6,}|\b[A-Z0-9_]*(?:API_KEY|TOKEN|PASSWORD|SECRET|CREDENTIAL)[A-Z0-9_]*\s*[:=]\s*[^\s,;]+|https?://[^\s]+|(?<![\w])(?:[A-Z]:[\\/]|\\\\|~/|/)[^\s""'<>,;]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexBudget);
    private static readonly Regex Code = new(@"\A[a-zA-Z0-9][a-zA-Z0-9_.-]{0,79}\z", RegexOptions.CultureInvariant, RegexBudget);
    private static readonly Regex Version = new(@"\A[0-9]+\.[0-9]+\.[0-9]+(?:[-+][a-zA-Z0-9.-]{1,64})?\z", RegexOptions.CultureInvariant, RegexBudget);

    public static DoctorPresentationResult Parse(string json)
    {
        if (json.Length > MaximumOutputBytes || Encoding.UTF8.GetByteCount(json) > MaximumOutputBytes) return new(null, "doctor_output_limit");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out var schema) ||
                !schema.TryGetInt32(out var value) || value != 1 ||
                !root.TryGetProperty("observedAt", out var observed) || !observed.TryGetDateTimeOffset(out var when) ||
                !root.TryGetProperty("findings", out var findings) || findings.ValueKind != JsonValueKind.Array)
                return new(null, "doctor_invalid_report");
            var rows = new List<DoctorDisplayFinding>();
            foreach (var row in findings.EnumerateArray().Take(MaximumFindings))
            {
                if (row.ValueKind != JsonValueKind.Object) return new(null, "doctor_invalid_report");
                var state = Read(row, "state", 24);
                rows.Add(new(SafeCode(Read(row, "code", 80)), state is "ok" or "warning" or "blocked" or "unknown" ? state : "unknown",
                    SafeText(Read(row, "explanation", 512)), SafeText(Read(row, "source", 160)),
                    SafeText(Read(row, "nextStep", 512))));
            }
            var versions = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (root.TryGetProperty("versions", out var versionsJson) && versionsJson.ValueKind == JsonValueKind.Object)
                foreach (var property in versionsJson.EnumerateObject().Take(8))
                    if (property.Name is "host" or "cli" or "desktop" or "codex" or "codex-cli" or "codex-vscode" or "upstream" or "patch" or "clientManifest" or "managedClient" or "selectedExtensionMetadata")
                    {
                        var version = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                        if (version is { Length: <= 80 } && Version.IsMatch(version)) versions[property.Name] = version;
                    }
            var target = Read(root, "target", 32);
            var scope = Read(root, "scope", 32);
            return new(new(1, when, target is "codex-cli" or "codex-vscode" ? target : null,
                scope is "service-or-root" or "project" ? scope : "unknown", versions, rows,
                findings.GetArrayLength() > MaximumFindings || (root.TryGetProperty("truncated", out var truncated) && truncated.ValueKind == JsonValueKind.True)));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or RegexMatchTimeoutException)
        { return new(null, "doctor_invalid_report"); }
    }

    public static string SupportText(DoctorDisplayReport report)
    {
        // Re-parse through the same allowlist even for an injected fixture/report instance.
        var safe = Parse(JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        if (safe.Report is null) return "Doctor report unavailable: " + safe.ErrorCode;
        var text = JsonSerializer.Serialize(safe.Report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        return text.Length <= MaximumSupportCharacters ? text : "Doctor report unavailable: doctor_output_limit";
    }

    private static string Read(JsonElement element, string name, int maximum) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { } text
            ? text.Length <= maximum ? text : text[..maximum] + "…" : "";
    private static string SafeCode(string text) => Code.IsMatch(text) && SafeText(text) == text ? text : "doctor.unknown_finding";
    private static string SafeText(string text)
    {
        if (text.Any(c => char.IsControl(c) && c is not '\n' and not '\t')) return "[unsafe text omitted]";
        return UnsafeText.Replace(text, "[private value omitted]");
    }
}
