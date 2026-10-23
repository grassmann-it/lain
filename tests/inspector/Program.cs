using System.Text.Json;
using Lain.Core;
using Lain.Host.Inspector;

var checks = new List<string>();
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); }
CoreInspectorEvent Event(long sequence, string kind = "attempt_started", string request = "request-a", string attempt = "attempt-a", string? text = null) =>
    new(sequence, DateTimeOffset.Parse("2026-10-24T00:00:00Z"), kind, "attempt", request, attempt, "account-a", "route-a", "provider-a", "alias-a", "model-a", Text: text);
InspectorStore Start(InspectorLimitsDto? limits = null, TimeProvider? clock = null)
{
    var store = new InspectorStore(limits, clock); store.BeginSource("core-one"); store.MarkConnected(); return store;
}
string Content(InspectorStore store) => string.Concat(store.Events().Events.Select(value => value.Text));

var store = Start();
store.Ingest(Event(1) with { Dimensions = new(ConversationId: "chat-1", TurnId: "turn-1", WorkerId: "worker-2", ParentWorkerId: "worker-1",
    ReasoningEffort: "high", EffectiveReasoningEffort: "medium", Evidence:
    [new("conversationId", "harness_header", "x-conversation-id"), new("turnId", "request_body", "metadata.turn_id"),
     new("workerId", "harness_header", "x-worker-id"), new("parentWorkerId", "harness_header", "x-parent-worker-id"),
     new("reasoningEffort", "request_body", "reasoning.effort"), new("effectiveReasoningEffort", "core", "usage.reasoningEffort")]) });
store.Ingest(Event(2, "text_delta", text: "Hello world.\n"));
store.Ingest(Event(3, "attempt_finished") with { Outcome = "success", OutputTokens = 0, TotalTokens = 12,
    Measurements = new(10, 50, Source: "core_monotonic_v1") });
var first = store.Snapshot();
var stream = first.Streams.Single();
Check(stream.Dimensions is { ConversationId: "chat-1", TurnId: "turn-1", WorkerId: "worker-2", ParentWorkerId: "worker-1" }, "explicit grouping and parent evidence retained");
Check(stream.Dimensions is { ReasoningEffort: "high", EffectiveReasoningEffort: "medium" }, "requested and effective reasoning stay separate");
Check(stream.Tokens is { Output: 0, Total: 12, Input: null, Cached: null, Reasoning: null }, "wire token zero and missing components remain distinct");
Check(stream.State == "success" && stream.Measurements?.TotalDurationMs == 50 && Content(store).Contains("Hello world."), "terminal state, measured duration and bounded text flush retained");
store.Ingest(Event(3, "attempt_finished") with { Text = "duplicate poison", Outcome = "failed" });
Check(store.Snapshot().Cursor == first.Cursor && !Content(store).Contains("poison"), "duplicate upstream event is inert");
store.Ingest(Event(4, request: "request-a", attempt: "attempt-b"));
store.Ingest(Event(5, request: "unrelated", attempt: "attempt-c"));
Check(store.Snapshot().Streams.Count(value => value.RequestId == "request-a") == 2, "attempts share only their explicit request identity");
Check(store.Snapshot().Streams.Single(value => value.RequestId == "unrelated").Dimensions is null, "same model does not invent a conversation or worker");
var filtered = store.Events(new(first.SourceId, 0, 100, "not-present"));
Check(filtered.Events.Length == 0 && filtered.NextCursor == filtered.HighWatermark && filtered.NextCursor > 0, "empty request-filtered page advances the global cursor");
var paged = store.Events(new(first.SourceId, 0, 1, "request-a"));
Check(paged.Events.Length == 1 && paged.NextCursor >= paged.Events[0].Cursor && paged.NextCursor < paged.HighWatermark, "bounded request page resumes without skipping a matching event");

var unknown = Start();
unknown.Ingest(Event(1) with { Dimensions = new(ConversationId: "unproven", WorkerId: "untrusted", Evidence: [new("workerId", "invented_source")]) });
Check(unknown.Snapshot().Streams.Single().Dimensions is null, "missing or unrecognized provenance never creates grouping");
unknown.Ingest(Event(2, "metadata") with { Dimensions = new(WorkerId: "worker-one", Evidence: [new("workerId", "core", "worker")]) });
unknown.Ingest(Event(3, "metadata") with { Dimensions = new(WorkerId: "worker-two", Evidence: [new("workerId", "core", "worker")]) });
unknown.Ingest(Event(4, "metadata") with { Dimensions = new(WorkerId: "worker-two", Evidence: [new("workerId", "core", "worker")]) });
Check(unknown.Snapshot().Streams.Single().Dimensions?.WorkerId is null && unknown.Snapshot().Streams.Single().Truncated, "conflicting identity remains unknown instead of moving groups");
unknown.MarkUnavailable();
Check(unknown.Snapshot().State == "unavailable" && unknown.Snapshot().Streams.Single().State == "unknown" && unknown.Snapshot().Streams.Single().FinishedAt is null,
    "source loss does not invent cancellation or completion");

