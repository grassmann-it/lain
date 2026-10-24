using System.Text.Json;
using System.Text.RegularExpressions;
using Lain.Core;

namespace Lain.Host.Quota;

/// <summary>Published offer text, never a measured account quantity.</summary>
public sealed record NvidiaBuildPublicLimits(string RequestsPerMinute, string RequestsPerDay);

/// <summary>Offline source normalization and dated product guidance; no polling or authentication.</summary>
public static class NvidiaBuildPublicReference
{
    public const string PublicSource = "https://build.nvidia.com/api/runtime/content/copy.yaml";
    public const string ConsoleUrl = "https://build.nvidia.com/";
    public const string ReviewedSourceDate = "2026-10-24";

    // Despite its suffix the official runtime route returns JSON. Preserve "Up to"
    // and the provider's units; these strings are never numerical quota readings.
    public static NvidiaBuildPublicLimits? ReadPublicLimits(JsonElement root)
    {
        var limits = JsonFields.Object(root, "rateLimits");
        var minute = JsonFields.String(limits, "requestsPerMinute");
        var day = JsonFields.String(limits, "requestsPerDay");
        return minute is { Length: <= 64 } && day is { Length: <= 64 }
            && Regex.IsMatch(minute, @"\A(?:Up to )?[0-9][0-9,]* rpm\z", RegexOptions.CultureInvariant)
            && Regex.IsMatch(day, @"\A(?:Up to )?[0-9][0-9,]* requests per day\z", RegexOptions.CultureInvariant)
            ? new(minute, day) : null;
    }

    // The source date is fixed evidence provenance, not a successful quota read
    // timestamp. No credential/setup change is promised to unlock account data.
    public static QuotaSupportDto Support() => new("console",
        "Published product reference (2026-10-24): Up to 40 rpm; 10,000 requests per day. Limits vary by model and shared traffic. "
        + "Account usage, remaining requests and reset time are unknown. No supported NVIDIA Build account reporting or delegated login is verified for lain.",
        ConsoleUrl);
}
