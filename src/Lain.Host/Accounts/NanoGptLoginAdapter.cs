using System.Net.Http.Json;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts.PublicLogin;
using Lain.Host.Providers.NanoGpt;

namespace Lain.Host.Accounts;

/// <summary>Documented public shortcut PKCE. NanoGPT creates its callback client; lain borrows no client identity.</summary>
public sealed class NanoGptLoginAdapter(HttpMessageHandler? handler = null, TimeProvider? clock = null)
    : PublicBrowserLoginAdapter(handler, clock)
{
    public const string AuthorizationEndpoint = "https://nano-gpt.com/auth";
    public const string TokenEndpoint = "https://nano-gpt.com/api/v1/auth/keys";
    public const string DefaultBaseUrl = "https://api.nano-gpt.com/api/v1";
    public const string DocumentationUrl = "https://docs.nano-gpt.com/api-reference/miscellaneous/oauth-pkce";
    public override string Provider => "nanogpt";

    public static ProviderConnectionMethodDto ConnectionMethod() => new("nanogpt", "browser", "Sign in with NanoGPT", "browser",
        ["inference", "models"], "available", false, false,
        Description: "Authorize NanoGPT to issue an app-specific inference key to lain. The key can spend balance subject to the settings shown by NanoGPT; connecting performs no inference or purchase.",
        DefaultBaseUrl: DefaultBaseUrl, DocumentationUrl: DocumentationUrl,
        Guidance: "Subscription monitoring uses a separate Usage only management token. Review key limits and expiration on NanoGPT's consent screen.", Recommended: true);

    protected override (Uri Authorization, string State, Func<AuthorizationCallback, HttpRequestMessage> Request) CreateAttempt(LoginOperationContext context, Uri callback)
    {
        if (context.Request.Purposes.Length == 0 || context.Request.Purposes.Any(p => p is not ("inference" or "models")))
            throw new ArgumentException("NanoGPT browser login provides inference and model access, not management access.");
        var verifier = Crypto.Random();
        var state = Crypto.Random();
        var parameters = new Dictionary<string, string>
        {
            ["callback_url"] = callback.AbsoluteUri, ["code_challenge"] = Crypto.Challenge(verifier),
            ["code_challenge_method"] = "S256", ["state"] = state,
            ["scope"] = "api.use models.read", ["client_name"] = "lain"
        };
        var authorization = new Uri(AuthorizationEndpoint + "?" + string.Join("&", parameters.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value))));
        return (authorization, state, result => new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        { Content = JsonContent.Create(new { grant_type = "authorization_code", code = result.Code, code_verifier = verifier }) });
    }

    protected override async Task<LoginAdapterResult> PrepareAsync(LoginOperationContext context, JsonElement response, CancellationToken ct)
    {
        var key = String(response, "key") ?? String(response, "access_token");
        if (response.TryGetProperty("error", out _) || !NanoGptCredentialPolicy.IsInferenceKey(key)) return Uncertain();
        // Save issued access before validating optional metadata. A lost response or invalid
        // scope must never silently trigger another one-time key issuance.
        var subject = Identity(String(response, "user_id"), key!);
        var binding = new ResourceBindingDto(Provider, Subject: subject, Product: context.Request.Binding?.Product,
            Region: context.Request.Binding?.Region, Provenance: subject is null ? "unverified" : "observed");
        var credential = Credential(context, key!, DefaultBaseUrl, binding) with { Purposes = [] };
        if (context.CheckpointCredentialAsync is { } checkpoint)
        {
            try { await checkpoint(credential, CancellationToken.None); }
            catch (Exception error) when (error is IOException or InvalidOperationException or OperationCanceledException)
            { return new("recovery-required", Credential: credential, Error: "The issued key could not be saved. Review recovery before reconnecting.", NextAction: "review-recovery"); }
        }
        var scopes = String(response, "scope")?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (String(response, "key") is { } explicitKey && String(response, "access_token") is { } token && explicitKey != token
            || String(response, "token_type") is { } type && !type.Equals("Bearer", StringComparison.OrdinalIgnoreCase)
            || scopes is not null && (!scopes.Contains("api.use") || context.Request.Purposes.Contains("models") && !scopes.Contains("models.read")))
            return new("recovery-required", Credential: credential, Error: "NanoGPT's issued credential metadata could not be confirmed. Review the saved result.", NextAction: "review-recovery");
        return BindingMatches(context.Request.Binding, binding)
            ? new("prepared", Credential: credential with { Purposes = context.Request.Purposes.ToArray() })
            : new("recovery-required", Credential: credential, Error: "NanoGPT did not confirm the selected account identity. Review the saved result.", NextAction: "review-recovery");
    }

    private static string? Identity(string? value, string secret) => value is { Length: > 0 and <= 256 }
        && value != secret && !value.StartsWith("sk-nano-", StringComparison.Ordinal)
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') ? value : null;
}
