using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Inspector;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

internal static class Program
{
    private static int assertions;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-24T12:00:00Z");

    public static async Task Main()
    {
        await WireCorrelationAndFiltering();
        await EpochGapAndFailureBoundaries();
        await BoundedCatchupAndInvalidPages();
        await RedactionAndRetention();
        RegistrationRejectsNonOwnedSources();
        Console.WriteLine($"Independent Host Inspector boundary: {assertions} assertions passed (fake HTTP only).");
    }

    private static async Task WireCorrelationAndFiltering()
    {
        var dims = Dimensions("conversation-one", "turn-one", "worker-one", "parent-one");
        var continuation = dims with
        {
            ToolCallId = "call-one", PreviousResponseId = "response-one", ParentRequestId = "request-one",
            Evidence = [.. dims.Evidence!, new("toolCallId", "request_body", "messages[].tool_call_id"),
                new("previousResponseId", "request_body", "previous_response_id"), new("parentRequestId", "request_body", "metadata.parent_request_id")]
        };
        // Raw JSON matches the actual Go wire: token counts are top-level nullable fields.
        var packet = """
        {"instanceId":"core-wire","earliestCursor":1,"nextCursor":8,"highWatermark":8,"droppedCount":0,"events":[
          {"sequence":1,"observedAt":"2026-10-24T12:00:00Z","kind":"attempt_started","scope":"attempt","requestId":"request-one","attemptId":"attempt-one","accountId":"account","routeId":"route","dimensions":DIMS},
          {"sequence":2,"observedAt":"2026-10-24T12:00:00Z","kind":"tool_delta","scope":"attempt","requestId":"request-one","attemptId":"attempt-one","toolName":"fixture_echo","toolCallId":"call-one","text":"{\"apiKey\":\"tool-secret-must-not-survive\"}"},
          {"sequence":3,"observedAt":"2026-10-24T12:00:03Z","kind":"attempt_finished","scope":"attempt","requestId":"request-one","attemptId":"attempt-one","outcome":"success","inputTokens":0,"outputTokens":7,"totalTokens":null,"measurements":{"source":"core_monotonic_v1","firstOutputLatencyMs":25,"totalDurationMs":3000,"firstOutputAt":"2026-10-24T12:00:00.025Z","finishedAt":"2026-10-24T12:00:03Z"}},
          {"sequence":4,"observedAt":"2026-10-24T12:00:04Z","kind":"attempt_started","scope":"attempt","requestId":"request-two","attemptId":"attempt-two","dimensions":CONTINUATION},
          {"sequence":5,"observedAt":"2026-10-24T12:00:05Z","kind":"text_delta","scope":"attempt","requestId":"request-two","attemptId":"attempt-two","text":"safe output\n"},
          {"sequence":6,"observedAt":"2026-10-24T12:00:06Z","kind":"attempt_finished","scope":"attempt","requestId":"request-two","attemptId":"attempt-two","outcome":"success"},
          {"sequence":7,"observedAt":"2026-10-24T12:00:07Z","kind":"attempt_started","scope":"attempt","requestId":"request-unknown","attemptId":"attempt-unknown"},
          {"sequence":8,"observedAt":"2026-10-24T12:00:08Z","kind":"attempt_finished","scope":"attempt","requestId":"request-unknown","attemptId":"attempt-unknown","outcome":"canceled"}
        ]}
        """.Replace("DIMS", JsonSerializer.Serialize(dims, Json)).Replace("CONTINUATION", JsonSerializer.Serialize(continuation, Json));
        var handler = new Scripted(_ => Text(packet));
        var store = new InspectorStore(clock: new Clock());
        using var collector = Collector(handler, store);
        Check(await collector.PollAsync(), "collector catches up the complete owned wire page");
        var snapshot = store.Snapshot();
        Check(snapshot.State == "live" && snapshot.Complete && snapshot.SourceId != "core-wire", "public generation is host-owned and complete only after collection");
        Check(snapshot.Streams.Length == 3 && snapshot.Streams.All(stream => stream.Scope == "attempt"), "tool continuation retains attempt streams without inventing chats");
        var first = snapshot.Streams.Single(stream => stream.RequestId == "request-one");
        var second = snapshot.Streams.Single(stream => stream.RequestId == "request-two");
        Check(first.Dimensions?.ConversationId == "conversation-one" && second.Dimensions is
            { ConversationId: "conversation-one", TurnId: "turn-one", WorkerId: "worker-one", ParentWorkerId: "parent-one" },
            "conversation turn worker and parent survive Core-to-Host mapping");
        Check(second.Dimensions is { ToolCallId: "call-one", PreviousResponseId: "response-one", ParentRequestId: "request-one" },
            "explicit tool and response continuation evidence is retained");
        Check(first.Tokens is { Input: 0, Output: 7, Total: null, Cached: null, Reasoning: null },
            "top-level Go token wire preserves zero known output and unknown absent components");
        Check(first.Measurements is { FirstOutputLatencyMs: 25, TotalDurationMs: 3000, Source: "core_monotonic_v1" },
            "owned measurement provenance and full-duration values survive projection");
        var unknown = snapshot.Streams.Single(stream => stream.RequestId == "request-unknown");
        Check(unknown.State == "canceled" && unknown.Dimensions is null && unknown.Tokens is null, "unknown relationships and missing token usage remain unknown after cancellation");
        var publicEvents = store.Events(new(Limit: 500));
        Check(publicEvents.Events.Any(row => row.ToolName == "fixture_echo" && row.ToolCallId == "call-one" && row.Text is null), "tool identity is displayed while arguments are removed");
        Check(!JsonSerializer.Serialize(publicEvents).Contains("tool-secret-must-not-survive"), "raw tool argument secret cannot cross the public boundary");
        Check(!JsonSerializer.Serialize(snapshot).Contains("safe output"), "stream snapshots do not duplicate transient payload content");

        var seen = new List<InspectorEventDto>();
        var after = 0L;
        while (after < publicEvents.HighWatermark)
        {
            var page = store.Events(new(snapshot.SourceId, after, 1, "request-two"));
            Check(page.Events.All(row => row.RequestId == "request-two") && page.NextCursor > after, "request-filter pagination progresses through global cursor intervals");
            seen.AddRange(page.Events); after = page.NextCursor;
        }
        Check(seen.Select(row => row.Cursor).SequenceEqual(publicEvents.Events.Where(row => row.RequestId == "request-two").Select(row => row.Cursor)),
            "filtered pages neither drop nor duplicate matching events");
        handler.Response = _ => Reply(Batch("core-wire", 8, 8));
        Check(await collector.PollAsync() && store.Events(new(Limit: 500)).Events.Length == publicEvents.Events.Length,
            "repeated empty collection does not replay previously acknowledged display events");
    }

