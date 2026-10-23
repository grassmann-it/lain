using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Lain.Host.Providers;

/// <summary>One response's observed window; never a money balance or an inferred shared model budget.</summary>
public sealed record GroqRateWindow(string Kind, string Unit, string Window,
    long? Limit, long? Remaining, DateTimeOffset? ResetsAt);
public sealed record GroqRateLimitObservation(DateTimeOffset ObservedAt, GroqRateWindow[] Windows,
    TimeSpan? RetryAfter = null);

/// <summary>Passive parser only. No quota probing or inference calls are made to obtain headers.</summary>
public static class GroqRateLimitHeaders
{
    private static readonly Regex Duration = new(@"\G(\d+(?:\.\d+)?)(ms|d|h|m|s)", RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public static GroqRateLimitObservation Parse(HttpResponseMessage response, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(response);
        var windows = new List<GroqRateWindow>();
        foreach (var (suffix, kind, unit, window) in new[]
        {
            ("requests", "requests_per_day", "requests", "day"),
            ("tokens", "tokens_per_minute", "tokens", "minute")
        })
        {
            var limit = Number(Header(response, "x-ratelimit-limit-" + suffix));
            var remaining = Number(Header(response, "x-ratelimit-remaining-" + suffix));
            if (limit.HasValue && remaining > limit) remaining = null;
            var duration = ParseDuration(Header(response, "x-ratelimit-reset-" + suffix));
            DateTimeOffset? reset = null;
            if (duration.HasValue)
                try { reset = observedAt + duration.Value; } catch (ArgumentOutOfRangeException) { }
            if (limit.HasValue || remaining.HasValue || reset.HasValue)
                windows.Add(new(kind, unit, window, limit, remaining, reset));
        }
        TimeSpan? retryAfter = null;
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retry = Header(response, "retry-after");
            var seconds = Number(retry);
            if (seconds is >= 0 and <= 31536000) retryAfter = TimeSpan.FromSeconds(seconds.Value);
            else if (DateTimeOffset.TryParseExact(retry, "r", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var when) && when >= observedAt)
                retryAfter = when - observedAt;
        }
        return new(observedAt, windows.ToArray(), retryAfter);
    }

    private static string? Header(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out var values)) return null;
        using var enumerator = values.GetEnumerator();
        if (!enumerator.MoveNext()) return null;
        var first = enumerator.Current;
        // Ambiguous duplicate headers are not a reliable numeric observation.
        return enumerator.MoveNext() ? null : first;
    }
    private static long? Number(string? value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
        && parsed >= 0 ? parsed : null;

    internal static TimeSpan? ParseDuration(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 100) return null;
        double milliseconds = 0;
        var offset = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            while (offset < value.Length)
            {
                var match = Duration.Match(value, offset);
                if (!match.Success || match.Index != offset || !seen.Add(match.Groups[2].Value)
                    || !double.TryParse(match.Groups[1].Value, NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out var amount) || !double.IsFinite(amount)) return null;
                milliseconds += amount * (match.Groups[2].Value switch { "d" => 86400000, "h" => 3600000, "m" => 60000, "s" => 1000, _ => 1 });
                if (!double.IsFinite(milliseconds) || milliseconds > 31536000000) return null;
                offset += match.Length;
            }
            return TimeSpan.FromMilliseconds(milliseconds);
        }
        catch (RegexMatchTimeoutException) { return null; }
    }
}
