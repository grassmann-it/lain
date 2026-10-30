using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Lain.Client;
using Lain.Core;
using Lain.Desktop.Presentation;

namespace Lain.Desktop.Recording;

/// <summary>API-owned local archive browser. Constructing or opening this view never enables capture.</summary>
public sealed partial class RecordingView : UserControl, IPresentationView
{
    private readonly LainApiClient _client;
    private readonly TextBlock _status = Ui.Text("Loading recording status…", 12, Ui.Muted);
    private readonly TextBlock _notice = Ui.Text("", 12, Ui.Warning);
    private readonly TextBlock _selected = Ui.Text("Choose a saved conversation", 15);
    private readonly TextBlock _scope = Ui.Text("", 12, Ui.Muted);
    private readonly TextBox _model = new() { PlaceholderText = "Optional model filter", Width = 180 };
    private readonly ComboBox _mode = new() { ItemsSource = new[] { "Metadata only", "Messages and tool content" }, SelectedIndex = 0, Width = 220 };
    private readonly CheckBox _consent = new() { Content = "I opt in to local capture for all used models on this gateway", IsChecked = false };
    private readonly Button _enable = new() { Content = "Enable recording", IsEnabled = false };
    private readonly Button _stop = new() { Content = "Turn capture off", IsEnabled = false };
    private readonly Button _pause = new() { Content = "Pause display" };
    private readonly Button _back = new() { Content = "Back to saved conversations", IsVisible = false };
    private readonly Button _more = new() { Content = "More conversations", IsVisible = false };
    private readonly Button _moreTimeline = new() { Content = "More timeline records", IsVisible = false };
    private readonly CheckBox _follow = new() { Content = "Follow appended records", IsChecked = false };
    private readonly StackPanel _list = new() { Spacing = 6 };
    private readonly StackPanel _timeline = new() { Spacing = 8 };
    private readonly StackPanel _variants = new() { Spacing = 6 };
    private readonly Grid _columns = new() { ColumnDefinitions = new ColumnDefinitions("270,*"), ColumnSpacing = 12 };
    private readonly Grid _detail = new() { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = 8 };
    private readonly Border _inventory;
    private readonly ScrollViewer _scroll;
    private readonly Expander _variantPanel;
    private readonly Dictionary<string, Button> _choices = new(StringComparer.Ordinal);
    private readonly HashSet<string> _renderedRecords = new(StringComparer.Ordinal);
    private CancellationTokenSource? _lifetime;
    private CancellationTokenSource? _selectionLifetime;
    private CapturePolicySnapshot? _policy;
    private string? _conversationId;
    private string? _listCursor;
    private string? _timelineCursor;
    private string? _modelFilter;
    private long _generation;
    private bool _paused;
    private bool _loadingList;
    private bool _loadingTimeline;
    private bool _adjustingScroll;
    private PresentationOptions _presentation = new();

