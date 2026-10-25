using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Lain.Cli;
using Lain.Client;
using Lain.Core;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private Action? _refreshCodexSetup;
    private long _codexSetupGeneration;
    private sealed record CodexDispatcherCheck(bool Ready, string Message, string? Entry = null, string? Receipt = null);
    private Func<CancellationToken, Task<CodexDispatcherCheck>> _codexDispatcherProbe = CheckCodexDispatcherAsync;
    private sealed record CodexSetupDraft(string? Executable, string? Parent, int Target, bool Groups);

    private void ShowCodexExport(ProfileDto profile) => ShowCodexExportDraft(profile, null);

    private void ShowCodexExportDraft(ProfileDto profile, CodexSetupDraft? retained)
    {
        if (!CanOpenSectionEditor() || _configuration is not { } cfg) return;
        if (cfg.Profiles.FirstOrDefault(item => item.Id == profile.Id) is not { } current)
        { _notice.Text = "This profile no longer exists. Return to Profiles and select an existing profile."; return; }
        profile = current;
        var profileId = profile.Id;
        var sessionGeneration = ++_codexSetupGeneration;
        _editing = true;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var launcher = Path.Combine(home, ".local", "bin", "lain");
        var executable = Ui.Input(retained?.Executable ?? (File.Exists(launcher) ? launcher : ""), "/absolute/path/to/lain");
        AutomationProperties.SetName(executable, "lain command-line executable");
        var folder = Ui.Input(retained?.Parent, "Choose or enter an existing parent folder");
        AutomationProperties.SetName(folder, "Destination parent folder");
        var target = new ComboBox { ItemsSource = new[] { "New private Codex folder", "Separate lain profile files in ~/.codex" }, SelectedIndex = retained?.Target == 1 ? 1 : 0,
            HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(target, "Codex installation target");
        var grouped = new CheckBox { Content = "Include model groups and configured automatic routers", IsChecked = retained?.Groups ?? false };
        AutomationProperties.SetName(grouped, "Include Codex model groups");
        // Prepared content deliberately contains no editable fields. Capture the
        // actual setup controls here as well, so shell Review latest can rebuild
        // inputs even when EditorSnapshot only sees the prepared review surface.
        // Keep the stable profile ID; never transfer a preparation to another
        // profile or carry old artifacts/proof across a configuration revision.
        RegisterEditor(() =>
        {
            var inputs = new CodexSetupDraft(executable.Text, folder.Text, target.SelectedIndex, grouped.IsChecked == true);
            var latest = _configuration?.Profiles.FirstOrDefault(item => item.Id == profileId)
                ?? throw new InvalidOperationException("This profile was removed. Your setup inputs are kept; cancel setup and review Profiles.");
            ShowCodexExportDraft(latest, inputs);
        }, cfg.Revision, profile.Name + " · Codex setup");
        var validation = Ui.Text("", 12, Ui.Warning);
        AutomationProperties.SetName(validation, "Client setup validation");
        var dispatcherText = Ui.Text("For model groups, check the separately installed lain Codex dispatcher before preparing.", 12, Ui.Muted);
        AutomationProperties.SetName(dispatcherText, "Codex dispatcher prerequisite");
        var root = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var working = false;
        var cancelled = false;
        CodexDispatcherCheck? proof = null;
        Button? prepare = null;
        Button? install = null;
        TextBlock? preparedStatus = null;
        bool Shared() => target.SelectedIndex == 1;
        bool Active() => !cancelled && sessionGeneration == _codexSetupGeneration;
        bool Current() => Active() && CodexPreparationCurrent(cfg.Revision, profile.Id);
        void Refresh()
        {
            var error = ValidateCodexExecutable(executable.Text) ?? (Shared() ? null : ValidateCodexParent(folder.Text));
            if (!Current()) error = "Saved configuration changed or this profile was removed. Prepare again from the latest profile. Your inputs are kept.";
            validation.Text = error ?? "";
            if (prepare is not null) prepare.IsEnabled = !working && !_busy && error is null;
            if (install is not null) install.IsEnabled = !working && !_busy && Current();
            if (preparedStatus is not null && !Current()) preparedStatus.Text = "Preparation is out of date · no files installed. Prepare again before installation.";
        }
        _refreshCodexSetup = Refresh;
        void Cancel()
        {
            if (!Active()) return;
            cancelled = true; ++_codexSetupGeneration; _editing = false; Render();
        }
        var browse = Ui.Button("Browse…", async () =>
        {
            try
            {
                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose a parent folder for a new Codex connection", AllowMultiple = false });
                if (!cancelled && folders.FirstOrDefault()?.TryGetLocalPath() is { } path) folder.Text = path;
            }
            catch (Exception error) { validation.Text = Friendly(error); }
            finally { if (Active()) folder.Focus(); }
        });
        var destinationRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        destinationRow.Children.Add(folder); Grid.SetColumn(browse, 1); destinationRow.Children.Add(browse);
        var parentField = Ui.Field("Parent folder", destinationRow);
        var targetHelp = Ui.Text("Install will create a fresh lain-codex-… folder. Prepare writes no files.", 12, Ui.Muted);
        void TargetChanged()
        {
            parentField.IsVisible = !Shared();
            targetHelp.Text = Shared()
                ? "Only separate lain.config.toml and lain model artifacts in ~/.codex are installed. Ordinary config.toml is preserved. A verified dispatcher is required for the displayed launch command."
                : "Install will create a fresh lain-codex-… folder. Ordinary Codex files are preserved. Prepare writes no files.";
            proof = null; Refresh();
        }
        target.SelectionChanged += (_, _) => TargetChanged();
        executable.TextChanged += (_, _) => Refresh(); folder.TextChanged += (_, _) => Refresh();
        var check = Ui.Button("Check installed dispatcher", async () =>
        {
            if (working) return;
            working = true; Refresh();
            proof = await _codexDispatcherProbe(_lifetime.Token);
            if (Active()) dispatcherText.Text = proof.Message;
            working = false; Refresh();
        });
        Control? setup = null;
        prepare = Ui.Button("Prepare Codex files", () => _ = PrepareAsync());
        prepare.Classes.Add("primary");
        setup = Ui.Stack(ProfileHeader("Set up Codex client", $"{profile.Name} · {profile.RouteIds.Length} routes · Saved revision {cfg.Revision}"),
            Ui.Card(Ui.Stack(Ui.Text("1  Local authentication helper", 14), Ui.Field("lain command-line executable", executable))),
            Ui.Card(Ui.Stack(Ui.Text("2  Export and destination", 14), Ui.Field("Installation target", target), parentField, targetHelp, grouped,
                Ui.Text("Groups share existing route IDs. Automatic selection keeps the chosen model and account for the conversation; it does not retry another model. Free automation still requires qualified free-only access.", 12, Ui.Muted))),
            Ui.Card(Ui.Stack(Ui.Text("3  Client prerequisite", 14), dispatcherText, check)), validation,
            Ui.Text("Prepare and Install do not apply this profile or install a Codex dispatcher.", 12, Ui.Muted),
            ProfileWrap(prepare, Ui.Button("Cancel", Cancel)));
        root.Content = setup; _page.Content = root; TargetChanged();

        async Task PrepareAsync()
        {
            Refresh();
            if (working || _busy || !prepare!.IsEnabled) return;
            var operationGeneration = sessionGeneration;
            working = true; Refresh();
            try
            {
                var groups = grouped.IsChecked == true;
                var shared = Shared();
                var executablePath = executable.Text!.Trim();
                var destination = shared ? Path.Combine(home, ".codex")
                    : Path.Combine(folder.Text!.Trim(), $"lain-codex-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
                // This is a pure host export. Even missing dispatcher prerequisites do
                // not cause a local install, repair or launcher replacement.
                if (groups || shared)
                {
                    proof = await _codexDispatcherProbe(_lifetime.Token);
                    if (!Active() || operationGeneration != sessionGeneration) return;
                    dispatcherText.Text = proof.Message;
                    if (!proof.Ready) throw new InvalidOperationException(proof.Message);
                }
                var preparedProof = proof;
                var artifacts = await _client.ExportCodexProfileAsync(cfg.Revision, profile.Id, executablePath,
                    Path.Combine(destination, "lain.models.json"), _lifetime.Token, enableLainModelPicker: groups);
                if (!Active() || operationGeneration != sessionGeneration) return;
                if (artifacts.ProfileId != profile.Id || artifacts.Revision != cfg.Revision || string.IsNullOrWhiteSpace(artifacts.GenerationId))
                    throw new InvalidOperationException("The export does not identify this saved profile and revision. Nothing was installed; prepare again.");
                // Count the actual compiled export, rather than infer group membership
                // from a name or a possibly newer configuration snapshot.
                var scope = CodexExportScope(artifacts, groups);
                var installationStarted = false;
                var installedFiles = false;
                var prepareMessage = Ui.Text("", 12, Ui.Warning);
                preparedStatus = Ui.Text("No files installed yet", 15);
                AutomationProperties.SetName(preparedStatus, "Codex setup status");
                install = Ui.Button("Install client files", () => _ = InstallAsync());
                install.Classes.Add("primary");
                var change = Ui.Button("Change setup", () =>
                {
                    if (working) return;
                    sessionGeneration = ++_codexSetupGeneration;
                    install = null; preparedStatus = null;
                    root.Content = setup; Refresh();
                });
                AutomationProperties.SetName(change, "Change prepared Codex target");
                var identity = Ui.Text($"Profile: {artifacts.ProfileId} · Export generation: {artifacts.GenerationId} · Revision: {artifacts.Revision}", 12, Ui.Muted);
                AutomationProperties.SetName(identity, "Prepared Codex artifact");
                root.Content = Ui.Stack(ProfileHeader("Codex export prepared", $"{profile.Name} · Saved export revision {artifacts.Revision}"),
                    Ui.Card(Ui.Stack(preparedStatus, identity, Ui.Text(scope, 12), ProfileReadOnly(destination),
                        Ui.Text(groups ? "Client: separately qualified lain Codex 0.164.0 with model groups." : "Client: compatible Codex 0.164.0; model groups are not included.", 12, Ui.Muted),
                        Ui.Text("Install uses exactly these prepared artifacts. Saving, global Apply and client installation are separate. A later Host change cannot be locked atomically with local files.", 12, Ui.Muted))),
                    prepareMessage, ProfileWrap(install, change, Ui.Button("Cancel", Cancel)));
                Refresh();

                async Task InstallAsync()
                {
                    if (working || installedFiles || !Active() || operationGeneration != sessionGeneration) return;
                    working = true; Refresh();
                    try
                    {
                        if (!Current()) throw new InvalidOperationException("Configuration changed. Nothing was installed; prepare again from the latest saved profile.");
                        if (groups || shared)
                        {
                            var latest = await _codexDispatcherProbe(_lifetime.Token);
                            if (!Active() || operationGeneration != sessionGeneration) return;
                            if (!latest.Ready || latest.Receipt != preparedProof?.Receipt)
                                throw new InvalidOperationException("The Codex dispatcher prerequisite changed. Nothing was installed; check the client and prepare again. " + latest.Message);
                        }
                        // This guard covers changes already observed by the GUI. A
                        // Host write after this check is not a local-file transaction.
                        if (!Active() || operationGeneration != sessionGeneration) return;
                        if (!Current()) throw new InvalidOperationException("Configuration changed. Prepare again before installing.");
                        if (!shared && !installationStarted && (Directory.Exists(destination) || File.Exists(destination)))
                            throw new InvalidOperationException("The private destination already exists. Prepare a fresh folder.");
                        installationStarted = true;
                        if (!Active() || operationGeneration != sessionGeneration) return;
                        var result = CodexArtifactInstaller.Install(artifacts, destination);
                        installedFiles = true;
                        var inspection = CodexArtifactInstaller.Inspect(destination,
                            ProfileApplyOutcomeUnknown ? null : _status?.ActiveProfileId, ProfileApplyOutcomeUnknown ? null : _status?.ActiveProfileRevision);
                        if (inspection.State != "installed") throw new InvalidOperationException(inspection.RecoveryAction ?? "Client installation needs recovery.");
                        var launch = await CodexLaunchCommandAsync(destination, shared, groups, preparedProof, result, _lifetime.Token);
                        if (!Active() || operationGeneration != sessionGeneration) return;
                        var command = Ui.Text(launch.Command ?? launch.Message, 12, launch.Command is null ? Ui.Warning : Ui.Muted);
                        var copy = Ui.Button("Copy command", async () =>
                        {
                            try
                            {
                                if (!Active() || operationGeneration != sessionGeneration) return;
                                var fresh = await CodexLaunchCommandAsync(destination, shared, groups, preparedProof, result, _lifetime.Token);
                                if (!Active() || operationGeneration != sessionGeneration) return;
                                if (fresh.Command is null) { command.Text = fresh.Message; return; }
                                if (Clipboard is not { } clipboard) throw new InvalidOperationException("Clipboard is unavailable. Select the command below.");
                                if (!Active() || operationGeneration != sessionGeneration) return;
                                await clipboard.SetTextAsync(fresh.Command);
                                if (Active() && operationGeneration == sessionGeneration) _notice.Text = "Launch command copied.";
                            }
                            catch (Exception error) { command.Text = Friendly(error); }
                        });
                        copy.IsVisible = launch.Command is not null;
                        root.Content = Ui.Stack(ProfileHeader("Codex files installed", $"{profile.Name} · Exported revision {result.Revision}"),
                            Ui.Card(Ui.Stack(Ui.Text(!Current() ? "This export is older than the now-known saved configuration. Prepare and install again when ready."
                                : inspection.Stale switch { false => "Matches the last reported applied profile and revision.", true => "Differs from the last reported applied profile. Apply remains separate.", _ => "Applied profile identity is unknown; runtime alignment cannot be confirmed." }, 12, Ui.Muted),
                                ProfileReadOnly(result.ConfigurationPath), ProfileReadOnly(result.CatalogPath), Ui.Text("Installed generation: " + result.GenerationId, 11, Ui.Muted))),
                            Ui.Card(Ui.Stack(ProfileHeader("Launch this connection", "Run the checked command in your terminal.", copy), command)),
                            Ui.Text("Separate lain files were installed. Ordinary Codex configuration and the global proxy were preserved.", 12, Ui.Muted),
                            Ui.Button("Back to profiles", Cancel));
                    }
                    catch (Exception error) { prepareMessage.Text = (installedFiles ? "Files were installed; review before retrying. " : "No completed installation. ") + Friendly(error); }
                    finally { working = false; Refresh(); }
                }
            }
            catch (Exception error) { validation.Text = Friendly(error); }
            finally { working = false; if (ReferenceEquals(root.Content, setup)) { /* Keep the inline export error. */ prepare.IsEnabled = !_busy && Current(); } else Refresh(); }
        }
    }

    private bool CodexPreparationCurrent(long revision, string profileId) => _configuration is { } current
        && current.Revision == revision && current.Profiles.Any(profile => profile.Id == profileId)
        && !(_status is { } status && status.Revision > revision);

    private static string CodexExportScope(CodexProfileArtifacts artifacts, bool groups)
    {
        using var document = JsonDocument.Parse(artifacts.Catalog);
        if (!groups) return "Model groups: off";
        if (!document.RootElement.TryGetProperty("lain_model_picker", out var picker)
            || !picker.TryGetProperty("groups", out var values) || values.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The export is missing the requested model groups. Nothing was installed.");
        var automatic = values.EnumerateArray().Count(group => group.TryGetProperty("router_model", out var router) && router.ValueKind == JsonValueKind.String);
        return $"Model groups: {values.GetArrayLength()} · Automatic targets: {automatic}. Only qualified exported members are included.";
    }

    private async Task<(string? Command, string Message)> CodexLaunchCommandAsync(string destination, bool shared, bool groups,
        CodexDispatcherCheck? prepared, CodexInstallResult expected, CancellationToken ct)
    {
        string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";
        var inspection = CodexArtifactInstaller.Inspect(destination);
        if (inspection.State != "installed") return (null, inspection.RecoveryAction ?? "Installed artifacts need review.");
        if (inspection.ProfileId != expected.ProfileId || inspection.Revision != expected.Revision || inspection.GenerationId != expected.GenerationId)
            return (null, "The installed lain files changed since this setup. Review the installation before launching this connection.");
        if (!shared && !groups) return ($"CODEX_HOME={Quote(destination)} codex --no-daemon --profile lain", "");
        var latest = await _codexDispatcherProbe(ct);
        if (!latest.Ready || latest.Receipt != prepared?.Receipt)
            return (null, "Files are installed, but the dispatcher no longer matches the checked prerequisite. Check and prepare the client setup again. " + latest.Message);
        // Pin the checked entry, not an unrelated executable resolved via PATH.
        // Explicit home selection also makes an inherited CODEX_HOME harmless.
        return ($"CODEX_HOME={Quote(destination)} {Quote(latest.Entry!)} --no-daemon --profile lain", "");
    }

    private static async Task<CodexDispatcherCheck> CheckCodexDispatcherAsync(CancellationToken ct)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var state = Path.Combine(home, ".local", "share", "lain", "clients", "codex-profile-lain");
        var maintenance = Path.Combine(state, "maintenance.py");
        if (!OperatingSystem.IsLinux() || !File.Exists(maintenance))
            return new(false, "The qualified lain Codex dispatcher status is unavailable. Install or explicitly maintain the separate client first; this screen does not install it. A private export without groups remains available.");
        try
        {
            // Do not execute a checker that no longer matches the installed
            // maintenance receipt. This is local integrity, not a signature or
            // protection against another process running as the same user.
            var receiptPath = Path.Combine(state, "receipt.json");
            var manifestPath = Path.Combine(state, "manifest.json");
            using var receiptDocument = JsonDocument.Parse(ReadCodexStatusFile(receiptPath));
            var installed = receiptDocument.RootElement;
            if (!CodexReceiptMatchesTarget(installed, home))
                return new(false, "The dispatcher receipt does not match this supported default installation. Explicit client maintenance is required; no checker was started.");
            var scriptHash = Convert.ToHexString(SHA256.HashData(ReadCodexStatusFile(maintenance))).ToLowerInvariant();
            var manifestHash = Convert.ToHexString(SHA256.HashData(ReadCodexStatusFile(manifestPath))).ToLowerInvariant();
            if (installed.GetProperty("maintenanceSha256").GetString() != scriptHash || installed.GetProperty("manifestSha256").GetString() != manifestHash)
                return new(false, "The installed dispatcher maintenance files changed. Explicit client maintenance is required; no checker was started.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or EntryPointNotFoundException)
        { return new(false, "The dispatcher maintenance receipt is missing or invalid. Explicit client maintenance is required; no checker was started."); }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var process = new Process { StartInfo = new ProcessStartInfo("/usr/bin/python3")
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } };
        foreach (var argument in new[] { maintenance, "--state", state, "status", "--json" }) process.StartInfo.ArgumentList.Add(argument);
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var json = await output; await errors;
            if (json.Length > 65536) return new(false, "Dispatcher status exceeded its size limit. Review the installed client.");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("code").GetString() != "ready" || !root.GetProperty("readyForGroups").GetBoolean()
                || !root.TryGetProperty("receipt", out var receipt) || receipt.ValueKind != JsonValueKind.Object)
                return new(false, "The lain Codex dispatcher is missing, changed or needs explicit maintenance. Review its status before preparing groups; no client was changed.");
            var entry = receipt.GetProperty("entry").GetString();
            if (process.ExitCode != 0 || !CodexReceiptMatchesTarget(receipt, home) || string.IsNullOrEmpty(entry) || !File.Exists(entry))
                return new(false, "The installed dispatcher did not provide a valid checked entry. Review the separate client.");
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(receipt.GetRawText())));
            return new(true, "Qualified lain Codex 0.164.0 dispatcher checked. Installation will check it again before writing files.", entry, fingerprint);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return new(false, "Dispatcher status did not finish. Retry the check; no client was changed.");
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or JsonException or InvalidOperationException or KeyNotFoundException)
        { return new(false, "The installed dispatcher could not be verified. Review or explicitly maintain the separate client; no client was changed."); }
    }
}
