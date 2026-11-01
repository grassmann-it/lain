using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Lain.Client.Lifecycle;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private CheckBox? _startOnOpenToggle;
    private CheckBox? _loginAutostartToggle;
    private TextBlock? _startupSettingsHint;
    private TextBlock? _startupSettingsFailure;
    private Button? _startupSettingsRetry;
    private Button? _startupSettingsServiceRetry;
    private bool _startupSettingsUpdating;
    private bool _startupSettingsBusy;
    private string? _startupSettingsError;
    private (bool Login, bool Enabled)? _startupSettingsFailedChoice;

    private Control StartupSettingsGroup()
    {
        CheckBox Toggle(string label, string description)
        {
            var toggle = new CheckBox { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 36,
                Content = new StackPanel { Spacing = 1, Children = { Ui.Text(label), Ui.Text(description, 12, Ui.Muted) } } };
            AutomationProperties.SetName(toggle, label);
            AutomationProperties.SetHelpText(toggle, description);
            return toggle;
        }
        _startOnOpenToggle = Toggle("Start background service when opening Lain",
            "Check and start the service on launch or reopen. This does not change sign-in startup.");
        _loginAutostartToggle = Toggle("Start background service at sign-in",
            "Change the user service manager's sign-in setting. Turning this off keeps the current service running.");
        _startOnOpenToggle.IsCheckedChanged += (_, _) =>
        {
            if (!_startupSettingsUpdating) _ = SaveStartupChoiceAsync(login: false, _startOnOpenToggle.IsChecked == true);
        };
        _loginAutostartToggle.IsCheckedChanged += (_, _) =>
        {
            if (!_startupSettingsUpdating && _loginAutostartToggle.IsChecked is { } enabled)
                _ = SaveStartupChoiceAsync(login: true, enabled);
        };
        _startupSettingsHint = Ui.Text("", 12, Ui.Muted);
        AutomationProperties.SetName(_startupSettingsHint, "Background startup settings status");
        _startupSettingsFailure = Ui.Text("", 12, Ui.Warning);
        AutomationProperties.SetName(_startupSettingsFailure, "Background startup settings error");
        AutomationProperties.SetLiveSetting(_startupSettingsFailure, AutomationLiveSetting.Polite);
        _startupSettingsRetry = Ui.Button("Retry startup setting", () =>
        {
            if (_startupSettingsFailedChoice is { } choice) _ = SaveStartupChoiceAsync(choice.Login, choice.Enabled);
        });
        AutomationProperties.SetName(_startupSettingsRetry, "Retry startup setting");
        var retryService = _startupSettingsServiceRetry = Ui.Button("Retry service startup", () => _ = RetryServiceStartupAsync());
        AutomationProperties.SetName(retryService, "Start background service now");
        retryService.IsEnabled = _startupServices is not null && !_startupInProgress;
        var group = Ui.Section("Background service startup", new StackPanel { Spacing = 5, Children = {
            _startOnOpenToggle, _loginAutostartToggle, _startupSettingsHint,
            _startupSettingsFailure, _startupSettingsRetry, retryService,
            Ui.Text("These choices are independent of tray behavior and proxy Start or Stop. Closing the window keeps the background service running.", 12, Ui.Muted) } });
        AutomationProperties.SetName(group, "Background startup settings");
        SynchronizeStartupSettings();
        return group;
    }

    private async Task SaveStartupChoiceAsync(bool login, bool enabled)
    {
        if (_startupSettingsBusy || _startupServices is null) return;
        _startupSettingsBusy = true;
        _startupSettingsError = null;
        SynchronizeStartupSettings();
        var cancellationToken = _lifetime.Token;
        try
        {
            if (login)
            {
                var result = await Task.Run(() => _startupServices.SetLoginAutostartAsync(enabled, cancellationToken), cancellationToken);
                // Read the manager's authoritative value. A successful process invocation
                // with an unchanged or unknown state is not a successful toggle.
                _startupStatus = result;
                _startupManagerRevision++;
                if (result.Manager.LoginAutostart != enabled)
                    throw new InvalidOperationException(result.Manager.Error ?? result.Error ?? "The service manager did not confirm the requested sign-in startup choice.");
            }
            else
            {
                var requested = _startupPreferences with { StartOnOpen = enabled };
                await Task.Run(() => _startupServices.Preferences.WriteAsync(requested, cancellationToken), cancellationToken);
                _startupPreferences = requested;
                _startupPreferencesRevision++;
                _startupPreferencesLoaded = true;
                _startupPreferencesError = null;
            }
            _startupSettingsFailedChoice = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
        catch (Exception error)
        {
            _startupSettingsFailedChoice = (login, enabled);
            var name = login ? "Sign-in startup" : "Startup when opening Lain";
            _startupSettingsError = $"{name} could not be saved. Requested: {(enabled ? "On" : "Off")}. {Friendly(error)}";
        }
        finally
        {
            _startupSettingsBusy = false;
            if (!cancellationToken.IsCancellationRequested) SynchronizeStartupSettings();
        }
    }

    private void SynchronizeStartupSettings()
    {
        if (_startOnOpenToggle is null || _loginAutostartToggle is null) return;
        _startupSettingsUpdating = true;
        try
        {
            _startOnOpenToggle.IsChecked = _startupPreferences.StartOnOpen;
            _loginAutostartToggle.IsChecked = _startupStatus?.Manager.LoginAutostart;
            _startOnOpenToggle.IsEnabled = _startupServices is not null && !_startupSettingsBusy && (_startupPreferencesLoaded || _startupPreferencesError is not null);
            _loginAutostartToggle.IsEnabled = _startupServices is not null && !_startupSettingsBusy && _startupStatus is not null;
            if (_startupSettingsHint is not null)
                _startupSettingsHint.Text = _startupServices is null ? "Startup management is unavailable in this window."
                    : _startupInProgress ? "Checking the background service. Settings remain available."
                    : _startupStatus is { } status
                        ? $"Service manager: {status.Manager.Manager} · {status.Manager.RegistrationState} · Sign-in startup: {(status.Manager.LoginAutostart is { } enabled ? enabled ? "On" : "Off" : "Unknown")}."
                        : "Service manager status is unavailable. Retry service startup to check it.";
            if (_startupSettingsFailure is not null)
            {
                _startupSettingsFailure.Text = _startupSettingsError ?? _startupPreferencesError ?? "";
                _startupSettingsFailure.IsVisible = !string.IsNullOrWhiteSpace(_startupSettingsFailure.Text);
            }
            if (_startupSettingsServiceRetry is not null) _startupSettingsServiceRetry.IsEnabled = _startupServices is not null && !_startupInProgress;
            if (_startupSettingsRetry is not null)
            {
                _startupSettingsRetry.IsVisible = _startupSettingsFailedChoice is not null;
                _startupSettingsRetry.IsEnabled = !_startupSettingsBusy;
            }
        }
        finally { _startupSettingsUpdating = false; }
    }
}
