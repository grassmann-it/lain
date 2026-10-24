using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts.PublicLogin;
using Lain.Host.Providers.Poe;

namespace Lain.Host.Accounts;

/// <summary>A registered maintainer public client; these are package settings, never user credential fields.</summary>
public sealed record PoeLoginOptions(string ClientId, Uri CallbackUri, bool RegistrationQualified = false)
{
    public static PoeLoginOptions? LoadMaintainerConfiguration(string lainHome)
    {
        var home = Path.GetFullPath(lainHome);
        var directory = Path.Combine(home, "maintainer");
        var path = Path.Combine(directory, "poe-login.json");
        if (!File.Exists(path)) return null;
        if ((File.GetAttributes(home) & FileAttributes.ReparsePoint) != 0
            || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0
            || (File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new InvalidDataException("Poe maintainer configuration must be a regular file in the owned state directory.");
        if (new FileInfo(path).Length > 8192) throw new InvalidDataException("Invalid Poe maintainer configuration.");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        return new(root.GetProperty("clientId").GetString() ?? "", new Uri(root.GetProperty("callbackUri").GetString() ?? "", UriKind.Absolute),
            root.GetProperty("registrationQualified").GetBoolean());
    }
}

/// <summary>Poe apikey:create with its existing PKCE helper; no refresh grant or stable user identity is claimed.</summary>
public sealed class PoeLoginAdapter : PublicBrowserLoginAdapter
{
    public const string DefaultBaseUrl = "https://api.poe.com/v1";
    private readonly PoeLoginOptions? options;
    public override string Provider => "poe";
    public override bool IsAvailable => options is { RegistrationQualified: true };
    protected override Uri? RegisteredCallback => options?.CallbackUri;
    public PoeLoginAdapter(PoeLoginOptions? options = null, HttpMessageHandler? handler = null, TimeProvider? clock = null) : base(handler, clock)
    {
        if (options is not null)
        {
            if (string.IsNullOrWhiteSpace(options.ClientId) || options.ClientId.Length > 512 || options.ClientId.Any(char.IsControl))
                throw new ArgumentException("Invalid Poe maintainer client ID.");
            LoopbackLoginCallback.ValidateRegisteredUri(options.CallbackUri);
        }
        this.options = options;
    }

    protected override (Uri Authorization, string State, Func<AuthorizationCallback, HttpRequestMessage> Request) CreateAttempt(LoginOperationContext context, Uri callback)
    {
        var attempt = PoeOAuthAttempt.Create(options!.ClientId, callback);
        var state = LoopbackLoginCallback.ParseQuery(attempt.AuthorizationUri.Query)["state"];
        return (attempt.AuthorizationUri, state, result => attempt.CreateTokenRequest(result.Code!, state));
    }

    protected override async Task<LoginAdapterResult> PrepareAsync(LoginOperationContext context, JsonElement response, CancellationToken ct)
    {
        var issued = PoeOAuthAttempt.ParseTokenResponse(response, Clock.GetUtcNow());
        if (issued is null) return Uncertain();
        var binding = new ResourceBindingDto(Provider, Product: context.Request.Binding?.Product, Region: context.Request.Binding?.Region, Provenance: "unverified");
        var credential = Credential(context, issued.ApiKey, DefaultBaseUrl, binding, issued.ExpiresAt);
        if (context.CheckpointCredentialAsync is { } checkpoint)
        {
            try { await checkpoint(credential, CancellationToken.None); }
            catch (Exception error) when (error is IOException or InvalidOperationException or OperationCanceledException)
            { return new("recovery-required", Credential: credential, Error: "The issued key could not be saved. Review recovery before reconnecting.", NextAction: "review-recovery"); }
        }
        return BindingMatches(context.Request.Binding, binding) ? new LoginAdapterResult("prepared", Credential: credential)
            : new("recovery-required", Error: "Poe did not return a stable identity for the selected account. Review the saved result.", Credential: credential, NextAction: "review-recovery");
    }
}
