using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Lain.Client;
using Lain.Core;
using Lain.Desktop.Usage;
using Avalonia.Platform.Storage;
using System.Text.Json;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private Control OverviewPage()
    {
        var status = _status;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,18,*") };
        var proxyState = status is null ? "Unknown" : !_hostAvailable ? $"Last seen: {(status.ProxyReady ? "ready" : status.State)}" : status.ProxyReady ? "Connected" : "Not ready";
        grid.Children.Add(Ui.Card(Ui.Stack(Ui.Text("PROXY", 11, Ui.Muted), Ui.Text(proxyState, 26, _hostAvailable ? Ui.Accent : Ui.Muted), Ui.Text(status?.Error ?? "Your own accounts. One local connection.", 13, Ui.Muted))));
        var config = Ui.Card(Ui.Stack(Ui.Text("CONFIGURATION", 11, Ui.Muted), Ui.Text(_configuration is null ? "Unknown" : $"Revision {_configuration.Revision}", 26), Ui.Text(_configuration is null ? "Waiting for the host" : _configuration.Revision == _configuration.AppliedRevision ? "Applied to the proxy" : $"Pending · proxy uses revision {_configuration.AppliedRevision}", 13, Ui.Muted)));
        Grid.SetColumn(config, 2); grid.Children.Add(config);
        var panel = Ui.Stack(Heading("Welcome to the wired", "A quiet place for your accounts, models and usage."), grid,
            Ui.Card(Ui.Stack(Ui.Text("Make a connection", 20), Ui.Text("Import an existing configuration or add an API account. Then choose model routes and build a profile for your client.", 14, Ui.Muted), Ui.Row(Ui.Button("Manage accounts", () => Navigate("Accounts")), Ui.Button("Setup guide", () => Navigate("Setup"))))));
        if (_configuration is { } cfg) panel.Children.Add(Ui.Card(Ui.Row(Metric(cfg.Accounts.Length, "accounts"), Metric(cfg.Routes.Length, "routes"), Metric(cfg.Profiles.Length, "profiles"))));
        panel.Children.Add(Ui.Card(Ui.Stack(Ui.Text("Local proxy", 20), Ui.Text("Manage only the proxy owned by lain. Restarting or stopping may interrupt active requests.", 13, Ui.Muted), Ui.Row(
            Action("Start proxy", async () => { _status = await _client.ServiceAsync("start", _lifetime.Token); }, "Proxy start requested."),
            Ui.Button("Restart…", () => ConfirmProxyAction("restart")), Ui.Button("Stop…", () => ConfirmProxyAction("stop"))))));
        return panel;
    }

    private void ConfirmProxyAction(string action)
    {
        _editing = true;
        _page.Content = Ui.Stack(Heading($"{char.ToUpperInvariant(action[0]) + action[1..]} the lain proxy?", "Active requests may be interrupted. Your configuration and stored usage remain available."),
            Ui.Row(Action("Continue", async () => { _status = await _client.ServiceAsync(action, _lifetime.Token); }, $"Proxy {action} requested."), Ui.Button("Cancel", () => { _editing = false; Render(); })));
    }

    private static Control Metric(int value, string label) => new Border { MinWidth = 150, Child = Ui.Stack(Ui.Text(value.ToString(), 28, Ui.Accent), Ui.Text(label, 13, Ui.Muted)) };

    private Control SetupPage() => Ui.Stack(Heading("Your first connection", "lain runs locally, with a background host that stays active when this window closes."),
        Ui.Card(Ui.Stack(Ui.Text("01  ·  Start the host", 20), Ui.Text("Install and start the lain user service using the supplied installer. The management API listens locally on port 18418; inference uses port 18417.", 14, Ui.Muted), Ui.Button("Check host connection", () => _ = RefreshAsync(true)))),
        Ui.Card(Ui.Stack(Ui.Text("02  ·  Connect an account", 20), Ui.Text("Connect Codex through its authorization page, add an API key, or import a supported local configuration. Imported access-token snapshots expire; reconnect by importing a fresh snapshot. Other OAuth providers remain unavailable until qualified.", 14, Ui.Muted), Ui.Button("Manage accounts", () => Navigate("Accounts")))),
        Ui.Card(Ui.Stack(Ui.Text("03  ·  Choose models and a profile", 20), Ui.Text("Create a model route bound to your account, then choose a profile. Profile export creates a separate client configuration so your existing client setup is preserved.", 14, Ui.Muted), Ui.Row(Ui.Button("Model routes", () => Navigate("Routes")), Ui.Button("Profiles", () => Navigate("Profiles"))))));}
