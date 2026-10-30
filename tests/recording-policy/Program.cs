using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Recording.Policy;

var assertions = 0;
var policy = new RecordingPolicyService();
Check(!policy.Snapshot.Enabled && policy.Snapshot.Revision == 0, "new policy default-off");
Check(policy.Sanitize(Source("{}"), policy.Snapshot) is null, "off rejects raw copy");
Check(policy.Update(new(1, true, "content")).ErrorCode == "stale_revision", "stale update rejected");
Check(policy.Update(new(0, true, "content", "claimed_client")).ErrorCode == "unsupported_scope", "no guessed client isolation");
Check(policy.Update(new(0, true, "training_only")).ErrorCode == "unsupported_mode", "unsupported mode rejected");
Check(policy.Update(new(0, true, "content", Limits: new(MaxRecordBytes: 10))).ErrorCode == "invalid_limits", "bounded limits");
Check(policy.Update(new(0, true, "content", RouteIds: ["r", "r"])).ErrorCode == "duplicate_route_filter", "duplicate filters");
var enabled = policy.Update(new(0, true, "content"));
Check(enabled.Applied && enabled.Policy.Enabled && enabled.Policy.RouteIds is null, "explicit owned-gateway opt-in covers all models");
var snapshot = policy.Snapshot;
SourceObservation Bound(string text) => Source(text) with { PolicyRevision = snapshot.Revision, StopGeneration = snapshot.StopGeneration };
var original = Bound("""{"model":"moving-alias","messages":[{"role":"user","content":"ordinary code: var answer = 42;"}]}""");
var bytes = original.Body!.ToArray();
var retained = policy.Sanitize(original, snapshot)!;
Check(retained.Body!.Contains("ordinary code") && retained.Model == "unknown-rights-model", "unknown training rights do not block capture");
Check(retained.RedactionState == "sanitized" && retained.BodyEncoding == "utf8_json", "reserialization tagged honestly");
Check(bytes.SequenceEqual(original.Body!), "forwarded raw bytes untouched");
Check(retained.Tokens is null && retained.Dimensions is null, "unknown usage and context stay unknown");
var secrets = new Dictionary<string, object?> {
    ["messages"] = new[] { new { role = "user", content = "keep ordinary text" } },
    ["authorization"] = "Bearer synthetic-auth-canary", ["cookie"] = "synthetic-cookie-canary",
    ["apiKey"] = "synthetic-api-canary", ["accessToken"] = "synthetic-access-canary",
    ["password"] = "synthetic-password-canary", ["private_key"] = "synthetic-private-canary",
    ["headers"] = new { arbitrary = "synthetic-header-canary" }, ["env"] = new { X = "synthetic-env-canary" },
    ["tool"] = new { arguments = JsonSerializer.Serialize(new { password = "synthetic-args-canary", code = "echo safe" }) },
    ["text"] = "Bearer syntheticbearercanary sk-syntheticcanary123 ghp_syntheticcanary123 https://alice:syntheticurlcanary@example.test/path?token=syntheticquerycanary",
    ["pem"] = "-----BEGIN PRIVATE KEY-----\nsyntheticpemcanary\n-----END PRIVATE KEY-----",
    ["jwt"] = "eyJhbGciOiJub25lIn0.eyJzdWIiOiJjYW5hcnkifQ.c3ludGhldGlj",
    ["assignment"] = "SERVICE_API_KEY=syntheticassignmentcanary",
    ["secret_file"] = "contents of /home/test/.env: syntheticfilecanary"
};
retained = policy.Sanitize(Bound(JsonSerializer.Serialize(secrets)), snapshot)!;
foreach (var canary in new[] { "synthetic-auth-canary", "synthetic-cookie-canary", "synthetic-api-canary", "synthetic-access-canary", "synthetic-password-canary", "synthetic-private-canary", "synthetic-header-canary", "synthetic-env-canary", "synthetic-args-canary", "syntheticbearercanary", "syntheticcanary123", "syntheticurlcanary", "syntheticquerycanary", "syntheticpemcanary", "c3ludGhldGlj", "syntheticassignmentcanary", "syntheticfilecanary" })
    Check(!JsonSerializer.Serialize(retained).Contains(canary, StringComparison.Ordinal), "canary excluded: " + canary);
