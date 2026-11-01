using System.Diagnostics;
using System.Text.Json;
using Lain.Desktop.Diagnostics;

var assertions = 0;
string Json(object[]? findings = null) => JsonSerializer.Serialize(new
{
    schemaVersion = 1, observedAt = "2026-10-30T15:00:00Z", target = "codex-cli", scope = "service-or-root",
    versions = new { host = "0.8.0", managedClient = "0.164.0", selectedExtensionMetadata = "0.164.0", unknown = "SYNTHETIC_RAW_CANARY" },
    findings = findings ?? [new { code = "host.reachable", state = "ok", explanation = "Host status was read.", source = "public status API", nextStep = "Check lain service status if needed." }],
    headers = new { authorization = "SYNTHETIC_RAW_CANARY" }, rawPayload = "SYNTHETIC_RAW_CANARY"
});
var result = DoctorPresentation.Parse(Json());
Check(result.Report is { SchemaVersion: 1 }, "existing doctor schema accepted");
Check(result.Report!.Findings[0].Code == "host.reachable" && result.Report.Findings[0].State == "ok", "CLI finding semantics preserved");
Check(result.Report.Versions.ContainsKey("managedClient") && !result.Report.Versions.ContainsKey("unknown"), "known version fields only");
Check(!DoctorPresentation.SupportText(result.Report).Contains("SYNTHETIC_RAW_CANARY"), "unknown payload/header fields excluded");
var unsafeRows = new object[] { new { code = "client.binding", state = "warning", explanation = "file /home/synthetic/private/config.toml and C:\\Users\\synthetic\\secret.json Bearer syntheticbearercanary sk-syntheticprovidercanary123", source = "https://alice:syntheticurlcanary@example.test/", nextStep = "SERVICE_TOKEN=syntheticassignmentcanary ~/private/file.json" } };
var filtered = DoctorPresentation.Parse(Json(unsafeRows)).Report!;
var safe = DoctorPresentation.SupportText(filtered);
foreach (var canary in new[] { "/home/", "C:\\Users", "syntheticbearercanary", "syntheticprovidercanary123", "syntheticurlcanary", "syntheticassignmentcanary", "~/private" })
    Check(!safe.Contains(canary, StringComparison.Ordinal), "support excludes private text");
Check(filtered.Findings[0].State == "warning", "redaction does not invent status");
Check(DoctorPresentation.Parse("not JSON").ErrorCode == "doctor_invalid_report", "invalid JSON safe error");
Check(DoctorPresentation.Parse(Json().Replace("\"schemaVersion\":1", "\"schemaVersion\":2")).ErrorCode == "doctor_invalid_report", "unknown schema rejected");
Check(DoctorPresentation.Parse(new string('x', DoctorPresentation.MaximumOutputBytes + 1)).ErrorCode == "doctor_output_limit", "bounded output parser");
var many = Enumerable.Range(0, 100).Select(i => (object)new { code = "fixture.code", state = "unknown", explanation = new string('x', 2000), source = "fixture", nextStep = "inspect" }).ToArray();
var bounded = DoctorPresentation.Parse(Json(many)).Report!;
Check(bounded.Findings.Count == DoctorPresentation.MaximumFindings && bounded.Truncated, "bounded findings and explicit truncation");
Check(bounded.Findings[0].Explanation.Length <= 513, "bounded field layout");
Check(DoctorPresentation.SupportText(bounded).Contains("\"truncated\": true"), "support report preserves truncation");
Check(DoctorCliSource.Arguments(DoctorTarget.Service).SequenceEqual(["doctor", "--json"]), "fixed service arguments");
Check(DoctorCliSource.Arguments(DoctorTarget.CodexCli).SequenceEqual(["doctor", "--target", "codex-cli", "--json"]), "fixed CLI target arguments");
Check(DoctorCliSource.Arguments(DoctorTarget.CodexVscode).SequenceEqual(["doctor", "--target", "codex-vscode", "--json"]), "fixed VS Code target arguments");
try { _ = new DoctorCliSource("lain;echo secret"); throw new Exception("relative executable accepted"); }
catch (ArgumentException) { assertions++; }
Check((await new DoctorCliSource(Path.Combine(Path.GetTempPath(), "nonexistent-lain-" + Guid.NewGuid().ToString("N"))).ReadAsync(DoctorTarget.Service)).ErrorCode == "doctor_cli_unavailable", "missing trusted executable safe failure");
if (!OperatingSystem.IsWindows())
{
    var directory = Path.Combine(Path.GetTempPath(), "lain-doctor-ui-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        string Script(string name, string content)
        {
            var path = Path.Combine(directory, name);
            File.WriteAllText(path, "#!/bin/sh\n" + content);
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return path;
        }
        var success = Script("success", "[ \"$1\" = doctor ] && [ \"$2\" = --target ] && [ \"$3\" = codex-cli ] && [ \"$4\" = --json ] || exit 2\ncat <<'DOCTORJSON'\n" + Json() + "\nDOCTORJSON\n");
        var read = await new DoctorCliSource(success).ReadAsync(DoctorTarget.CodexCli);
        Check(read.ErrorCode is null && DoctorPresentation.Parse(read.Json!).Report is not null, "process bridge uses exact arguments");
        var failed = Script("failed", "echo 'SYNTHETIC_STDERR_CANARY' >&2\nexit 3\n");
        var fail = await new DoctorCliSource(failed).ReadAsync(DoctorTarget.Service);
        Check(fail.Json is null && fail.ErrorCode == "doctor_cli_failed", "stderr never returned");
        var unlimited = Script("over-limit", "head -c 524288 /dev/zero\n");
        var clock = Stopwatch.StartNew();
        Check((await new DoctorCliSource(unlimited).ReadAsync(DoctorTarget.Service)).ErrorCode == "doctor_output_limit", "stdout bound stops process");
        Check(clock.Elapsed < TimeSpan.FromSeconds(5), "output limit fails promptly");
        var waiting = Script("cancel", "sleep 60\n");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        clock.Restart();
        Check((await new DoctorCliSource(waiting).ReadAsync(DoctorTarget.Service, cancellation.Token)).ErrorCode == "doctor_cancelled", "caller cancellation stops process tree");
        Check(clock.Elapsed < TimeSpan.FromSeconds(5), "cancellation bounded");
    }
    finally { Directory.Delete(directory, true); }
}
Console.WriteLine($"Desktop diagnostics bridge/presentation: {assertions} assertions passed; synthetic only.");
void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
