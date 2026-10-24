using System.Text.Json;
using System.Text.RegularExpressions;
using Lain.Core;
using Lain.Host.Accounts.ConsoleConnections.Nvidia;

namespace Lain.Host.Quota;

/// <summary>Public offer reference only. No account quantity is inferred from it.</summary>
public sealed record NvidiaBuildPublicLimits(string RequestsPerMinute, string RequestsPerDay);

public static class NvidiaBuildQuotaReader
{
    public const string PublicSource = "https://build.nvidia.com/api/runtime/content/copy.yaml";

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
}

/// <summary>Fetches anonymous product documentation, never inference/NGC credentials or sessions.</summary>
public sealed class NvidiaBuildQuotaAdapter : IQuotaAdapter, IDisposable
{
    private readonly HttpClient http;
    public string Source => NvidiaBuildQuotaReader.PublicSource;
    public bool RequiresCredential => false;
    public bool Supports(string provider) => provider.Equals("nvidia-build", StringComparison.OrdinalIgnoreCase)
        || provider.Equals("nvidia", StringComparison.OrdinalIgnoreCase);

    // An owned client prevents ambient cookies, default authentication and automatic
    // redirects from turning this anonymous reference read into an account login.
    public NvidiaBuildQuotaAdapter(HttpMessageHandler? handler = null)
    {
        http = new(handler ?? new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false });
        http.DefaultRequestHeaders.UserAgent.ParseAdd("lain/1.0");
    }

    public async Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        if (!Supports(account.Provider)) throw new ArgumentException("Not an NVIDIA Build account.");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Source);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode != System.Net.HttpStatusCode.OK) return Unavailable();
            const int maximum = 32 * 1024;
            if (response.Content.Headers.ContentLength > maximum) return Unavailable();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var body = new MemoryStream();
            var buffer = new byte[4096];
            int count;
            while ((count = await stream.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (body.Length + count > maximum) return Unavailable();
                body.Write(buffer, 0, count);
            }
            using var document = JsonDocument.Parse(body.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
            var limits = NvidiaBuildQuotaReader.ReadPublicLimits(document.RootElement);
            return limits is null ? Unavailable() : new([], Support: NvidiaBuildConnection.QuotaSupport(
                $"Published product reference: {limits.RequestsPerMinute}; {limits.RequestsPerDay}. Model and shared-traffic throttling apply. Account use, remaining requests and reset time are unknown."));
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException
            || error is OperationCanceledException && !ct.IsCancellationRequested) { return Unavailable(); }
    }

    private static QuotaFetchResult Unavailable() => new([], Support: NvidiaBuildConnection.QuotaSupport(
        "The public product-limit reference could not be read. Account use, remaining requests and reset time are unknown."));
    public void Dispose() => http.Dispose();
}
