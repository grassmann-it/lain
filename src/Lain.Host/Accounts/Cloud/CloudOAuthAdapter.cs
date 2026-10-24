using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts.Credentials;
using Lain.Host.Accounts.PublicLogin;

namespace Lain.Host.Accounts.Cloud;

/// <summary>Maintainer configuration, never user-entered client IDs or borrowed CLI registrations.</summary>
public sealed record CloudOAuthClient(string Provider, string ClientId, string[] Scopes,
    Uri? RegisteredCallback = null, bool Qualified = false);

internal sealed record CloudOAuthGrant(string Kind, string Provider, string ClientId, string AccessToken,
    string? RefreshToken, string[] Scopes, DateTimeOffset ExpiresAt, Dictionary<string, string> Settings)
{
    public override string ToString() => "CloudOAuthGrant [redacted]";
}

/// <summary>Provider-owned public PKCE grant; host lifecycle exclusively persists and publishes generations.</summary>
public sealed class CloudOAuthAdapter : IAccountLoginAdapter, ICredentialOwner, IDisposable
{
    public const string GoogleCloudScope = "https://www.googleapis.com/auth/cloud-platform";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly CloudOAuthClient client;
    private readonly CloudProviderHttp http;
    private readonly CloudResourceDiscovery discovery;
    private readonly TimeProvider clock;
    private readonly object gate = new();
    private readonly Dictionary<string, Attempt> attempts = new(StringComparer.Ordinal);
    private bool disposed;
    private sealed class Attempt(LoginOperationContext context, LoopbackLoginCallback callback, string verifier, Uri authorization)
    {
        public LoginOperationContext Context { get; } = context;
        public LoopbackLoginCallback Callback { get; } = callback;
        public string Verifier { get; } = verifier;
        public Uri Authorization { get; } = authorization;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public CloudOAuthGrant? Grant;
        public ResourceBindingDto[]? Resources;
        public LoginAdapterResult? Result;
    }
    public string Provider => client.Provider;
    public string Method => "browser";
    public string Owner => Provider == CloudResourceDiscovery.GoogleProvider ? "google-oauth" : "cloudflare-oauth";
    public bool RequiresCore => false;
    public bool IsAvailable => client.Qualified;
    private string AuthorizationEndpoint => Provider == CloudResourceDiscovery.GoogleProvider
        ? "https://accounts.google.com/o/oauth2/v2/auth" : "https://dash.cloudflare.com/oauth2/auth";
    private string TokenEndpoint => Provider == CloudResourceDiscovery.GoogleProvider
        ? "https://oauth2.googleapis.com/token" : "https://dash.cloudflare.com/oauth2/token";

