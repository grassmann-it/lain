using System.Text;
using System.Text.Json;
using Lain.Core;
using static Lain.Host.Recording.Normalization.RecordingProtocolNormalizer;

namespace Lain.Host.Recording.Normalization;

public sealed record RecordingAssemblyLimits(int MaxActiveStreams = 64, int MaxStreamBytes = 16 * 1024 * 1024,
    int MaxFrameBytes = 1024 * 1024, int MaxParts = 4096, int MaxSourceRecords = 8192,
    int IdleSeconds = 120, long MaxTotalBytes = 64L * 1024 * 1024);

/// <summary>
/// Transient pre-policy framing. Raw SSE/delta bytes never leave Push. Call Push before Sanitize, then
/// persist only its returned observation. An assembled body is still untrusted and MUST pass policy.
/// Instance lifetime is one capture generation; Reset discards every pending private buffer.
/// </summary>
public sealed class RecordingStreamAssembler(RecordingAssemblyLimits? limits = null, TimeProvider? clock = null)
{
    private readonly RecordingAssemblyLimits _limits = limits ?? new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly Dictionary<string, Assembly> _active = new(StringComparer.Ordinal);
    private long _bytes;
    private readonly HashSet<string> _expired = new(StringComparer.Ordinal);
    private static bool IsChunk(SourceObservation o) => o.BodyEncoding is "utf8_sse" or "sse" or "text/event-stream" ||
        o.Kind.Contains("chunk", StringComparison.OrdinalIgnoreCase) || o.Kind.Contains("delta", StringComparison.OrdinalIgnoreCase);
    private static bool IsTerminal(SourceObservation o) => o.Kind is "attempt_finished" or "response_complete" or "request_finished" or "stream_end" or "attempt_failed" or "attempt_canceled" or "response";

    public void Reset() { lock (_gate) { _active.Clear(); _expired.Clear(); _bytes = 0; } }

    public SourceObservation Push(SourceObservation observation)
    {
        lock (_gate)
        {
            if (observation.SourceEpoch.Length > 256 || observation.EventId.Length > 256 || observation.CaptureId.Length > 256 ||
                observation.RequestId.Length > 256 || observation.AttemptId?.Length > 256)
                return Omitted(observation, "assembly_identity_limit");
            var now = _clock.GetTimestamp();
            foreach (var key in _active.Where(pair => _clock.GetElapsedTime(pair.Value.LastSeen, now).TotalSeconds > _limits.IdleSeconds)
                         .Select(pair => pair.Key).ToArray())
            {
                Remove(key);
                if (_expired.Count < _limits.MaxActiveStreams) _expired.Add(key);
            }
            var keyId = Identity(observation.CaptureId, observation.SourceEpoch, observation.RequestId, observation.AttemptId);
            if (_expired.Contains(keyId))
            {
                if (IsTerminal(observation)) _expired.Remove(keyId);
                return Omitted(observation, "assembly_expired");
            }
            if (!IsChunk(observation))
            {
                if (!IsTerminal(observation) || !_active.TryGetValue(keyId, out var buffered)) return observation;
                Remove(keyId);
                return observation.Body is { Length: > 0 } && observation.BodyEncoding is ("utf8_json" or "json" or "application/json")
                    ? observation : Finalize(observation, buffered);
            }
            if (observation.Body is not { Length: > 0 })
                return observation with { Body = null, BodyEncoding = null };
            if (!_active.TryGetValue(keyId, out var state))
            {
                if (_active.Count >= _limits.MaxActiveStreams)
                    return Omitted(observation, "assembly_capacity");
                state = new Assembly(_limits) { LastSeen = now };
                _active.Add(keyId, state);
            }
            if (observation.Sequence <= state.LastSequence) return Omitted(observation, "duplicate_or_out_of_order_fragment");
            state.LastSequence = observation.Sequence;
            state.LastSeen = now;
            if (state.Sources.Count >= _limits.MaxSourceRecords) state.Fail("assembly_source_limit");
            else state.Sources.Add(new(observation.SourceEpoch, observation.Sequence, observation.EventId));
            var count = (observation.Body?.Length ?? 0) + 256L +
                2L * (observation.SourceEpoch.Length + observation.EventId.Length);
            if (count + state.Bytes > _limits.MaxStreamBytes || count + _bytes > _limits.MaxTotalBytes)
            { state.Fail("assembly_byte_limit"); return Omitted(observation, state.Gap); }
            state.Bytes += count;
            _bytes += count;
            if (observation.GapReason is not null || observation.Truncated) state.Fail(observation.GapReason ?? "truncated_fragment");
            if (state.Gap is null && observation.Body is { Length: > 0 } bytes)
            {
                try { state.Push(bytes); }
                catch (JsonException) { state.Fail("invalid_stream_json"); }
                catch (DecoderFallbackException) { state.Fail("invalid_stream_utf8"); }
                catch (AssemblyLimitException) { state.Fail("assembly_frame_limit"); }
                catch (InvalidOperationException) { state.Fail("invalid_stream_shape"); }
            }
            if (IsTerminal(observation))
            {
                Remove(keyId);
                return Finalize(observation, state);
            }
            // Counts/source metadata survive; no raw or partially redacted fragment is persisted.
            return observation with { Body = null, BodyEncoding = null, Complete = false,
                GapReason = state.Gap ?? "stream_content_deferred" };
        }
    }

