using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Recording.Normalization;

public sealed record RecordingNormalizerLimits(int MaxBodyBytes = 16 * 1024 * 1024,
    int MaxMessages = 4096, int MaxParts = 16384, int MaxDepth = 64);

/// <summary>Pure, versioned semantic projection of already screened source records. Never reads wire/auth state.</summary>
public sealed class RecordingProtocolNormalizer(RecordingNormalizerLimits? limits = null) : IRecordingNormalizer
{
    public const int Version = 1;
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly RecordingNormalizerLimits _limits = limits ?? new();
    private static readonly HashSet<string> EvidenceSources = new(StringComparer.Ordinal)
        { "route_binding", "harness_header", "request_body", "provider_response", "core" };

    public void Reset() { } // Each retained observation is self-contained; stream state lives only in the pre-policy assembler.

    public IReadOnlyList<RecordingNormalizedRecord> Normalize(CommittedObservation committed)
    {
        var o = committed.Observation;
        var source = new RecordingSourceReference(o.SourceEpoch, o.Sequence, o.EventId);
        string? Dim(string field, string? value) => o.Dimensions?.Evidence?.Any(e =>
            e.Field == field && EvidenceSources.Contains(e.Source)) == true ? value : null;
        var d = o.Dimensions;
        var record = new RecordingNormalizedRecord(Identity(o.CaptureId, o.SourceEpoch, o.EventId), o.CaptureId,
            o.RequestId, o.AttemptId, o.ObservedAt, o.Protocol, [], [source],
            ConversationId: Dim("conversationId", d?.ConversationId), WorkerId: Dim("workerId", d?.WorkerId),
            ParentWorkerId: Dim("parentWorkerId", d?.ParentWorkerId), ParentRequestId: Dim("parentRequestId", d?.ParentRequestId),
            Model: o.Model, RequestedModel: o.RequestedModel, RouteId: o.RouteId, Provider: o.Provider, AccountId: o.AccountId,
            ResponseId: Dim("responseId", d?.ResponseId), PreviousResponseId: Dim("previousResponseId", d?.PreviousResponseId),
            Usage: o.Tokens is { } t ? new(t.Input, t.Output, t.Cached, t.Reasoning, t.Total, "source_reported") : null,
            Complete: o.Complete && !o.Truncated && o.GapReason is null, GapReason: o.GapReason,
            ReasoningEffort: Dim("reasoningEffort", d?.ReasoningEffort),
            EffectiveReasoningEffort: Dim("effectiveReasoningEffort", d?.EffectiveReasoningEffort), IdentityScope: o.IdentityScope);
        if (o.Body is null)
        {
            if (o.RedactionState == "no_body" && o.GapReason is null && o.Kind is
                "attempt_started" or "attempt_finished" or "request_started" or "metadata" or "chunk_metadata" or "usage" or "response_complete") return [record];
            return [record with { Complete = false, GapReason = o.GapReason ?? "content_unavailable" }];
        }
        if (o.RedactionState is not ("sanitized" or "redacted" or "clean"))
            return [record with { Complete = false, GapReason = "content_not_screened" }];
        if (Encoding.UTF8.GetByteCount(o.Body) > _limits.MaxBodyBytes)
            return [record with { Complete = false, GapReason = "normalization_limit" }];
        try
        {
            using var doc = JsonDocument.Parse(o.Body, new() { MaxDepth = _limits.MaxDepth });
            if (o.Kind == "normalized_stream_body" && doc.RootElement.TryGetProperty("recordingNormalizationVersion", out var version) && version.ValueKind == JsonValueKind.Number && version.GetInt32() == Version)
            {
                var assembled = doc.RootElement.Deserialize<StreamEnvelope>(Json);
                if (assembled is null || assembled.Messages is null || assembled.Sources?.Length > 8192 ||
                    assembled.Messages.Length > _limits.MaxMessages || assembled.Messages.Any(m => m is null || m.Parts is null) ||
                    assembled.Messages.Sum(m => (long)m.Parts.Length) > _limits.MaxParts) throw new NormalizationLimitException();
                if (assembled.FullResponse is { ValueKind: JsonValueKind.Object } fullResponse)
                {
                    var normalized = new BodyReader(record, source, _limits).Read(fullResponse);
                    var sources = (assembled.Sources ?? []).Append(source).Distinct().ToArray();
                    return [normalized with { Sources = sources, Messages = normalized.Messages.Select(m => m with { Sources = sources }).ToArray(),
                        Complete = normalized.Complete && assembled.Complete, GapReason = assembled.GapReason ?? normalized.GapReason }];
                }
                return [record with { Messages = assembled.Messages.Select((m, i) => m with
                    { Id = record.Id + ":message:" + i, Sources = (assembled.Sources ?? []).Append(source).Distinct().ToArray(), Complete = m.Complete && record.Complete }).ToArray(),
                    Sources = (assembled.Sources ?? []).Append(source).Distinct().ToArray(),
                    Usage = assembled.Usage ?? record.Usage, ResponseId = assembled.ResponseId ?? record.ResponseId,
                    FinishReason = assembled.FinishReason, Model = assembled.Model ?? record.Model,
                    Complete = record.Complete && assembled.Complete, GapReason = assembled.GapReason ?? record.GapReason }];
            }
            if (o.Kind is "run_manifest" or "run_annotation") return [record];
            if (o.Protocol.ToLowerInvariant() is not ("responses" or "openai-responses" or "chat" or "chat_completions" or
                "openai-chat" or "openai" or "anthropic" or "claude" or "gemini"))
                return [record with { Complete = false, GapReason = "unsupported_protocol" }];
            var reader = new BodyReader(record, source, _limits, o.Dimensions?.Evidence,
                o.Boundary is "client_ingress" or "normalized_request" or "provider_request");
            return [reader.Read(doc.RootElement)];
        }
        catch (JsonException) { return [record with { Complete = false, GapReason = "invalid_json" }]; }
        catch (InvalidOperationException) { return [record with { Complete = false, GapReason = "invalid_protocol_shape" }]; }
        catch (NormalizationLimitException) { return [record with { Messages = [], Complete = false, GapReason = "normalization_limit" }]; }
    }

