using System.Text.Json;

namespace Lain.Host.Quota;

/// <summary>Bounded transport for provider source adapters and the private observation endpoint.</summary>
internal static class QuotaSourceHttp
{
    public static async Task<JsonDocument> ReadJsonAsync(HttpClient http, HttpRequestMessage request, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode) throw new QuotaFetchException($"Quota HTTP {(int)response.StatusCode}.");
        const int maximumBytes = 1024 * 1024;
        if (response.Content.Headers.ContentLength > maximumBytes) throw new QuotaFetchException("Quota response too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var body = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, timeout.Token)) > 0)
        {
            if (body.Length + count > maximumBytes) throw new QuotaFetchException("Quota response too large.");
            body.Write(buffer, 0, count);
        }
        return JsonDocument.Parse(body.ToArray());
    }
}