    private static async Task EpochGapAndFailureBoundaries()
    {
        var clock = new Clock();
        var handler = new Scripted(_ => Reply(Batch("core-a", 2, 2,
            Event(1, "attempt_started"), Event(2, "text_delta") with { Text = "old source content\n" + new string('z', 160) + "\n" })));
        var store = new InspectorStore(clock: clock);
        using var collector = Collector(handler, store);
        await collector.PollAsync();
        var before = store.Snapshot();
        Check(store.Events(new(Limit: 500)).Events.Any(row => row.Text?.Contains("old source content", StringComparison.Ordinal) == true),
            "safe prior-epoch content was retained before the reset");
        handler.Response = _ => Reply(new CoreInspectorBatch("core-a", 6, 2, 5, 3, []));
        await collector.PollAsync();
        var gap = store.Events(new(before.SourceId, 0, 500));
        Check(gap.Truncated && gap.DroppedEvents >= 3 && gap.GapReason == "upstream_evicted", "fully evicted upstream interval becomes a visible public gap");
        Check(!store.Snapshot().Complete, "upstream loss cannot be advertised as complete coverage");

        handler.Response = request => After(request) == 0
            ? Reply(Batch("core-b", 2, 2, Event(1, "attempt_started", "new-request", "new-attempt"), Event(2, "attempt_finished", "new-request", "new-attempt") with { Outcome = "success" }))
            : Reply(new CoreInspectorBatch("core-b", 1, 0, 2, 0, []));
        await collector.PollAsync();
        var restarted = store.Snapshot();
        Check(restarted.SourceId != before.SourceId, "new Core epoch rotates the public Host generation");
        Check(restarted.Streams.Single(stream => stream.RequestId == "request").State == "unknown", "unresolved old stream is not fabricated as success or cancellation after restart");
        var reset = store.Events(new(before.SourceId, before.Cursor, 500));
        Check(reset.SourceId == restarted.SourceId && reset.Truncated && reset.GapReason == "source_changed", "stale generation cursor receives an explicit reset gap");
        Check(reset.Events.All(row => row.RequestId == "new-request") && !JsonSerializer.Serialize(reset).Contains("old source content"),
            "response content never crosses a source generation");

        handler.Response = _ => throw new HttpRequestException("transport-private-secret-body");
        await RejectsAsync(() => collector.PollAsync(), "collector exposes transport failure to its retry loop");
        var unavailable = store.Snapshot();
        Check(unavailable.State == "unavailable" && !unavailable.Complete && unavailable.Streams.Any(stream => stream.RequestId == "new-request"),
            "transport failure retains last observed metadata with unavailable coverage");
        Check(!JsonSerializer.Serialize(unavailable).Contains("transport-private-secret-body"), "public failure diagnostic omits raw exception details");
        handler.Response = _ => Reply(Batch("core-b", 2, 2));
        await collector.PollAsync();
        var recovered = JsonSerializer.Serialize(store.Snapshot());
        handler.AsyncResponse = async token => { await Task.Delay(Timeout.Infinite, token); return Reply(Batch("core-b", 2, 2)); };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        var canceled = false;
        try { await collector.PollAsync(cancellation.Token); } catch (OperationCanceledException) { canceled = true; }
        Check(canceled && JsonSerializer.Serialize(store.Snapshot()) == recovered, "canceled collection leaves the previous snapshot and error state unchanged");
    }