    internal static string Identity(params string?[] parts) => "rec_" + Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(parts))));

    internal sealed record StreamEnvelope(RecordingMessage[] Messages, RecordingUsage? Usage = null,
        string? ResponseId = null, string? FinishReason = null, string? Model = null,
        bool Complete = false, string? GapReason = null, RecordingSourceReference[]? Sources = null, int RecordingNormalizationVersion = Version, JsonElement? FullResponse = null);
    private sealed class NormalizationLimitException : Exception;

    private sealed class BodyReader(RecordingNormalizedRecord initial, RecordingSourceReference source, RecordingNormalizerLimits limits, DimensionEvidenceDto[]? evidence = null, bool isRequest = false)
    {
        private RecordingNormalizedRecord _record = initial;
        private readonly List<RecordingMessage> _messages = [];
        private int _parts;
        private bool _unsupported;
        public RecordingNormalizedRecord Read(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object) return _record with { Complete = false, GapReason = "unsupported_body" };
            if (root.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object &&
                evidence?.Any(e => e.Field == "parentConversationId" && EvidenceSources.Contains(e.Source)) == true)
                _record = _record with { ParentConversationId = S(metadata, "parent_conversation_id") };
            // Input occurrences are intentionally kept for each request; equal text is never a deduplication key.
            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
                foreach (var m in messages.EnumerateArray()) Message(m);
            else if (root.TryGetProperty("input", out var input)) Input(input);
            else if (root.TryGetProperty("contents", out var contents) && contents.ValueKind == JsonValueKind.Array)
                foreach (var m in contents.EnumerateArray()) GeminiMessage(m);
            if (root.TryGetProperty("instructions", out var instructions) && instructions.ValueKind == JsonValueKind.String)
                Add("developer", [new("text", instructions.GetString())]);
            if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
                foreach (var m in output.EnumerateArray()) Item(m);
            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
                foreach (var choice in choices.EnumerateArray())
                {
                    if (choice.TryGetProperty("message", out var m)) Message(m);
                    else if (choice.TryGetProperty("delta", out _)) _unsupported = true; // raw fragments are not safe durable content.
                    Finish(S(choice, "finish_reason"));
                }
            if (root.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array)
                foreach (var candidate in candidates.EnumerateArray())
                {
                    if (candidate.TryGetProperty("content", out var content)) GeminiMessage(content);
                    Finish(S(candidate, "finishReason"));
                }
            if (S(root, "type") == "message" && root.TryGetProperty("content", out _)) Message(root);
            if (root.TryGetProperty("usage", out var usage)) _record = _record with { Usage = ReadUsage(usage, _record.Protocol) };
            if (root.TryGetProperty("usageMetadata", out usage)) _record = _record with { Usage = ReadUsage(usage, "gemini") };
            _record = _record with { ResponseId = _record.ResponseId ?? (isRequest ? null : S(root, "id")),
                PreviousResponseId = S(root, "previous_response_id") ?? _record.PreviousResponseId,
                Model = isRequest ? _record.Model : S(root, "model") ?? S(root, "modelVersion") ?? _record.Model,
                RequestedModel = isRequest ? S(root, "model") ?? _record.RequestedModel : _record.RequestedModel };
            var status = S(root, "status");
            Finish(S(root, "stop_reason") ?? status);
            if (status is "incomplete" or "failed" or "cancelled" or "canceled" || root.TryGetProperty("error", out _))
                _record = _record with { Complete = false, GapReason = status ?? "provider_error" };
            return _record with { Messages = _messages.ToArray(), Complete = _record.Complete && !_unsupported,
                GapReason = _record.GapReason ?? (_unsupported ? "unsupported_content" : null) };
        }
        private void Input(JsonElement input)
        {
            if (input.ValueKind == JsonValueKind.String) Add("user", [new("text", input.GetString())]);
            else if (input.ValueKind == JsonValueKind.Array) foreach (var item in input.EnumerateArray()) Item(item);
            else _unsupported = true;
        }
        private void Item(JsonElement item)
        {
            switch (S(item, "type"))
            {
                case "function_call": case "custom_tool_call":
                    Add("assistant", [new("tool_request", ToolCallId: S(item, "call_id") ?? S(item, "id"),
                        ToolName: S(item, "name"), Arguments: S(item, "arguments") ?? Raw(item, "input"), Evidence: "model_requested")], S(item, "id")); break;
                case "function_call_output": case "custom_tool_call_output":
                    Add("tool", [new("tool_result", ToolCallId: S(item, "call_id"), Result: Raw(item, "output"), Evidence: "client_reported")], S(item, "id")); break;
                case "compaction":
                    Add("system", [new("compaction", Complete: false)], S(item, "id"));
                    _record = _record with { Complete = false, GapReason = "compacted_prefix_unavailable" }; break;
                case "reasoning": // No hidden reasoning/signature reconstruction.
                    Add("assistant", [new("unsupported_reasoning", Complete: false)], S(item, "id")); _unsupported = true; break;
                default: Message(item); break;
            }
        }
        private void Message(JsonElement m)
        {
            var role = S(m, "role") ?? "unknown";
            var parts = new List<RecordingContentPart>();
            if (m.TryGetProperty("content", out var content)) Content(content, parts);
            if (m.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
                foreach (var call in calls.EnumerateArray())
                {
                    if (call.TryGetProperty("function", out var function)) parts.Add(new("tool_request", ToolCallId: S(call, "id"),
                        ToolName: S(function, "name"), Arguments: S(function, "arguments"), Evidence: "model_requested"));
                    else { parts.Add(new("unsupported_tool", Complete: false)); _unsupported = true; }
                }
            if (S(m, "refusal") is { } refusal) parts.Add(new("refusal", refusal));
            if (role == "tool")
            {
                var text = content.ValueKind == JsonValueKind.String ? content.GetString() : Raw(m, "content");
                parts = [new("tool_result", ToolCallId: S(m, "tool_call_id"), Result: text, Evidence: "client_reported")];
            }
            if (parts.Count > 0) Add(role, parts.ToArray(), S(m, "id"));
            else if (S(m, "type") != "message") _unsupported = true;
        }
        private void Content(JsonElement content, List<RecordingContentPart> parts)
        {
            if (content.ValueKind == JsonValueKind.String) { parts.Add(new("text", content.GetString())); return; }
            if (content.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return;
            if (content.ValueKind != JsonValueKind.Array) { _unsupported = true; return; }
            foreach (var part in content.EnumerateArray())
            {
                switch (S(part, "type"))
                {
                    case "text": case "input_text": case "output_text": parts.Add(new("text", S(part, "text"))); break;
                    case "refusal": parts.Add(new("refusal", S(part, "refusal"))); break;
                    case "tool_use": parts.Add(new("tool_request", ToolCallId: S(part, "id"), ToolName: S(part, "name"),
                        Arguments: Raw(part, "input"), Evidence: "model_requested")); break;
                    case "tool_result": parts.Add(new("tool_result", ToolCallId: S(part, "tool_use_id"), Result: Raw(part, "content"), Evidence: "client_reported")); break;
                    default: parts.Add(new("unsupported_content", Complete: false)); _unsupported = true; break;
                }
            }
        }
        private void GeminiMessage(JsonElement m)
        {
            var parts = new List<RecordingContentPart>();
            if (m.TryGetProperty("parts", out var items) && items.ValueKind == JsonValueKind.Array)
                foreach (var part in items.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var text) && !Bool(part, "thought")) parts.Add(new("text", text.GetString()));
                    else if (part.TryGetProperty("functionCall", out var call)) parts.Add(new("tool_request", ToolCallId: S(call, "id"),
                        ToolName: S(call, "name"), Arguments: Raw(call, "args"), Evidence: "model_requested"));
                    else if (part.TryGetProperty("functionResponse", out var result)) parts.Add(new("tool_result", ToolCallId: S(result, "id"),
                        ToolName: S(result, "name"), Result: Raw(result, "response"), Evidence: "client_reported"));
                    else { parts.Add(new("unsupported_content", Complete: false)); _unsupported = true; }
                }
            Add(S(m, "role") == "model" ? "assistant" : S(m, "role") ?? "unknown", parts.ToArray());
        }
        private void Add(string role, RecordingContentPart[] parts, string? native = null)
        {
            _parts += parts.Length;
            if (_messages.Count >= limits.MaxMessages || _parts > limits.MaxParts) throw new NormalizationLimitException();
            _messages.Add(new(_record.Id + ":message:" + _messages.Count, role, parts, [source], native,
                Complete: _record.Complete && parts.All(p => p.Complete)));
        }
        private void Finish(string? reason) { if (reason is not null) _record = _record with { FinishReason = reason }; }
    }

    internal static string? S(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    internal static string? Raw(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) ? p.ValueKind == JsonValueKind.String ? p.GetString() : p.GetRawText() : null;
    internal static bool Bool(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
    internal static long? N(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var n) && n >= 0 ? n : null;
    internal static RecordingUsage ReadUsage(JsonElement u, string protocol)
    {
        long? Nested(string parent, string name) => u.ValueKind == JsonValueKind.Object && u.TryGetProperty(parent, out var p) ? N(p, name) : null;
        return new(N(u, "input_tokens") ?? N(u, "prompt_tokens") ?? N(u, "promptTokenCount"),
            N(u, "output_tokens") ?? N(u, "completion_tokens") ?? N(u, "candidatesTokenCount"),
            N(u, "cache_read_input_tokens") ?? Nested("input_tokens_details", "cached_tokens") ?? Nested("prompt_tokens_details", "cached_tokens") ?? N(u, "cachedContentTokenCount"),
            Nested("output_tokens_details", "reasoning_tokens") ?? Nested("completion_tokens_details", "reasoning_tokens") ?? N(u, "thoughtsTokenCount"),
            N(u, "total_tokens") ?? N(u, "totalTokenCount"), protocol + ":reported");
    }
}
