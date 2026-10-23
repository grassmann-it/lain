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

public sealed partial class MainWindow
{
    private Control ProfilesPage()
    {
        var panel = Ui.Stack(Heading("Profiles", "Build focused model menus for each client or workflow."), Ui.Button("Create profile", () => EditProfile(null)));
        if (_configuration is not { } cfg) { panel.Children.Add(Empty("Connect to the host to manage profiles.")); return panel; }
        if (cfg.Profiles.Length == 0) panel.Children.Add(Empty("No profiles yet. Create routes, then choose which models your client should offer."));
        foreach (var profile in cfg.Profiles)
            panel.Children.Add(Ui.Card(Ui.Stack(Ui.Text(profile.Name, 20), Ui.Text(string.Join(" · ", profile.RouteIds.Select(id => cfg.Routes.FirstOrDefault(r => r.Id == id)?.Alias ?? id)), 13, Ui.Muted), Ui.Text($"Default: {cfg.Routes.FirstOrDefault(r => r.Id == profile.DefaultRouteId)?.Alias ?? "None"}", 12, Ui.Muted),
                Ui.Row(Ui.Button("Edit selection", () => EditProfile(profile)), Action("Apply", async () => { _configuration = await _client.ApplyProfileAsync(cfg.Revision, profile.Id, _lifetime.Token); }, $"Profile '{profile.Name}' applied."), Ui.Button("Connect Codex…", () => ShowCodexExport(profile))),
                Ui.Row(Action("Export profile…", () => ExportProfileAsync(profile), "Profile exported without credentials."), Ui.Button("Remove…", () => ConfirmRemoval(profile.Name, async () => { _configuration = await _client.RemoveProfileAsync(cfg.Revision, profile.Id, _lifetime.Token); }))))));
        return panel;
    }

    private void ShowCodexExport(ProfileDto profile)
    {
        if (_configuration is not { } cfg) return;
        _editing = true;
        var launcher = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "lain");
        var executable = Ui.Input(File.Exists(launcher) ? launcher : "", "/absolute/path/to/lain");
        var folder = Ui.Input(hint: "Choose or enter an export destination");
        var pick = Ui.Button("Choose folder…", async () => {
            try
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose a parent folder for a new Codex connection", AllowMultiple = false });
                if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) folder.Text = path;
            }
            catch (Exception error) { _notice.Text = Friendly(error); }
        });
        _page.Content = Ui.Stack(Heading($"Connect Codex · {profile.Name}", "Export a separate Codex configuration and model catalog. A new folder is created inside your chosen destination."),
            Ui.Card(Ui.Stack(Ui.Field("lain command-line executable (used as the local auth helper)", executable), Ui.Field("Destination parent folder", folder), pick)),
            Ui.Row(Action("Export Codex connection", async () => {
                if (string.IsNullOrWhiteSpace(executable.Text) || !Path.IsPathFullyQualified(executable.Text) || !File.Exists(executable.Text)) throw new InvalidOperationException("Choose an existing absolute path to the lain command-line executable.");
                if (string.IsNullOrWhiteSpace(folder.Text) || !Directory.Exists(folder.Text)) throw new InvalidOperationException("Choose an existing local destination folder.");
                var destination = Path.Combine(folder.Text, $"lain-codex-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}");
                var artifacts = await _client.ExportCodexProfileAsync(cfg.Revision, profile.Id, executable.Text.Trim(), Path.Combine(destination, "lain.models.json"), _lifetime.Token);
                Directory.CreateDirectory(destination);
                await WriteNewFileAsync(Path.Combine(destination, "lain.config.toml"), artifacts.Configuration);
                await WriteNewFileAsync(Path.Combine(destination, "lain.models.json"), artifacts.Catalog);
                static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";
                var command = $"CODEX_HOME={Quote(destination)} codex {string.Join(" ", artifacts.LaunchArguments.Select(Quote))}";
                _notice.Text = $"Codex connection exported to {destination}";
                _page.Content = Ui.Stack(Heading("Ready for Codex", $"Exported profile '{profile.Name}' at revision {artifacts.Revision}."), Ui.Card(Ui.Stack(Ui.Text("Launch this connection", 20), new TextBox { Text = command, IsReadOnly = true, TextWrapping = TextWrapping.Wrap }, Ui.Text("This uses the exported folder as Codex's home. Your existing Codex configuration is preserved.", 13, Ui.Muted))), Ui.Button("Back to profiles", () => { _editing = false; Render(); }));
            }, "Codex connection exported.", stayOnPage: true), Ui.Button("Cancel", () => { _editing = false; Render(); })));
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

}