    private void Remove(string key)
    {
        if (_active.Remove(key, out var state)) _bytes -= state.Bytes;
    }
    private static SourceObservation Omitted(SourceObservation o, string? gap) => o with
        { Body = null, BodyEncoding = null, Complete = false, Truncated = true, GapReason = gap };
    private static SourceObservation Finalize(SourceObservation o, Assembly state)
    {
        if (state.Gap is not null) return Omitted(o, state.Gap);
        try { state.FinishFrames(); }
        catch (Exception e) when (e is JsonException or DecoderFallbackException or AssemblyLimitException or InvalidOperationException)
        { return Omitted(o, "invalid_stream_tail"); }
        var complete = state.Terminal && o.Complete && !o.Truncated && o.Kind is not ("attempt_failed" or "attempt_canceled");
        var envelope = state.Envelope(complete, complete ? null : o.GapReason ?? "partial_stream");
        // Complete describes a complete assembled representation. The envelope records inference partiality.
        return o with { Kind = "normalized_stream_body", BodyEncoding = "utf8_json", Body = JsonSerializer.SerializeToUtf8Bytes(envelope, Json),
            Complete = true, Truncated = false, GapReason = envelope.GapReason };
    }

    private sealed class Assembly(RecordingAssemblyLimits limits)
    {
        private readonly Decoder _decoder = new UTF8Encoding(false, true).GetDecoder();
        private readonly StringBuilder _frame = new();
        private readonly StringBuilder _line = new();
        private readonly SortedDictionary<int, Part> _parts = [];
        private readonly Dictionary<string, int> _itemIndices = new(StringComparer.Ordinal);
        private string? _event;
        private bool _previousCr;
        public readonly List<RecordingSourceReference> Sources = [];
        public long LastSeen;
        public long LastSequence = -1;
        public long Bytes;
        public string? Gap;
        public bool Terminal;
        private RecordingUsage? _usage;
        private string? _responseId;
        private string? _model;
        private string? _finish;
        private string? _partial;
        private JsonElement? _fullResponse;
        public void Fail(string? reason) { Gap ??= reason; _parts.Clear(); _frame.Clear(); _line.Clear(); }
        public void Push(byte[] bytes)
        {
            var chars = new char[4096];
            var offset = 0;
            while (offset < bytes.Length)
            {
                _decoder.Convert(bytes.AsSpan(offset), chars, false, out var read, out var written, out _);
                offset += read;
                for (var i = 0; i < written; i++) Character(chars[i]);
            }
        }
        private void Character(char c)
        {
            if (_previousCr && c == '\n') { _previousCr = false; return; }
            _previousCr = c == '\r';
            if (c is '\r' or '\n') { Line(); return; }
            _line.Append(c);
            if ((_line.Length + _frame.Length) * 4L > limits.MaxFrameBytes) throw new AssemblyLimitException();
        }
        private void Line()
        {
            var line = _line.ToString(); _line.Clear();
            if (line.Length == 0) { Dispatch(); return; }
            if (line.StartsWith(':')) return;
            var colon = line.IndexOf(':');
            var field = colon < 0 ? line : line[..colon];
            var value = colon < 0 ? "" : line[(colon + 1)..];
            if (value.StartsWith(' ')) value = value[1..];
            if (field == "event") _event = value;
            if (field == "data") { if (_frame.Length > 0) _frame.Append('\n'); _frame.Append(value); }
        }
        public void FinishFrames()
        {
            Span<char> end = stackalloc char[8];
            _decoder.Convert(ReadOnlySpan<byte>.Empty, end, true, out _, out var written, out _);
            for (var i = 0; i < written; i++) Character(end[i]);
            if (_line.Length > 0) Line();
            Dispatch();
        }
        private void Dispatch()
        {
            if (_frame.Length == 0) { _event = null; return; }
            var frame = _frame.ToString(); _frame.Clear();
            if (frame == "[DONE]") { Terminal = true; _event = null; return; }
            using var doc = JsonDocument.Parse(frame, new() { MaxDepth = 64 });
            var root = doc.RootElement;
            var type = S(root, "type") ?? _event;
            _event = null;
            _responseId ??= S(root, "id");
            _model ??= S(root, "model");
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                _usage = Merge(_usage, ReadUsage(usage, "stream"));
            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array) Chat(choices);
            else if (root.TryGetProperty("candidates", out var candidates)) Gemini(root, candidates);
            else if (type?.StartsWith("response.", StringComparison.Ordinal) == true) Responses(root, type);
            else if (type is "message_start" or "content_block_start" or "content_block_delta" or "content_block_stop" or "message_delta" or "message_stop" or "error") Anthropic(root, type);
            else if (type is not "ping") Fail("unsupported_stream_frame");
        }
        private Part Get(int index, string kind = "text", int choice = 0)
        {
            // Choice/output/block indices have separate protocol-local ordering; stable within one attempt.
            var scoped = checked(choice * 100000 + index);
            if (_parts.TryGetValue(scoped, out var p)) return p;
            if (_parts.Count >= limits.MaxParts) throw new AssemblyLimitException();
            p = new Part { Kind = kind }; _parts.Add(scoped, p); return p;
        }
        private void Chat(JsonElement choices)
        {
            foreach (var choice in choices.EnumerateArray())
            {
                var ci = (int)(N(choice, "index") ?? 0);
                if (ci is < 0 or > 1000) throw new AssemblyLimitException();
                if (S(choice, "finish_reason") is { } finish) { _finish = finish; Terminal = true; }
                if (!choice.TryGetProperty("delta", out var delta)) continue;
                if (S(delta, "content") is { } text) Get(0, choice: ci).Text.Append(text);
                if (S(delta, "refusal") is { } refusal) Get(1, "refusal", ci).Text.Append(refusal);
                if (!delta.TryGetProperty("tool_calls", out var calls) || calls.ValueKind != JsonValueKind.Array) continue;
                foreach (var call in calls.EnumerateArray())
                {
                    var index = (int)(N(call, "index") ?? -1);
                    if (index is < 0 or > 10000) throw new AssemblyLimitException();
                    var p = Get(index + 2, "tool_request", ci);
                    Set(ref p.CallId, S(call, "id"));
                    if (call.TryGetProperty("function", out var function))
                    { Set(ref p.Name, S(function, "name")); p.Arguments.Append(S(function, "arguments")); }
                }
            }
        }
        private void Responses(JsonElement root, string type)
        {
            if (root.TryGetProperty("response", out var response))
            {
                _responseId = S(response, "id") ?? _responseId; _model = S(response, "model") ?? _model;
                if (type is "response.completed" or "response.done" or "response.incomplete" && response.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array && output.GetArrayLength() > 0)
                    _fullResponse = response.Clone();
                if (response.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object) _usage = ReadUsage(usage, "responses");
            }
            var index = (int)(N(root, "output_index") ?? -1);
            if (index is < -1 or > 10000) throw new AssemblyLimitException();
            var itemId = S(root, "item_id");
            if (index < 0 && itemId is not null && _itemIndices.TryGetValue(itemId, out var known)) index = known;
            if (type == "response.output_item.added" && root.TryGetProperty("item", out var item))
            {
                if (index < 0) { Fail("missing_item_index"); return; }
                var id = S(item, "id"); if (id is not null) _itemIndices[id] = index;
                if (S(item, "type") is "function_call" or "custom_tool_call")
                {
                    var p = Get(index * 100, "tool_request"); p.NativeId = id; p.CallId = S(item, "call_id"); p.Name = S(item, "name");
                    p.Arguments.Append(S(item, "arguments") ?? RecordingProtocolNormalizer.Raw(item, "input"));
                }
            }
            else if (type is "response.output_text.delta" or "response.refusal.delta")
            {
                if (index < 0) { Fail("missing_item_index"); return; }
                var content = (int)(N(root, "content_index") ?? 0);
                if (content is < 0 or > 99) throw new AssemblyLimitException();
                var p = Get(index * 100 + content, type == "response.refusal.delta" ? "refusal" : "text");
                p.NativeId = itemId; p.Text.Append(S(root, "delta"));
            }
            else if (type is "response.function_call_arguments.delta" or "response.custom_tool_call_input.delta")
            {
                if (index < 0) { Fail("missing_item_index"); return; }
                Get(index * 100, "tool_request").Arguments.Append(S(root, "delta"));
            }
            else if (type is "response.completed" or "response.done") { Terminal = true; _finish = "completed"; }
            else if (type is "response.failed" or "response.incomplete" or "error") { _finish = type; _partial = "partial_stream"; }
            // Lifecycle and *.done frames duplicate accumulated bytes and are deliberately not appended.
        }
        private void Anthropic(JsonElement root, string type)
        {
            var index = (int)(N(root, "index") ?? 0);
            if (index is < 0 or > 10000) throw new AssemblyLimitException();
            if (type == "message_start" && root.TryGetProperty("message", out var message))
            {
                _responseId = S(message, "id"); _model = S(message, "model");
                if (message.TryGetProperty("usage", out var usage)) _usage = Merge(_usage, ReadUsage(usage, "anthropic"));
            }
            else if (type == "content_block_start" && root.TryGetProperty("content_block", out var block))
            {
                var p = Get(index, S(block, "type") == "tool_use" ? "tool_request" : "text");
                p.CallId = S(block, "id"); p.Name = S(block, "name"); p.Text.Append(S(block, "text"));
                var input = RecordingProtocolNormalizer.Raw(block, "input"); if (input is not (null or "{}")) p.Arguments.Append(input);
                if (S(block, "type") is not ("tool_use" or "text")) { p.Kind = "unsupported_content"; p.Complete = false; }
            }
            else if (type == "content_block_delta" && root.TryGetProperty("delta", out var delta))
            {
                if (S(delta, "type") == "text_delta") Get(index).Text.Append(S(delta, "text"));
                else if (S(delta, "type") == "input_json_delta") Get(index, "tool_request").Arguments.Append(S(delta, "partial_json"));
                else { Get(index, "unsupported_content").Complete = false; }
            }
            else if (type == "message_delta" && root.TryGetProperty("delta", out var stopDelta)) _finish = S(stopDelta, "stop_reason");
            else if (type == "message_stop") Terminal = true;
            else if (type == "error") _partial = "provider_stream_error";
        }
        private void Gemini(JsonElement root, JsonElement candidates)
        {
            if (candidates.ValueKind != JsonValueKind.Array) { Fail("invalid_stream_candidates"); return; }
            foreach (var candidate in candidates.EnumerateArray())
            {
                var ci = (int)(N(candidate, "index") ?? 0);
                if (ci is < 0 or > 1000) throw new AssemblyLimitException();
                if (S(candidate, "finishReason") is { } finish) { Terminal = true; _finish = finish; }
                if (!candidate.TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts)) continue;
                var index = 0;
                foreach (var part in parts.EnumerateArray())
                {
                    if (S(part, "text") is { } text && !Bool(part, "thought")) Get(index, choice: ci).Text.Append(text);
                    else if (part.TryGetProperty("functionCall", out var call))
                    {
                        var p = Get(index, "tool_request", ci); p.CallId = S(call, "id"); p.Name = S(call, "name"); p.Arguments.Append(RecordingProtocolNormalizer.Raw(call, "args"));
                    }
                    else { Get(index, "unsupported_content", ci).Complete = false; }
                    index++;
                }
            }
            if (root.TryGetProperty("usageMetadata", out var usage)) _usage = ReadUsage(usage, "gemini");
        }
        private void Set(ref string? field, string? value)
        {
            if (value is null) return;
            if (field is not null && field != value) { Fail("stream_identity_conflict"); return; }
            field = value;
        }
        public StreamEnvelope Envelope(bool complete, string? gap)
        {
            complete = complete && _partial is null;
            gap ??= _partial;
            var parts = _parts.Values.Select(p => new RecordingContentPart(p.Kind, p.Text.Length > 0 ? p.Text.ToString() : null,
                p.CallId, p.Name, p.Arguments.Length > 0 ? p.Arguments.ToString() : null,
                Evidence: p.Kind == "tool_request" ? "model_requested" : "observed", Complete: complete && p.Complete)).ToArray();
            return new([new("transient", "assistant", parts, Sources.ToArray(), Complete: complete, GapReason: gap)],
                _usage, _responseId, _finish, _model, complete, gap, Sources.ToArray(), FullResponse: _fullResponse);
        }
        private static RecordingUsage Merge(RecordingUsage? old, RecordingUsage next) => old is null ? next : new(
            next.Input ?? old.Input, next.Output ?? old.Output, next.Cached ?? old.Cached,
            next.Reasoning ?? old.Reasoning, next.Total ?? old.Total, next.Source);
        private sealed class Part
        {
            public string Kind = "text";
            public string? CallId, Name, NativeId;
            public bool Complete = true;
            public readonly StringBuilder Text = new(), Arguments = new();
        }
    }
    private sealed class AssemblyLimitException : Exception;
}
