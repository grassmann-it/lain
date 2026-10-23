using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lain.Desktop.Providers;

/// <summary>Read-only provider discovery and setup guidance; account actions belong to the shell.</summary>
public sealed partial class ProviderCatalogView : UserControl
{
    private readonly Action<Uri>? _openLink;
    private readonly TextBox _search = Ui.Input(hint: "Search providers, protocols or models");
    private readonly ComboBox _billing = new() { ItemsSource = BillingFilters, SelectedIndex = 0, MinWidth = 174 };
    private readonly ComboBox _setup = new() { ItemsSource = SetupFilters, SelectedIndex = 0, MinWidth = 174 };
    private readonly TextBlock _count = Ui.Text("Loading provider catalog…", 12, Ui.Muted);
    private readonly StackPanel _cards = new() { Spacing = 10 };
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private CatalogCard[] _catalog = [];
    private bool _loaded;
    private static readonly string[] BillingFilters = ["All access types", "Ongoing free", "One-time trial", "Subscription", "Pay as you go", "Unknown pricing"];
    private static readonly string[] SetupFilters = ["All providers", "No account configured", "Has configured accounts"];

    public ProviderCatalogView(Action<Uri>? openLink = null)
    {
        _openLink = openLink;
        AutomationProperties.SetName(this, "Provider catalog");
        AutomationProperties.SetName(_search, "Search providers");
        AutomationProperties.SetName(_billing, "Filter provider access type");
        AutomationProperties.SetName(_setup, "Filter provider setup state");
        var filterRow = new WrapPanel { Orientation = Orientation.Horizontal };
        _billing.Margin = new Thickness(0, 0, 8, 6);
        _setup.Margin = new Thickness(0, 0, 8, 6);
        filterRow.Children.Add(_billing);
        filterRow.Children.Add(_setup);
        var filters = new StackPanel { Spacing = 8, Children = { _search, filterRow } };
        Content = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                Ui.PageHeader("Providers", "Official access, credentials and setup guidance for your own accounts."),
                Ui.Text("A configured account is not a verified connection. Budget values stay unknown until reported by a supported source.", 12, Ui.Muted),
                filters, _count, _cards
            }
        };
        _search.TextChanged += (_, _) => Populate();
        _billing.SelectionChanged += (_, _) => Populate();
        _setup.SelectionChanged += (_, _) => Populate();
    }

    /// <summary>Keep a failed fetch visible without implying that provider credentials are invalid.</summary>
    public void ShowUnavailable(string? message = null)
    {
        _loaded = false;
        _catalog = [];
        _cards.Children.Clear();
        _count.Text = "Provider catalog unavailable";
        _cards.Children.Add(Ui.Card(Ui.Stack(
            Ui.Text("The catalog could not be loaded.", 14),
            Ui.Text(string.IsNullOrWhiteSpace(message) ? "Refresh after the local host becomes available." : message, 12, Ui.Muted))));
    }

    private void ApplyCatalog(CatalogCard[] cards)
    {
        _catalog = cards;
        _loaded = true;
        Populate();
    }

    private void Populate()
    {
        if (!_loaded) return;
        var query = _search.Text?.Trim() ?? "";
        var billing = _billing.SelectedItem as string ?? BillingFilters[0];
        var setup = _setup.SelectedItem as string ?? SetupFilters[0];
        var found = _catalog.Where(p => p.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase)
            && (billing == BillingFilters[0] || p.AccessTypes.Contains(billing, StringComparer.Ordinal))
            && (setup == SetupFilters[0] || (p.AccountCount > 0) == (setup == SetupFilters[2]))).ToArray();
        _count.Text = $"{found.Length} of {_catalog.Length} providers · open a card for requirements, model capabilities and sources";
        _cards.Children.Clear();
        foreach (var provider in found) _cards.Children.Add(Card(provider));
        if (found.Length == 0)
            _cards.Children.Add(Ui.Card(Ui.Text(_catalog.Length == 0 ? "No provider descriptions are available yet." : "No providers match these filters.", 13, Ui.Muted)));
    }

    private Expander Card(CatalogCard provider)
    {
        var name = Ui.Text(provider.Name, 16);
        name.FontFamily = Ui.HeadingFont;
        name.FontWeight = FontWeight.SemiBold;
        var account = Ui.Text(provider.AccountCount > 0 ? $"{provider.AccountCount} account{(provider.AccountCount == 1 ? "" : "s")} configured" : "No account configured", 11, Ui.Muted);
        account.HorizontalAlignment = HorizontalAlignment.Right;
        var identity = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        identity.Children.Add(name);
        Grid.SetColumn(account, 1);
        identity.Children.Add(account);
        var tags = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var label in provider.AccessTypes.DefaultIfEmpty("Unknown pricing").Concat(provider.Protocols).Distinct(StringComparer.Ordinal))
        {
            var tag = Ui.Badge(label);
            tag.Margin = new Thickness(0, 0, 6, 3);
            tags.Children.Add(tag);
        }
        var heading = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Stretch, Children = { identity, Ui.Text(provider.Summary, 12, Ui.Muted), tags } };
        var details = new StackPanel { Spacing = 15 };
        details.Children.Add(Facts("Access and credentials", provider.Access));
        details.Children.Add(Facts("Billing and budgets", provider.Budgets));
        details.Children.Add(Facts("Limits and scope", provider.Limits));
        if (provider.Routes.Length > 0)
        {
            var routes = new StackPanel { Spacing = 7 };
            foreach (var route in provider.Routes)
            {
                var title = Ui.Text(route.Name, 13);
                title.FontWeight = FontWeight.SemiBold;
                routes.Children.Add(Ui.Card(new StackPanel { Spacing = 7, Children = { title, FactRows(route.Facts) } }));
            }
            details.Children.Add(Ui.Section("Protocols and model capabilities", routes, "Capabilities belong to the concrete route. Unreported capabilities remain unknown."));
        }
        else details.Children.Add(Ui.Section("Protocols and model capabilities", Ui.Text("No verified route capability matrix is available yet.", 12, Ui.Muted)));
        if (provider.Steps.Length > 0)
        {
            var steps = new StackPanel { Spacing = 8 };
            for (var i = 0; i < provider.Steps.Length; i++) steps.Children.Add(Ui.Text($"{i + 1}. {provider.Steps[i]}", 12));
            details.Children.Add(Ui.Section("Setup", steps));
        }
        if (provider.Notes.Length > 0)
            details.Children.Add(Ui.Section("Before you connect", new StackPanel { Spacing = 6, Children = { Ui.Text(string.Join("\n\n", provider.Notes), 12, Ui.Muted) } }));
        var links = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var link in provider.Links)
        {
            if (!Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) continue;
            var button = Ui.Button(link.Label, () => _openLink?.Invoke(uri));
            button.IsEnabled = _openLink is not null;
            button.Margin = new Thickness(0, 0, 8, 6);
            ToolTip.SetTip(button, uri.AbsoluteUri);
            AutomationProperties.SetName(button, $"{provider.Name}: {link.Label}");
            links.Children.Add(button);
        }
        var sources = new StackPanel { Spacing = 6, Children = { Ui.Text(provider.Verification, 12, Ui.Muted), links } };
        details.Children.Add(Ui.Section("Official sources", sources));
        var expander = new Expander
        {
            Header = heading, Content = details,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            BorderBrush = Ui.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            IsExpanded = _expanded.Contains(provider.Id)
        };
        expander.Classes.Add("account-tile");
        if (Application.Current?.TryFindResource("CardExpander", out var theme) == true && theme is Avalonia.Styling.ControlTheme controlTheme)
            expander.Theme = controlTheme;
        expander.Resources["ExpanderHeaderPadding"] = new Thickness(12);
        expander.Resources["ExpanderHeaderMargin"] = new Thickness(0);
        expander.Resources["ExpanderContentPadding"] = new Thickness(12, 0, 12, 12);
        expander.Resources["ExpanderContentMinLength"] = 0d;
        AutomationProperties.SetName(expander, $"Provider {provider.Name}");
        expander.PropertyChanged += (_, change) =>
        {
            if (change.Property != Expander.IsExpandedProperty) return;
            if (expander.IsExpanded) _expanded.Add(provider.Id); else _expanded.Remove(provider.Id);
        };
        return expander;
    }

    private static Control Facts(string title, CatalogFact[] facts) => Ui.Section(title,
        facts.Length == 0 ? Ui.Text("Not reported", 12, Ui.Muted) : FactRows(facts));

    private static StackPanel FactRows(CatalogFact[] facts)
    {
        var rows = new StackPanel { Spacing = 8 };
        foreach (var fact in facts)
        {
            var row = new StackPanel { Spacing = 3 };
            row.Children.Add(Ui.Text(fact.Label, 11, Ui.Muted));
            row.Children.Add(Ui.Text(string.IsNullOrWhiteSpace(fact.Value) ? "Unknown" : fact.Value, 12));
            rows.Children.Add(row);
        }
        return rows;
    }

    private sealed record CatalogCard(string Id, string Name, string Summary, string SearchText, int AccountCount,
        string[] AccessTypes, string[] Protocols, CatalogFact[] Access, CatalogFact[] Budgets, CatalogFact[] Limits,
        CatalogRoute[] Routes, string[] Steps, string[] Notes, CatalogLink[] Links, string Verification);
    private sealed record CatalogFact(string Label, string Value);
    private sealed record CatalogRoute(string Name, CatalogFact[] Facts);
    private sealed record CatalogLink(string Label, string Url);
}
