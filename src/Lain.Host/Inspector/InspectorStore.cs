using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Inspector;

/// <summary>Bounded, transient projection of the owned core. No files, database or payload logging.</summary>
public sealed class InspectorStore
{
    private const int MaximumPageBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal)
        { "attempt_started", "text_delta", "reasoning_delta", "tool_delta", "metadata", "attempt_finished" };
    private readonly object gate = new();
    private readonly TimeProvider clock;
    private readonly InspectorLimitsDto limits;
    private readonly Dictionary<string, StreamEntry> streams = new(StringComparer.Ordinal);
    private readonly LinkedList<StoredEvent> events = new();
    private string sourceId = Guid.NewGuid().ToString("N");
    private string? upstreamSource;
    private string state = "waiting";
    private string? error;
    private DateTimeOffset observedAt = DateTimeOffset.UnixEpoch;
    private long cursor, lastSequence, dropped, lastGapCursor, retainedBytes;
    private long? oldestEventAt;
    private string? gapReason;

    public InspectorStore(InspectorLimitsDto? limits = null, TimeProvider? clock = null)
    {
        this.limits = limits ?? new();
        this.clock = clock ?? TimeProvider.System;
        if (this.limits.MaxStreams is < 1 or > 256 || this.limits.MaxEvents is < 1 or > 8192 ||
            this.limits.ContentBytes is < 1 or > 33554432 || this.limits.PerStreamBytes < 1 || this.limits.PerStreamBytes > this.limits.ContentBytes ||
            this.limits.PerStreamBytes > 1048576 || this.limits.MaxEventBytes is < 1 or > 262144 ||
            this.limits.ContentTtlSeconds is < 1 or > 120 || this.limits.TerminalTtlSeconds is < 1 or > 60 ||
            this.limits.MaxPageSize is < 1 or > 500 || this.limits.ContentPolicy != "redacted_output_only_v1")
            throw new ArgumentException("Invalid inspector limits.", nameof(limits));
    }

    public void BeginSource(string instanceId)
    {
        if (InspectorTextRedactor.Identity(instanceId) is not { } safeSource || safeSource != instanceId)
            throw new InvalidDataException("Invalid inspector source identity.");
        lock (gate)
        {
            Prune();
            if (upstreamSource == instanceId) return;
            if (upstreamSource is not null)
            {
                // Content never crosses a source generation. Keep short-lived metadata tombstones
                // for previously active attempts, whose outcome was not observed.
                foreach (var entry in streams.Values)
                {
                    entry.Events.Clear(); entry.Bytes = 0;
                    foreach (var redactor in entry.Redactors.Values) redactor.Clear();
                    entry.Value = entry.Value with { Truncated = true, FirstCursor = 0, LastCursor = 0 };
                    if (!entry.Terminal)
                    {
                        entry.Value = entry.Value with { State = "unknown", Truncated = true, FirstCursor = 0, LastCursor = 0 };
                        entry.TerminalAt = clock.GetTimestamp();
                    }
                }
                events.Clear(); retainedBytes = 0; oldestEventAt = null;
                sourceId = Guid.NewGuid().ToString("N"); cursor = dropped = lastGapCursor = 0; gapReason = null;
            }
            upstreamSource = instanceId; lastSequence = 0; state = "connecting"; error = null;
        }
    }

    public void MarkConnected()
    {
        lock (gate) { Prune(); observedAt = clock.GetUtcNow(); state = "live"; error = null; }
    }

    public void MarkUnavailable()
    {
        lock (gate)
        {
            Prune(); state = "unavailable"; error = "The owned inspector source is unavailable; active outcomes are unknown.";
            foreach (var entry in streams.Values.Where(entry => !entry.Terminal))
            {
                entry.Value = entry.Value with { State = "unknown", Truncated = true };
                entry.TerminalAt ??= clock.GetTimestamp();
                foreach (var redactor in entry.Redactors.Values) redactor.ResetAfterGap();
            }
        }
    }

    /// <summary>Records a known unseen upstream interval, including a fully evicted empty page.</summary>
    public void MarkUpstreamGap(long throughSequence)
    {
        lock (gate)
        {
            if (throughSequence <= lastSequence) return;
            Gap(throughSequence - lastSequence, "upstream_evicted");
            lastSequence = throughSequence;
            foreach (var entry in streams.Values)
            {
                foreach (var redactor in entry.Redactors.Values) redactor.ResetAfterGap();
                if (!entry.Terminal)
                {
                    entry.Value = entry.Value with { State = "unknown", Truncated = true };
                    entry.TerminalAt ??= clock.GetTimestamp();
                }
            }
        }
    }

    /// <summary>Duplicate upstream sequences are inert. Source changes must precede ingestion.</summary>
    public void Ingest(CoreInspectorEvent incoming)
    {
        lock (gate)
        {
            Prune();
            if (upstreamSource is null) throw new InvalidOperationException("Inspector source has not been established.");
            if (incoming.Sequence <= lastSequence) return;
            if (incoming.Sequence > lastSequence + 1) MarkUpstreamGap(incoming.Sequence - 1);
            lastSequence = incoming.Sequence;
            var request = InspectorTextRedactor.Identity(incoming.RequestId);
            var attempt = InspectorTextRedactor.Identity(incoming.AttemptId);
            if (request is null || attempt is null || incoming.Scope != "attempt" || !Kinds.Contains(incoming.Kind))
            { Gap(1, "unsupported_or_invalid_event"); return; }
            var id = upstreamSource + "/attempt/" + attempt;
            if (!streams.TryGetValue(id, out var entry))
            {
                if (!MakeRoom()) { Gap(1, "tracking_capacity"); return; }
                entry = new(new(id, "attempt", request, attempt, incoming.ObservedAt, incoming.ObservedAt, "active", 0, 0,
                    Truncated: incoming.Kind != "attempt_started"));
                streams.Add(id, entry);
            }
            if (entry.Value.RequestId != request || IdentityConflict(entry.Value.AccountId, incoming.AccountId) || IdentityConflict(entry.Value.RouteId, incoming.RouteId))
            { entry.Value = entry.Value with { Truncated = true }; Gap(1, "identity_conflict"); return; }

            var dimensions = InspectorDimensions.Merge(entry.Value.Dimensions, incoming.Dimensions, entry.DimensionConflicts);
            var measurements = Measurements(incoming.Measurements) ?? entry.Value.Measurements;
            var suppliedTokens = incoming.Tokens ?? (incoming.InputTokens.HasValue || incoming.OutputTokens.HasValue || incoming.TotalTokens.HasValue
                ? new TokenCounts(incoming.InputTokens, incoming.OutputTokens, null, null, incoming.TotalTokens) : null);
            var tokens = suppliedTokens is null ? entry.Value.Tokens : Tokens(suppliedTokens);
            var isEnd = incoming.Kind == "attempt_finished";
            var nextState = isEnd ? incoming.Outcome is "success" or "failed" or "canceled" ? incoming.Outcome : "unknown"
                : entry.Terminal ? entry.Value.State : "active";
            entry.Value = entry.Value with
            {
                UpdatedAt = incoming.ObservedAt, State = nextState,
                AccountId = entry.Value.AccountId ?? InspectorTextRedactor.Identity(incoming.AccountId),
                RouteId = entry.Value.RouteId ?? InspectorTextRedactor.Identity(incoming.RouteId),
                Provider = entry.Value.Provider ?? InspectorTextRedactor.Metadata(incoming.Provider, 160),
                Model = InspectorTextRedactor.Metadata(incoming.Model, 160) ?? entry.Value.Model,
                RequestedModel = entry.Value.RequestedModel ?? InspectorTextRedactor.Metadata(incoming.RequestedModel, 160),
                Dimensions = dimensions, Measurements = measurements, Tokens = tokens,
                StatusCode = incoming.StatusCode is >= 100 and <= 599 ? incoming.StatusCode : entry.Value.StatusCode,
                FinishedAt = isEnd ? measurements?.FinishedAt ?? incoming.ObservedAt : entry.Value.FinishedAt,
                Truncated = entry.Value.Truncated || incoming.Truncated || entry.DimensionConflicts.Count > 0
            };
            if (!entry.Terminal && !isEnd) entry.TerminalAt = null;
            if (incoming.Truncated)
            {
                foreach (var redactor in entry.Redactors.Values) redactor.ResetAfterGap();
                Gap(1, "upstream_content_truncated");
            }
            var channel = incoming.Kind switch { "text_delta" => "text", "reasoning_delta" => "reasoning", "tool_delta" => "tool", _ => "status" };
            string? text = null;
            string? redactionState = null;
            long? textReceivedAt = null;
            if (channel is "text" or "reasoning")
            {
                var cleaned = entry.Redactors[channel].Push(incoming.Text, incoming.ObservedAt, receivedAt: clock.GetTimestamp());
                text = cleaned.Text; redactionState = cleaned.Removed ? "removed" : "sanitized";
                textReceivedAt = cleaned.ReceivedAt;
                if (cleaned.Removed) { entry.Value = entry.Value with { Truncated = true }; Gap(1, "oversize_content_removed"); }
            }
            else if (incoming.Text is not null) redactionState = "removed";
            // Tool arguments/results and arbitrary metadata text are never display content.
            if (isEnd)
            {
                foreach (var pair in entry.Redactors)
                {
                    var at = pair.Value.LastObservedAt ?? incoming.ObservedAt;
                    var tail = pair.Value.Push(null, at, final: true);
                    if (tail.Text is not null) Add(entry, Event(entry, incoming, pair.Key + "_delta", pair.Key, tail.Text, "sanitized") with { ObservedAt = at }, tail.ReceivedAt);
                }
                entry.Terminal = true; entry.TerminalAt = clock.GetTimestamp();
            }
            Add(entry, Event(entry, incoming, incoming.Kind, channel, text, redactionState), textReceivedAt);
        }
    }

    public InspectorSnapshotDto Snapshot(InspectorQuery? query = null)
    {
        query ??= new();
        ValidateRequest(query.RequestId);
        if (query.Limit < 1 || query.Limit > limits.MaxStreams) throw new ArgumentException("Invalid inspector stream limit.");
        lock (gate)
        {
            Prune();
            var selected = streams.Values.Select(entry => entry.Value).Where(value => query.RequestId is null || value.RequestId == query.RequestId)
                .OrderBy(value => value.State is "active" or "pending" ? 0 : 1).ThenByDescending(value => value.UpdatedAt).ThenBy(value => value.Id, StringComparer.Ordinal).ToArray();
            return new(Api.Version, sourceId, observedAt, state, state == "live" && dropped == 0 && selected.Length <= query.Limit && selected.All(value => !value.Truncated),
                cursor, Earliest(), dropped, limits, selected.Take(query.Limit).ToArray(), error);
        }
    }

    public InspectorEventPageDto Events(InspectorEventQuery? query = null)
    {
        query ??= new();
        ValidateRequest(query.RequestId);
        if (query.After < 0 || query.Limit < 1 || query.Limit > limits.MaxPageSize || query.After > 0 && string.IsNullOrEmpty(query.SourceId))
            throw new ArgumentException("Invalid inspector event query or missing source generation.");
        lock (gate)
        {
            Prune();
            var changed = query.SourceId is not null && query.SourceId != sourceId;
            var after = changed ? 0 : query.After;
            if (after > cursor) throw new ArgumentException("Inspector cursor is ahead of the current generation.");
            var page = new List<InspectorEventDto>();
            var next = after; var bytes = 0; var bounded = false;
            foreach (var stored in events)
            {
                if (stored.Value.Cursor <= after) continue;
                if (query.RequestId is null || stored.Value.RequestId == query.RequestId)
                {
                    if (page.Count == query.Limit || bytes + stored.Bytes > MaximumPageBytes) { bounded = true; break; }
                    page.Add(stored.Value); bytes += stored.Bytes;
                }
                next = stored.Value.Cursor;
            }
            if (!bounded) next = cursor; // Filtered-out records and explicit gaps must not stall a reader.
            var truncated = changed || after < Earliest() - 1 || lastGapCursor > after;
            return new(sourceId, page.ToArray(), next, cursor, Earliest(), dropped, truncated,
                changed ? "source_changed" : truncated ? gapReason ?? "retention" : null);
        }
    }

    private InspectorEventDto Event(StreamEntry entry, CoreInspectorEvent input, string kind, string channel, string? text, string? redaction) =>
        new(0, kind, input.ObservedAt, entry.Value.RequestId, entry.Value.AttemptId, "attempt", entry.Value.AccountId, entry.Value.RouteId,
            entry.Value.Provider, entry.Value.Model, entry.Value.Dimensions, channel, text,
            InspectorTextRedactor.Identity(input.ToolCallId), InspectorTextRedactor.Metadata(input.ToolName, 160), entry.Value.State,
            entry.Value.StatusCode, entry.Value.Tokens, entry.Value.Measurements, entry.Value.Truncated,
            entry.DimensionConflicts.Count > 0 ? "dimension_conflict" : null, entry.Value.RequestedModel, redaction);

    private void Add(StreamEntry entry, InspectorEventDto value, long? receivedAt = null)
    {
        value = value with { Cursor = ++cursor };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json).Length;
        entry.Value = entry.Value with { FirstCursor = entry.Value.FirstCursor == 0 ? cursor : entry.Value.FirstCursor, LastCursor = cursor };
        if (bytes > limits.MaxEventBytes || bytes > limits.PerStreamBytes || bytes > limits.ContentBytes)
        { entry.Value = entry.Value with { Truncated = true }; Drop(1, cursor, "event_capacity"); return; }
        var stored = new StoredEvent(value, bytes, receivedAt ?? clock.GetTimestamp(), entry);
        oldestEventAt = oldestEventAt is { } oldest ? Math.Min(oldest, stored.At) : stored.At;
        stored.Global = events.AddLast(stored); stored.Stream = entry.Events.AddLast(stored);
        retainedBytes += bytes; entry.Bytes += bytes;
        while (entry.Bytes > limits.PerStreamBytes) Remove(entry.Events.First!.Value, "stream_retention");
        while (events.Count > limits.MaxEvents || retainedBytes > limits.ContentBytes) Remove(events.First!.Value, "global_retention");
    }

    private void Prune()
    {
        var now = clock.GetTimestamp();
        if (oldestEventAt is { } oldest && clock.GetElapsedTime(oldest, now).TotalSeconds >= limits.ContentTtlSeconds)
        {
            // Delayed sanitized output can be older than an earlier cursor.
            // Scan only when the earliest retained receipt can have expired.
            oldestEventAt = null;
            for (var node = events.First; node is not null;)
            {
                var next = node.Next;
                var item = node.Value;
                if (clock.GetElapsedTime(item.At, now).TotalSeconds >= limits.ContentTtlSeconds) Remove(item, "expired");
                else oldestEventAt = oldestEventAt is { } retained ? Math.Min(retained, item.At) : item.At;
                node = next;
            }
        }
        foreach (var entry in streams.Values.Where(entry => entry.TerminalAt is { } at && clock.GetElapsedTime(at, now).TotalSeconds >= limits.TerminalTtlSeconds).ToArray())
            RemoveStream(entry, "stream_expired");
        // Deferred text follows the same lifetime, even when its source stops mid-token.
        foreach (var entry in streams.Values)
            foreach (var redactor in entry.Redactors.Values)
                if (redactor.OldestReceipt is { } at && clock.GetElapsedTime(at, now).TotalSeconds >= limits.ContentTtlSeconds)
                    redactor.ResetAfterGap();
    }

    private bool MakeRoom()
    {
        if (streams.Count < limits.MaxStreams) return true;
        var victim = streams.Values.Where(entry => entry.TerminalAt.HasValue).OrderBy(entry => entry.TerminalAt).FirstOrDefault();
        if (victim is null) return false;
        RemoveStream(victim, "stream_evicted"); return true;
    }

    private void RemoveStream(StreamEntry entry, string reason)
    {
        while (entry.Events.First is { } first) Remove(first.Value, reason);
        foreach (var redactor in entry.Redactors.Values) redactor.Clear();
        streams.Remove(entry.Value.Id);
    }

    private void Remove(StoredEvent stored, string reason)
    {
        events.Remove(stored.Global!); stored.Entry.Events.Remove(stored.Stream!);
        retainedBytes -= stored.Bytes; stored.Entry.Bytes -= stored.Bytes;
        stored.Entry.Value = stored.Entry.Value with { Truncated = true };
        Drop(1, stored.Value.Cursor, reason);
    }

    private void Gap(long count, string reason) => Drop(count, ++cursor, reason);
    private void Drop(long count, long at, string reason)
    {
        dropped = count > long.MaxValue - dropped ? long.MaxValue : dropped + count;
        lastGapCursor = Math.Max(lastGapCursor, at); gapReason = reason;
    }
    private long Earliest() => events.First?.Value.Value.Cursor ?? cursor + 1;
    private static void ValidateRequest(string? id) { if (id is not null && InspectorTextRedactor.Identity(id) is null) throw new ArgumentException("Invalid inspector request identity."); }
    private static bool IdentityConflict(string? known, string? value) => known is not null && InspectorTextRedactor.Identity(value) is { } candidate && known != candidate;
    private static TokenCounts Tokens(TokenCounts tokens)
    {
        long? Amount(long? value) => value is >= 0 ? value : null;
        return new(Amount(tokens.Input), Amount(tokens.Output), Amount(tokens.Cached), Amount(tokens.Reasoning), Amount(tokens.Total));
    }
    private static AttemptMeasurementsDto? Measurements(AttemptMeasurementsDto? value)
    {
        if (value is null || InspectorTextRedactor.Metadata(value.Source, 96) is not { } source) return null;
        double? Valid(double? number) => number is { } n && double.IsFinite(n) && n >= 0 ? n : null;
        var duration = Valid(value.TotalDurationMs); var first = Valid(value.FirstOutputLatencyMs);
        if (duration is { } total && first > total) first = null;
        return new(first, duration, value.FirstOutputAt, value.FinishedAt, source);
    }

    private sealed class StreamEntry(InspectorStreamDto value)
    {
        public InspectorStreamDto Value = value;
        public bool Terminal;
        public long? TerminalAt;
        public long Bytes;
        public readonly LinkedList<StoredEvent> Events = new();
        public readonly HashSet<string> DimensionConflicts = new(StringComparer.Ordinal);
        public readonly Dictionary<string, InspectorTextRedactor> Redactors = new(StringComparer.Ordinal)
            { ["text"] = new(), ["reasoning"] = new() };
    }
    private sealed class StoredEvent(InspectorEventDto value, int bytes, long at, StreamEntry entry)
    {
        public readonly InspectorEventDto Value = value;
        public readonly int Bytes = bytes;
        public readonly long At = at;
        public readonly StreamEntry Entry = entry;
        public LinkedListNode<StoredEvent>? Global, Stream;
    }
}
