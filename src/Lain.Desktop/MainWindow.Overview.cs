using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private Control ServicePage()
    {
        var status = _status;
        var configuration = _configuration;
        var panel = Ui.Stack(Ui.PageHeader("Local service", "Proxy operation, applied configuration and setup help.", Ui.Button("Back to accounts", () => Navigate("Accounts"))));
        panel.Spacing = 14;

        var hostState = _hostAvailable ? "Host online" : status is null ? "Host unavailable" : "Host offline";
        var proxyState = IsMonitorMode ? "Monitor · no proxy configured" : status is null ? "Proxy unknown" : status.ProxyReady ? "Proxy ready" : $"Proxy {status.State}";
        if (!_hostAvailable && status is not null) proxyState = $"Last known: {proxyState}";
        var revisionState = IsMonitorMode ? $"Monitor configuration {configuration?.Revision}" : configuration is null ? "Unavailable" : configuration.Revision == configuration.AppliedRevision
            ? $"Configuration {configuration.Revision} applied"
            : $"Configuration {configuration.Revision} pending · proxy uses {configuration.AppliedRevision}";
        if (!_hostAvailable && configuration is not null) revisionState = $"Last known: {revisionState}";
        var start = Action("Start proxy", async () => { _status = await _client.ServiceAsync("start", _lifetime.Token); }, "Proxy start requested.");
        var restart = Ui.Button("Restart…", () => ConfirmProxyAction("restart"));
        var stop = Ui.Button("Stop…", () => ConfirmProxyAction("stop"));
        start.IsEnabled = restart.IsEnabled = stop.IsEnabled = _hostAvailable && !IsMonitorMode;
        var service = new StackPanel { Spacing = 8 };
        var serviceHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };
        var serviceTitle = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var serviceLabel = Ui.Text("Local service", 13); serviceLabel.FontWeight = FontWeight.SemiBold;
        serviceLabel.Classes.Add("section-title");
        serviceTitle.Children.Add(serviceLabel);
        serviceTitle.Children.Add(Ui.Text($"{hostState} · {proxyState}", 12, _hostAvailable && status?.ProxyReady == true ? Ui.Success : Ui.Muted));
        serviceHeader.Children.Add(serviceTitle);
        var serviceActions = OverviewActions(start, restart, stop);
        serviceActions.IsVisible = !IsMonitorMode;
        serviceActions.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(serviceActions, 1); serviceHeader.Children.Add(serviceActions);
        service.Children.Add(serviceHeader);
        var serviceFooter = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };
        var revision = Ui.Text(revisionState, 12, Ui.Muted);
        revision.VerticalAlignment = VerticalAlignment.Center;
        serviceFooter.Children.Add(revision);
        if (configuration is null)
        {
            var check = Ui.Button("Check connection", () => _ = RefreshAsync(true));
            Grid.SetColumn(check, 1); serviceFooter.Children.Add(check);
        }
        else
        {
            var inventory = OverviewActions(
                Ui.Button($"{configuration.Accounts.Count(x => x.Enabled)} accounts enabled", () => Navigate("Accounts")),
                Ui.Button($"{configuration.Routes.Length} routes", () => Navigate("Routes")),
                Ui.Button($"{configuration.Profiles.Length} profiles", () => Navigate("Profiles")));
            Grid.SetColumn(inventory, 1); serviceFooter.Children.Add(inventory);
        }
        service.Children.Add(serviceFooter);
        if (!string.IsNullOrWhiteSpace(status?.Error))
            service.Children.Add(Ui.Text(_hostAvailable ? status.Error : $"Last host error: {status.Error}", 12, Ui.Warning));
        panel.Children.Add(Ui.Card(service));
        if (!_hostAvailable)
            panel.Children.Add(OverviewNext("Connect to the host", "Start the lain user service, then refresh to see the current state.", "Setup guide", "Setup"));
        else if (configuration is { Accounts.Length: 0 })
            panel.Children.Add(OverviewNext("Connect an account", "Add an API key, authorize Codex or import a supported configuration.", "Add account", "Accounts"));
        else if (configuration is not null && !configuration.Accounts.Any(x => x.Enabled))
            panel.Children.Add(OverviewNext("Enable an account", "A route needs an enabled account to serve requests.", "Manage accounts", "Accounts"));
        else if (!IsMonitorMode && configuration is { Routes.Length: 0 })
            panel.Children.Add(OverviewNext("Create a model route", "Choose an upstream model and the account that will serve it.", "Model routes", "Routes"));
        else if (!IsMonitorMode && configuration is not null && !configuration.Routes.Any(x => x.Enabled))
            panel.Children.Add(OverviewNext("Enable a model route", "Enable a route before using it in a client profile.", "Model routes", "Routes"));
        else if (!IsMonitorMode && configuration is { Profiles.Length: 0 })
            panel.Children.Add(OverviewNext("Create a profile", "Select routes and export a separate configuration for your client.", "Profiles", "Profiles"));
        return panel;
    }

    private void ConfirmProxyAction(string action)
    {
        _editing = true;
        _page.Content = Ui.Stack(Heading($"{char.ToUpperInvariant(action[0]) + action[1..]} the lain proxy?", "Active requests may be interrupted. Your configuration and stored usage remain available."),
            OverviewActions(Action($"{char.ToUpperInvariant(action[0]) + action[1..]} proxy", async () => { _status = await _client.ServiceAsync(action, _lifetime.Token); }, $"Proxy {action} requested."), Ui.Button("Cancel", () => { _editing = false; Render(); })));
    }

    private Control SetupPage()
    {
        var configuration = _configuration;
        var hostState = _hostAvailable ? "Online" : "Connection required";
        var accountsState = configuration is null ? "Not loaded" : $"{configuration.Accounts.Length} configured";
        var routingState = configuration is null ? "Not loaded" : $"{configuration.Routes.Length} routes · {configuration.Profiles.Length} profiles";
        if (!_hostAvailable && configuration is not null)
        {
            accountsState = $"Last known: {accountsState}";
            routingState = $"Last known: {routingState}";
        }
        return Ui.Stack(Ui.PageHeader("Getting started", "Connect an account, choose models and configure your client.", Ui.Button("Back to accounts", () => Navigate("Accounts"))),
            Ui.Card(OverviewGroup("1. Connect to the local host",
                Ui.Text(hostState, 13, _hostAvailable ? Ui.Accent : Ui.Muted),
                Ui.Text("Run lain service start in your terminal. The host keeps running when this window closes.", 13, Ui.Muted),
                OverviewActions(Ui.Button("Check connection", () => _ = RefreshAsync(true))))),
            Ui.Card(OverviewGroup("2. Add an account",
                Ui.Text(accountsState, 13, Ui.Muted),
                Ui.Text("Authorize Codex, add an API key or import a supported local configuration.", 13, Ui.Muted),
                Ui.Text("Imported access-token snapshots expire; import a fresh snapshot to reconnect. Other OAuth providers are not yet available.", 12, Ui.Muted),
                OverviewActions(Ui.Button("Manage accounts", () => Navigate("Accounts"))))),
            Ui.Card(OverviewGroup("3. Choose models and export a profile",
                Ui.Text(routingState, 13, Ui.Muted),
                Ui.Text("Bind a model route to your account, then include it in a profile. Export creates a separate client configuration.", 13, Ui.Muted),
                OverviewActions(Ui.Button("Model routes", () => Navigate("Routes")), Ui.Button("Profiles", () => Navigate("Profiles"))))));
    }

    private static StackPanel OverviewGroup(string title, params Control[] controls)
    {
        var panel = new StackPanel { Spacing = 9 };
        var heading = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Ui.Ink, Margin = new Thickness(0, 0, 0, 3), TextWrapping = TextWrapping.Wrap };
        heading.Classes.Add("section-title");
        panel.Children.Add(heading);
        foreach (var control in controls) panel.Children.Add(control);
        return panel;
    }

    private static Control OverviewValue(string label, string value, bool accent = false)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("112,*"), ColumnSpacing = 12 };
        row.Children.Add(Ui.Text(label, 13, Ui.Muted));
        var text = Ui.Text(value, 13, accent ? Ui.Accent : Ui.Ink);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        return row;
    }

    private Control OverviewInventory(string label, string value, string section)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12, Margin = new Thickness(0, 2) };
        var details = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        details.Children.Add(Ui.Text(label, 13));
        details.Children.Add(Ui.Text(value, 12, Ui.Muted));
        row.Children.Add(details);
        var button = Ui.Button("Manage", () => Navigate(section));
        button.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(button, 1);
        row.Children.Add(button);
        return row;
    }

    private Control OverviewNext(string title, string detail, string action, string section) =>
        Ui.Card(OverviewGroup(title, Ui.Text(detail, 13, Ui.Muted), OverviewActions(Ui.Button(action, () => Navigate(section)))));

    private static WrapPanel OverviewActions(params Control[] controls)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var control in controls)
        {
            control.Margin = new Thickness(0, 0, 8, 4);
            row.Children.Add(control);
        }
        return row;
    }
}
