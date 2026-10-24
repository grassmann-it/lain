using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
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
    private readonly Dictionary<string, bool> _modelDetails = new(StringComparer.Ordinal);

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
        if (target.RouteId is { } id) _modelDetails[id] = true;
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
        var profileId = _selectedProfileId;
        var profileDetails = new Dictionary<string, bool>(_profileDetails, StringComparer.Ordinal);
        return () =>
        {
            if (section == "Models")
            {
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
        var presentation = PresentationFor("Models");
        var dense = presentation.Density == ViewDensity.Compact;
        var create = Ui.Button("Add model access", () => EditRoute(null));
        create.Classes.Add("primary");
        var panel = Ui.Stack(ProfileHeader("Models", presentation.Level == InformationLevel.Barebones ? "Configured model access." : "Configured model access: the name clients use, its provider model and account binding.", create));
        if (_configuration is not { } cfg) { panel.Children.Add(Empty("Connect to the host to manage model access.")); return panel; }

        var search = Ui.Input(_routeSearch, "Search client names, provider models or accounts");
        var providers = new[] { "All providers" }.Concat(cfg.Routes.Select(r => r.Provider).Distinct().OrderBy(p => p, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (!providers.Contains(_routeProvider)) _routeProvider = providers[0];
        var provider = new ComboBox { ItemsSource = providers, SelectedItem = _routeProvider, MinWidth = 132 };
        var visibility = new ComboBox { ItemsSource = new[] { "All visibility", "Visible", "Hidden" }, SelectedItem = _routeVisibility, MinWidth = 116 };
        var state = new ComboBox { ItemsSource = new[] { "All states", "Enabled", "Disabled" }, SelectedItem = _routeState, MinWidth = 104 };
        AutomationProperties.SetName(search, "Search configured model access");
        AutomationProperties.SetName(provider, "Filter model access by provider");
        AutomationProperties.SetName(visibility, "Filter model visibility");
        AutomationProperties.SetName(state, "Filter model state");
        panel.Children.Add(Ui.Stack(search, ProfileWrap(provider, visibility, state)));

        var count = Ui.Text("", 12, Ui.Muted);
        var rows = new StackPanel { Spacing = dense ? 6 : 12 };
        var contextText = Ui.Text("", 12, Ui.Accent);
        var context = Ui.Stack(contextText);
        context.Children.Add(Ui.Button("Clear model context", () => {
            _routeAccountId = _routeContextId = _routeModel = null;
            Populate();
        }));
        panel.Children.Add(context);
        panel.Children.Add(count);
        panel.Children.Add(rows);
        string AccountNames(string[] ids) => string.Join(", ", ids.Select(id => cfg.Accounts.FirstOrDefault(a => a.Id == id)?.Name ?? id));
        void Populate()
        {
            rows.Children.Clear();
            var contexts = new List<string>();
            if (_routeAccountId is { } account) contexts.Add($"Account: {cfg.Accounts.FirstOrDefault(a => a.Id == account)?.Name ?? account}");
            if (_routeContextId is { } routeId) contexts.Add($"Model access: {cfg.Routes.FirstOrDefault(r => r.Id == routeId)?.Alias ?? routeId}");
            if (_routeModel is { } model) contexts.Add($"Model: {model}");
            contextText.Text = string.Join(" · ", contexts);
            context.IsVisible = contexts.Count > 0;
            var found = cfg.Routes.Where(r => (_routeProvider == "All providers" || r.Provider == _routeProvider)
                && (_routeVisibility == "All visibility" || r.Visible == (_routeVisibility == "Visible"))
                && (_routeState == "All states" || r.Enabled == (_routeState == "Enabled"))
                && (_routeAccountId is null || r.AccountIds.Contains(_routeAccountId, StringComparer.Ordinal))
                && (_routeContextId is null || r.Id == _routeContextId)
                && (_routeModel is null || r.Alias == _routeModel || r.UpstreamModel == _routeModel)
                && $"{r.Alias} {r.UpstreamModel} {r.Provider} {AccountNames(r.AccountIds)}".Contains(_routeSearch.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.Alias, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Id, StringComparer.Ordinal).ToArray();
            count.Text = $"{found.Length} of {cfg.Routes.Length} configured model access entries · {cfg.Routes.Count(r => r.Visible && r.Enabled)} enabled and visible to clients";
            foreach (var route in found) rows.Children.Add(ModelAccessCard(cfg, route, presentation));
            if (found.Length == 0)
                rows.Children.Add(Empty(cfg.Routes.Length == 0
                    ? "No model access yet. Add a client name, provider model and account binding. Known model suggestions are available while adding."
                    : "No configured model access matches these filters. Clear the context or adjust the filters."));
        }
        search.TextChanged += (_, _) => { _routeSearch = search.Text ?? ""; Populate(); };
        provider.SelectionChanged += (_, _) => { _routeProvider = provider.SelectedItem as string ?? "All providers"; Populate(); };
        visibility.SelectionChanged += (_, _) => { _routeVisibility = visibility.SelectedItem as string ?? "All visibility"; Populate(); };
        state.SelectionChanged += (_, _) => { _routeState = state.SelectedItem as string ?? "All states"; Populate(); };
        Populate();
        return panel;
    }

    private Control ModelAccessCard(ConfigurationDto cfg, RouteDto route, PresentationOptions presentation)
    {
        var dense = presentation.Density == ViewDensity.Compact;
        var names = string.Join(", ", route.AccountIds.Select(id => cfg.Accounts.FirstOrDefault(a => a.Id == id)?.Name ?? id));
        var identity = Ui.Stack(Ui.Text($"Client name: {route.Alias}", 15), Ui.Text($"Provider model: {route.Provider} / {route.UpstreamModel}", 12, Ui.Muted),
            Ui.Text($"Account binding: {names} · {(route.Pooled ? "Explicit pool" : "Strict · one account")}", 12));
        identity.Spacing = dense ? 2 : 5;
        var policy = route.Policy?.CostPolicy ?? "configured";
        var state = Ui.Text($"{(route.Enabled ? "Enabled" : "Disabled")} · {(route.Visible ? "Visible in client model menu" : "Hidden from client model menu")} · " +
            (policy == "free_only" ? "Only free — no paid fallback" : policy == "configured" ? "Configured spending policy" : $"Spending policy: {policy}"), 12,
            route.Enabled ? Ui.Muted : Ui.Warning);
        var accounts = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var id in route.AccountIds)
        {
            var link = CompactRouteButton(cfg.Accounts.FirstOrDefault(a => a.Id == id)?.Name ?? id,
                () => NavigateTo(new WorkspaceNavigationRequest("Accounts", AccountId: id)));
            AutomationProperties.SetName(link, $"Open account {cfg.Accounts.FirstOrDefault(a => a.Id == id)?.Name ?? id}");
            link.Margin = new Thickness(0, 0, 6, 4);
            accounts.Children.Add(link);
        }
        var edit = CompactRouteButton("Edit", () => EditRoute(route));
        AutomationProperties.SetName(edit, $"Edit model access {route.Alias}");
        var usage = CompactRouteButton("Usage", () => NavigateTo(new WorkspaceNavigationRequest("Usage", RouteId: route.Id)));
        AutomationProperties.SetName(usage, $"Usage for model access {route.Alias}");
        var remove = CompactRouteButton("Remove…", () => ConfirmRemoval(route.Alias, async () => { _configuration = await _client.RemoveRouteAsync(cfg.Revision, route.Id, _lifetime.Token); }));
        AutomationProperties.SetName(remove, $"Remove model access {route.Alias}");
        var detail = Ui.Stack(Ui.Text("Bound accounts", 12, Ui.Muted), accounts,
            Ui.Text($"Provider route provenance: {route.ProviderRouteId ?? "Not classified"}", 12, Ui.Muted),
            Ui.Text($"Model access ID: {route.Id}", 11, Ui.Muted),
            Ui.Text($"Used by {cfg.Profiles.Count(p => p.RouteIds.Contains(route.Id))} saved profiles", 12, Ui.Muted));
        if (route.Policy?.FallbackRouteIds is { Length: > 0 } fallbacks)
            detail.Children.Add(Ui.Text($"Saved fallback selections: {string.Join(", ", fallbacks.Select(id => cfg.Routes.FirstOrDefault(r => r.Id == id)?.Alias ?? id))} · Paid fallback permission: {(route.Policy.AllowPaidFallback ? "allowed" : "not allowed")}", 12, Ui.Muted));
        detail.Children.Add(ProfileWrap(edit, usage, remove));
        var expanded = _modelDetails.TryGetValue(route.Id, out var chosen) ? chosen : presentation.Level == InformationLevel.Advanced;
        var details = new Expander { Header = "Details", Content = detail, IsExpanded = expanded, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(details, $"Details for model access {route.Alias}");
        details.PropertyChanged += (_, e) => { if (e.Property == Expander.IsExpandedProperty) _modelDetails[route.Id] = details.IsExpanded; };
        var body = Ui.Stack(identity, state, details);
        body.Spacing = dense ? 5 : 9;
        var card = Ui.Card(body);
        card.Padding = new Thickness(dense ? 10 : 14);
        if (_routeContextId == route.Id) card.BorderBrush = Ui.Accent;
        return card;
    }

    private static Button CompactRouteButton(string label, Action click)
    {
        var button = Ui.Button(label, click);
        button.Padding = new Thickness(9, 3); button.MinHeight = 28;
        return button;
    }

    private void EditRoute(RouteDto? route, ModelEntryDto? catalogModel = null)
    {
        if (_configuration is not { } cfg) return;
        var routeId = route?.Id;
        RegisterEditor(() =>
        {
            if (routeId is null) { EditRoute(null); return; }
            var latest = _configuration?.Routes.FirstOrDefault(item => item.Id == routeId)
                ?? throw new InvalidOperationException("This model access was removed from the latest configuration. Cancel this draft and review Models.");
            EditRoute(latest);
        }, cfg.Revision);
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
            spending.Control, policyCheck, policyStatus,
            Ui.Text("Checks use the saved route. Save changes to its model or account binding before checking again. Configuration eligibility does not prove live access or a fulfilled spending cap.", 11, Ui.Muted));
        binding.Spacing = 10;
        var form = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 24 };
        form.Children.Add(identity); Grid.SetColumn(binding, 1); form.Children.Add(binding);
        var save = Action("Save model access", async () => {
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
                Policy = spending.Resolve()
            };
            _configuration = await _client.SaveRouteAsync(cfg.Revision, saved, _lifetime.Token);
        }, "Model access saved.");
        save.Classes.Add("primary");
        _page.Content = Ui.Stack(Heading(route is null ? "Add model access" : $"Edit model access {route.Alias}", "Choose the client name, provider model and account binding."),
            new Border { BorderBrush = Ui.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(18), Child = form },
            Ui.Row(save, Ui.Button("Cancel", () => { _editing = false; Render(); })));
    }
    private sealed class SpendingPolicyEditor
    {
        private readonly RoutingPolicyDto? _original;
        private readonly int _initial;
        private readonly ComboBox _choice;
        public Control Control { get; }
        public string Code => _choice.SelectedIndex switch { 0 => "configured", 1 => "free_only", _ => _original?.CostPolicy ?? "configured" };
        public SpendingPolicyEditor(RoutingPolicyDto? original)
        {
            _original = original;
            _initial = original?.CostPolicy switch { "free_only" => 1, null or "configured" => 0, _ => 2 };
            var labels = new List<string> { "Use configured access", "Only free — no paid fallback" };
            if (_initial == 2) labels.Add("Keep existing policy");
            _choice = new ComboBox { ItemsSource = labels, SelectedIndex = _initial, HorizontalAlignment = HorizontalAlignment.Stretch };
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
