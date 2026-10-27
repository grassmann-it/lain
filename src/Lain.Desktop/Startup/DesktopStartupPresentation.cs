using Lain.Client.Lifecycle;
using Lain.Core;

namespace Lain.Desktop;

/// <summary>Host availability and Gateway readiness remain separate in startup feedback.</summary>
public sealed record DesktopStartupPresentation(string Message, string? Detail, bool NeedsAttention)
{
    public static DesktopStartupPresentation Describe(UserHostLifecycleStatus? lifecycle, StatusDto? host,
        bool hostReady, bool starting = false, string? error = null)
    {
        if (starting) return new("Starting background service…", "You can keep using this window and open Settings while startup completes.", true);
        if (!hostReady)
        {
            var reason = error ?? lifecycle?.Error ?? lifecycle?.Manager.Error;
            var blocked = lifecycle?.HostState is "blocked" or "incompatible" or "unauthorized"
                || lifecycle?.InstallationState is not (null or "installed" or "ready")
                || lifecycle?.Manager.RegistrationState is not (null or "registered" or "ready");
            return new(blocked ? "Background service startup blocked" : "Background service unavailable",
                reason ?? "Retry service startup or check the independent startup choices in Settings.", true);
        }
        if (host?.RuntimeMode == "monitor" || (host is null && lifecycle?.CoreState == "not-required"))
            return new("Host ready · Monitor available", "Monitoring does not require the proxy Core.", false);
        if (host?.ProxyReady == true || (host is null && lifecycle?.CoreState == "ready"))
            return new("Host ready · Gateway ready", null, false);
        var stage = host?.RuntimeLifecycle?.Stage ?? host?.State ?? lifecycle?.CoreState;
        if (stage is "suppressed" or "stopped" || host?.RuntimeLifecycle?.AutomaticStartSuppressed == true)
            return new("Host ready · Proxy stopped", "Automatic proxy startup is paused after Stop. Use Start proxy when you want to resume.", true);
        if (stage is "recovery-required" or "integrity" or "configuration" || host?.Application?.State is "recovery-required" or "applying")
            return new("Host ready · Gateway recovery required", host?.RuntimeLifecycle?.Reason ?? host?.Error ?? error
                ?? "Review the service state before explicitly recovering or starting the proxy.", true);
        if (stage == "setup-required" || (host is { AppliedRevision: 0 } && host.Application?.State is not ("recovery-required" or "applying")))
            return new("Host ready · Gateway setup needed", "Choose and apply a profile to enable Gateway requests. Saved changes are not applied by startup.", true);
        if (stage == "starting" || host?.RuntimeLifecycle is { StartedAt: not null, CompletedAt: null })
            return new("Host ready · Proxy Core starting", "The proxy Core is starting. Status refresh will report its readiness.", true);
        return new("Host ready · Proxy Core not ready", host?.RuntimeLifecycle?.Reason ?? host?.Error ?? error
            ?? "Review Local service for the proxy state and available actions.", true);
    }
}
