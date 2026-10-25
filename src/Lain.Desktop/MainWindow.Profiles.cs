using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Input;
using Avalonia.Threading;
using Lain.Client;
using Lain.Cli;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Lain.Core;
using Lain.Desktop.Presentation;
using System.Text.Json;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private string? _selectedProfileId;
    private readonly Dictionary<string, bool> _profileDetails = new(StringComparer.Ordinal);

    private string _profileSearch = "";
    private Vector _profileListOffset;
    private readonly Dictionary<string, Vector> _profileDetailOffsets = new(StringComparer.Ordinal);
    private bool _profileNarrowDetail;
    private Button? _profileApplyFocus;
    private EditorFocusTarget? _profileEditorFocusTarget;
    private ProfileReview? _profileReview;
    private Action? _refreshProfileStatusUi;
    private string? _profileUnconfirmedApplyId;
    private long? _profileUnconfirmedApplyRevision;
    private StatusDto? _profileLastConfirmedStatus;
    private bool ProfileApplyOutcomeUnknown => _profileUnconfirmedApplyId is not null;
    private StatusDto? ProfileConfirmedStatus => ProfileApplyOutcomeUnknown ? _profileLastConfirmedStatus : _status;

    private sealed class ProfileReview(string profileId, string profileName)
    {
        public string ProfileId { get; } = profileId;
        public string ProfileName { get; } = profileName;
        public ProfileApplyPreviewDto? Preview;
        public ConfigurationDto? Snapshot;
        public int Request;
        public bool Working;
        public bool Committing;
        public bool Stale;
        public bool AwaitingOutcome;
        public string Message = "Loading current saved configuration…";
        public Action? RefreshUi;
        public Button? CancelButton;
    }

    private Control ProfilesPage()
    {
        var presentation = PresentationFor("Profiles");
        var create = Ui.IconButton("add", "Create profile", () => EditProfile(null));
        create.Tag = new EditorFocusTarget("Profiles", null, "create");
        if (_configuration is not { } cfg)
            return Ui.Stack(ProfileHeader("Profiles", "Saved model menus.", create), Empty("Connect to the host to manage profiles."));
        var status = Ui.Text(AppliedProfileDescription(cfg), 12, Ui.Muted);
        AutomationProperties.SetName(status, "Applied proxy profile status");
        var summary = Ui.Stack(status);
        summary.Spacing = 3;
        void UpdateSummary()
        {
            var current = _configuration ?? cfg;
            status.Text = AppliedProfileDescription(current);
            while (summary.Children.Count > 1) summary.Children.RemoveAt(summary.Children.Count - 1);
            var reported = ProfileConfirmedStatus;
            var pending = !ProfileApplyOutcomeUnknown && reported is not null && reported.Revision >= current.Revision
                ? reported.Revision != reported.AppliedRevision
                : current.Revision != current.AppliedRevision;
            if (pending)
                summary.Children.Add(Ui.Text("Saved changes pending · Viewing and saving do not change the proxy.", 12, Ui.Warning));
            if (_status?.Application is { State: "recovery-required" } recovery)
                summary.Children.Add(Ui.Text("Apply needs recovery. " + recovery.RecoveryAction, 12, Ui.Warning));
            else if (_status?.Application is { State: "draining" or "publishing" } operation)
                summary.Children.Add(Ui.Text("Apply in progress: " + operation.State + ". All clients share this runtime.", 12, Ui.Warning));
            else if (_status?.Application is { State: "legacy-preserved" })
                summary.Children.Add(Ui.Text("Existing runtime preserved. Its profile identity may be unknown.", 12, Ui.Muted));
        }
        _refreshProfileStatusUi = UpdateSummary;
        UpdateSummary();
        if (cfg.Profiles.Length == 0)
            return Ui.Stack(ProfileHeader("Profiles", "Saved model menus.", create), summary,
                Empty("No saved profiles yet. Create a profile to select its models and default."));

        var selected = cfg.Profiles.FirstOrDefault(p => p.Id == _selectedProfileId) ?? cfg.Profiles[0];
        _selectedProfileId = selected.Id;
        var search = Ui.Input(_profileSearch, "Search profiles…");
        AutomationProperties.SetName(search, "Search profiles");
        var list = new StackPanel { Spacing = 4 };
        var listScroll = new ScrollViewer { Content = list, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetName(listScroll, "Profile inventory");
        listScroll.ScrollChanged += (_, _) => { if (listScroll.IsEffectivelyVisible && listScroll.Bounds.Height > 0) _profileListOffset = listScroll.Offset; };
        var detailHost = new ContentControl();
        var detailScroll = new ScrollViewer { Content = detailHost, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetName(detailScroll, "Profile details");
        detailScroll.ScrollChanged += (_, _) => { if (detailScroll.IsEffectivelyVisible && detailScroll.Bounds.Height > 0 && _selectedProfileId is { } id) _profileDetailOffsets[id] = detailScroll.Offset; };
        var selectors = new Dictionary<string, Button>();
        var profileLabels = new Dictionary<string, StackPanel>();
        var detailActions = new List<Button>();
        var reviewHost = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        var back = Ui.Button("Back to profiles", () => { });
        back.IsVisible = false;
        var detail = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 6 };
        detail.Children.Add(back); Grid.SetRow(detailScroll, 1); detail.Children.Add(detailScroll); Grid.SetRow(reviewHost, 1); detail.Children.Add(reviewHost);
        var inventory = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 8 };
        inventory.Children.Add(ProfileHeader("Profiles", "Select to view", create));
        Grid.SetRow(search, 1); inventory.Children.Add(search);
        Grid.SetRow(listScroll, 2); inventory.Children.Add(listScroll);
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*"), ColumnSpacing = 14 };
        layout.Children.Add(inventory); Grid.SetColumn(detail, 1); layout.Children.Add(detail);
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 10 };
        root.Children.Add(summary); Grid.SetRow(layout, 1); root.Children.Add(layout);
        // SizeChanged can run before the parent window updates Bounds. Use this
        // viewport's event size: both 760px Normal and 772px Compact are narrow.
        var viewportWidth = 0d;
        bool Narrow() => viewportWidth is > 0 and < 800;
        void Arrange()
        {
            var narrow = Narrow() && _profileReview is null;
            layout.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "220,*");
            Grid.SetColumn(detail, narrow ? 0 : 1);
            inventory.IsVisible = !narrow || !_profileNarrowDetail;
            detail.IsVisible = !narrow || _profileNarrowDetail;
            back.IsVisible = narrow;
        }
        back.Click += (_, _) =>
        {
            _profileNarrowDetail = false;
            Arrange();
            Dispatcher.UIThread.Post(() => { listScroll.Offset = _profileListOffset; selectors.GetValueOrDefault(_selectedProfileId ?? "")?.Focus(); }, DispatcherPriority.Loaded);
        };
        void SelectProfile(ProfileDto profile, bool openDetail = true)
        {
            if (_busy && openDetail) return;
            if (openDetail && _selectedProfileId is { } previous) _profileDetailOffsets[previous] = detailScroll.Offset;
            if (_profileReview is { } previousReview && previousReview.ProfileId != profile.Id)
            {
                previousReview.Request++;
                _profileReview = null;
            }
            _selectedProfileId = profile.Id;
            if (openDetail) _profileNarrowDetail = true;
            foreach (var (id, selector) in selectors)
            {
                selector.Background = id == profile.Id ? Ui.AccentSurface : Brushes.Transparent;
                selector.BorderBrush = id == profile.Id ? Ui.Accent : Brushes.Transparent;
                AutomationProperties.SetHelpText(selector, id == profile.Id ? "Viewing; selection does not change the proxy" : "View saved profile");
            }
            foreach (var action in detailActions) _actions.Remove(action);
            detailActions.Clear();
            var reviewing = _profileReview is { } review && review.ProfileId == profile.Id;
            detailScroll.IsVisible = !reviewing;
            reviewHost.IsVisible = reviewing;
            if (reviewing) reviewHost.Content = ProfileApplyReview(_profileReview!);
            else detailHost.Content = ProfileDetails(cfg, profile, detailActions, presentation);
            Arrange();
            var restore = _profileDetailOffsets.GetValueOrDefault(profile.Id);
            Dispatcher.UIThread.Post(() => { detailScroll.Offset = restore; if (openDetail && Narrow()) back.Focus(); }, DispatcherPriority.Loaded);
        }
        foreach (var profile in cfg.Profiles)
        {
            var button = Ui.Button("", () => SelectProfile(profile));
            var label = new StackPanel { Spacing = 3, Children = { Ui.Text(profile.Name, 14), Ui.Text($"{profile.RouteIds.Length} models", 12, Ui.Muted) } };
            profileLabels.Add(profile.Id, label);
            button.Content = label;
            AutomationProperties.SetName(button, $"Select profile {profile.Name}");
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.Padding = new Thickness(10, 8);
            selectors.Add(profile.Id, button); list.Children.Add(button);
        }
        // Status replies can outlive their review (Cancel or another selection).
        // Update these mounted text surfaces directly; do not replace a focused
        // page or restore an obsolete review just to publish the global outcome.
        _refreshProfileStatusUi = () =>
        {
            UpdateSummary();
            foreach (var (id, label) in profileLabels)
            {
                while (label.Children.Count > 2) label.Children.RemoveAt(label.Children.Count - 1);
                if (ProfileConfirmedStatus?.ActiveProfileId == id)
                    label.Children.Add(Ui.Text((ProfileApplyOutcomeUnknown ? "Last confirmed · revision " : "Applied · revision ") +
                        (ProfileConfirmedStatus.ActiveProfileRevision?.ToString() ?? "unknown"), 11, Ui.Accent));
                if (ProfileApplyOutcomeUnknown && _profileUnconfirmedApplyId == id)
                    label.Children.Add(Ui.Text("Apply outcome unknown · requested " + _profileUnconfirmedApplyRevision, 11, Ui.Warning));
            }
        };
        _refreshProfileStatusUi();
        var noMatches = Ui.Text("No matching profiles.", 12, Ui.Muted);
        list.Children.Add(noMatches);
        void Filter()
        {
            _profileSearch = search.Text ?? "";
            foreach (var profile in cfg.Profiles) selectors[profile.Id].IsVisible = profile.Name.Contains(_profileSearch.Trim(), StringComparison.OrdinalIgnoreCase);
            noMatches.IsVisible = !selectors.Values.Any(button => button.IsVisible);
        }
        search.TextChanged += (_, _) => Filter();
        root.SizeChanged += (_, e) => { viewportWidth = e.NewSize.Width; Arrange(); };
        Filter(); SelectProfile(selected, false);
        var inventoryOffset = _profileListOffset;
        Dispatcher.UIThread.Post(() => { listScroll.Offset = inventoryOffset; Arrange(); }, DispatcherPriority.Loaded);
        return Ui.Viewport(root);
    }

    private string AppliedProfileDescription(ConfigurationDto cfg)
    {
        var lastReport = ProfileApplyOutcomeUnknown ? "Apply outcome unknown · Last confirmed" : _hostAvailable ? "Applied to proxy" : "Last host report: applied to proxy";
        var reported = ProfileConfirmedStatus;
        var configuration = ProfileApplyOutcomeUnknown
            ? $"Requested {_profileUnconfirmedApplyId} · revision {_profileUnconfirmedApplyRevision} · Configuration: saved {cfg.Revision}; active outcome unconfirmed"
            : reported is not null && reported.Revision > cfg.Revision
                ? $"Configuration: saved snapshot {cfg.Revision} / host saved {reported.Revision} / proxy {reported.AppliedRevision}"
                : $"Configuration: saved {cfg.Revision} / proxy {(reported is not null && reported.Revision >= cfg.Revision ? reported.AppliedRevision : cfg.AppliedRevision)}";
        if (reported?.ActiveProfileId is not { } id) return ProfileApplyOutcomeUnknown
            ? $"Apply outcome unknown · Last confirmed profile not reported · {configuration}"
            : $"Applied profile not reported by the host · {configuration}";
        var name = cfg.Profiles.FirstOrDefault(p => p.Id == id)?.Name ?? id;
        var revision = reported?.ActiveProfileRevision is { } value ? value.ToString() : "unknown";
        return $"{lastReport}: {name} · Profile revision {revision} · {configuration}";
    }

    private Control ProfileDetails(ConfigurationDto cfg, ProfileDto selected, List<Button> detailActions, PresentationOptions presentation)
    {
        var apply = Ui.Button("Apply to proxy…", () => ConfirmProfileApplication(selected));
        apply.Classes.Add("primary");
        _profileApplyFocus = apply;
        apply.IsEnabled = _hostAvailable && !_busy;
        var export = Action("Export profile…", () => ExportProfileAsync(selected), "Profile exported without credentials.");
        detailActions.Add(export);
        var routes = new StackPanel { Spacing = 0 };
        foreach (var id in selected.RouteIds)
        {
            var route = cfg.Routes.FirstOrDefault(r => r.Id == id);
            if (route is null) { routes.Children.Add(Ui.Text(id + " · Unavailable model", 12, Ui.Warning)); continue; }
            var state = !route.Enabled ? "Disabled" : !route.Visible ? "Hidden from client menu" : "Visible";
            var accounts = string.Join(", ", route.AccountIds.Select(accountId => cfg.Accounts.FirstOrDefault(a => a.Id == accountId)?.Name ?? accountId));
            var open = Ui.Button("", () => NavigateTo(new WorkspaceNavigationRequest("Models", RouteId: id)));
            open.Content = ProfileRouteContent(route.Alias, $"{route.UpstreamModel} · {accounts} · {state}", id == selected.DefaultRouteId);
            open.HorizontalAlignment = HorizontalAlignment.Stretch;
            open.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            open.Padding = new Thickness(10, 6);
            open.Background = Brushes.Transparent;
            AutomationProperties.SetName(open, $"Open model access {route.Alias}");
            routes.Children.Add(new Border { BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 0, 0, 1), Child = open });
        }
        var selectionState = Ui.Text($"Viewing {selected.Name}", 20);
        AutomationProperties.SetName(selectionState, "Selected profile status");
        ObjectCommand[] commands =
        [
            new("apply", "Preview and apply…", () => ConfirmProfileApplication(selected), _hostAvailable),
            new("edit", "Edit profile", () => EditProfile(selected)),
            new("export", "Export profile…", () => _ = RunAsync(() => ExportProfileAsync(selected), "Profile exported without credentials.")),
            new("codex", "Set up Codex client…", () => ShowCodexExport(selected)),
            new("remove", "Remove profile…", () => ConfirmProfileRemoval(selected.Id))
        ];
        var menu = ObjectCommandMenu(commands);
        var moreButton = Ui.Button("⋯", () => menu.Open());
        moreButton.ContextMenu = menu;
        AutomationProperties.SetName(moreButton, $"Actions for profile {selected.Name}");
        var defaultRoute = cfg.Routes.FirstOrDefault(route => route.Id == selected.DefaultRouteId);
        var details = new StackPanel { Spacing = 8 };
        var title = Ui.Stack(selectionState, Ui.Text($"{selected.RouteIds.Length} models · Saved revision {cfg.Revision}", 12, Ui.Muted));
        title.Spacing = 3;
        var edit = Ui.Button("Edit selection", () => EditProfile(selected));
        edit.Tag = new EditorFocusTarget("Profiles", selected.Id, "detail-edit");
        var actions = ProfileWrap(apply, edit, moreButton);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10, RowSpacing = 6 };
        header.Children.Add(title); Grid.SetColumn(actions, 1); header.Children.Add(actions);
        header.SizeChanged += (_, _) =>
        {
            var narrow = header.Bounds.Width > 0 && header.Bounds.Width < 640;
            header.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "*,Auto");
            header.RowDefinitions = new RowDefinitions(narrow ? "Auto,Auto" : "Auto");
            Grid.SetColumn(actions, narrow ? 0 : 1); Grid.SetRow(actions, narrow ? 1 : 0);
        };
        details.Children.Add(header);
        var summary = Ui.Stack(Ui.Text("DEFAULT CLIENT MODEL", 11, Ui.Muted),
            Ui.Text(defaultRoute?.Alias ?? "No explicit default", 15),
            Ui.Text(selected.Policy?.CostPolicy switch
            {
                "free_only" => "Only free · no paid fallback",
                null or "configured" => "Configured access · route policies still apply",
                var policy => "Spending policy: " + policy
            }, 12, Ui.Muted));
        summary.Spacing = 3;
        var summaryCard = Ui.Card(summary); summaryCard.Padding = new Thickness(10);
        details.Children.Add(summaryCard);
        if (defaultRoute is { Enabled: false } or { Visible: false })
            details.Children.Add(Ui.Text("The saved default is disabled or hidden. Edit this profile or its model before applying.", 12, Ui.Warning));
        details.Children.Add(Ui.Text("MODEL VISIBILITY & ROUTING", 11, Ui.Muted));
        var routeCard = Ui.Card(routes); routeCard.Padding = new Thickness(0);
        details.Children.Add(routeCard);
        details.Children.Add(ProfileWrap(export, Ui.Button("Set up Codex client…", () => ShowCodexExport(selected))));
        var technical = Ui.Stack(Ui.Text($"Profile ID: {selected.Id} · Default ID: {selected.DefaultRouteId ?? "none"}", 11, Ui.Muted),
            Ui.Text("Apply updates the global proxy. Export and client installation are separate operations.", 12, Ui.Muted));
        if (selected.Policy?.FallbackRouteIds is { Length: > 0 } fallbacks)
            technical.Children.Add(Ui.Text("Saved fallback order: " + string.Join(" → ", fallbacks.Select(id => cfg.Routes.FirstOrDefault(r => r.Id == id)?.Alias ?? id)) +
                $" · Paid fallback permission: {(selected.Policy.AllowPaidFallback ? "allowed" : "not allowed")}. Saved fallbacks are not an active fallback runtime.", 12, Ui.Muted));
        var more = new Expander { Header = "Expert details & policy", Content = technical,
            IsExpanded = _profileDetails.GetValueOrDefault(selected.Id), HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(more, $"Details and export for profile {selected.Name}");
        more.PropertyChanged += (_, e) => { if (e.Property == Expander.IsExpandedProperty) _profileDetails[selected.Id] = more.IsExpanded; };
        details.Children.Add(more);
        details.ContextMenu = ObjectCommandMenu(commands);
        return details;
    }

    private void ConfirmProfileApplication(ProfileDto profile)
    {
        if (_configuration is null || _busy) return;
        var review = new ProfileReview(profile.Id, profile.Name) { AwaitingOutcome = ProfileApplyOutcomeUnknown };
        if (ProfileApplyOutcomeUnknown) review.Message = "A previous apply outcome is unknown. Check current status before preparing another apply.";
        _profileReview = review;
        _profileNarrowDetail = true;
        Render();
        Dispatcher.UIThread.Post(() => review.CancelButton?.Focus(), DispatcherPriority.Loaded);
        _ = LoadProfilePreviewAsync(review);
    }

    // Called by the shell after polling too: a focused review must become invalid
    // without replacing the mounted controls or removing its readable old diff.
    private void RefreshProfileReviewState()
    {
        _refreshProfileStatusUi?.Invoke();
        if (_profileReview is not { } review) return;
        if (review.AwaitingOutcome && !ProfileApplyOutcomeUnknown)
        {
            review.AwaitingOutcome = false;
            review.Message = "Current host status checked. " + (_status?.Application?.RecoveryAction ?? "Refresh preview before a new explicit apply.");
        }
        if (review.Preview is { } preview && (_configuration?.Revision != preview.Revision ||
            _configuration.Profiles.All(profile => profile.Id != preview.ProfileId)))
        {
            review.Stale = true;
            review.Message = "Saved configuration changed. This old preview is invalid. Refresh preview and confirm again.";
        }
        review.RefreshUi?.Invoke();
    }

    private Control ProfileApplyReview(ProfileReview review)
    {
        var title = Ui.Text("Review profile changes · " + review.ProfileName, 18);
        var message = Ui.Text(review.Message, 13, Ui.Warning);
        AutomationProperties.SetName(message, "Profile apply review message");
        var changes = new StackPanel { Spacing = 10 };
        if (review.Preview is { } preview && review.Snapshot is { } snapshot)
        {
            string Routes(string[] ids) => ids.Length == 0 ? "None" : string.Join(", ", ids.Select(id => snapshot.Routes.FirstOrDefault(route => route.Id == id)?.Alias ?? id));
            string Default(string? id) => id is null ? "No explicit default" : snapshot.Routes.FirstOrDefault(route => route.Id == id)?.Alias ?? id;
            var active = snapshot.Profiles.FirstOrDefault(profile => profile.Id == preview.ActiveProfileId)?.Name ?? preview.ActiveProfileId ?? "Unknown profile";
            changes.Children.Add(Ui.Text("GLOBAL · Shared proxy runtime for every connected client", 12, Ui.Warning));
            changes.Children.Add(Ui.Text($"{active} → {review.ProfileName} · Profile ID {preview.ProfileId}", 14));
            changes.Children.Add(Ui.Text($"Applied revision {preview.ActiveRevision?.ToString() ?? "unknown"} → Saved revision {preview.Revision}", 12, Ui.Muted));
            if (!preview.BaselineKnown) changes.Children.Add(Ui.Text("Applied baseline unknown. This is not a complete comparison with the running proxy.", 12, Ui.Warning));
            changes.Children.Add(Ui.Text("Added: " + Routes(preview.AddedRouteIds), 13));
            changes.Children.Add(Ui.Text("Changed: " + Routes(preview.ChangedRouteIds), 13));
            changes.Children.Add(Ui.Text("Removed: " + Routes(preview.RemovedRouteIds), 13));
            if (preview.ProfileChanged || preview.DefaultRouteId != preview.ActiveDefaultRouteId)
                changes.Children.Add(Ui.Text($"Default: {(preview.BaselineKnown ? Default(preview.ActiveDefaultRouteId) : "Unknown applied default")} → {Default(preview.DefaultRouteId)}", 13));
            changes.Children.Add(Ui.Text("Accounts: " + (preview.AffectedAccountIds.Length == 0 ? "None reported" : string.Join(", ", preview.AffectedAccountIds.Select(id => snapshot.Accounts.FirstOrDefault(account => account.Id == id)?.Name ?? id))), 12, Ui.Muted));
            changes.Children.Add(Ui.Text("Clients: " + string.Join(", ", preview.AffectedClients), 12, Ui.Muted));
            changes.Children.Add(Ui.Text(preview.StreamBehavior, 13, Ui.Warning));
            changes.Children.Add(Ui.Text("Includes all saved changes listed by the host. Client exports and installations remain separate.", 12, Ui.Muted));
        }
        var status = Ui.Text("", 12, Ui.Warning);
        AutomationProperties.SetName(status, "Profile apply confirmed status");
        var confirm = Ui.Button("Apply to proxy", () => _ = CommitProfileReviewAsync(review));
        confirm.Classes.Add("primary");
        var refresh = Ui.Button("Refresh preview", () => _ = LoadProfilePreviewAsync(review));
        var check = Ui.Button("Check current status", () => _ = CheckProfileStatusAsync(review));
        void CancelReview()
        {
            if (review.Committing) return;
            review.Request++;
            _profileReview = null;
            Render();
            Dispatcher.UIThread.Post(() => _profileApplyFocus?.Focus(), DispatcherPriority.Loaded);
        }
        var cancel = Ui.Button("Cancel review", CancelReview);
        review.CancelButton = cancel;
        review.RefreshUi = () =>
        {
            message.Text = review.Message;
            status.Text = (_configuration is { } cfg ? AppliedProfileDescription(cfg) : "Applied profile not reported") +
                (ProfileApplyOutcomeUnknown ? ". Check current status before another apply." : "");
            confirm.IsEnabled = _hostAvailable && !review.Working && !review.Stale && review.Preview is not null && !ProfileApplyOutcomeUnknown;
            refresh.IsEnabled = !review.Working && !ProfileApplyOutcomeUnknown;
            check.IsEnabled = !review.Working;
            cancel.IsEnabled = !review.Committing;
        };
        var scroll = new ScrollViewer { Content = changes, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetName(scroll, "Profile apply diff");
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"), RowSpacing = 10 };
        AutomationProperties.SetName(body, "Profile apply review");
        body.Children.Add(title);
        Grid.SetRow(message, 1); body.Children.Add(message);
        Grid.SetRow(status, 2); body.Children.Add(status);
        Grid.SetRow(scroll, 3); body.Children.Add(scroll);
        var actions = ProfileWrap(cancel, check, refresh, confirm);
        Grid.SetRow(actions, 4); body.Children.Add(actions);
        body.KeyDown += (_, e) => { if (e.Key == Key.Escape && !review.Committing) { e.Handled = true; CancelReview(); } };
        RefreshProfileReviewState();
        return body;
    }

    private void RenderProfileReview(ProfileReview review)
    {
        UpdateServiceSummary();
        if (ReferenceEquals(_profileReview, review) && _section == "Profiles" && !_editing)
        {
            Render();
            Dispatcher.UIThread.Post(() => { if (ReferenceEquals(_profileReview, review) && !review.Committing) review.CancelButton?.Focus(); }, DispatcherPriority.Loaded);
        }
    }

    private async Task LoadProfilePreviewAsync(ProfileReview review)
    {
        if (review.Working || ProfileApplyOutcomeUnknown || !ReferenceEquals(_profileReview, review)) return;
        review.Working = true;
        var request = ++review.Request;
        review.Message = "Loading current saved configuration…";
        review.RefreshUi?.Invoke();
        try
        {
            var current = await _client.GetConfigurationAsync(_lifetime.Token);
            if (!ReferenceEquals(_profileReview, review) || review.Request != request || _selectedProfileId != review.ProfileId) return;
            if (current.Profiles.All(profile => profile.Id != review.ProfileId)) throw new InvalidOperationException("This profile was removed. Cancel review and choose another profile.");
            var result = await _client.PreviewProfileAsync(review.ProfileId, current.Revision, _lifetime.Token);
            if (!ReferenceEquals(_profileReview, review) || review.Request != request || _selectedProfileId != review.ProfileId) return;
            if (result.Revision != current.Revision || result.ProfileId != review.ProfileId)
                throw new InvalidOperationException("Preview identity does not match the saved profile. Refresh to review again.");
            // A poll may already have observed a newer revision while preview was in flight.
            if (_configuration is null || _configuration.Revision <= current.Revision) _configuration = current;
            review.Snapshot = current;
            review.Preview = result;
            review.Stale = _configuration.Revision != result.Revision;
            review.Message = review.Stale ? "Saved configuration changed. This old preview is invalid. Refresh preview and confirm again." : "Review the saved changes before applying.";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { review.Stale = true; review.Message = "Preview unavailable. " + Friendly(error); }
        finally { review.Working = false; RenderProfileReview(review); }
    }

    private async Task CommitProfileReviewAsync(ProfileReview review)
    {
        RefreshProfileReviewState();
        if (review.Working || review.Stale || ProfileApplyOutcomeUnknown || !ReferenceEquals(_profileReview, review) || review.Preview is not { } preview) return;
        review.Working = true;
        review.Committing = true;
        _busy = true;
        var sent = false;
        review.Message = "Checking saved revision before applying…";
        review.RefreshUi?.Invoke();
        try
        {
            var current = await _client.GetConfigurationAsync(_lifetime.Token);
            _configuration = current;
            if (current.Revision != preview.Revision || current.Profiles.All(profile => profile.Id != preview.ProfileId))
            {
                review.Stale = true;
                review.Message = "Saved configuration changed. This old preview is invalid. Refresh preview and confirm again.";
                return;
            }
            _profileLastConfirmedStatus = _status;
            _profileUnconfirmedApplyId = preview.ProfileId;
            _profileUnconfirmedApplyRevision = preview.Revision;
            sent = true;
            review.Message = "Apply sent. Waiting for host confirmation; leaving this page does not roll it back.";
            RenderProfileReview(review);
            _configuration = await _client.ApplyProfileAsync(preview.Revision, preview.ProfileId, _lifetime.Token);
            await ReadProfileStatusAsync(review);
            if (!ProfileApplyOutcomeUnknown) review.Stale = true;
        }
        catch (LainApiException error) when (error.Code == "revision_conflict")
        {
            _profileUnconfirmedApplyId = null; _profileUnconfirmedApplyRevision = null;
            review.Stale = true;
            review.Message = "Apply rejected: saved configuration changed. The old preview remains below. Refresh preview and confirm again.";
        }
        catch (LainApiException error) when (sent && error.StatusCode == System.Net.HttpStatusCode.Conflict && error.Code == "runtime_busy")
        {
            // This exact host response guarantees drain refusal restored the old
            // runtime and its previous operation proof. No new operation is needed
            // to establish the rejection; transport failures do not share that guarantee.
            _status = _profileLastConfirmedStatus;
            _profileUnconfirmedApplyId = null; _profileUnconfirmedApplyRevision = null;
            review.Stale = true;
            review.Message = "Apply rejected: the runtime is busy. The previous runtime was preserved. Refresh preview and confirm again when streams have finished.";
        }
        catch (LainApiException error) when (sent && error.StatusCode == System.Net.HttpStatusCode.Conflict && error.Code == "runtime_recovery_required")
        {
            review.Stale = true;
            review.Message = "The host requires runtime recovery. " + Friendly(error) + " Check current status before preparing another explicit apply. No apply will be repeated automatically.";
        }
        catch (Exception error)
        {
            review.Stale = true;
            review.Message = (sent ? "Apply outcome unknown. " : "Apply was not sent. ") + Friendly(error) +
                (sent ? " Check current status; no automatic retry will be sent." : " Refresh preview before trying again.");
        }
        finally
        {
            review.Working = false; review.Committing = false; _busy = false;
            RenderProfileReview(review);
        }
    }

    private async Task ReadProfileStatusAsync(ProfileReview review)
    {
        var current = await _client.StatusAsync(_lifetime.Token);
        _status = current;
        _hostAvailable = true;
        if (ProfileApplyOutcomeUnknown)
        {
            var fresh = _profileLastConfirmedStatus is null || current.ObservedAt > _profileLastConfirmedStatus.ObservedAt;
            var application = current.Application;
            var completed = application is { State: "completed", OperationId: not null, ProfileId: not null, Revision: not null }
                && current.ActiveProfileId == application.ProfileId && current.ActiveProfileRevision == application.Revision
                && current.AppliedRevision == application.Revision
                && application.OperationId != _profileLastConfirmedStatus?.Application?.OperationId;
            var recovery = application is { State: "recovery-required" } && application.ProfileId == _profileUnconfirmedApplyId
                && application.Revision == _profileUnconfirmedApplyRevision;
            if (fresh && (completed || recovery))
            {
                review.AwaitingOutcome = false;
                _profileUnconfirmedApplyId = null; _profileUnconfirmedApplyRevision = null;
                review.Stale = true;
                review.Message = recovery ? "Host reports recovery required. " + application?.RecoveryAction + " Refresh preview before a new explicit apply."
                    : "Current host state confirmed. Refresh preview before any new apply.";
            }
            else review.Message = "Apply outcome remains unknown. The returned status does not establish a new completed operation. Check current status again; no apply was repeated.";
        }
        else review.Message = "Current host status read. " + current.Application?.RecoveryAction;
        // A lost apply response can leave only this metadata stale locally.
        // Keep every saved object, editor revision, selection and mounted draft;
        // the real status read supplies the confirmed applied revision alone.
        if (!ProfileApplyOutcomeUnknown && _configuration is { } configuration)
            _configuration = configuration with { AppliedRevision = current.AppliedRevision };
    }

    private async Task CheckProfileStatusAsync(ProfileReview review)
    {
        if (review.Working) return;
        review.Working = true; review.RefreshUi?.Invoke();
        try { await ReadProfileStatusAsync(review); }
        catch (Exception error) { review.Message = "Current status unavailable. " + Friendly(error) + (ProfileApplyOutcomeUnknown ? " Apply outcome remains unknown; last confirmed status is retained." : ""); }
        finally { review.Working = false; RenderProfileReview(review); }
    }

    private void ShowCodexExport(ProfileDto profile)
    {
        if (!CanOpenSectionEditor()) return;
        if (_configuration is not { } cfg) return;
        if (cfg.Profiles.FirstOrDefault(item => item.Id == profile.Id) is not { } current)
        {
            _notice.Text = "This profile no longer exists. Return to Profiles and select an existing profile.";
            return;
        }
        profile = current;
        RegisterEditor(() =>
        {
            var latest = _configuration?.Profiles.FirstOrDefault(item => item.Id == profile.Id)
                ?? throw new InvalidOperationException("This profile was removed. Cancel setup and review Profiles.");
            ShowCodexExport(latest);
        }, cfg.Revision, profile.Name + " · Codex setup");
        _editing = true;
        var launcher = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "lain");
        var executable = Ui.Input(File.Exists(launcher) ? launcher : "", "/absolute/path/to/lain");
        var folder = Ui.Input(hint: "Choose or enter an existing parent folder");
        var validation = Ui.Text("", 12, Ui.Warning);
        AutomationProperties.SetName(validation, "Client setup validation");
        Button? prepare = null;
        bool ValidInputs()
        {
            var executableError = ValidateCodexExecutable(executable.Text);
            var parentError = ValidateCodexParent(folder.Text);
            validation.Text = string.Join(" ", new[] { executableError, parentError }.Where(error => error is not null));
            if (prepare is not null) prepare.IsEnabled = !_busy && executableError is null && parentError is null;
            return executableError is null && parentError is null;
        }
        executable.TextChanged += (_, _) => ValidInputs();
        folder.TextChanged += (_, _) => ValidInputs();
        AutomationProperties.SetName(folder, "Destination parent folder");
        AutomationProperties.SetName(executable, "lain command-line executable");
        var pick = Ui.Button("Browse…", async () => {
            try
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose a parent folder for a new Codex connection", AllowMultiple = false });
                if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) folder.Text = path;
            }
            catch (Exception error) { validation.Text = Friendly(error); }
            finally { folder.Focus(); }
        });
        var destinationRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        destinationRow.Children.Add(folder);
        Grid.SetColumn(pick, 1);
        destinationRow.Children.Add(pick);
        Control? setup = null;
        prepare = Ui.Button("Prepare Codex files", () => _ = PrepareAsync());
        async Task PrepareAsync()
        {
            // Recheck at the action boundary too: the file or directory may have
            // disappeared since TextChanged last validated the retained inputs.
            if (_busy || !ValidInputs()) return;
            var executablePath = executable.Text!.Trim();
            var parentPath = folder.Text!.Trim();
            await RunAsync(async () =>
            {
                var destination = Path.Combine(parentPath, $"lain-codex-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}");
                var artifacts = await _client.ExportCodexProfileAsync(cfg.Revision, profile.Id, executablePath, Path.Combine(destination, "lain.models.json"), _lifetime.Token);
                if (artifacts.ProfileId != profile.Id || artifacts.Revision != cfg.Revision || string.IsNullOrWhiteSpace(artifacts.GenerationId))
                    throw new InvalidOperationException("The prepared artifact does not identify this saved profile and revision. Nothing was installed; prepare again.");
                var installationStarted = false;
                var install = Action("Install client files", async () =>
                {
                    if (!installationStarted && (Directory.Exists(destination) || File.Exists(destination)))
                        throw new InvalidOperationException("This destination already exists. Prepare a new private client folder before installing.");
                    installationStarted = true;
                    var installed = CodexArtifactInstaller.Install(artifacts, destination);
                    var installation = CodexArtifactInstaller.Inspect(destination, ProfileApplyOutcomeUnknown ? null : _status?.ActiveProfileId, ProfileApplyOutcomeUnknown ? null : _status?.ActiveProfileRevision);
                    if (installation.State != "installed") throw new InvalidOperationException(installation.RecoveryAction ?? "Client installation needs recovery.");
                    static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";
                    var command = $"CODEX_HOME={Quote(destination)} codex {string.Join(" ", installed.LaunchArguments.Select(Quote))}";
                    var copy = Ui.Button("Copy command", async () =>
                    {
                        try
                        {
                            if (Clipboard is not { } clipboard) throw new InvalidOperationException("Clipboard is unavailable. Select the command below.");
                            await clipboard.SetTextAsync(command); _notice.Text = "Launch command copied.";
                        }
                        catch (Exception error) { _notice.Text = Friendly(error); }
                    });
                    _page.Content = Ui.Stack(ProfileHeader("Codex files installed", $"{profile.Name} · Exported revision {installed.Revision}"),
                        Ui.Card(Ui.Stack(Ui.Text(installation.Stale switch
                        {
                            true => "This export differs from the last reported applied profile. Apply and client setup are separate operations.",
                            false => "Matches the last reported applied profile and revision.",
                            _ => "Applied profile identity is unknown; client/runtime alignment cannot be confirmed."
                        }, 13, installation.Stale == false ? Ui.Muted : Ui.Warning),
                            ProfileReadOnly(installed.ConfigurationPath), ProfileReadOnly(installed.CatalogPath),
                            Ui.Text("Installed generation: " + installed.GenerationId, 11, Ui.Muted))),
                        Ui.Card(Ui.Stack(ProfileHeader("Launch this connection", "Run the command in your terminal.", copy), ProfileReadOnly(command))),
                        Ui.Text("Separate client files were installed. The global proxy and ordinary Codex configuration were not changed.", 12, Ui.Muted),
                        Ui.Button("Back to profiles", () => { _editing = false; Render(); }));
                    await Task.CompletedTask;
                }, "Client files installed. Proxy unchanged.", stayOnPage: true);
                install.Classes.Add("primary");
                var preparedIdentity = Ui.Text("Profile: " + artifacts.ProfileId + " · Export generation: " + artifacts.GenerationId + " · Revision: " + artifacts.Revision, 12, Ui.Muted);
                AutomationProperties.SetName(preparedIdentity, "Prepared Codex artifact");
                var preparedStatus = Ui.Text("No files installed yet", 15);
                AutomationProperties.SetName(preparedStatus, "Codex setup status");
                _page.Content = Ui.Stack(ProfileHeader("Codex export prepared", $"{profile.Name} · Saved export revision {artifacts.Revision}"),
                    Ui.Card(Ui.Stack(preparedStatus, ProfileReadOnly(destination),
                        preparedIdentity,
                        Ui.Text("Install creates a separate private configuration with an immutable model catalog. This does not apply the profile.", 12, Ui.Muted))),
                    ProfileWrap(install, Ui.Button("Cancel", () => { _editing = false; Render(); })));
            }, "Codex export prepared. No files installed yet.", stayOnPage: true);
            if (ReferenceEquals(_page.Content, setup)) ValidInputs();
        }
        prepare.Classes.Add("primary");
        setup = Ui.Stack(ProfileHeader("Set up Codex client", $"{profile.Name}  ·  {profile.RouteIds.Length} routes  ·  Saved revision {cfg.Revision}"),
            Ui.Card(Ui.Stack(Ui.Text("1  Local authentication helper", 14), Ui.Field("lain command-line executable", executable),
                Ui.Text("Select the installed lain executable used by Codex to authenticate locally.", 12, Ui.Muted))),
            Ui.Card(Ui.Stack(Ui.Text("2  Export destination", 14), Ui.Field("Parent folder", destinationRow),
                Ui.Text("Prepare generates a saved-profile export without writing files. Review it, then choose Install client files to create a separate lain-codex-… folder. Neither action applies the profile.", 12, Ui.Muted))),
            validation,
            ProfileWrap(prepare, Ui.Button("Cancel", () => { _editing = false; Render(); })));
        _page.Content = setup;
        ValidInputs();
    }

    private static string? ValidateCodexExecutable(string? value)
    {
        var path = value?.Trim();
        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path))
            return "Choose an existing absolute path to the lain command-line executable.";
        try
        {
            if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) &
                (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0)
                return "The selected lain file is not executable. Choose an installed executable.";
            if (OperatingSystem.IsWindows() && !new[] { ".exe", ".com", ".cmd", ".bat" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                return "Choose a runnable lain command-line executable.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return "The selected executable cannot be inspected. Choose an accessible lain executable."; }
        return null;
    }

    private static string? ValidateCodexParent(string? value)
    {
        var path = value?.Trim();
        return string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path) || !Directory.Exists(path)
            ? "Choose an existing absolute destination parent folder. No folder or client files have been created."
            : null;
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

    private void ConfirmProfileRemoval(string profileId)
    {
        if (!CanOpenSectionEditor()) return;
        if (_configuration is not { } cfg) return;
        if (cfg.Profiles.FirstOrDefault(profile => profile.Id == profileId) is not { } selected)
        {
            _notice.Text = "This profile no longer exists. Return to Profiles and select an existing profile.";
            return;
        }
        RegisterEditor(() => ConfirmProfileRemoval(profileId), cfg.Revision, selected.Name + " · removal");
        ConfirmRemoval(selected.Name, async () =>
        {
            RequireCurrentProfileDraft(cfg.Revision, profileId);
            _configuration = await _client.RemoveProfileAsync(cfg.Revision, profileId, _lifetime.Token);
        });
    }

    private void RequireCurrentProfileDraft(long revision, string? profileId)
    {
        if (_configuration is not { } current)
            throw new InvalidOperationException("Saved configuration is unavailable. Your profile draft is kept; reload status before saving.");
        if (profileId is not null && current.Profiles.All(profile => profile.Id != profileId))
            throw new InvalidOperationException("This profile was removed. Your draft is kept; it cannot recreate the removed profile. Cancel this draft and review Profiles.");
        if (current.Revision != revision)
            throw new InvalidOperationException("The saved configuration changed. Your profile draft is kept. Review the latest configuration before saving again.");
    }

    private void EditProfile(ProfileDto? profile)
    {
        if (!CanOpenSectionEditor()) return;
        if (_configuration is not { } cfg) return;
        if (profile is not null)
        {
            if (cfg.Profiles.FirstOrDefault(item => item.Id == profile.Id) is not { } current)
            {
                _notice.Text = "This profile no longer exists. Return to Profiles and select an existing profile.";
                return;
            }
            profile = current;
            _selectedProfileId = current.Id;
        }
        var profileId = profile?.Id;
        var returnFocus = CaptureEditorFocus("Profiles", profileId, profileId is null ? "create" : "detail-edit", _profileEditorFocusTarget);
        _profileEditorFocusTarget = returnFocus;
        var savedId = profileId;
        RegisterEditor(() =>
        {
            if (profileId is null) { EditProfile(null); return; }
            var latest = _configuration?.Profiles.FirstOrDefault(item => item.Id == profileId)
                ?? throw new InvalidOperationException("This profile was removed from the latest configuration. Cancel this draft and review Profiles.");
            EditProfile(latest);
        }, cfg.Revision, profile?.Name ?? "New profile");
        _editing = true;
        var name = Ui.Input(profile?.Name, "Profile name");
        var search = Ui.Input(hint: "Search model access, providers or models");
        AutomationProperties.SetName(search, "Search profile model access");
        AutomationProperties.SetName(name, "Profile name");
        var spending = new SpendingPolicyEditor(profile?.Policy);
        var fallbackIds = profile?.Policy?.FallbackRouteIds ?? [];
        var fallbackPanel = new StackPanel { Spacing = 4 };
        var fallbackChoices = fallbackIds.Concat(cfg.Routes.Select(route => route.Id)).Distinct(StringComparer.Ordinal).Select(id =>
        {
            var route = cfg.Routes.FirstOrDefault(r => r.Id == id);
            var checkbox = new CheckBox { IsChecked = fallbackIds.Contains(id),
                Content = Ui.Text(route is null ? id + " · missing model" : route.Alias + (!route.Enabled ? " · disabled" : ""), 12),
                HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(checkbox, "Profile fallback " + id);
            fallbackPanel.Children.Add(checkbox);
            return (Id: id, Box: checkbox);
        }).ToArray();
        var fallbackOrder = fallbackChoices.ToList();
        void RenderFallbackOrder()
        {
            foreach (var oldRow in fallbackPanel.Children.OfType<Grid>()) oldRow.Children.Clear();
            fallbackPanel.Children.Clear();
            foreach (var choice in fallbackOrder)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 4 };
                row.Children.Add(choice.Box);
                void Move(int delta)
                {
                    var index = fallbackOrder.FindIndex(item => item.Id == choice.Id);
                    var next = index + delta;
                    if (next < 0 || next >= fallbackOrder.Count) return;
                    fallbackOrder.RemoveAt(index); fallbackOrder.Insert(next, choice);
                    RenderFallbackOrder(); choice.Box.Focus();
                }
                var up = Ui.Button("↑", () => Move(-1));
                var down = Ui.Button("↓", () => Move(1));
                AutomationProperties.SetName(up, "Move profile fallback " + choice.Id + " up");
                AutomationProperties.SetName(down, "Move profile fallback " + choice.Id + " down");
                Grid.SetColumn(up, 1); row.Children.Add(up); Grid.SetColumn(down, 2); row.Children.Add(down);
                fallbackPanel.Children.Add(row);
            }
        }
        RenderFallbackOrder();
        var allowPaid = new CheckBox { Content = "Allow paid fallback", IsChecked = profile?.Policy?.AllowPaidFallback ?? false };
        AutomationProperties.SetName(allowPaid, "Allow paid profile fallback");
        var fallbackEditor = new Expander { Header = "Fallback selections & permission", HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = Ui.Stack(Ui.Text("Saved fallback order follows the list below. Fallback execution is not supported by the current runtime; Apply rejects fallback graphs.", 12, Ui.Warning),
                new ScrollViewer { Content = fallbackPanel, MaxHeight = 180, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, allowPaid) };
        AutomationProperties.SetName(fallbackEditor, "Profile fallback policy");
        RoutingPolicyDto? ResolvePolicy()
        {
            var policy = spending.Resolve();
            var fallbacks = fallbackOrder.Where(choice => choice.Box.IsChecked == true).Select(choice => choice.Id).ToArray();
            var paid = allowPaid.IsChecked == true;
            if (spending.Code == "free_only" && paid) throw new InvalidOperationException("Free-only cannot allow paid fallback. Clear that permission before saving.");
            if (policy is null && fallbacks.Length == 0 && !paid) return null;
            return (policy ?? new RoutingPolicyDto()) with { FallbackRouteIds = fallbacks.Length == 0 ? null : fallbacks, AllowPaidFallback = paid };
        }
        var policyStatus = Ui.Text("Check saved routes before choosing strict free-only access. Profile fallbacks are validated on save and apply.", 12, Ui.Muted);
        var count = Ui.Text("", 12, Ui.Muted);
        var empty = Ui.Text("No model access entries match this search.", 13, Ui.Muted);
        var defaults = new ComboBox { ItemsSource = new[] { "None" }.Concat(cfg.Routes.Select(r => r.Alias)).ToArray(), SelectedIndex = profile?.DefaultRouteId is { } id ? Array.FindIndex(cfg.Routes, r => r.Id == id) + 1 : 0 };
        AutomationProperties.SetName(defaults, "Default client name");
        var defaultHint = Ui.Text("", 12, Ui.Muted);
        var routeList = new StackPanel { Spacing = 0 };
        var choices = cfg.Routes.Select(route => {
            var box = new CheckBox { IsChecked = profile?.RouteIds.Contains(route.Id) ?? false,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = ProfileRouteContent(route.Alias, $"{route.Provider} / {route.UpstreamModel}", false), MinHeight = 42 };
            AutomationProperties.SetName(box, $"Include model access {route.Id}");
            AutomationProperties.SetHelpText(box, $"{route.Alias}, {route.Provider}, {route.UpstreamModel}");
            var row = new Border { BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 0), Child = box };
            routeList.Children.Add(row);
            return (Route: route, Box: box, Row: row);
        }).ToArray();
        void UpdateSelection()
        {
            count.Text = $"{choices.Count(x => x.Box.IsChecked == true)} of {choices.Length} selected  ·  {choices.Count(x => x.Row.IsVisible)} shown";
            var defaultId = defaults.SelectedIndex > 0 ? cfg.Routes[defaults.SelectedIndex - 1].Id : null;
            defaultHint.Text = defaultId is null ? "No default model. Clients can choose any selected client name."
                : !choices.Any(x => x.Route.Id == defaultId && x.Box.IsChecked == true) ? "Include the default model access in your selection before saving."
                : cfg.Routes.Any(route => route.Id == defaultId && (!route.Enabled || !route.Visible)) ? "The default is disabled or hidden. Apply is unavailable until it is enabled and visible."
                : "Default model access is included in the selection.";
        }
        foreach (var choice in choices) choice.Box.IsCheckedChanged += (_, _) => UpdateSelection();
        defaults.SelectionChanged += (_, _) => UpdateSelection();
        search.TextChanged += (_, _) => {
            var query = search.Text?.Trim() ?? "";
            foreach (var choice in choices)
                choice.Row.IsVisible = $"{choice.Route.Alias} {choice.Route.Provider} {choice.Route.UpstreamModel}".Contains(query, StringComparison.OrdinalIgnoreCase);
            empty.IsVisible = !choices.Any(x => x.Row.IsVisible);
            UpdateSelection();
        };
        empty.IsVisible = choices.Length == 0;
        if (choices.Length == 0) empty.Text = "No model access available. Add model access before creating a profile.";
        routeList.Children.Add(empty);
        UpdateSelection();
        var save = Action("Save profile", async () => {
            RequireCurrentProfileDraft(cfg.Revision, profileId);
            if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException("Enter a profile name.");
            var ids = choices.Where(x => x.Box.IsChecked == true).Select(x => x.Route.Id).ToArray();
            var defaultId = defaults.SelectedIndex > 0 ? cfg.Routes[defaults.SelectedIndex - 1].Id : null;
            if (ids.Length == 0) throw new InvalidOperationException("Select at least one model access entry.");
            if (defaultId is not null && !ids.Contains(defaultId)) throw new InvalidOperationException("Select the default model access in this profile too.");
            var savedProfileId = profile?.Id ?? Guid.NewGuid().ToString("N");
            if (profile is not null && profile.RouteIds.Length == ids.Length && profile.RouteIds.All(id => ids.Contains(id, StringComparer.Ordinal))) ids = profile.RouteIds;
            var original = profile ?? new ProfileDto(savedProfileId, "", []);
            _configuration = await _client.SaveProfileAsync(cfg.Revision,
                original with { Name = name.Text.Trim(), RouteIds = ids, DefaultRouteId = defaultId, Policy = ResolvePolicy() }, _lifetime.Token);
            _selectedProfileId = savedProfileId;
            savedId = savedProfileId;
            if (profileId is null) _profileNarrowDetail = true;
        }, "Profile saved.", afterSuccess: () => RestoreEditorFocus(profileId is null
            ? new("Profiles", savedId, "detail-edit") : returnFocus));
        save.Classes.Add("primary");
        var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 14, RowSpacing = 10 };
        fields.Children.Add(Ui.Field("Profile name", name));
        var defaultField = Ui.Field("Default client name", defaults);
        Grid.SetColumn(defaultField, 1);
        fields.Children.Add(defaultField);
        fields.SizeChanged += (_, _) =>
        {
            var narrow = fields.Bounds.Width > 0 && fields.Bounds.Width < 560;
            fields.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "*,*");
            fields.RowDefinitions = new RowDefinitions(narrow ? "Auto,Auto" : "Auto");
            Grid.SetColumn(defaultField, narrow ? 0 : 1); Grid.SetRow(defaultField, narrow ? 1 : 0);
        };
        var selection = Ui.Stack(search, ProfileHeader("Model access", "Choose the configured entries this profile offers."),
            ProfileWrap(Ui.Button("Select shown", () => { foreach (var x in choices.Where(x => x.Row.IsVisible)) x.Box.IsChecked = true; }),
                Ui.Button("Clear shown", () => { foreach (var x in choices.Where(x => x.Row.IsVisible)) x.Box.IsChecked = false; })), count,
            new ScrollViewer { MaxHeight = 300, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = routeList });
        selection.Spacing = 8;
        _page.Content = Ui.Stack(ProfileHeader(profile is null ? "Create profile" : "Edit profile", $"Choose a model menu and its default client name. Saved revision {cfg.Revision}."),
            Ui.Card(Ui.Stack(fields, defaultHint, spending.Control, fallbackEditor,
                Ui.Text("A profile can make routes stricter. Configured access never weakens a route's existing free-only rule.", 11, Ui.Muted),
                SpendingPolicyCheck(spending, () => choices.Where(item => item.Box.IsChecked == true).Select(item => item.Route.Id).ToArray(), policyStatus), policyStatus)), Ui.Card(selection),
            ProfileWrap(save, Ui.Button("Cancel", () => { _editing = false; Render(); RestoreEditorFocus(returnFocus); })));
    }

    private static Control ProfileHeader(string title, string description, Control? action = null)
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        header.Children.Add(new StackPanel { Spacing = 4, Children = { Ui.Text(title, 20), Ui.Text(description, 12, Ui.Muted) } });
        if (action is not null) { action.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(action, 1); header.Children.Add(action); }
        return header;
    }

    private static Control ProfileRouteContent(string alias, string model, bool isDefault) => new StackPanel
    {
        Spacing = 2, Children = { Ui.Text(alias + (isDefault ? "  ·  Default" : ""), 13), Ui.Text(model, 12, Ui.Muted) }
    };

    private static Control ProfileWrap(params Control[] controls)
    {
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var control in controls) { control.Margin = new Thickness(0, 0, 8, 6); wrap.Children.Add(control); }
        return wrap;
    }

    private static TextBox ProfileReadOnly(string text) => new()
    {
        Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 13
    };
}
