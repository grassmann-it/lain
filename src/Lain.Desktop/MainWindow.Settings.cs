using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lain.Desktop.Presentation;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private static readonly string[] DensitySections = ["Accounts", "Models", "Profiles", "Usage", "Live"];
    private string _settingsDensitySection = "Accounts";
    private ComboBox? _settingsDensity;
    private TextBlock? _settingsTrayHint;
    private readonly Dictionary<CheckBox, Func<WorkspacePreferences, bool>> _settingsToggles = new();

    private Control SettingsPage()
    {
        var section = new ComboBox
        {
            ItemsSource = DensitySections, SelectedItem = _settingsDensitySection,
            MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var density = new ComboBox { ItemsSource = Enum.GetNames<ViewDensity>(), MinWidth = 125 };
        _settingsDensity = density;
        var densityRow = new WrapPanel { Orientation = Orientation.Horizontal };
        var sectionField = Ui.Field("Density section", section);
        sectionField.Margin = new Avalonia.Thickness(0, 0, 18, 8);
        densityRow.Children.Add(sectionField);
        densityRow.Children.Add(Ui.Field("View density", density));
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
            SavePreferences(p => p with
            {
                Densities = new Dictionary<string, ViewDensity>(p.Densities, StringComparer.Ordinal)
                    { [_settingsDensitySection] = selected }
            }, render: false);
        };

        _settingsToggles.Clear();
        CheckBox BackgroundOption(string label, Func<WorkspacePreferences, bool> value,
            Func<WorkspacePreferences, bool, WorkspacePreferences> change)
        {
            var toggle = new CheckBox { Content = label };
            AutomationProperties.SetName(toggle, label);
            _settingsToggles.Add(toggle, value);
            toggle.IsCheckedChanged += (_, _) =>
            {
                if (_viewOptionsUpdating) return;
                SavePreferences(p => change(p, toggle.IsChecked == true), render: false);
            };
            return toggle;
        }
        var minimize = BackgroundOption("Minimize to tray", p => p.MinimizeToTray, (p, value) => p with { MinimizeToTray = value });
        var close = BackgroundOption("Close to tray", p => p.CloseToTray, (p, value) => p with { CloseToTray = value });
        var alerts = BackgroundOption("Usage and rate-limit alerts", p => p.NotificationsEnabled, (p, value) => p with { NotificationsEnabled = value });
        _settingsTrayHint = Ui.Text("", 12, Ui.Muted);
        AutomationProperties.SetName(_settingsTrayHint, "Tray availability");

        var about = Ui.Button("About lain", () => Navigate("About"));
        AutomationProperties.SetName(about, "About lain");
        var page = Ui.Stack(
            Heading("Settings", "Changes are saved locally for this workspace."),
            Ui.Card(Ui.Stack(Ui.Text("Density", 15), densityRow,
                Ui.Text("Choose Normal or Compact independently for each main area. The information level remains available in the bottom navigation.", 12, Ui.Muted))),
            Ui.Card(Ui.Stack(Ui.Text("Background and notifications", 15), minimize, close, alerts, _settingsTrayHint,
                Ui.Text("These window options do not stop the independently running lain host.", 12, Ui.Muted))),
            Ui.Card(Ui.Stack(Ui.Text("Help and application", 15),
                Ui.Row(Ui.Button("Getting started", () => Navigate("Setup")), about))));
        page.MaxWidth = 820;
        page.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(page, "Settings page");
        SynchronizeSettings();
        return page;
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
            _settingsDensity.SelectedIndex = (int)preferences.Densities.GetValueOrDefault(_settingsDensitySection, ViewDensity.Normal);
            foreach (var (toggle, value) in _settingsToggles) toggle.IsChecked = value(preferences);
            if (_settingsTrayHint is not null)
                _settingsTrayHint.Text = _trayController?.TrayAvailable == true
                    ? "Tray available. Alerts keep working while the window is hidden. Choose Quit UI from the tray menu to exit."
                    : "Tray unavailable. The window uses normal minimize and close behavior until a tray is available.";
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
        AutomationProperties.SetName(versionText, "Application version");
        const string repository = "https://github.com/grassmann-it/lain";
        var projectText = Ui.Text(repository, 13);
        AutomationProperties.SetName(projectText, "Project repository");
        var project = Ui.Button("Open project repository", () => _ = OpenProviderLinkAsync(new Uri(repository)));
        AutomationProperties.SetName(project, "Open project repository");
        var licenses = Ui.Text(
            "Application license: no application-wide license is declared in the repository.\n\n" +
            "Bundled third-party license records include MIT (.NET, Avalonia, Semi, Ursa, Irihi and CLIProxyAPI Core), " +
            "Apache 2.0 (SQLitePCLRaw), and SIL Open Font License 1.1 (IBM Plex Sans Condensed and Oxanium). " +
            "Full component and runtime notices are included in the package's notices directory.\n\n" +
            "The cat artwork is a contrast-adjusted variant of the user-supplied photo; its provenance records no separate license statement.", 12, Ui.Muted);
        AutomationProperties.SetName(licenses, "License information");
        var page = Ui.Stack(
            Heading("About lain", "Local AI Gateway"),
            Ui.Card(Ui.Stack(Ui.Row(logo, brand), Ui.Text("Application version", 12, Ui.Muted), versionText)),
            Ui.Card(Ui.Stack(Ui.Text("Project", 15), projectText, project)),
            Ui.Card(Ui.Stack(Ui.Text("Licenses and credits", 15), licenses)));
        page.MaxWidth = 820;
        page.HorizontalAlignment = HorizontalAlignment.Left;
        AutomationProperties.SetName(page, "About page");
        return page;
    }
}
