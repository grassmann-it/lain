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

/// <summary>The management window is a client of the independently running host.</summary>
public sealed class MainWindow : Window
{
    private readonly LainApiClient _client;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ContentControl _page = new();
    private readonly TextBlock _connection = Ui.Text("Connecting…", 12, Ui.Muted);
    private readonly TextBlock _notice = Ui.Text("", 13, Ui.Accent);
    private readonly StackPanel _navigation = new() { Spacing = 7 };
    private readonly List<Button> _actions = [];
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(10) };
    private ConfigurationDto? _configuration;
    private QuotaDto[] _quotas = [];
    private string? _quotaError;
    private StatusDto? _status;
    private string _section = "Overview";
    private bool _busy;
    private bool _refreshing;
    private bool _editing;
    private bool _hostAvailable;
    private readonly UsageView _usage;

    public MainWindow(LainApiClient client)
    {
        _client = client;
        _usage = new UsageView(client);
        Title = "lain · the wired";
        Width = 1220; Height = 850; MinWidth = 880; MinHeight = 640;
        Background = Brush.Parse("#0A1118");
        var shell = new Grid { ColumnDefinitions = new ColumnDefinitions("215,*") };
        var brand = Ui.Stack(Ui.Text("lain", 38, Ui.Accent), Ui.Text("PERSONAL CONNECTION LAYER", 10, Ui.Muted));
        foreach (var name in new[] { "Overview", "Accounts", "Routes", "Profiles", "Usage", "Setup" })
        {
            var nav = Ui.Button(name, () => Navigate(name));
            nav.HorizontalAlignment = HorizontalAlignment.Stretch;
            nav.HorizontalContentAlignment = HorizontalAlignment.Left;
            _navigation.Children.Add(nav);
        }
        var side = new DockPanel { LastChildFill = true };
        var footer = Ui.Stack(Ui.Text("STAY CONNECTED", 10, Ui.Accent), Ui.Text("Closing this window keeps your host and usage capture running.", 12, Ui.Muted));
        DockPanel.SetDock(footer, Dock.Bottom); side.Children.Add(footer);
        side.Children.Add(Ui.Stack(brand, new Border { Height = 20 }, _navigation));
        shell.Children.Add(new Border { Background = Brush.Parse("#0D171F"), BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(22, 30), Child = side });
        var body = new DockPanel { Margin = new Thickness(34, 26) };
        var refresh = Ui.Button("Refresh", () => _ = RefreshAsync(true));
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        DockPanel.SetDock(refresh, Dock.Right); header.Children.Add(refresh); header.Children.Add(_connection);
        DockPanel.SetDock(header, Dock.Top); body.Children.Add(header);
        var noticeBox = new Border { Child = _notice, Padding = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(noticeBox, Dock.Top); body.Children.Add(noticeBox);
        body.Children.Add(new ScrollViewer { Content = _page, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Grid.SetColumn(body, 1); shell.Children.Add(body); Content = shell;
        Opened += async (_, _) => { await RefreshAsync(true); _poll.Start(); };
        _poll.Tick += async (_, _) => await RefreshAsync(false);
        Closed += (_, _) => { _poll.Stop(); _lifetime.Cancel(); _client.Dispose(); _lifetime.Dispose(); };
        Render();
    }

    private void Navigate(string section) { _section = section; _editing = false; _notice.Text = ""; Render(); }

    private async Task RefreshAsync(bool explicitRefresh)
    {
        if (_refreshing || _busy) return;
        _refreshing = true;
        try
        {
            var status = await _client.StatusAsync(_lifetime.Token);
            _status = status;
            _hostAvailable = true;
            _connection.Text = $"LOCAL HOST  ·  {status.State}  ·  observed {Ui.When(status.ObservedAt)}";
            _connection.Foreground = status.ProxyReady ? Ui.Accent : Ui.Muted;
            if (!_editing || explicitRefresh)
            {
                _configuration = await _client.GetConfigurationAsync(_lifetime.Token);
                try { _quotas = await _client.QuotasAsync(_lifetime.Token); _quotaError = null; }
                catch (Exception error) when (error is not OperationCanceledException) { _quotaError = Friendly(error); }
            }
            if (explicitRefresh) { _editing = false; _notice.Text = ""; }
            if (!_editing && _section != "Usage") Render();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _hostAvailable = false;
            _connection.Text = _status is null ? "LOCAL HOST  ·  unavailable" : $"LOCAL HOST  ·  stale, last observed {Ui.When(_status.ObservedAt)}";
            _connection.Foreground = Brush.Parse("#EAB879");
            if (explicitRefresh || _status is null) _notice.Text = Friendly(ex);
            if (!_editing && _section != "Usage") Render();
        }
        finally { _refreshing = false; }
    }

    private async Task RunAsync(Func<Task> operation, string success)
    {
        if (_busy) return;
        _busy = true; _notice.Text = "Working…";
        foreach (var action in _actions) action.IsEnabled = false;
        try { await operation(); _editing = false; if (_notice.Text == "Working…") _notice.Text = success; }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
        catch (Exception ex) { _notice.Text = Friendly(ex); }
        finally { _busy = false; foreach (var action in _actions) action.IsEnabled = true; }
        if (!_editing) { await RefreshAsync(false); Render(); }
    }

    private static string Friendly(Exception error) => error is LainApiException api
        ? $"{api.Message} ({api.Code}). Refresh before retrying if another client changed these settings."
        : error is HttpRequestException ? "The lain host is unavailable. Start the user service, then refresh. Your saved configuration stays on the host."
        : error is TaskCanceledException ? "The host took too long to respond. Refresh to check its current state."
        : error.Message;

    private Button Action(string label, Func<Task> operation, string success)
    {
        var button = Ui.Button(label, () => _ = RunAsync(operation, success)); _actions.Add(button); return button;
    }

    private void Render()
    {
        _actions.Clear();
        foreach (var control in _navigation.Children.OfType<Button>())
            control.Background = Equals(control.Content, _section) ? Brush.Parse("#204039") : Brushes.Transparent;
        _page.Content = _section switch
        {
            "Accounts" => AccountsPage(), "Routes" => RoutesPage(), "Profiles" => ProfilesPage(),
            "Usage" => _usage, "Setup" => SetupPage(), _ => OverviewPage()
        };
    }

    private StackPanel Heading(string title, string description) => Ui.Stack(Ui.Title(title), Ui.Text(description, 14, Ui.Muted));

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

    private Control AccountsPage()
    {
        var panel = Ui.Stack(Heading("Accounts", "Each connection keeps its own identity and history."), Ui.Row(Ui.Button("Add API account", ShowAddAccount), Ui.Button("Import existing setup", ShowImport)));
        if (_configuration is not { } cfg) { panel.Children.Add(Empty("Accounts are unavailable until the host responds.")); return panel; }
        if (cfg.Accounts.Length == 0) panel.Children.Add(Empty("No accounts yet. Add an API key or import a supported local configuration."));
        foreach (var account in cfg.Accounts)
        {
            var details = Ui.Stack(Ui.Text(account.Name, 20), Ui.Text($"{account.Provider} · {account.CredentialKind} · {account.State}", 13, Ui.Muted), Ui.Text($"{(account.Enabled ? "Enabled" : "Disabled")}  ·  {account.Id}", 12, Ui.Muted));
            if (account.ExpiresAt is { } expiry) details.Children.Add(Ui.Text($"Credential expires {Ui.When(expiry)}", 12, expiry < DateTimeOffset.Now ? Brush.Parse("#EAB879") : Ui.Muted));
            if (account.SharedWithLegacy) details.Children.Add(Ui.Text("Shared provider budget with the imported account", 12, Ui.Accent));
            var quota = _quotas.FirstOrDefault(q => q.AccountId == account.Id);
            details.Children.Add(QuotaDetails(quota));
            if (_quotaError is not null) details.Children.Add(Ui.Text("Quota snapshot may be stale · " + _quotaError, 12, Ui.Muted));
            details.Children.Add(Ui.Row(
                Action(account.Enabled ? "Disable" : "Enable", async () => { _configuration = await _client.SetAccountEnabledAsync(cfg.Revision, account.Id, !account.Enabled, _lifetime.Token); }, "Account updated."),
                Action("Check connection", async () => { var result = await _client.CheckAccountAsync(account.Id, _lifetime.Token); _notice.Text = $"Connection: {result.State}"; }, "Connection check complete."),
                Ui.Button("Remove…", () => ConfirmRemoval(account.Name, async () => { _configuration = await _client.RemoveAccountAsync(cfg.Revision, account.Id, _lifetime.Token); }))));
            details.Children.Add(Action("Refresh quota", async () => { _quotas = await _client.RefreshQuotasAsync(account.Id, _lifetime.Token); }, "Quota refresh complete."));
            panel.Children.Add(Ui.Card(details));
        }
        return panel;
    }

    private static Control Empty(string text) => Ui.Card(Ui.Text(text, 14, Ui.Muted));

    private static Control QuotaDetails(QuotaDto? quota)
    {
        if (quota is null) return Ui.Text("Quota · unknown", 12, Ui.Muted);
        static string Amount(double? value) => value?.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture) ?? "Unknown";
        var line = quota.Limit is null && quota.Remaining is null && quota.Used is null
            ? $"Quota · {quota.State} · amount unknown"
            : $"Quota · {quota.State} · {Amount(quota.Remaining)} {quota.Unit} remaining · {Amount(quota.Used)} / {Amount(quota.Limit)} used";
        return Ui.Stack(Ui.Text(line, 13, Ui.Accent), Ui.Text($"Source: {quota.Source} · observed {Ui.When(quota.ObservedAt)} · last success {Ui.When(quota.LastSuccessAt)} · reset {Ui.When(quota.ResetsAt)}", 11, Ui.Muted), Ui.Text(quota.Error ?? "", 12, Ui.Muted));
    }

    private void ConfirmRemoval(string name, Func<Task> remove)
    {
        _editing = true;
        _page.Content = Ui.Stack(Heading($"Remove {name}?", "The connection is removed from the active configuration. Historical usage remains available. Routes depending on this connection may need updating."),
            Ui.Row(Action("Remove", remove, "Removed."), Ui.Button("Cancel", () => { _editing = false; Render(); })));
    }

    private void ShowAddAccount()
    {
        if (_configuration is not { } cfg) { _notice.Text = "Connect to the host first."; return; }
        _editing = true;
        var name = Ui.Input(hint: "My API account");
        var provider = Ui.Input(hint: "Provider identifier, e.g. command-code");
        var endpoint = Ui.Input(hint: "https://…/v1");
        var key = Ui.Input(hint: "API key"); key.PasswordChar = '●';
        _page.Content = Ui.Stack(Heading("Add an API account", "Credentials are sent only to your local host and are not included in exports."),
            Ui.Card(Ui.Stack(Ui.Field("Display name", name), Ui.Field("Provider", provider), Ui.Field("OpenAI-compatible base URL", endpoint), Ui.Field("API key", key))),
            Ui.Row(Action("Add account", async () => {
                if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(provider.Text) || string.IsNullOrWhiteSpace(key.Text) || string.IsNullOrWhiteSpace(endpoint.Text)) throw new InvalidOperationException("Enter a name, provider, base URL and API key.");
                if (!Uri.TryCreate(endpoint.Text, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) throw new InvalidOperationException("Enter a valid HTTP or HTTPS base URL.");
                _configuration = await _client.AddApiAccountAsync(cfg.Revision, new AddApiAccountRequest(name.Text.Trim(), provider.Text.Trim(), key.Text, endpoint.Text.Trim()), _lifetime.Token); key.Text = "";
            }, "Account added."), Ui.Button("Cancel", () => { key.Text = ""; _editing = false; Render(); })));
    }

    private void ShowImport()
    {
        if (_configuration is not { } cfg) { _notice.Text = "Connect to the host first."; return; }
        _editing = true;
        var path = Ui.Input(hint: "/path/to/config.yaml"); var auth = Ui.Input(hint: "Optional auth directory");
        _page.Content = Ui.Stack(Heading("Import an existing setup", "Read supported API configuration and Codex access-token snapshots. Source files stay unchanged; imported OAuth snapshots are never refreshed or revoked."),
            Ui.Card(Ui.Stack(Ui.Field("Configuration path on this computer", path), Ui.Field("Auth directory (optional)", auth))),
            Ui.Row(Action("Import", async () => {
                if (string.IsNullOrWhiteSpace(path.Text)) throw new InvalidOperationException("Enter a configuration path.");
                var result = await _client.ImportAsync(new ImportRequest(cfg.Revision, path.Text.Trim(), string.IsNullOrWhiteSpace(auth.Text) ? null : auth.Text.Trim()), _lifetime.Token);
                _notice.Text = $"Imported {result.Added}, updated {result.Updated}. {string.Join(" ", result.Warnings)}";
            }, "Import complete. Refresh to inspect the imported accounts."), Ui.Button("Cancel", () => { _editing = false; Render(); })));
    }

    private Control RoutesPage()
    {
        var panel = Ui.Stack(Heading("Model routes", "Choose the actual upstream model and the account allowed to serve it."), Ui.Button("Create route", () => EditRoute(null)));
        if (_configuration is not { } cfg) { panel.Children.Add(Empty("Connect to the host to manage routes.")); return panel; }
        if (cfg.Routes.Length == 0) panel.Children.Add(Empty("No routes yet. Add an account, then create a route for its model."));
        foreach (var route in cfg.Routes)
        {
            var accounts = string.Join(", ", route.AccountIds.Select(id => cfg.Accounts.FirstOrDefault(a => a.Id == id)?.Name ?? id));
            panel.Children.Add(Ui.Card(Ui.Stack(Ui.Text(route.Alias, 20), Ui.Text($"{route.Provider} / {route.UpstreamModel}", 13, Ui.Muted), Ui.Text($"{(route.Pooled ? "Explicit pool" : "Strict account")} · {accounts} · {(route.Visible ? "Visible" : "Hidden")} · {(route.Enabled ? "Enabled" : "Disabled")}", 12, Ui.Muted), Ui.Row(Ui.Button("Edit", () => EditRoute(route)), Ui.Button("Remove…", () => ConfirmRemoval(route.Alias, async () => { _configuration = await _client.RemoveRouteAsync(cfg.Revision, route.Id, _lifetime.Token); }))))));
        }
        return panel;
    }

    private void EditRoute(RouteDto? route)
    {
        if (_configuration is not { } cfg) return;
        _editing = true;
        var alias = Ui.Input(route?.Alias, "client-facing-model"); var model = Ui.Input(route?.UpstreamModel, "Upstream model ID"); var provider = Ui.Input(route?.Provider, "Provider identifier");
        var enabled = new CheckBox { Content = "Enabled", IsChecked = route?.Enabled ?? true };
        var visible = new CheckBox { Content = "Visible in the client model menu", IsChecked = route?.Visible ?? true };
        var pooled = new CheckBox { Content = "Explicit pool (allows these accounts to serve the route)", IsChecked = route?.Pooled ?? false };
        var choices = cfg.Accounts.Select(a => (Account: a, Box: new CheckBox { Content = $"{a.Name} · {a.Provider}", IsChecked = route?.AccountIds.Contains(a.Id) ?? false })).ToArray();
        var accounts = Ui.Stack(choices.Select(x => (Control)x.Box).ToArray());
        _page.Content = Ui.Stack(Heading(route is null ? "Create a route" : "Edit route", "A strict route uses exactly one account. Multiple accounts require an explicit pool."),
            Ui.Card(Ui.Stack(Ui.Field("Model alias", alias), Ui.Field("Upstream model", model), Ui.Field("Provider", provider), Ui.Field("Allowed accounts", accounts), pooled, enabled, visible)),
            Ui.Row(Action("Save route", async () => {
                var ids = choices.Where(x => x.Box.IsChecked == true).Select(x => x.Account.Id).ToArray();
                if (string.IsNullOrWhiteSpace(alias.Text) || string.IsNullOrWhiteSpace(model.Text) || string.IsNullOrWhiteSpace(provider.Text) || ids.Length == 0) throw new InvalidOperationException("Enter an alias, upstream model, provider and at least one account.");
                if (ids.Length > 1 && pooled.IsChecked != true) throw new InvalidOperationException("Select one account or explicitly enable the pool.");
                _configuration = await _client.SaveRouteAsync(cfg.Revision, new RouteDto(route?.Id ?? Guid.NewGuid().ToString("N"), alias.Text.Trim(), model.Text.Trim(), provider.Text.Trim(), ids, pooled.IsChecked == true, visible.IsChecked == true, enabled.IsChecked == true), _lifetime.Token);
            }, "Route saved."), Ui.Button("Cancel", () => { _editing = false; Render(); })));
    }

    private Control ProfilesPage()
    {
        var panel = Ui.Stack(Heading("Profiles", "Build focused model menus for each client or workflow."), Ui.Button("Create profile", () => EditProfile(null)));
        if (_configuration is not { } cfg) { panel.Children.Add(Empty("Connect to the host to manage profiles.")); return panel; }
        if (cfg.Profiles.Length == 0) panel.Children.Add(Empty("No profiles yet. Create routes, then choose which models your client should offer."));
        foreach (var profile in cfg.Profiles)
            panel.Children.Add(Ui.Card(Ui.Stack(Ui.Text(profile.Name, 20), Ui.Text(string.Join(" · ", profile.RouteIds.Select(id => cfg.Routes.FirstOrDefault(r => r.Id == id)?.Alias ?? id)), 13, Ui.Muted), Ui.Text($"Default: {cfg.Routes.FirstOrDefault(r => r.Id == profile.DefaultRouteId)?.Alias ?? "None"}", 12, Ui.Muted), Ui.Row(Ui.Button("Edit selection", () => EditProfile(profile)), Action("Export…", () => ExportProfileAsync(profile), "Profile exported without credentials."), Ui.Button("Remove…", () => ConfirmRemoval(profile.Name, async () => { _configuration = await _client.RemoveProfileAsync(cfg.Revision, profile.Id, _lifetime.Token); }))))));
        return panel;
    }

    private async Task ExportProfileAsync(ProfileDto profile)
    {
        var export = await _client.ExportProfileAsync(profile.Id, _lifetime.Token);
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export profile", SuggestedFileName = $"lain-profile-{profile.Id}.json", DefaultExtension = "json", FileTypeChoices = [new FilePickerFileType("lain profile") { Patterns = ["*.json"] }] });
        if (file is null) { _notice.Text = "Export cancelled."; return; }
        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);
        await JsonSerializer.SerializeAsync(stream, export, new JsonSerializerOptions { WriteIndented = true }, _lifetime.Token);
    }

    private void EditProfile(ProfileDto? profile)
    {
        if (_configuration is not { } cfg) return;
        _editing = true;
        var name = Ui.Input(profile?.Name, "Profile name");
        var choices = cfg.Routes.Select(r => (Route: r, Box: new CheckBox { Content = $"{r.Alias} · {r.Provider} / {r.UpstreamModel}", IsChecked = profile?.RouteIds.Contains(r.Id) ?? false })).ToArray();
        var defaults = new ComboBox { ItemsSource = new[] { "None" }.Concat(cfg.Routes.Select(r => r.Alias)).ToArray(), SelectedIndex = profile?.DefaultRouteId is { } id ? Array.FindIndex(cfg.Routes, r => r.Id == id) + 1 : 0 };
        _page.Content = Ui.Stack(Heading(profile is null ? "Create a profile" : "Edit profile", "Select the routes to make available together. The default must be included in your selection."),
            Ui.Card(Ui.Stack(Ui.Field("Profile name", name), Ui.Field("Routes", Ui.Stack(choices.Select(x => (Control)x.Box).ToArray())), Ui.Field("Default route", defaults))),
            Ui.Row(Action("Save profile", async () => {
                if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException("Enter a profile name.");
                var ids = choices.Where(x => x.Box.IsChecked == true).Select(x => x.Route.Id).ToArray();
                var defaultId = defaults.SelectedIndex > 0 ? cfg.Routes[defaults.SelectedIndex - 1].Id : null;
                if (ids.Length == 0) throw new InvalidOperationException("Select at least one route.");
                if (defaultId is not null && !ids.Contains(defaultId)) throw new InvalidOperationException("Select the default route in this profile too.");
                _configuration = await _client.SaveProfileAsync(cfg.Revision, new ProfileDto(profile?.Id ?? Guid.NewGuid().ToString("N"), name.Text.Trim(), ids, defaultId), _lifetime.Token);
            }, "Profile saved."), Ui.Button("Cancel", () => { _editing = false; Render(); })));
    }

    private Control SetupPage() => Ui.Stack(Heading("Your first connection", "lain runs locally, with a background host that stays active when this window closes."),
        Ui.Card(Ui.Stack(Ui.Text("01  ·  Start the host", 20), Ui.Text("Install and start the lain user service using the supplied installer. The management API listens locally on port 18418; inference uses port 18417.", 14, Ui.Muted), Ui.Button("Check host connection", () => _ = RefreshAsync(true)))),
        Ui.Card(Ui.Stack(Ui.Text("02  ·  Connect an account", 20), Ui.Text("Add an API key or import a supported local configuration. Imported Codex access-token snapshots expire; reconnect by importing a fresh snapshot. Other OAuth providers remain unavailable until qualified.", 14, Ui.Muted), Ui.Button("Manage accounts", () => Navigate("Accounts")))),
        Ui.Card(Ui.Stack(Ui.Text("03  ·  Choose models and a profile", 20), Ui.Text("Create a model route bound to your account, then choose a profile. Profile export creates a separate client configuration so your existing client setup is preserved.", 14, Ui.Muted), Ui.Row(Ui.Button("Model routes", () => Navigate("Routes")), Ui.Button("Profiles", () => Navigate("Profiles"))))));
}
