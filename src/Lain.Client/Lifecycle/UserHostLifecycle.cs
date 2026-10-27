using System.Net;
using Lain.Core;

namespace Lain.Client.Lifecycle;

/// <summary>OS managers own the Host; only the authenticated Host may resume an applied Core runtime.</summary>
public sealed class UserHostLifecycle : IUserHostLifecycle
{
    private readonly Func<InstallationMetadata> discover;
    private readonly Func<InstallationMetadata, IUserServiceManager> managerFactory;
    private readonly Func<LainApiClient> clientFactory;
    private readonly TimeSpan readinessTimeout;
    private readonly TimeSpan pollInterval;
    private readonly string stateDirectory;
    private readonly SemaphoreSlim operations = new(1, 1);

    public UserHostLifecycle(InstallationMetadata installation, IUserServiceManager manager,
        Func<LainApiClient>? clientFactory = null, TimeSpan? readinessTimeout = null,
        TimeSpan? pollInterval = null, string? stateDirectory = null)
        : this(() => installation, _ => manager, clientFactory, readinessTimeout, pollInterval, stateDirectory) { }

    private UserHostLifecycle(Func<InstallationMetadata> discover, Func<InstallationMetadata, IUserServiceManager> managerFactory,
        Func<LainApiClient>? clientFactory, TimeSpan? readinessTimeout, TimeSpan? pollInterval, string? stateDirectory)
    {
        this.discover = discover; this.managerFactory = managerFactory;
        this.clientFactory = clientFactory ?? LainApiClient.CreateLocal;
        this.readinessTimeout = readinessTimeout ?? TimeSpan.FromSeconds(45);
        this.pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(250);
        this.stateDirectory = Path.GetFullPath(stateDirectory ?? LocalDiscovery.StateDirectory);
    }

    public static IUserHostLifecycle CreateLocal(Func<LainApiClient>? clientFactory = null) =>
        new UserHostLifecycle(() => InstallationDiscovery.Discover(), installation => installation.Platform switch {
            "linux" when OperatingSystem.IsLinux() => new LinuxUserServiceManager(installation),
            "macos" when OperatingSystem.IsMacOS() => new MacOSUserServiceManager(installation),
            "windows" when OperatingSystem.IsWindows() => new WindowsUserServiceManager(installation),
            _ => new UnsupportedManager()
        }, clientFactory, null, null, null);

