using Avalonia;
using Avalonia.Controls;
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
        (_showModelCatalog ? catalogTab : routesTab).Background = Brush.Parse("#263650");
        panel.Children.Add(Ui.Row(routesTab, catalogTab));

        var search = Ui.Input(_routeSearch, "Search aliases, models, providers or accounts");
        var providers = new[] { "All providers" }.Concat(cfg.Routes.Select(r => r.Provider).Concat(_models.Select(m => m.Provider)).Distinct().OrderBy(p => p, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (!providers.Contains(_routeProvider)) _routeProvider = providers[0];
        var provider = new ComboBox { ItemsSource = providers, SelectedItem = _routeProvider, MinWidth = 132 };
        var visibility = new ComboBox { ItemsSource = new[] { "All visibility", "Visible", "Hidden" }, SelectedItem = _routeVisibility, MinWidth = 116, IsVisible = !_showModelCatalog };
        var state = new ComboBox { ItemsSource = new[] { "All states", "Enabled", "Disabled" }, SelectedItem = _routeState, MinWidth = 104, IsVisible = !_showModelCatalog };
        var filters = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = 8 };
        filters.Children.Add(search); Grid.SetColumn(provider, 1); filters.Children.Add(provider);
        Grid.SetColumn(visibility, 2); filters.Children.Add(visibility); Grid.SetColumn(state, 3); filters.Children.Add(state);
        panel.Children.Add(filters);

        var count = Ui.Text("", 12, Ui.Muted);
        var rows = new StackPanel { Spacing = 0 };
        var table = new Border { BorderBrush = Ui.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = rows };
        panel.Children.Add(count);
        panel.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = table });
        void Populate()
        {
            rows.Children.Clear();
            bool Matches(string value) => value.Contains(_routeSearch.Trim(), StringComparison.OrdinalIgnoreCase);
            string Accounts(string[] ids) => string.Join(", ", ids.Select(id => cfg.Accounts.FirstOrDefault(a => a.Id == id)?.Name ?? id));
            if (_showModelCatalog)
            {
                rows.Children.Add(RouteTableHeader("Model ID", "Provider", "Available accounts", "Source", ""));
                var found = _models.Where(m => (_routeProvider == "All providers" || m.Provider == _routeProvider)
                    && Matches($"{m.Id} {m.Provider} {m.Source} {Accounts(m.AccountIds)}")).OrderBy(m => m.Provider).ThenBy(m => m.Id).ToArray();
                count.Text = $"{found.Length} of {_models.Length} models · Choose a model to create a route. Catalog entries do not expose a client alias automatically.";
                foreach (var model in found)
                {
                    var add = CompactRouteButton("Use model", () => EditRoute(null, model));
                    rows.Children.Add(RouteTableRow(RouteCell(model.Id, false, true), RouteCell(model.Provider), RouteCell(Accounts(model.AccountIds)), RouteCell(model.Source, true), add));
                }
                if (found.Length == 0) rows.Children.Add(RouteEmptyRow(_models.Length == 0 ? "No model catalog is available. You can still create a route by entering its model ID." : "No models match these filters."));
            }
            else
            {
                rows.Children.Add(RouteTableHeader("Alias / upstream model", "Provider", "Account binding", "Visibility / state", ""));
                var found = cfg.Routes.Where(r => (_routeProvider == "All providers" || r.Provider == _routeProvider)
                    && (_routeVisibility == "All visibility" || r.Visible == (_routeVisibility == "Visible"))
                    && (_routeState == "All states" || r.Enabled == (_routeState == "Enabled"))
                    && Matches($"{r.Alias} {r.UpstreamModel} {r.Provider} {Accounts(r.AccountIds)}")).OrderBy(r => r.Alias, StringComparer.OrdinalIgnoreCase).ToArray();
                count.Text = $"{found.Length} of {cfg.Routes.Length} routes · {cfg.Routes.Count(r => r.Visible && r.Enabled)} enabled and visible in the client menu";
                foreach (var route in found)
                {
                    var identity = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
                    identity.Children.Add(RouteCell(route.Alias, false, true)); identity.Children.Add(RouteCell(route.UpstreamModel, true));
                    var binding = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
                    binding.Children.Add(RouteCell(Accounts(route.AccountIds)));
                    binding.Children.Add(RouteCell(route.Pooled ? $"Explicit pool · {route.AccountIds.Length} accounts" : "Strict · one account", true));
                    var status = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
                    status.Children.Add(RouteCell(route.Visible ? "Visible" : "Hidden", !route.Visible));
                    status.Children.Add(RouteCell(route.Enabled ? "Enabled" : "Disabled", true));
                    var edit = CompactRouteButton("Edit", () => EditRoute(route));
                    var remove = CompactRouteButton("×", () => ConfirmRemoval(route.Alias, async () => { _configuration = await _client.RemoveRouteAsync(cfg.Revision, route.Id, _lifetime.Token); }));
                    ToolTip.SetTip(remove, $"Remove {route.Alias}");
                    Avalonia.Automation.AutomationProperties.SetName(remove, $"Remove route {route.Alias}");
                    var actions = Ui.Row(edit, remove); actions.Spacing = 4; actions.VerticalAlignment = VerticalAlignment.Center;
                    rows.Children.Add(RouteTableRow(identity, RouteCell(route.Provider), binding, status, actions));
                }
                if (found.Length == 0) rows.Children.Add(RouteEmptyRow(cfg.Routes.Length == 0 ? "No routes yet. Create a route to give a model a client alias and an account binding." : "No routes match these filters."));
            }
        }
        search.TextChanged += (_, _) => { _routeSearch = search.Text ?? ""; Populate(); };
        provider.SelectionChanged += (_, _) => { _routeProvider = provider.SelectedItem as string ?? "All providers"; Populate(); };
        visibility.SelectionChanged += (_, _) => { _routeVisibility = visibility.SelectedItem as string ?? "All visibility"; Populate(); };
        state.SelectionChanged += (_, _) => { _routeState = state.SelectedItem as string ?? "All states"; Populate(); };
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

    private static Border RouteTableRow(params Control[] cells)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2.1*,110,1.7*,118,105"), ColumnSpacing = 14, MinWidth = 780 };
        for (var i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); grid.Children.Add(cells[i]); }
        return new Border { Padding = new Thickness(14, 7), MinHeight = 49, BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
    }

    private static Border RouteTableHeader(params string[] titles)
    {
        var header = RouteTableRow(titles.Select(t => (Control)RouteCell(t, true)).ToArray());
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
        var identity = Ui.Stack(Ui.Text("Model identity", 14), Ui.Field("Model catalog", catalog), Ui.Field("Client alias", alias), Ui.Field("Upstream model ID", model), Ui.Field("Provider", provider));
        identity.Spacing = 10;
        var binding = Ui.Stack(Ui.Text("Account binding", 14), Ui.Text("A strict route uses one account. Enable an explicit pool to allow several accounts.", 12, Ui.Muted), pooled, accountCount,
            new ScrollViewer { MaxHeight = 260, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = accounts },
            new Border { Height = 1, Background = Ui.Edge }, enabled, visible);
        binding.Spacing = 10;
        var form = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 24 };
        form.Children.Add(identity); Grid.SetColumn(binding, 1); form.Children.Add(binding);
        var save = Action("Save route", async () => {
            var ids = choices.Where(x => x.Box.IsChecked == true).Select(x => x.Account.Id).ToArray();
            if (string.IsNullOrWhiteSpace(alias.Text) || string.IsNullOrWhiteSpace(model.Text) || string.IsNullOrWhiteSpace(provider.Text) || ids.Length == 0) throw new InvalidOperationException("Enter an alias, upstream model, provider and at least one account.");
            if (ids.Length > 1 && pooled.IsChecked != true) throw new InvalidOperationException("Select one account or explicitly enable the pool.");
            _configuration = await _client.SaveRouteAsync(cfg.Revision, new RouteDto(route?.Id ?? Guid.NewGuid().ToString("N"), alias.Text.Trim(), model.Text.Trim(), provider.Text.Trim(), ids, pooled.IsChecked == true, visible.IsChecked == true, enabled.IsChecked == true), _lifetime.Token);
        }, "Route saved.");
        save.Classes.Add("primary");
        _page.Content = Ui.Stack(Heading(route is null ? "Create route" : $"Edit {route.Alias}", "Choose the alias clients see and the upstream account binding."),
            new Border { BorderBrush = Ui.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(18), Child = form },
            Ui.Row(save, Ui.Button("Cancel", () => { _editing = false; Render(); })));
    }
}