    public CloudOAuthAdapter(CloudOAuthClient client, HttpMessageHandler? handler = null, TimeProvider? clock = null)
    {
        if (client.Provider is not (CloudResourceDiscovery.GoogleProvider or CloudResourceDiscovery.CloudflareProvider)) throw new ArgumentException("Unsupported cloud provider.");
        if (string.IsNullOrWhiteSpace(client.ClientId) || client.ClientId.Length > 256 || client.ClientId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_')))
            throw new ArgumentException("Invalid maintainer client registration.");
        if (client.Scopes is not { Length: > 0 and <= 32 } || client.Scopes.Distinct(StringComparer.Ordinal).Count() != client.Scopes.Length ||
            client.Scopes.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 256 || s.Any(char.IsWhiteSpace) || s.Any(char.IsControl)))
            throw new ArgumentException("Invalid maintainer scope configuration.");
        if (client.Provider == CloudResourceDiscovery.GoogleProvider && (!client.ClientId.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal)
            || !client.Scopes.SequenceEqual([GoogleCloudScope])))
            throw new ArgumentException("Google monitoring requires its own Desktop registration and explicit cloud-platform grant.");
        if (client.Provider == CloudResourceDiscovery.CloudflareProvider && (client.RegisteredCallback is null || client.Scopes.Length < 2 || !client.Scopes.Contains("offline_access", StringComparer.Ordinal)
            || client.Scopes.Any(s => s.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_')))))
            throw new ArgumentException("Cloudflare requires a qualified registered callback and actual scope-catalog IDs including offline_access.");
        if (client.RegisteredCallback is not null) LoopbackLoginCallback.ValidateRegisteredUri(client.RegisteredCallback);
        this.client = client with { Scopes = client.Scopes.ToArray() };
        this.clock = clock ?? TimeProvider.System;
        http = new(handler); discovery = new(http);
    }

    public Task<LoginAdapterResult> BeginAsync(LoginOperationContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ValidateContext(context);
            if (!IsAvailable) return Task.FromResult(new LoginAdapterResult("failed", Error: "Browser monitoring requires lain's own registered OAuth client. The maintainer must configure maintainer/cloud-oauth.json with the provider-issued client ID, approved scopes and qualified callback/refresh flow. Existing monitoring access remains available.", NextAction: "reconnect"));
            if (attempts.TryGetValue(context.OperationId, out var prior))
            { EnsureSame(prior.Context, context); return Task.FromResult(prior.Result ?? new("waiting", prior.Authorization.AbsoluteUri)); }
            if (attempts.Count >= 8) throw new InvalidOperationException("Too many active cloud authorizations.");
            var verifier = Random(); var state = Random(); Uri? authorization = null;
            var callback = new LoopbackLoginCallback(context.OperationId, context.ExpiresAt, clock, uri =>
            {
                var values = new Dictionary<string, string>
                {
                    ["client_id"] = client.ClientId, ["redirect_uri"] = uri.AbsoluteUri, ["response_type"] = "code",
                    ["scope"] = string.Join(' ', client.Scopes), ["state"] = state,
                    ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))), ["code_challenge_method"] = "S256"
                };
                if (Provider == CloudResourceDiscovery.GoogleProvider) { values["access_type"] = "offline"; values["prompt"] = "consent"; }
                authorization = new(AuthorizationEndpoint + "?" + string.Join('&', values.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value))));
                return state;
            }, client.RegisteredCallback);
            attempts.Add(context.OperationId, new(context, callback, verifier, authorization!));
            return Task.FromResult(new LoginAdapterResult("waiting", authorization!.AbsoluteUri));
        }
    }

    public async Task<LoginAdapterResult> StatusAsync(LoginOperationContext context, CancellationToken ct)
    {
        Attempt attempt;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!attempts.TryGetValue(context.OperationId, out attempt!)) throw new KeyNotFoundException("Cloud authorization not found.");
            EnsureSame(attempt.Context, context);
        }
        await attempt.Gate.WaitAsync(ct);
        try
        {
            if (attempt.Result is not null) return attempt.Result;
            if (clock.GetUtcNow() >= context.ExpiresAt) return attempt.Result = new("cancelled");
            if (attempt.Grant is null)
            {
                if (!attempt.Callback.Completion.IsCompleted) return new("waiting", attempt.Authorization.AbsoluteUri);
                AuthorizationCallback callback;
                try { callback = await attempt.Callback.Completion; }
                catch { return attempt.Result = new("failed", Error: "Cloud authorization callback was unavailable."); }
                if (callback.Denied) return attempt.Result = new("failed", Error: "Cloud authorization was declined.");
                ct.ThrowIfCancellationRequested();
                // A one-use code exchange finishes independently of caller cancellation.
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var request = TokenRequest(new()
                {
                    ["grant_type"] = "authorization_code", ["code"] = callback.Code!, ["client_id"] = client.ClientId,
                    ["redirect_uri"] = attempt.Callback.Uri.AbsoluteUri, ["code_verifier"] = attempt.Verifier
                });
                using var response = await http.ReadAsync(request, deadline.Token);
                attempt.Grant = ParseGrant(response.RootElement, null);
                var preliminary = Credential(attempt.Grant, context.Request.Binding ?? new(Provider), []);
                if (context.CheckpointCredentialAsync is null) throw new InvalidOperationException("Cloud grants require durable checkpoint support.");
                await context.CheckpointCredentialAsync(preliminary, CancellationToken.None);
            }
            if (attempt.Grant.RefreshToken is null || !HasScopes(attempt.Grant))
                return attempt.Result = new("recovery-required", Error: "The grant lacks required monitoring scopes or offline access. Reconnect with the approved consent configuration.", NextAction: "reconnect");
            if (attempt.Resources is null)
                attempt.Resources = Provider == CloudResourceDiscovery.GoogleProvider
                    ? context.Request.Binding?.ResourceId is { } projectId
                        ? [await discovery.GoogleProjectAsync(attempt.Grant.AccessToken, projectId, ct)]
                        : await discovery.GoogleProjectsAsync(attempt.Grant.AccessToken, ct)
                    : await discovery.CloudflareAccountsAsync(attempt.Grant.AccessToken, ct);
            if (attempt.Resources.Length == 0)
                return attempt.Result = new("recovery-required", Error: "No authorized existing project or account was returned. Review resource access before reconnecting.", NextAction: "review-recovery");
            var selected = context.Request.Binding;
            var candidates = selected is null ? attempt.Resources : attempt.Resources.Where(b => Matches(selected, b)).ToArray();
            if (candidates.Length > 1) return new("resource-required", Resources: candidates);
            var binding = candidates.SingleOrDefault();
            if (binding is null) return attempt.Result = new("recovery-required", Error: "The previously selected resource is not authorized by this grant. Existing account binding was preserved.", NextAction: "review-recovery");
            var grant = attempt.Grant with { Settings = Settings(binding) };
            return attempt.Result = new("prepared", Credential: Credential(grant, binding, ["quota"]));
        }
        catch (Exception error) when (error is CloudProviderException or HttpRequestException or IOException or JsonException or OperationCanceledException)
        {
            return attempt.Result = new("recovery-required", Error: error is CloudProviderException provider ? provider.Message : "Cloud authorization could not be completed. Review the saved grant; do not repeat a one-time exchange.", NextAction: "review-recovery");
        }
        finally { attempt.Gate.Release(); }
    }

    public async Task<PreparedLoginCredential> RefreshAsync(CredentialRefreshContext context, CancellationToken ct)
    {
        if (!IsAvailable || context.Handle.Owner != Owner || context.Handle.Binding.Provider != Provider)
            throw new InvalidOperationException("Cloud credential owner is unavailable or mismatched.");
        var grant = JsonSerializer.Deserialize<CloudOAuthGrant>(context.PrivateJson, Json) ?? throw new InvalidDataException("Invalid private cloud grant.");
        if (grant.Kind != "cloud-oauth" || grant.Provider != Provider || grant.ClientId != client.ClientId || !Token(grant.RefreshToken)
            || !Settings(context.Handle.Binding).OrderBy(p => p.Key).SequenceEqual(grant.Settings.OrderBy(p => p.Key)))
            throw new InvalidDataException("Cloud credential resource or client binding changed.");
        ct.ThrowIfCancellationRequested();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var request = TokenRequest(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = grant.RefreshToken!, ["client_id"] = client.ClientId });
        using var response = await http.ReadAsync(request, deadline.Token);
        var renewed = ParseGrant(response.RootElement, grant);
        // Even a narrowed rotating grant must be durably preserved before publication is rejected.
        return Credential(renewed, context.Handle.Binding, HasScopes(renewed) ? context.Handle.Purposes : []);
    }

    private CloudOAuthGrant ParseGrant(JsonElement response, CloudOAuthGrant? prior)
    {
        var access = CloudResourceDiscovery.Text(response, "access_token");
        var refresh = CloudResourceDiscovery.Text(response, "refresh_token") ?? prior?.RefreshToken;
        if (!Token(access) || refresh is not null && !Token(refresh) || !string.Equals(CloudResourceDiscovery.Text(response, "token_type"), "Bearer", StringComparison.OrdinalIgnoreCase)
            || !response.TryGetProperty("expires_in", out var expires) || !expires.TryGetInt64(out var seconds) || seconds is <= 0 or > 86400)
            throw new InvalidDataException("Invalid cloud token response.");
        var scope = CloudResourceDiscovery.Text(response, "scope");
        if (scope is { Length: > 8192 }) throw new InvalidDataException("Invalid cloud scopes.");
        var scopes = scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToArray() ?? prior?.Scopes ?? client.Scopes;
        if (scopes.Any(s => s.Length > 256 || s.Any(char.IsControl))) throw new InvalidDataException("Invalid cloud scopes.");
        return new("cloud-oauth", Provider, client.ClientId, access!, refresh, scopes, clock.GetUtcNow().AddSeconds(seconds), prior?.Settings ?? new());
    }
    private bool HasScopes(CloudOAuthGrant grant) => client.Scopes.All(s => grant.Scopes.Contains(s, StringComparer.Ordinal));
    private PreparedLoginCredential Credential(CloudOAuthGrant grant, ResourceBindingDto binding, string[] purposes) =>
        new(Owner, purposes, binding, JsonSerializer.Serialize(grant, Json), grant.ExpiresAt, PreserveForRecovery: true);
    private HttpRequestMessage TokenRequest(Dictionary<string, string> fields) => new(HttpMethod.Post, TokenEndpoint) { Content = new FormUrlEncodedContent(fields) };
    private static bool Token(string? value) => value is { Length: > 0 and <= 32768 } && !value.Any(char.IsWhiteSpace) && !value.Any(char.IsControl);
    private static Dictionary<string, string> Settings(ResourceBindingDto binding) => binding.Provider == CloudResourceDiscovery.GoogleProvider
        && binding.ResourceType == "project" && CloudResourceDiscovery.ProjectId(binding.ResourceId) && CloudResourceDiscovery.ProjectNumber(binding.Subject)
        ? new() { ["projectId"] = binding.ResourceId!, ["projectNumber"] = binding.Subject! }
        : binding.Provider == CloudResourceDiscovery.CloudflareProvider && binding.ResourceType == "account" && CloudResourceDiscovery.CloudflareAccount(binding.ResourceId) && binding.Subject == binding.ResourceId
        ? new() { ["accountId"] = binding.ResourceId! } : throw new InvalidDataException("Cloud credential requires a verified resource binding.");
    private static bool Matches(ResourceBindingDto expected, ResourceBindingDto actual) => expected.Provider == actual.Provider
        && (expected.ResourceId is null || expected.ResourceId == actual.ResourceId) && (expected.Subject is null || expected.Subject == actual.Subject)
        && (expected.ResourceType is null || expected.ResourceType == actual.ResourceType) && (expected.Product is null || expected.Product == actual.Product)
        && (expected.Region is null || expected.Region == actual.Region);
    private void ValidateContext(LoginOperationContext context)
    {
        if (context.Request.Provider != Provider || context.Request.Method != Method || !context.Request.Purposes.SequenceEqual(["quota"])
            || context.Request.Binding is { } binding && binding.Provider != Provider || string.IsNullOrWhiteSpace(context.OperationId)
            || context.OperationId.Length > 128 || context.OperationId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') || context.CheckpointCredentialAsync is null)
            throw new ArgumentException("Cloud browser connection supports only purpose-bound monitoring with durable grant storage.");
    }
    private static void EnsureSame(LoginOperationContext initial, LoginOperationContext current)
    {
        if (initial.OperationId != current.OperationId || initial.AccountId != current.AccountId || initial.Revision != current.Revision || initial.AccountGeneration != current.AccountGeneration
            || initial.ExpiresAt != current.ExpiresAt || JsonSerializer.Serialize(initial.Request with { Binding = null }, Json) != JsonSerializer.Serialize(current.Request with { Binding = null }, Json)
            || initial.Request.Binding is { } prior && (current.Request.Binding is null || !Matches(prior, current.Request.Binding)))
            throw new InvalidOperationException("Cloud authorization context changed.");
    }
    public Task CancelAsync(LoginOperationContext context, CancellationToken ct)
    {
        lock (gate) if (attempts.TryGetValue(context.OperationId, out var attempt))
        { EnsureSame(attempt.Context, context); attempt.Callback.Dispose(); attempt.Result ??= new("cancelled"); }
        return Task.CompletedTask;
    }
    public Task ReleaseAsync(LoginOperationContext context, CancellationToken ct)
    {
        lock (gate) if (attempts.TryGetValue(context.OperationId, out var attempt))
        { EnsureSame(attempt.Context, context); attempts.Remove(context.OperationId); attempt.Callback.Dispose(); }
        return Task.CompletedTask;
    }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; foreach (var attempt in attempts.Values) attempt.Callback.Dispose(); attempts.Clear(); http.Dispose(); }
    }
    private static string Random() => Base64Url(RandomNumberGenerator.GetBytes(32));
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