    private static async Task BoundedCatchupAndInvalidPages()
    {
        var handler = new Scripted(request =>
        {
            var next = After(request) + 1;
            return Reply(new CoreInspectorBatch("bounded-core", 1, next, 20, 0, [Event(next, next == 1 ? "attempt_started" : "metadata")]));
        });
        var store = new InspectorStore(clock: new Clock());
        using var collector = Collector(handler, store);
        Check(!await collector.PollAsync() && handler.Requests.Count == 16, "one catch-up pass stops at sixteen bounded source pages");
        Check(await collector.PollAsync() && handler.Requests.Count == 20, "following pass resumes at the exact previous source cursor");
        var retained = store.Events(new(Limit: 500));
        Check(retained.Events.Length == 20 && retained.Events.Select(row => row.Cursor).Distinct().Count() == 20, "bounded passes do not duplicate or skip retained events");
        handler.Response = _ => Reply(new CoreInspectorBatch("bounded-core", 1, 20, 20, 0, [Event(20, "metadata")]));
        await RejectsAsync(() => collector.PollAsync(), "duplicate or reordered upstream sequence is rejected before ingestion");
        Check(store.Events(new(Limit: 500)).Events.Length == retained.Events.Length && !store.Snapshot().Complete,
            "invalid page cannot partially mutate retained events or claim complete coverage");
        handler.Response = _ => Reply(new CoreInspectorBatch("bounded-core", 1, 19, 19, 0, []));
        await RejectsAsync(() => collector.PollAsync(), "same-epoch cursor rollback is not silently treated as a restart");
        handler.Response = _ => Text(new string('x', 8 * 1024 * 1024 + 1));
        await RejectsAsync(() => collector.PollAsync(), "oversized Core response is rejected by the HTTP boundary");
        Check(!JsonSerializer.Serialize(store.Snapshot()).Contains(new string('x', 256)), "oversized response content cannot become a public diagnostic");
    }

    private static async Task RedactionAndRetention()
    {
        var poisoned = new AttemptDimensionsDto(WorkerId: "invented-worker", ParentWorkerId: "invented-parent",
            Evidence: [new("parentWorkerId", "heuristic", "common-prefix")]);
        var good = Dimensions("conversation", "turn", "worker-a", "parent");
        var conflict = good with { WorkerId = "worker-b" };
        var batch = Batch("redaction-core", 6, 6,
            Event(1, "attempt_started") with { Dimensions = poisoned },
            Event(2, "metadata") with { Dimensions = good },
            Event(3, "text_delta") with { Text = "Authorization: Bea" },
            Event(4, "text_delta") with { Text = "rer split-secret-value\nsafe output\n" },
            Event(5, "metadata") with { Dimensions = conflict },
            Event(6, "attempt_finished") with { Outcome = "success" });
        var handler = new Scripted(_ => Reply(batch));
        var store = new InspectorStore(clock: new Clock());
        using (var collector = Collector(handler, store)) await collector.PollAsync();
        var stream = store.Snapshot().Streams.Single();
        Check(stream.Dimensions is { WorkerId: null, ParentWorkerId: "parent" } && stream.Truncated,
            "conflicting worker proof becomes unknown without discarding independently evidenced parent metadata");
        var events = store.Events(new(Limit: 500));
        var serialized = JsonSerializer.Serialize(events);
        Check(!serialized.Contains("split-secret-value") && !serialized.Contains("invented-worker") && !serialized.Contains("invented-parent"),
            "chunk-split credentials and unproven guessed identities do not cross the public boundary");
        Check(events.Events.Any(row => row.Text?.Contains("safe output", StringComparison.Ordinal) == true)
            && events.Events.Any(row => row.GapReason == "dimension_conflict"), "redaction retains safe text and reports contradictory identity evidence");

        var clock = new Clock();
        var limits = new InspectorLimitsDto(MaxEvents: 3, ContentBytes: 16384, PerStreamBytes: 8192, MaxEventBytes: 4096, ContentTtlSeconds: 2, TerminalTtlSeconds: 5);
        var bounded = new InspectorStore(limits, clock);
        handler = new Scripted(_ => Reply(Batch("retention-core", 5, 5,
            Event(1, "attempt_started"), Event(2, "metadata"), Event(3, "metadata"), Event(4, "metadata"),
            Event(5, "attempt_finished") with { Outcome = "success" })));
        using (var collector = Collector(handler, bounded)) await collector.PollAsync();
        var retained = bounded.Events(new(Limit: 500));
        Check(retained.Events.Length <= 3 && retained.Truncated && retained.DroppedEvents > 0 && !bounded.Snapshot().Complete,
            "local ring overflow is bounded and explicitly visible rather than silent event loss");
        clock.Advance(TimeSpan.FromSeconds(3));
        Check(bounded.Events(new(Limit: 500)).Events.Length == 0 && bounded.Snapshot().Streams.Length == 1, "content expires before the independent terminal metadata lifetime");
        clock.Advance(TimeSpan.FromSeconds(3));
        Check(bounded.Snapshot().Streams.Length == 0, "terminal metadata is released after its bounded lifetime");
    }

