using System.Net.Http.Headers;
using System.Text.Json;
using Lain.Host.Operations;

namespace Lain.Host.Accounts;

/// <summary>Only the launcher-owned OAuth surface, never legacy management endpoints.</summary>
public sealed class HttpCodexOwnedLoginBackend : ICodexOwnedLoginBackend, IDisposable
{
    private readonly HttpClient http;
    public HttpCodexOwnedLoginBackend(OwnStatePaths paths, SecretStore secrets, HttpMessageHandler? handler = null)
    {
        http = new HttpClient(handler ?? new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
        http.BaseAddress = new Uri($"http://127.0.0.1:{paths.GatewayPort}");
        http.Timeout = TimeSpan.FromSeconds(15);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secrets.GetOrCreateToken(paths.ManagementToken));
    }
    public async Task<CoreCodexLoginStart> BeginAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/lain/login/codex");
        using var result = await SendAsync(request, ct);
        var state = Read(result.RootElement, "state");
        var url = Read(result.RootElement, "authorizationUrl");
        if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(url)) throw new InvalidDataException("Core could not begin Codex login.");
        return new(state, url);
    }
    public async Task<CoreCodexLoginProgress> StatusAsync(string state, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/lain/login/" + Uri.EscapeDataString(state));
        using var result = await SendAsync(request, ct);
        var status = Read(result.RootElement, "status") switch
        {
            "pending" or "exchanging" => "waiting", "completed" => "completed", "canceled" or "cancelled" => "cancelled", "failed" => "failed",
            _ => throw new InvalidDataException("Core returned an invalid login status.")
        };
        return new(status, Read(result.RootElement, "ownedAuthFile"));
    }
    public async Task CancelAsync(string state, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/lain/login/" + Uri.EscapeDataString(state));
        using var result = await SendAsync(request, ct);
        if (Read(result.RootElement, "status") is not ("canceled" or "cancelled")) throw new InvalidOperationException("Login could not be cancelled; check its current status.");
    }
    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("The own proxy could not perform this login operation.");
        var json = await response.Content.ReadAsStringAsync(ct);
        if (json.Length > 64 * 1024) throw new InvalidDataException("Invalid core login response.");
        try { return JsonDocument.Parse(json); }
        catch (JsonException) { throw new InvalidDataException("Invalid core login response."); }
    }
    private static string? Read(JsonElement node, string name) => node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    public void Dispose() => http.Dispose();
}
