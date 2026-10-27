using Lain.Client.Lifecycle;

namespace Lain.Desktop;

/// <summary>
/// Explicit bootstrap dependencies. Fixture windows can supply callbacks and an in-memory
/// preference store without discovering a user's installation or invoking a service manager.
/// </summary>
public sealed class DesktopStartupServices
{
    public Func<CancellationToken, Task<UserHostLifecycleStatus>> EnsureRunningAsync { get; }
    public Func<CancellationToken, Task<UserHostLifecycleStatus>> InspectAsync { get; }
    public Func<bool, CancellationToken, Task<UserHostLifecycleStatus>> SetLoginAutostartAsync { get; }
    public IStartupPreferencesStore Preferences { get; }

    public DesktopStartupServices(IUserHostLifecycle lifecycle, IStartupPreferencesStore preferences)
        : this(lifecycle.EnsureRunningAsync, lifecycle.InspectAsync, lifecycle.SetLoginAutostartAsync, preferences) { }

    public DesktopStartupServices(Func<CancellationToken, Task<UserHostLifecycleStatus>> ensureRunningAsync,
        Func<CancellationToken, Task<UserHostLifecycleStatus>> inspectAsync,
        Func<bool, CancellationToken, Task<UserHostLifecycleStatus>> setLoginAutostartAsync,
        IStartupPreferencesStore preferences)
    {
        EnsureRunningAsync = ensureRunningAsync;
        InspectAsync = inspectAsync;
        SetLoginAutostartAsync = setLoginAutostartAsync;
        Preferences = preferences;
    }

    public static DesktopStartupServices CreateLocal() => new(UserHostLifecycle.CreateLocal(), new JsonStartupPreferencesStore());
}
