using System.Net.Http.Headers;
using System.Text.Json;

namespace Lain.Host.Quota;

internal static class QuotaHttp
{
    public static async Task<JsonDocument> GetAsync(HttpClient client, string url, QuotaCredential credential,
        CancellationToken ct, bool codex = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.BearerToken);
        if (codex && !string.IsNullOrEmpty(credential.ProviderAccountId))
            request.Headers.Add("ChatGPT-Account-Id", credential.ProviderAccountId);
        request.Headers.UserAgent.ParseAdd(codex ? "lain/1.0" : "command-code/1.84.0 lain/1.0");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode) throw new QuotaFetchException($"Quota HTTP {(int)response.StatusCode}.");
        // Bounded response; arbitrary provider text is never included in diagnostics.
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

internal sealed class QuotaFetchException(string message) : Exception(message);
