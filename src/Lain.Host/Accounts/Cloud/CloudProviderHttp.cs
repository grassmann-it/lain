using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Lain.Host.Accounts.Cloud;

/// <summary>Only fixed provider endpoints; bounded responses, no redirects/cookies or raw diagnostics.</summary>
public sealed class CloudProviderHttp : IDisposable
{
    private readonly HttpClient http;
    public CloudProviderHttp(HttpMessageHandler? handler = null) => http = new(handler ?? new SocketsHttpHandler
    { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<JsonDocument> ReadAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (!response.IsSuccessStatusCode) throw new CloudProviderException(response.StatusCode);
        const int maximumBytes = 1024 * 1024;
        if (response.Content.Headers.ContentLength > maximumBytes) throw new InvalidDataException("Cloud response exceeds the size bound.");
        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var body = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(buffer, deadline.Token)) > 0)
        {
            if (body.Length + count > maximumBytes) throw new InvalidDataException("Cloud response exceeds the size bound.");
            body.Write(buffer, 0, count);
        }
        var document = JsonDocument.Parse(body.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
        if (document.RootElement.ValueKind != JsonValueKind.Object) { document.Dispose(); throw new InvalidDataException("Invalid cloud response."); }
        return document;
    }
    public static HttpRequestMessage Get(string url, string token, string? quotaProjectId = null)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 32768 || token.Any(char.IsWhiteSpace) || token.Any(char.IsControl))
            throw new ArgumentException("Invalid cloud access credential.");
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (quotaProjectId is not null)
        {
            if (!CloudResourceDiscovery.ProjectId(quotaProjectId)) throw new ArgumentException("Invalid Google quota project.");
            request.Headers.Add("X-Goog-User-Project", quotaProjectId);
        }
        return request;
    }
    public void Dispose() => http.Dispose();
}
public sealed class CloudProviderException(HttpStatusCode status) : Exception(status switch
{
    HttpStatusCode.Unauthorized => "Cloud authorization expired or was rejected. Reconnect the existing account.",
    HttpStatusCode.Forbidden => "Cloud permission denied. Review access to the selected project or account; reconnect only if grant scopes changed.",
    HttpStatusCode.TooManyRequests => "Cloud reporting is temporarily rate limited. Retry later.",
    _ => "Cloud reporting request failed."
})
{
    public HttpStatusCode Status { get; } = status;
}
