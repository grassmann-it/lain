using System.Globalization;
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lain.Client;
using Lain.Core;
using Lain.Desktop.Presentation;

namespace Lain.Desktop.Inspector;

/// <summary>Bounded, read-only live inspection. Content is never written to disk by this view.</summary>
public sealed class InspectorView : UserControl, IPresentationView
{
    private const int DisplayEventLimit = 500;
    private const int DisplayCharacterLimit = 262144;
    private static readonly HashSet<string> EvidenceSources = new(StringComparer.Ordinal)
        { "route_binding", "harness_header", "request_body", "provider_response", "core" };
    private readonly LainApiClient _client;
    private readonly TextBlock _status = Ui.Text("Waiting for the live inspector", 12, Ui.Muted);
    private readonly TextBlock _empty = Ui.Text("No observed streams yet. Send a request through lain to inspect it.", 14, Ui.Muted);
    private readonly WrapPanel _navigation = new() { Orientation = Orientation.Horizontal };
    private readonly ContentControl _identityFacts = new();
    private readonly ScrollViewer _identityScroll = new() { MaxHeight = 140, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly List<TextBlock> _eventTimes = [];
    private readonly List<Expander> _eventDetails = [];
    private readonly List<Border> _eventSurfaces = [];
    private readonly TextBlock _buffer = Ui.Text("No live buffer received", 11, Ui.Muted);
    private readonly TextBlock _selection = Ui.Text("Choose a conversation or request", 14);
    private readonly TextBlock _contentStatus = Ui.Text("", 11, Ui.Muted);
    private readonly TextBox _search = new() { Width = 200, PlaceholderText = "Model, provider or identity" };
    private readonly ComboBox _activity = new() { Width = 160, ItemsSource = new[] { "All observed", "Active conversations", "With errors" }, SelectedIndex = 0 };
    private readonly ComboBox _conversation = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _request = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox _follow = new() { Content = "Follow output", IsChecked = true };
    private readonly Button _pause = new() { Content = "Pause display" };
    private readonly StackPanel _groupList = new() { Spacing = 5 };
    private readonly StackPanel _eventList = new() { Spacing = 8 };
    private readonly Expander _identity = new() { Header = "Identities & measurement sources", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly ScrollViewer _eventScroll;
    private readonly Grid _layout = new() { ColumnDefinitions = new ColumnDefinitions("260,*"), ColumnSpacing = 12, Height = 480 };
    private readonly Border _sidebar;
    private readonly Grid _details;
    private readonly Grid _pickers = new() { ColumnDefinitions = new ColumnDefinitions("*"), ColumnSpacing = 10 };
    private readonly Control _conversationField;
    private readonly Control _requestField;
    private readonly Dictionary<string, Button> _groupButtons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GroupChoice> _groups = new(StringComparer.Ordinal);
    private readonly List<InspectorEventDto> _events = [];
    private readonly Dictionary<long, long> _eventReceived = [];
    private CancellationTokenSource? _lifetime;
    private TopLevel? _topLevel;
    private InspectorSnapshotDto? _snapshot;
    private string? _sourceId;
    private string? _selectedGroup;
    private string? _selectedRequest;
    private long _after;
    private long _nextSnapshot;
    private bool _paused;
    private bool _refreshOnce;
    private bool _connected;
    private bool _updatingChoices;
    private bool _renderPending;
    private bool _followScheduled;
    private SelectableTextBlock? _lastOutput;
    private string? _contentGap;
    private string? _sourceNotice;
    private string? _navigationKey;
    private PresentationOptions _presentation = new();

    public Action<WorkspaceNavigationRequest>? NavigateRequested { get; set; }

    public InspectorView(LainApiClient client)
    {
        _client = client;
        var refresh = Ui.Button("Refresh", () => { _nextSnapshot = 0; _refreshOnce = true; });
        var clear = Ui.Button("Clear view", () =>
        {
            ClearEvents(); _after = Math.Max(_after, _snapshot?.Cursor ?? 0);
            _contentGap = "Displayed events cleared. New events will appear as they arrive.";
            RenderEvents();
        });
        _pause.Click += (_, _) =>
        {
            _paused = !_paused;
            _pause.Content = _paused ? "Resume display" : "Pause display";
            _nextSnapshot = 0;
            UpdateStatus();
        };
        AutomationProperties.SetName(refresh, "Refresh inspector");
        AutomationProperties.SetName(clear, "Clear displayed events");
        AutomationProperties.SetName(_pause, "Toggle inspector updates");
        AutomationProperties.SetName(_status, "Inspector status");
        AutomationProperties.SetName(_search, "Search live requests");
        AutomationProperties.SetName(_activity, "Inspector activity");
        AutomationProperties.SetName(_conversation, "Inspector conversation");
        AutomationProperties.SetName(_request, "Inspector request");
        AutomationProperties.SetName(_follow, "Follow live output");
        AutomationProperties.SetName(_eventList, "Inspector events");
        ToolTip.SetTip(_pause, "Pause or resume live updates. Content still expires while paused.");
        ToolTip.SetTip(refresh, "Refresh the Inspector once, including while paused.");
        ToolTip.SetTip(clear, "Clears this view. The host's bounded live buffer expires automatically.");
        ToolTip.SetTip(_follow, "Turn off to read earlier output while new events arrive.");
        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var control in new Control[] { _search, _activity, _pause, refresh, clear, _follow })
        {
            control.Margin = new Thickness(0, 0, 7, 5);
            control.VerticalAlignment = VerticalAlignment.Center;
            toolbar.Children.Add(control);
        }
        _conversationField = Ui.Field("Conversation / linked requests", _conversation);
        _conversationField.IsVisible = false;
        var groupsScroll = new ScrollViewer { Content = _groupList, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var sidebarContent = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 8 };
        sidebarContent.Children.Add(Ui.Text("Conversations & requests", 13));
        Grid.SetRow(groupsScroll, 1); sidebarContent.Children.Add(groupsScroll);
        _sidebar = Surface(sidebarContent);
        _layout.Children.Add(_sidebar);
        _identityScroll.Content = new StackPanel { Spacing = 7, Children = { _navigation, _identityFacts } };
        _identity.Content = _identityScroll;
        ToolTip.SetTip(_identity, "Request identities, measurement sources and related Account, Models and Usage views.");
        _eventScroll = new ScrollViewer { Content = _eventList, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _eventScroll.SizeChanged += (_, _) => ScheduleFollowOutput();
        _follow.IsCheckedChanged += (_, _) => ScheduleFollowOutput();
        _details = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"), RowSpacing = 8 };
        _details.Children.Add(_selection);
        _requestField = Ui.Field("Request within this group", _request);
        _pickers.Children.Add(_conversationField); _pickers.Children.Add(_requestField);
        Grid.SetRow(_pickers, 1); _details.Children.Add(_pickers);
        Grid.SetRow(_contentStatus, 2); _details.Children.Add(_contentStatus);
        Grid.SetRow(_eventScroll, 3); _details.Children.Add(_eventScroll);
        Grid.SetRow(_identity, 4); _details.Children.Add(_identity);
        Grid.SetColumn(_details, 1); _layout.Children.Add(_details);
        Grid.SetColumn(_empty, 1); _layout.Children.Add(_empty); _details.IsVisible = false;
        _requestField.IsVisible = false;
        AutomationProperties.SetName(_empty, "Live empty state");
        Content = new StackPanel { Spacing = 10, Children =
        {
            Ui.PageHeader("Live", "Inspector · output, attempts and tool continuations grouped by proven identities."),
            new StackPanel { Spacing = 3, Children = { _status, _buffer } }, toolbar, _layout
        } };
        _search.TextChanged += (_, _) => RenderGroups();
        _activity.SelectionChanged += (_, _) => RenderGroups();
        _conversation.SelectionChanged += (_, _) => { if (!_updatingChoices && _conversation.SelectedItem is GroupChoice choice) SelectGroup(choice.Key); };
        _request.SelectionChanged += (_, _) => { if (!_updatingChoices && _request.SelectedItem is RequestChoice choice) SelectRequest(choice.Id); };
        SizeChanged += (_, _) => ArrangeInspector();
        LayoutUpdated += (_, _) => FitWorkspace();
        AttachedToVisualTree += (_, _) =>
        {
            if (_lifetime is not null) return;
            _topLevel = TopLevel.GetTopLevel(this);
            if (_topLevel is not null) _topLevel.PropertyChanged += TopLevelChanged;
            ArrangeInspector();
            _lifetime = new CancellationTokenSource();
            _status.Text = _paused ? "Updates paused" : "Connecting to the live inspector…";
            _ = PollAsync(_lifetime.Token);
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _lifetime?.Cancel(); _lifetime?.Dispose(); _lifetime = null;
            if (_topLevel is not null) _topLevel.PropertyChanged -= TopLevelChanged;
            _topLevel = null; _connected = false;
            ResetDetachedState();
        };
    }

    private void TopLevelChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property.Name == "ClientSize") ArrangeInspector();
    }