Check(retained.Body!.Contains("keep ordinary text") && retained.Body.Contains("echo safe"), "ordinary messages and tool code preserved");
var badArgs = policy.Sanitize(Bound("""{"arguments":"{\"token\":\"unfinished"}"""), snapshot)!;
Check(!badArgs.Body!.Contains("unfinished"), "malformed arguments omitted");
Check(policy.Sanitize(Bound("{bad"), snapshot)!.RedactionState == "omitted_invalid", "malformed document omitted");
Check(policy.Sanitize(Bound("{}") with { Body = [0xff, 0xfe] }, snapshot)!.Body is null, "invalid UTF8 omitted");
Check(policy.Sanitize(Bound("data: {\"text\":\"sk-\"}\n\ndata: {\"text\":\"canary\"}\n\n") with { BodyEncoding = "sse", Kind = "response_chunk" }, snapshot)!.RedactionState == "omitted_fragment", "split SSE secrets never retained as raw chunks");
Check(policy.Sanitize(Bound("{}") with { Complete = false }, snapshot)!.Body is null, "partial body omitted");
Check(policy.Sanitize(Bound("{}") with { BodyEncoding = "bytes" }, snapshot)!.GapReason == "unsupported_encoding", "unknown representation visible");
Check(policy.Sanitize(Bound("{}") with { EventId = "Bearer unsafe" }, snapshot) is null, "unsafe correlation ID rejected rather than changed");
var tiny = new RecordingPolicyService();
var tinySnapshot = tiny.Update(new(0, true, "content", Limits: new(MaxRecordBytes: 1024))).Policy;
var oversize = Source(JsonSerializer.Serialize(new { text = new string('x', 1500) })) with { PolicyRevision = tinySnapshot.Revision, StopGeneration = tinySnapshot.StopGeneration };
Check(tiny.Sanitize(oversize, tinySnapshot)!.GapReason == "body_oversize", "oversize before parse");
var metadata = policy.Update(new(snapshot.Revision, true)).Policy;
Check(policy.Sanitize(Source("{secret}") with { PolicyRevision = metadata.Revision, StopGeneration = metadata.StopGeneration }, metadata)!.Body is null, "metadata mode never parses body");
Check(policy.Sanitize(original, snapshot) is null, "old queued revision invalidated");
var filtered = policy.Update(new(metadata.Revision, true, "content", RouteIds: ["allowed"])).Policy;
Check(policy.Sanitize(Source("{}") with { PolicyRevision = filtered.Revision, StopGeneration = filtered.StopGeneration, RouteId = "denied" }, filtered) is null, "actual selected route filter");
var external = policy.Snapshot;
external.RouteIds![0] = "mutated";
Check(policy.Snapshot.RouteIds![0] == "allowed", "caller cannot mutate snapshot filters");
var stopped = policy.Update(new(filtered.Revision, false, "content")).Policy;
Check(!policy.IsCurrent(filtered.Revision, filtered.StopGeneration) && !stopped.Enabled, "stop invalidates admission atomically");

var directory = Path.Combine(Path.GetTempPath(), "lain-recording-policy-" + Guid.NewGuid().ToString("N"));
try
{
    var persistent = new RecordingPolicyService(directory);
    Check(!File.Exists(Path.Combine(directory, "policy.json")), "default-off creates no capture/control file");
    Check(persistent.Update(new(0, true, "content")).Applied, "explicit opt-in persisted");
    var reopened = new RecordingPolicyService(directory);
    Check(reopened.Snapshot.Enabled && reopened.Snapshot.Revision == 1, "explicit opt-in survives restart");
    Check(Directory.GetFiles(directory).Length == 1, "atomic save leaves no temporary file");
    if (!OperatingSystem.IsWindows())
    {
        Check((File.GetUnixFileMode(Path.Combine(directory, "policy.json")) & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) == 0, "private file mode");
        var target = Path.Combine(directory, "target");
        File.WriteAllText(target, "preserve");
        File.Delete(Path.Combine(directory, "policy.json"));
        File.CreateSymbolicLink(Path.Combine(directory, "policy.json"), target);
        Check(!persistent.Update(new(1, false, "content")).Applied && !persistent.Snapshot.Enabled, "stop remains off when symlink blocks save");
        Check(File.ReadAllText(target) == "preserve", "symlink target untouched");
    }
}
finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
Console.WriteLine($"Recording policy: {assertions} assertions passed (synthetic; no capture enabled in real installation).");

SourceObservation Source(string json) => new("synthetic-epoch", 1, "synthetic-event-1", "capture-1", "request-1",
    DateTimeOffset.Parse("2026-10-30T12:00:00Z"), "client_request", "openai-chat", "request_body",
    Model: "unknown-rights-model", BodyEncoding: "utf8_json", Body: Encoding.UTF8.GetBytes(json));
void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
