using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts.Credentials;
using Lain.Host.Operations;

namespace Lain.Host.Accounts.ConsoleConnections.MiMo;

/// <summary>Only an unselected lain-owned MiMo profile may be closed after publication.</summary>
public sealed class MiMoSessionCleanup(IMiMoConsoleBrowser browser, SecretStore secrets, ConfigurationRepository configuration)
{
    public async Task CloseDetachedAsync(AccountDto previous, CancellationToken ct)
    {
        var profile = Profile(previous);
        if (profile is null) return;
        // A new immutable generation may still reference the same session. Preserve
        // that session, including a reference selected on another local account.
        if (configuration.Snapshot.Accounts.Any(account => Profile(account) == profile)) return;
        await browser.CloseAsync(profile, true, ct);
    }

    private string? Profile(AccountDto account)
    {
        if (AccountService.CanonicalProviderId(account.Provider) != "xiaomi-mimo"
            || QuotaConnectionCleanup.SelectedQuota(account) is not { Owner: "console-browser" } handle) return null;
        using var stored = JsonDocument.Parse(secrets.Generations.Read(account.Id, handle).PrivateJson);
        var root = stored.RootElement;
        if (!root.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String || kind.GetString() != "mimo-console-session"
            || !root.TryGetProperty("profileReference", out var reference) || reference.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Invalid owned MiMo session reference.");
        var profile = reference.GetString();
        return profile is { Length: 32 } && profile.All(char.IsAsciiHexDigit) ? profile
            : throw new InvalidDataException("Invalid owned MiMo session reference.");
    }
}
