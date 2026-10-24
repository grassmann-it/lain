using System.Net.Http.Headers;
using System.Text.Json;

namespace Lain.Host.Quota;

internal static class QuotaHttp
{
    public static async Task<JsonDocument> GetAsync(HttpClient client, string url, QuotaCredential credential,
        CancellationToken ct, bool codex = false, bool rawAuthorization = false, string? userAgent = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (rawAuthorization) request.Headers.TryAddWithoutValidation("Authorization", credential.BearerToken);
        else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.BearerToken);
        if (codex && !string.IsNullOrEmpty(credential.ProviderAccountId))
            request.Headers.Add("ChatGPT-Account-Id", credential.ProviderAccountId);
        request.Headers.UserAgent.ParseAdd(userAgent ?? (codex ? "lain/1.0" : "command-code/1.84.0 lain/1.0"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode) throw QuotaFetchException.Http(response.StatusCode);
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

internal sealed class QuotaFetchException(string message) : Exception(message)
{
    // Only fixed text and a numeric status leave the provider boundary. Never include
    // response bodies, reason phrases, request URLs, headers or arbitrary exception text.
    public static QuotaFetchException Http(System.Net.HttpStatusCode status)
    {
        var code = (int)status;
        var category = code switch
        {
            401 => "authentication", 403 => "permission", 408 or 504 => "timeout", 429 => "rate-limit",
            >= 500 and <= 599 => "provider-unavailable", _ => "http-error"
        };
        return new($"Quota HTTP {code} ({category}).");
    }

    public static string SafeDiagnostic(Exception error) => error switch
    {
        QuotaFetchException safe => safe.Message,
        HttpRequestException { StatusCode: { } status } => Http(status).Message,
        OperationCanceledException or TimeoutException => "Quota request failed (timeout).",
        HttpRequestException => "Quota request failed (transport).",
        JsonException or FormatException => "Quota response failed (invalid-response).",
        InvalidDataException or FileNotFoundException => "Quota access unavailable (credential).",
        _ => "Quota refresh failed (unavailable)."
    };
}
