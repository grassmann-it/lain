using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Lain.Core;
using Lain.Desktop.Presentation;
using Lain.Desktop.Tray;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private readonly WorkspacePreferencesStore _preferencesStore;
    private WorkspacePreferences _preferences => _preferencesStore.Current;
    private readonly Button _settingsButton;
    private readonly Button _backButton;
    private readonly Button _reviewDraftButton;
    private readonly StackPanel _retainedDraftNotices = new() { Spacing = 6, Margin = new Thickness(20, 12, 20, 0), IsVisible = false };
    private readonly ComboBox _informationLevel = new()
    {
        ItemsSource = Enum.GetNames<InformationLevel>(), Width = 146, MinWidth = 0,
        Height = 32, Margin = new Thickness(8, 0, 14, 0), FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Stack<PageVisit> _history = new();
    private readonly Dictionary<string, PageDraft> _drafts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Vector> _scrollPositions = new(StringComparer.Ordinal);
    private bool _navigating;
    private bool _viewOptionsUpdating;
    private Control? _providerDiscoveryPage;
    private long? _editorRevision;
    private Action? _editorRebuild;
    private string? _displayedDraftSection;
    private string? _editorIdentity;
    private (string Section, PageDraft Draft)[] _shownRetainedDrafts = [];
    private Control? _lastEditorFocus;
    private TrayWorkspaceController? _trayController;
    private sealed record PageVisit(string Section, Vector Offset, Action Restore);
    private sealed record PageDraft(Control Content, Button[] Actions, bool LoginVisible, long? Revision, Action? Rebuild, Control? FocusedControl, string Identity);

    public void AttachTrayController(TrayWorkspaceController controller)
    {
        _trayController = controller;
        controller.NotificationsEnabledChanged += enabled =>
        {
            SavePreferences(p => p with { NotificationsEnabled = enabled }, render: false);
            // A failed save keeps the stored choice authoritative for the tray too.
            ConfigureTray();
            SynchronizeSettings();
        };
        controller.AvailabilityChanged += _ => Dispatcher.UIThread.Post(() =>
        {
            SynchronizeSettings();
        });
        ConfigureTray();
    }

    private void ConfigureTray() => _trayController?.Configure(_preferences.MinimizeToTray,
        _preferences.CloseToTray, _preferences.NotificationsEnabled);

    private void RegisterEditor(Action rebuild, long revision, string? identity = null)
    {
        // New task editors start at their identity. Restoring a mounted draft
        // remains separate and restores that draft's own scroll position.
        _workspace.Offset = default;
        Dispatcher.UIThread.Post(() => _workspace.Offset = default, DispatcherPriority.Loaded);
        CloseAccountInspector(restoreFocus: false);
        _editorRevision = revision;
        _editorRebuild = rebuild;
        _editorIdentity = identity;
        _displayedDraftSection = _section;
        UpdateDraftStatus();
    }

    private void UpdateDraftStatus()
    {
        if (_reviewDraftButton is null) return;
        var stale = _editing && _editorRebuild is not null && _editorRevision is { } revision
            && _configuration is { } config && revision != config.Revision;
        _reviewDraftButton.IsVisible = stale;
        if (stale) _notice.Text = "Configuration changed while this draft was open. Your inputs are kept. Review the latest configuration before saving.";
        UpdateRetainedDraftNotices();
    }

    private string DraftIdentity(Control content)
    {
        if (!string.IsNullOrWhiteSpace(_editorIdentity)) return _editorIdentity;
        // Older editors need no new state manager: their mounted title identifies
        // the local task until the page owner supplies an explicit object name.
        return content.GetLogicalDescendants().OfType<TextBlock>()
            .FirstOrDefault(text => text.FontSize >= 18 && !string.IsNullOrWhiteSpace(text.Text))?.Text
            ?? "Local editor";
    }

    private string RetainedDraftText(string section, PageDraft draft)
    {
        var revisionNote = draft.Revision is { } revision && _configuration is { } configuration && revision != configuration.Revision
            ? $" Configuration changed from revision {revision} to {configuration.Revision}; review after returning." : "";
        return $"Local draft kept: {section} · {draft.Identity}. Unsaved in this window." + revisionNote;
    }

    private void UpdateRetainedDraftNotices()
    {
        var retained = _drafts.Where(pair => !(_editing && _displayedDraftSection == pair.Key && _section == pair.Key
                && ReferenceEquals(_page.Content, pair.Value.Content)))
            .Select(pair => (Section: pair.Key, Draft: pair.Value)).ToArray();
        // Polling updates message text and busy state in place so keyboard focus
        // survives even when the saved configuration revision changes.
        if (_shownRetainedDrafts.SequenceEqual(retained))
        {
            for (var index = 0; index < retained.Length; index++)
                if (_retainedDraftNotices.Children[index] is Border { Child: Grid row })
                {
                    ((TextBlock)row.Children[0]).Text = RetainedDraftText(retained[index].Section, retained[index].Draft);
                    foreach (var button in row.Children.OfType<Button>()) button.IsEnabled = !_busy;
                }
            return;
        }
        _shownRetainedDrafts = retained;
        _retainedDraftNotices.Children.Clear();
        foreach (var (section, draft) in retained)
        {
            var identity = $"{section} · {draft.Identity}";
            var text = Ui.Text(RetainedDraftText(section, draft), 12, Ui.Warning);
            var resume = Ui.Button("Continue editing", () => Navigate(section));
            AutomationProperties.SetName(resume, "Return to " + identity + " draft");
            var discard = Ui.Button("Discard draft", () =>
            {
                if (_busy) return;
                // Remove only this retained section. Navigation itself never saves,
                // applies or discards another page's mounted editor.
                _drafts.Remove(section);
                UpdateRetainedDraftNotices();
                var focus = _section is "Settings" or "About" ? _settingsButton
                    : _navigation.Children.OfType<Button>().FirstOrDefault(button => button.IsVisible && Equals(button.Tag, PrimarySection(_section))) ?? _moreNavigation;
                focus.Focus();
            });
            AutomationProperties.SetName(discard, "Discard " + identity + " draft");
            resume.IsEnabled = discard.IsEnabled = !_busy;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
            text.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(text);
            Grid.SetColumn(resume, 1); row.Children.Add(resume);
            Grid.SetColumn(discard, 2); row.Children.Add(discard);
            var notice = new Border { Background = Ui.Raised, BorderBrush = Ui.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8), Child = row };
            AutomationProperties.SetName(notice, "Retained local draft " + identity);
            _retainedDraftNotices.Children.Add(notice);
        }
        _retainedDraftNotices.IsVisible = _retainedDraftNotices.Children.Count > 0;
    }

    private void ReviewDraft()
    {
        if (_busy || _editorRebuild is null || _page.Content is not Control previous) return;
        var rebuild = _editorRebuild;
        var originalRevision = _editorRevision;
        var originalIdentity = _editorIdentity;
        var originalActions = _actions.ToArray();
        var snapshot = EditorSnapshot.Capture(previous);
        try
        {
            rebuild();
            if (_page.Content is not Control current) throw new InvalidOperationException("The editor could not be reopened.");
            var unmatched = snapshot.Restore(current);
            // The rebuilt editor is now the active draft; the dictionary entry
            // still points at its previous mount until the next navigation.
            _drafts.Remove(_section);
            _notice.Text = unmatched.Count == 0
                ? "Draft reopened against the latest saved configuration. Your inputs were restored; review them before saving."
                : "Review updated choices before saving. These fields could not be restored: " + string.Join(", ", unmatched);
            UpdateDraftStatus();
        }
        catch (Exception error)
        {
            _page.Content = previous;
            _actions.Clear(); _actions.AddRange(originalActions);
            _editorRevision = originalRevision; _editorRebuild = rebuild; _editorIdentity = originalIdentity; _editing = true;
            _notice.Text = "Draft kept: " + Friendly(error);
        }
    }

    private static string NormalizeSection(string section) => section switch
    {
        "Overview" => "Accounts", "Routes" => "Models", "Inspector" => "Live", _ => section
    };

    private static string PrimarySection(string section) => section switch
    {
        "Providers" or "Setup" or "Service" => "Accounts", _ => NormalizeSection(section)
    };

    private PresentationOptions PresentationFor(string section)
    {
        var preferences = _preferences;
        return new(preferences.Level, preferences.Densities.GetValueOrDefault(PrimarySection(section), ViewDensity.Normal));
    }

    private void SavePreferences(Func<WorkspacePreferences, WorkspacePreferences> change, bool render = true)
    {
        if (!_preferencesStore.Save(change(_preferences)))
        {
            _notice.Text = _preferencesStore.Error ?? "View preferences could not be saved.";
            SynchronizeInformationLevel();
            SynchronizeSettings();
            return;
        }
        ApplyPresentation(render);
    }

    private void ApplyPresentation(bool render)
    {
        ConfigureTray();
        _usage.SetPresentation(PresentationFor("Usage"));
        _inspector.SetPresentation(PresentationFor("Live"));
        _providers.SetPresentation(PresentationFor("Accounts"));
        SetProviderPresentation(PresentationFor("Accounts"));
        SynchronizeInformationLevel();
        SynchronizeSettings();
        UpdateNavigationDestinations();
        if (_workspace is not null)
            _workspace.Margin = PresentationFor(_section).Density == ViewDensity.Compact
                ? new Thickness(14, 12) : new Thickness(20, 18);
        // Edit controls stay mounted: preferences cannot discard drafts or secrets.
        // Live/Usage have in-place presentation; detaching Live would clear its buffer.
        if (render && !_editing && _section is not ("Usage" or "Live" or "Providers" or "Settings" or "About")) Render();
    }

    private void SynchronizeInformationLevel()
    {
        var wasUpdating = _viewOptionsUpdating;
        _viewOptionsUpdating = true;
        _informationLevel.SelectedIndex = (int)_preferences.Level;
        _viewOptionsUpdating = wasUpdating;
    }

    private void SaveCurrentPage()
    {
        _scrollPositions[_section] = _workspace.Offset;
        if (_editing && _page.Content is Control draft)
        {
            var controls = draft.GetLogicalDescendants().OfType<Control>().Prepend(draft).ToArray();
            var focused = controls.FirstOrDefault(control => control.IsFocused)
                ?? controls.FirstOrDefault(control => ReferenceEquals(control, _lastEditorFocus));
            _drafts[_section] = new(draft, _actions.ToArray(), _loginVisible, _editorRevision, _editorRebuild, focused, DraftIdentity(draft));
        }
    }

    private void Navigate(string section) => NavigateTo(new(NormalizeSection(section)));

    private void NavigateTo(WorkspaceNavigationRequest target) => _ = NavigateToAsync(target);

    private async Task NavigateToAsync(WorkspaceNavigationRequest target, bool remember = true, Vector? offset = null)
    {
        if (_navigating || _busy) return;
        _navigating = true;
        try
        {
            var section = NormalizeSection(target.Section);
            if (section is not ("Accounts" or "Models" or "Profiles" or "Usage" or "Live" or "Providers" or "Setup" or "Service" or "Settings" or "About")) return;
            if (section == "Settings" && _section is not ("Settings" or "About"))
                _settingsDensitySection = PrimarySection(_section);
            SaveCurrentPage();
            if (remember && (section != _section || target.AccountId is not null || target.RouteId is not null || target.Model is not null || target.ProfileId is not null))
            {
                var restore = _section switch
                {
                    "Accounts" => CaptureAccountsNavigation(),
                    "Models" or "Profiles" => CaptureModelsNavigation(),
                    "Usage" => _usage.CaptureNavigationState(),
                    _ => (Action)(() => { })
                };
                _history.Push(new(_section, _workspace.Offset, restore));
                if (_history.Count > 50)
                {
                    var retained = _history.Take(50).Reverse().ToArray();
                    _history.Clear(); foreach (var visit in retained) _history.Push(visit);
                }
            }
            CloseAccountInspector(restoreFocus: false);
            _section = section;
            _notice.Text = "";
            _editing = false; _loginVisible = false;
            _editorRevision = null; _editorRebuild = null; _editorIdentity = null; _displayedDraftSection = null;
            if (section == "Accounts") ApplyAccountNavigation(target);
            if (section == "Models") ApplyModelsNavigation(target);
            if (section == "Profiles" && target.ProfileId is { } profileId) _selectedProfileId = profileId;
            Control? restoreFocus = null;
            if (target.AccountId is null && target.RouteId is null && target.Model is null && target.ProfileId is null && _drafts.TryGetValue(section, out var draft))
            {
                _editing = true; _loginVisible = draft.LoginVisible;
                _editorRevision = draft.Revision; _editorRebuild = draft.Rebuild; _editorIdentity = draft.Identity; _displayedDraftSection = section;
                _actions.Clear(); _actions.AddRange(draft.Actions);
                _page.Content = draft.Content;
                restoreFocus = draft.FocusedControl;
                UpdateNavigationSelection();
                if (draft.LoginVisible) RestoreLoginPresentation();
            }
            else Render();
            ApplyPresentation(false);
            var scroll = offset ?? _scrollPositions.GetValueOrDefault(section);
            Dispatcher.UIThread.Post(() =>
            {
                if (_section != section) return;
                _workspace.Offset = scroll;
                if (_editing)
                {
                    var focused = restoreFocus?.Focus() ?? false;
                    if (!focused && _page.Content is Control editor)
                        editor.GetLogicalDescendants().OfType<Control>().FirstOrDefault(control => control.Focusable && control.IsEffectivelyEnabled && control.IsEffectivelyVisible)?.Focus();
                }
            }, DispatcherPriority.Loaded);
            _backButton.IsEnabled = _history.Count > 0;
            UpdateDraftStatus();
            if (section == "Providers") _ = RefreshProviderCatalogAsync();
            _navigating = false;
            if (section == "Usage")
            {
                if (target.RouteId is not null) await _usage.FilterToRouteAsync(target.RouteId);
                else if (target.AccountId is not null) await _usage.FilterToAccountAsync(target.AccountId);
                else if (target.Model is not null) await _usage.FilterToModelAsync(target.Model);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { _notice.Text = Friendly(error); }
        finally { _navigating = false; }
    }

    private void GoBack()
    {
        if (_navigating || _busy) return;
        if (_history.TryPop(out var visit))
        {
            visit.Restore();
            _ = NavigateToAsync(new(visit.Section), false, visit.Offset);
        }
    }

    private void UpdateNavigationSelection()
    {
        foreach (var button in _navigation.Children.OfType<Button>())
        {
            var primary = PrimarySection(_section);
            var selected = Equals(button.Tag, primary) || ReferenceEquals(button, _moreNavigation)
                && primary is "Models" or "Profiles" or "Usage" or "Live" or "Service"
                && !_navigation.Children.OfType<Button>().Any(item => item.IsVisible && Equals(item.Tag, primary));
            button.Background = selected ? Ui.Raised : Avalonia.Media.Brushes.Transparent;
            button.Foreground = selected ? Ui.Ink : Ui.Muted;
            if (button.Content is StackPanel row)
                foreach (var text in row.Children.OfType<TextBlock>()) text.Foreground = selected ? Ui.Ink : Ui.Muted;
            AutomationProperties.SetHelpText(button, selected ? "Current page" : $"Open {button.Tag}");
        }
    }

    private void ShowProviderCatalog() => Navigate("Providers");
    private void ShowUsageForAccount(string id) => NavigateTo(new("Usage", AccountId: id));
    private void ShowModelsForAccount(string id) => NavigateTo(new("Models", AccountId: id));

    private Control ProviderDiscoveryPage() => _providerDiscoveryPage ??= Ui.Stack(
        OverviewActions(Ui.Button("Back to accounts", () => Navigate("Accounts")), Ui.Button("Add account", ShowAddAccountChooser)),
        _providers);

    private string ServiceSummary(StatusDto status)
    {
        if (ProfileApplyOutcomeUnknown)
        {
            var target = _configuration?.Profiles.FirstOrDefault(profile => profile.Id == _profileUnconfirmedApplyId)?.Name
                ?? _profileUnconfirmedApplyId ?? "Unknown profile";
            var unknownSummary = $"Apply outcome unknown · Requested: {target} · revision {_profileUnconfirmedApplyRevision?.ToString() ?? "unknown"}";
            if (ProfileConfirmedStatus is { ActiveProfileId: { } confirmedId } confirmed)
            {
                var name = _configuration?.Profiles.FirstOrDefault(profile => profile.Id == confirmedId)?.Name ?? confirmedId;
                unknownSummary += $" · Last confirmed: {name} · revision {confirmed.ActiveProfileRevision?.ToString() ?? "unknown"}";
            }
            else unknownSummary += " · Last confirmed profile unknown";
            return unknownSummary;
        }
        if (IsMonitorMode) return "Monitor · " + (status.Capabilities?.QuotaMonitoring == false ? "Quota monitoring unavailable" : "Account quotas")
            + (HasHistory ? " · Recorded history available" : "");
        var text = status.ProxyReady ? "Proxy ready" : "Proxy " + status.State;
        // Keep operational warnings before a user-controlled, potentially long name.
        if (status.Revision != status.AppliedRevision) text += $" · Changes pending ({status.Revision} / {status.AppliedRevision})";
        if (!string.IsNullOrWhiteSpace(status.Error)) text += " · Attention required";
        if (status.ActiveProfileId is { } id)
        {
            var name = _configuration?.Profiles.FirstOrDefault(p => p.Id == id)?.Name ?? id;
            text += " · " + (status.ProxyReady ? "Active: " : "Applied: ") + name;
        }
        return text;
    }

    private void UpdateServiceSummary()
    {
        if (_status is { } status && (ProfileApplyOutcomeUnknown || _hostAvailable))
        {
            _connection.Text = ServiceSummary(status) + (!_hostAvailable ? " · Host unavailable" : "");
            _hostIndicator.Background = ProfileApplyOutcomeUnknown ? Ui.Warning
                : IsMonitorMode || status.ProxyReady ? Ui.Success : Ui.Warning;
        }
        RefreshProfileReviewState();
    }
}
