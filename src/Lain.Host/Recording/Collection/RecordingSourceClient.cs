using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;
using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Recording.Collection;

/// <summary>The only recording source is the authenticated management endpoint of the owned Core.</summary>
public sealed class RecordingSourceClient : IDisposable
{
    public const int MaximumResponseBytes = 48 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 80 };
    private readonly HttpClient http;
    private readonly bool ownsHttp;

    public RecordingSourceClient(Uri coreAddress, string managementToken)
    {
        ValidateAddress(coreAddress);
        if (string.IsNullOrWhiteSpace(managementToken)) throw new ArgumentException("Recording requires local authentication.");
        http = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        { BaseAddress = coreAddress, Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Authorization = new("Bearer", managementToken);
        ownsHttp = true;
    }

    /// <summary>Fixture injection only. Production construction disables proxies and redirects.</summary>
    public RecordingSourceClient(HttpClient client)
    {
        ValidateAddress(client.BaseAddress ?? throw new ArgumentException("Owned Core address is required."));
        http = client;
    }

    public Task<JsonElement> StatusAsync(CancellationToken ct = default) => ReadAsync<JsonElement>("lain/recording/status", ct);
    public async Task ConfigureAsync(CapturePolicySnapshot policy, CancellationToken ct = default)
    {
        // Core independently reads the recording-only persisted policy file. Its management
        // interface never accepts profile/configuration mutations from this collector.
        if (!policy.Enabled)
        {
            using var response = await http.PostAsync("lain/recording/disable", null, ct);
            if (!response.IsSuccessStatusCode) throw new RecordingSourceException(Error(response.StatusCode));
            return;
        }
        var status = await ReadAsync<RecordingSourceStatus>("lain/recording/status", ct);
        if (status.SchemaVersion != RecordingContract.Version) throw new RecordingSourceException("source_unsupported");
        if (!status.Enabled || status.PolicyRevision != policy.Revision || status.StopGeneration != policy.StopGeneration || status.Mode != policy.Mode)
            throw new RecordingSourceException("source_policy_pending");
    }
    public Task<RecordingSourceBatch> ReadAsync(long after, string? epoch, int limit, CancellationToken ct = default)
    {
        if (after < 0 || limit is < 1 or > 100) throw new ArgumentException("Invalid recording source cursor.");
        var path = $"lain/recording/events?after={after.ToString(CultureInfo.InvariantCulture)}&limit={limit}";
        if (epoch is not null) path += "&sourceEpoch=" + Uri.EscapeDataString(epoch);
        return ReadAsync<RecordingSourceBatch>(path, ct);
    }
    private async Task<T> ReadAsync<T>(string path, CancellationToken ct)
    {
        using var response = await http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new RecordingSourceException(Error(response.StatusCode));
        if (response.Content.Headers.ContentLength is > MaximumResponseBytes) throw new RecordingSourceException("source_oversize");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var body = new MemoryStream();
        var buffer = new byte[16384];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (body.Length + count > MaximumResponseBytes) throw new RecordingSourceException("source_oversize");
            body.Write(buffer, 0, count);
        }
        try { return JsonSerializer.Deserialize<T>(body.GetBuffer().AsSpan(0, checked((int)body.Length)), Json)
            ?? throw new RecordingSourceException("source_invalid"); }
        catch (JsonException) { throw new RecordingSourceException("source_invalid"); }
    }
    private static string Error(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "source_unauthorized",
        HttpStatusCode.NotFound => "source_unsupported",
        HttpStatusCode.Conflict => "source_reset",
        _ when (int)code is >= 300 and <= 399 => "source_redirect_rejected",
        _ => "source_unavailable"
    };
    private static void ValidateAddress(Uri address)
    {
        if (!address.IsAbsoluteUri || address.Scheme != "http" || !IPAddress.TryParse(address.Host, out var ip) ||
            !IPAddress.IsLoopback(ip) || address.AbsolutePath != "/" || address.Query.Length != 0 ||
            address.Fragment.Length != 0 || address.UserInfo.Length != 0)
            throw new ArgumentException("Recording requires the owned loopback Core address.");
    }
    public void Dispose() { if (ownsHttp) http.Dispose(); }
}

public sealed record RecordingSourceBatch(string SourceEpoch, SourceObservation[] Observations,
    long DroppedCount = 0, bool UnknownLoss = false, string? GapReason = null,
    [property: JsonPropertyName("nextSequence")] long? NextCursor = null,
    [property: JsonPropertyName("latestSequence")] long? HighWatermark = null,
    RecordingSourceGap? Gap = null, int SchemaVersion = RecordingContract.Version);
public sealed record RecordingSourceGap(long FromSequence, long ThroughSequence, string Reason);
public sealed record RecordingSourceStatus(int SchemaVersion, bool Enabled, string Mode,
    long PolicyRevision, long StopGeneration, string? SourceEpoch = null, bool UnknownCrashLoss = true);
public sealed class RecordingSourceException(string code) : IOException(code)
{
    public string Code { get; } = code;
}
