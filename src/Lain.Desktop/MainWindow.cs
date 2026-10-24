using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Lain.Client;
using Lain.Core;
using Lain.Desktop.Usage;
using Lain.Desktop.Providers;
using Lain.Desktop.Inspector;
using Avalonia.Platform.Storage;
using System.Text.Json;
using Lain.Desktop.Presentation;

namespace Lain.Desktop;

/// <summary>The management window is a client of the independently running host.</summary>
public sealed partial class MainWindow : Window
{
    private readonly LainApiClient _client;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ContentControl _page = new();
    private readonly TextBlock _connection = Ui.Text("Connecting to local host…", 11, Ui.Muted);
    private readonly TextBlock _notice = Ui.Text("", 13);
    private readonly Border _hostIndicator = new() { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = Ui.Muted, VerticalAlignment = VerticalAlignment.Center };
    private readonly Avalonia.Controls.Primitives.UniformGrid _navigation = new() { Columns = 5, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(10, 0), MaxWidth = 1120 };
    private readonly Button _refresh;
    private readonly ScrollViewer _workspace;
    private readonly List<Button> _actions = [];
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(10) };
    private ConfigurationDto? _configuration;
    private string? _configurationError;
    private QuotaDto[] _quotas = [];
    private string? _quotaError;
    private ModelEntryDto[] _models = [];
    private StatusDto? _status;
    private string _section = "Accounts";
    private bool _busy;
    private bool _refreshing;
    private bool _editing;
    private bool _hostAvailable;
    private string? _renderedPageState;
    private readonly UsageView _usage;
    private readonly InspectorView _inspector;
    private readonly ProviderCatalogView _providers;
    private ProviderCatalogDto? _providerCatalog;
    private bool _providerLoading;
    private LoginDto? _login;
    private CancellationTokenSource? _loginPolling;
    private bool _loginVisible;
    private string? _loginError;

    public MainWindow(LainApiClient client, string? preferencesPath = null)
    {
        _client = client;
        _preferencesStore = new WorkspacePreferencesStore(preferencesPath);
        _usage = new UsageView(client);
        _inspector = new InspectorView(client);
        _providers = new ProviderCatalogView(uri => _ = OpenProviderLinkAsync(uri), BeginProviderSetup);
        _usage.NavigateRequested = NavigateTo;
        _inspector.NavigateRequested = NavigateTo;
        Title = "lain — Local AI Gateway";
        Icon = Branding.WindowIcon;
        Width = 1120; Height = 780; MinWidth = 800; MinHeight = 560;
        Background = Ui.Background;
        WindowDecorations = WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = 48;
        var shell = new Grid { RowDefinitions = new RowDefinitions("48,Auto,*,32,52") };

        var brand = Ui.Text("lain", 21);
        brand.FontFamily = Ui.BrandFont;
        brand.FontWeight = FontWeight.SemiBold;
        var subtitle = Ui.Text("Local AI Gateway", 12, Ui.Muted);
        subtitle.TextWrapping = TextWrapping.NoWrap;
        subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
        var titleArea = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 14, Background = Brushes.Transparent, Margin = new Thickness(20, 0, 16, 0) };
        brand.VerticalAlignment = subtitle.VerticalAlignment = VerticalAlignment.Center;
        var logo = new Image { Source = Branding.Logo, Width = 32, Height = 32, Stretch = Stretch.Uniform };
        AutomationProperties.SetName(logo, "Application logo");
        titleArea.Children.Add(Ui.Row(logo, brand));
        Grid.SetColumn(subtitle, 1); titleArea.Children.Add(subtitle);
        titleArea.PointerPressed += (_, args) => { if (args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(args); };
        titleArea.DoubleTapped += (_, _) => ToggleMaximize();

        _refresh = Ui.Button("Reload status", () => _ = RefreshAsync(true));
        _refresh.Content = Ui.Row(Ui.Icon("refresh", 14), Ui.Text("Reload status", 12));
        _refresh.Padding = new Thickness(10, 5);
        _refresh.MinHeight = 30;
        _refresh.VerticalAlignment = VerticalAlignment.Center;
        _refresh.Margin = new Thickness(0, 0, 12, 0);
        AutomationProperties.SetName(_refresh, "Reload status");
        ToolTip.SetTip(_refresh, "Refresh host status and configuration");
        var captions = new StackPanel { Orientation = Orientation.Horizontal };
        captions.Children.Add(CaptionButton("minimize", "Minimize", () => WindowState = WindowState.Minimized));
        captions.Children.Add(CaptionButton("maximize", "Maximize or restore", ToggleMaximize));
        captions.Children.Add(CaptionButton("close", "Close window", Close));
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto") };
        header.Children.Add(titleArea);
        _backButton = Ui.Button("← Back", GoBack);
        _backButton.Margin = new Thickness(0, 6, 8, 6);
        AutomationProperties.SetName(_backButton, "Back"); ToolTip.SetTip(_backButton, "Back to previous view");
        Grid.SetColumn(_backButton, 1); header.Children.Add(_backButton);
        _settingsButton = Ui.IconButton("settings", "Settings", () => Navigate("Settings"));
        _settingsButton.Margin = new Thickness(0, 6, 8, 6);
        _settingsButton.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_settingsButton, 2); header.Children.Add(_settingsButton);
        Grid.SetColumn(_refresh, 3); header.Children.Add(_refresh);
        Grid.SetColumn(captions, 4); header.Children.Add(captions);
        shell.Children.Add(new Border { Background = Ui.Surface, BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 0, 0, 1), Child = header });

        var noticeContent = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 12 };
        noticeContent.Children.Add(new ScrollViewer { Content = _notice, MaxHeight = 88, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        _reviewDraftButton = Ui.Button("Review latest configuration", ReviewDraft);
        _reviewDraftButton.IsVisible = false;
        _reviewDraftButton.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_reviewDraftButton, 1); noticeContent.Children.Add(_reviewDraftButton);
        var dismiss = Ui.IconButton("close", "Dismiss message", () => _notice.Text = "");
        dismiss.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(dismiss, 2); noticeContent.Children.Add(dismiss);
        var noticeBox = new Border { Background = Ui.Raised, BorderBrush = Ui.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8), Margin = new Thickness(20, 12, 20, 0), Child = noticeContent, IsVisible = false };
        _notice.PropertyChanged += (_, args) => { if (args.Property == TextBlock.TextProperty) noticeBox.IsVisible = !string.IsNullOrWhiteSpace(_notice.Text); };
        var notices = new StackPanel { Spacing = 0 };
        notices.Children.Add(noticeBox);
        notices.Children.Add(_retainedDraftNotices);
        Grid.SetRow(notices, 1); shell.Children.Add(notices);

        _workspace = new ScrollViewer { Content = _page, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Margin = new Thickness(20, 18, 20, 18) };
        _page.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _page.VerticalContentAlignment = VerticalAlignment.Stretch;
        _page.PropertyChanged += (_, args) =>
        {
            if (args.Property != ContentControl.ContentProperty) return;
            UpdateWorkspaceScrolling();
            UpdateAccountInspectorLayout();
        };
        _page.GotFocus += (_, args) => { if (args.Source is Control control) _lastEditorFocus = control; };
        _workspaceRegion.Children.Add(_workspace);
        Grid.SetColumn(_accountInspector, 1); _workspaceRegion.Children.Add(_accountInspector);
        Grid.SetRow(_workspaceRegion, 2); shell.Children.Add(_workspaceRegion);
        KeyDown += (_, args) =>
        {
            if (!args.Handled && _openObjectMenus == 0 && args.Key == Avalonia.Input.Key.Escape && _inspectedAccountId is not null)
            {
                if (_accountRemovalVisible) ShowAccountInspector(_inspectedAccountId);
                else CloseAccountInspector();
                args.Handled = true;
            }
        };

        _connection.TextWrapping = TextWrapping.NoWrap;
        _connection.TextTrimming = TextTrimming.CharacterEllipsis;
        _connection.VerticalAlignment = VerticalAlignment.Center;
        var connectionRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 7 };
        connectionRow.Children.Add(_hostIndicator);
        Grid.SetColumn(_connection, 1); connectionRow.Children.Add(_connection);
        var footerContent = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 20, Margin = new Thickness(20, 0) };
        var serviceButton = Ui.Button("", () => Navigate("Service"));
        serviceButton.Content = connectionRow;
        serviceButton.Padding = new Thickness(0); serviceButton.Background = Brushes.Transparent;
        serviceButton.BorderThickness = new Thickness(0); serviceButton.HorizontalContentAlignment = HorizontalAlignment.Left;
        serviceButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(serviceButton, "Local service");
        ToolTip.SetTip(serviceButton, "Local service, applied profile and pending changes");
        footerContent.Children.Add(serviceButton);
        var lifecycle = Ui.Text("Host stays active when this window closes", 11, Ui.Muted);
        lifecycle.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(lifecycle, 1); footerContent.Children.Add(lifecycle);
        var footer = new Border { BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 1, 0, 0), Child = footerContent };
        Grid.SetRow(footer, 3); shell.Children.Add(footer);

        foreach (var name in new[] { "Accounts", "Models", "Profiles", "Usage", "Live" })
        {
            var nav = Ui.Button(name, () => Navigate(name));
            nav.Tag = name;
            nav.Content = Ui.Row(Ui.Icon(name switch { "Models" => "network", "Live" => "search", _ => name.ToLowerInvariant() }, 16), Ui.Text(name, 13));
            nav.Classes.Add("nav");
            nav.MinWidth = 0;
            nav.Height = 36;
            nav.Margin = new Thickness(2, 0);
            nav.Padding = new Thickness(6, 7);
            nav.HorizontalContentAlignment = HorizontalAlignment.Center;
            if (nav.Content is StackPanel navRow) { navRow.Spacing = 6; navRow.HorizontalAlignment = HorizontalAlignment.Center; }
            nav.BorderThickness = new Thickness(0);
            nav.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetName(nav, name);
            _navigation.Children.Add(nav);
        }
        _moreNavigation.Classes.Add("nav");
        _moreNavigation.Click += (_, _) => _moreNavigation.ContextMenu?.Open();
        AutomationProperties.SetName(_moreNavigation, "More destinations");
        _navigation.Children.Add(_moreNavigation);
        AutomationProperties.SetName(_informationLevel, "Information level");
        const string levelHelp = "Barebones: essentials with local details available. Simple: everyday summaries. Advanced: more technical detail.";
        AutomationProperties.SetHelpText(_informationLevel, levelHelp);
        ToolTip.SetTip(_informationLevel, levelHelp);
        _informationLevel.SelectionChanged += (_, _) =>
        {
            if (_viewOptionsUpdating || _informationLevel.SelectedIndex < 0) return;
            SavePreferences(p => p with { Level = (InformationLevel)_informationLevel.SelectedIndex });
        };
        var navigationContent = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        navigationContent.Children.Add(_navigation);
        Grid.SetColumn(_informationLevel, 1); navigationContent.Children.Add(_informationLevel);
        var navigationBar = new Border { Background = Ui.Surface, BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 1, 0, 0), Child = navigationContent };
        Grid.SetRow(navigationBar, 4); shell.Children.Add(navigationBar);
        Content = shell;
        SizeChanged += (_, _) => { UpdateNavigationLayout(); UpdateAccountInspectorLayout(); };
        UpdateNavigationLayout();
        Opened += async (_, _) => { await RefreshAsync(true); _poll.Start(); };
        _poll.Tick += async (_, _) => await RefreshAsync(false);
        Closed += (_, _) => { _poll.Stop(); _lifetime.Cancel(); _client.Dispose(); _lifetime.Dispose(); };
        ApplyPresentation(false);
        if (_preferencesStore.Error is { } preferenceError) _notice.Text = preferenceError;
        Render();
    }

    private Button CaptionButton(string icon, string label, Action click)
    {
        var button = Ui.IconButton(icon, label, click);
        button.Width = 44; button.Height = 47;
        button.Padding = new Thickness(14);
        button.CornerRadius = new CornerRadius(0);
        button.Background = Brushes.Transparent;
        button.BorderThickness = new Thickness(0);
        return button;
    }

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void UpdateNavigationLayout()
    {
        var showIcons = (Bounds.Width > 0 ? Bounds.Width : Width) >= 1000;
        foreach (var button in _navigation.Children.OfType<Button>())
            if (button.Content is StackPanel row)
                foreach (var icon in row.Children.OfType<PathIcon>()) icon.IsVisible = showIcons;
    }

    private async Task RefreshAsync(bool explicitRefresh)
    {
        if (_refreshing || _busy) return;
        _refreshing = true;
        _refresh.IsEnabled = false;
        var readingConfiguration = false;
        try
        {
            var status = await _client.StatusAsync(_lifetime.Token);
            _status = status;
            UpdateNavigationDestinations();
            _hostAvailable = true;
            _connection.Text = ServiceSummary(status);
            _hostIndicator.Background = ProfileApplyOutcomeUnknown ? Ui.Warning : IsMonitorMode || status.ProxyReady ? Ui.Success : Ui.Warning;
            ToolTip.SetTip(_connection, $"lain host {status.Version} · {status.State}\nConfiguration {status.Revision} · Applied {status.AppliedRevision}");
            if (!_editing || explicitRefresh)
            {
                readingConfiguration = true;
                _configuration = await _client.GetConfigurationAsync(_lifetime.Token);
                readingConfiguration = false;
                _configurationError = null;
                UpdateNavigationDestinations();
                if (_accountQuotaRefreshes.Count == 0)
                {
                    var quotaGeneration = _accountQuotaRefreshGeneration;
                    try
                    {
                        var quotas = await _client.QuotasAsync(_lifetime.Token);
                        // A full snapshot started before a local account refresh
                        // must not overwrite that newer account-bound result.
                        if (quotaGeneration == _accountQuotaRefreshGeneration && _accountQuotaRefreshes.Count == 0)
                        { _quotas = quotas; _quotaError = null; }
                    }
                    catch (Exception error) when (error is not OperationCanceledException)
                    {
                        if (quotaGeneration == _accountQuotaRefreshGeneration && _accountQuotaRefreshes.Count == 0)
                            _quotaError = Friendly(error);
                    }
                }
                try { _models = (await _client.ModelsAsync(_lifetime.Token)).Models; }
                catch (Exception error) when (error is not OperationCanceledException) { _models = []; }
            }
            // Explicit refresh retains in-progress forms and their entered values.
            if (_providerCatalog is not null && (explicitRefresh || !_providers.IsKeyboardFocusWithin))
                _providers.SetCatalog(_providerCatalog, _configuration?.Accounts);
            if (_section == "Providers" && (explicitRefresh || _providerCatalog is null)) await RefreshProviderCatalogAsync();
            UpdateServiceSummary();
            UpdateDraftStatus();
            RefreshPage(explicitRefresh);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (readingConfiguration || _configuration is null) _configurationError = Friendly(ex);
            _hostAvailable = false;
            _connection.Text = _status is null ? "Local host unavailable · Open service help" : $"Local host unavailable · Last updated {Ui.When(_status.ObservedAt)}";
            _hostIndicator.Background = Ui.Warning;
            ToolTip.SetTip(_connection, Friendly(ex));
            UpdateServiceSummary();
            if (explicitRefresh || _status is null) _notice.Text = Friendly(ex);
            RefreshPage(explicitRefresh);
        }
        finally { _refreshing = false; _refresh.IsEnabled = !_busy; }
    }

    private async Task RunAsync(Func<Task> operation, string success, bool stayOnPage = false)
    {
        if (_busy) return;
        var operationSection = _section;
        _busy = true; _notice.Text = "Working…";
        UpdateDraftStatus();
        _refresh.IsEnabled = false;
        _page.IsEnabled = false; _navigation.IsEnabled = false; _settingsButton.IsEnabled = false; _backButton.IsEnabled = false;
        foreach (var action in _actions) action.IsEnabled = false;
        try { await operation(); _editing = stayOnPage; if (_notice.Text == "Working…") _notice.Text = success; }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
        catch (Exception ex) { _notice.Text = Friendly(ex); }
        finally { _busy = false; _refresh.IsEnabled = !_refreshing; _page.IsEnabled = true; _navigation.IsEnabled = true; _settingsButton.IsEnabled = true; _backButton.IsEnabled = _history.Count > 0; foreach (var action in _actions) action.IsEnabled = true; }
        if (!_editing) { _drafts.Remove(operationSection); await RefreshAsync(false); Render(); }
        UpdateDraftStatus();
        UpdateServiceSummary();
    }

    private static string Friendly(Exception error) => error is LainApiException api
        ? api.Code == "revision_conflict"
            ? "The saved configuration changed. Your draft is kept. Reload status, then review the latest configuration before saving again."
            : $"{CostPolicyMessage(api.Message)} ({api.Code}). Review the current configuration before retrying."
        : error is HttpRequestException ? "The lain host is unavailable. Start the user service, then refresh. Your saved configuration stays on the host."
        : error is TaskCanceledException ? "The host took too long to respond. Refresh to check its current state."
        : error.Message;

    private Button Action(string label, Func<Task> operation, string success, bool stayOnPage = false)
    {
        var button = Ui.Button(label, () => _ = RunAsync(operation, success, stayOnPage)); _actions.Add(button); return button;
    }

    private void RefreshPage(bool explicitRefresh)
    {
        if (_inspectedAccountId is { } id && _configuration?.Accounts.Any(a => a.Id == id) != true)
        {
            CloseAccountInspector();
            _notice.Text = "The selected account is no longer configured. Historical usage remains available.";
        }
        if (_editing || _accountRemovalVisible || _openObjectMenus > 0 || _section is "Usage" or "Live" or "Providers" or "Settings" or "About") return;
        if (_inspectedAccountId is not null && !explicitRefresh && _accountInspector.IsKeyboardFocusWithin) return;
        // Polling updates the shell immediately, but must not remove the control being
        // used with the keyboard. A changed snapshot remains pending until focus leaves
        // the page, navigation occurs, or the user explicitly refreshes.
        if (explicitRefresh || (!_page.IsKeyboardFocusWithin && PageState() != _renderedPageState)) Render();
    }

    private string PageState() => JsonSerializer.Serialize(new
    {
        Section = _section,
        HostAvailable = _hostAvailable,
        Configuration = _configuration,
        ConfigurationError = _section == "Accounts" ? _configurationError : null,
        AccountQuotaRefreshGeneration = _section == "Accounts" ? _accountQuotaRefreshGeneration : 0,
        // Observation time is visible in Overview; other pages should retain their
        // controls when the host only advances its observation timestamp.
        Status = _section is "Service" or "Profiles" ? _status : null,
        Quotas = _section == "Accounts" ? _quotas : null,
        QuotaError = _section == "Accounts" ? _quotaError : null,
        Models = _section is "Models" or "Profiles" ? _models : null,
        LoginState = _section == "Accounts" ? _login?.State : null
    });

    private void Render()
    {
        _actions.Clear();
        UpdateNavigationSelection();
        if (!_editing && _displayedDraftSection == _section)
        {
            _drafts.Remove(_section); _editorRevision = null; _editorRebuild = null; _editorIdentity = null; _displayedDraftSection = null;
        }
        UpdateDraftStatus();
        var offset = _workspace.Offset;
        if (_section == "Accounts" && !_editing) PrepareAccountsRender();
        _page.Content = _section switch
        {
            "Accounts" => AccountsPage(), "Models" => RoutesPage(), "Profiles" => ProfilesPage(),
            "Providers" => ProviderDiscoveryPage(), "Live" => _inspector,
            "Usage" => _usage, "Setup" => SetupPage(), "Service" => ServicePage(),
            "Settings" => SettingsPage(), "About" => AboutPage(), _ => AccountsPage()
        };
        UpdateWorkspaceScrolling();
        UpdateAccountInspectorLayout();
        _renderedPageState = PageState();
        _backButton.IsEnabled = _history.Count > 0;
        Dispatcher.UIThread.Post(() => _workspace.Offset = offset, DispatcherPriority.Loaded);
    }

    private void UpdateWorkspaceScrolling()
    {
        if (_workspace is null) return;
        var bounded = _page.Content is WorkspacePage || ReferenceEquals(_page.Content, _usage) || ReferenceEquals(_page.Content, _inspector);
        _workspace.VerticalScrollBarVisibility = bounded
            ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
    }

    private StackPanel Heading(string title, string description) => new() { Spacing = 5, Children = { Ui.Title(title), Ui.Text(description, 13, Ui.Muted) } };

    private static Control Empty(string text) => Ui.Card(Ui.Text(text, 14, Ui.Muted));

    private void ConfirmRemoval(string name, Func<Task> remove)
    {
        _editing = true;
        var confirm = Action("Remove", remove, "Removed.");
        confirm.Classes.Add("danger");
        _page.Content = Ui.Stack(Heading($"Remove {name}?", "This item will be removed from the saved configuration. Historical usage remains available."),
            Ui.Row(confirm, Ui.Button("Cancel", () => { _editing = false; Render(); })));
    }


}
