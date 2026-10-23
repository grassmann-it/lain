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
    private Control RoutesPage()
    {
        var panel = Ui.Stack(Heading("Model routes", "Choose the actual upstream model and the account allowed to serve it."), Ui.Button("Create route", () => EditRoute(null)));
        if (_configuration is not { } cfg) { panel.Children.Add(Empty("Connect to the host to manage routes.")); return panel; }
        if (cfg.Routes.Length == 0) panel.Children.Add(Empty("No routes yet. Add an account, then create a route for its model."));
        foreach (var route in cfg.Routes)
        {
            var accounts = string.Join(", ", route.AccountIds.Select(id => cfg.Accounts.FirstOrDefault(a => a.Id == id)?.Name ?? id));
            panel.Children.Add(Ui.Card(Ui.Stack(Ui.Text(route.Alias, 20), Ui.Text($"{route.Provider} / {route.UpstreamModel}", 13, Ui.Muted), Ui.Text($"{(route.Pooled ? "Explicit pool" : "Strict account")} · {accounts} · {(route.Visible ? "Visible" : "Hidden")} · {(route.Enabled ? "Enabled" : "Disabled")}", 12, Ui.Muted), Ui.Row(Ui.Button("Edit", () => EditRoute(route)), Ui.Button("Remove…", () => ConfirmRemoval(route.Alias, async () => { _configuration = await _client.RemoveRouteAsync(cfg.Revision, route.Id, _lifetime.Token); }))))));
        }
        return panel;
    }

    private void EditRoute(RouteDto? route)
    {
        if (_configuration is not { } cfg) return;
        _editing = true;
        var alias = Ui.Input(route?.Alias, "client-facing-model"); var model = Ui.Input(route?.UpstreamModel, "Upstream model ID"); var provider = Ui.Input(route?.Provider, "Provider identifier");
        var enabled = new CheckBox { Content = "Enabled", IsChecked = route?.Enabled ?? true };
        var visible = new CheckBox { Content = "Visible in the client model menu", IsChecked = route?.Visible ?? true };
        var pooled = new CheckBox { Content = "Explicit pool (allows these accounts to serve the route)", IsChecked = route?.Pooled ?? false };
        var choices = cfg.Accounts.Select(a => (Account: a, Box: new CheckBox { Content = $"{a.Name} · {a.Provider}", IsChecked = route?.AccountIds.Contains(a.Id) ?? false })).ToArray();
        var accounts = Ui.Stack(choices.Select(x => (Control)x.Box).ToArray());
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
        _page.Content = Ui.Stack(Heading(route is null ? "Create a route" : "Edit route", "A strict route uses exactly one account. Multiple accounts require an explicit pool."),
            Ui.Card(Ui.Stack(Ui.Field("Known models (source shown; manual entry is also available)", catalog), Ui.Field("Model alias", alias), Ui.Field("Upstream model", model), Ui.Field("Provider", provider), Ui.Field("Allowed accounts", accounts), pooled, enabled, visible)),
            Ui.Row(Action("Save route", async () => {
                var ids = choices.Where(x => x.Box.IsChecked == true).Select(x => x.Account.Id).ToArray();
                if (string.IsNullOrWhiteSpace(alias.Text) || string.IsNullOrWhiteSpace(model.Text) || string.IsNullOrWhiteSpace(provider.Text) || ids.Length == 0) throw new InvalidOperationException("Enter an alias, upstream model, provider and at least one account.");
                if (ids.Length > 1 && pooled.IsChecked != true) throw new InvalidOperationException("Select one account or explicitly enable the pool.");
                _configuration = await _client.SaveRouteAsync(cfg.Revision, new RouteDto(route?.Id ?? Guid.NewGuid().ToString("N"), alias.Text.Trim(), model.Text.Trim(), provider.Text.Trim(), ids, pooled.IsChecked == true, visible.IsChecked == true, enabled.IsChecked == true), _lifetime.Token);
            }, "Route saved."), Ui.Button("Cancel", () => { _editing = false; Render(); })));
    }

}
