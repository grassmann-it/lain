using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using Avalonia.Media;
using Avalonia.Threading;
using Lain.Client;
using Lain.Core;
using Lain.Desktop.Presentation;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private string _routeSearch = "";
    private string _routeProvider = "All providers";
    private string _routeVisibility = "All visibility";
    private string _routeState = "All states";
    private string? _routeAccountId;
    private string? _routeContextId;
    private string? _routeModel;
    private string? _selectedModelRouteId;
    private bool _modelNarrowDetail;
    private bool _modelFilterExpanded;
    private Vector _modelInventoryOffset;
    private readonly Dictionary<string, long> _modelPendingRevisions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _modelDetails = new(StringComparer.Ordinal);
    private sealed record EditorFocusTarget(string Section, string? ObjectId, string EntryPoint);
    private EditorFocusTarget? _modelEditorFocusTarget;

    private EditorFocusTarget CaptureEditorFocus(string section, string? id, string fallback, EditorFocusTarget? retained)
    {
        if ((FocusManager?.GetFocusedElement() as Control)?.Tag is EditorFocusTarget focused
            && focused.Section == section && focused.ObjectId == id) return focused;
        // Rebuilding a stale editor keeps the original return target, not a field
        // or menu item from the editor that is about to be replaced.
        if (_editing && retained?.Section == section && retained.ObjectId == id) return retained;
        return new(section, id, fallback);
    }

    private void RestoreEditorFocus(EditorFocusTarget target)
    {
        var page = _page.Content as Control;
        var navigation = _workspaceNavigationGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            if (_editing || _busy || _section != target.Section || navigation != _workspaceNavigationGeneration
                || page is null || !ReferenceEquals(_page.Content, page) || TopLevel.GetTopLevel(page) != this) return;
            if (target.ObjectId is { } id && (target.Section == "Models" ? _selectedModelRouteId : _selectedProfileId) != id) return;
            var candidates = page.GetVisualDescendants().OfType<Button>()
                .Where(button => button.IsEffectivelyVisible && button.IsEffectivelyEnabled && button.Focusable
                    && button.Tag is EditorFocusTarget key && key.Section == target.Section && key.ObjectId == target.ObjectId).ToArray();
            // A resized window or menu entry can require the same object's other
            // visible Edit action. Never substitute an alias or a different row.
            var trigger = candidates.FirstOrDefault(button => Equals(button.Tag, target)) ?? candidates.FirstOrDefault();
            if (trigger is null) return;
            trigger.BringIntoView();
            trigger.Focus();
        }, DispatcherPriority.Loaded);
    }

    private void ApplyModelsNavigation(WorkspaceNavigationRequest target)
    {
        if (target.AccountId is null && target.RouteId is null && target.Model is null) return;
        // A recorded route ID identifies configured model access even when the
        // observed provider model or the current account binding has changed.
        // Those observation fields are fallback filters, not extra identity checks.
        _routeContextId = target.RouteId;
        _routeAccountId = target.RouteId is null ? target.AccountId : null;
        _routeModel = target.RouteId is null ? target.Model : null;
        _routeSearch = "";
        _routeProvider = "All providers";
        _routeVisibility = "All visibility";
        _routeState = "All states";
        if (target.RouteId is { } id) { _selectedModelRouteId = id; _modelNarrowDetail = true; _modelDetails[id] = true; }
    }

    private Action CaptureModelsNavigation()
    {
        var section = _section;
        var search = _routeSearch;
        var provider = _routeProvider;
        var visibility = _routeVisibility;
        var state = _routeState;
        var accountId = _routeAccountId;
        var routeId = _routeContextId;
        var model = _routeModel;
        var modelDetails = new Dictionary<string, bool>(_modelDetails, StringComparer.Ordinal);
        var selected = _selectedModelRouteId;
        var narrowDetail = _modelNarrowDetail;
        var listOffset = _modelInventoryOffset;
        var filterExpanded = _modelFilterExpanded;
        var profileId = _selectedProfileId;
        var profileDetails = new Dictionary<string, bool>(_profileDetails, StringComparer.Ordinal);
        return () =>
        {
            if (section == "Models")
            {
                _selectedModelRouteId = selected;
                _modelNarrowDetail = narrowDetail;
                _modelInventoryOffset = listOffset;
                _modelFilterExpanded = filterExpanded;
                _routeSearch = search;
                _routeProvider = provider;
                _routeVisibility = visibility;
                _routeState = state;
                _routeAccountId = accountId;
                _routeContextId = routeId;
                _routeModel = model;
                _modelDetails.Clear();
                foreach (var pair in modelDetails) _modelDetails.Add(pair.Key, pair.Value);
            }
            else if (section == "Profiles")
            {
                _selectedProfileId = profileId;
                _profileDetails.Clear();
                foreach (var pair in profileDetails) _profileDetails.Add(pair.Key, pair.Value);
            }
        };
    }

    private Control RoutesPage()
    {
        if (_configuration is not { } cfg) return Empty("Connect to the host to manage model access.");
        var dense = PresentationFor("Models").Density == ViewDensity.Compact;
        foreach (var id in _modelPendingRevisions.Where(p => p.Value <= cfg.AppliedRevision).Select(p => p.Key).ToArray()) _modelPendingRevisions.Remove(id);
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,0"), ColumnSpacing = 16 };
        var inventory = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"), RowSpacing = 8 };
        var create = Ui.PrimaryButton("Add model access", () => EditRoute(null));
        create.Tag = new EditorFocusTarget("Models", null, "create");
        inventory.Children.Add(Ui.PageHeader("Models", null, create));
        var search = Ui.Input(_routeSearch, "Search client names, provider models or accounts");
        AutomationProperties.SetName(search, "Search configured model access");
        var providers = new[] { "All providers" }.Concat(cfg.Routes.Select(r => r.Provider).Distinct().OrderBy(p => p, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (!providers.Contains(_routeProvider)) _routeProvider = providers[0];
        var provider = new ComboBox { ItemsSource = providers, SelectedItem = _routeProvider, MinWidth = 132 };
        var visibility = new ComboBox { ItemsSource = new[] { "All visibility", "Visible", "Hidden" }, SelectedItem = _routeVisibility, MinWidth = 116 };
        var state = new ComboBox { ItemsSource = new[] { "All states", "Enabled", "Disabled" }, SelectedItem = _routeState, MinWidth = 104 };
        AutomationProperties.SetName(provider, "Filter model access by provider");
        AutomationProperties.SetName(visibility, "Filter model visibility");
        AutomationProperties.SetName(state, "Filter model state");
        var filters = new Expander { Header = "Filter", IsExpanded = _modelFilterExpanded, Content = ProfileWrap(provider, visibility, state) };
        filters.PropertyChanged += (_, e) => { if (e.Property == Expander.IsExpandedProperty) _modelFilterExpanded = filters.IsExpanded; };
        var controls = Ui.Stack(search, filters); controls.Spacing = 5;
        Grid.SetRow(controls, 1); inventory.Children.Add(controls);
        var summary = Ui.Stack(); summary.Spacing = 4;
        var count = Ui.Text("", 11, Ui.Muted); summary.Children.Add(count);
        if (cfg.Revision != cfg.AppliedRevision)
            summary.Children.Add(Ui.Text("Saved configuration has changes pending global Apply. Per-model active differences are not supplied.", 11, Ui.Warning));
        var contextText = Ui.Text("", 12, Ui.Accent);
        var context = Ui.Stack(contextText); context.Spacing = 4;
        var templates = CompactRouteButton("Choose provider models…", () => { if (_routeAccountId is { } id) _ = ShowAccountModelsAsync(id); });
        context.Children.Add(ProfileWrap(templates, CompactRouteButton("Clear model context", () => {
            _routeAccountId = _routeContextId = _routeModel = null;
            Render();
        })));
        summary.Children.Add(context); Grid.SetRow(summary, 2); inventory.Children.Add(summary);
        var rows = new StackPanel { Spacing = 0 };
        var list = new ScrollViewer { Content = rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetName(list, "Configured model inventory");
        list.ScrollChanged += (_, _) => { if (inventory.IsVisible) _modelInventoryOffset = list.Offset; };
        Grid.SetRow(list, 3); inventory.Children.Add(list);
        var detail = new Border { Background = Ui.Surface, BorderBrush = Ui.Edge, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(14) };
        AutomationProperties.SetName(detail, "Selected model details");
        root.Children.Add(inventory); root.Children.Add(detail);
        var rowBorders = new Dictionary<string, Border>(StringComparer.Ordinal);
        var rowOrigins = new Dictionary<string, Button>(StringComparer.Ordinal);
        string AccountNames(string[] ids) => ids.Length == 0 ? "Account not supplied" : string.Join(", ", ids.Select(id => cfg.Accounts.FirstOrDefault(a => a.Id == id)?.Name ?? id));
        Grid RowGrid() => new() { ColumnDefinitions = new ColumnDefinitions("1.1*,1.5*,82,58,92"), ColumnSpacing = 8 };
        void UpdateLayout(double? availableWidth = null)
        {
            // The child is arranged during its parent's resize. Window.Bounds can
            // still describe the preceding viewport at that point.
            var wide = (availableWidth ?? root.Bounds.Width) >= 960;
            inventory.IsVisible = wide || !_modelNarrowDetail;
            detail.IsVisible = wide || _modelNarrowDetail;
            root.ColumnDefinitions = new ColumnDefinitions(wide ? "*,320" : "*");
            Grid.SetColumn(detail, wide ? 1 : 0);
        }
        void ShowDetails(string id, bool openDetail = true)
        {
            _selectedModelRouteId = id;
            if (openDetail) _modelNarrowDetail = true;
            foreach (var pair in rowBorders) pair.Value.Background = pair.Key == id ? Ui.AccentSurface : Brushes.Transparent;
            detail.Child = new ScrollViewer { Content = ModelAccessDetails(_configuration ?? cfg, id, () => {
                _modelNarrowDetail = false; _selectedModelRouteId = null; UpdateLayout();
                detail.Child = Ui.Stack(Ui.Text("Model configuration", 15), Ui.Text("Select a model to inspect its binding, profiles and provenance. Edit and actions are available in every row.", 12, Ui.Muted));
                foreach (var row in rowBorders.Values) row.Background = Brushes.Transparent;
                var offset = _modelInventoryOffset;
                Dispatcher.UIThread.Post(() => { list.Offset = offset; if (rowOrigins.TryGetValue(id, out var origin)) origin.Focus(); }, DispatcherPriority.Loaded);
            }), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            UpdateLayout();
        }
        void Populate()
        {
            rows.Children.Clear(); rowBorders.Clear(); rowOrigins.Clear();
            var contexts = new List<string>();
            if (_routeAccountId is { } account) contexts.Add($"Account: {cfg.Accounts.FirstOrDefault(a => a.Id == account)?.Name ?? account}");
            if (_routeContextId is { } routeId) contexts.Add($"Model access: {cfg.Routes.FirstOrDefault(r => r.Id == routeId)?.Alias ?? routeId}");
            if (_routeModel is { } model) contexts.Add($"Model: {model}");
            contextText.Text = string.Join(" · ", contexts); context.IsVisible = contexts.Count > 0;
            templates.IsVisible = _routeAccountId is { } accountId && cfg.Accounts.Any(a => a.Id == accountId);
            var found = cfg.Routes.Where(r => (_routeProvider == "All providers" || r.Provider == _routeProvider)
                && (_routeVisibility == "All visibility" || r.Visible == (_routeVisibility == "Visible"))
                && (_routeState == "All states" || r.Enabled == (_routeState == "Enabled"))
                && (_routeAccountId is null || r.AccountIds.Contains(_routeAccountId, StringComparer.Ordinal))
                && (_routeContextId is null || r.Id == _routeContextId)
                && (_routeModel is null || r.Alias == _routeModel || r.UpstreamModel == _routeModel)
                && $"{r.Alias} {r.UpstreamModel} {r.Provider} {AccountNames(r.AccountIds)}".Contains(_routeSearch.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.Alias, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Id, StringComparer.Ordinal).ToArray();
            count.Text = $"{found.Length} of {cfg.Routes.Length} entries · {cfg.Routes.Count(r => r.Visible && r.Enabled)} saved enabled and visible";
            var header = RowGrid(); header.Margin = new Thickness(9, 4, 9, 6);
            var labels = new[] { "Name in client", "Provider model / account", "Access", "Menu", "Actions" };
            for (var i = 0; i < labels.Length; i++) { var label = Ui.Text(labels[i], 10, Ui.Muted); Grid.SetColumn(label, i); header.Children.Add(label); }
            rows.Children.Add(header);
            foreach (var route in found)
            {
                var grid = RowGrid();
                var title = CompactRouteButton(route.Alias, () => ShowDetails(route.Id));
                title.Background = Brushes.Transparent; title.HorizontalAlignment = HorizontalAlignment.Stretch;
                title.HorizontalContentAlignment = HorizontalAlignment.Stretch; title.BorderThickness = new Thickness(0); title.CornerRadius = new CornerRadius(0);
                var alias = Ui.Text(route.Alias, 12); alias.TextAlignment = TextAlignment.Left;
                title.Content = alias; title.Padding = new Thickness(0, 3);
                AutomationProperties.SetName(title, "Select model access " + route.Alias); rowOrigins[route.Id] = title;
                grid.Children.Add(title);
                var binding = Ui.Stack(Ui.Text($"{route.Provider} / {route.UpstreamModel}", 11), Ui.Text(AccountNames(route.AccountIds), 11, Ui.Muted)); binding.Spacing = 1;
                Grid.SetColumn(binding, 1); grid.Children.Add(binding);
                var access = Ui.Stack(Ui.Text(route.Enabled ? "Enabled" : "Disabled", 11, route.Enabled ? Ui.Accent : Ui.Warning)); access.Spacing = 1;
                if (_modelPendingRevisions.ContainsKey(route.Id)) access.Children.Add(Ui.Text("Pending apply", 10, Ui.Warning));
                ToolTip.SetTip(access, "Saved access state. Disabling takes effect at the next global profile Apply.");
                Grid.SetColumn(access, 2); grid.Children.Add(access);
                var menuState = Ui.Text(route.Visible ? "Visible" : "Hidden", 11, route.Visible ? Ui.Ink : Ui.Muted);
                ToolTip.SetTip(menuState, "Hidden removes the alias from client model menus; direct requests remain possible.");
                Grid.SetColumn(menuState, 3); grid.Children.Add(menuState);
                var edit = CompactRouteButton("Edit", () => EditRoute(route)); AutomationProperties.SetName(edit, "Edit model access " + route.Alias);
                edit.Tag = new EditorFocusTarget("Models", route.Id, "inventory-edit");
                Button more = null!;
                var moreMenu = ModelAccessCommandMenu(route.Id, () => ShowDetails(route.Id), () => more, out var openMoreMenu);
                more = CompactRouteButton("⋯", () => openMoreMenu(more)); more.ContextMenu = moreMenu;
                AutomationProperties.SetName(more, "Actions for model access " + route.Alias);
                var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 3, Children = { edit, more } };
                Grid.SetColumn(actions, 4); grid.Children.Add(actions);
                var row = new Border { Child = grid, Padding = new Thickness(9, dense ? 4 : 7), BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 0, 0, 1), Background = route.Id == _selectedModelRouteId ? Ui.AccentSurface : Brushes.Transparent };
                AutomationProperties.SetName(row, "Model row " + route.Id);
                Control menuOrigin = title;
                row.ContextMenu = ModelAccessCommandMenu(route.Id, () => ShowDetails(route.Id), () => menuOrigin, out var openRowMenu);
                row.PointerPressed += (_, args) => {
                    if (args.GetCurrentPoint(row).Properties.IsRightButtonPressed)
                        menuOrigin = args.Source is Button button ? button : (args.Source as Control)?.GetVisualAncestors().OfType<Button>().FirstOrDefault() ?? title;
                };
                row.KeyDown += (_, args) => {
                    if ((args.Key == Key.F10 && args.KeyModifiers.HasFlag(KeyModifiers.Shift)) || args.Key == Key.Apps)
                    {
                        menuOrigin = args.Source as Control ?? title;
                        openRowMenu(row); args.Handled = true;
                    }
                };
                rowBorders[route.Id] = row; rows.Children.Add(row);
            }
            if (found.Length == 0)
            {
                var empty = Ui.Stack(Empty(cfg.Routes.Length == 0
                    ? "No model access yet. Choose provider models from an account or add model access manually."
                    : "No model access matches these filters."));
                if (cfg.Routes.Length > 0)
                {
                    empty.Children.Add(Ui.Text("Search query: " + (string.IsNullOrWhiteSpace(_routeSearch) ? "(empty)" : _routeSearch), 12, Ui.Muted));
                    empty.Children.Add(Ui.Button("Clear filters", () => {
                        _routeSearch = ""; _routeProvider = "All providers"; _routeVisibility = "All visibility"; _routeState = "All states";
                        _routeAccountId = _routeContextId = _routeModel = null;
                        Render();
                    }));
                }
                rows.Children.Add(empty);
            }
        }
        search.TextChanged += (_, _) => { _routeSearch = search.Text ?? ""; Populate(); };
        provider.SelectionChanged += (_, _) => { _routeProvider = provider.SelectedItem as string ?? "All providers"; Populate(); };
        visibility.SelectionChanged += (_, _) => { _routeVisibility = visibility.SelectedItem as string ?? "All visibility"; Populate(); };
        state.SelectionChanged += (_, _) => { _routeState = state.SelectedItem as string ?? "All states"; Populate(); };
        root.SizeChanged += (_, e) => UpdateLayout(e.NewSize.Width);
        Populate();
        if (_selectedModelRouteId is { } selected) ShowDetails(selected, openDetail: false);
        else detail.Child = Ui.Stack(Ui.Text("Model configuration", 15), Ui.Text("Select a model to inspect its binding, profiles and provenance. Edit and actions are available in every row.", 12, Ui.Muted));
        UpdateLayout();
        var offset = _modelInventoryOffset;
        Dispatcher.UIThread.Post(() => list.Offset = offset, DispatcherPriority.Loaded);
        return Ui.Viewport(root);
    }

    // Bind on opening, then keep that ID and revision for the lifetime of the
    // menu. Polling or selection changes must never retarget an existing action.
    private ContextMenu ModelAccessCommandMenu(string routeId, Action showDetails, Func<Control?> origin, out Action<Control> open)
    {
        var menu = ObjectCommandMenu([]);
        void Bind()
        {
            ObjectCommand[] commands = _configuration is { } cfg && cfg.Routes.FirstOrDefault(r => r.Id == routeId) is { } route
                ? ModelAccessCommands(cfg, route, showDetails)
                : [new ObjectCommand("missing", "Model access no longer exists", () => { }, false, "Return to the inventory and choose an existing stable ID.")];
            var populated = ObjectCommands.Menu(commands);
            var items = populated.Items.ToArray();
            populated.Items.Clear(); menu.Items.Clear();
            foreach (var item in items) menu.Items.Add(item);
        }
        // Avalonia raises Opening for native context requests, but Open(target)
        // can bypass it. Both paths must bind before the popup is measured.
        menu.Opening += (_, _) => Bind();
        open = target => { Bind(); menu.Open(target); };
        menu.Closed += (_, _) => Dispatcher.UIThread.Post(() => {
            if (_section == "Models" && !_editing && origin() is { } control && TopLevel.GetTopLevel(control) == this)
                control.Focus();
        }, DispatcherPriority.Loaded);
        return menu;
    }

    private bool CurrentModelCommand(long revision, string routeId)
    {
        if (_busy) return false;
        var current = _configuration;
        if (current is null || !current.Routes.Any(r => r.Id == routeId))
        {
            _notice.Text = "This model access no longer exists. No other model was selected in its place. Return to the inventory; any draft inputs are kept.";
            return false;
        }
        if (current.Revision != revision)
        {
            _notice.Text = "The saved configuration changed after these actions were opened. Reopen actions for this model access and review its current state.";
            return false;
        }
        return true;
    }

    private void RequireCurrentModelDraft(long revision, string? routeId)
    {
        if (_configuration is not { } current) throw new InvalidOperationException("Connect to the host before saving this draft.");
        if (routeId is not null && !current.Routes.Any(r => r.Id == routeId))
            throw new InvalidOperationException("This model access was removed. Your draft is kept for review and cannot recreate it or save to another model. Return to Models or discard this draft explicitly.");
        if (current.Revision != revision)
            throw new InvalidOperationException("The saved configuration changed. Your inputs are kept. Review the latest configuration before saving this model access.");
    }

    private ObjectCommand[] ModelAccessCommands(ConfigurationDto cfg, RouteDto route, Action showDetails)
    {
        Action Guard(Action execute) => () => { if (CurrentModelCommand(cfg.Revision, route.Id)) execute(); };
        void Save(RouteDto updated) => _ = RunAsync(async () => {
            RequireCurrentModelDraft(cfg.Revision, route.Id);
            _configuration = await _client.SaveRouteAsync(cfg.Revision, updated, _lifetime.Token);
            _modelPendingRevisions[route.Id] = _configuration.Revision;
        }, "Model access saved. Changes take effect at the next global profile Apply.");
        var commands = new List<ObjectCommand>
        {
            new("detail", "Model details", Guard(showDetails)),
            new("edit", "Edit model access", Guard(() => EditRoute(route))),
            new("enabled", route.Enabled ? "Disable at next Apply" : "Enable at next Apply", Guard(() => Save(route with { Enabled = !route.Enabled }))),
            new("visibility", route.Visible ? "Hide from model menu" : "Show in model menu", Guard(() => Save(route with { Visible = !route.Visible }))),
            new("usage", "Recorded usage", Guard(() => { if (HasHistory) NavigateTo(new("Usage", RouteId: route.Id)); }), HasHistory, "No recorded history source."),
            new("profiles", "Profile uses", Guard(showDetails)),
            new("copy", "Copy client model name", Guard(() => { if (Clipboard is { } clipboard) _ = clipboard.SetTextAsync(route.Alias); })),
            new("remove", "Remove model access…", Guard(() => ShowModelRemoval(cfg, route)))
        };
        foreach (var id in route.AccountIds)
        {
            var name = cfg.Accounts.FirstOrDefault(a => a.Id == id)?.Name ?? id;
            commands.Insert(commands.Count - 2, new("account-" + id, "Open account " + name, Guard(() => NavigateTo(new("Accounts", AccountId: id)))));
        }
        return commands.ToArray();
    }

    private Control ModelAccessDetails(ConfigurationDto cfg, string id, Action close)
    {
        if (cfg.Routes.FirstOrDefault(r => r.Id == id) is not { } route)
            return Ui.Stack(Ui.Text("Model access no longer exists", 16), Ui.Text("The selected stable ID was removed. No other model has been selected in its place.", 12, Ui.Muted), Ui.Button("Back to inventory", close));
        var edit = CompactRouteButton("Edit", () => EditRoute(route));
        edit.Tag = new EditorFocusTarget("Models", route.Id, "detail-edit");
        AutomationProperties.SetName(edit, "Edit selected model access " + route.Alias);
        Button more = null!;
        var moreMenu = ModelAccessCommandMenu(route.Id, () => { }, () => more, out var openMoreMenu);
        more = CompactRouteButton("⋯", () => openMoreMenu(more)); more.ContextMenu = moreMenu;
        AutomationProperties.SetName(more, "Selected model actions " + route.Alias);
        var done = CompactRouteButton("Close", close); AutomationProperties.SetName(done, "Close model details " + route.Alias);
        var body = Ui.Stack(Ui.PageHeader(route.Alias, "Model configuration", edit, more, done),
            Ui.Text("Identity and binding", 13, Ui.Muted), Ui.Text("Client name: " + route.Alias),
            Ui.Text("Provider model: " + route.Provider + " / " + route.UpstreamModel),
            Ui.Text(route.Pooled ? "Explicit account pool" : "Strict account binding", 12, Ui.Muted));
        foreach (var accountId in route.AccountIds)
            body.Children.Add(Ui.Button(cfg.Accounts.FirstOrDefault(a => a.Id == accountId)?.Name ?? accountId, () => NavigateTo(new("Accounts", AccountId: accountId))));
        if (route.AccountIds.Length == 0) body.Children.Add(Ui.Text("Account not supplied", 12, Ui.Muted));
        body.Children.Add(Ui.Text($"{(route.Enabled ? "Enabled" : "Disabled")} · {(route.Visible ? "Visible in model menus" : "Hidden from model menus")}", 12));
        body.Children.Add(Ui.Text("Saved disable takes effect after global Apply. Hidden affects model menus and does not block direct requests.", 11, Ui.Muted));
        var profiles = cfg.Profiles.Where(p => p.RouteIds.Contains(route.Id, StringComparer.Ordinal)).ToArray();
        body.Children.Add(Ui.Text($"Used by {profiles.Length} profiles", 13, Ui.Muted));
        foreach (var profile in profiles)
            body.Children.Add(Ui.Button(profile.Name + (profile.DefaultRouteId == route.Id ? " · default" : ""), () => NavigateTo(new("Profiles", ProfileId: profile.Id))));
        var fallbackProfiles = cfg.Profiles.Where(p => p.Policy?.FallbackRouteIds?.Contains(route.Id, StringComparer.Ordinal) == true).ToArray();
        foreach (var profile in fallbackProfiles)
            body.Children.Add(Ui.Button("Fallback in profile: " + profile.Name, () => NavigateTo(new("Profiles", ProfileId: profile.Id))));
        foreach (var source in cfg.Routes.Where(r => r.Id != route.Id && r.Policy?.FallbackRouteIds?.Contains(route.Id, StringComparer.Ordinal) == true))
            body.Children.Add(Ui.Button("Fallback from model: " + source.Alias, () => NavigateTo(new("Models", RouteId: source.Id))));
        body.Children.Add(Ui.Text("Spending policy: " + (route.Policy?.CostPolicy ?? "Not supplied"), 12));
        if (route.Policy?.FallbackRouteIds is { Length: > 0 } fallbacks)
            body.Children.Add(Ui.Text("Saved fallback order: " + string.Join(" → ", fallbacks.Select(f => cfg.Routes.FirstOrDefault(r => r.Id == f)?.Alias ?? f)), 12));
        body.Children.Add(new Expander { Header = "Provenance and technical", Content = Ui.Stack(
            Ui.Text("Model access ID: " + route.Id, 11, Ui.Muted), Ui.Text("Provider route: " + (route.ProviderRouteId ?? "Not supplied"), 11, Ui.Muted),
            Ui.Text("Unit price, quota and tool capabilities: Not supplied by configured model access.", 11, Ui.Muted)) });
        Control detailMenuOrigin = more;
        body.ContextMenu = ModelAccessCommandMenu(route.Id, () => { }, () => detailMenuOrigin, out var openDetailMenu);
        body.PointerPressed += (_, args) => {
            if (args.GetCurrentPoint(body).Properties.IsRightButtonPressed)
                detailMenuOrigin = args.Source is Button button ? button : (args.Source as Control)?.GetVisualAncestors().OfType<Button>().FirstOrDefault() ?? more;
        };
        body.KeyDown += (_, args) => {
            if ((args.Key == Key.F10 && args.KeyModifiers.HasFlag(KeyModifiers.Shift)) || args.Key == Key.Apps)
            { detailMenuOrigin = args.Source as Control ?? more; openDetailMenu(body); args.Handled = true; }
        };
        return body;
    }

    private void ShowModelRemoval(ConfigurationDto cfg, RouteDto route)
    {
        if (!CanOpenSectionEditor()) return;
        _editing = true;
        var references = ModelRouteReferences(cfg, route.Id);
        var remove = Action("Remove model access", async () => {
            RequireCurrentModelDraft(cfg.Revision, route.Id);
            var currentReferences = ModelRouteReferences(_configuration!, route.Id);
            if (currentReferences.Length > 0) throw new InvalidOperationException("Remove the saved references first: " + string.Join(", ", currentReferences));
            _configuration = await _client.RemoveRouteAsync(cfg.Revision, route.Id, _lifetime.Token);
            if (_selectedModelRouteId == route.Id) { _selectedModelRouteId = null; _modelNarrowDetail = false; }
        }, "Model access removed. Historical usage remains available.");
        remove.Classes.Add("danger");
        remove.IsEnabled = references.Length == 0;
        _page.Content = Ui.Stack(Heading("Remove " + route.Alias + "?", "This changes saved model access. Historical usage remains available."),
            Ui.Text(references.Length == 0 ? "No saved profile or fallback references." : "Saved references:\n" + string.Join("\n", references), 13, references.Length == 0 ? Ui.Muted : Ui.Warning),
            Ui.Text("The host validates references before removal. Review the named profiles and fallbacks first.", 12, Ui.Muted),
            OverviewActions(remove, Ui.Button("Cancel", () => { _editing = false; Render(); })));
    }

    private static string[] ModelRouteReferences(ConfigurationDto cfg, string routeId) =>
        cfg.Profiles.Where(p => p.RouteIds.Contains(routeId, StringComparer.Ordinal)).Select(p => "Profile member: " + p.Name)
            .Concat(cfg.Profiles.Where(p => p.Policy?.FallbackRouteIds?.Contains(routeId, StringComparer.Ordinal) == true).Select(p => "Profile fallback: " + p.Name))
            .Concat(cfg.Routes.Where(r => r.Id != routeId && r.Policy?.FallbackRouteIds?.Contains(routeId, StringComparer.Ordinal) == true).Select(r => "Fallback from model: " + r.Alias)).ToArray();

    private static Button CompactRouteButton(string label, Action click)
    {
        var button = Ui.Button(label, click);
        button.Padding = new Thickness(9, 3); button.MinHeight = 28;
        return button;
    }

    private void EditRoute(RouteDto? route, ModelEntryDto? catalogModel = null)
    {
        if (!CanOpenSectionEditor()) return;
        if (_configuration is not { } cfg) return;
        if (route is not null)
        {
            if (cfg.Routes.FirstOrDefault(r => r.Id == route.Id) is not { } current)
            {
                _notice.Text = "This model access no longer exists. Return to the inventory and select an existing stable ID.";
                return;
            }
            route = current;
        }
        var routeId = route?.Id;
        var returnFocus = CaptureEditorFocus("Models", routeId,
            routeId is null ? "create" : _modelNarrowDetail ? "detail-edit" : "inventory-edit", _modelEditorFocusTarget);
        _modelEditorFocusTarget = returnFocus;
        if (routeId is not null) _selectedModelRouteId = routeId;
        var savedRouteId = routeId;
        RegisterEditor(() =>
        {
            if (routeId is null) { EditRoute(null); return; }
            var latest = _configuration?.Routes.FirstOrDefault(item => item.Id == routeId)
                ?? throw new InvalidOperationException("This model access was removed from the latest configuration. Cancel this draft and review Models.");
            EditRoute(latest);
        }, cfg.Revision, route?.Alias ?? "New model access");
        _editing = true;
        var contextAccount = route is null ? cfg.Accounts.FirstOrDefault(a => a.Id == _routeAccountId) : null;
        var alias = Ui.Input(route?.Alias ?? catalogModel?.Id ?? _routeModel, "client-facing-model");
        var model = Ui.Input(route?.UpstreamModel ?? catalogModel?.Id ?? _routeModel, "Upstream model ID");
        var provider = Ui.Input(route?.Provider ?? catalogModel?.Provider ?? contextAccount?.Provider, "Provider identifier");
        var providerRouteId = Ui.Input(route?.ProviderRouteId, "Optional concrete catalog route ID");
        var presentation = PresentationFor("Models");
        var routeChoices = new ComboBox { PlaceholderText = "Load documented provider routes", HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(routeChoices, "Provider catalog route");
        var documentedRoutes = Array.Empty<(string Provider, ProviderRouteDescriptorDto Route)>();
        var loadChoices = Ui.Button("Load documented provider routes", async () =>
        {
            await RefreshProviderCatalogAsync();
            documentedRoutes = (_providerCatalog?.Providers ?? []).SelectMany(item => item.Routes.Select(candidate => (item.Id, candidate))).ToArray();
            routeChoices.ItemsSource = documentedRoutes.Select(item => $"{item.Provider} · {item.Route.Name} · {item.Route.Protocol}").ToArray();
        });
        routeChoices.SelectionChanged += (_, _) =>
        {
            if (routeChoices.SelectedIndex < 0 || routeChoices.SelectedIndex >= documentedRoutes.Length) return;
            var selected = documentedRoutes[routeChoices.SelectedIndex];
            providerRouteId.Text = selected.Route.Id; provider.Text = selected.Provider; model.Text = selected.Route.Model;
        };
        var spending = new SpendingPolicyEditor(route?.Policy);
        var fallback = new RouteFallbackEditor(cfg, route);
        void UpdateFallbackPermission() => fallback.SetCostPolicy(spending.Code);
        spending.Changed += UpdateFallbackPermission;
        UpdateFallbackPermission();
        var policyStatus = Ui.Text("Check saved route prerequisites without contacting the provider.", 12, Ui.Muted);
        var policyCheck = SpendingPolicyCheck(spending, () => route is null ? [] : [route.Id], policyStatus);
        if (route is null) policyStatus.Text = "Save a concrete route to check its runtime prerequisites. The host validates its spending policy before saving.";
        var enabled = new CheckBox { Content = "Enabled", IsChecked = route?.Enabled ?? true };
        var visible = new CheckBox { Content = "Visible in client model menu", IsChecked = route?.Visible ?? true };
        var pooled = new CheckBox { Content = "Explicit account pool", IsChecked = route?.Pooled ?? catalogModel?.AccountIds.Length > 1 };
        AutomationProperties.SetName(enabled, "Model access enabled");
        AutomationProperties.SetName(visible, "Model access visible in client menu");
        AutomationProperties.SetName(pooled, "Model access uses explicit account pool");
        var selectedIds = route?.AccountIds ?? catalogModel?.AccountIds ?? (contextAccount is null ? [] : new[] { contextAccount.Id });
        var choices = cfg.Accounts.Select(a => (Account: a, Box: new CheckBox { Content = Ui.Text($"{a.Name} · {a.Provider}"), IsChecked = selectedIds.Contains(a.Id), MinHeight = 30 })).ToArray();
        var accounts = new StackPanel { Spacing = 2 };
        foreach (var choice in choices)
        {
            AutomationProperties.SetName(choice.Box, $"Bind account {choice.Account.Id}");
            AutomationProperties.SetHelpText(choice.Box, $"{choice.Account.Name} · {choice.Account.Provider}");
            accounts.Children.Add(choice.Box);
        }
        if (choices.Length == 0) accounts.Children.Add(Ui.Text("Add an account before saving model access.", 13, Ui.Muted));
        var knownModels = _models;
        var catalog = new ComboBox { ItemsSource = new[] { "Enter a model manually" }.Concat(knownModels.Select(m => $"{m.Id} · {m.Provider} · {m.Source}")).ToArray(), SelectedIndex = 0 };
        catalog.SelectionChanged += (_, _) =>
        {
            if (catalog.SelectedIndex <= 0) return;
            var selected = knownModels[catalog.SelectedIndex - 1];
            model.Text = selected.Id; provider.Text = selected.Provider;
            if (string.IsNullOrWhiteSpace(alias.Text)) alias.Text = selected.Id;
            foreach (var choice in choices) choice.Box.IsChecked = selected.AccountIds.Contains(choice.Account.Id);
            pooled.IsChecked = selected.AccountIds.Length > 1;
        };
        var accountCount = Ui.Text("", 12, Ui.Muted);
        void UpdateBindingSummary()
        {
            var count = choices.Count(c => c.Box.IsChecked == true);
            accountCount.Text = pooled.IsChecked == true ? $"Pool · {count} selected accounts may serve this route." : $"Strict binding · {count} selected. Exactly one account is required.";
        }
        foreach (var choice in choices) choice.Box.IsCheckedChanged += (_, _) => UpdateBindingSummary();
        pooled.IsCheckedChanged += (_, _) => UpdateBindingSummary();
        UpdateBindingSummary();
        var suggestions = new Expander { Header = "Known model suggestions (optional)", HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = Ui.Stack(Ui.Field("Known model suggestion", catalog),
                Ui.Text("Suggestions are derived from saved accounts and configuration. They do not establish provider availability or live access. You can enter any model ID manually.", 11, Ui.Muted)) };
        var provenance = new Expander { Header = "Provider route provenance", HorizontalAlignment = HorizontalAlignment.Stretch,
            IsExpanded = presentation.Level == InformationLevel.Advanced,
            Content = Ui.Stack(Ui.Field("Provider route provenance ID", providerRouteId), routeChoices, loadChoices,
                Ui.Text("Documented provider routes supply pricing provenance for spending rules. Selecting one updates its provider model. Existing provenance stays saved unless you edit it.", 11, Ui.Muted)) };
        var identity = Ui.Stack(Ui.Text("Model identity", 14), Ui.Field("Client name", alias), Ui.Field("Provider model ID", model), Ui.Field("Provider", provider), suggestions, provenance);
        identity.Spacing = presentation.Density == ViewDensity.Compact ? 6 : 10;
        var binding = Ui.Stack(Ui.Text("Account binding", 14), Ui.Text("A strict route uses one account. Enable an explicit pool to allow several accounts.", 12, Ui.Muted), pooled, accountCount,
            new ScrollViewer { MaxHeight = 260, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = accounts },
            new Border { Height = 1, Background = Ui.Edge }, enabled, visible,
            new Expander { Header = "Spending and fallback policy", IsExpanded = presentation.Level == InformationLevel.Advanced,
                Content = Ui.Stack(spending.Control, fallback.Control, policyCheck, policyStatus) },
            Ui.Text("Checks use the saved route. Save changes to its model or account binding before checking again. Configuration eligibility does not prove live access or a fulfilled spending cap.", 11, Ui.Muted));
        binding.Spacing = 10;
        var form = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 24 };
        form.Children.Add(identity); Grid.SetColumn(binding, 1); form.Children.Add(binding);
        var save = Action("Save model access", async () => {
            RequireCurrentModelDraft(cfg.Revision, routeId);
            var ids = choices.Where(x => x.Box.IsChecked == true).Select(x => x.Account.Id).ToArray();
            if (string.IsNullOrWhiteSpace(alias.Text) || string.IsNullOrWhiteSpace(model.Text) || string.IsNullOrWhiteSpace(provider.Text) || ids.Length == 0) throw new InvalidOperationException("Enter a client name, provider model, provider and at least one account.");
            if (ids.Length > 1 && pooled.IsChecked != true) throw new InvalidOperationException("Select one account or explicitly enable the pool.");
            if (route is not null && route.AccountIds.Length == ids.Length && route.AccountIds.All(id => ids.Contains(id, StringComparer.Ordinal))) ids = route.AccountIds;
            var original = route ?? new RouteDto(Guid.NewGuid().ToString("N"), "", "", "", []);
            var saved = original with
            {
                Alias = alias.Text.Trim(), UpstreamModel = model.Text.Trim(), Provider = provider.Text.Trim(), AccountIds = ids,
                Pooled = pooled.IsChecked == true, Visible = visible.IsChecked == true, Enabled = enabled.IsChecked == true,
                ProviderRouteId = providerRouteId.Text == route?.ProviderRouteId ? route?.ProviderRouteId : string.IsNullOrWhiteSpace(providerRouteId.Text) ? null : providerRouteId.Text.Trim(),
                Policy = fallback.Resolve(spending.Resolve())
            };
            _configuration = await _client.SaveRouteAsync(cfg.Revision, saved, _lifetime.Token);
            _selectedModelRouteId = saved.Id;
            savedRouteId = saved.Id;
            if (routeId is null) _modelNarrowDetail = true;
            _modelPendingRevisions[saved.Id] = _configuration.Revision;
        }, "Model access saved.", afterSuccess: () => RestoreEditorFocus(routeId is null
            ? new("Models", savedRouteId, "detail-edit") : returnFocus));
        save.Classes.Add("primary");
        var editor = Ui.Stack(Heading(route is null ? "Add model access" : $"Edit model access {route.Alias}", "Choose the client name, provider model and account binding. Saved changes take effect after global Apply."));
        if (route is null && contextAccount is not null)
            editor.Children.Add(Ui.Button("Use " + contextAccount.Name + " provider templates instead", () => _ = ShowAccountModelsAsync(contextAccount.Id)));
        editor.Children.Add(new Border { BorderBrush = Ui.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(18), Child = form });
        editor.Children.Add(OverviewActions(save, Ui.Button("Cancel", () => { _editing = false; Render(); RestoreEditorFocus(returnFocus); })));
        _page.Content = editor;
    }
    private sealed record RouteFallbackChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed class RouteFallbackEditor
    {
        private readonly RoutingPolicyDto? _original;
        private readonly ComboBox[] _slots;
        private readonly CheckBox _paid;
        public Control Control { get; }
        public RouteFallbackEditor(ConfigurationDto cfg, RouteDto? route)
        {
            _original = route?.Policy;
            var original = _original?.FallbackRouteIds ?? [];
            var choices = new[] { new RouteFallbackChoice("", "None") }
                .Concat(cfg.Routes.Where(r => r.Id != route?.Id).OrderBy(r => r.Alias, StringComparer.OrdinalIgnoreCase)
                    .Select(r => new RouteFallbackChoice(r.Id, r.Alias + (r.Enabled ? "" : " · disabled"))))
                .Concat(original.Where(id => !cfg.Routes.Any(r => r.Id == id)).Select(id => new RouteFallbackChoice(id, id + " · missing"))).ToArray();
            _slots = Enumerable.Range(0, Math.Max(1, choices.Length - 1)).Select(i => new ComboBox {
                ItemsSource = choices, SelectedItem = choices.FirstOrDefault(c => c.Id == (i < original.Length ? original[i] : "")) ?? choices[0],
                HorizontalAlignment = HorizontalAlignment.Stretch }).ToArray();
            var fields = _slots.Select((box, i) => Ui.Field("Fallback position " + (i + 1), box)).ToArray();
            void UpdateSlots()
            {
                var last = Array.FindLastIndex(_slots, c => c.SelectedItem is RouteFallbackChoice { Id.Length: > 0 });
                for (var i = 0; i < fields.Length; i++) fields[i].IsVisible = i <= last + 1;
            }
            foreach (var slot in _slots) slot.SelectionChanged += (_, _) => UpdateSlots();
            UpdateSlots();
            _paid = new CheckBox { Content = "Allow paid or unknown-price fallbacks", IsChecked = _original?.AllowPaidFallback ?? false };
            AutomationProperties.SetName(_paid, "Allow paid fallback routes");
            var panel = Ui.Stack(Ui.Text("Explicit fallback order", 13),
                Ui.Text("Only selected routes are fallback candidates. The host validates missing routes, loops and spending policy. No provider cascade is inferred.", 11, Ui.Muted));
            foreach (var field in fields) panel.Children.Add(field);
            panel.Children.Add(_paid); Control = panel;
        }
        public void SetCostPolicy(string code)
        {
            _paid.IsEnabled = code != "free_only";
            if (!_paid.IsEnabled) _paid.IsChecked = false;
        }
        public RoutingPolicyDto? Resolve(RoutingPolicyDto? policy)
        {
            var selected = _slots.Select(c => (c.SelectedItem as RouteFallbackChoice)?.Id).Where(id => !string.IsNullOrEmpty(id)).Cast<string>().ToArray();
            if (selected.Distinct(StringComparer.Ordinal).Count() != selected.Length) throw new InvalidOperationException("Choose each fallback model only once.");
            var paid = _paid.IsEnabled && _paid.IsChecked == true;
            if (selected.SequenceEqual(_original?.FallbackRouteIds ?? []) && paid == (_original?.AllowPaidFallback ?? false)) return policy;
            return (policy ?? new RoutingPolicyDto()) with { FallbackRouteIds = selected.Length == 0 ? null : selected, AllowPaidFallback = paid };
        }
    }

    private sealed class SpendingPolicyEditor
    {
        private readonly RoutingPolicyDto? _original;
        private readonly int _initial;
        private readonly ComboBox _choice;
        public Control Control { get; }
        public event Action? Changed;
        public string Code => _choice.SelectedIndex switch { 0 => "configured", 1 => "free_only", _ => _original?.CostPolicy ?? "configured" };
        public SpendingPolicyEditor(RoutingPolicyDto? original)
        {
            _original = original;
            _initial = original?.CostPolicy switch { "free_only" => 1, null or "configured" => 0, _ => 2 };
            var labels = new List<string> { "Use configured access", "Only free — no paid fallback" };
            if (_initial == 2) labels.Add("Keep existing policy");
            _choice = new ComboBox { ItemsSource = labels, SelectedIndex = _initial, HorizontalAlignment = HorizontalAlignment.Stretch };
            _choice.SelectionChanged += (_, _) => Changed?.Invoke();
            var panel = Ui.Stack(Ui.Field("Spending policy", _choice), Ui.Text(
                "Only free excludes paid and unknown-price routes and fallbacks. Existing fallback selections stay saved; the host validates every selected route.", 11, Ui.Muted));
            if (original?.FallbackRouteIds is { Length: > 0 } fallbacks)
                panel.Children.Add(Ui.Text($"{fallbacks.Length} existing fallback selections retained · Paid fallback permission: {(original.AllowPaidFallback ? "previously allowed" : "not allowed")}", 11, Ui.Muted));
            Control = panel;
        }
        public RoutingPolicyDto? Resolve()
        {
            if (_choice.SelectedIndex == _initial) return _original;
            return (_original ?? new RoutingPolicyDto()) with
            {
                CostPolicy = Code, AllowPaidFallback = Code == "free_only" ? false : _original?.AllowPaidFallback ?? false
            };
        }
    }

    private Button SpendingPolicyCheck(SpendingPolicyEditor editor, Func<string[]> routeIds, TextBlock status)
    {
        var button = new Button { Content = "Check spending policy" };
        AutomationProperties.SetName(button, "Check spending policy");
        button.Click += async (_, _) =>
        {
            var ids = routeIds();
            if (ids.Length == 0) { status.Text = "Save or select a route first."; return; }
            button.IsEnabled = false;
            status.Text = "Checking saved route policy…";
            try
            {
                var decisions = await Task.WhenAll(ids.Select(id => _client.RouteCostPolicyAsync(id, editor.Code, _lifetime.Token)));
                status.Text = string.Join("\n\n", decisions.Select(decision =>
                {
                    var label = _configuration?.Routes.FirstOrDefault(route => route.Id == decision.RouteId)?.Alias ?? decision.RouteId;
                    var description = decision.Allowed
                        ? decision.CostPolicy == "free_only"
                            ? "Configuration is eligible for guarded free-only routing. Runtime must still verify its spending controls before every request. OpenRouter requires a zero-USD API-key limit including BYOK use; this check does not inspect that cap or prove live access."
                            : "Configured access is permitted. It can use paid access under your selected plan; this check does not verify live access."
                        : decision.Reasons.Length == 0 ? "Policy is not allowed; no explanation was supplied." : string.Join("\n", decision.Reasons.Select(CostPolicyReason));
                    return $"{label}: {description}";
                }));
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception error) { status.Text = Friendly(error); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private static readonly Dictionary<string, string> CostPolicyReasons = new(StringComparer.Ordinal)
    {
        ["unclassified_route"] = "Choose a concrete provider catalog route before requesting strict free-only access",
        ["route_not_permanently_free"] = "The route is paid or its free pricing is unverified",
        ["free_only_not_documented"] = "The provider documentation does not establish free-only eligibility",
        ["upstream_cost_controls_unavailable"] = "This route has no qualified runtime spending controls",
        ["unknown_provider_route"] = "The saved provider catalog route is unknown",
        ["provider_model_mismatch"] = "The catalog provenance does not match the selected upstream model",
        ["invalid_cost_policy"] = "The saved spending policy is unsupported",
        ["fallback_cycle"] = "Fallback routes form a loop",
        ["unknown_fallback_route"] = "A selected fallback route is missing",
        ["disabled_fallback_route"] = "A selected fallback route is disabled",
        ["paid_or_unknown_fallback_requires_opt_in"] = "A paid or unknown-price fallback requires explicit permission"
    };
    private static string CostPolicyReason(string reason) => CostPolicyReasons.GetValueOrDefault(reason) ?? reason.Replace('_', ' ');
    private static string CostPolicyMessage(string message)
    {
        foreach (var pair in CostPolicyReasons) message = message.Replace(pair.Key, pair.Value, StringComparison.Ordinal);
        return message;
    }

}
