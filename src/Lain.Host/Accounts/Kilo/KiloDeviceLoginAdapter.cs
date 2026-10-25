using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Operations;

namespace Lain.Host.Accounts.Kilo;

/// <summary>
/// Own Kilo device authorization through the public exported kilo-gateway contract.
/// No client ID, external store, refresh grant, organization switch or inference.
/// Publication and durable issued-token recovery belong to LoginOperationService.
/// </summary>
public sealed class KiloDeviceLoginAdapter : IAccountLoginAdapter, IAsyncDisposable
{
    public const string DeviceEndpoint = "https://api.kilo.ai/api/device-auth/codes";
    public const string ProfileEndpoint = "https://api.kilo.ai/api/profile";
    public const string DefaultBaseUrl = "https://ai-gateway.kilo.ai/api/v1";
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private const int MaximumResponseBytes = 65536;
    private readonly HttpClient client;
    private readonly TimeProvider clock;
    private readonly KiloTransportHandoff? handoff;
    private readonly Dictionary<string, Attempt> attempts = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private bool disposed;

    private sealed class Attempt(LoginOperationContext context, string? transportTemplate)
    {
        public LoginOperationContext Context { get; } = context;
        public string? TransportTemplate { get; } = transportTemplate;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public Task<LoginAdapterResult> Initialization = null!;
        public LoginAdapterResult? Result;
        public string? Code;
        public string? Verification;
        public DateTimeOffset ExpiresAt;
        public DateTimeOffset NextPollAt;
        public volatile bool Cancelled;
    }