    public void SetPresentation(PresentationOptions options)
    {
        var levelChanged = options.Level != _presentation.Level;
        _presentation = options;
        // Presentation never re-fetches content, changes its receipt time or
        // resets selection. Expiry also continues while updates are paused.
        PruneEvents();
        if (levelChanged)
        {
            foreach (var details in _eventDetails.Where(x => !x.IsKeyboardFocusWithin))
                details.IsExpanded = options.Level == InformationLevel.Advanced;
        }
        ApplyPresentation();
    }

    private void ApplyPresentation()
    {
        var compact = _presentation.Density == ViewDensity.Compact;
        var narrow = Bounds.Width > 0 && Bounds.Width < 900;
        _layout.ColumnSpacing = compact ? 8 : 12;
        _details.RowSpacing = narrow ? 4 : compact ? 5 : 8;
        _groupList.Spacing = compact ? 3 : 5;
        _eventList.Spacing = compact ? 4 : 8;
        foreach (var button in _groupButtons.Values) button.Padding = new Thickness(compact ? 6 : 10);
        foreach (var surface in _eventSurfaces) surface.Padding = new Thickness(compact ? 6 : 10);
        foreach (var time in _eventTimes) time.IsVisible = _presentation.Level != InformationLevel.Barebones;
        FitWorkspace();
        ScheduleFollowOutput();
    }

