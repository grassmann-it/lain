using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Lain.Host.Providers;

/// <summary>Metadata access is not evidence of inference entitlement, price or remaining quota.</summary>
public sealed record DirectApiModelObservation(string Provider, string State, string[] Models,
    DateTimeOffset? ObservedAt = null, string? Error = null,
    string InferenceVerification = "not_tested", string QuotaVerification = "unsupported");

/// <summary>
/// Calls only verified official model-list endpoints. No configurable URL, credential refresh,
/// inference request, account registration or billing action is exposed by this adapter.
/// </summary>
public sealed class DirectApiModelAdapter : IDisposable
{
    private const int MaximumResponseBytes = 2 * 1024 * 1024;
    private readonly HttpClient http;
    private readonly TimeProvider clock;

    public DirectApiModelAdapter(TimeProvider? timeProvider = null)
        : this(new SocketsHttpHandler { AllowAutoRedirect = false }, timeProvider) { }

    // A custom handler exists for isolated mock tests; production uses the redirect-disabled handler above.
    public DirectApiModelAdapter(HttpMessageHandler handler, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        http = new(handler) { Timeout = TimeSpan.FromSeconds(20) };
        clock = timeProvider ?? TimeProvider.System;
    }

    public static string? ModelsEndpoint(string provider) => provider switch
    {
        "gemini-api" => "https://generativelanguage.googleapis.com/v1beta/openai/models",
        "groq" => "https://api.groq.com/openai/v1/models",
        "mistral" => "https://api.mistral.ai/v1/models",
        _ => null
    };

    public async Task<DirectApiModelObservation> ObserveAsync(string provider, string? apiKey, CancellationToken ct = default)
    {
        var endpoint = ModelsEndpoint(provider);
        if (endpoint is null) return new(provider, "unsupported", [], Error: "Provider metadata access is not supported.");
        if (string.IsNullOrWhiteSpace(apiKey)) return new(provider, "missing-credential", [], Error: "Add your own provider API key.");
        // A malformed secret must not escape through a transport/header exception message.
        if (apiKey.Length > 16384 || apiKey.Any(c => char.IsControl(c)))
            return new(provider, "error", [], Error: "API key format is invalid.");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Accept.Add(new("application/json"));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return new(provider, "error", [], clock.GetUtcNow(), response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Provider rejected metadata access; check API-key permissions.",
                    HttpStatusCode.TooManyRequests => "Provider metadata rate limit reached.",
                    _ when (int)response.StatusCode is >= 300 and < 400 => "Provider metadata redirect was refused.",
                    _ => "Provider metadata request failed."
                });
            if (response.Content.Headers.ContentLength > MaximumResponseBytes)
                return new(provider, "error", [], clock.GetUtcNow(), "Provider metadata response is too large.");
            await using var content = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int count;
            while ((count = await content.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + count > MaximumResponseBytes)
                    return new(provider, "error", [], clock.GetUtcNow(), "Provider metadata response is too large.");
                await buffer.WriteAsync(chunk.AsMemory(0, count), ct);
            }
            buffer.Position = 0;
            using var json = await JsonDocument.ParseAsync(buffer, new JsonDocumentOptions { MaxDepth = 32 }, ct);
            var root = json.RootElement;
            var data = root.ValueKind == JsonValueKind.Array ? root
                : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var list) ? list : default;
            if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() > 10000)
                return new(provider, "error", [], clock.GetUtcNow(), "Provider metadata response format is unrecognized.");
            var models = new List<string>();
            foreach (var model in data.EnumerateArray())
            {
                if (model.ValueKind != JsonValueKind.Object || !model.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String)
                    return new(provider, "error", [], clock.GetUtcNow(), "Provider model identifier is invalid.");
                var value = id.GetString()!;
                if (value.Length is 0 or > 200 || value.Contains(apiKey, StringComparison.Ordinal)
                    || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('/' or '-' or '_' or '.' or ':')))
                    return new(provider, "error", [], clock.GetUtcNow(), "Provider model identifier is invalid.");
                models.Add(value);
            }
            return new(provider, "metadata_available", models.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), clock.GetUtcNow());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or OperationCanceledException or ArgumentException)
        {
            // Never include request headers, exception text, provider bodies or credential material.
            return new(provider, "error", [], clock.GetUtcNow(), "Provider metadata access failed.");
        }
    }

    public void Dispose() => http.Dispose();
}
