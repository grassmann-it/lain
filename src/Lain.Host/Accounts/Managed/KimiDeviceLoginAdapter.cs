using System.Net;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts.Credentials;

namespace Lain.Host.Accounts.Managed;

/// <summary>Own host device flow and refresh owner. Publication/persistence belongs exclusively to the shared generation service.</summary>
public sealed class KimiDeviceLoginAdapter : IAccountLoginAdapter, ICredentialOwner, IDisposable
{
    private sealed class Attempt(LoginOperationContext context)
    {
        public readonly LoginOperationContext Initial = context;
        public readonly SemaphoreSlim Gate = new(1, 1);
        public readonly string DeviceId = Guid.NewGuid().ToString("N");
        public string? Region, DeviceCode, UserCode, Url, Verification;
        public DateTimeOffset ExpiresAt, NextPoll;
        public long Interval = 5;
        public volatile bool Cancelled;
        public KimiDeviceGrant? Grant;
        public LoginAdapterResult? Result;
    }
    private readonly Dictionary<string, Attempt> attempts = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private readonly KimiDeviceHttp http;
    private readonly TimeProvider clock;
    private bool disposed;
    public string Provider => "kimi";
    public string Method => "kimi-device";
    public string Owner => "kimi-oauth";
    public bool RequiresCore => false;
    public bool IsAvailable => true;

    public KimiDeviceLoginAdapter(HttpMessageHandler? handler = null, TimeProvider? clock = null)
    { http = new(handler); this.clock = clock ?? TimeProvider.System; }

