using System.Collections.Concurrent;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Operations;
using Lain.Host.Quota;

namespace Lain.Host.Accounts.ConsoleConnections.MiMo;

/// <summary>Browser session capability only; existing inference credentials are never read or replaced.</summary>
public sealed class MiMoConsoleLoginAdapter(IMiMoConsoleBrowser browser, TimeProvider? timeProvider = null) : IAccountLoginAdapter
{
    private sealed class Attempt(LoginOperationContext context, string profile)
    {
        public LoginOperationContext Context = context;
        public string Profile = profile;
        public bool Prepared;
    }
    private readonly ConcurrentDictionary<string, Attempt> attempts = new();
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    public string Provider => "xiaomi-mimo";
    public string Method => "console-browser";
    public bool IsAvailable => browser.IsAvailable;
    public async Task<LoginAdapterResult> BeginAsync(LoginOperationContext context, CancellationToken ct)
    {
        Validate(context);
        if (!IsAvailable) return new("failed", Error: "Install Chromium or Chrome and open lain in a desktop session to connect MiMo monitoring.");
        if (attempts.ContainsKey(context.OperationId)) return new("waiting");
        var profile = await browser.OpenAsync(context.AccountId, ct);
        if (!attempts.TryAdd(context.OperationId, new(context, profile)))
            await browser.CloseAsync(profile, true, ct);
        return new("waiting");
    }
    public async Task<LoginAdapterResult> StatusAsync(LoginOperationContext context, CancellationToken ct)
    {
        Validate(context);
        if (!attempts.TryGetValue(context.OperationId, out var attempt)) return new("failed", Error: "MiMo browser operation is no longer active; reconnect monitoring.");
        Same(attempt.Context, context);
        try
        {
            using var identity = await browser.ReadAsync(attempt.Profile, ConsoleReadOperation.MiMoIdentity, ct);
            var bound = MiMoConsoleIdentity.Discover(identity.RootElement);
            if (bound is null)
                return MiMoConsoleIdentity.TrySuccessData(identity.RootElement, out _) ? new("failed", Error: "MiMo identity or region could not be verified. This connector supports verified CN/EU Individual Token Plan accounts.") : new("waiting");
            var binding = new ResourceBindingDto(Provider, bound.Subject, bound.Product, bound.Region, "user", bound.Subject, "provider-discovery");
            AccountService.EnsureCompatibleBinding(context.Request.Binding, binding);
            // An unbound legacy key carries no proof of Xiaomi UID. The user explicitly
            // selects the observed console identity before attaching its monitoring access.
            if (context.Request.Binding?.Subject is null || context.Request.Binding?.Region is null)
                return new("resource-required", Resources: [binding]);
            using var usage = await browser.ReadAsync(attempt.Profile, ConsoleReadOperation.MiMoIndividualUsage, ct);
            // A visible browser can switch accounts between requests. Revalidate
            // after usage before preparing any private credential for publication.
            using var finalIdentity = await browser.ReadAsync(attempt.Profile, ConsoleReadOperation.MiMoIdentity, ct);
            if (!MiMoConsoleIdentity.Matches(finalIdentity.RootElement, bound))
                return new("failed", Error: "MiMo identity changed or the session expired during verification. Reconnect monitoring; existing access is preserved.");
            var result = MiMoConsoleQuotaReader.Individual(identity.RootElement, usage.RootElement, bound, clock.GetUtcNow());
            if (result.Readings.Count == 0) return new("failed", Error: "MiMo signed in, but Individual Token Plan usage could not be verified. The inference key is preserved.");
            attempt.Prepared = true;
            return new("prepared", Credential: new("console-browser", ["quota"], binding,
                JsonSerializer.Serialize(new { kind = "mimo-console-session", profileReference = attempt.Profile, binding = bound })));
        }
        catch (ArgumentException) { return new("failed", Error: "The signed-in MiMo identity or region does not match this account. Existing access is preserved."); }
        catch (Exception error) when (error is InvalidOperationException or IOException or JsonException or System.Net.WebSockets.WebSocketException or HttpRequestException)
        { return new("waiting"); }
    }
    public async Task CancelAsync(LoginOperationContext context, CancellationToken ct)
    {
        if (!attempts.TryRemove(context.OperationId, out var attempt)) return;
        Same(attempt.Context, context);
        await browser.CloseAsync(attempt.Profile, true, ct);
    }
    public async Task ReleaseAsync(LoginOperationContext context, CancellationToken ct)
    {
        if (!attempts.TryRemove(context.OperationId, out var attempt)) return;
        Same(attempt.Context, context);
        // Published sessions outlive the interactive operation. Host shutdown closes
        // their browser, and future reads request explicit reconnection.
        if (!attempt.Prepared) await browser.CloseAsync(attempt.Profile, true, ct);
    }
    private void Validate(LoginOperationContext context)
    {
        if (context.Request.Provider != Provider || context.Request.Method != Method || context.Request.Purposes.Length != 1 || context.Request.Purposes[0] != "quota"
            || context.ExpiresAt <= clock.GetUtcNow() || context.Request.Binding?.Product is { } product && product != "individual-token-plan")
            throw new ArgumentException("MiMo browser monitoring connects Individual Token Plan quota only.");
    }
    private static void Same(LoginOperationContext expected, LoginOperationContext actual)
    {
        if (expected.OperationId != actual.OperationId || expected.AccountId != actual.AccountId || expected.Revision != actual.Revision
            || expected.AccountGeneration != actual.AccountGeneration || expected.ExpiresAt != actual.ExpiresAt)
            throw new InvalidOperationException("MiMo operation identity changed.");
    }
}
