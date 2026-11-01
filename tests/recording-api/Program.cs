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
var pagingSource = new RecordingSourceReference("emoji-epoch", 1, "emoji-event");
var emojiRecord = store.Records[0] with { Observation = store.Records[0].Observation with { Body = "😀🙂" } };
foreach (var characters in new[] { 1, 2, RecordingSourcePaging.MaximumCharacters })
{
    var offset = 0;
    var reconstructed = new StringBuilder();
    var pages = 0;
    while (true)
    {
        Check(RecordingSourcePaging.TryCreate(emojiRecord, pagingSource, offset, characters, out var page) && page is not null,
            "UTF16 source page exists at size " + characters);
        var value = page!;
        Check(value.Offset == offset && value.Text is { Length: > 0 } && value.Text.Length <= RecordingSourcePaging.MaximumCharacters &&
            !char.IsLowSurrogate(value.Text[0]) && !char.IsHighSurrogate(value.Text[^1]),
            "UTF16 emoji page preserves both surrogate boundaries at size " + characters);
        reconstructed.Append(value.Text);
        var next = value.Offset + value.Text!.Length;
        Check(next > offset, "nonterminal one-unit emoji paging always advances");
        offset = next;
        if (!value.HasMore) break;
        if (++pages > emojiRecord.Observation.Body!.Length) throw new Exception("UTF16 page failed to advance");
    }
    Check(reconstructed.ToString() == "😀🙂" && offset == 4, "UTF16 emoji pages reconstruct exact source at size " + characters);
}
Check(RecordingSourcePaging.TryCreate(emojiRecord, pagingSource, 1, 1, out var lowSurrogatePage) &&
    lowSurrogatePage is { Offset: 0, Text: "😀", TotalCharacters: 4, HasMore: true },
    "offset on a low surrogate rewinds to the complete scalar and advances past it");
Check(RecordingSourcePaging.TryCreate(emojiRecord, pagingSource, 4, 1, out var endPage) &&
    endPage is { Offset: 4, Text: "", TotalCharacters: 4, HasMore: false }, "exact source end returns an empty terminal page");
Check(!RecordingSourcePaging.TryCreate(emojiRecord, pagingSource, 5, 1, out var expiredPage) && expiredPage is null,
    "source cursor past exact end remains expired");
var maximumRecord = emojiRecord with { Observation = emojiRecord.Observation with {
    Body = new string('a', RecordingSourcePaging.MaximumCharacters - 1) + "😀z" } };
Check(RecordingSourcePaging.TryCreate(maximumRecord, pagingSource, 0, RecordingSourcePaging.MaximumCharacters, out var maximumPage) &&
    maximumPage is { Offset: 0, HasMore: true } && maximumPage.Text!.Length == RecordingSourcePaging.MaximumCharacters - 1 &&
    maximumPage.Text.All(c => c == 'a'), "maximum source window backs off a trailing high surrogate without exceeding its cap");
Check(RecordingSourcePaging.TryCreate(maximumRecord, pagingSource, maximumPage!.Text!.Length, RecordingSourcePaging.MaximumCharacters, out var tailPage) &&
    tailPage is { Text: "😀z", HasMore: false }, "maximum source window resumes with the complete trailing emoji");
Check(endPage!.Source == pagingSource && endPage.Boundary == emojiRecord.Observation.Boundary &&
    endPage.Protocol == emojiRecord.Observation.Protocol && endPage.RedactionState == emojiRecord.Observation.RedactionState &&
    endPage.ContentHash == emojiRecord.ContentHash, "UTF16 paging preserves selected source identity and retention metadata");
var unavailableRecord = emojiRecord with { Observation = emojiRecord.Observation with { Body = null, GapReason = "content_unavailable" } };
Check(RecordingSourcePaging.TryCreate(unavailableRecord, pagingSource, 0, 1, out var unavailablePage) &&
    unavailablePage is { Text: null, TotalCharacters: 0, HasMore: false, GapReason: "content_unavailable" },
    "unavailable retained text remains null with its explicit gap");
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
