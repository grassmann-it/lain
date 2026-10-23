using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Lain.Core;
using System.Text.Json;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private string? _selectedProfileId;

    private Control ProfilesPage()
    {
        var create = Ui.Button("Create profile", () => EditProfile(null));
        create.Classes.Add("primary");
        var panel = Ui.Stack(ProfileHeader("Profiles", "Focused model menus for your clients and workflows.", create));
        if (_configuration is not { } cfg) { panel.Children.Add(Empty("Connect to the host to manage profiles.")); return panel; }
        panel.Children.Add(Ui.Text($"{cfg.Profiles.Length} profiles  ·  Saved revision {cfg.Revision}  ·  Host applied revision {cfg.AppliedRevision}", 12, Ui.Muted));
        if (cfg.Profiles.Length == 0)
        {
            panel.Children.Add(Empty("No profiles yet. Create a profile to choose its routes and default model."));
            return panel;
        }
        var selected = cfg.Profiles.FirstOrDefault(p => p.Id == _selectedProfileId) ?? cfg.Profiles[0];
        _selectedProfileId = selected.Id;
        var list = new StackPanel { Spacing = 4 };
        foreach (var profile in cfg.Profiles)
        {
            var button = Ui.Button("", () => { _selectedProfileId = profile.Id; Render(); });
            button.Content = new StackPanel { Spacing = 3, Children = { Ui.Text(profile.Name, 13), Ui.Text($"{profile.RouteIds.Length} routes", 12, Ui.Muted) } };
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.Background = profile.Id == selected.Id ? Brush.Parse("#263750") : Brushes.Transparent;
            button.BorderBrush = profile.Id == selected.Id ? Ui.Accent : Brushes.Transparent;
            list.Children.Add(button);
        }
        var routes = new StackPanel { Spacing = 0 };
        foreach (var id in selected.RouteIds)
        {
            var route = cfg.Routes.FirstOrDefault(r => r.Id == id);
            routes.Children.Add(new Border { BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 8),
                Child = ProfileRouteContent(route?.Alias ?? id, route is null ? "Route unavailable" : $"{route.Provider} / {route.UpstreamModel}", id == selected.DefaultRouteId) });
        }
        var details = Ui.Stack(Ui.Text(selected.Name, 18),
            Ui.Text($"{selected.RouteIds.Length} selected routes  ·  Default: {cfg.Routes.FirstOrDefault(r => r.Id == selected.DefaultRouteId)?.Alias ?? "None"}", 12, Ui.Muted),
            ProfileWrap(Ui.Button("Edit selection", () => EditProfile(selected)),
                Action("Apply profile", async () => { _configuration = await _client.ApplyProfileAsync(cfg.Revision, selected.Id, _lifetime.Token); }, $"Profile '{selected.Name}' applied."),
                Ui.Button("Connect Codex…", () => ShowCodexExport(selected))),
            new ScrollViewer { MaxHeight = 310, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = routes },
            ProfileWrap(Action("Export profile…", () => ExportProfileAsync(selected), "Profile exported without credentials."),
                Ui.Button("Remove…", () => ConfirmRemoval(selected.Name, async () => { _configuration = await _client.RemoveProfileAsync(cfg.Revision, selected.Id, _lifetime.Token); }))));
        details.Spacing = 12;
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("200,*"), ColumnSpacing = 14 };
        layout.Children.Add(Ui.Card(new ScrollViewer { MaxHeight = 470, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = list }));
        var detailCard = Ui.Card(details);
        Grid.SetColumn(detailCard, 1);
        layout.Children.Add(detailCard);
        panel.Children.Add(layout);
        return panel;
    }

    private void ShowCodexExport(ProfileDto profile)
    {
        if (_configuration is not { } cfg) return;
        _editing = true;
        var launcher = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "lain");
        var executable = Ui.Input(File.Exists(launcher) ? launcher : "", "/absolute/path/to/lain");
        var folder = Ui.Input(hint: "Choose or enter an existing parent folder");
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
        _page.Content = Ui.Stack(ProfileHeader("Connect Codex", $"{profile.Name}  ·  {profile.RouteIds.Length} routes  ·  Saved revision {cfg.Revision}"),
            Ui.Card(Ui.Stack(Ui.Text("1  Local authentication helper", 14), Ui.Field("lain command-line executable", executable),
                Ui.Text("Select the installed lain executable used by Codex to authenticate locally.", 12, Ui.Muted))),
            Ui.Card(Ui.Stack(Ui.Text("2  Export destination", 14), Ui.Field("Parent folder", destinationRow),
                Ui.Text("Creates a new lain-codex-… folder containing a separate configuration and model catalog. Your normal Codex configuration stays unchanged.", 12, Ui.Muted))),
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
        var search = Ui.Input(hint: "Search routes, providers or models");
        var count = Ui.Text("", 12, Ui.Muted);
        var empty = Ui.Text("No routes match this search.", 13, Ui.Muted);
        var defaults = new ComboBox { ItemsSource = new[] { "None" }.Concat(cfg.Routes.Select(r => r.Alias)).ToArray(), SelectedIndex = profile?.DefaultRouteId is { } id ? Array.FindIndex(cfg.Routes, r => r.Id == id) + 1 : 0 };
        var defaultHint = Ui.Text("", 12, Ui.Muted);
        var routeList = new StackPanel { Spacing = 0 };
        var choices = cfg.Routes.Select(route => {
            var box = new CheckBox { IsChecked = profile?.RouteIds.Contains(route.Id) ?? false,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = ProfileRouteContent(route.Alias, $"{route.Provider} / {route.UpstreamModel}", false), MinHeight = 42 };
            var row = new Border { BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 0), Child = box };
            routeList.Children.Add(row);
            return (Route: route, Box: box, Row: row);
        }).ToArray();
        void UpdateSelection()
        {
            count.Text = $"{choices.Count(x => x.Box.IsChecked == true)} of {choices.Length} selected  ·  {choices.Count(x => x.Row.IsVisible)} shown";
            var defaultId = defaults.SelectedIndex > 0 ? cfg.Routes[defaults.SelectedIndex - 1].Id : null;
            defaultHint.Text = defaultId is null ? "No default route. Clients can choose any selected model."
                : choices.Any(x => x.Route.Id == defaultId && x.Box.IsChecked == true) ? "Default route is included in the selection." : "Include the default route in your selection before saving.";
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
        if (choices.Length == 0) empty.Text = "No routes available. Add routes before creating a profile.";
        routeList.Children.Add(empty);
        UpdateSelection();
        var save = Action("Save profile", async () => {
            if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException("Enter a profile name.");
            var ids = choices.Where(x => x.Box.IsChecked == true).Select(x => x.Route.Id).ToArray();
            var defaultId = defaults.SelectedIndex > 0 ? cfg.Routes[defaults.SelectedIndex - 1].Id : null;
            if (ids.Length == 0) throw new InvalidOperationException("Select at least one route.");
            if (defaultId is not null && !ids.Contains(defaultId)) throw new InvalidOperationException("Select the default route in this profile too.");
            var profileId = profile?.Id ?? Guid.NewGuid().ToString("N");
            _configuration = await _client.SaveProfileAsync(cfg.Revision, new ProfileDto(profileId, name.Text.Trim(), ids, defaultId), _lifetime.Token);
            _selectedProfileId = profileId;
        }, "Profile saved.");
        save.Classes.Add("primary");
        var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 14 };
        fields.Children.Add(Ui.Field("Profile name", name));
        var defaultField = Ui.Field("Default route", defaults);
        Grid.SetColumn(defaultField, 1);
        fields.Children.Add(defaultField);
        var selection = Ui.Stack(search, ProfileHeader("Routes", "Choose the models this profile offers."),
            ProfileWrap(Ui.Button("Select shown", () => { foreach (var x in choices.Where(x => x.Row.IsVisible)) x.Box.IsChecked = true; }),
                Ui.Button("Clear shown", () => { foreach (var x in choices.Where(x => x.Row.IsVisible)) x.Box.IsChecked = false; })), count,
            new ScrollViewer { MaxHeight = 300, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = routeList });
        selection.Spacing = 8;
        _page.Content = Ui.Stack(ProfileHeader(profile is null ? "Create profile" : "Edit profile", $"Choose a model menu and its default. Saved revision {cfg.Revision}."),
            Ui.Card(Ui.Stack(fields, defaultHint)), Ui.Card(selection),
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
