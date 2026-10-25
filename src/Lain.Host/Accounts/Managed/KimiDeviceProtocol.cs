using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Accounts.Managed;

/// <summary>Published MIT toolkit contract, pinned in the qualification note. Public client, never a secret.</summary>
public static class KimiDeviceProtocol
{
    public const string ClientId = "17e5f671-d194-4dfb-9706-5516cb48c098";
    public const string Kind = "kimi-oauth";
    public const string Product = "coding";
    public static readonly string Version = typeof(KimiDeviceProtocol).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion.Split('+')[0] ?? "1.0.0";

    public static (string Auth, string Api) Endpoints(string region) => region switch
    {
        "global" => ("https://auth.kimi.ai", "https://api.kimi.ai/coding/v1"),
        "mainland-cn" => ("https://auth.kimi.com", "https://api.kimi.com/coding/v1"),
        _ => throw new InvalidDataException("Choose the explicit Kimi Code region.")
    };

    internal static bool Token(string? value) => value is { Length: > 0 and <= 32768 }
        && !value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c));
    internal static string RequiredText(JsonElement root, string name, int maximum = 256)
    {
        var value = ManagedJson.Text(root, name);
        return value is { Length: > 0 } && value.Length <= maximum && !value.Any(char.IsControl)
            ? value : throw new InvalidDataException("Kimi returned an invalid response field.");
    }
    internal static long Seconds(JsonElement root, string name, long fallback, long maximum)
    {
        var field = ManagedJson.Field(root, name);
        if (field.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return fallback;
        if (field.ValueKind != JsonValueKind.Number || !field.TryGetInt64(out var value) || value <= 0 || value > maximum)
            throw new InvalidDataException("Kimi returned an invalid expiry or polling interval.");
        return value;
    }
    internal static string Verification(string value, string region)
    {
        var suffix = region == "global" ? "kimi.ai" : "kimi.com";
        if (value.Length > 4096 || value.Any(char.IsControl) || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
            || !(uri.IdnHost == suffix || uri.IdnHost == "auth." + suffix || uri.IdnHost == "www." + suffix || uri.IdnHost == "code." + suffix))
            throw new InvalidDataException("Kimi returned an unsupported verification address.");
        return uri.AbsoluteUri;
    }
    internal static ResourceBindingDto Identity(JsonElement root, string region)
    {
        _ = Endpoints(region);
        var subject = RequiredText(root, "user_id");
        _ = RequiredText(root, "region", 80); // Preserve provider region separately; do not invent a CN/global enum mapping.
        return new("kimi", subject, Product, region, "user", subject, "provider-discovery");
    }
    internal static bool Matches(ResourceBindingDto? expected, ResourceBindingDto actual) => expected is null ||
        expected.Provider == actual.Provider && (expected.Subject is null || expected.Subject == actual.Subject)
        && (expected.Product is null || expected.Product == actual.Product) && (expected.Region is null || expected.Region == actual.Region)
        && (expected.ResourceType is null || expected.ResourceType == actual.ResourceType) && (expected.ResourceId is null || expected.ResourceId == actual.ResourceId);

    internal static void Headers(HttpRequestMessage request, string deviceId)
    {
        if (!Guid.TryParseExact(deviceId, "N", out _)) throw new InvalidDataException("Invalid lain Kimi device identity.");
        request.Headers.UserAgent.ParseAdd("lain/" + Version);
        request.Headers.Add("X-Msh-Platform", "lain");
        request.Headers.Add("X-Msh-Version", Version);
        request.Headers.Add("X-Msh-Device-Id", deviceId);
        request.Headers.Add("X-Msh-Device-Name", "lain");
        request.Headers.Add("X-Msh-Device-Model", "lain-host");
        request.Headers.Add("X-Msh-Os-Version", Environment.OSVersion.Version.ToString());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }
}

internal sealed record KimiDeviceGrant(string Kind, string Provider, string ClientId, string Region, string BaseUrl,
    string DeviceId, string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, string Scope,
    string? Subject = null, string? ProviderRegion = null)
{
    public override string ToString() => "KimiDeviceGrant [redacted]";
    public Dictionary<string, string> Settings => new()
    {
        ["region"] = Region, ["deviceId"] = DeviceId, ["clientId"] = ClientId,
        ["subject"] = Subject ?? "", ["providerRegion"] = ProviderRegion ?? ""
    };
    public string Json() => JsonSerializer.Serialize(new
    {
        Kind, Provider, ClientId, Region, BaseUrl, DeviceId, AccessToken, RefreshToken, ExpiresAt, Scope, Subject, ProviderRegion,
        accountId = Subject, settings = Settings
    }, KimiDeviceHttp.Json);
}

/// <summary>Bounded fixed-host HTTP. No cookie jar, redirects, ambient credentials, provider errors or retry of consumed grants.</summary>
internal sealed class KimiDeviceHttp(HttpMessageHandler? handler = null) : IDisposable
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { MaxDepth = 16, PropertyNameCaseInsensitive = false };
    private readonly HttpClient http = new(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = TimeSpan.FromSeconds(20) };
    internal sealed record Reply(HttpStatusCode Status, JsonElement Body);
    public async Task<Reply> SendAsync(string region, string path, string deviceId, string? access,
        Dictionary<string, string>? form, CancellationToken ct)
    {
        var hosts = KimiDeviceProtocol.Endpoints(region);
        var url = path switch
        {
            "/api/oauth/device_authorization" or "/api/oauth/token" when form is not null && access is null => hosts.Auth + path,
            "/me" or "/usages" when form is null && KimiDeviceProtocol.Token(access) => hosts.Api + path,
            _ => throw new InvalidDataException("Unsupported Kimi operation.")
        };
        using var request = new HttpRequestMessage(form is null ? HttpMethod.Get : HttpMethod.Post, url);
        KimiDeviceProtocol.Headers(request, deviceId);
        if (form is not null) request.Content = new FormUrlEncodedContent(form);
        else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        const int maximum = 1024 * 1024;
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("Kimi response exceeds its size limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var body = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(buffer, deadline.Token)) > 0)
        {
            if (body.Length + count > maximum) throw new InvalidDataException("Kimi response exceeds its size limit.");
            body.Write(buffer, 0, count);
        }
        using var doc = JsonDocument.Parse(body.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
        RejectDuplicates(doc.RootElement);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid Kimi response.");
        return new(response.StatusCode, doc.RootElement.Clone());
    }
    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Ambiguous Kimi response.");
                RejectDuplicates(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) RejectDuplicates(item);
    }
    public void Dispose() => http.Dispose();
}