    public KiloDeviceLoginAdapter(HttpMessageHandler? handler = null, TimeProvider? clock = null,
        ConfigurationRepository? configuration = null, SecretStore? secrets = null)
    {
        this.clock = clock ?? TimeProvider.System;
        if ((configuration is null) != (secrets is null)) throw new ArgumentException("Both local credential dependencies are required.");
        if (configuration is not null) handoff = new(configuration, secrets!);
        // A supplied handler is only an offline test seam. Production endpoints
        // are fixed; browser cookies, redirects and environment proxies are unused.
        client = new(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false })
        { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = MaximumResponseBytes };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("lain/0.1.0");
    }

    public string Provider => "kilo";
    public string Method => "device";
    public bool RequiresCore => false;
    public bool IsAvailable => true;

    public static ProviderConnectionMethodDto ConnectionMethod() => new("kilo", "device", "Sign in with Kilo", "device",
        ["inference", "models", "quota"], "available", false, false,
        Description: "Authorize a separate Kilo connection in your browser using the displayed code. Uses your personal Gateway account; no purchase or model request.",
        DefaultBaseUrl: DefaultBaseUrl, DocumentationUrl: "https://kilo.ai/docs/getting-started/setup-authentication",
        Guidance: "Enter the code only on the displayed Kilo website. Organization-only accounts are not supported by this personal connection. Reconnect if Kilo rejects the token; no refresh grant is assumed.", Recommended: true);

    public Task<LoginAdapterResult> BeginAsync(LoginOperationContext context, CancellationToken ct)
    {
        Validate(context);
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Prune();
            if (attempts.TryGetValue(context.OperationId, out var prior))
            {
                Same(prior.Context, context);
                return prior.Initialization;
            }
            if (attempts.Values.Count(a => a.Result is null) >= LoginOperationService.MaximumActiveOperations)
                throw new InvalidOperationException("Too many active Kilo device connections.");
            // Capture only this purpose's current generation, before issuing a remote code.
            // Publication still uses the central revision/generation/removal fences.
            var template = context.Request.AccountId is not null && context.Request.Purposes.Contains("inference", StringComparer.Ordinal)
                ? (handoff ?? throw new InvalidOperationException("Kilo reconnect requires its local transport handoff.")).Capture(context) : null;
            var attempt = new Attempt(context, template);
            attempts.Add(context.OperationId, attempt); // Reserve before the first POST; Begin replay cannot create another code.
            return attempt.Initialization = InitiateAsync(attempt, ct);
        }
    }

    private async Task<LoginAdapterResult> InitiateAsync(Attempt attempt, CancellationToken ct)
    {
        await attempt.Gate.WaitAsync(CancellationToken.None);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15), clock);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
            using var request = new HttpRequestMessage(HttpMethod.Post, DeviceEndpoint);
            // The official endpoint has no client_id, scope, secret or request body.
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, lifetime.Token);
            if (!response.IsSuccessStatusCode)
                return attempt.Result = new("failed", Error: response.StatusCode == HttpStatusCode.TooManyRequests
                    ? "Kilo has too many pending device requests. Wait before starting another connection."
                    : "Kilo device authorization could not begin.", NextAction: "reconnect");
            using var json = await ReadJsonAsync(response, lifetime.Token);
            var code = Text(json.RootElement, "code");
            var verification = Text(json.RootElement, "verificationUrl");
            if (!UserCode(code) || !Verification(verification) || !json.RootElement.TryGetProperty("expiresIn", out var expiry)
                || !expiry.TryGetInt32(out var seconds) || seconds is < 1 or > 3600)
                return attempt.Result = new("failed", Error: "Kilo returned invalid device authorization details.", NextAction: "reconnect");
            attempt.Code = code;
            attempt.Verification = verification;
            var providerExpiry = clock.GetUtcNow().AddSeconds(seconds);
            attempt.ExpiresAt = providerExpiry < attempt.Context.ExpiresAt ? providerExpiry : attempt.Context.ExpiresAt;
            attempt.NextPollAt = clock.GetUtcNow();
            if (attempt.Cancelled) return attempt.Result = new("cancelled");
            return attempt.ExpiresAt <= clock.GetUtcNow() ? attempt.Result = new("expired") : Waiting(attempt);
        }
        catch (Exception error) when (Boundary(error))
        { return attempt.Result = new("failed", Error: "Kilo device authorization could not begin. No existing credentials were changed.", NextAction: "reconnect"); }
        finally { attempt.Gate.Release(); }
    }

    public async Task<LoginAdapterResult> StatusAsync(LoginOperationContext context, CancellationToken ct)
    {
        Attempt attempt;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!attempts.TryGetValue(context.OperationId, out attempt!))
                return new("recovery-required", Error: "Kilo device authorization is not active in this host session. Review saved recovery before reconnecting.", NextAction: "review-recovery");
            Same(attempt.Context, context);
        }
        await attempt.Initialization.WaitAsync(ct);
        await attempt.Gate.WaitAsync(ct);
        try
        {
            if (attempt.Result is not null) return attempt.Result;
            if (attempt.Cancelled) return attempt.Result = new("cancelled");
            if (attempt.ExpiresAt <= clock.GetUtcNow()) return attempt.Result = new("expired");
            if (attempt.NextPollAt > clock.GetUtcNow()) return Waiting(attempt);
            ct.ThrowIfCancellationRequested();
            attempt.NextPollAt = clock.GetUtcNow() + PollInterval;
            // Approval may issue/consume a credential. Once sent, finish this
            // bounded read and checkpoint a late result even after local Cancel.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20), clock);
            using var request = new HttpRequestMessage(HttpMethod.Get, DeviceEndpoint + "/" + Uri.EscapeDataString(attempt.Code!));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.StatusCode == HttpStatusCode.Accepted) return Waiting(attempt);
            if (response.StatusCode == HttpStatusCode.Forbidden)
                return attempt.Result = new("failed", Error: "Kilo device authorization was declined.", NextAction: "reconnect");
            if (response.StatusCode == HttpStatusCode.Gone) return attempt.Result = new("expired");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(6);
                attempt.NextPollAt = clock.GetUtcNow() + TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 3, 60));
                return Waiting(attempt);
            }
            if (!response.IsSuccessStatusCode) return attempt.Result = Uncertain();
            using var json = await ReadJsonAsync(response, deadline.Token);
            var root = json.RootElement;
            switch (Text(root, "status"))
            {
                case "pending": return Waiting(attempt);
                case "denied": return attempt.Result = new("failed", Error: "Kilo device authorization was declined.", NextAction: "reconnect");
                case "expired": return attempt.Result = new("expired");
                case "approved": return attempt.Result = await PrepareAsync(attempt, root, deadline.Token, ct);
                default: return attempt.Result = Uncertain();
            }
        }
        catch (Exception error) when (Boundary(error))
        { return attempt.Result = Uncertain(); }
        finally { attempt.Gate.Release(); }
    }

    private async Task<LoginAdapterResult> PrepareAsync(Attempt attempt, JsonElement approved, CancellationToken ct,
        CancellationToken caller)
    {
        var context = attempt.Context;
        var token = Text(approved, "token");
        if (!Token(token)) return Uncertain();
        var email = Email(Text(approved, "userEmail"));
        var binding = new ResourceBindingDto(Provider, email, Product: "gateway", ResourceType: "personal", Provenance: "unverified");
        // The token is durable evidence, but confers no adoptable purpose until
        // this personal profile is confirmed. Recovery must not bypass that gate.
        var credential = Credential(attempt, token!, binding) with { Purposes = [] };
        // Save before optional identity reads; a valid issued token must never be lost to a metadata or local CAS failure.
        try { await context.CheckpointCredentialAsync!(credential, CancellationToken.None); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or OperationCanceledException)
        { return Recovery(credential, "The Kilo token could not be saved. Keep this host running and review recovery; do not repeat authorization."); }
        if (attempt.Cancelled || caller.IsCancellationRequested)
            return Recovery(credential, "Kilo authorization completed after local cancellation. Its token is saved; review recovery before reconnecting.");
        try
        {
            using var metadata = CancellationTokenSource.CreateLinkedTokenSource(ct, caller);
            using var request = new HttpRequestMessage(HttpMethod.Get, ProfileEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, metadata.Token);
            if (!response.IsSuccessStatusCode) return Recovery(credential, "The Kilo token is saved, but personal profile identity could not be confirmed. Review recovery.");
            using var json = await ReadJsonAsync(response, metadata.Token);
            var root = json.RootElement;
            var observed = Email((root.TryGetProperty("user", out var user) && user.ValueKind == JsonValueKind.Object
                ? Text(user, "email") : null) ?? Text(root, "email"));
            if (email is null || observed is null || !StringComparer.OrdinalIgnoreCase.Equals(email, observed)
                || root.TryGetProperty("hasPersonalAccount", out var personal) && personal.ValueKind != JsonValueKind.True && personal.ValueKind != JsonValueKind.Null)
                return Recovery(credential, "Kilo did not confirm the same personal account. Organization access was not selected; review recovery.");
            binding = binding with { Subject = observed, Provenance = "observed" };
            return Matches(context.Request.Binding, binding) ? new("prepared", Credential: Credential(attempt, token!, binding))
                : Recovery(credential, "The Kilo login belongs to another account or resource. Review its saved result before reconnecting.");
        }
        catch (Exception error) when (Boundary(error))
        { return Recovery(credential, "The Kilo token is saved, but personal profile identity could not be confirmed. Review recovery."); }
    }

    private PreparedLoginCredential Credential(Attempt attempt, string token, ResourceBindingDto binding) =>
        new("static-key", attempt.Context.Request.Purposes.ToArray(), binding,
            KiloTransportHandoff.Issue(attempt.TransportTemplate, token),
            PreserveForRecovery: true);

    public Task CancelAsync(LoginOperationContext context, CancellationToken ct)
    {
        lock (gate)
            if (attempts.TryGetValue(context.OperationId, out var attempt))
            { Same(attempt.Context, context); attempt.Cancelled = true; }
        // There is no documented remote revoke/cancel endpoint. Pending codes expire; an issued token stays recoverable.
        return Task.CompletedTask;
    }

    public async Task ReleaseAsync(LoginOperationContext context, CancellationToken ct)
    {
        Attempt? attempt;
        lock (gate)
        {
            if (!attempts.TryGetValue(context.OperationId, out attempt)) return;
            Same(attempt.Context, context); attempt.Cancelled = true;
        }
        await attempt.Initialization.WaitAsync(ct);
        await attempt.Gate.WaitAsync(ct);
        try { lock (gate) attempts.Remove(context.OperationId); attempt.Code = null; attempt.Verification = null; }
        finally { attempt.Gate.Release(); }
    }

    private void Validate(LoginOperationContext context)
    {
        if (AccountService.CanonicalProviderId(context.Request.Provider) != Provider || context.Request.Method != Method || context.ExpiresAt <= clock.GetUtcNow()
            || context.CheckpointCredentialAsync is null
            || context.ExpiresAt - clock.GetUtcNow() > LoginOperationService.InteractiveLifetime || context.Request.Purposes.Length == 0
            || context.Request.Purposes.Any(p => p is not ("inference" or "quota" or "models"))
            || string.IsNullOrWhiteSpace(context.OperationId) || context.OperationId.Length > 100
            || context.OperationId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))
            || context.Request.Binding is { } b && (AccountService.CanonicalProviderId(b.Provider) != Provider || b.Product is not (null or "gateway")
                || b.Region is not null || b.ResourceType is not (null or "personal") || b.ResourceId is not null))
            throw new ArgumentException("Kilo device connection requires the selected personal Gateway account and supported purposes.");
    }
    private static void Same(LoginOperationContext a, LoginOperationContext b)
    {
        if (a.OperationId != b.OperationId || a.AccountId != b.AccountId || a.Revision != b.Revision || a.AccountGeneration != b.AccountGeneration
            || a.ExpiresAt != b.ExpiresAt || a.TargetRemovalEpoch != b.TargetRemovalEpoch || JsonSerializer.Serialize(a.Request) != JsonSerializer.Serialize(b.Request))
            throw new InvalidOperationException("Kilo device connection context changed.");
    }
    private static bool Matches(ResourceBindingDto? expected, ResourceBindingDto actual) => expected is null ||
        AccountService.CanonicalProviderId(expected.Provider) == AccountService.CanonicalProviderId(actual.Provider)
        && (expected.Subject is null || expected.Subject == actual.Subject)
        && (expected.Product is null || expected.Product == actual.Product) && (expected.Region is null || expected.Region == actual.Region)
        && (expected.ResourceType is null || expected.ResourceType == actual.ResourceType) && (expected.ResourceId is null || expected.ResourceId == actual.ResourceId);
    private static LoginAdapterResult Waiting(Attempt attempt) => new("waiting", Url: attempt.Verification,
        UserCode: attempt.Code, VerificationUri: attempt.Verification);
    private static LoginAdapterResult Uncertain() => new("recovery-required", Error: "Kilo authorization could not be confirmed. Review saved recovery and your Kilo account before reconnecting.", NextAction: "review-recovery");
    private static LoginAdapterResult Recovery(PreparedLoginCredential credential, string error) => new("recovery-required", Error: error, Credential: credential, NextAction: "review-recovery");
    private static bool Boundary(Exception e) => e is HttpRequestException or IOException or OperationCanceledException or JsonException or ArgumentException or InvalidOperationException;
    private static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool UserCode(string? value) => value is { Length: >= 4 and <= 64 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    private static bool Token(string? value) => value is { Length: >= 1 and <= 16384 } && !value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c));
    private static string? Email(string? value) => value is { Length: >= 3 and <= 320 } && value.Contains('@')
        && !value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) ? value.ToLowerInvariant() : null;
    private static bool Verification(string? value) => value is { Length: <= 2048 } && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.Port == 443 && (uri.Host is "kilo.ai" or "app.kilo.ai")
        && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw new InvalidDataException("Invalid Kilo device response.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var body = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) != 0)
        { if (body.Length + read > MaximumResponseBytes) throw new InvalidDataException("Invalid Kilo device response."); await body.WriteAsync(buffer.AsMemory(0, read), ct); }
        var document = JsonDocument.Parse(body.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
        if (document.RootElement.ValueKind != JsonValueKind.Object) { document.Dispose(); throw new InvalidDataException("Invalid Kilo device response."); }
        return document;
    }
    private void Prune()
    {
        foreach (var pair in attempts.Where(p => p.Value.Context.ExpiresAt <= clock.GetUtcNow() && p.Value.Gate.CurrentCount == 1).ToArray()) attempts.Remove(pair.Key);
        foreach (var pair in attempts.Where(p => p.Value.Result is not null && p.Value.Gate.CurrentCount == 1)
            .OrderByDescending(p => p.Value.Context.ExpiresAt).Skip(LoginOperationService.MaximumTerminalOperations).ToArray()) attempts.Remove(pair.Key);
    }
    public async ValueTask DisposeAsync()
    {
        Attempt[] pending;
        lock (gate)
        { if (disposed) return; disposed = true; pending = attempts.Values.ToArray(); foreach (var a in pending) a.Cancelled = true; }
        foreach (var attempt in pending)
        { await attempt.Initialization; await attempt.Gate.WaitAsync(); attempt.Code = null; attempt.Verification = null; attempt.Gate.Release(); }
        lock (gate) attempts.Clear();
        client.Dispose();
    }
}
