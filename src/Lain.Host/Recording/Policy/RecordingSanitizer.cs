using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lain.Core;

namespace Lain.Host.Recording.Policy;

/// <summary>Conservative full-document sanitizer. Streaming deltas must be assembled before this boundary.</summary>
public sealed class RecordingSanitizer
{
    private const string Redacted = "[REDACTED]";
    private static readonly TimeSpan RegexBudget = TimeSpan.FromMilliseconds(100);
    private static readonly Regex SecretField = Pattern(@"^(?:auth|authentication|authorization|proxy[-_]?authorization|cookie|set[-_]?cookie|.*(?:api[-_]?key|access[-_]?token|refresh[-_]?token|id[-_]?token|auth[-_]?token|password|passwd|secret|private[-_]?key|credential).*|token|key|headers|request[-_]?headers|response[-_]?headers|environment|env)$");
    private static readonly Regex[] SecretPatterns =
    [
        Pattern(@"-----BEGIN (?:[A-Z ]*PRIVATE KEY|OPENSSH PRIVATE KEY)-----[\s\S]*?(?:-----END [A-Z ]*PRIVATE KEY-----|$)"),
        Pattern(@"\b(?:Bearer|Basic)\s+[A-Za-z0-9+/_.=:\-]+"),
        Pattern(@"\beyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b"),
        Pattern(@"\b(?:sk-|gh[pousr]_|github_pat_|xox[baprs]-|AKIA|AIza)[A-Za-z0-9_\-]{6,}\b"),
        Pattern(@"(?im)\b[A-Z0-9_]*(?:API_KEY|TOKEN|SECRET|PASSWORD|PASSWD|PRIVATE_KEY|CREDENTIAL)[A-Z0-9_]*\s*[:=]\s*[^\s,;]+"),
        Pattern(@"(?i)\b(?:api[-_]?key|access[-_]?token|refresh[-_]?token|id[-_]?token|password|passwd|secret|cookie|authorization)[\""']*\s*[:=]\s*[\""']*[^\s,""';}]+"),
        Pattern(@"(?im)\b(?:Authorization|Proxy-Authorization|Set-Cookie|Cookie)\s*:\s*[^\r\n]+"),
        Pattern(@"https?://[^\s/@:]+:[^\s/@]+@[^\s]+"),
        Pattern(@"(?i)([?&](?:api[-_]?key|token|access_token|secret|password|auth|signature)=)[^&#\s]+")
    ];
    private static readonly Regex SecretFile = Pattern(@"(?:^|[/\\\s""'])(?:\.env(?:\.[\w.-]+)?|id_rsa|id_ed25519|credentials(?:\.json)?|service[-_]?account(?:\.json)?|\.netrc)(?:$|[/\\\s""':])");
    private static readonly Regex Identifier = new(@"\A[A-Za-z0-9][A-Za-z0-9._:/@+\-]{0,255}\z", RegexOptions.CultureInvariant, RegexBudget);

    private static Regex Pattern(string value) => new(value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexBudget);

    public SanitizedObservation? Sanitize(SourceObservation source, CapturePolicySnapshot policy)
    {
        if (!policy.Enabled || policy.Scope != RecordingContract.OwnedGatewayScope ||
            source.SchemaVersion != RecordingContract.Version || source.Sequence < 0 ||
            source.PolicyRevision != policy.Revision || source.StopGeneration != policy.StopGeneration ||
            (source.IdentityScope is not null && source.IdentityScope != policy.IdentityScope) ||
            (source.Kind != "source_gap" && policy.RouteIds is { Length: > 0 } routes && !routes.Contains(source.RouteId, StringComparer.Ordinal))) return null;
        // Never rewrite correlation IDs into misleading joins. Unsafe identity makes this observation inadmissible.
        if (!SafeId(source.SourceEpoch) || !SafeId(source.EventId) || !SafeId(source.CaptureId) || !SafeId(source.RequestId) ||
            !SafeOptionalId(source.AttemptId) || !SafeOptionalId(source.RouteId) || !SafeOptionalId(source.AccountId)) return null;

        var state = policy.Mode == RecordingContract.Content ? "no_body" : "metadata_only";
        string? body = null;
        string? encoding = null;
        string? gap = SafeCode(source.GapReason);
        var complete = source.Complete;
        if (policy.Mode == RecordingContract.Content && source.Body is { Length: > 0 } bytes)
        {
            if (bytes.Length > policy.EffectiveLimits.MaxRecordBytes)
            { state = "omitted_oversize"; gap = "body_oversize"; complete = false; }
            else if (!source.Complete || source.Truncated || IsFragment(source))
            { state = "omitted_fragment"; gap = "unassembled_fragment"; complete = false; }
            else if (source.BodyEncoding is not ("utf8_json" or "json" or "application/json"))
            { state = "omitted_unsupported"; gap = "unsupported_encoding"; complete = false; }
            else
            {
                try
                {
                    // Strict UTF-8 and JSON: no best-effort replacement or partially parsed retention.
                    var text = new UTF8Encoding(false, true).GetString(bytes);
                    var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = policy.EffectiveLimits.MaxBodyDepth });
                    var jsonTokens = 0;
                    while (reader.Read()) if (++jsonTokens > 200000) throw new BodyLimitException();
                    using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = policy.EffectiveLimits.MaxBodyDepth });
                    using var output = new LimitedMemoryStream(policy.EffectiveLimits.MaxRecordBytes);
                    using (var writer = new Utf8JsonWriter(output)) WriteSanitized(writer, document.RootElement);
                    if (output.Length > policy.EffectiveLimits.MaxRecordBytes)
                    { state = "omitted_oversize"; gap = "sanitized_body_oversize"; complete = false; }
                    else
                    {
                        body = Encoding.UTF8.GetString(output.ToArray());
                        encoding = "utf8_json";
                        // JSON is reserialized even when no secret matched: do not call it byte-original.
                        state = "sanitized";
                    }
                }
                catch (BodyLimitException)
                { state = "omitted_oversize"; gap = "sanitized_body_oversize"; complete = false; }
                catch (Exception ex) when (ex is JsonException or DecoderFallbackException or RegexMatchTimeoutException or ArgumentException)
                { state = "omitted_invalid"; gap = "redaction_failed"; complete = false; }
            }
        }
        return new(source.SourceEpoch, source.Sequence, source.EventId, source.CaptureId, source.RequestId,
            source.ObservedAt, SafeCode(source.Boundary) ?? "unknown", SafeCode(source.Protocol) ?? "unknown",
            SafeCode(source.Kind) ?? "unknown", policy.Revision, policy.StopGeneration,
            RecordingContract.RedactionVersion, state, source.AttemptId, source.RouteId, source.AccountId,
            SafeLabel(source.Provider), SafeLabel(source.Model), SafeLabel(source.RequestedModel),
            SanitizeDimensions(source.Dimensions), encoding, body, complete, source.Truncated, gap,
            source.StatusCode is >= 100 and <= 599 ? source.StatusCode : null, source.Tokens,
            source.MonotonicNanoseconds, IdentityScope: policy.IdentityScope);
    }

    private static bool IsFragment(SourceObservation s) =>
        s.BodyEncoding is "utf8_sse" or "sse" or "text/event-stream" ||
        s.Kind.Contains("delta", StringComparison.OrdinalIgnoreCase) ||
        s.Kind.Contains("chunk", StringComparison.OrdinalIgnoreCase) ||
        s.Kind.Contains("fragment", StringComparison.OrdinalIgnoreCase);

    public static string SanitizeText(string value)
    {
        if (SecretFile.IsMatch(value)) return "[REDACTED_SECRET_FILE]";
        foreach (var pattern in SecretPatterns) value = pattern.Replace(value, Redacted);
        return value;
    }

    private static void WriteSanitized(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    // Property names themselves can contain free-form secrets. Preserve no such key.
                    if (SanitizeText(property.Name) != property.Name) continue;
                    writer.WritePropertyName(property.Name);
                    if (SecretField.IsMatch(property.Name)) writer.WriteStringValue(Redacted);
                    else if (property.Name == "arguments" && property.Value.ValueKind == JsonValueKind.String)
                        WriteArguments(writer, property.Value.GetString()!);
                    else WriteSanitized(writer, property.Value);
                }
                writer.WriteEndObject(); break;
            case JsonValueKind.Array:
                writer.WriteStartArray(); foreach (var item in element.EnumerateArray()) WriteSanitized(writer, item); writer.WriteEndArray(); break;
            case JsonValueKind.String: writer.WriteStringValue(SanitizeText(element.GetString()!)); break;
            default: element.WriteTo(writer); break;
        }
    }

    private static void WriteArguments(Utf8JsonWriter writer, string arguments)
    {
        try
        {
            using var document = JsonDocument.Parse(arguments, new JsonDocumentOptions { MaxDepth = 64 });
            using var output = new MemoryStream();
            using (var nested = new Utf8JsonWriter(output)) WriteSanitized(nested, document.RootElement);
            writer.WriteStringValue(Encoding.UTF8.GetString(output.ToArray()));
        }
        catch (JsonException) { writer.WriteStringValue("[OMITTED_INVALID_TOOL_ARGUMENTS]"); }
    }

    private static bool SafeId(string value) => Identifier.IsMatch(value) && SanitizeText(value) == value;
    private static bool SafeOptionalId(string? value) => value is null || SafeId(value);
    private static string? SafeLabel(string? value) => value is null || value.Length > 256 ? null : SanitizeText(value);
    private static string? SafeCode(string? value) => value is not null && value.Length <= 80 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or '/') && SanitizeText(value) == value ? value : null;
    private static string? DimensionId(string? value) => value is not null && SafeId(value) ? value : null;
    private static AttemptDimensionsDto? SanitizeDimensions(AttemptDimensionsDto? d) => d is null ? null : new(
        SafeLabel(d.ModelFamily), SafeLabel(d.Manufacturer), SafeLabel(d.InferenceProvider), SafeLabel(d.Gateway),
        DimensionId(d.BillingAccountId), SafeCode(d.ReasoningEffort), SafeCode(d.Mode), DimensionId(d.ConversationId),
        DimensionId(d.TurnId), DimensionId(d.WorkerId), DimensionId(d.ParentWorkerId), DimensionId(d.ParentRequestId),
        DimensionId(d.ToolCallId), SafeLabel(d.Harness), DimensionId(d.ProfileId),
        d.Evidence?.Take(32).Select(e => new DimensionEvidenceDto(SafeCode(e.Field) ?? "unknown",
            SafeCode(e.Source) ?? "unknown", SafeCode(e.Key))).ToArray(), DimensionId(d.ResponseId),
        DimensionId(d.PreviousResponseId), SafeCode(d.EffectiveReasoningEffort));
    private sealed class BodyLimitException : IOException { }
    private sealed class LimitedMemoryStream(int limit) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        { if (Length + count > limit) throw new BodyLimitException(); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer)
        { if (Length + buffer.Length > limit) throw new BodyLimitException(); base.Write(buffer); }
    }

}
