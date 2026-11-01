using System.Net;
using System.Text.Json;

namespace Lain.Host.Operations;

public sealed record ProxyHealthObservation(bool Ready, string Reason)
{
    public static async Task<ProxyHealthObservation> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return new(false, "Core management health authentication was refused.");
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.ServiceUnavailable)
            return new(false, "Core management health returned an unexpected HTTP status.");
        // Never buffer arbitrary error pages or forward any response text. Only
        // the documented boolean fields can affect the fixed diagnostic below.
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        var bytes = new byte[4097];
        var length = 0;
        while (length < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(length), ct);
            if (read == 0) break;
            length += read;
        }
        if (length > 4096) return new(false, "Core management health exceeded the bounded response limit.");
        try
        {
            using var health = JsonDocument.Parse(bytes.AsMemory(0, length), new JsonDocumentOptions { MaxDepth = 8 });
            if (health.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            var root = health.RootElement;
            foreach (var field in new[] { "ready", "journalHealthy", "routersReady" })
                if (root.TryGetProperty(field, out var value) && value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new JsonException();
            if (root.TryGetProperty("journalHealthy", out var journal) && journal.ValueKind == JsonValueKind.False)
                return new(false, "Core health reports that the durable journal is not healthy.");
            if (root.TryGetProperty("routersReady", out var routers) && routers.ValueKind == JsonValueKind.False)
                return new(false, "Core health reports that collection routers are not ready.");
            return root.TryGetProperty("ready", out var readiness) && readiness.ValueKind == JsonValueKind.True && response.IsSuccessStatusCode
                ? new(true, "Core management health confirmed readiness.")
                : new(false, "Core health has not reported ready; no more specific safe condition is available.");
        }
        catch (JsonException) { return new(false, "Core management health returned an invalid bounded response."); }
    }
}

public sealed record ProxyStartupTiming(TimeSpan ReadinessDeadline, TimeSpan PollInterval, TimeSpan RequestTimeout)
{
    public static ProxyStartupTiming Default { get; } = new(TimeSpan.FromSeconds(20), TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(2));
}
