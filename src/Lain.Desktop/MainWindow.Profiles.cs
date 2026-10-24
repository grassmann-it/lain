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
    private bool _profileDialogOpen;
    private Button? _profileApplyFocus;

    private Control ProfilesPage()
    {
        var presentation = PresentationFor("Profiles");
        var create = Ui.IconButton("add", "Create profile", () => EditProfile(null));
        if (_configuration is not { } cfg)
            return Ui.Stack(ProfileHeader("Profiles", "Saved model menus.", create), Empty("Connect to the host to manage profiles."));
        var status = Ui.Text(AppliedProfileDescription(cfg), 12, Ui.Muted);
        AutomationProperties.SetName(status, "Applied proxy profile status");
        var summary = Ui.Stack(status);
        summary.Spacing = 3;
        if (cfg.Revision != cfg.AppliedRevision)
            summary.Children.Add(Ui.Text("Saved changes pending · Viewing and saving do not change the proxy.", 12, Ui.Warning));
        if (_status?.Application is { State: "recovery-required" } recovery)
            summary.Children.Add(Ui.Text("Apply needs recovery. " + recovery.RecoveryAction, 12, Ui.Warning));
        else if (_status?.Application is { State: "draining" or "publishing" } operation)
            summary.Children.Add(Ui.Text("Apply in progress: " + operation.State + ". All clients share this runtime.", 12, Ui.Warning));
        else if (_status?.Application is { State: "legacy-preserved" })
            summary.Children.Add(Ui.Text("Existing runtime preserved. Its profile identity may be unknown.", 12, Ui.Muted));
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
        var detailActions = new List<Button>();
        var back = Ui.Button("Back to profiles", () => { });
        back.IsVisible = false;
        var detail = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 6 };
        detail.Children.Add(back); Grid.SetRow(detailScroll, 1); detail.Children.Add(detailScroll);
        var inventory = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 8 };
        inventory.Children.Add(ProfileHeader("Profiles", "Select to view", create));
        Grid.SetRow(search, 1); inventory.Children.Add(search);
        Grid.SetRow(listScroll, 2); inventory.Children.Add(listScroll);
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*"), ColumnSpacing = 14 };
        layout.Children.Add(inventory); Grid.SetColumn(detail, 1); layout.Children.Add(detail);
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 10 };
        root.Children.Add(summary); Grid.SetRow(layout, 1); root.Children.Add(layout);
        bool Narrow() => root.Bounds.Width > 0 && root.Bounds.Width < 760;
        void Arrange()
        {
            var narrow = Narrow();
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
            if (_busy) return;
            if (openDetail && _selectedProfileId is { } previous) _profileDetailOffsets[previous] = detailScroll.Offset;
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
            detailHost.Content = ProfileDetails(cfg, profile, detailActions, presentation);
            Arrange();
            var restore = _profileDetailOffsets.GetValueOrDefault(profile.Id);
            Dispatcher.UIThread.Post(() => { detailScroll.Offset = restore; if (openDetail && Narrow()) back.Focus(); }, DispatcherPriority.Loaded);
        }
        foreach (var profile in cfg.Profiles)
        {
            var button = Ui.Button("", () => SelectProfile(profile));
            var label = new StackPanel { Spacing = 3, Children = { Ui.Text(profile.Name, 14), Ui.Text($"{profile.RouteIds.Length} models", 12, Ui.Muted) } };
            if (_status?.ActiveProfileId == profile.Id) label.Children.Add(Ui.Text("Applied · revision " + (_status.ActiveProfileRevision?.ToString() ?? "unknown"), 11, Ui.Accent));
            button.Content = label;
            AutomationProperties.SetName(button, $"Select profile {profile.Name}");
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.Padding = new Thickness(10, 8);
            selectors.Add(profile.Id, button); list.Children.Add(button);
        }
        var noMatches = Ui.Text("No matching profiles.", 12, Ui.Muted);
        list.Children.Add(noMatches);
        void Filter()
        {
            _profileSearch = search.Text ?? "";
            foreach (var profile in cfg.Profiles) selectors[profile.Id].IsVisible = profile.Name.Contains(_profileSearch.Trim(), StringComparison.OrdinalIgnoreCase);
            noMatches.IsVisible = !selectors.Values.Any(button => button.IsVisible);
        }
        search.TextChanged += (_, _) => Filter();
        root.SizeChanged += (_, _) => Arrange();
        Filter(); SelectProfile(selected, false);
        var inventoryOffset = _profileListOffset;
        Dispatcher.UIThread.Post(() => { listScroll.Offset = inventoryOffset; Arrange(); }, DispatcherPriority.Loaded);
        return Ui.Viewport(root);
    }

    private string AppliedProfileDescription(ConfigurationDto cfg)
    {
        var lastReport = _hostAvailable ? "Applied to proxy" : "Last host report: applied to proxy";
        var configuration = $"Configuration: saved {cfg.Revision} / proxy {cfg.AppliedRevision}";
        if (_status?.ActiveProfileId is not { } id) return $"Applied profile not reported by the host · {configuration}";
        var name = cfg.Profiles.FirstOrDefault(p => p.Id == id)?.Name ?? id;
        var revision = _status?.ActiveProfileRevision is { } value ? value.ToString() : "unknown";
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
            open.Padding = new Thickness(10, 8);
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
            new("remove", "Remove profile…", () => ConfirmRemoval(selected.Name, async () => _configuration = await _client.RemoveProfileAsync(cfg.Revision, selected.Id, _lifetime.Token)))
        ];
        var menu = ObjectCommandMenu(commands);
        var moreButton = Ui.Button("⋯", () => menu.Open());
        moreButton.ContextMenu = menu;
        AutomationProperties.SetName(moreButton, $"Actions for profile {selected.Name}");
        var defaultRoute = cfg.Routes.FirstOrDefault(route => route.Id == selected.DefaultRouteId);
        var details = new StackPanel { Spacing = 10 };
        details.Children.Add(selectionState);
        details.Children.Add(ProfileWrap(apply, Ui.Button("Edit selection", () => EditProfile(selected)), moreButton));
        details.Children.Add(Ui.Text($"{selected.RouteIds.Length} models · Saved revision {cfg.Revision}", 12, Ui.Muted));
        details.Children.Add(Ui.Card(Ui.Stack(Ui.Text("DEFAULT CLIENT MODEL", 11, Ui.Muted),
            Ui.Text(defaultRoute?.Alias ?? "No explicit default", 15),
            Ui.Text(selected.Policy?.CostPolicy switch
            {
                "free_only" => "Only free · no paid fallback",
                null or "configured" => "Configured access · route policies still apply",
                var policy => "Spending policy: " + policy
            }, 12, Ui.Muted))));
        if (defaultRoute is { Enabled: false } or { Visible: false })
            details.Children.Add(Ui.Text("The saved default is disabled or hidden. Edit this profile or its model before applying.", 12, Ui.Warning));
        details.Children.Add(Ui.Text("MODEL VISIBILITY & ROUTING", 11, Ui.Muted));
        details.Children.Add(Ui.Card(routes));
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

    private async void ConfirmProfileApplication(ProfileDto profile)
    {
        if (_configuration is null || _busy || _profileDialogOpen) return;
        _profileDialogOpen = true;
        var wasEditing = _editing;
        _editing = true;
        var origin = FocusManager?.GetFocusedElement() as Control;
        var ownerContent = _page.Content;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var dialog = new Window
        {
            Title = "Review profile changes", Width = Math.Min(620, Math.Max(360, Bounds.Width - 48)),
            Height = Math.Min(660, Math.Max(320, Bounds.Height - 64)), MinWidth = 320, MinHeight = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = true,
            Background = Ui.Background, RequestedThemeVariant = RequestedThemeVariant
        };
        var message = Ui.Text("Loading current saved configuration…", 13, Ui.Muted);
        var changes = new StackPanel { Spacing = 10 };
        var confirm = Ui.Button("Apply to proxy", () => { });
        confirm.Classes.Add("primary"); confirm.IsEnabled = false;
        var cancel = Ui.Button("Cancel", () => dialog.Close());
        var refresh = Ui.Button("Refresh preview", () => { });
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), RowSpacing = 10, Margin = new Thickness(18) };
        body.Children.Add(Ui.Text("Review profile changes", 20));
        Grid.SetRow(message, 1); body.Children.Add(message);
        var scroll = new ScrollViewer { Content = changes, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 2); body.Children.Add(scroll);
        var actions = ProfileWrap(cancel, refresh, confirm);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetRow(actions, 3); body.Children.Add(actions);
        KeyboardNavigation.SetTabNavigation(body, KeyboardNavigationMode.Cycle);
        dialog.Content = body;
        ProfileApplyPreviewDto? preview = null;
        var working = false;
        var applying = false;
        var confirmed = false;
        void Busy(bool value) { working = value; refresh.IsEnabled = !value; confirm.IsEnabled = !value && preview is not null; cancel.IsEnabled = !applying; }
        async Task LoadPreview(string? reason = null)
        {
            if (working) return;
            preview = null; Busy(true);
            message.Text = reason ?? "Loading current saved configuration…";
            try
            {
                var current = await _client.GetConfigurationAsync(lifetime.Token);
                var selected = current.Profiles.FirstOrDefault(p => p.Id == profile.Id)
                    ?? throw new InvalidOperationException("This profile was removed. Close this review and choose another profile.");
                var result = await _client.PreviewProfileAsync(selected.Id, current.Revision, lifetime.Token);
                if (result.Revision != current.Revision || result.ProfileId != selected.Id)
                    throw new InvalidOperationException("Preview identity does not match the saved profile. Refresh to review again.");
                _configuration = current;
                preview = result;
                changes.Children.Clear();
                string Routes(string[] ids) => ids.Length == 0 ? "None" : string.Join(", ", ids.Select(id => current.Routes.FirstOrDefault(r => r.Id == id)?.Alias ?? id));
                string Default(string? id) => id is null ? "No explicit default" : current.Routes.FirstOrDefault(r => r.Id == id)?.Alias ?? id;
                var active = current.Profiles.FirstOrDefault(p => p.Id == result.ActiveProfileId)?.Name ?? result.ActiveProfileId ?? "Unknown profile";
                changes.Children.Add(Ui.Text("GLOBAL · Shared proxy runtime for every connected client", 12, Ui.Warning));
                changes.Children.Add(Ui.Card(Ui.Stack(Ui.Text($"{active}  →  {selected.Name}", 17),
                    Ui.Text($"Applied revision {result.ActiveRevision?.ToString() ?? "unknown"} → Saved revision {result.Revision}", 12, Ui.Muted))));
                if (!result.BaselineKnown) changes.Children.Add(Ui.Text("Applied baseline unknown. This is not a complete comparison with the running proxy.", 12, Ui.Warning));
                changes.Children.Add(Ui.Text("Added: " + Routes(result.AddedRouteIds), 13));
                changes.Children.Add(Ui.Text("Changed: " + Routes(result.ChangedRouteIds), 13));
                changes.Children.Add(Ui.Text("Removed: " + Routes(result.RemovedRouteIds), 13));
                if (result.ProfileChanged || result.DefaultRouteId != result.ActiveDefaultRouteId)
                    changes.Children.Add(Ui.Text($"Default: {Default(result.ActiveDefaultRouteId)} → {Default(result.DefaultRouteId)}", 13));
                changes.Children.Add(Ui.Text("Accounts: " + (result.AffectedAccountIds.Length == 0 ? "None reported" : string.Join(", ", result.AffectedAccountIds.Select(id => current.Accounts.FirstOrDefault(a => a.Id == id)?.Name ?? id))), 12, Ui.Muted));
                changes.Children.Add(Ui.Text("Clients: " + string.Join(", ", result.AffectedClients), 12, Ui.Muted));
                changes.Children.Add(Ui.Card(Ui.Text(result.StreamBehavior, 13, Ui.Warning)));
                changes.Children.Add(Ui.Text("Includes all saved changes listed by the host. Client exports and installations remain separate.", 12, Ui.Muted));
                message.Text = reason ?? "Review the saved changes before applying.";
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception error) { preview = null; message.Text = "Preview unavailable. " + Friendly(error); }
            finally { Busy(false); }
        }
        refresh.Click += async (_, _) => await LoadPreview();
        confirm.Click += async (_, _) =>
        {
            if (working || preview is not { } reviewed) return;
            applying = true; Busy(true); _busy = true;
            var sent = false;
            var refreshRequired = false;
            try
            {
                var current = await _client.GetConfigurationAsync(lifetime.Token);
                _configuration = current;
                if (current.Revision != reviewed.Revision)
                {
                    refreshRequired = true;
                    return;
                }
                sent = true;
                _configuration = await _client.ApplyProfileAsync(reviewed.Revision, profile.Id, lifetime.Token);
                _status = await _client.StatusAsync(lifetime.Token);
                if (_status.ActiveProfileId != profile.Id || _status.ActiveProfileRevision != reviewed.Revision)
                    throw new InvalidOperationException("The host has not confirmed this profile as active. Refresh the preview to inspect its current state.");
                confirmed = true;
                _notice.Text = $"Applied {profile.Name} · revision {reviewed.Revision}. Client files are unchanged.";
            }
            catch (LainApiException error) when (error.Code == "revision_conflict") { refreshRequired = true; }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception error)
            {
                preview = null;
                message.Text = (sent ? "Apply is not confirmed. " : "Apply unavailable. ") + Friendly(error) + " Refresh preview before retrying.";
                try
                {
                    _status = await _client.StatusAsync(lifetime.Token);
                    if (_status.Application?.RecoveryAction is { } recovery) message.Text += " " + recovery;
                }
                catch { /* The failed status read is not evidence of a successful apply. */ }
            }
            finally
            {
                applying = false; _busy = false; Busy(false);
                if (confirmed) dialog.Close();
                else if (refreshRequired && !lifetime.IsCancellationRequested)
                    await LoadPreview("Saved configuration changed. Review this refreshed preview, then confirm again.");
            }
        };
        dialog.Closing += (_, e) => { if (applying && !confirmed) e.Cancel = true; };
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape && !applying) { e.Handled = true; dialog.Close(); } };
        dialog.Opened += async (_, _) => { cancel.Focus(); await LoadPreview(); };
        dialog.Closed += (_, _) => lifetime.Cancel();
        try { await dialog.ShowDialog(this); }
        finally
        {
            _profileDialogOpen = false; _editing = wasEditing;
            // Keep the mounted context and its scroll anchors; polling may refresh it after focus leaves.
            if (confirmed && !wasEditing)
            {
                Render();
                Dispatcher.UIThread.Post(() => _profileApplyFocus?.Focus(), DispatcherPriority.Loaded);
            }
            else if (ReferenceEquals(_page.Content, ownerContent)) origin?.Focus();
        }
    }

    private void ShowCodexExport(ProfileDto profile)
    {
        if (_configuration is not { } cfg) return;
        _editing = true;
        var launcher = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "lain");
        var executable = Ui.Input(File.Exists(launcher) ? launcher : "", "/absolute/path/to/lain");
        var folder = Ui.Input(hint: "Choose or enter an existing parent folder");
        AutomationProperties.SetName(folder, "Destination parent folder");
        AutomationProperties.SetName(executable, "lain command-line executable");
        var pick = Ui.Button("Browse…", async () => {
            try
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose a parent folder for a new Codex connection", AllowMultiple = false });
                if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) folder.Text = path;
            }
            catch (Exception error) { _notice.Text = Friendly(error); }
        });
        var destinationRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        destinationRow.Children.Add(folder);
        Grid.SetColumn(pick, 1);
        destinationRow.Children.Add(pick);
        var export = Action("Prepare Codex files", async () => {
            if (string.IsNullOrWhiteSpace(executable.Text) || !Path.IsPathFullyQualified(executable.Text) || !File.Exists(executable.Text)) throw new InvalidOperationException("Choose an existing absolute path to the lain command-line executable.");
            if (string.IsNullOrWhiteSpace(folder.Text) || !Directory.Exists(folder.Text)) throw new InvalidOperationException("Choose an existing local destination folder.");
            var destination = Path.Combine(folder.Text, $"lain-codex-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}");
            var artifacts = await _client.ExportCodexProfileAsync(cfg.Revision, profile.Id, executable.Text.Trim(), Path.Combine(destination, "lain.models.json"), _lifetime.Token);
            var install = Action("Install client files", async () =>
            {
                var installed = CodexArtifactInstaller.Install(artifacts, destination);
                var installation = CodexArtifactInstaller.Inspect(destination, _status?.ActiveProfileId, _status?.ActiveProfileRevision);
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
            _page.Content = Ui.Stack(ProfileHeader("Codex export prepared", $"{profile.Name} · Saved export revision {artifacts.Revision}"),
                Ui.Card(Ui.Stack(Ui.Text("No files installed yet", 15), ProfileReadOnly(destination),
                    Ui.Text("Profile: " + (artifacts.ProfileId ?? "not reported") + " · Export generation: " + (artifacts.GenerationId ?? "not reported"), 12, Ui.Muted),
                    Ui.Text("Install creates a separate private configuration with an immutable model catalog. This does not apply the profile.", 12, Ui.Muted))),
                ProfileWrap(install, Ui.Button("Cancel", () => { _editing = false; Render(); })));
        }, "Codex export prepared. No files installed yet.", stayOnPage: true);
        export.Classes.Add("primary");
        _page.Content = Ui.Stack(ProfileHeader("Set up Codex client", $"{profile.Name}  ·  {profile.RouteIds.Length} routes  ·  Saved revision {cfg.Revision}"),
            Ui.Card(Ui.Stack(Ui.Text("1  Local authentication helper", 14), Ui.Field("lain command-line executable", executable),
                Ui.Text("Select the installed lain executable used by Codex to authenticate locally.", 12, Ui.Muted))),
            Ui.Card(Ui.Stack(Ui.Text("2  Export destination", 14), Ui.Field("Parent folder", destinationRow),
                Ui.Text("Prepare generates a saved-profile export without writing files. Review it, then choose Install client files to create a separate lain-codex-… folder. Neither action applies the profile.", 12, Ui.Muted))),
            ProfileWrap(export, Ui.Button("Cancel", () => { _editing = false; Render(); })));
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
        var profileId = profile?.Id;
        RegisterEditor(() =>
        {
            if (profileId is null) { EditProfile(null); return; }
            var latest = _configuration?.Profiles.FirstOrDefault(item => item.Id == profileId)
                ?? throw new InvalidOperationException("This profile was removed from the latest configuration. Cancel this draft and review Profiles.");
            EditProfile(latest);
        }, cfg.Revision);
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
        }, "Profile saved.");
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
            ProfileWrap(save, Ui.Button("Cancel", () => { _editing = false; Render(); })));
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