    public async Task<UserHostLifecycleStatus> InspectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        InstallationMetadata installation;
        try { installation = discover(); }
        catch (IOException ex) { return new("missing", new("unknown", "missing", null, "unknown"), "unknown", "unknown", Error: ex.Message); }
        return await InspectAsync(managerFactory(installation), ct).ConfigureAwait(false);
    }

    public Task<UserHostLifecycleStatus> EnsureRunningAsync(CancellationToken ct = default) => RunAsync("ensure", ct);
    public Task<UserHostLifecycleStatus> StartAsync(CancellationToken ct = default) => RunAsync("start", ct);
    public Task<UserHostLifecycleStatus> StopAsync(CancellationToken ct = default) => RunAsync("stop", ct);
    public Task<UserHostLifecycleStatus> RestartAsync(CancellationToken ct = default) => RunAsync("restart", ct);

    public async Task<UserHostLifecycleStatus> SetLoginAutostartAsync(bool enabled, CancellationToken ct = default)
    {
        await operations.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var admission = LifecycleCoordination.AcquireOperation(stateDirectory);
            var manager = managerFactory(discover());
            await manager.SetLoginAutostartAsync(enabled, ct).ConfigureAwait(false);
            return await InspectAsync(manager, ct).ConfigureAwait(false);
        }
        finally { operations.Release(); }
    }

    private async Task<UserHostLifecycleStatus> RunAsync(string action, CancellationToken ct)
    {
        await operations.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Admission must be released before manager start: the child Host acquires it before opening state.
            // A maintenance marker created afterwards makes that Host fail closed before initialization.
            await WaitForAdmissionAsync(ct).ConfigureAwait(false);
            var manager = managerFactory(discover());
            var before = await InspectAsync(manager, ct).ConfigureAwait(false);
            if (before.HostState is "unauthorized" or "incompatible") throw new IOException(before.Error);
            var needsManagerAction = action is "stop" or "restart" || before.HostState != "ready";
            if (needsManagerAction && (before.Manager.RegistrationState is "modified" or "unavailable"))
                throw new IOException(before.Manager.Error ?? "The owned lain user service is unavailable.");
            if (action == "stop")
            {
                await manager.StopAsync(ct).ConfigureAwait(false);
                return await InspectAsync(manager, ct).ConfigureAwait(false);
            }
            if (action == "restart") await manager.RestartAsync(ct).ConfigureAwait(false);
            else if (before.HostState != "ready") await manager.StartAsync(ct).ConfigureAwait(false);
            var startedHost = action == "restart" || before.HostState != "ready";
            var current = startedHost || before.Host is { } existing && IsCoreStarting(existing)
                ? await WaitForHostAsync(manager, ct).ConfigureAwait(false) : before;
            if (current.Host is not { } host) return current;
            // Host ensure validates completed applied-runtime proof. Saved configuration is never applied here.
            if (ShouldRequestCore(host, action, startedHost))
            {
                await WaitForAdmissionAsync(ct).ConfigureAwait(false);
                current = await InspectAsync(manager, ct).ConfigureAwait(false);
                if (current.Host is not { } latest || !ShouldRequestCore(latest, action, startedHost)) return current;
                using var client = clientFactory();
                var resumed = await client.ServiceAsync(action == "ensure" ? "ensure" : "start", ct).ConfigureAwait(false);
                return FromHost(current.Manager, resumed);
            }
            return current;
        }
        finally { operations.Release(); }
    }

    private async Task<UserHostLifecycleStatus> WaitForHostAsync(IUserServiceManager manager, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(readinessTimeout);
        UserHostLifecycleStatus? current = null;
        try
        {
            while (true)
            {
                current = await InspectAsync(manager, deadline.Token).ConfigureAwait(false);
                if (current.HostState is "unauthorized" or "incompatible") return current;
                if (current.HostState == "ready" && !IsCoreStarting(current.Host!))
                {
                    // Initial Host startup can expose HTTP just before taking its Core mutex.
                    // Its admission is released only after that initial attempt has settled.
                    try { using (LifecycleCoordination.AcquireOperation(stateDirectory)) { } return current; }
                    catch (LifecycleMaintenanceException ex) when (ex.Code == "lifecycle-busy") { }
                }
                if (current.Manager.RegistrationState is "modified" or "unavailable") return current;
                await Task.Delay(pollInterval, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (current ?? new("installed", new("unknown", "unknown", null, "unknown"), "unavailable", "unknown"))
                with { Error = "The user service started, but authenticated Host readiness was not reached before the deadline." };
        }
    }

    private async Task WaitForAdmissionAsync(CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(readinessTimeout);
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                try { using (LifecycleCoordination.AcquireOperation(stateDirectory)) { } return; }
                catch (LifecycleMaintenanceException ex) when (ex.Code == "lifecycle-busy")
                { await Task.Delay(pollInterval, deadline.Token).ConfigureAwait(false); }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new IOException("Another lain startup or state operation did not finish before the deadline."); }
    }

    private static bool CanAutomaticallyResume(StatusDto host) => host.RuntimeLifecycle?.AutomaticStartSuppressed != true
        && host.RuntimeLifecycle?.Stage is null or "stopped";

    private static bool ShouldRequestCore(StatusDto host, string action, bool startedHost)
    {
        var allowed = action == "ensure" ? !startedHost && CanAutomaticallyResume(host)
            : !startedHost || host.RuntimeLifecycle?.AutomaticStartSuppressed == true || host.RuntimeLifecycle?.Stage == "stopped";
        return allowed && host.RuntimeMode == "gateway" && !host.ProxyReady && host.AppliedRevision > 0
            && (host.Application is null || host.Application.State is "completed" or "legacy-preserved");
    }

    private static bool IsCoreStarting(StatusDto host) => host.RuntimeMode == "gateway" && !host.ProxyReady
        && host.RuntimeLifecycle is { StartedAt: not null, CompletedAt: null };

    private async Task<UserHostLifecycleStatus> InspectAsync(IUserServiceManager manager, CancellationToken ct)
    {
        var service = await manager.InspectAsync(ct).ConfigureAwait(false);
        using var probe = CancellationTokenSource.CreateLinkedTokenSource(ct);
        probe.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            using var client = clientFactory();
            return FromHost(service, await client.StatusAsync(probe.Token).ConfigureAwait(false));
        }
        catch (LainApiException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        { return new("installed", service, "unauthorized", "unknown", Error: "The local endpoint rejected this installation's Host credential. No process was adopted or restarted."); }
        catch (LainApiException)
        { return new("installed", service, "incompatible", "unknown", Error: "The local endpoint did not return a compatible authenticated Host status."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return new("installed", service, "unavailable", "unknown", Error: "The authenticated Host status request timed out."); }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        { return new("installed", service, "unavailable", "unknown", Error: "The Host is unavailable or its private credential has not been created yet."); }
    }

    private static UserHostLifecycleStatus FromHost(UserServiceStatus manager, StatusDto host)
    {
        if (host.ContractVersion != Api.Version)
            return new("installed", manager, "incompatible", "unknown", host, "The running Host uses an incompatible API contract.");
        var core = host.RuntimeMode == "monitor" ? "not-required" : host.ProxyReady ? "ready" :
            host.RuntimeLifecycle?.AutomaticStartSuppressed == true ? "suppressed" :
            host.Application is { State: not ("completed" or "legacy-preserved" or "idle") } ? "recovery-required" :
            host.AppliedRevision <= 0 ? "setup-required" : host.RuntimeLifecycle?.Stage ?? host.State;
        return new("installed", manager, "ready", core, host, host.Error);
    }

    private sealed class UnsupportedManager : IUserServiceManager
    {
        private static IOException Unsupported() => new("This installation has no supported per-user service manager on this platform.");
        public Task<UserServiceStatus> InspectAsync(CancellationToken ct = default) => Task.FromResult(new UserServiceStatus("unsupported", "unavailable", null, "unknown", Unsupported().Message));
        public Task StartAsync(CancellationToken ct = default) => Task.FromException(Unsupported());
        public Task StopAsync(CancellationToken ct = default) => Task.FromException(Unsupported());
        public Task RestartAsync(CancellationToken ct = default) => Task.FromException(Unsupported());
        public Task SetLoginAutostartAsync(bool enabled, CancellationToken ct = default) => Task.FromException(Unsupported());
    }
}
