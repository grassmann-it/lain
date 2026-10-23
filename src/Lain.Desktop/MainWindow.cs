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
public sealed partial class MainWindow : Window
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
    private ModelEntryDto[] _models = [];
    private StatusDto? _status;
    private string _section = "Overview";
    private bool _busy;
    private bool _refreshing;
    private bool _editing;
    private bool _hostAvailable;
    private readonly UsageView _usage;
    private LoginDto? _login;
    private CancellationTokenSource? _loginPolling;
    private bool _loginVisible;
    private string? _loginError;

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

    private void Navigate(string section) { _section = section; _editing = false; _loginVisible = false; _notice.Text = ""; Render(); }

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
                try { _models = (await _client.ModelsAsync(_lifetime.Token)).Models; }
                catch (Exception error) when (error is not OperationCanceledException) { _models = []; }
            }
            if (explicitRefresh) { _editing = false; _loginVisible = false; _notice.Text = ""; }
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

    private async Task RunAsync(Func<Task> operation, string success, bool stayOnPage = false)
    {
        if (_busy) return;
        _busy = true; _notice.Text = "Working…";
        foreach (var action in _actions) action.IsEnabled = false;
        try { await operation(); _editing = stayOnPage; if (_notice.Text == "Working…") _notice.Text = success; }
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

    private Button Action(string label, Func<Task> operation, string success, bool stayOnPage = false)
    {
        var button = Ui.Button(label, () => _ = RunAsync(operation, success, stayOnPage)); _actions.Add(button); return button;
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

    private static Control Empty(string text) => Ui.Card(Ui.Text(text, 14, Ui.Muted));

    private void ConfirmRemoval(string name, Func<Task> remove)
    {
        _editing = true;
        _page.Content = Ui.Stack(Heading($"Remove {name}?", "The connection is removed from the active configuration. Historical usage remains available. Routes depending on this connection may need updating."),
            Ui.Row(Action("Remove", remove, "Removed."), Ui.Button("Cancel", () => { _editing = false; Render(); })));
    }


}
