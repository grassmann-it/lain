using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts.PublicLogin;

namespace Lain.Host.Accounts;

/// <summary>Public OpenRouter S256 key bootstrap, without a client registration or refresh grant.</summary>
public sealed class OpenRouterLoginAdapter(HttpMessageHandler? handler = null, TimeProvider? clock = null) : PublicBrowserLoginAdapter(handler, clock)
{
    public const string AuthorizationEndpoint = "https://openrouter.ai/auth";
    public const string TokenEndpoint = "https://openrouter.ai/api/v1/auth/keys";
    public const string KeyMetadataEndpoint = "https://openrouter.ai/api/v1/key";
    public const string DefaultBaseUrl = "https://openrouter.ai/api/v1";
    public override string Provider => "openrouter";

    protected override (Uri Authorization, string State, Func<AuthorizationCallback, HttpRequestMessage> Request) CreateAttempt(LoginOperationContext context, Uri callback)
    {
        var verifier = Crypto.Random();
        var state = Crypto.Random();
        var parameters = new Dictionary<string, string>
        {
            ["callback_url"] = callback.AbsoluteUri, ["state"] = state,
            ["code_challenge"] = Crypto.Challenge(verifier), ["code_challenge_method"] = "S256"
        };
        if (context.Request.Binding is { ResourceId: { } resource })
        {
            if (context.Request.Binding.ResourceType != "workspace" || string.IsNullOrWhiteSpace(resource) || resource.Length > 256 || resource.Any(char.IsControl))
                throw new ArgumentException("OpenRouter browser connection requires a valid workspace binding.");
            parameters["required_workspace_id"] = resource;
        }
        var authorization = new Uri(AuthorizationEndpoint + "?" + string.Join("&", parameters.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value))));
        return (authorization, state, callbackResult => new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        { Content = JsonContent.Create(new { code = callbackResult.Code, code_verifier = verifier, code_challenge_method = "S256" }) });
    }

    protected override async Task<LoginAdapterResult> PrepareAsync(LoginOperationContext context, JsonElement response, CancellationToken ct)
    {
        if (response.TryGetProperty("error", out _) || String(response, "key") is not { } key) return Uncertain();
        var binding = new ResourceBindingDto(Provider, Product: context.Request.Binding?.Product, Region: context.Request.Binding?.Region, Provenance: "unverified");
        DateTimeOffset? expires = null;
        var initial = Credential(context, key, DefaultBaseUrl, binding); // Validate before constructing any Bearer header.
        if (context.CheckpointCredentialAsync is { } checkpoint)
        {
            try { await checkpoint(initial, CancellationToken.None); }
            catch (Exception error) when (error is IOException or InvalidOperationException or OperationCanceledException)
            { return new("recovery-required", Credential: initial, Error: "The issued key could not be saved. Review recovery before reconnecting.", NextAction: "review-recovery"); }
        }
        // Metadata is a read, not another authorization. Failure keeps a newly connected key usable,
        // but cannot certify a reconnect to a previously confirmed identity or workspace.
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, KeyMetadataEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var metadataResponse = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (metadataResponse.IsSuccessStatusCode)
            {
                using var document = await ReadJsonAsync(metadataResponse, ct);
                if (document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                {
                    var user = Identity(data, "creator_user_id");
                    var workspace = Identity(data, "workspace_id");
                    binding = binding with { Subject = user, ResourceType = workspace is null ? null : "workspace", ResourceId = workspace,
                        Provenance = user is not null || workspace is not null ? "observed" : "unverified" };
                    if (DateTimeOffset.TryParse(String(data, "expires_at"), out var expiry)) expires = expiry;
                }
            }
        }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or JsonException or InvalidDataException) { }
        var credential = Credential(context, key, DefaultBaseUrl, binding, expires);
        return BindingMatches(context.Request.Binding, binding)
            ? new("prepared", Credential: credential)
            : new("recovery-required", Error: "Issued key identity does not confirm the selected account or workspace. Review the saved result.", Credential: credential, NextAction: "review-recovery");
    }
    private static string? Identity(JsonElement data, string name)
    {
        var value = String(data, name);
        return string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.Any(char.IsControl) ? null : value;
    }

}