var privacy = Start();
privacy.Ingest(Event(1));
privacy.Ingest(Event(2, "text_delta", text: "before Authorization: Bea"));
privacy.Ingest(Event(3, "text_delta", text: "rer super-secret-value\nafter\n"));
privacy.Ingest(Event(4, "tool_delta", text: "{\"api_key\":\"never-show-tool-args\"}") with { ToolName = "search", ToolCallId = "call-one" });
privacy.Ingest(Event(5, "attempt_finished") with { Outcome = "canceled" });
var visible = JsonSerializer.Serialize(privacy.Events());
Check(!visible.Contains("super-secret-value") && !visible.Contains("Authorization") && !visible.Contains("never-show-tool-args"), "split authorization and all tool arguments are absent from output");
Check(Content(privacy).Contains("[redacted]") && Content(privacy).Contains("after"), "redaction resumes safe output after the secret line");
Check(privacy.Events().Events.Single(value => value.Kind == "tool_delta") is { Text: null, ToolName: "search", ToolCallId: "call-one", RedactionState: "removed" }, "tool identity survives without arguments/results");
var secret = new InspectorTextRedactor();
secret.Push(new string('x', 9000), DateTimeOffset.UtcNow);
secret.Push("continuing-sensitive-token\nallowed", DateTimeOffset.UtcNow);
var secretTail = secret.Push(null, DateTimeOffset.UtcNow, final: true).Text;
Check(secretTail is not null && !secretTail.Contains("continuing-sensitive-token") && secretTail.Contains("allowed"), "oversize secret continuation is discarded through newline");
var bare = new InspectorTextRedactor();
bare.Push("sk-ABCD", DateTimeOffset.UtcNow);
bare.Push("EFGHijkl rest", DateTimeOffset.UtcNow);
Check(bare.Push(null, DateTimeOffset.UtcNow, final: true).Text == "[redacted] rest", "split credential-shaped token cannot emit its suffix");

var capacity = Start(new(MaxStreams: 1, MaxEvents: 2));
capacity.Ingest(Event(1)); capacity.Ingest(Event(2, attempt: "other"));
Check(capacity.Snapshot(new(Limit: 1)).Streams.Length == 1 && capacity.Snapshot(new(Limit: 1)).DroppedEvents > 0, "active registry stays bounded and records overflow");
capacity.Ingest(Event(3, "metadata")); capacity.Ingest(Event(4, "metadata")); capacity.Ingest(Event(5, "metadata"));
var tail = capacity.Events();
Check(tail.Events.Length <= 2 && tail.Truncated && tail.EarliestCursor > 1, "event cardinality bound produces an explicit replay gap");
var bytes = Start(new(ContentBytes: 3000, PerStreamBytes: 2500, MaxEventBytes: 2000));
bytes.Ingest(Event(1));
for (var i = 2; i < 12; i++) bytes.Ingest(Event(i, "text_delta", text: new string('a', 1000) + "\n"));
Check(bytes.Events().Truncated && bytes.Events().Events.Length < 10, "serialized-byte budget also bounds a short event list");

var clock = new TestClock();
var expiry = Start(new(ContentTtlSeconds: 2, TerminalTtlSeconds: 1), clock);
expiry.Ingest(Event(1)); expiry.Ingest(Event(2, "text_delta", text: "pending-tail"));
clock.JumpWall(TimeSpan.FromDays(-30)); clock.Advance(TimeSpan.FromSeconds(3));
Check(expiry.Events().Events.Length == 0 && expiry.Events().Truncated, "retention uses monotonic elapsed time across wall-clock rollback");
expiry.Ingest(Event(3, "attempt_finished") with { Outcome = "success" });
Check(!Content(expiry).Contains("pending-tail"), "pending redaction buffer shares content lifetime");
clock.Advance(TimeSpan.FromSeconds(2));
Check(expiry.Snapshot().Streams.Length == 0, "terminal metadata and content expire together");

var restart = Start(); restart.Ingest(Event(1));
var old = restart.Snapshot(); restart.BeginSource("core-two"); restart.MarkConnected();
var reset = restart.Events(new(old.SourceId, old.Cursor));
Check(reset.SourceId != old.SourceId && reset.Truncated && reset.GapReason == "source_changed", "core restart produces a new Host source generation");
Check(restart.Snapshot().Streams.Single().State == "unknown" && restart.Snapshot().Streams.Single().LastCursor == 0, "old active metadata has unknown outcome and no new-generation cursor");
restart.Ingest(Event(1, request: "new-request", attempt: "new-attempt"));
Check(restart.Snapshot().Streams.Any(value => value.RequestId == "new-request"), "new core sequence starts independently");
var rejected = 0;
foreach (var query in new[] { new InspectorEventQuery(After: -1), new InspectorEventQuery(After: 1), new InspectorEventQuery(Limit: 501), new InspectorEventQuery(restart.Snapshot().SourceId, long.MaxValue) })
    try { restart.Events(query); } catch (ArgumentException) { rejected++; }
Check(rejected == 4, "invalid cursors, missing generations and oversized pages are rejected");
Console.WriteLine($"PASS {checks.Count} focused inspector assertions");
foreach (var check in checks) Console.WriteLine("  " + check);

internal sealed class TestClock : TimeProvider
{
    private long ticks;
    private TimeSpan wall;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => ticks;
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-10-24T00:00:00Z") + TimeSpan.FromTicks(ticks) + wall;
    public void Advance(TimeSpan duration) => ticks += duration.Ticks;
    public void JumpWall(TimeSpan duration) => wall += duration;
}
