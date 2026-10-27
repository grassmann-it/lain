using Lain.Core;

namespace Lain.Host.Operations;

public interface IOwnedProxyStartup
{
    ProxyProcessStatus Status { get; }
    RuntimeLifecycleDto RuntimeLifecycle { get; }
    Task<ProxyProcessStatus> StartAsync(CancellationToken ct = default, Action? validated = null);
    Task<ProxyProcessStatus> StopAsync(CancellationToken ct = default);
    Task<ProxyProcessStatus> RestartAsync(CancellationToken ct = default, Action? validated = null);
}

/// <summary>Resumes existing applied files only. It never writes configuration or repairs application state.</summary>
public sealed class RuntimeStartupService(RuntimeStartIntent intent, IOwnedProxyStartup proxy,
    Func<bool> gatewayMode, Func<bool> credentialsReady, Func<ProfileApplicationStatusDto> applicationStatus,
    Action requireAppliedRuntime)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private RuntimeLifecycleDto? refusal;
    private bool automaticAttempted;

    public RuntimeLifecycleDto Status
    {
        get
        {
            var current = proxy.RuntimeLifecycle;
            if (!gatewayMode()) return new(intent.AutomaticStartSuppressed, "monitoring", UpdatedAt: DateTimeOffset.UtcNow);
            if (intent.AutomaticStartSuppressed)
                return current with { AutomaticStartSuppressed = true, Stage = "suppressed", Reason = "Automatic Core startup is paused after an explicit Stop. Use Start to resume." };
            return !proxy.Status.Ready && current.Stage is ("stopped" or "ready") && Volatile.Read(ref refusal) is { } blocked ? blocked : current;
        }
    }

    public async Task EnsureAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            refusal = null;
            if (!gatewayMode() || intent.AutomaticStartSuppressed || proxy.Status.Ready) return;
            if (!credentialsReady()) { Refuse("recovery-required", "Restored credentials require local recovery before Gateway can start."); return; }
            if (proxy.Status.LoginOnly) { Refuse("setup-required", "A login-only runtime is active. Explicitly apply a profile before inference."); return; }
            if (proxy.Status.State == "drain-recovery-required" || applicationStatus().State == "recovery-required")
            { Refuse("recovery-required", "Runtime recovery requires an explicit profile application."); return; }
            // A failed attempt has no qualified transient classification. Reopening a
            // window must not create a retry loop or replace its original diagnostic.
            if (automaticAttempted || proxy.RuntimeLifecycle.Stage is not ("stopped" or "ready")) return;
            try { requireAppliedRuntime(); }
            catch (InvalidOperationException)
            { Refuse("setup-required", "No completed valid applied runtime is available. Preview and apply a profile first."); return; }
            automaticAttempted = true;
            try { await proxy.StartAsync(ct); }
            catch (Exception error) when (error is not OperationCanceledException || !ct.IsCancellationRequested)
            { /* Owned lifecycle keeps the bounded failure stage; no unqualified retry. */ }
        }
        finally { gate.Release(); }
    }

    public async Task StartAsync(bool restart, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (!gatewayMode() || !credentialsReady()) throw new InvalidOperationException("Gateway requires local recovery or setup.");
            requireAppliedRuntime();
            refusal = null;
            automaticAttempted = true;
            // Qualification, configuration, checksum and port checks run before the
            // lifecycle invokes this callback. Invalid Start cannot erase Stop intent.
            if (restart) await proxy.RestartAsync(ct, intent.AllowAutomaticStart);
            else await proxy.StartAsync(ct, intent.AllowAutomaticStart);
        }
        finally { gate.Release(); }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            intent.SuppressAutomaticStart();
            refusal = null;
            await proxy.StopAsync(ct);
        }
        finally { gate.Release(); }
    }

    private void Refuse(string stage, string reason) =>
        Volatile.Write(ref refusal, new(false, stage, reason, UpdatedAt: DateTimeOffset.UtcNow, CompletedAt: DateTimeOffset.UtcNow));
}
