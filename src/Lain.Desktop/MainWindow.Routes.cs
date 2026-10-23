using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Lain.Client;
using Lain.Core;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private string _routeSearch = "";
    private string _routeProvider = "All providers";
    private string _routeVisibility = "All visibility";
    private string _routeState = "All states";
    private bool _showModelCatalog;

    private Control RoutesPage()
    {
        var create = Ui.Button("Create route", () => EditRoute(null));
        create.Classes.Add("primary");
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(Heading("Model routes", "Client aliases, upstream models and the accounts that serve them."));
        Grid.SetColumn(create, 1); create.VerticalAlignment = VerticalAlignment.Top; header.Children.Add(create);
        var panel = Ui.Stack(header);
        if (_configuration is not { } cfg) { panel.Children.Add(Empty("Connect to the host to manage routes.")); return panel; }

        var routesTab = Ui.Button($"Routes · {cfg.Routes.Length}", () => { _showModelCatalog = false; Render(); });
        var catalogTab = Ui.Button($"Model catalog · {_models.Length}", () => { _showModelCatalog = true; Render(); });
        (_showModelCatalog ? catalogTab : routesTab).Background = Ui.AccentSurface;
        panel.Children.Add(Ui.Row(routesTab, catalogTab));

        var search = Ui.Input(_routeSearch, "Search aliases, models, providers or accounts");
        var providers = new[] { "All providers" }.Concat(cfg.Routes.Select(r => r.Provider).Concat(_models.Select(m => m.Provider)).Distinct().OrderBy(p => p, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (!providers.Contains(_routeProvider)) _routeProvider = providers[0];
        var provider = new ComboBox { ItemsSource = providers, SelectedItem = _routeProvider, MinWidth = 132 };
        var visibility = new ComboBox { ItemsSource = new[] { "All visibility", "Visible", "Hidden" }, SelectedItem = _routeVisibility, MinWidth = 116, IsVisible = !_showModelCatalog };
        var state = new ComboBox { ItemsSource = new[] { "All states", "Enabled", "Disabled" }, SelectedItem = _routeState, MinWidth = 104, IsVisible = !_showModelCatalog };
        Avalonia.Automation.AutomationProperties.SetName(search, "Search model routes and catalog");
        Avalonia.Automation.AutomationProperties.SetName(provider, "Filter by provider");
        Avalonia.Automation.AutomationProperties.SetName(visibility, "Filter route visibility");
        Avalonia.Automation.AutomationProperties.SetName(state, "Filter route state");
        var filters = new StackPanel { Spacing = 8 };
        filters.Children.Add(search);
        filters.Children.Add(Ui.Row(provider, visibility, state));
        panel.Children.Add(filters);

        var count = Ui.Text("", 12, Ui.Muted);
        var rows = new StackPanel { Spacing = 0 };
        var table = new Border { BorderBrush = Ui.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = rows };
        panel.Children.Add(count);
        panel.Children.Add(table);
        var compact = _page.Bounds.Width < 820;
        void Populate()
        {
            rows.Children.Clear();
            bool Matches(string value) => value.Contains(_routeSearch.Trim(), StringComparison.OrdinalIgnoreCase);
            string Accounts(string[] ids) => string.Join(", ", ids.Select(id => cfg.Accounts.FirstOrDefault(a => a.Id == id)?.Name ?? id));
            if (_showModelCatalog)
            {
                rows.Children.Add(RouteTableHeader(compact, compact ? "Model / provider" : "Model ID", "Provider", "Available accounts", "Source", ""));
                var found = _models.Where(m => (_routeProvider == "All providers" || m.Provider == _routeProvider)
                    && Matches($"{m.Id} {m.Provider} {m.Source} {Accounts(m.AccountIds)}")).OrderBy(m => m.Provider).ThenBy(m => m.Id).ToArray();
                count.Text = $"{found.Length} of {_models.Length} models · Choose a model to create a route. Catalog entries do not expose a client alias automatically.";
                foreach (var model in found)
                {
                    var add = CompactRouteButton("Use model", () => EditRoute(null, model));
                    Avalonia.Automation.AutomationProperties.SetName(add, $"Create route for {model.Id}");
                    Control identity = RouteCell(model.Id, false, true);
                    if (compact)
                    {
                        var label = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
                        label.Children.Add(identity); label.Children.Add(RouteCell(model.Provider, true)); identity = label;
                    }
                    rows.Children.Add(RouteTableRow(compact, identity, RouteCell(model.Provider), RouteCell(Accounts(model.AccountIds)), RouteCell(model.Source, true), add));
                }
                if (found.Length == 0) rows.Children.Add(RouteEmptyRow(_models.Length == 0 ? "No model catalog is available. You can still create a route by entering its model ID." : "No models match these filters."));
            }
            else
            {
                rows.Children.Add(RouteTableHeader(compact, "Alias / upstream model", "Provider", "Account binding", "Visibility / state", ""));
                var found = cfg.Routes.Where(r => (_routeProvider == "All providers" || r.Provider == _routeProvider)
                    && (_routeVisibility == "All visibility" || r.Visible == (_routeVisibility == "Visible"))
                    && (_routeState == "All states" || r.Enabled == (_routeState == "Enabled"))
                    && Matches($"{r.Alias} {r.UpstreamModel} {r.Provider} {Accounts(r.AccountIds)}")).OrderBy(r => r.Alias, StringComparer.OrdinalIgnoreCase).ToArray();
                count.Text = $"{found.Length} of {cfg.Routes.Length} routes · {cfg.Routes.Count(r => r.Visible && r.Enabled)} enabled and visible in the client menu";
                foreach (var route in found)
                {
                    var identity = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
                    identity.Children.Add(RouteCell(route.Alias, false, true)); identity.Children.Add(RouteCell(route.UpstreamModel, true));
                    if (compact) identity.Children.Add(RouteCell(route.Provider, true));
                    var binding = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
                    binding.Children.Add(RouteCell(Accounts(route.AccountIds)));
                    binding.Children.Add(RouteCell(route.Pooled ? $"Explicit pool · {route.AccountIds.Length} accounts" : "Strict · one account", true));
                    var status = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
                    status.Children.Add(RouteCell(route.Visible ? "Visible" : "Hidden", !route.Visible));
                    status.Children.Add(RouteCell(route.Enabled ? "Enabled" : "Disabled", true));
                    status.Children.Add(RouteCell(route.Policy?.CostPolicy == "free_only" ? "Only free" : "Configured", true));
                    var edit = CompactRouteButton("Edit", () => EditRoute(route));
                    Avalonia.Automation.AutomationProperties.SetName(edit, $"Edit route {route.Alias}");
                    var remove = CompactRouteButton("×", () => ConfirmRemoval(route.Alias, async () => { _configuration = await _client.RemoveRouteAsync(cfg.Revision, route.Id, _lifetime.Token); }));
                    ToolTip.SetTip(remove, $"Remove {route.Alias}");
                    Avalonia.Automation.AutomationProperties.SetName(remove, $"Remove route {route.Alias}");
                    var actions = Ui.Row(edit, remove); actions.Spacing = 4; actions.VerticalAlignment = VerticalAlignment.Center;
                    rows.Children.Add(RouteTableRow(compact, identity, RouteCell(route.Provider), binding, status, actions));
                }
                if (found.Length == 0) rows.Children.Add(RouteEmptyRow(cfg.Routes.Length == 0 ? "No routes yet. Create a route to give a model a client alias and an account binding." : "No routes match these filters."));
            }
        }
        search.TextChanged += (_, _) => { _routeSearch = search.Text ?? ""; Populate(); };
        provider.SelectionChanged += (_, _) => { _routeProvider = provider.SelectedItem as string ?? "All providers"; Populate(); };
        visibility.SelectionChanged += (_, _) => { _routeVisibility = visibility.SelectedItem as string ?? "All visibility"; Populate(); };
        state.SelectionChanged += (_, _) => { _routeState = state.SelectedItem as string ?? "All states"; Populate(); };
        panel.SizeChanged += (_, _) =>
        {
            var nextCompact = panel.Bounds.Width < 820;
            if (compact == nextCompact) return;
            compact = nextCompact;
            Populate();
        };
        Populate();
        return panel;
    }

    private static TextBlock RouteCell(string text, bool muted = false, bool strong = false)
    {
        var cell = Ui.Text(text, muted ? 11 : 13, muted ? Ui.Muted : Ui.Ink);
        cell.TextWrapping = TextWrapping.NoWrap; cell.TextTrimming = TextTrimming.CharacterEllipsis;
        cell.VerticalAlignment = VerticalAlignment.Center;
        if (strong) cell.FontWeight = FontWeight.SemiBold;
        ToolTip.SetTip(cell, text);
        return cell;
    }

    private static Button CompactRouteButton(string label, Action click)
    {
        var button = Ui.Button(label, click);
        button.Padding = new Thickness(9, 3); button.MinHeight = 28; button.Height = 28;
        return button;
    }

    private static Border RouteTableRow(bool compact, params Control[] cells)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(compact ? "1.4*,0,1.1*,86,89" : "2.1*,110,1.7*,118,105"), ColumnSpacing = compact ? 6 : 14 };
        for (var i = 0; i < cells.Length; i++)
        {
            if (compact && i == 1) cells[i].IsVisible = false;
            Grid.SetColumn(cells[i], i); grid.Children.Add(cells[i]);
        }
        return new Border { Padding = new Thickness(14, 7), MinHeight = 49, BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
    }

    private static Border RouteTableHeader(bool compact, params string[] titles)
    {
        var header = RouteTableRow(compact, titles.Select(t => (Control)RouteCell(t, true)).ToArray());
        header.Background = Ui.Surface; header.MinHeight = 34; header.Padding = new Thickness(14, 8);
        return header;
    }

    private static Border RouteEmptyRow(string message) => new() { Padding = new Thickness(16, 22), Child = Ui.Text(message, 13, Ui.Muted) };

    private void EditRoute(RouteDto? route, ModelEntryDto? catalogModel = null)
    {
        if (_configuration is not { } cfg) return;
        _editing = true;
        var alias = Ui.Input(route?.Alias ?? catalogModel?.Id, "client-facing-model");
        var model = Ui.Input(route?.UpstreamModel ?? catalogModel?.Id, "Upstream model ID");
        var provider = Ui.Input(route?.Provider ?? catalogModel?.Provider, "Provider identifier");
        var providerRouteId = Ui.Input(route?.ProviderRouteId, "Optional concrete catalog route ID");
        var routeChoices = new ComboBox { PlaceholderText = "Load documented provider routes", HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(routeChoices, "Provider catalog route");
        var documentedRoutes = Array.Empty<(string Provider, ProviderRouteDescriptorDto Route)>();
        var loadChoices = Ui.Button("Load provider route choices", async () =>
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
        var selectedIds = route?.AccountIds ?? catalogModel?.AccountIds ?? [];
        var choices = cfg.Accounts.Select(a => (Account: a, Box: new CheckBox { Content = $"{a.Name} · {a.Provider}", IsChecked = selectedIds.Contains(a.Id), MinHeight = 30 })).ToArray();
        var accounts = new StackPanel { Spacing = 2 };
        foreach (var choice in choices) accounts.Children.Add(choice.Box);
        if (choices.Length == 0) accounts.Children.Add(Ui.Text("Add an account before saving a route.", 13, Ui.Muted));
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
        var identity = Ui.Stack(Ui.Text("Model identity", 14), Ui.Field("Model catalog", catalog), Ui.Field("Client alias", alias), Ui.Field("Upstream model ID", model), Ui.Field("Provider", provider),
            Ui.Field("Provider catalog route ID", providerRouteId), routeChoices, loadChoices,
            Ui.Text("Selecting a catalog route updates its model and provider. Existing provenance stays unchanged unless you edit it.", 11, Ui.Muted));
        identity.Spacing = 10;
        var binding = Ui.Stack(Ui.Text("Account binding", 14), Ui.Text("A strict route uses one account. Enable an explicit pool to allow several accounts.", 12, Ui.Muted), pooled, accountCount,
            new ScrollViewer { MaxHeight = 260, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = accounts },
            new Border { Height = 1, Background = Ui.Edge }, enabled, visible,
            spending.Control, policyCheck, policyStatus,
            Ui.Text("Checks use the saved route. Save changes to its model or account binding before checking again. Configuration eligibility does not prove live access or a fulfilled spending cap.", 11, Ui.Muted));
        binding.Spacing = 10;
        var form = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 24 };
        form.Children.Add(identity); Grid.SetColumn(binding, 1); form.Children.Add(binding);
        var save = Action("Save route", async () => {
            var ids = choices.Where(x => x.Box.IsChecked == true).Select(x => x.Account.Id).ToArray();
            if (string.IsNullOrWhiteSpace(alias.Text) || string.IsNullOrWhiteSpace(model.Text) || string.IsNullOrWhiteSpace(provider.Text) || ids.Length == 0) throw new InvalidOperationException("Enter an alias, upstream model, provider and at least one account.");
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
        }, "Route saved.");
        save.Classes.Add("primary");
        _page.Content = Ui.Stack(Heading(route is null ? "Create route" : $"Edit {route.Alias}", "Choose the alias clients see and the upstream account binding."),
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
