using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Lain.Desktop.Tray;

/// <summary>UI-only background presence; closing the UI never stops the independent host.</summary>
public sealed class TrayWorkspaceController : IDisposable
{
    private readonly Window _window;
    private readonly Action _quitUi;
    private readonly Func<DateTimeOffset, CancellationToken, Task<TrayStatusSnapshot>> _poll;
    private readonly IDesktopNotificationSink _notifications;
    private readonly Func<CancellationToken, Task<bool>> _trayAvailable;
    private readonly CancellationTokenSource _stop = new();
    private readonly TrayIcon _icon;
    private readonly NativeMenu _quotaMenu = new();
    private readonly NativeMenuItem _notificationToggle;
    private readonly NativeMenuItem _notificationStatus = new("Notifications are disabled.") { IsEnabled = false };
    private readonly Dictionary<string, PendingNotification> _pendingNotifications = new(StringComparer.Ordinal);
    private bool _notificationDeliveryFailed;
    private long _notificationSequence;
    private const int MaximumPendingNotifications = 20;
    private static readonly TimeSpan NotificationLifetime = TimeSpan.FromMinutes(2);
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

    public TrayWorkspaceController(Window window, Action quitUi,
        Func<DateTimeOffset, CancellationToken, Task<TrayStatusSnapshot>> poll,
        IDesktopNotificationSink notifications, Func<CancellationToken, Task<bool>> trayAvailable)
    {
        _window = window;
        _quitUi = quitUi;
        _poll = poll;
        _notifications = notifications;
        _trayAvailable = trayAvailable;
        var open = new NativeMenuItem("Open window");
        open.Click += (_, _) => Reopen();
        var quota = new NativeMenuItem("Quota summary") { Menu = _quotaMenu };
        _quotaMenu.Items.Add(new NativeMenuItem("Quota status has not been loaded.") { IsEnabled = false });
        _notificationToggle = new NativeMenuItem("Enable notifications");
        _notificationToggle.Click += (_, _) =>
        {
            SetNotificationsEnabled(!_notificationsEnabled);
            NotificationsEnabledChanged?.Invoke(_notificationsEnabled);
        };
        var quit = new NativeMenuItem("Quit UI — host stays running");
        quit.Click += (_, _) => Quit();
        var menu = new NativeMenu();
        menu.Items.Add(open);
        menu.Items.Add(quota);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(_notificationToggle);
        menu.Items.Add(_notificationStatus);
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
        SetNotificationsEnabled(notificationsEnabled);
        if (!_minimizeToTray && !_closeToTray && _hiddenInTray) Reopen();
    }

    private void SetNotificationsEnabled(bool enabled)
    {
        if (_notificationsEnabled != enabled) _notificationDeliveryFailed = false;
        _notificationsEnabled = enabled;
        if (!enabled) _pendingNotifications.Clear();
        UpdateNotificationMenu();
    }

    private void UpdateNotificationMenu()
    {
        _notificationToggle.Header = _notificationsEnabled ? "Notifications: on — disable" : "Notifications: off — enable";
        _notificationStatus.Header = !_notificationsEnabled ? "Notifications are disabled."
            : _notificationDeliveryFailed && _pendingNotifications.Count > 0 ? "Notifications unavailable; retrying recent alerts."
            : _notificationDeliveryFailed ? "Notifications unavailable; delivery will retry with the next alert."
            : "Desktop notifications enabled.";
    }

    private void QueueNotifications(IEnumerable<DesktopAlert> alerts, DateTimeOffset now)
    {
        if (!_notificationsEnabled) return;
        RemoveExpiredNotifications(now);
        foreach (var alert in alerts)
        {
            if (string.IsNullOrWhiteSpace(alert.Id) || _pendingNotifications.ContainsKey(alert.Id)) continue;
            if (_pendingNotifications.Count >= MaximumPendingNotifications)
            {
                var oldest = _pendingNotifications.MinBy(pair => pair.Value.Sequence).Key;
                _pendingNotifications.Remove(oldest);
            }
            _pendingNotifications.Add(alert.Id, new PendingNotification(alert, now + NotificationLifetime, ++_notificationSequence));
        }
    }

