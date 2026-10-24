using Avalonia;
using Avalonia.Controls;
using Lain.Desktop.Presentation;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private readonly Button _moreNavigation = Ui.Button("More", () => { });
    // Explicit configured intent wins, including when the gateway is offline.
    private bool IsMonitorMode => (_configuration?.RuntimeMode ?? _status?.RuntimeMode) == "monitor";
    private bool HasHistory => _status?.Capabilities?.LocalHistory ?? !IsMonitorMode;

    private void UpdateNavigationDestinations()
    {
        if (_moreNavigation.ContextMenu is { IsOpen: true }) return;
        var direct = IsMonitorMode
            ? HasHistory ? new[] { "Accounts", "Usage" } : new[] { "Accounts" }
            : _preferences.Level switch
            {
                InformationLevel.Barebones => new[] { "Accounts", "Profiles", "Usage" },
                InformationLevel.Simple => new[] { "Accounts", "Models", "Profiles", "Usage" },
                _ => new[] { "Accounts", "Models", "Profiles", "Usage", "Live" }
            };
        var menu = new ContextMenu();
        foreach (var button in _navigation.Children.OfType<Button>())
        {
            if (ReferenceEquals(button, _moreNavigation)) continue;
            var target = button.Tag as string ?? "";
            var allowed = target switch
            {
                "Models" => !IsMonitorMode && (_status?.Capabilities?.Inference ?? true),
                "Profiles" => !IsMonitorMode && (_status?.Capabilities?.ModelProfiles ?? true),
                "Usage" => HasHistory,
                "Live" => !IsMonitorMode && (_status?.Capabilities?.LiveInspector ?? true),
                _ => true
            };
            button.IsVisible = allowed && direct.Contains(target);
            if (allowed && !button.IsVisible)
            {
                var item = new MenuItem { Header = target };
                item.Click += (_, _) => Navigate(target);
                menu.Items.Add(item);
            }
        }
        var service = new MenuItem { Header = "Local service" };
        service.Click += (_, _) => Navigate("Service");
        menu.Items.Add(service);
        _moreNavigation.ContextMenu = menu;
        _moreNavigation.IsVisible = true;
        _navigation.Columns = _navigation.Children.Count(child => child.IsVisible);
        // Permit a compact monitor without forcing the user's window geometry.
        MinWidth = IsMonitorMode ? 640 : 800;
        MinHeight = IsMonitorMode ? 420 : 560;
        Title = IsMonitorMode ? "lain — Account Monitor" : "lain — Local AI Gateway";
    }
}
