using Lain.Core;

namespace Lain.Host.Recording.Api;

/// <summary>Pure UTF-16 source windows. Source identity and retained metadata are copied without interpretation.</summary>
internal static class RecordingSourcePaging
{
    internal const int MaximumCharacters = 32768;

    /// <summary>
    /// Returns false for a cursor beyond retained text. Surrogate pairs stay atomic; a one-unit
    /// request may return two units so a nonterminal page always advances within the absolute cap.
    /// </summary>
    internal static bool TryCreate(CommittedObservation record, RecordingSourceReference source,
        int offset, int characters, out RecordingSourceTextPage? page)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (characters is < 1 or > MaximumCharacters) throw new ArgumentOutOfRangeException(nameof(characters));
        var observation = record.Observation;
        var text = observation.Body;
        var length = text?.Length ?? 0;
        page = null;
        if (offset > length) return false;
        if (text is not null && offset > 0 && offset < length &&
            char.IsLowSurrogate(text[offset]) && char.IsHighSurrogate(text[offset - 1])) offset--;
        var count = Math.Min(characters, length - offset);
        if (text is not null && count > 0 && offset + count < length &&
            char.IsHighSurrogate(text[offset + count - 1]) && char.IsLowSurrogate(text[offset + count]))
            count = count == 1 ? 2 : count - 1;
        page = new(source, observation.Boundary, observation.Protocol, text?.Substring(offset, count),
            offset, length, offset + count < length, observation.RedactionState, record.ContentHash, observation.GapReason);
        return true;
    }
}
