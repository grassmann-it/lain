using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Lain.Desktop.Tray;

/// <summary>UI-only background presence; closing the UI never stops the independent host.</summary>
public sealed class TrayWorkspaceController : IDisposable
{
    private readonly Window _window;
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly Func<DateTimeOffset, CancellationToken, Task<TrayStatusSnapshot>> _poll;
    private readonly IDesktopNotificationSink _notifications;
    private readonly Func<CancellationToken, Task<bool>> _trayAvailable;
    private readonly CancellationTokenSource _stop = new();
    private readonly TrayIcon _icon;
    private readonly NativeMenu _quotaMenu = new();
    private readonly NativeMenuItem _notificationToggle;
    private bool _minimizeToTray;
    private bool _closeToTray;
    private bool _notificationsEnabled;
    private bool _hiddenInTray;
    private bool _checkingHide;
    private bool _closeWhileChecking;
    private int _visibilityRevision;
    private bool _allowClose;
    private bool _quitting;
    private bool _disposed;
    private Task? _polling;
    private WindowState _restoreState = WindowState.Normal;

    public bool TrayAvailable { get; private set; }
    public event Action<bool>? AvailabilityChanged;
    public event Action<bool>? NotificationsEnabledChanged;

    public TrayWorkspaceController(Window window, IClassicDesktopStyleApplicationLifetime lifetime,
        Func<DateTimeOffset, CancellationToken, Task<TrayStatusSnapshot>> poll,
        IDesktopNotificationSink notifications, Func<CancellationToken, Task<bool>> trayAvailable)
    {
        _window = window;
        _lifetime = lifetime;
        _poll = poll;
        _notifications = notifications;
        _trayAvailable = trayAvailable;
        // Hide keeps the main window alive; a real close must always terminate the UI.
        lifetime.ShutdownMode = ShutdownMode.OnMainWindowClose;
        var open = new NativeMenuItem("Open window");
        open.Click += (_, _) => Reopen();
        var quota = new NativeMenuItem("Quota summary") { Menu = _quotaMenu };
        _quotaMenu.Items.Add(new NativeMenuItem("Quota status has not been loaded.") { IsEnabled = false });
        _notificationToggle = new NativeMenuItem("Enable notifications");
        _notificationToggle.Click += (_, _) =>
        {
            _notificationsEnabled = !_notificationsEnabled;
            UpdateNotificationMenu();
            NotificationsEnabledChanged?.Invoke(_notificationsEnabled);
        };
        var quit = new NativeMenuItem("Quit UI — host stays running");
        quit.Click += (_, _) => Quit();
        var menu = new NativeMenu();
        menu.Items.Add(open);
        menu.Items.Add(quota);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_notificationToggle);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);
        _icon = new TrayIcon { Icon = window.Icon, ToolTipText = "lain · Loading quota status…", Menu = menu, IsVisible = true };
        _icon.Clicked += (_, _) => Reopen();
        if (Application.Current is { } application)
            TrayIcon.SetIcons(application, new TrayIcons { _icon });
        window.PropertyChanged += OnWindowPropertyChanged;
        window.Closing += OnWindowClosing;
        window.Closed += OnWindowClosed;
    }

    public void Configure(bool minimizeToTray, bool closeToTray, bool notificationsEnabled)
    {
        _minimizeToTray = minimizeToTray;
        _closeToTray = closeToTray;
        _notificationsEnabled = notificationsEnabled;
        UpdateNotificationMenu();
        if (!_minimizeToTray && !_closeToTray && _hiddenInTray) Reopen();
    }

    private void UpdateNotificationMenu() =>
        _notificationToggle.Header = _notificationsEnabled ? "Notifications: on — disable" : "Notifications: off — enable";

    public void Start()
    {
        if (_polling is null && !_disposed) _polling = PollAsync();
    }

    private async Task PollAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await CheckAvailabilityAsync();
                var snapshot = await _poll(DateTimeOffset.UtcNow, _stop.Token);
                if (_stop.IsCancellationRequested) return;
                _icon.ToolTipText = snapshot.Tooltip;
                _quotaMenu.Items.Clear();
                foreach (var line in snapshot.Lines)
                    _quotaMenu.Items.Add(new NativeMenuItem(line) { IsEnabled = false });
                if (snapshot.Lines.Length == 0)
                    _quotaMenu.Items.Add(new NativeMenuItem(snapshot.Connected ? "No account quota information available." : "Local host unavailable.") { IsEnabled = false });
                foreach (var alert in snapshot.Alerts)
                {
                    if (!_notificationsEnabled) continue;
                    try { await _notifications.SendAsync(alert, _stop.Token); }
                    catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return; }
                    catch (Exception) { /* Notification failure does not invalidate the quota snapshot. */ }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return; }
            catch (Exception)
            {
                // Keep the last values explicitly stale; polling retries without modal UI.
                _icon.ToolTipText = "lain · Status temporarily unavailable; open the window for details.";
                _quotaMenu.Items.Clear();
                _quotaMenu.Items.Add(new NativeMenuItem("Status temporarily unavailable. Open the window for details.") { IsEnabled = false });
            }
            try { await Task.Delay(TimeSpan.FromSeconds(10), _stop.Token); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task<bool> CheckAvailabilityAsync()
    {
        bool available;
        try { available = _icon.Icon is not null && await _trayAvailable(_stop.Token); }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return false; }
        catch (Exception) { available = false; }
        if (_stop.IsCancellationRequested) return false;
        if (TrayAvailable != available)
        {
            TrayAvailable = available;
            AvailabilityChanged?.Invoke(available);
        }
        if (!available && _hiddenInTray) Reopen();
        return available;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property != Window.WindowStateProperty || _quitting || _disposed) return;
        if (_window.WindowState != WindowState.Minimized)
        {
            _restoreState = _window.WindowState;
            return;
        }
        if (_minimizeToTray) _ = HideAfterAvailabilityCheckAsync(closeRequested: false);
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_quitting || _allowClose || !_closeToTray || _disposed || args.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown) return;
        args.Cancel = true;
        _ = HideAfterAvailabilityCheckAsync(closeRequested: true);
    }

    private async Task HideAfterAvailabilityCheckAsync(bool closeRequested)
    {
        if (_checkingHide) { _closeWhileChecking |= closeRequested; return; }
        _checkingHide = true;
        var visibilityRevision = _visibilityRevision;
        try
        {
            var available = await CheckAvailabilityAsync();
            closeRequested |= _closeWhileChecking;
            _closeWhileChecking = false;
            if (visibilityRevision != _visibilityRevision) return;
            if (available && !_disposed && !_quitting
                && (closeRequested ? _closeToTray : _minimizeToTray && _window.WindowState == WindowState.Minimized))
            {
                _hiddenInTray = true;
                _window.Hide();
            }
            else if (closeRequested && !_disposed && !_quitting)
            {
                // No registered tray host means X retains normal close behavior.
                _allowClose = true;
                try { _window.Close(); }
                finally { _allowClose = false; }
            }
        }
        finally { _checkingHide = false; }
    }

    public void Reopen()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(Reopen); return; }
        if (_disposed || _quitting) return;
        _visibilityRevision++;
        _hiddenInTray = false;
        _window.Show();
        _window.WindowState = _restoreState == WindowState.Minimized ? WindowState.Normal : _restoreState;
        _window.Activate();
    }

    private void Quit()
    {
        _quitting = true;
        _lifetime.Shutdown();
    }

    private void OnWindowClosed(object? sender, EventArgs args) => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        _window.PropertyChanged -= OnWindowPropertyChanged;
        _window.Closing -= OnWindowClosing;
        _window.Closed -= OnWindowClosed;
        if (Application.Current is { } application && TrayIcon.GetIcons(application) is { } icons)
            icons.Remove(_icon);
        _icon.Dispose();
    }
}
