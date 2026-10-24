using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts.ConsoleConnections;
using Lain.Host.Accounts.ConsoleConnections.MiMo;
using Lain.Host.Operations;

namespace Lain.Host.Quota;

/// <summary>The private quota generation selects a browser session; an API key is never used as a console cookie.</summary>
public sealed class MiMoQuotaAdapter(IMiMoConsoleBrowser browser, SecretStore secrets, ConfigurationRepository configuration, TimeProvider? timeProvider = null) : IQuotaAdapter
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    public bool Supports(string provider) => provider == "xiaomi-mimo";
    public string Source => MiMoConsoleQuotaReader.IndividualSource;
    // Reads its quota capability directly. This also permits real monitor-only accounts
    // without manufacturing a BearerToken for the legacy inference projection.
    public bool RequiresCredential => false;
    public async Task<QuotaFetchResult> FetchAsync(AccountDto account, QuotaCredential credential, CancellationToken ct = default)
    {
        var support = new QuotaSupportDto("api", "MiMo console session: Individual Token Plan weighted credits. Reconnect monitoring after browser closure, host restart or session expiry.");
        try
        {
            var capability = account.Capabilities?.SingleOrDefault(c => c.Purpose == "quota");
            var handle = account.Credentials?.SingleOrDefault(c => c.Id == capability?.CredentialId && c.Owner == "console-browser" && c.Purposes.Contains("quota"));
            if (handle is null) return SetupRequired("Connect MiMo console monitoring on this account; the inference API key cannot read console usage.");
            if (!IsCurrent(account, handle) || handle.ExpiresAt <= clock.GetUtcNow() || secrets.Generations.IsRetired(handle))
                return new([], "MiMo monitor capability changed or expired; reconnect monitoring.", support);
            using var stored = JsonDocument.Parse(secrets.Generations.Read(account.Id, handle).PrivateJson);
            var root = stored.RootElement;
            if (root.GetProperty("kind").GetString() != "mimo-console-session") throw new InvalidDataException();
            var profile = root.GetProperty("profileReference").GetString();
            if (profile is null || profile.Length != 32 || profile.Any(c => !char.IsAsciiHexDigit(c))) throw new InvalidDataException();
            var binding = root.GetProperty("binding").Deserialize<ConsoleResourceBinding>() ?? throw new InvalidDataException();
            if (handle.Binding.Subject != binding.Subject || handle.Binding.Region != binding.Region || handle.Binding.Product != binding.Product)
                throw new InvalidDataException();
            using var identity = await browser.ReadAsync(profile, ConsoleReadOperation.MiMoIdentity, ct);
            if (NeedsSignIn(identity.RootElement)) return SetupRequired("MiMo console session expired; reconnect monitoring.");
            if (!MiMoConsoleIdentity.Matches(identity.RootElement, binding)) return new([], "MiMo identity changed; reconnect monitoring.", support);
            using var usage = await browser.ReadAsync(profile, ConsoleReadOperation.MiMoIndividualUsage, ct);
            var result = MiMoConsoleQuotaReader.Individual(identity.RootElement, usage.RootElement, binding, clock.GetUtcNow());
            // Plan validity is separate from a budget reset. Detail failure keeps usage usable.
            try
            {
                using var detail = await browser.ReadAsync(profile, ConsoleReadOperation.MiMoIndividualDetail, ct);
                if (MiMoConsoleIdentity.TrySuccessData(detail.RootElement, out var data))
                    result = result with { Readings = result.Readings.Select(r => r with { WindowEnd = JsonFields.Date(data, "currentPeriodEnd") }).ToArray() };
            }
            catch (Exception error) when (error is InvalidOperationException or IOException or JsonException or HttpRequestException or System.Net.WebSockets.WebSocketException)
            { result = result with { Error = result.Error ?? "MiMo usage measured; plan validity unavailable." }; }
            // Recheck after every usage/detail read, including partial detail failure:
            // local generation checks alone cannot detect a browser account switch.
            using var finalIdentity = await browser.ReadAsync(profile, ConsoleReadOperation.MiMoIdentity, ct);
            if (NeedsSignIn(finalIdentity.RootElement)) return SetupRequired("MiMo console session expired during the read; reconnect monitoring.");
            if (!MiMoConsoleIdentity.Matches(finalIdentity.RootElement, binding))
                return new([], "MiMo identity changed during the read; reconnect monitoring.", support);
            if (!IsCurrent(account, handle)) return new([], "MiMo monitor capability changed during the read; reconnect monitoring.", support);
            return result with { Support = support };
        }
        catch (MiMoConsoleSessionUnavailableException)
        { return SetupRequired("MiMo console session closed; reconnect monitoring."); }
        catch (Exception error) when (error is InvalidOperationException or IOException or JsonException or HttpRequestException or KeyNotFoundException or System.Net.WebSockets.WebSocketException)
        { return new([], "MiMo console session unavailable; reconnect monitoring. Existing inference access is preserved.", support); }
    }
    private static bool NeedsSignIn(JsonElement payload) => payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number
        && code.TryGetInt32(out var value) && value is 401 or 403;
    private static QuotaFetchResult SetupRequired(string message) =>
        new([], Support: new("setup-required", message, "https://platform.xiaomimimo.com/", "quota"));
    private bool IsCurrent(AccountDto account, CredentialHandleDto handle)
    {
        var current = configuration.Snapshot.Accounts.SingleOrDefault(a => a.Id == account.Id);
        var capability = current?.Capabilities?.SingleOrDefault(c => c.Purpose == "quota");
        var selected = current?.Credentials?.SingleOrDefault(c => c.Id == capability?.CredentialId);
        return current?.Generation == account.Generation && selected is not null && selected.Id == handle.Id && selected.Generation == handle.Generation
            && selected.Binding == handle.Binding && selected.Owner == handle.Owner && selected.Purposes.SequenceEqual(handle.Purposes);
    }
    public async Task DisconnectAsync(AccountDto account, CancellationToken ct = default)
    {
        var capability = account.Capabilities?.SingleOrDefault(c => c.Purpose == "quota");
        var handle = account.Credentials?.SingleOrDefault(c => c.Id == capability?.CredentialId && c.Owner == "console-browser");
        if (handle is null) return;
        using var stored = JsonDocument.Parse(secrets.Generations.Read(account.Id, handle).PrivateJson);
        var profile = stored.RootElement.GetProperty("profileReference").GetString();
        if (profile is { Length: 32 } && profile.All(char.IsAsciiHexDigit)) await browser.CloseAsync(profile, true, ct);
    }
}