    private void RenderNavigation(InspectorStreamDto[] streams)
    {
        var accounts = streams.Select(x => x.AccountId).Where(x => !string.IsNullOrWhiteSpace(x)).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        var routes = streams.Select(x => x.RouteId).Where(x => !string.IsNullOrWhiteSpace(x)).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        var key = string.Join("\n", accounts.Select(x => "account:" + x).Concat(routes.Select(x => "route:" + x)));
        if (_navigationKey == key) return;
        _navigationKey = key;
        _navigation.Children.Clear();
        void Link(string label, WorkspaceNavigationRequest target)
        {
            var button = Ui.Button(label, () => NavigateRequested?.Invoke(target));
            button.Margin = new Thickness(0, 0, 6, 3);
            AutomationProperties.SetName(button, label);
            _navigation.Children.Add(button);
        }
        foreach (var id in accounts)
        {
            Link("Account " + Short(id), new WorkspaceNavigationRequest("Accounts", AccountId: id));
            Link("Usage for account " + Short(id), new WorkspaceNavigationRequest("Usage", AccountId: id));
        }
        foreach (var id in routes)
        {
            Link("Model access " + Short(id), new WorkspaceNavigationRequest("Models", RouteId: id));
            Link("Usage for model access " + Short(id), new WorkspaceNavigationRequest("Usage", RouteId: id));
        }
        // A displayed model name or conversation identifier does not establish
        // a configured model-access identity or durable Usage relationship.
    }

