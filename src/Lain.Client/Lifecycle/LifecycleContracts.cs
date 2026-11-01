using Lain.Core;

namespace Lain.Client.Lifecycle;

public sealed record UserServiceStatus(string Manager, string RegistrationState, bool? LoginAutostart,
    string State, string? Error = null);

public sealed record UserHostLifecycleStatus(string InstallationState, UserServiceStatus Manager,
    string HostState, string CoreState, StatusDto? Host = null, string? Error = null)
{
    public bool IsReady => HostState == "ready" && CoreState is "ready" or "not-required";
}

public interface IUserHostLifecycle
{
    Task<UserHostLifecycleStatus> InspectAsync(CancellationToken ct = default);
    Task<UserHostLifecycleStatus> EnsureRunningAsync(CancellationToken ct = default);
    Task<UserHostLifecycleStatus> StartAsync(CancellationToken ct = default);
    Task<UserHostLifecycleStatus> StopAsync(CancellationToken ct = default);
    Task<UserHostLifecycleStatus> RestartAsync(CancellationToken ct = default);
    Task<UserHostLifecycleStatus> SetLoginAutostartAsync(bool enabled, CancellationToken ct = default);
}

public interface IUserServiceManager
{
    Task<UserServiceStatus> InspectAsync(CancellationToken ct = default);
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    Task RestartAsync(CancellationToken ct = default);
    Task SetLoginAutostartAsync(bool enabled, CancellationToken ct = default);
}

public sealed record StartupPreferences(bool StartOnOpen = true);

public interface IStartupPreferencesStore
{
    Task<StartupPreferences> ReadAsync(CancellationToken ct = default);
    Task WriteAsync(StartupPreferences preferences, CancellationToken ct = default);
}

public sealed record InstallationMetadata(int SchemaVersion, string Platform, string InstallationRoot,
    string BinaryRoot, string HostPath, string CliPath, string DesktopPath, string? ProxyPath,
    string Manager, string ServiceName, string RegistrationPath, bool LoginAutostart);

public sealed record LifecycleProcessResult(int ExitCode, string StandardOutput, string StandardError);

public interface ILifecycleProcessRunner
{
    Task<LifecycleProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments,
        CancellationToken ct = default);
}
