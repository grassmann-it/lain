using System.Text;
using System.Text.RegularExpressions;
using Lain.Core;

namespace Lain.Host.Inspector;

/// <summary>
/// Allowlisted display content only. Bounded holdback catches credential markers split
/// across deltas; arbitrary prose cannot be classified as a secret by pattern matching.
/// Tool arguments/results, prompts and raw frames are never passed to this sanitizer.
/// </summary>
internal sealed class InspectorTextRedactor
{
    private const int Holdback = 128;
    private const int MaximumInputChars = 8192;
    private static readonly Regex Marker = new(
        @"(?i)(?:authorization[ \t]{0,8}[:=][ \t]{0,8}[""']?[ \t]{0,8}(?:bearer[ \t]{1,8})?|bearer[ \t]{1,8}|(?:api[_-]?key|access[_-]?token|refresh[_-]?token|client[_-]?secret)[ \t]{0,8}[""']?[ \t]{0,8}[:=][ \t]{0,8}[""']?)",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(50));
    private static readonly Regex Credential = new(
        @"(?i)\b(?:sk[-_][a-z0-9_-]{4,}|AIza[a-z0-9_-]{8,}|gh[pousr]_[a-z0-9]{8,}|xox[baprs]-[a-z0-9-]{6,}|eyJ[a-z0-9_-]{4,}\.[a-z0-9_.-]*)",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(50));
    private string pending = "";
    private bool discardCredential;
    private bool discardLine;

    public bool HasPending => pending.Length != 0;
    public DateTimeOffset? LastObservedAt { get; private set; }

    public (string? Text, bool Removed) Push(string? value, DateTimeOffset at, bool final = false)
    {
        if (value?.Length > MaximumInputChars)
        {
            ResetAfterGap();
            return (null, true);
        }
        LastObservedAt = value is null ? LastObservedAt : at;
        var input = Inert(value ?? "");
        // A credential can be arbitrarily long. Drop its continuation until a delimiter;
        // never keep the secret itself or emit its suffix after a split delta.
        if (discardCredential)
        {
            var end = discardLine ? input.IndexOf('\n') : FindDelimiter(input, 0);
            if (end < 0) input = "";
            else { input = input[end..]; discardCredential = discardLine = false; }
        }
        pending += input;
        var marker = Marker.Match(pending);
        while (marker.Success)
        {
            var start = marker.Index + marker.Length;
            if (start == pending.Length && !final) break;
            var wholeLine = pending.AsSpan(marker.Index).StartsWith("authorization", StringComparison.OrdinalIgnoreCase);
            var end = wholeLine ? pending.IndexOf('\n', start) : FindDelimiter(pending, start);
            if (end < 0) { end = pending.Length; discardCredential = !final; discardLine = wholeLine; }
            pending = pending[..marker.Index] + "[redacted]" + pending[end..];
            marker = Marker.Match(pending);
        }
        var credential = Credential.Match(pending);
        while (credential.Success)
        {
            var end = FindDelimiter(pending, credential.Index);
            if (end < 0) { end = pending.Length; discardCredential = !final; }
            pending = pending[..credential.Index] + "[redacted]" + pending[end..];
            credential = Credential.Match(pending);
        }
        var length = final ? pending.Length : Math.Max(0, pending.Length - Holdback);
        if (length > 0 && length < pending.Length && char.IsHighSurrogate(pending[length - 1])) length--;
        var result = length == 0 ? null : pending[..length];
        pending = pending[length..];
        if (final) discardCredential = discardLine = false;
        return (result, false);
    }

    public void Clear() { pending = ""; discardCredential = discardLine = false; LastObservedAt = null; }
    public void ResetAfterGap() { Clear(); discardCredential = discardLine = true; }

