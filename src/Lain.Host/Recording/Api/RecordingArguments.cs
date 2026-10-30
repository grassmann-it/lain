using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace Lain.Host.Recording.Api;

public static class RecordingArguments
{
    public static string Identity(string value)
    {
        if (value is not { Length: > 0 and <= 256 } || value is "." or ".." ||
            value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.' or ':' or '@' or '+')))
            throw new ArgumentException("Invalid recording identity.");
        return value;
    }
    public static void QueryKeys(HttpRequest request, params string[] allowed)
    {
        if (request.Query.Keys.Any(key => !allowed.Contains(key, StringComparer.Ordinal))) throw new ArgumentException("Unknown recording query.");
    }
    public static string? Value(HttpRequest request, string name, int maximum = 256)
    {
        if (!request.Query.TryGetValue(name, out var values)) return null;
        var value = values.Count == 1 ? values[0] : null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Any(char.IsControl)) throw new ArgumentException("Invalid recording query value.");
        return value;
    }
    public static int Limit(HttpRequest request, int maximum) => Value(request, "limit") is { } text &&
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= 1 && value <= maximum ? value :
        request.Query.ContainsKey("limit") ? throw new ArgumentException("Invalid recording page limit.") : Math.Min(50, maximum);
    public static long After(HttpRequest request) => Value(request, "after") is { } text &&
        long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= 0 ? value :
        request.Query.ContainsKey("after") ? throw new ArgumentException("Invalid recording cursor.") : 0;
}