    private static void RegistrationRejectsNonOwnedSources()
    {
        foreach (var address in new[] { "https://127.0.0.1/", "http://provider.invalid/", "http://192.0.2.1/", "http://user:password@127.0.0.1/" })
        {
            var rejected = false;
            try { new ServiceCollection().AddInspector("synthetic-management", new Uri(address)); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "collector registration rejects non-owned or credential-bearing source: " + address);
        }
    }

    private static InspectorCollector Collector(Scripted handler, InspectorStore store)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:18417/") };
        http.DefaultRequestHeaders.Authorization = new("Bearer", "synthetic-management");
        return new(http, store, NullLogger<InspectorCollector>.Instance);
    }
    private static AttemptDimensionsDto Dimensions(string conversation, string turn, string worker, string parent) => new(
        ConversationId: conversation, TurnId: turn, WorkerId: worker, ParentWorkerId: parent,
        Evidence: [new("conversationId", "harness_header", "Thread-Id"), new("turnId", "harness_header", "X-Codex-Turn-Metadata.turn_id"),
            new("workerId", "harness_header", "X-Codex-Turn-Metadata.agent_id"), new("parentWorkerId", "harness_header", "X-Codex-Turn-Metadata.parent_agent_id")]);
    private static CoreInspectorEvent Event(long sequence, string kind, string request = "request", string attempt = "attempt")
        => new(sequence, At.AddMilliseconds(sequence), kind, "attempt", request, attempt);
    private static CoreInspectorBatch Batch(string source, long next, long high, params CoreInspectorEvent[] rows)
        => new(source, 1, next, high, 0, rows);
    private static HttpResponseMessage Reply(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value, options: Json) };
    private static HttpResponseMessage Text(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    private static long After(HttpRequestMessage request) => long.Parse(request.RequestUri!.Query.TrimStart('?').Split('&').Single(pair => pair.StartsWith("after=", StringComparison.Ordinal))[6..]);
    private static async Task RejectsAsync(Func<Task<bool>> operation, string label)
    {
        var rejected = false;
        try { await operation(); } catch (Exception error) when (error is HttpRequestException or InvalidDataException or JsonException) { rejected = true; }
        Check(rejected, label);
    }
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        assertions++;
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = At;
        public override DateTimeOffset GetUtcNow() => now;
        public override long GetTimestamp() => now.UtcTicks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan amount) => now += amount;
    }
    private sealed class Scripted(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Response = response;
        public Func<CancellationToken, Task<HttpResponseMessage>>? AsyncResponse;
        public readonly List<string> Requests = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Check(request.Method == HttpMethod.Get && request.RequestUri!.Host == "127.0.0.1"
                && request.RequestUri.AbsolutePath == "/lain/inspector/events" && request.Headers.Authorization?.Parameter == "synthetic-management",
                "collector performs only authenticated read-only owned-source requests");
            Requests.Add(request.RequestUri!.ToString());
            return AsyncResponse is null ? Task.FromResult(Response(request)) : AsyncResponse(token);
        }
    }
}
