using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Lain.Client.Lifecycle;
using Lain.Core;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private readonly DesktopStartupServices? _startupServices;
    private StartupPreferences _startupPreferences = new();
    private UserHostLifecycleStatus? _startupStatus;
    private Task? _startupOperation;
    private bool _startupInProgress;
    private bool _startupPreferencesLoaded;
    private string? _startupError;
    private bool _startupRefreshFailed;
    private bool _startupAdmissionBlocked;
    private int _startupPreferencesRevision;
    private int _startupManagerRevision;
    private string? _startupPreferencesError;
    private readonly TextBlock _startupMessage = Ui.Text("", 13);
    private readonly TextBlock _startupDetail = Ui.Text("", 12, Ui.Muted);
    private Border? _startupPanel;
    private Button? _startupRetry;

    /// <summary>Used for both first launch and reopening an existing Desktop instance.</summary>
    public Task EnsureServiceOnOpenAsync() => BeginStartupAsync(forceStart: false);

    /// <summary>An explicit on-demand request, independent of the start-on-open preference.</summary>
    public Task RetryServiceStartupAsync() => BeginStartupAsync(forceStart: true);

    private Task BeginStartupAsync(bool forceStart)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(async () =>
            {
                try { await BeginStartupAsync(forceStart); completion.TrySetResult(); }
                catch (Exception error) { completion.TrySetException(error); }
            });
            return completion.Task;
        }
        if (_lifetime.IsCancellationRequested) return Task.CompletedTask;
        if (_startupOperation is { IsCompleted: false }) return _startupOperation;
        // RunStartupAsync yields before invoking any dependency. Repeated reopen requests
        // see this operation before the first callback can finish synchronously.
        return _startupOperation = RunStartupAsync(forceStart);
    }

    private async Task RunStartupAsync(bool forceStart)
    {
        if (_startupServices is null) { await RefreshAsync(true); return; }
        _startupInProgress = true;
        _startupError = null;
        _startupRefreshFailed = false;
        UpdateStartupPresentation();
        SynchronizeStartupSettings();
        await Task.Yield();
        var cancellationToken = _lifetime.Token;
        var preferencesRevision = _startupPreferencesRevision;
        var managerRevision = _startupManagerRevision;
        try
        {
            // Discovery, preference IO and manager inspection may block before their first
            // await. Keep them off the UI thread so Settings and the window stay usable.
            var result = await Task.Run(async () =>
            {
                StartupPreferences? preferences = null;
                string? preferencesError = null;
                try { preferences = await _startupServices.Preferences.ReadAsync(cancellationToken); }
                catch (Exception error) when (error is not OperationCanceledException)
                { preferencesError = "Startup preferences could not be read. " + Friendly(error); }
                // A failed preference read must not silently undo a saved opt-out.
                UserHostLifecycleStatus? status = null;
                string? startupError = null;
                try
                {
                    status = forceStart || preferences?.StartOnOpen == true
                        ? await _startupServices.EnsureRunningAsync(cancellationToken)
                        : await _startupServices.InspectAsync(cancellationToken);
                }
                catch (LifecycleMaintenanceException error)
                {
                    startupError = Friendly(error);
                    status = new("maintenance", new("unknown", "unknown", null, "unknown"), "blocked", "unknown", Error: startupError);
                }
                catch (Exception error) when (error is not OperationCanceledException) { startupError = Friendly(error); }
                return (preferences, preferencesError, status, startupError);
            }, cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;
            if (result.preferences is { } loaded && preferencesRevision == _startupPreferencesRevision)
            {
                _startupPreferences = loaded;
                _startupPreferencesLoaded = true;
            }
            if (preferencesRevision == _startupPreferencesRevision) _startupPreferencesError = result.preferencesError;
            if (result.status is { } status)
            {
                _startupStatus = managerRevision == _startupManagerRevision || _startupStatus is null
                    ? status : status with { Manager = _startupStatus.Manager };
                _startupAdmissionBlocked = status.InstallationState == "maintenance";
                if (status.HostState != "ready") _hostAvailable = false;
            }
            else
            {
                _startupStatus = null;
                _startupAdmissionBlocked = false;
                _hostAvailable = false;
                _startupRefreshFailed = true;
            }
            _startupError = result.startupError ?? result.status?.Error;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
        catch (Exception error) { _startupError = Friendly(error); }
        finally
        {
            _startupInProgress = false;
            if (!cancellationToken.IsCancellationRequested)
            {
                UpdateStartupPresentation();
                SynchronizeStartupSettings();
            }
        }
        if (!cancellationToken.IsCancellationRequested)
        {
            // A lifecycle block is authoritative. Do not reopen API state through a
            // maintenance gate or replace its explanation with a missing-token error.
            if (_startupStatus?.HostState == "ready") await RefreshAsync(true);
            else RefreshPage(explicitRefresh: true);
        }
    }

    private Control StartupStatusPanel()
    {
        AutomationProperties.SetName(_startupMessage, "Background service startup status");
        AutomationProperties.SetLiveSetting(_startupMessage, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(_startupDetail, "Background service startup detail");
        _startupRetry = Ui.Button("Retry service startup", () => _ = RetryServiceStartupAsync());
        AutomationProperties.SetName(_startupRetry, "Retry service startup");
        var settings = Ui.Button("Settings", () => Navigate("Settings"));
        AutomationProperties.SetName(settings, "Open startup settings");
        var actions = Ui.Row(_startupRetry, settings);
        var content = new StackPanel { Spacing = 5, Children = { _startupMessage, _startupDetail, actions } };
        _startupPanel = new Border { Background = Ui.Raised, BorderBrush = Ui.Edge, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8), Margin = new Thickness(20, 12, 20, 0),
            Child = content, IsVisible = false };
        return _startupPanel;
    }

    private void UpdateStartupPresentation()
    {
        if (_startupServices is null || _startupPanel is null) return;
        var status = _hostAvailable ? _status : _startupStatus?.Host;
        var hostReady = _hostAvailable || (!_startupRefreshFailed && _startupStatus?.HostState == "ready");
        var presentation = DesktopStartupPresentation.Describe(_startupStatus, status, hostReady, _startupInProgress, _startupError);
        _startupMessage.Text = presentation.Message;
        _startupDetail.Text = presentation.Detail;
        _startupDetail.IsVisible = !string.IsNullOrWhiteSpace(presentation.Detail);
        _startupPanel.IsVisible = presentation.NeedsAttention || _startupInProgress;
        if (_startupRetry is not null) _startupRetry.IsEnabled = !_startupInProgress;
        if (_startupInProgress || !_hostAvailable)
        {
            _connection.Text = presentation.Message;
            _hostIndicator.Background = Ui.Warning;
        }
        SynchronizeStartupSettings();
    }
}
