using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Accounts.PublicLogin;

/// <summary>Provider-local callback and exchange ownership; publication belongs to LoginOperationService.</summary>
public abstract class PublicBrowserLoginAdapter : IAccountLoginAdapter, IDisposable
{
    private sealed class Attempt(LoginOperationContext context, LoopbackLoginCallback callback, Uri authorization, Func<AuthorizationCallback, HttpRequestMessage> tokenRequest)
    {
        public LoginOperationContext Context { get; } = context;
        public LoopbackLoginCallback Callback { get; } = callback;
        public Uri Authorization { get; } = authorization;
        public Func<AuthorizationCallback, HttpRequestMessage> TokenRequest { get; } = tokenRequest;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public volatile LoginAdapterResult? Result;
    }
    private readonly Dictionary<string, Attempt> attempts = new(StringComparer.Ordinal);
    private readonly object gate = new();
    protected readonly HttpClient Client;
    protected readonly TimeProvider Clock;
    private bool disposed;
    public abstract string Provider { get; }
    public string Method => "browser";
    public bool RequiresCore => false;
    public virtual bool IsAvailable => true;
    protected virtual Uri? RegisteredCallback => null;

    protected PublicBrowserLoginAdapter(HttpMessageHandler? handler = null, TimeProvider? clock = null)
    {
        Clock = clock ?? TimeProvider.System;
        // A supplied handler is a test seam. Production never follows redirects or sends browser cookies.
        Client = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 65536 };
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("lain/0.1.0");
    }

    protected abstract (Uri Authorization, string State, Func<AuthorizationCallback, HttpRequestMessage> Request) CreateAttempt(LoginOperationContext context, Uri callback);
    protected abstract Task<LoginAdapterResult> PrepareAsync(LoginOperationContext context, JsonElement response, CancellationToken ct);

    public Task<LoginAdapterResult> BeginAsync(LoginOperationContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Prune();
            ValidateContext(context);
            if (!IsAvailable) return Task.FromResult(new LoginAdapterResult("failed", Error: "Browser connection is unavailable. Use an existing API key.", NextAction: "use-api-key"));
            if (attempts.TryGetValue(context.OperationId, out var prior))
            {
                EnsureSame(prior.Context, context);
                return Task.FromResult(prior.Result ?? new("waiting", prior.Authorization.AbsoluteUri));
            }
            if (attempts.Values.Count(a => a.Result is null) >= 8) throw new InvalidOperationException("Too many active browser connections.");
            (Uri Authorization, string State, Func<AuthorizationCallback, HttpRequestMessage> Request) created = default;
            var callback = new LoopbackLoginCallback(context.OperationId, context.ExpiresAt, Clock,
                uri => { created = CreateAttempt(context, uri); return created.State; }, RegisteredCallback);
            try
            {
                var authorization = created.Authorization ?? throw new InvalidOperationException("Missing authorization URL.");
                var tokenRequest = created.Request ?? throw new InvalidOperationException("Missing authorization request.");
                var attempt = new Attempt(context, callback, authorization, tokenRequest);
                attempts.Add(context.OperationId, attempt);
                return Task.FromResult(new LoginAdapterResult("waiting", authorization.AbsoluteUri));
            }
            catch { callback.Dispose(); throw; }
        }
    }

    public async Task<LoginAdapterResult> StatusAsync(LoginOperationContext context, CancellationToken ct)
    {
        Attempt attempt;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!attempts.TryGetValue(context.OperationId, out attempt!)) throw new KeyNotFoundException("Browser connection not found.");
            EnsureSame(attempt.Context, context);
        }
        await attempt.Gate.WaitAsync(ct);
        try
        {
            if (attempt.Result is not null) return attempt.Result;
            if (Clock.GetUtcNow() >= context.ExpiresAt || attempt.Callback.Completion.IsCanceled)
                return attempt.Result = new("cancelled");
            if (!attempt.Callback.Completion.IsCompleted) return new("waiting", attempt.Authorization.AbsoluteUri);
            AuthorizationCallback callback;
            try { callback = await attempt.Callback.Completion; }
            catch { return attempt.Result = new("failed", Error: "Authorization callback was unavailable."); }
            attempt.Callback.Dispose();
            if (callback.Denied) return attempt.Result = new("failed", Error: "Authorization was declined.");
            ct.ThrowIfCancellationRequested();
            // Once a one-time exchange starts, finish the bounded exchange even if its caller cancels.
            // The shared operation service then durably preserves late results before checking publication rights.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try
            {
                using var request = attempt.TokenRequest(callback);
                using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if (!response.IsSuccessStatusCode) return attempt.Result = Uncertain();
                using var json = await ReadJsonAsync(response, deadline.Token);
                return attempt.Result = await PrepareAsync(context, json.RootElement, deadline.Token);
            }
            catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or JsonException or InvalidDataException or ArgumentException)
            { return attempt.Result = Uncertain(); }
        }
        finally { attempt.Gate.Release(); }
    }

    public Task CancelAsync(LoginOperationContext context, CancellationToken ct)
    {
        lock (gate)
        {
            if (attempts.TryGetValue(context.OperationId, out var attempt))
            { EnsureSame(attempt.Context, context); attempt.Callback.Dispose(); attempt.Result ??= new("cancelled"); }
        }
        return Task.CompletedTask;
    }

    public Task ReleaseAsync(LoginOperationContext context, CancellationToken ct)
    {
        lock (gate)
        {
            if (attempts.TryGetValue(context.OperationId, out var attempt))
            { EnsureSame(attempt.Context, context); attempts.Remove(context.OperationId); attempt.Callback.Dispose(); }
        }
        return Task.CompletedTask;
    }

    protected static LoginAdapterResult Uncertain() => new("recovery-required", Error: "Key issuance could not be confirmed. Check the provider's key list before starting a new connection.", NextAction: "review-provider-keys");
    protected PreparedLoginCredential Credential(LoginOperationContext context, string key, string baseUrl, ResourceBindingDto binding, DateTimeOffset? expires = null)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 16384 || key.Any(char.IsWhiteSpace) || key.Any(char.IsControl)) throw new InvalidDataException("Invalid issued key.");
        var json = JsonSerializer.Serialize(new { kind = "api-key", provider = Provider, coreProvider = Provider,
            sourceSection = "openai-compatibility", apiKey = key, baseUrl, origin = "public-browser-pkce", expiresAt = expires });
        return new("static-key", context.Request.Purposes.ToArray(), binding, json, expires, PreserveForRecovery: true);
    }
    protected static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > 65536) throw new InvalidDataException("Provider response too large.");
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var bytes = new byte[4096];
        int count;
        while ((count = await input.ReadAsync(bytes, ct)) != 0)
        { if (buffer.Length + count > 65536) throw new InvalidDataException("Provider response too large."); await buffer.WriteAsync(bytes.AsMemory(0, count), ct); }
        var document = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
        if (document.RootElement.ValueKind != JsonValueKind.Object) { document.Dispose(); throw new InvalidDataException("Invalid provider response."); }
        return document;
    }
    protected static string? String(JsonElement node, string name) => node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    protected static bool BindingMatches(ResourceBindingDto? expected, ResourceBindingDto actual) => expected is null ||
        (expected.Provider == actual.Provider && (expected.Subject is null || expected.Subject == actual.Subject)
        && (expected.ResourceId is null || expected.ResourceId == actual.ResourceId) && (expected.ResourceType is null || expected.ResourceType == actual.ResourceType));
    private void ValidateContext(LoginOperationContext context)
    {
        if (context.Request.Provider != Provider || context.Request.Method != Method || context.Request.Binding?.Provider is { } provider && provider != Provider
            || string.IsNullOrWhiteSpace(context.OperationId) || context.OperationId.Length > 128 || context.OperationId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Invalid browser connection context.");
    }
    private static void EnsureSame(LoginOperationContext prior, LoginOperationContext current)
    {
        if (prior.OperationId != current.OperationId || prior.AccountId != current.AccountId || prior.Revision != current.Revision
            || prior.AccountGeneration != current.AccountGeneration || prior.ExpiresAt != current.ExpiresAt
            || JsonSerializer.Serialize(prior.Request) != JsonSerializer.Serialize(current.Request)) throw new InvalidOperationException("Browser connection context changed.");
    }
    private void Prune()
    {
        foreach (var pair in attempts.Where(p => p.Value.Result is not null).OrderByDescending(p => p.Value.Context.ExpiresAt).Skip(64).ToArray())
        { pair.Value.Callback.Dispose(); attempts.Remove(pair.Key); }
        foreach (var pair in attempts.Where(p => p.Value.Context.ExpiresAt <= Clock.GetUtcNow()).ToArray())
        { pair.Value.Callback.Dispose(); attempts.Remove(pair.Key); }
    }
    public void Dispose()
    {
        lock (gate)
        { if (disposed) return; disposed = true; foreach (var attempt in attempts.Values) attempt.Callback.Dispose(); attempts.Clear(); Client.Dispose(); }
    }
}

internal static class Crypto
{
    public static string Random() => Base64Url(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    public static string Challenge(string verifier) => Base64Url(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier)));
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
