using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
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

    private Control ProfilesPage()
    {
        var presentation = PresentationFor("Profiles");
        var dense = presentation.Density == ViewDensity.Compact;
        var create = Ui.Button("Create profile", () => EditProfile(null));
        create.Classes.Add("primary");
        var panel = Ui.Stack(ProfileHeader("Profiles", presentation.Level == InformationLevel.Barebones ? "Saved model menus. Select to view; apply to change the proxy." : "Choose a saved model menu, apply it to the global proxy, or set up a client.", create));
        if (_configuration is not { } cfg) { panel.Children.Add(Empty("Connect to the host to manage profiles.")); return panel; }
        var applied = Ui.Text(AppliedProfileDescription(cfg), 12, Ui.Muted);
        AutomationProperties.SetName(applied, "Applied proxy profile status");
        panel.Children.Add(applied);
        panel.Children.Add(Ui.Text($"{cfg.Profiles.Length} saved profiles · Saved configuration {cfg.Revision} · Proxy configuration {cfg.AppliedRevision}", 12, Ui.Muted));
        if (cfg.Revision != cfg.AppliedRevision)
            panel.Children.Add(Ui.Text($"Saved configuration {cfg.Revision} is pending; the proxy uses configuration {cfg.AppliedRevision}.", 12, Ui.Warning));
        if (_status?.ActiveProfileRevision is { } activeRevision && activeRevision != cfg.Revision)
            panel.Children.Add(Ui.Text($"The saved configuration differs from applied profile revision {activeRevision}. Selecting or editing a profile does not apply those changes.", 12, Ui.Warning));
        if (cfg.Profiles.Length == 0)
        {
            panel.Children.Add(Empty("No saved profiles yet. Create a profile to choose its model access entries and default model."));
            return panel;
        }
        var selected = cfg.Profiles.FirstOrDefault(p => p.Id == _selectedProfileId) ?? cfg.Profiles[0];
        _selectedProfileId = selected.Id;
        var list = new StackPanel { Spacing = dense ? 2 : 4 };
        var detailHost = new ContentControl();
        var selectors = new Dictionary<string, Button>();
        var detailActions = new List<Button>();
        void SelectProfile(ProfileDto profile)
        {
            if (_busy) return;
            _selectedProfileId = profile.Id;
            foreach (var (id, selector) in selectors)
            {
                selector.Background = id == profile.Id ? Ui.AccentSurface : Brushes.Transparent;
                selector.BorderBrush = id == profile.Id ? Ui.Accent : Brushes.Transparent;
                AutomationProperties.SetHelpText(selector, id == profile.Id ? "Selected for viewing; selection does not change the proxy" : "Select this saved profile for viewing");
            }
            foreach (var action in detailActions) _actions.Remove(action);
            detailActions.Clear();
            detailHost.Content = ProfileDetails(cfg, profile, detailActions, presentation);
        }
        foreach (var profile in cfg.Profiles)
        {
            var button = Ui.Button("", () => SelectProfile(profile));
            var label = new StackPanel { Spacing = dense ? 1 : 3, Children = { Ui.Text(profile.Name, 13), Ui.Text($"{profile.RouteIds.Length} model access entries", 12, Ui.Muted) } };
            if (_status?.ActiveProfileId == profile.Id) label.Children.Add(Ui.Text("Applied to proxy", 11, Ui.Accent));
            button.Content = label;
            AutomationProperties.SetName(button, $"Select profile {profile.Name}");
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.Padding = new Thickness(dense ? 8 : 10, dense ? 5 : 8);
            selectors.Add(profile.Id, button);
            list.Children.Add(button);
        }
        SelectProfile(selected);
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("190,*"), ColumnSpacing = dense ? 8 : 14 };
        var inventory = Ui.Card(new ScrollViewer { MaxHeight = 470, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = list });
        inventory.Padding = new Thickness(dense ? 6 : 10);
        layout.Children.Add(inventory);
        Grid.SetColumn(detailHost, 1);
        layout.Children.Add(detailHost);
        panel.Children.Add(layout);
        return panel;
    }

    private string AppliedProfileDescription(ConfigurationDto cfg)
    {
        var lastReport = _hostAvailable ? "Applied to proxy" : "Last host report: applied to proxy";
        if (_status?.ActiveProfileId is not { } id) return "Applied profile not reported by the host. Selecting a saved profile does not change the proxy.";
        var name = cfg.Profiles.FirstOrDefault(p => p.Id == id)?.Name ?? id;
        var revision = _status?.ActiveProfileRevision is { } value ? value.ToString() : "unknown";
        return $"{lastReport}: {name} · Profile revision {revision}";
    }

    private Control ProfileDetails(ConfigurationDto cfg, ProfileDto selected, List<Button> detailActions, PresentationOptions presentation)
    {
        var apply = Ui.Button("Apply to proxy…", () => ConfirmProfileApplication(selected));
        var export = Action("Export profile…", () => ExportProfileAsync(selected), "Profile exported without credentials.");
        detailActions.Add(export);
        var routes = new StackPanel { Spacing = 0 };
        foreach (var id in selected.RouteIds)
        {
            var route = cfg.Routes.FirstOrDefault(r => r.Id == id);
            var content = Ui.Stack(ProfileRouteContent(route?.Alias ?? id, route is null ? "Model access unavailable" : $"Provider model: {route.Provider} / {route.UpstreamModel}", id == selected.DefaultRouteId));
            content.Spacing = 4;
            if (route is not null)
            {
                var open = CompactRouteButton("Open model access", () => NavigateTo(new WorkspaceNavigationRequest("Models", RouteId: id)));
                AutomationProperties.SetName(open, $"Open model access {route.Alias}");
                content.Children.Add(open);
            }
            routes.Children.Add(new Border { BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, presentation.Density == ViewDensity.Compact ? 5 : 8), Child = content });
        }
        var selectionState = Ui.Text($"Selected profile: {selected.Name}", 12, Ui.Accent);
        AutomationProperties.SetName(selectionState, "Selected profile status");
        var details = Ui.Stack(Ui.Text(selected.Name, 18), selectionState,
            Ui.Text(_status?.ActiveProfileId == selected.Id
                ? $"This profile identity is applied to the proxy · Host profile revision {_status?.ActiveProfileRevision?.ToString() ?? "unknown"}"
                : "This selection is for viewing. Apply to proxy changes the running proxy for every client.", 12, Ui.Muted),
            Ui.Text($"{selected.RouteIds.Length} selected model access entries · Default client name: {cfg.Routes.FirstOrDefault(r => r.Id == selected.DefaultRouteId)?.Alias ?? "None"}", 12, Ui.Muted),
            Ui.Text(selected.Policy?.CostPolicy switch {
                "free_only" => "Spending policy: Only free — no paid fallback",
                null or "configured" => "Spending policy: Configured access; stricter model access policies still apply",
                var policy => $"Spending policy: {policy}"
            }, 12, Ui.Muted),
            ProfileWrap(Ui.Button("Edit selection", () => EditProfile(selected)), apply, Ui.Button("Set up Codex client…", () => ShowCodexExport(selected))),
            Ui.Text("Applying changes the global proxy. Client setup exports a separate Codex connection.", 12, Ui.Muted),
            new ScrollViewer { MaxHeight = presentation.Level == InformationLevel.Barebones ? 220 : 310, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = routes });
        var technical = Ui.Stack(Ui.Text($"Profile ID: {selected.Id}", 11, Ui.Muted),
            Ui.Text($"Default model access ID: {selected.DefaultRouteId ?? "None"}", 11, Ui.Muted));
        if (selected.Policy?.FallbackRouteIds is { Length: > 0 } fallbacks)
            technical.Children.Add(Ui.Text($"Saved fallback selections: {string.Join(", ", fallbacks.Select(id => cfg.Routes.FirstOrDefault(r => r.Id == id)?.Alias ?? id))} · Paid fallback permission: {(selected.Policy.AllowPaidFallback ? "allowed" : "not allowed")}", 12, Ui.Muted));
        technical.Children.Add(ProfileWrap(export, Ui.Button("Remove…", () => ConfirmRemoval(selected.Name, async () => { _configuration = await _client.RemoveProfileAsync(cfg.Revision, selected.Id, _lifetime.Token); }))));
        var more = new Expander { Header = "Profile details and export", Content = technical,
            IsExpanded = _profileDetails.TryGetValue(selected.Id, out var expanded) ? expanded : presentation.Level == InformationLevel.Advanced,
            HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(more, $"Details and export for profile {selected.Name}");
        more.PropertyChanged += (_, e) => { if (e.Property == Expander.IsExpandedProperty) _profileDetails[selected.Id] = more.IsExpanded; };
        details.Children.Add(more);
        details.Spacing = presentation.Density == ViewDensity.Compact ? 7 : 12;
        return Ui.Card(details);
    }

    private void ConfirmProfileApplication(ProfileDto profile)
    {
        if (_configuration is not { } cfg) return;
        _editing = true;
        var confirm = Action("Apply to proxy", async () => {
            _configuration = await _client.ApplyProfileAsync(cfg.Revision, profile.Id, _lifetime.Token);
        }, $"Profile '{profile.Name}' applied to the global proxy.");
        confirm.Classes.Add("primary");
        _page.Content = Ui.Stack(ProfileHeader($"Apply {profile.Name} to the proxy?", "This changes the GLOBAL proxy configuration for every connected client."),
            Ui.Card(Ui.Stack(Ui.Text("Active requests may be interrupted while the proxy applies this profile.", 14, Ui.Warning),
                Ui.Text($"Apply saved profile '{profile.Name}' from configuration revision {cfg.Revision}. Selecting a profile or exporting a Codex client connection does not perform this action.", 13))),
            ProfileWrap(confirm, Ui.Button("Cancel", () => { _editing = false; Render(); })));
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
        var export = Action("Export Codex connection", async () => {
            if (string.IsNullOrWhiteSpace(executable.Text) || !Path.IsPathFullyQualified(executable.Text) || !File.Exists(executable.Text)) throw new InvalidOperationException("Choose an existing absolute path to the lain command-line executable.");
            if (string.IsNullOrWhiteSpace(folder.Text) || !Directory.Exists(folder.Text)) throw new InvalidOperationException("Choose an existing local destination folder.");
            var destination = Path.Combine(folder.Text, $"lain-codex-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}");
            var artifacts = await _client.ExportCodexProfileAsync(cfg.Revision, profile.Id, executable.Text.Trim(), Path.Combine(destination, "lain.models.json"), _lifetime.Token);
            Directory.CreateDirectory(destination);
            await WriteNewFileAsync(Path.Combine(destination, "lain.config.toml"), artifacts.Configuration);
            await WriteNewFileAsync(Path.Combine(destination, "lain.models.json"), artifacts.Catalog);
            static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";
            var command = $"CODEX_HOME={Quote(destination)} codex {string.Join(" ", artifacts.LaunchArguments.Select(Quote))}";
            var copy = Ui.Button("Copy command", async () => {
                try
                {
                    if (Clipboard is not { } clipboard) throw new InvalidOperationException("Clipboard is unavailable. Select and copy the launch command below.");
                    await clipboard.SetTextAsync(command);
                    _notice.Text = "Launch command copied.";
                }
                catch (Exception error) { _notice.Text = Friendly(error); }
            });
            _notice.Text = $"Codex connection exported to {destination}";
            _page.Content = Ui.Stack(ProfileHeader("Ready for Codex", $"{profile.Name}  ·  {profile.RouteIds.Length} routes  ·  Exported revision {artifacts.Revision}"),
                Ui.Card(Ui.Stack(Ui.Text("1  Exported files", 14), ProfileReadOnly(destination), Ui.Text("lain.config.toml  ·  lain.models.json", 12, Ui.Muted))),
                Ui.Card(Ui.Stack(ProfileHeader("2  Launch this connection", "Run this command in your terminal.", copy), ProfileReadOnly(command),
                    Ui.Text("This launch uses the exported folder as CODEX_HOME. Your existing Codex configuration is preserved.", 12, Ui.Muted))),
                Ui.Button("Back to profiles", () => { _editing = false; Render(); }));
        }, "Codex connection exported.", stayOnPage: true);
        export.Classes.Add("primary");
        _page.Content = Ui.Stack(ProfileHeader("Set up Codex client", $"{profile.Name}  ·  {profile.RouteIds.Length} routes  ·  Saved revision {cfg.Revision}"),
            Ui.Card(Ui.Stack(Ui.Text("1  Local authentication helper", 14), Ui.Field("lain command-line executable", executable),
                Ui.Text("Select the installed lain executable used by Codex to authenticate locally.", 12, Ui.Muted))),
            Ui.Card(Ui.Stack(Ui.Text("2  Export destination", 14), Ui.Field("Parent folder", destinationRow),
                Ui.Text("Creates a new lain-codex-… folder containing a separate configuration and model catalog. This exports client files; it does not apply the profile to the global proxy.", 12, Ui.Muted))),
            ProfileWrap(export, Ui.Button("Cancel", () => { _editing = false; Render(); })));
    }

    private static async Task WriteNewFileAsync(string path, string content)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content);
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
        var search = Ui.Input(hint: "Search model access, providers or models");
        AutomationProperties.SetName(search, "Search profile model access");
        AutomationProperties.SetName(name, "Profile name");
        var spending = new SpendingPolicyEditor(profile?.Policy);
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
            AutomationProperties.SetName(box, $"{route.Alias}, {route.Provider}, {route.UpstreamModel}");
            var row = new Border { BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 0), Child = box };
            routeList.Children.Add(row);
            return (Route: route, Box: box, Row: row);
        }).ToArray();
        void UpdateSelection()
        {
            count.Text = $"{choices.Count(x => x.Box.IsChecked == true)} of {choices.Length} selected  ·  {choices.Count(x => x.Row.IsVisible)} shown";
            var defaultId = defaults.SelectedIndex > 0 ? cfg.Routes[defaults.SelectedIndex - 1].Id : null;
            defaultHint.Text = defaultId is null ? "No default model. Clients can choose any selected client name."
                : choices.Any(x => x.Route.Id == defaultId && x.Box.IsChecked == true) ? "Default model access is included in the selection." : "Include the default model access in your selection before saving.";
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
            var profileId = profile?.Id ?? Guid.NewGuid().ToString("N");
            if (profile is not null && profile.RouteIds.Length == ids.Length && profile.RouteIds.All(id => ids.Contains(id, StringComparer.Ordinal))) ids = profile.RouteIds;
            var original = profile ?? new ProfileDto(profileId, "", []);
            _configuration = await _client.SaveProfileAsync(cfg.Revision,
                original with { Name = name.Text.Trim(), RouteIds = ids, DefaultRouteId = defaultId, Policy = spending.Resolve() }, _lifetime.Token);
            _selectedProfileId = profileId;
        }, "Profile saved.");
        save.Classes.Add("primary");
        var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 14 };
        fields.Children.Add(Ui.Field("Profile name", name));
        var defaultField = Ui.Field("Default client name", defaults);
        Grid.SetColumn(defaultField, 1);
        fields.Children.Add(defaultField);
        var selection = Ui.Stack(search, ProfileHeader("Model access", "Choose the configured entries this profile offers."),
            ProfileWrap(Ui.Button("Select shown", () => { foreach (var x in choices.Where(x => x.Row.IsVisible)) x.Box.IsChecked = true; }),
                Ui.Button("Clear shown", () => { foreach (var x in choices.Where(x => x.Row.IsVisible)) x.Box.IsChecked = false; })), count,
            new ScrollViewer { MaxHeight = 300, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = routeList });
        selection.Spacing = 8;
        _page.Content = Ui.Stack(ProfileHeader(profile is null ? "Create profile" : "Edit profile", $"Choose a model menu and its default client name. Saved revision {cfg.Revision}."),
            Ui.Card(Ui.Stack(fields, defaultHint, spending.Control,
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
