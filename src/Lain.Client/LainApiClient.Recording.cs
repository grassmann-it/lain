using System.Globalization;
using Lain.Core;

namespace Lain.Client;

public sealed partial class LainApiClient
{
    public Task<RecordingHealthDto> RecordingStatusAsync(CancellationToken ct = default) => Get<RecordingHealthDto>("recording/status", ct);
    public Task<CapturePolicySnapshot> RecordingPolicyAsync(CancellationToken ct = default) => Get<CapturePolicySnapshot>("recording/policy", ct);
    public Task<CapturePolicySnapshot> UpdateRecordingPolicyAsync(RecordingPolicyUpdate update, CancellationToken ct = default) =>
        Send<CapturePolicySnapshot>(HttpMethod.Put, "recording/policy", update, ct);
    public Task<RecordingConversationPage> RecordingConversationsAsync(RecordingConversationQuery? query = null, CancellationToken ct = default)
    {
        query ??= new(); CheckRecordingLimit(query.Limit);
        var values = new List<string> { "limit=" + query.Limit.ToString(CultureInfo.InvariantCulture) };
        void Add(string name, string? value) { if (value is not null) values.Add(name + "=" + Id(value)); }
        Add("cursor", query.Cursor); Add("model", query.Model); Add("routeId", query.RouteId); Add("workerId", query.WorkerId);
        return Get<RecordingConversationPage>("recording/conversations?" + string.Join('&', values), ct);
    }
    public Task<RecordingConversationDetail> RecordingConversationAsync(string id, string? cursor = null, int limit = 50, CancellationToken ct = default)
    {
        CheckRecordingLimit(limit);
        return Get<RecordingConversationDetail>($"recording/conversations/{RecordingId(id)}?limit={limit}" +
            (cursor is null ? "" : "&cursor=" + Id(cursor)), ct);
    }
    public Task<RecordingCommitPage> RecordingEventsAsync(long after = 0, int limit = 50, CancellationToken ct = default)
    {
        if (after < 0) throw new ArgumentException("Recording cursor must be nonnegative."); CheckRecordingLimit(limit);
        return Get<RecordingCommitPage>($"recording/events?after={after.ToString(CultureInfo.InvariantCulture)}&limit={limit}", ct);
    }
    public Task<RecordingRunManifest[]> RecordingRunsAsync(string conversationId, CancellationToken ct = default) =>
        Get<RecordingRunManifest[]>($"recording/conversations/{RecordingId(conversationId)}/runs", ct);
    public Task<RecordingRunManifest[]> LabelRecordingRunAsync(string conversationId, long expectedRevision,
        RecordingRunManifest manifest, CancellationToken ct = default) =>
        Send<RecordingRunManifest[]>(HttpMethod.Put, $"recording/conversations/{RecordingId(conversationId)}/runs",
            new { expectedRevision, manifest }, ct);
    public Task<RecordingClearResult> ClearRecordingArchiveAsync(long expectedRevision, CancellationToken ct = default)
    {
        if (expectedRevision < 0) throw new ArgumentException("Recording revision must be nonnegative.");
        return Send<RecordingClearResult>(HttpMethod.Delete, $"recording/archive?expectedRevision={expectedRevision.ToString(CultureInfo.InvariantCulture)}", null, ct);
    }
    public Task<RecordingSourceTextPage> RecordingSourceTextAsync(RecordingSourceReference source, int offset = 0, int characters = 8192, CancellationToken ct = default)
    {
        if (source.Sequence < 1 || offset < 0 || characters is < 1 or > 32768) throw new ArgumentException("Invalid source page.");
        return Get<RecordingSourceTextPage>($"recording/sources/{RecordingId(source.SourceEpoch)}/{source.Sequence.ToString(CultureInfo.InvariantCulture)}?eventId={RecordingId(source.EventId)}&offset={offset.ToString(CultureInfo.InvariantCulture)}&characters={characters.ToString(CultureInfo.InvariantCulture)}", ct);
    }
    private static void CheckRecordingLimit(int limit)
    { if (limit is < 1 or > 500) throw new ArgumentException("Recording pages require 1..500 items and remain subject to policy limits."); }
    private static string RecordingId(string value)
    {
        if (value is not { Length: > 0 and <= 256 } || value is "." or ".." ||
            value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.' or ':' or '@' or '+')))
            throw new ArgumentException("Invalid recording identity.");
        return Id(value);
    }
}