    public Task<LoginAdapterResult> BeginAsync(LoginOperationContext context, CancellationToken ct)
    {
        Validate(context); ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!attempts.ContainsKey(context.OperationId))
            {
                if (attempts.Count >= 8) throw new InvalidOperationException("Too many Kimi authorizations. Finish an existing operation.");
                attempts.Add(context.OperationId, new(context));
            }
        }
        return StatusAsync(context, ct);
    }

    public async Task<LoginAdapterResult> StatusAsync(LoginOperationContext context, CancellationToken ct)
    {
        Validate(context);
        Attempt attempt;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!attempts.TryGetValue(context.OperationId, out attempt!)) throw new KeyNotFoundException("Kimi authorization not found.");
        }
        EnsureSame(attempt, context);
        await attempt.Gate.WaitAsync(ct);
        try
        {
            if (attempt.Result is not null) return attempt.Result;
            if (attempt.Cancelled) return attempt.Result = new("cancelled");
            if (clock.GetUtcNow() >= context.ExpiresAt) return attempt.Result = Expired();
            if (context.Request.Binding?.Region is not { } region)
            {
                var known = context.Request.Binding ?? new(Provider);
                return new("resource-required", Resources: [known with { Product = KimiDeviceProtocol.Product, Region = "global" },
                    known with { Product = KimiDeviceProtocol.Product, Region = "mainland-cn" }]);
            }
            _ = KimiDeviceProtocol.Endpoints(region);
            if (attempt.Region is not null && attempt.Region != region) throw new InvalidDataException("Kimi region changed during authorization.");
            attempt.Region = region;
            if (attempt.DeviceCode is null)
            {
                var reply = await http.SendAsync(region, "/api/oauth/device_authorization", attempt.DeviceId, null,
                    new() { ["client_id"] = KimiDeviceProtocol.ClientId }, ct);
                if (reply.Status != HttpStatusCode.OK) return attempt.Result = Failure("Kimi could not start device authorization. Existing access is preserved.");
                attempt.DeviceCode = KimiDeviceProtocol.RequiredText(reply.Body, "device_code", 32768);
                if (!KimiDeviceProtocol.Token(attempt.DeviceCode)) throw new InvalidDataException("Invalid Kimi device authorization.");
                attempt.UserCode = KimiDeviceProtocol.RequiredText(reply.Body, "user_code", 64);
                if (attempt.UserCode.Any(c => c < 33 || c > 126)) throw new InvalidDataException("Invalid Kimi user code.");
                attempt.Url = KimiDeviceProtocol.Verification(KimiDeviceProtocol.RequiredText(reply.Body, "verification_uri_complete", 4096), region);
                var verification = ManagedJson.Text(reply.Body, "verification_uri");
                attempt.Verification = string.IsNullOrEmpty(verification) ? attempt.Url : KimiDeviceProtocol.Verification(verification, region);
                var now = clock.GetUtcNow();
                var expiry = now.AddSeconds(KimiDeviceProtocol.Seconds(reply.Body, "expires_in", 600, 86400));
                attempt.ExpiresAt = expiry < context.ExpiresAt ? expiry : context.ExpiresAt;
                attempt.Interval = KimiDeviceProtocol.Seconds(reply.Body, "interval", 5, 600);
                attempt.NextPoll = now.AddSeconds(attempt.Interval);
                return attempt.Cancelled ? attempt.Result = new("cancelled") : Waiting(attempt);
            }
            if (clock.GetUtcNow() >= attempt.ExpiresAt) return attempt.Result = Expired();
            if (clock.GetUtcNow() < attempt.NextPoll) return Waiting(attempt);
            ct.ThrowIfCancellationRequested();
            // One potentially issuing exchange completes independently of UI cancellation.
            // Its first local action is a durable checkpoint, before identity/usage reads.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var started = clock.GetUtcNow();
            attempt.NextPoll = started.AddSeconds(attempt.Interval);
            var token = await http.SendAsync(region, "/api/oauth/token", attempt.DeviceId, null, new()
            {
                ["client_id"] = KimiDeviceProtocol.ClientId, ["device_code"] = attempt.DeviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            }, deadline.Token);
            if (token.Status != HttpStatusCode.OK)
            {
                var error = ManagedJson.Text(token.Body, "error");
                if (error is "authorization_pending" or "slow_down" || token.Status == HttpStatusCode.TooManyRequests)
                {
                    if (error == "slow_down" || token.Status == HttpStatusCode.TooManyRequests) attempt.Interval += 5;
                    attempt.NextPoll = clock.GetUtcNow().AddSeconds(attempt.Interval);
                    return attempt.Cancelled ? attempt.Result = new("cancelled") : Waiting(attempt);
                }
                if (error == "expired_token") return attempt.Result = Expired();
                return attempt.Result = Failure(error == "access_denied" ? "Kimi authorization was declined. Existing access is preserved."
                    : "Kimi device authorization could not be completed. No existing access was replaced.");
            }
            attempt.Grant = ParseToken(token.Body, region, attempt.DeviceId, started);
            var preliminary = Credential(attempt.Grant, context.Request.Binding!, []);
            await context.CheckpointCredentialAsync!(preliminary, CancellationToken.None);
            if (attempt.Cancelled) return attempt.Result = new("cancelled", Credential: preliminary);
            var me = await http.SendAsync(region, "/me", attempt.DeviceId, attempt.Grant.AccessToken, null, deadline.Token);
            if (me.Status != HttpStatusCode.OK) return attempt.Result = Recovery(preliminary);
            var binding = KimiDeviceProtocol.Identity(me.Body, region);
            attempt.Grant = attempt.Grant with { Subject = binding.Subject, ProviderRegion = KimiDeviceProtocol.RequiredText(me.Body, "region", 80) };
            if (!KimiDeviceProtocol.Matches(context.Request.Binding, binding))
                return attempt.Result = Recovery(Credential(attempt.Grant, binding, []));
            var verified = Credential(attempt.Grant, binding, ["quota"]);
            return attempt.Result = new(attempt.Cancelled ? "cancelled" : "prepared", Credential: verified);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or JsonException or OperationCanceledException)
        {
            if (error is OperationCanceledException && ct.IsCancellationRequested && attempt.Grant is null) throw;
            return attempt.Result = attempt.Grant is { } issued
                ? Recovery(Credential(issued, context.Request.Binding ?? new(Provider), []))
                : Failure("Kimi authorization could not be confirmed. Review the provider login before starting another attempt.");
        }
        finally { attempt.Gate.Release(); }
    }

    public async Task<PreparedLoginCredential> RefreshAsync(CredentialRefreshContext context, CancellationToken ct)
    {
        var grant = JsonSerializer.Deserialize<KimiDeviceGrant>(context.PrivateJson, KimiDeviceHttp.Json)
            ?? throw new InvalidDataException("Invalid private Kimi grant.");
        var binding = context.Handle.Binding;
        if (context.Handle.Owner != Owner || !context.Handle.Purposes.SequenceEqual(["quota"]) || grant.Kind != KimiDeviceProtocol.Kind
            || grant.Provider != Provider || grant.ClientId != KimiDeviceProtocol.ClientId || !KimiDeviceProtocol.Token(grant.RefreshToken)
            || !Guid.TryParseExact(grant.DeviceId, "N", out _)
            || grant.BaseUrl != KimiDeviceProtocol.Endpoints(grant.Region).Api || AccountService.CanonicalProviderId(binding.Provider) != Provider
            || binding.Product != KimiDeviceProtocol.Product || binding.Region != grant.Region || binding.Subject != grant.Subject
            || binding.ResourceId != grant.Subject || binding.ResourceType != "user" || string.IsNullOrEmpty(grant.Subject)
            || string.IsNullOrEmpty(grant.ProviderRegion)) throw new InvalidDataException("Kimi owner/client/subject/region binding changed.");
        ct.ThrowIfCancellationRequested();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var started = clock.GetUtcNow();
        var response = await http.SendAsync(grant.Region, "/api/oauth/token", grant.DeviceId, null, new()
        {
            ["client_id"] = KimiDeviceProtocol.ClientId, ["grant_type"] = "refresh_token", ["refresh_token"] = grant.RefreshToken
        }, deadline.Token);
        if (response.Status != HttpStatusCode.OK) throw new InvalidDataException("Kimi renewal could not be confirmed. Review credential recovery.");
        var renewed = ParseToken(response.Body, grant.Region, grant.DeviceId, started) with { Subject = grant.Subject, ProviderRegion = grant.ProviderRegion };
        // Always return rotated material for durable preservation, even if subsequent
        // identity validation fails. Empty purposes prevent publishing an unverified grant.
        try
        {
            var me = await http.SendAsync(grant.Region, "/me", grant.DeviceId, renewed.AccessToken, null, deadline.Token);
            if (me.Status != HttpStatusCode.OK) return Credential(renewed, binding, []);
            var actual = KimiDeviceProtocol.Identity(me.Body, grant.Region);
            var providerRegion = KimiDeviceProtocol.RequiredText(me.Body, "region", 80);
            renewed = renewed with { Subject = actual.Subject, ProviderRegion = providerRegion };
            return Credential(renewed, actual, KimiDeviceProtocol.Matches(binding, actual) && KimiDeviceProtocol.Matches(actual, binding)
                && providerRegion == grant.ProviderRegion ? ["quota"] : []);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or JsonException or OperationCanceledException)
        { return Credential(renewed, binding, []); }
    }

    public Task CancelAsync(LoginOperationContext context, CancellationToken ct)
    {
        Validate(context);
        lock (gate) if (attempts.TryGetValue(context.OperationId, out var attempt)) { EnsureSame(attempt, context); attempt.Cancelled = true; }
        return Task.CompletedTask; // Local operation only; no remote logout/revoke.
    }
    public Task ReleaseAsync(LoginOperationContext context, CancellationToken ct)
    {
        Validate(context);
        lock (gate) if (attempts.TryGetValue(context.OperationId, out var attempt)) { EnsureSame(attempt, context); attempt.Cancelled = true; attempts.Remove(context.OperationId); }
        return Task.CompletedTask;
    }
    private static KimiDeviceGrant ParseToken(JsonElement root, string region, string device, DateTimeOffset started)
    {
        var access = KimiDeviceProtocol.RequiredText(root, "access_token", 32768);
        var refresh = KimiDeviceProtocol.RequiredText(root, "refresh_token", 32768);
        var type = ManagedJson.Text(root, "token_type");
        var seconds = KimiDeviceProtocol.Seconds(root, "expires_in", 0, 86400);
        if (!KimiDeviceProtocol.Token(access) || !KimiDeviceProtocol.Token(refresh) || seconds <= 0
            || type is not null && !string.Equals(type, "Bearer", StringComparison.OrdinalIgnoreCase)) throw ManagedJson.Invalid();
        var scope = ManagedJson.Text(root, "scope") ?? "";
        if (scope.Length > 8192 || scope.Any(char.IsControl)) throw ManagedJson.Invalid();
        return new(KimiDeviceProtocol.Kind, "kimi", KimiDeviceProtocol.ClientId, region, KimiDeviceProtocol.Endpoints(region).Api,
            device, access, refresh, started.AddSeconds(seconds), scope);
    }
    private PreparedLoginCredential Credential(KimiDeviceGrant grant, ResourceBindingDto binding, string[] purposes) =>
        new(Owner, purposes, binding, grant.Json(), grant.ExpiresAt, PreserveForRecovery: true);
    private static LoginAdapterResult Waiting(Attempt attempt) => new("authorizing", attempt.Url,
        UserCode: attempt.UserCode, VerificationUri: attempt.Verification);
    private static LoginAdapterResult Failure(string message) => new("failed", Error: message);
    private static LoginAdapterResult Expired() => new("expired", Error: "Kimi device authorization expired. Start a new explicit login when ready.");
    private static LoginAdapterResult Recovery(PreparedLoginCredential credential) => new("recovery-required", Credential: credential,
        Error: "Kimi issued access, but its account/region could not be verified. The private result is retained; existing access was preserved.", NextAction: "review-recovery");
    private static void Validate(LoginOperationContext context)
    {
        if (AccountService.CanonicalProviderId(context.Request.Provider) != "kimi" || context.Request.Method != "kimi-device" || !context.Request.Purposes.SequenceEqual(["quota"])
            || context.Request.Binding is { } binding && (AccountService.CanonicalProviderId(binding.Provider) != "kimi" || binding.Product is not (null or KimiDeviceProtocol.Product)
                || binding.Region is not (null or "global" or "mainland-cn")) || context.CheckpointCredentialAsync is null)
            throw new ArgumentException("Kimi device authorization requires its own quota-only context and explicit region.");
    }
    private static void EnsureSame(Attempt attempt, LoginOperationContext context)
    {
        var prior = attempt.Initial;
        if (prior.AccountId != context.AccountId || prior.Revision != context.Revision || prior.AccountGeneration != context.AccountGeneration
            || prior.ExpiresAt != context.ExpiresAt || JsonSerializer.Serialize(prior.Request with { Binding = context.Request.Binding }) != JsonSerializer.Serialize(context.Request)
            || !KimiDeviceProtocol.Matches(prior.Request.Binding, context.Request.Binding ?? new("kimi")))
            throw new InvalidDataException("Kimi authorization target changed.");
    }
    public void Dispose()
    {
        lock (gate) { disposed = true; foreach (var attempt in attempts.Values) attempt.Cancelled = true; attempts.Clear(); }
        http.Dispose();
    }
}