    public RecordingView(LainApiClient client)
    {
        _client = client;
        SetName(_status, "Recording status"); SetName(_notice, "Recording notice"); SetName(_model, "Filter saved conversations by model");
        SetName(_consent, "Opt in to local recording for all models"); SetName(_mode, "Recording content mode");
        SetName(_enable, "Enable recording"); SetName(_stop, "Turn capture off"); SetName(_pause, "Pause archive display");
        SetName(_back, "Back to saved conversations"); SetName(_follow, "Follow appended archive records");
        SetName(_more, "Load more saved conversations"); SetName(_moreTimeline, "Load more timeline records");
        _consent.IsCheckedChanged += (_, _) => UpdateControls();
        _enable.Click += async (_, _) => await ChangeCaptureAsync(true);
        _stop.Click += async (_, _) => await ChangeCaptureAsync(false);
        _pause.Click += (_, _) => { _paused = !_paused; _pause.Content = _paused ? "Resume display" : "Pause display"; };
        _back.Click += (_, _) => { _conversationId = null; _selectionLifetime?.Cancel(); _generation++; ArrangeView(); };
        _more.Click += async (_, _) => await LoadListAsync(false);
        _moreTimeline.Click += async (_, _) => await LoadTimelineAsync(false);
        ToolTip.SetTip(_pause, "Pauses automatic updates in this view. Capture and inference continue independently.");
        ToolTip.SetTip(_stop, "Stops capture admission and discards pending uncommitted capture. Retained conversations remain readable; inference continues.");
        var refresh = Ui.Button("Refresh", async () => { await RefreshStatusAsync(); if (!_paused) { await LoadListAsync(true); if (_conversationId is not null) await LoadTimelineAsync(true); } });
        SetName(refresh, "Refresh recording archive");
        var filter = Ui.Button("Apply filter", async () => { _modelFilter = string.IsNullOrWhiteSpace(_model.Text) ? null : _model.Text.Trim(); await LoadListAsync(true); });
        SetName(filter, "Apply saved conversation filter");
        var controls = new Expander
        {
            Header = "Recording controls (explicit opt-in)", HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel { Spacing = 7, Children = { Ui.Text("New installations default off. Files are stored locally without encryption under your OS permissions. Reading does not require training approval.", 12, Ui.Muted), _scope, _mode, _consent, Wrap(_enable, _stop), ArchiveDeletionControls() } }
        };
        controls.Content = new ScrollViewer { Content = controls.Content, MaxHeight = 200, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        SetName(controls, "Recording controls");
        var listGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 6 };
        listGrid.Children.Add(Ui.Text("Saved conversations", 14));
        var listScroll = new ScrollViewer { Content = _list, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        SetName(listScroll, "Saved conversation inventory"); Grid.SetRow(listScroll, 1); listGrid.Children.Add(listScroll);
        Grid.SetRow(_more, 2); listGrid.Children.Add(_more);
        _inventory = Ui.Card(listGrid); _inventory.Padding = new Thickness(10);
        _columns.Children.Add(_inventory);
        _scroll = new ScrollViewer { Content = _timeline, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        SetName(_scroll, "Saved conversation timeline");
        _scroll.ScrollChanged += (_, e) => { if (!_adjustingScroll && e.OffsetDelta.Y < -1 && e.ExtentDelta.Y == 0) _follow.IsChecked = false; };
        _variantPanel = new Expander { Header = "Run and skill / prompt variants", Content = new ScrollViewer { Content = _variants, MaxHeight = 160, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, HorizontalAlignment = HorizontalAlignment.Stretch };
        SetName(_variantPanel, "Run variant manifests");
        _variantPanel.PropertyChanged += async (_, e) => { if (e.Property == Expander.IsExpandedProperty && _variantPanel.IsExpanded) await LoadManifestsAsync(); };
        _detail.Children.Add(Wrap(_back, _selected));
        Grid.SetRow(_variantPanel, 1); _detail.Children.Add(_variantPanel);
        Grid.SetRow(_scroll, 2); _detail.Children.Add(_scroll);
        Grid.SetRow(_moreTimeline, 3); _detail.Children.Add(_moreTimeline);
        Grid.SetColumn(_detail, 1); _columns.Children.Add(_detail);
        var page = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*"), RowSpacing = 8 };
        page.Children.Add(_status); Grid.SetRow(controls, 1); page.Children.Add(controls);
        var toolbar = Wrap(_model, filter, _pause, _follow, refresh);
        Grid.SetRow(toolbar, 2); page.Children.Add(toolbar); Grid.SetRow(_notice, 3); page.Children.Add(_notice);
        Grid.SetRow(_columns, 4); page.Children.Add(_columns); Content = page;
        SizeChanged += (_, _) => ArrangeView();
        AttachedToVisualTree += (_, _) => { if (_lifetime is null) { _lifetime = new(); _ = StartAsync(_lifetime.Token); } };
        DetachedFromVisualTree += (_, _) => { _generation++; _selectionLifetime?.Cancel(); _selectionLifetime?.Dispose(); _selectionLifetime = null; _lifetime?.Cancel(); _lifetime?.Dispose(); _lifetime = null; };
        ArrangeView();
    }

    private async Task StartAsync(CancellationToken ct)
    {
        await RefreshStatusAsync();
        await LoadListAsync(true);
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(ct)) if (!_paused) { await RefreshStatusAsync(); if (_conversationId is not null) await LoadTimelineAsync(true); }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
    private void UpdateControls()
    {
        _enable.IsEnabled = _policy is { Enabled: false } && _consent.IsChecked == true;
        _stop.IsEnabled = _policy?.Enabled == true;
        _mode.IsEnabled = _policy?.Enabled != true;
    }
    public void SetPresentation(PresentationOptions options) { _presentation = options; ArrangeView(); }
    private void ArrangeView()
    {
        var narrow = Bounds.Width is > 0 and < 900;
        var selected = _conversationId is not null;
        _columns.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "270,*");
        _inventory.IsVisible = !narrow || !selected;
        _detail.IsVisible = !narrow || selected;
        Grid.SetColumn(_detail, narrow ? 0 : 1);
        _back.IsVisible = narrow && selected;
        _timeline.Spacing = _presentation.Density == ViewDensity.Compact ? 4 : 8;
        _variantPanel.MaxHeight = narrow ? 140 : 220;
    }
    private static void SetName(Control control, string name) => AutomationProperties.SetName(control, name);
    private static WrapPanel Wrap(params Control[] controls)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var control in controls) { control.Margin = new Thickness(0, 0, 7, 5); panel.Children.Add(control); }
        return panel;
    }
    private void Error(Exception ex)
    {
        if (ex is OperationCanceledException) return;
        // API errors may contain captured content in future server versions. Show only the safe code.
        _notice.Text = ex is LainApiException api ? $"Archive unavailable ({api.Code}). Retained history is independent of capture." : "Archive request failed. Refresh to retry.";
    }
}