    public static string? Metadata(string? value, int maximum = 256)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || Marker.IsMatch(value) || Credential.IsMatch(value)) return null;
        var clean = Inert(value);
        return clean.Length == 0 ? null : clean;
    }

    public static string? Identity(string? value) => Metadata(value) is { } clean &&
        clean.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.' or ':' or '/' or '@') ? clean : null;

    private static int FindDelimiter(string value, int start)
    {
        for (var i = start; i < value.Length; i++)
            if (char.IsWhiteSpace(value[i]) || value[i] is '"' or '\'' or '`' or ',' or ';' or '}' or ']') return i;
        return -1;
    }

    private static string Inert(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (rune.Value is '\n' or '\t' || category is not (System.Globalization.UnicodeCategory.Control or System.Globalization.UnicodeCategory.Format or System.Globalization.UnicodeCategory.Surrogate))
                result.Append(rune.ToString());
        }
        return result.ToString();
    }
}

internal static class InspectorDimensions
{
    private static readonly HashSet<string> Sources = new(StringComparer.Ordinal) { "route_binding", "harness_header", "request_body", "provider_response", "core" };
    private static readonly HashSet<string> IdentityFields = new(StringComparer.Ordinal)
    {
        "conversationId", "turnId", "workerId", "parentWorkerId", "parentRequestId", "toolCallId", "profileId", "responseId", "previousResponseId", "billingAccountId"
    };

    private static Dictionary<string, string?> Fields(AttemptDimensionsDto value) => new(StringComparer.Ordinal)
    {
        ["modelFamily"] = value.ModelFamily, ["manufacturer"] = value.Manufacturer, ["inferenceProvider"] = value.InferenceProvider,
        ["gateway"] = value.Gateway, ["billingAccountId"] = value.BillingAccountId, ["reasoningEffort"] = value.ReasoningEffort,
        ["mode"] = value.Mode, ["conversationId"] = value.ConversationId, ["turnId"] = value.TurnId, ["workerId"] = value.WorkerId,
        ["parentWorkerId"] = value.ParentWorkerId, ["parentRequestId"] = value.ParentRequestId, ["toolCallId"] = value.ToolCallId,
        ["harness"] = value.Harness, ["profileId"] = value.ProfileId, ["responseId"] = value.ResponseId, ["previousResponseId"] = value.PreviousResponseId,
        ["effectiveReasoningEffort"] = value.EffectiveReasoningEffort
    };

    public static AttemptDimensionsDto? Merge(AttemptDimensionsDto? previous, AttemptDimensionsDto? supplied, HashSet<string> conflicts)
    {
        if (supplied is null) return previous;
        var incoming = Fields(supplied);
        var retained = Fields(previous ?? new());
        var evidence = (previous?.Evidence ?? []).ToDictionary(item => item.Field, item => item, StringComparer.Ordinal);
        foreach (var proof in (supplied.Evidence ?? []).Take(64))
        {
            if (proof is null) continue;
            var field = incoming.Keys.FirstOrDefault(key => key.Equals(proof.Field, StringComparison.OrdinalIgnoreCase));
            var source = InspectorTextRedactor.Metadata(proof.Source, 96);
            if (field is null || source is null || !Sources.Contains(source) || conflicts.Contains(field)) continue;
            var value = IdentityFields.Contains(field) ? InspectorTextRedactor.Identity(incoming[field]) : InspectorTextRedactor.Metadata(incoming[field], 160);
            if (value is null) continue;
            if (retained[field] is { } known && known != value)
            {
                // Conflicting asserted IDs do not silently move a request to another worker/chat.
                conflicts.Add(field); retained[field] = null; evidence.Remove(field); continue;
            }
            retained[field] = value;
            evidence.TryAdd(field, new(field, source, InspectorTextRedactor.Metadata(proof.Key, 96)));
        }
        foreach (var field in conflicts) { retained[field] = null; evidence.Remove(field); }
        if (retained.Values.All(value => value is null)) return null;
        string? Get(string name) => retained[name];
        return new(Get("modelFamily"), Get("manufacturer"), Get("inferenceProvider"), Get("gateway"), Get("billingAccountId"),
            Get("reasoningEffort"), Get("mode"), Get("conversationId"), Get("turnId"), Get("workerId"), Get("parentWorkerId"),
            Get("parentRequestId"), Get("toolCallId"), Get("harness"), Get("profileId"), evidence.Values.ToArray(), Get("responseId"), Get("previousResponseId"), Get("effectiveReasoningEffort"));
    }
}
