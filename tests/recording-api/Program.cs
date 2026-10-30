using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Recording.Api;
using Lain.Host.Recording.Collection;
using Lain.Host.Recording.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

var count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); count++; }
async Task Reject(Func<Task> action, string code)
{
    try { await action(); throw new Exception("Expected rejection " + code); }
    catch (RecordingSourceException e) { Check(e.Code == code, code); }
}
var policy = new RecordingPolicyService();
var handler = new FixtureSource();
using var http = new HttpClient(handler) { BaseAddress = new("http://127.0.0.1:18417/") };
using var source = new RecordingSourceClient(http);
var store = new FixtureStore();
var projection = new FixtureProjection();
var collector = new RecordingCollector(source, policy, store, projection, NullLogger<RecordingCollector>.Instance);
await collector.PollAsync();
Check(handler.Requests == 0 && store.Records.Count == 0, "default off does not poll or store content");
policy.Update(new(0, true, RecordingContract.Content));
var enabled = policy.Snapshot;
handler.Policy = enabled;
SourceObservation Observation(long sequence, string body, string kind = "request", string encoding = "utf8_json") => new("epoch-a", sequence,
    "event-" + sequence, "capture-a", "request-a", DateTimeOffset.UtcNow, "client_ingress", "openai_chat", kind,
    Body: Encoding.UTF8.GetBytes(body), BodyEncoding: encoding, PolicyRevision: enabled.Revision, StopGeneration: enabled.StopGeneration);
handler.Events = [Observation(1, "{\"messages\":[{\"role\":\"user\",\"content\":\"hello\"}],\"api_key\":\"SYNTHETIC_SECRET\"}")];
store.Fail = true;
await Reject(() => collector.PollAsync(), "store_commit_failed");
Check(store.Records.Count == 0, "store failure has no durable checkpoint");
store.Fail = false;
await collector.PollAsync();
Check(handler.After.TakeLast(1).Single() == 0 && store.Records.Count == 1, "retry starts from committed source cursor");
Check(!JsonSerializer.Serialize(store.Records).Contains("SYNTHETIC_SECRET"), "source credential removed before durable commit");
await collector.PollAsync();
Check(store.Records.Count == 1, "duplicate polling is inert");
projection.Fail = true;
handler.Events = [.. handler.Events, Observation(2, "{}")];
try { await collector.PollAsync(); } catch (IOException) { }
Check(store.Records.Count == 2, "projection failure cannot undo durable source record");
projection.Fail = false;
await collector.PollAsync();
Check(handler.After.Last() == 2 && projection.Rebuilds >= 2, "projection replay preserves committed source checkpoint");
handler.Events = [.. handler.Events, Observation(3, "data: {\"choices\":[{\"delta\":{\"content\":\"Bearer abc\"}}]}\n\n", "response_chunk", "utf8_sse"),
    Observation(4, "data: {\"choices\":[{\"delta\":{\"content\":\"defghijk\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n", "response_chunk", "utf8_sse"),
    Observation(5, "", "attempt_finished")];
await collector.PollAsync();
var saved = JsonSerializer.Serialize(store.Records);
Check(!saved.Contains("abcdefghijk") && !saved.Contains("Bearer abc") && store.Records.Any(r => r.Observation.Kind == "normalized_stream_body"),
    "stream fragments assembled transiently and redacted before commit");
var unavailable = new RecordingCollector(source, policy, store, projection, NullLogger<RecordingCollector>.Instance, () => false);
var before = handler.Requests; await unavailable.PollAsync();
Check(handler.Requests == before && unavailable.State == "source_unavailable", "runtime unavailable never starts or probes Core");
handler.Code = HttpStatusCode.Redirect;
await Reject(() => source.ReadAsync(0, null, 1), "source_redirect_rejected");
handler.Code = HttpStatusCode.Unauthorized;
await Reject(() => source.ReadAsync(0, null, 1), "source_unauthorized");
var rejectedAddress = 0;
foreach (var address in new[] { "https://127.0.0.1/", "http://example.com/", "http://127.0.0.1/elsewhere", "http://user:pass@127.0.0.1/" })
    try { using var ignored = new RecordingSourceClient(new Uri(address), "synthetic"); } catch (ArgumentException) { rejectedAddress++; }
