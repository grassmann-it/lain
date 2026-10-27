using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Quota;

/// <summary>Serial, best-effort complete replacement of Core's memory-only availability cache.
/// Uses the existing authenticated loopback client; never calls a provider or alters runtime configuration.</summary>
public sealed class RouterAvailabilityPublisher(HttpClient coreHttp, Func<ConfigurationDto> getConfiguration,
    Func<CancellationToken, Task<RouterAvailabilityQuotaSnapshot[]>> getSnapshots,
    Func<CancellationToken, Task<RouterAvailabilityBindings>> getBindings, TimeProvider? timeProvider = null,
    Func<bool>? canPublish = null)
{
    public const string Endpoint = "/lain/router-availability";
    public const int MaximumPayloadBytes = 1024 * 1024;
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim publication = new(1, 1);

    public async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        try
        {
            do { await PublishOnceAsync(ct); } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    public async Task<bool> PublishOnceAsync(CancellationToken ct = default)
    {
        await publication.WaitAsync(ct);
        try
        {
            if (getConfiguration().RuntimeMode != "gateway" || canPublish?.Invoke() == false) return false;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            var token = deadline.Token;
            var observedAt = clock.GetUtcNow();
            // Bind the process nonce and CAS sequence BEFORE reading observations. Never
            // retry an old payload after 409, a timeout or a Core restart.
            using var stateResponse = await coreHttp.GetAsync(Endpoint, HttpCompletionOption.ResponseHeadersRead, token);
            if (stateResponse.StatusCode != HttpStatusCode.OK) return false;
            await using var stateStream = await stateResponse.Content.ReadAsStreamAsync(token);
            var stateBytes = await RouterAvailabilityBindings.ReadBoundedAsync(stateStream, 16 * 1024, token);
            var state = JsonSerializer.Deserialize<RouterAvailabilityCoreState>(stateBytes, Json);
            if (state is null || state.SchemaVersion != 1 || !Guid.TryParseExact(state.Generation, "D", out _) || state.Sequence == ulong.MaxValue)
                return false;
            var configuration = getConfiguration();
            var configStamp = JsonSerializer.Serialize(configuration);
            RouterAvailabilityEntry[] entries = [];
            if (configuration.RuntimeMode == "gateway" && configuration.Revision == configuration.AppliedRevision)
            {
                var bindings = await getBindings(token);
                var snapshots = await getSnapshots(token);
                entries = new RouterAvailabilityProjector(clock).Project(configuration, bindings.Members, snapshots);
                var rebound = await getBindings(token);
                if (bindings.Fingerprint != rebound.Fingerprint) return false;
            }
            // Includes account generations, enabled flags, handles and AppliedRevision.
            if (JsonSerializer.Serialize(getConfiguration()) != configStamp || canPublish?.Invoke() == false) return false;
            var now = clock.GetUtcNow();
            if (now < observedAt || now >= observedAt + RouterAvailabilityProjector.MaximumAge) return false;
            entries = entries.Where(e => e.BlockedUntil > now).Select(e => e with
            { BlockedUntil = e.BlockedUntil < observedAt + RouterAvailabilityProjector.MaximumAge
                ? e.BlockedUntil : observedAt + RouterAvailabilityProjector.MaximumAge }).ToArray();
            var payload = new RouterAvailabilityReplacement(1, state.Generation, state.Sequence + 1, observedAt,
                observedAt + RouterAvailabilityProjector.MaximumAge, entries);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
            if (bytes.Length > MaximumPayloadBytes) return false;
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = new ByteArrayContent(bytes) };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await coreHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            return response.StatusCode == HttpStatusCode.NoContent;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            // Transport/parser/credential exceptions may contain secrets. This independent
            // loop never logs exception text, response bodies, tokens or quota readings.
            return false;
        }
        finally { publication.Release(); }
    }
}