    private void ArrangeInspector()
    {
        var narrow = Bounds.Width > 0 && Bounds.Width < 900;
        // Keep the display controls on one line at the minimum window width;
        // wrapped controls otherwise consume the output pane's usable height.
        _search.Width = narrow ? 155 : 200;
        _activity.Width = narrow ? 140 : 160;
        _details.RowSpacing = narrow ? 4 : _presentation.Density == ViewDensity.Compact ? 5 : 8;
        _sidebar.IsVisible = !narrow;
        _conversationField.IsVisible = narrow;
        _layout.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "260,*");
        Grid.SetColumn(_details, narrow ? 0 : 1);
        Grid.SetColumn(_empty, narrow ? 0 : 1);
        var twoPickers = narrow && _requestField.IsVisible;
        _pickers.ColumnDefinitions = new ColumnDefinitions(twoPickers ? "*,*" : "*");
        Grid.SetColumn(_requestField, twoPickers ? 1 : 0);
        FitWorkspace();
    }

    private void FitWorkspace()
    {
        var viewport = this.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault()?.Viewport.Height ?? 0;
        if (viewport <= 0 || _layout.TranslatePoint(default, this) is not { } origin) return;
        // The output owns the inner scroll. A minimum layout height here would
        // force a second outer scroll when the shell has less vertical room.
        var height = Math.Max(0, viewport - origin.Y);
        if (Math.Abs(_layout.Height - height) > 1) _layout.Height = height;
        // Expanding technical details must still leave readable output above
        // them. Their own scroll handles identities and context links.
        var identityHeader = Math.Max(40, _identity.Bounds.Height - (_identity.IsExpanded ? _identityScroll.Bounds.Height : 0));
        var detailRoom = height - _selection.Bounds.Height - _pickers.Bounds.Height - _contentStatus.Bounds.Height
            - _details.RowSpacing * 4 - identityHeader - 64;
        var detailHeight = Math.Clamp(detailRoom, 0, 140);
        if (Math.Abs(_identityScroll.MaxHeight - detailHeight) > 1) _identityScroll.MaxHeight = detailHeight;
    }

    private void ScheduleFollowOutput()
    {
        if (_follow.IsChecked != true || _followScheduled) return;
        _followScheduled = true;
        // Resizing changes the output viewport after its parent is arranged.
        // Follow the new extent only once layout has completed; preserving the
        // old pixel offset can leave only card headers visible in a short pane.
        Dispatcher.UIThread.Post(() =>
        {
            _followScheduled = false;
            if (_follow.IsChecked != true || _lifetime is null) return;
            var oldOffset = _eventScroll.Offset.Y;
            var output = _lastOutput;
            var point = output?.TranslatePoint(default, _eventScroll);
            var viewport = _eventScroll.Viewport.Height;
            _eventScroll.ScrollToEnd();
            if (output is null || point is null) return;
            if (viewport <= 0) return;
            // Expanded event metadata can extend beyond the latest output.
            // Keep the end of the actual text in view while following it.
            var offset = oldOffset + point.Value.Y + output.Bounds.Height - viewport + 4;
            _eventScroll.Offset = new Vector(_eventScroll.Offset.X,
                Math.Clamp(offset, 0, Math.Max(0, _eventScroll.Extent.Height - viewport)));
        }, DispatcherPriority.Loaded);
    }

    private async Task PollAsync(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
            do
            {
                PruneEvents();
                if (_renderPending && !_eventList.IsKeyboardFocusWithin) RenderEvents();
                if (_paused && !_refreshOnce) continue;
                _refreshOnce = false;
                if (Stopwatch.GetTimestamp() >= _nextSnapshot)
                {
                    _nextSnapshot = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
                    await RefreshSnapshotAsync(token);
                }
                if (_connected && _selectedRequest is not null && _details.IsVisible) await RefreshEventsAsync(token);
            } while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async Task RefreshSnapshotAsync(CancellationToken token)
    {
        try
        {
            var snapshot = await _client.InspectorAsync(new InspectorQuery(Limit: 256), token);
            token.ThrowIfCancellationRequested();
            if (snapshot.ContractVersion != Api.Version) throw new InvalidOperationException("Unsupported inspector contract.");
            if (_sourceId is not null && _sourceId != snapshot.SourceId)
                ResetSource(snapshot.SourceId);
            _sourceId = snapshot.SourceId;
            _snapshot = snapshot;
            _connected = snapshot.State == "live";
            UpdateStatus();
            RenderGroups();
            RenderSelection();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            if (token.IsCancellationRequested) return;
            _connected = false;
            _status.Text = "Inspector disconnected · active state is unknown · " + error.Message;
            _status.Foreground = Ui.Warning;
            RenderGroups(); RenderSelection();
        }
    }

    private async Task RefreshEventsAsync(CancellationToken token)
    {
        var request = _selectedRequest;
        if (request is null) return;
        try
        {
            var page = await _client.InspectorEventsAsync(new InspectorEventQuery(_sourceId, _after, 100, request), token);
            token.ThrowIfCancellationRequested();
            if (request != _selectedRequest) return;
            if (page.SourceId != _sourceId)
            {
                ResetSource(page.SourceId);
                _snapshot = null; _connected = false;
                _status.Text = "Live source changed · loading its new snapshot";
                _nextSnapshot = 0;
                return;
            }
            if (page.Truncated || page.GapReason is not null)
                _contentGap = "Content gap · " + (page.GapReason ?? "part of the live buffer is no longer available");
            foreach (var entry in page.Events.Where(e => e.RequestId == request && e.Cursor > _after).OrderBy(e => e.Cursor))
                if (!_events.Any(existing => existing.Cursor == entry.Cursor)) { _events.Add(entry); _eventReceived[entry.Cursor] = Stopwatch.GetTimestamp(); }
            _after = Math.Max(_after, page.NextCursor);
            var characters = _events.Sum(e => e.Text?.Length ?? 0);
            var trimmed = false;
            while (_events.Count > DisplayEventLimit || characters > DisplayCharacterLimit)
            {
                trimmed = true;
                characters -= _events[0].Text?.Length ?? 0; _eventReceived.Remove(_events[0].Cursor); _events.RemoveAt(0);
                _contentGap = "Earlier displayed events were trimmed to keep this view bounded.";
            }
            if (page.Events.Length > 0 || page.Truncated || page.GapReason is not null) RenderEvents(preserveFocusedOutput: !trimmed);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            if (token.IsCancellationRequested) return;
            _connected = false;
            _status.Text = "Live updates unavailable · active state is unknown · " + error.Message;
            _status.Foreground = Ui.Warning;
            RenderSelection();
        }
    }

    private void UpdateStatus()
    {
        if (_snapshot is null) return;
        var state = _snapshot.State switch { "live" => "Connected to the live inspector", "waiting" => "Waiting for the owned proxy stream",
            "connecting" => "Connecting to the owned proxy stream", _ => "Live source unavailable" };
        var observed = _snapshot.ObservedAt > DateTimeOffset.UnixEpoch ? $" · checked {_snapshot.ObservedAt.ToLocalTime():HH:mm:ss} local" : " · no source snapshot yet";
        _status.Text = (_paused ? "Updates paused" : state)
            + $" · {_snapshot.Streams.Length:N0} observed streams" + observed
            + (_snapshot.Complete ? "" : " · snapshot incomplete");
        if (_snapshot.Error is not null) _status.Text += " · " + _snapshot.Error;
        _status.Foreground = _connected && _snapshot.Complete ? Ui.Accent : Ui.Warning;
        _buffer.Text = $"Host content buffer: {_snapshot.Limits.ContentTtlSeconds:N0}s · this view keeps content at most {_snapshot.Limits.ContentTtlSeconds:N0}s after receipt · {_snapshot.DroppedEvents:N0} dropped events";
        if (_sourceNotice is not null) _buffer.Text += "\n" + _sourceNotice;
    }

    private void RenderGroups()
    {
        if (_snapshot is null) return;
        var grouped = _snapshot.Streams.GroupBy(GroupKey).Select(group =>
        {
            var streams = group.ToArray();
            var label = group.Key.StartsWith("conversation:", StringComparison.Ordinal) ? "Conversation " + Short(group.Key[13..])
                : streams.Select(x => x.RequestId).Distinct().Count() > 1 ? "Linked requests " + Short(group.Key[8..]) : "Request " + Short(streams[0].RequestId);
            return new GroupChoice(group.Key, label, streams);
        }).ToArray();
        _groups.Clear();
        foreach (var group in grouped) _groups[group.Key] = group;
        foreach (var key in _groupButtons.Keys.Where(key => !_groups.ContainsKey(key)).ToArray())
        { _groupList.Children.Remove(_groupButtons[key]); _groupButtons.Remove(key); }
        var visible = new List<GroupChoice>();
        foreach (var group in grouped)
        {
            var search = _search.Text?.Trim() ?? "";
            var matches = (search.Length == 0 || group.Streams.Any(stream => SearchText(stream).Contains(search, StringComparison.OrdinalIgnoreCase)))
                && (_activity.SelectedIndex != 1 || group.Streams.Any(IsActive))
                && (_activity.SelectedIndex != 2 || group.Streams.Any(stream => stream.State is "failed" or "error" || stream.StatusCode >= 400));
            if (matches) visible.Add(group);
            if (!_groupButtons.TryGetValue(group.Key, out var button))
            {
                button = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(10), BorderThickness = new Thickness(1) };
                AutomationProperties.SetName(button, "Inspect group " + group.Key);
                button.Click += (_, _) => SelectGroup(group.Key);
                _groupButtons[group.Key] = button; _groupList.Children.Add(button);
            }
            button.IsVisible = matches;
            button.Background = group.Key == _selectedGroup ? Ui.AccentSurface : Ui.Surface;
            button.BorderBrush = group.Key == _selectedGroup ? Ui.Accent : Ui.Edge;
            var state = group.Streams.Any(IsActive) ? _connected ? "Active" : "Active state unknown"
                : group.Streams.All(IsTerminal) ? "Finished / retained" : "State unknown";
            button.Content = new StackPanel { Spacing = 4, Children = { Ui.Text(group.Name, 13),
                Ui.Text($"{group.Streams.Select(x => x.RequestId).Distinct().Count()} requests · {group.Streams.Length} streams · {state}", 11, Ui.Muted),
                Ui.Text(string.Join(" · ", group.Streams.Select(x => x.Model ?? x.RequestedModel ?? "Model unknown").Distinct()), 11, Ui.Muted) } };
        }
        if (!_conversation.IsKeyboardFocusWithin)
        {
            _updatingChoices = true;
            _conversation.ItemsSource = visible;
            _conversation.SelectedItem = visible.FirstOrDefault(x => x.Key == _selectedGroup);
            _updatingChoices = false;
        }
        _details.IsVisible = visible.Count > 0;
        _empty.IsVisible = visible.Count == 0;
        _empty.Text = grouped.Length == 0
            ? "No observed streams yet. Send a request through lain to inspect it."
            : "No requests match these filters. Clear the search or change activity to see observed requests.";
        // A hidden selection may stay in memory until its ordinary expiry, but
        // never present its output as a result of the current filters.
        if (visible.Count > 0)
        {
            var selected = visible.FirstOrDefault(x => x.Key == _selectedGroup);
            if (selected is null) SelectGroup(visible[0].Key);
            else if (!_request.IsKeyboardFocusWithin) SelectGroup(selected.Key);
        }
        ApplyPresentation();
    }

    private string GroupKey(InspectorStreamDto stream)
    {
        var current = stream.RequestId;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (visited.Add(current))
        {
            var attempts = _snapshot!.Streams.Where(candidate => candidate.RequestId == current).ToArray();
            var conversations = attempts
                .Select(candidate => KnownIdentity(candidate.Dimensions, "conversationId", candidate.Dimensions?.ConversationId))
                .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
            var parents = attempts.Select(candidate => KnownIdentity(candidate.Dimensions, "parentRequestId", candidate.Dimensions?.ParentRequestId))
                .OfType<string>().ToHashSet(StringComparer.Ordinal);
            var previousResponses = attempts.Select(candidate => KnownIdentity(candidate.Dimensions, "previousResponseId", candidate.Dimensions?.PreviousResponseId))
                .OfType<string>().Distinct(StringComparer.Ordinal);
            foreach (var previous in previousResponses)
            {
                var matches = _snapshot!.Streams.Where(candidate => KnownIdentity(candidate.Dimensions, "responseId", candidate.Dimensions?.ResponseId) == previous).Select(x => x.RequestId).Distinct().ToArray();
                if (matches.Length > 1) return "request:" + stream.RequestId;
                if (matches.Length == 1) parents.Add(matches[0]);
            }
            // Resolve relationships for the logical request, not an arbitrary
            // retry. Conflicting evidence must never split its attempts.
            if (conversations.Length > 1 || parents.Count > 1) return "request:" + stream.RequestId;
            if (conversations.Length == 1) return "conversation:" + conversations[0];
            if (parents.Count == 0) return "request:" + current;
            current = parents.Single();
        }
        return "request:" + stream.RequestId;
    }

    private void SelectGroup(string key)
    {
        if (!_groups.TryGetValue(key, out var group)) return;
        _selectedGroup = key;
        var requests = group.Streams.GroupBy(x => x.RequestId).Select(streams => new RequestChoice(streams.Key,
            $"{streams.First().Model ?? streams.First().RequestedModel ?? "Model unknown"} · request {Short(streams.Key)} · {streams.Count()} streams")).ToArray();
        var selected = requests.FirstOrDefault(x => x.Id == _selectedRequest) ?? requests.FirstOrDefault();
        _requestField.IsVisible = requests.Length > 1;
        ArrangeInspector();
        _updatingChoices = true; _request.ItemsSource = requests; _request.SelectedItem = selected; _conversation.SelectedItem = group; _updatingChoices = false;
        foreach (var pair in _groupButtons) { pair.Value.Background = pair.Key == key ? Ui.AccentSurface : Ui.Surface; pair.Value.BorderBrush = pair.Key == key ? Ui.Accent : Ui.Edge; }
        if (selected is not null) SelectRequest(selected.Id);
    }

    private void SelectRequest(string id)
    {
        if (_selectedRequest != id)
        {
            _selectedRequest = id; ClearEvents(); _after = Math.Max(0, (_snapshot?.EarliestCursor ?? 1) - 1);
            _contentGap = null; RenderEvents();
        }
        RenderSelection();
    }

    private void RenderSelection()
    {
        if (_snapshot is null || _selectedRequest is null) return;
        var streams = _snapshot.Streams.Where(x => x.RequestId == _selectedRequest).ToArray();
        if (streams.Length == 0) { _selection.Text = "Selected request is no longer described in the live buffer"; _navigation.Children.Clear(); _navigationKey = null; _identityFacts.Content = null; return; }
        var active = streams.Any(IsActive);
        var stateLabel = active ? _connected ? "Active" : "Active state unknown" : streams.All(IsTerminal) ? "Retained result" : "State unknown";
        _selection.Text = $"{streams.First().Model ?? streams.First().RequestedModel ?? "Model unknown"} · {stateLabel} · {streams.Length} observed streams";
        RenderNavigation(streams);
        if (_identity.IsKeyboardFocusWithin) return;
        var facts = new StackPanel { Spacing = 7 };
        foreach (var stream in streams)
        {
            var state = !_connected && IsActive(stream) ? "unknown after connection loss" : stream.State;
            facts.Children.Add(Selectable($"Request {stream.RequestId} · attempt {stream.AttemptId ?? "not supplied"} · {stream.Scope} · {state}\n{stream.Provider ?? "Provider unknown"} · account {stream.AccountId ?? "unknown"} · route {stream.RouteId ?? "unknown"}"));
            if (stream.Measurements is { } timing) facts.Children.Add(Ui.Text($"First output {Milliseconds(timing.FirstOutputLatencyMs)} · total {Milliseconds(timing.TotalDurationMs)} · {timing.Source ?? "Measurement source unknown"}", 11, Ui.Muted));
            if (stream.Dimensions is { } dimensions)
            {
                var ids = new (string Name, string Field, string? Value)[] { ("Conversation", "conversationId", dimensions.ConversationId), ("Turn", "turnId", dimensions.TurnId),
                    ("Worker", "workerId", dimensions.WorkerId), ("Parent worker", "parentWorkerId", dimensions.ParentWorkerId), ("Parent request", "parentRequestId", dimensions.ParentRequestId) };
                foreach (var (name, field, value) in ids)
                    facts.Children.Add(Selectable(name + ": " + (KnownIdentity(dimensions, field, value) ?? "Unknown")));
                foreach (var evidence in (dimensions.Evidence ?? []).Where(e => e is not null && EvidenceSources.Contains(e.Source)))
                    facts.Children.Add(Ui.Text($"{evidence.Field} · {evidence.Source}" + (evidence.Key is null ? "" : " · " + evidence.Key), 10, Ui.Muted));
            }
            else facts.Children.Add(Ui.Text("Conversation, turn and worker identities are unknown.", 11, Ui.Muted));
            if (stream.Truncated) facts.Children.Add(Ui.Text("This stream is incomplete in the live buffer.", 11, Ui.Warning));
        }
        _identityFacts.Content = facts;
    }

    private void PruneEvents()
    {
        var lifetime = TimeSpan.FromSeconds(_snapshot?.Limits.ContentTtlSeconds ?? 120);
        var expired = _events.Where(entry => !_eventReceived.TryGetValue(entry.Cursor, out var received)
            || Stopwatch.GetElapsedTime(received) >= lifetime).Select(entry => entry.Cursor).ToHashSet();
        if (expired.Count == 0) return;
        _events.RemoveAll(entry => expired.Contains(entry.Cursor));
        foreach (var cursor in expired) _eventReceived.Remove(cursor);
        _contentGap = "Earlier content expired from the live display buffer.";
        RenderEvents();
    }

    private void RenderEvents(bool preserveFocusedOutput = false)
    {
        if (preserveFocusedOutput && _eventList.IsKeyboardFocusWithin)
        {
            _renderPending = true;
            _contentStatus.Text = (_contentGap is null ? "" : _contentGap + " · ") + "New live output is ready; leave the focused output to display it. Content still expires.";
            return;
        }
        _renderPending = false;
        _eventList.Children.Clear();
        _lastOutput = null;
        _eventTimes.Clear(); _eventDetails.Clear(); _eventSurfaces.Clear();
        _contentStatus.Text = _contentGap ?? (_selectedRequest is null ? "Select a request to read its live events." : "Retained live events · text and tool chunks stay within this request");
        _contentStatus.Foreground = _contentGap is null ? Ui.Muted : Ui.Warning;
        var blocks = new List<EventBlock>();
        foreach (var entry in _events)
        {
            var channel = entry.Channel ?? entry.Kind;
            var previous = blocks.LastOrDefault();
            if (!string.IsNullOrEmpty(entry.Text) && previous is not null && previous.Channel == channel && previous.AttemptId == entry.AttemptId
                && previous.ToolCallId == entry.ToolCallId && previous.First.Text is not null
                && previous.First.Dimensions?.TurnId == entry.Dimensions?.TurnId && previous.First.Dimensions?.WorkerId == entry.Dimensions?.WorkerId
                && previous.First.RedactionState == entry.RedactionState && !entry.Truncated && entry.GapReason is null && !previous.Truncated)
                previous.Text += entry.Text;
            else blocks.Add(new EventBlock(entry, channel));
        }
        foreach (var block in blocks)
        {
            var entry = block.First;
            var label = block.Channel switch { "text" or "output" or "assistant" => "Assistant", "reasoning" => "Reasoning", "tool" or "tool_call" or "tool_result" => "Tool",
                _ => entry.Kind switch { "attempt_started" => "Attempt started", "attempt_finished" => "Attempt finished", "metadata" => "Metadata", _ => "Status" } };
            if (entry.ToolName is not null) label += " · " + entry.ToolName;
            var metadata = new List<string> { entry.ObservedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture) + " local" };
            if (entry.AttemptId is not null) metadata.Add("attempt " + Short(entry.AttemptId));
            if (KnownIdentity(entry.Dimensions, "turnId", entry.Dimensions?.TurnId) is { } turn) metadata.Add("turn " + Short(turn));
            if (KnownIdentity(entry.Dimensions, "workerId", entry.Dimensions?.WorkerId) is { } worker) metadata.Add("worker " + Short(worker));
            var time = Ui.Text(metadata[0], 10, Ui.Muted);
            _eventTimes.Add(time);
            var content = new StackPanel { Spacing = 5, Children = { Ui.Text(label, 13), time } };
            if (!string.IsNullOrEmpty(block.Text))
            {
                _lastOutput = Selectable(block.Text, 13);
                content.Children.Add(_lastOutput);
            }
            else content.Children.Add(Ui.Text(entry.State ?? entry.GapReason ?? "Metadata event", 12, Ui.Muted));
            if (entry.Truncated || entry.GapReason is not null) content.Children.Add(Ui.Text("Partial event · " + (entry.GapReason ?? "content truncated"), 11, Ui.Warning));
            if (entry.RedactionState is not null and not "none") content.Children.Add(Ui.Text("Content policy: " + entry.RedactionState, 10, Ui.Muted));
            var technical = new StackPanel { Spacing = 3, Children =
            {
                Selectable(string.Join(" · ", metadata)),
                Selectable($"Request {entry.RequestId} · attempt {entry.AttemptId ?? "unknown"} · event {entry.Cursor:N0}")
            } };
            var details = new Expander { Header = "Event details", Content = technical,
                HorizontalAlignment = HorizontalAlignment.Stretch, IsExpanded = _presentation.Level == InformationLevel.Advanced };
            content.Children.Add(details); _eventDetails.Add(details);
            var surface = Surface(content); _eventSurfaces.Add(surface); _eventList.Children.Add(surface);
        }
        if (blocks.Count == 0) _eventList.Children.Add(Ui.Text("No retained content for this request. Metadata may be available before the first output or after content expires.", 12, Ui.Muted));
        ApplyPresentation();
        ScheduleFollowOutput();
    }

    private static string? KnownIdentity(AttemptDimensionsDto? dimensions, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        static string Normalize(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        return dimensions?.Evidence?.Any(e => e is not null && !string.IsNullOrWhiteSpace(e.Field) && Normalize(e.Field) == Normalize(field) && EvidenceSources.Contains(e.Source)) == true ? value : null;
    }
    private static bool IsActive(InspectorStreamDto stream) => stream.State is "active" or "pending" or "streaming" or "started";
    private static bool IsTerminal(InspectorStreamDto stream) => stream.State is "success" or "completed" or "succeeded" or "failed" or "error" or "canceled" or "cancelled" or "finished";
    private void ClearEvents() { _events.Clear(); _eventReceived.Clear(); }
    private void ResetDetachedState()
    {
        _snapshot = null; _sourceId = null; _selectedGroup = null; _selectedRequest = null;
        _after = 0; _nextSnapshot = 0; _refreshOnce = false; _renderPending = false; _lastOutput = null;
        ClearEvents(); _eventList.Children.Clear(); _eventTimes.Clear(); _eventDetails.Clear(); _eventSurfaces.Clear();
        _groups.Clear(); _groupButtons.Clear(); _groupList.Children.Clear();
        _updatingChoices = true;
        _conversation.ItemsSource = Array.Empty<GroupChoice>(); _request.ItemsSource = Array.Empty<RequestChoice>();
        _updatingChoices = false;
        _navigation.Children.Clear(); _navigationKey = null; _identityFacts.Content = null;
        _sourceNotice = null; _contentGap = "Live content cleared when Live was closed.";
        _buffer.Text = "No live buffer received"; _selection.Text = "Choose a conversation or request";
        _details.IsVisible = false; _empty.IsVisible = true;
        _empty.Text = "Live content was cleared when this page closed. Waiting for a new snapshot.";
    }
    private void ResetSource(string sourceId)
    {
        _sourceId = sourceId; _selectedGroup = null; _selectedRequest = null; _after = 0;
        ClearEvents(); _groups.Clear(); _groupButtons.Clear(); _groupList.Children.Clear();
        _navigation.Children.Clear(); _navigationKey = null;
        _updatingChoices = true; _conversation.ItemsSource = Array.Empty<GroupChoice>(); _request.ItemsSource = Array.Empty<RequestChoice>(); _updatingChoices = false;
        _identityFacts.Content = Ui.Text("Waiting for identities from the new live source", 12, Ui.Muted);
        _sourceNotice = "Live source restarted; previous content and relationships were cleared.";
        _contentGap = _sourceNotice;
        RenderEvents();
    }
    private static string SearchText(InspectorStreamDto stream) => string.Join(" ", stream.RequestId, stream.AttemptId, stream.Model, stream.RequestedModel,
        stream.Provider, stream.AccountId, stream.RouteId, stream.Dimensions?.ConversationId, stream.Dimensions?.TurnId, stream.Dimensions?.WorkerId);
    private static string Short(string value) => value.Length <= 12 ? value : value[..12] + "…";
    private static string Milliseconds(double? value) => value is { } number && double.IsFinite(number) && number >= 0 ? number < 1000 ? $"{number:N0} ms" : $"{number / 1000:N1} s" : "—";
    private static SelectableTextBlock Selectable(string text, double size = 11) => new() { Text = text, FontSize = size, Foreground = Ui.Ink, TextWrapping = TextWrapping.Wrap };
    private static Border Surface(Control child) => new() { Background = Ui.Surface, BorderBrush = Ui.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(10), Child = child };
    private sealed record GroupChoice(string Key, string Name, InspectorStreamDto[] Streams) { public override string ToString() => Name; }
    private sealed record RequestChoice(string Id, string Name) { public override string ToString() => Name; }
    private sealed class EventBlock(InspectorEventDto first, string channel)
    {
        public InspectorEventDto First { get; } = first;
        public string Channel { get; } = channel;
        public string? AttemptId => First.AttemptId;
        public string? ToolCallId => First.ToolCallId;
        public bool Truncated => First.Truncated;
        public string Text { get; set; } = first.Text ?? "";
    }
}
