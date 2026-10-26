using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Lain.Desktop.Presentation;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private static readonly string[] DensitySections = ["Accounts", "Models", "Profiles", "Usage", "Live"];
    private string _settingsDensitySection = "Accounts";
    private ComboBox? _settingsDensity;
    private TextBlock? _settingsTrayHint;
    private TextBlock? _settingsDensityLabel;
    private readonly Dictionary<CheckBox, Func<WorkspacePreferences, bool>> _settingsToggles = new();
    private readonly Dictionary<string, (StackPanel Panel, TextBlock Message)> _settingsSaveFeedback = new(StringComparer.Ordinal);
    private SettingsSaveAttempt? _settingsSaveAttempt;
    private sealed record SettingsSaveAttempt(string Key, string Label, string Requested,
        Func<WorkspacePreferences, WorkspacePreferences> Change, string? Error = null);

    private Control SettingsSaveFeedback(string key)
    {
        var message = Ui.Text("", 12, Ui.Warning);
        AutomationProperties.SetName(message, key + " save error");
        AutomationProperties.SetLiveSetting(message, AutomationLiveSetting.Polite);
        var retry = Ui.Button("Retry saving", () => {
            if (_settingsSaveAttempt is { } attempt && attempt.Key == key) SaveSettingsPreference(attempt);
        });
        AutomationProperties.SetName(retry, "Retry saving " + key);
        var panel = new StackPanel { Spacing = 4, IsVisible = false, Children = { message, retry } };
        _settingsSaveFeedback[key] = (panel, message);
        return panel;
    }

    private void SaveSettingsPreference(SettingsSaveAttempt attempt)
    {
        var previousError = _settingsSaveAttempt?.Error;
        if (SavePreferences(attempt.Change, render: false))
        {
            _settingsSaveAttempt = null;
            if (_notice.Text == previousError) _notice.Text = "";
        }
        else
        {
            _settingsSaveAttempt = attempt with { Error = _preferencesStore.Error ?? "Settings could not be saved." };
            // Settings explain the failure beside its control, rather than duplicating it in the shell.
            if (_notice.Text == _settingsSaveAttempt.Error) _notice.Text = "";
        }
        SynchronizeSettingsSaveFeedback();
        if (_settingsSaveAttempt is { } pending && _settingsSaveFeedback.TryGetValue(pending.Key, out var feedback))
            Dispatcher.UIThread.Post(() => feedback.Panel.BringIntoView(), DispatcherPriority.Loaded);
    }

    private void SynchronizeSettingsSaveFeedback()
    {
        foreach (var (key, feedback) in _settingsSaveFeedback)
        {
            var attempt = _settingsSaveAttempt;
            feedback.Panel.IsVisible = attempt?.Key == key;
            feedback.Message.Text = attempt?.Key == key
                ? $"{attempt.Label} could not be saved. Requested: {attempt.Requested}. {attempt.Error} Retry saves this requested choice."
                : "";
        }
    }

    private Control SettingsPage()
    {
        _settingsSaveFeedback.Clear();
        var section = new ComboBox
        {
            ItemsSource = DensitySections, SelectedItem = _settingsDensitySection,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(section, "Density section");
        var density = new ComboBox { ItemsSource = Enum.GetNames<ViewDensity>(), HorizontalAlignment = HorizontalAlignment.Stretch };
        _settingsDensity = density;
        _settingsDensityLabel = Ui.Text("Density for " + _settingsDensitySection, 12, Ui.Muted);
        var densityRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 16 };
        var sectionField = new StackPanel { Spacing = 4, Children = { _settingsDensityLabel, section } };
        densityRow.Children.Add(sectionField);
        var densityField = Ui.Field("Density", density);
        Grid.SetColumn(densityField, 1);
        densityRow.Children.Add(densityField);
        section.SelectionChanged += (_, _) =>
        {
            if (_viewOptionsUpdating || section.SelectedItem is not string selected) return;
            _settingsDensitySection = selected;
            SynchronizeSettings();
        };
        density.SelectionChanged += (_, _) =>
        {
            if (_viewOptionsUpdating || density.SelectedIndex < 0) return;
            var selected = (ViewDensity)density.SelectedIndex;
            var selectedSection = _settingsDensitySection;
            SaveSettingsPreference(new("density", "Density for " + selectedSection, selected.ToString(), p => p with
            {
                Densities = new Dictionary<string, ViewDensity>(p.Densities, StringComparer.Ordinal)
                    { [selectedSection] = selected }
            }));
        };

        _settingsToggles.Clear();
        Control BackgroundOption(string label, string description, Func<WorkspacePreferences, bool> value,
            Func<WorkspacePreferences, bool, WorkspacePreferences> change)
        {
            var labelContent = new StackPanel { Spacing = 1, Children = { Ui.Text(label), Ui.Text(description, 12, Ui.Muted) } };
            var toggle = new CheckBox { Content = labelContent, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 36 };
            AutomationProperties.SetHelpText(toggle, description);
            AutomationProperties.SetName(toggle, label);
            _settingsToggles.Add(toggle, value);
            toggle.IsCheckedChanged += (_, _) =>
            {
                if (_viewOptionsUpdating) return;
                var requested = toggle.IsChecked == true;
                SaveSettingsPreference(new(label, label, requested ? "On" : "Off", p => change(p, requested)));
            };
            return new StackPanel { Spacing = 4, Children = { toggle, SettingsSaveFeedback(label) } };
        }
        var minimize = BackgroundOption("Minimize to tray", "Hide this window when minimized, when a tray is available.", p => p.MinimizeToTray, (p, value) => p with { MinimizeToTray = value });
        var close = BackgroundOption("Close to tray", "Keep the UI in the tray when its window is closed.", p => p.CloseToTray, (p, value) => p with { CloseToTray = value });
        var alerts = BackgroundOption("Usage and rate-limit alerts", "Show local usage and rate-limit notifications.", p => p.NotificationsEnabled, (p, value) => p with { NotificationsEnabled = value });
        _settingsTrayHint = Ui.Text("", 12, Ui.Muted);
        AutomationProperties.SetName(_settingsTrayHint, "Tray availability");

        var help = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var button in new[] {
            Ui.Button("Getting started", () => Navigate("Setup")),
            Ui.Button("About lain", () => Navigate("About")),
            Ui.Button("Host operating mode…", ShowRuntimeModeSettings) })
        {
            button.Margin = new Avalonia.Thickness(0, 0, 8, 0);
            AutomationProperties.SetName(button, button.Content?.ToString());
            help.Children.Add(button);
        }
        var densityGroup = Ui.Section("View density", new StackPanel { Spacing = 5, Children = {
            densityRow, SettingsSaveFeedback("density"), Ui.Text("Saved locally for each page; also available in that page's View menu.", 12, Ui.Muted) } });
        var windowGroup = Ui.Section("Window and notifications", new StackPanel { Spacing = 4,
            Children = { minimize, close, alerts, _settingsTrayHint } });
        var helpGroup = Ui.Section("Help and application", new StackPanel { Spacing = 5, Children = {
            help, Ui.Text("Host mode changes are reviewed separately and saved to the host configuration.", 12, Ui.Muted) } });
        AutomationProperties.SetName(densityGroup, "View density settings");
        AutomationProperties.SetName(windowGroup, "Window and notification settings");
        AutomationProperties.SetName(helpGroup, "Help and application settings");
        var page = new StackPanel { Spacing = 18, Children = {
            Heading("Settings", "Local window preferences. Changes are saved as you choose them."),
            densityGroup, windowGroup, helpGroup } };
        page.MaxWidth = 760;
        page.HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(page, "Settings page");
        SynchronizeSettings();
        return page;
    }

    private void ShowRuntimeModeSettings()
    {
        var cfg = _configuration;
        var knownMode = cfg?.RuntimeMode is "monitor" or "gateway";
        var current = knownMode ? cfg!.RuntimeMode : "unknown";
        var target = current == "monitor" ? "gateway" : current == "gateway" ? "monitor" : "";
        var review = Ui.Button(knownMode ? "Review switch to " + target : "Host mode unavailable", () => {
            if (cfg is null || !_hostAvailable || !knownMode) return;
            _editing = true;
            var apply = Action("Change host mode", async () => {
                // A stale review never retries with a newer revision automatically.
                if (!_hostAvailable || _configuration?.Revision != cfg.Revision)
                    throw new InvalidOperationException("Host configuration changed or is unavailable. Reload status, then review the mode change again.");
                _status = await _client.SetRuntimeModeAsync(new Lain.Core.RuntimeModeRequest(cfg.Revision, target), _lifetime.Token);
                _configuration = await _client.GetConfigurationAsync(_lifetime.Token);
                UpdateNavigationDestinations();
            }, "Host operating mode changed.");
            _page.Content = Ui.Stack(Heading("Change host to " + target + "?", "This saves a change to host configuration revision " + cfg.Revision + "."),
                Ui.Text("The proxy Core may stop or start. Active requests can be interrupted. Existing accounts, profiles and recorded history are preserved.", 13, Ui.Warning),
                OverviewActions(apply, Ui.Button("Cancel", () => { _editing = false; Render(); })));
        });
        review.IsEnabled = _hostAvailable && knownMode;
        AutomationProperties.SetName(review, "Review host mode change");
        var hostPage = Ui.Stack(Heading("Host operating mode", !knownMode ? "Configured mode unavailable" : (_hostAvailable ? "Configured mode: " : "Last known configured mode: ") + current),
            Ui.Text("Monitor reads account quotas and available saved history. Gateway adds model access, profiles and the optional proxy Core.", 13),
            Ui.Text("This changes the independently running host. Window density, information level and tray preferences stay local.", 12, Ui.Muted),
            Ui.Text(_hostAvailable && knownMode ? "Review the effects before confirming a switch." : "Host configuration is unavailable. Reload status before reviewing a mode change.", 12, Ui.Muted),
            review, Ui.Button("Back to Settings", () => { _editing = false; Render(); }));
        hostPage.MaxWidth = 760;
        hostPage.HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(hostPage, "Host operating mode settings");
        _page.Content = hostPage;
    }

    private void SynchronizeSettings()
    {
        if (_section != "Settings" || _settingsDensity is null) return;
        var wasUpdating = _viewOptionsUpdating;
        _viewOptionsUpdating = true;
        try
        {
            var preferences = _preferences;
            AutomationProperties.SetName(_settingsDensity, "Density for " + _settingsDensitySection);
            if (_settingsDensityLabel is not null) _settingsDensityLabel.Text = "Density for " + _settingsDensitySection;
            _settingsDensity.SelectedIndex = (int)preferences.Densities.GetValueOrDefault(_settingsDensitySection, ViewDensity.Normal);
            foreach (var (toggle, value) in _settingsToggles) toggle.IsChecked = value(preferences);
            SynchronizeSettingsSaveFeedback();
            if (_settingsTrayHint is not null)
                _settingsTrayHint.Text = _trayController?.TrayAvailable == true
                    ? "Tray available. Minimize and close use your independent choices. Hiding or closing the UI does not stop the host."
                    : "Tray unavailable — normal minimize and close behavior is used. Your tray preferences are kept. Closing the UI does not stop the host.";
        }
        finally { _viewOptionsUpdating = wasUpdating; }
    }

    private Control AboutPage()
    {
        var brand = Ui.Text("lain", 28);
        brand.FontFamily = Ui.BrandFont;
        brand.VerticalAlignment = VerticalAlignment.Center;
        var logo = new Image { Source = Branding.Logo, Width = 88, Height = 88, Stretch = Stretch.Uniform };
        AutomationProperties.SetName(logo, "About application logo");
        var assembly = typeof(MainWindow).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString() ?? "Unknown";
        var versionText = Ui.Text(version, 13);
        var hostVersion = Ui.Text(_hostAvailable && _status is not null ? _status.Version : "Unavailable · host offline", 13);
        AutomationProperties.SetName(hostVersion, "Reported host version");
        AutomationProperties.SetName(versionText, "Application version");
        const string repository = "https://github.com/grassmann-it/lain";
        var projectText = Ui.Text(repository, 13);
        AutomationProperties.SetName(projectText, "Project repository");
        var project = Ui.Button("Open project repository", () => _ = OpenProviderLinkAsync(new Uri(repository)));
        AutomationProperties.SetName(project, "Open project repository");
        var licenses = Ui.Text(
            "Application license: Artistic License 2.0. Copyright © 2026 Tom Graßmann. " +
            "See LICENSE and NOTICE in the package; the exact source is included in source/lain-source.tar.gz.\n\n" +
            "Bundled third-party license records include MIT (.NET, Avalonia, Semi, Ursa, Irihi and CLIProxyAPI Core), " +
            "Apache 2.0 (SQLitePCLRaw), and SIL Open Font License 1.1 (IBM Plex Sans Condensed and Oxanium). " +
            "Full component and runtime notices are included in the package's notices directory.\n\n" +
            "Cat photograph: Alexander Huxel · Webentwicklung Huxel. " +
            "Edited for lain and used with permission; separate from the application license.", 12, Ui.Muted);
        AutomationProperties.SetName(licenses, "License information");
        var photographer = Ui.Button("(Flickr)", () =>
            _ = OpenProviderLinkAsync(new Uri("https://flickr.com/people/204852984@N05/")));
        AutomationProperties.SetName(photographer, "Open photographer profile");
        var photographerWebsite = Ui.Button("Alexander Huxel · Webentwicklung Huxel", () =>
            _ = OpenProviderLinkAsync(new Uri("https://webentwicklung-huxel.de/")));
        AutomationProperties.SetName(photographerWebsite, "Open photographer website");
        var page = Ui.Stack(
            Heading("About lain", "Account monitoring and local AI gateway"),
            Ui.Card(Ui.Stack(Ui.Row(logo, brand), Ui.Text("Desktop build", 12, Ui.Muted), versionText,
                Ui.Text("Host version · reported by the connected host", 12, Ui.Muted), hostVersion)),
            Ui.Card(Ui.Stack(Ui.Text("Project", 15), projectText, project)),
            Ui.Card(Ui.Stack(Ui.Text("Licenses and credits", 15), licenses, Ui.Row(photographerWebsite, photographer))));
        page.MaxWidth = 820;
        page.HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(page, "About page");
        return page;
    }
}