    private void RemoveExpiredNotifications(DateTimeOffset now)
    {
        foreach (var id in _pendingNotifications.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
            _pendingNotifications.Remove(id);
    }

    private async Task DeliverNotificationsAsync()
    {
        RemoveExpiredNotifications(DateTimeOffset.UtcNow);
        if (!_notificationsEnabled) { _pendingNotifications.Clear(); return; }
        // Bound delivery work per poll. Failed items rotate behind alerts not attempted yet.
        var pending = _pendingNotifications.Values.OrderBy(item => item.LastAttempt).ThenBy(item => item.Sequence).Take(3).ToArray();
        foreach (var item in pending)
        {
            if (!_notificationsEnabled || _stop.IsCancellationRequested) return;
            if (item.ExpiresAt <= DateTimeOffset.UtcNow) { _pendingNotifications.Remove(item.Alert.Id); continue; }
            bool delivered;
            try { delivered = await _notifications.SendAsync(item.Alert, _stop.Token); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return; }
            catch (Exception) { delivered = false; }
            if (!_notificationsEnabled) return;
            item.LastAttempt = ++_notificationSequence;
            if (delivered) _pendingNotifications.Remove(item.Alert.Id);
            if (!delivered) _notificationDeliveryFailed = true;
            else if (_pendingNotifications.Count == 0) _notificationDeliveryFailed = false;
        }
        UpdateNotificationMenu();
    }

    private sealed class PendingNotification(DesktopAlert alert, DateTimeOffset expiresAt, long sequence)
    {
        public DesktopAlert Alert { get; } = alert;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public long Sequence { get; } = sequence;
        public long LastAttempt { get; set; }
    }

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
                _icon.ToolTipText = FormatTooltip(snapshot);
                _quotaMenu.Items.Clear();
                foreach (var line in snapshot.Lines)
                    _quotaMenu.Items.Add(new NativeMenuItem(line) { IsEnabled = false });
                if (snapshot.Lines.Length == 0)
                    _quotaMenu.Items.Add(new NativeMenuItem(snapshot.Connected ? "No account quota information available." : "Local host unavailable.") { IsEnabled = false });
                QueueNotifications(snapshot.Alerts, DateTimeOffset.UtcNow);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return; }
            catch (Exception)
            {
                if (_stop.IsCancellationRequested) return;
                // Show unavailable status; polling retries without modal UI.
                _icon.ToolTipText = "lain · Status temporarily unavailable; open the window for details.";
                _quotaMenu.Items.Clear();
                _quotaMenu.Items.Add(new NativeMenuItem("Status temporarily unavailable. Open the window for details.") { IsEnabled = false });
            }
            // Retry remains independent of host availability; disabling never accumulates old alerts.
            await DeliverNotificationsAsync();
            try { await Task.Delay(TimeSpan.FromSeconds(10), _stop.Token); }
            catch (OperationCanceledException) { return; }
        }
    }

    private static string FormatTooltip(TrayStatusSnapshot snapshot)
    {
        static string ShortLine(string line)
        {
            var text = line.Replace('\r', ' ').Replace('\n', ' ');
            return text.Length <= 120 ? text : text[..119] + "…";
        }
        var lines = new[] { snapshot.Tooltip }.Concat(snapshot.Lines)
            .Where(line => !string.IsNullOrWhiteSpace(line)).Select(ShortLine).ToArray();
        // Eight lines of at most 120 characters plus separators remain below 1000 characters.
        return string.Join("\n", lines.Length <= 8 ? lines
            : lines.Take(7).Append("More quota details in the tray menu."));
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
        _quitUi();
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