Check(rejectedAddress == 4, "remote, credentialed and non-root source addresses rejected");
var context = new DefaultHttpContext(); context.Request.QueryString = new("?limit=101&limit=2");
try { _ = RecordingArguments.Limit(context.Request, 100); throw new Exception("Expected duplicate limit rejection"); } catch (ArgumentException) { count++; }
var bodyRecord = store.Records[0] with { Observation = store.Records[0].Observation with { Body = new string('a', RecordingEndpoints.MaximumContentResponseBytes + 1) } };
Check(RecordingEndpoints.BoundedEvents(new([bodyRecord], 1, 1, false)).Records.Single().Observation is { Body: null, GapReason: "api_body_limit" }, "body response cap is explicit");
Console.WriteLine($"PASS {count} recording API/collector assertions");

sealed class FixtureSource : HttpMessageHandler
{
    public int Requests;
    public CapturePolicySnapshot Policy = new();
    public SourceObservation[] Events = [];
    public List<long> After = [];
    public HttpStatusCode Code = HttpStatusCode.OK;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests++;
        if (Code != HttpStatusCode.OK) return Task.FromResult(new HttpResponseMessage(Code) { Content = new StringContent("SYNTHETIC_DO_NOT_ECHO") });
        if (request.RequestUri!.AbsolutePath.EndsWith("/status"))
            return Task.FromResult(new HttpResponseMessage(Code) { Content = JsonContent.Create(new RecordingSourceStatus(1, Policy.Enabled, Policy.Mode, Policy.Revision, Policy.StopGeneration)) });
        var values = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri.Query);
        var after = long.Parse(values["after"].ToString()); After.Add(after);
        var events = Events.Where(e => e.Sequence > after).ToArray();
        return Task.FromResult(new HttpResponseMessage(Code) { Content = JsonContent.Create(new RecordingSourceBatch("epoch-a", events,
            NextCursor: events.LastOrDefault()?.Sequence ?? after, HighWatermark: Events.LastOrDefault()?.Sequence ?? 0)) });
    }
}
sealed class FixtureStore : IRecordingStore
{
    public bool Fail;
    public List<CommittedObservation> Records = [];
    public Task<RecordingCommitResult> CommitAsync(SanitizedObservation observation, CancellationToken ct = default)
    {
        if (Fail) return Task.FromResult(new RecordingCommitResult(false, ErrorCode: "synthetic_disk_fault"));
        var record = Records.FirstOrDefault(r => r.Observation.EventId == observation.EventId && r.Observation.SourceEpoch == observation.SourceEpoch);
        if (record is null) { record = new(observation, Records.Count + 1, "journal", Records.Count, "synthetic"); Records.Add(record); }
        return Task.FromResult(new RecordingCommitResult(true, record));
    }
    public Task<RecordingCommitPage> ReadCommittedAsync(long after = 0, int limit = 100, CancellationToken ct = default) =>
        Task.FromResult(new RecordingCommitPage(Records.Where(r => r.Cursor > after).Take(limit).ToArray(), Records.Count, Records.Count, false));
    public Task<RecordingStorageHealthDto> HealthAsync(CancellationToken ct = default) => Task.FromResult(new RecordingStorageHealthDto());
    public Task ClearAsync(CancellationToken ct = default) { Records.Clear(); return Task.CompletedTask; }
}
sealed class FixtureProjection : IRecordingProjection
{
    public bool Fail;
    public int Rebuilds;
    public Task ApplyAsync(CommittedObservation observation, CancellationToken ct = default) => Fail ? Task.FromException(new IOException("SYNTHETIC_NO_ECHO")) : Task.CompletedTask;
    public Task RebuildAsync(CancellationToken ct = default) { Rebuilds++; return Task.CompletedTask; }
    public Task<RecordingConversationPage> ListAsync(RecordingConversationQuery query, CancellationToken ct = default) => Task.FromResult(new RecordingConversationPage([]));
    public Task<RecordingConversationDetail?> GetAsync(string conversationId, string? cursor = null, int limit = 100, CancellationToken ct = default) => Task.FromResult<RecordingConversationDetail?>(null);
}
