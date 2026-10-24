using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Lain.Desktop.Presentation;

namespace Lain.Desktop.Providers;

/// <summary>Read-only provider discovery and setup guidance; account actions belong to the shell.</summary>
public sealed partial class ProviderCatalogView : UserControl, IPresentationView
{
    private readonly Action<Uri>? _openLink;
    private readonly Action<string>? _beginSetup;
    private readonly TextBox _search = Ui.Input(hint: "Search providers, protocols or models");
    private readonly ComboBox _billing = new() { ItemsSource = BillingFilters, SelectedIndex = 0, MinWidth = 174 };
    private readonly ComboBox _setup = new() { ItemsSource = SetupFilters, SelectedIndex = 0, MinWidth = 174 };
    private readonly TextBlock _count = Ui.Text("Loading provider catalog…", 12, Ui.Muted);
    private readonly StackPanel _cards = new() { Spacing = 10 };
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private CatalogCard[] _catalog = [];
    private bool _loaded;
    private PresentationOptions _presentation = new();
    private readonly List<(string Id, Expander Expander)> _detailExpanders = [];
    private readonly List<Border> _cardBorders = [];
    private readonly HashSet<string> _detailOverrides = new(StringComparer.Ordinal);
    private bool _applyingPresentation;
    private static readonly string[] BillingFilters = ["All access types", "Free access", "One-time trial", "Subscription", "Pay as you go", "Unknown pricing"];
    private static readonly string[] SetupFilters = ["All providers", "No account configured", "Has configured accounts"];

    public ProviderCatalogView(Action<Uri>? openLink = null, Action<string>? beginSetup = null)
    {
        _openLink = openLink;
        _beginSetup = beginSetup;
        AutomationProperties.SetName(this, "Discover providers");
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
                Ui.PageHeader("Discover providers", "Compare providers and access requirements before adding an account. Credentials are not needed to browse."),
                Ui.Text("Documentation, prepared adapters and live verification are shown separately. A saved account does not prove a working connection.", 12, Ui.Muted),
                filters, _count, _cards
            }
        };
        _search.TextChanged += (_, _) => Populate();
        _billing.SelectionChanged += (_, _) => Populate();
        _setup.SelectionChanged += (_, _) => Populate();
    }

    /// <summary>Change defaults in place so filtering, local details and keyboard focus survive.</summary>
    public void SetPresentation(PresentationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _presentation = options;
        _cards.Spacing = options.Density == ViewDensity.Compact ? 6 : 10;
        foreach (var card in _cardBorders) card.Padding = new Thickness(options.Density == ViewDensity.Compact ? 10 : 16);
        _applyingPresentation = true;
        try
        {
            foreach (var (id, expander) in _detailExpanders)
                if (!_detailOverrides.Contains(id) && !expander.IsKeyboardFocusWithin)
                    expander.IsExpanded = options.Level == InformationLevel.Advanced;
        }
        finally { _applyingPresentation = false; }
    }

    /// <summary>Keep a failed fetch visible without implying that provider credentials are invalid.</summary>
    public void ShowUnavailable(string? message = null)
    {
        _loaded = false;
        _catalog = [];
        _cards.Children.Clear();
        _detailExpanders.Clear();
        _cardBorders.Clear();
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
        _detailExpanders.Clear();
        _cardBorders.Clear();
        foreach (var provider in found) _cards.Children.Add(Card(provider));
        if (found.Length == 0)
            _cards.Children.Add(Ui.Card(Ui.Text(_catalog.Length == 0 ? "No provider descriptions are available yet." : "No providers match these filters.", 13, Ui.Muted)));
    }

    private Control Card(CatalogCard provider)
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
        var readiness = Ui.Badge(provider.Readiness, provider.LiveVerified ? "success" : null);
        readiness.Margin = new Thickness(0, 0, 6, 3);
        tags.Children.Add(readiness);
        foreach (var label in provider.AccessTypes.DefaultIfEmpty("Unknown pricing").Concat(provider.Protocols).Distinct(StringComparer.Ordinal))
        {
            var tag = Ui.Badge(label);
            tag.Margin = new Thickness(0, 0, 6, 3);
            tags.Children.Add(tag);
        }
        var heading = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Stretch, Children = { identity, Ui.Text(provider.Summary, 12, Ui.Muted), tags } };
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        AddLink(actions, provider, new CatalogLink("Official account / keys", provider.AccountUrl), primary: false);
        AddLink(actions, provider, new CatalogLink("Official documentation", provider.DocumentationUrl));
        var setup = Ui.Button("Add this provider account", () => _beginSetup?.Invoke(provider.Id));
        setup.IsEnabled = _beginSetup is not null;
        setup.Margin = new Thickness(0, 0, 8, 6);
        AutomationProperties.SetName(setup, $"Add {provider.Name} account");
        ToolTip.SetTip(setup, "Choose an available provider connection. Models follow optionally; saving is separate from live access.");
        setup.Classes.Add("primary");
        actions.Children.Insert(0, setup);
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
                var routeDetails = new Expander
                {
                    Header = title, Content = FactRows(route.Facts),
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
                };
                AutomationProperties.SetName(routeDetails, $"{provider.Name}: {route.Name} capabilities");
                routes.Children.Add(routeDetails);
            }
            details.Children.Add(Ui.Section("Protocols and model capabilities", routes, "Capabilities belong to the concrete connection / API endpoint. Unreported capabilities remain unknown."));
        }
        else details.Children.Add(Ui.Section("Protocols and model capabilities", Ui.Text("No verified connection capability matrix is available yet.", 12, Ui.Muted)));
        if (provider.Steps.Length > 0)
        {
            var steps = new StackPanel { Spacing = 8 };
            for (var i = 0; i < provider.Steps.Length; i++) steps.Children.Add(Ui.Text($"{i + 1}. {provider.Steps[i]}", 12));
            details.Children.Add(Ui.Section("Setup", steps));
        }
        if (provider.Notes.Length > 0)
            details.Children.Add(Ui.Section("Before you connect", new StackPanel { Spacing = 6, Children = { Ui.Text(string.Join("\n\n", provider.Notes), 12, Ui.Muted) } }));
        var links = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var link in provider.Links) AddLink(links, provider, link);
        var sources = new StackPanel { Spacing = 6, Children = { Ui.Text(provider.Verification, 12, Ui.Muted), links } };
        details.Children.Add(Ui.Section("Official sources", sources));
        var expander = new Expander
        {
            Header = Ui.Text("Setup, budgets and model capabilities", 12), Content = details,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            BorderBrush = Ui.Edge, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(4),
            IsExpanded = _expanded.Contains(provider.Id) || (!_detailOverrides.Contains(provider.Id) && _presentation.Level == InformationLevel.Advanced)
        };
        expander.Classes.Add("account-tile");
        if (Application.Current?.TryFindResource("CardExpander", out var theme) == true && theme is Avalonia.Styling.ControlTheme controlTheme)
            expander.Theme = controlTheme;
        expander.Resources["ExpanderHeaderPadding"] = new Thickness(12);
        expander.Resources["ExpanderHeaderMargin"] = new Thickness(0);
        expander.Resources["ExpanderContentPadding"] = new Thickness(12, 0, 12, 12);
        expander.Resources["ExpanderContentMinLength"] = 0d;
        AutomationProperties.SetName(expander, $"Provider {provider.Name}");
        expander.TemplateApplied += (_, applied) =>
        {
            if (applied.NameScope.Find<ToggleButton>("ExpanderHeader") is { } toggle)
                AutomationProperties.SetName(toggle, $"{provider.Name} setup and capabilities");
        };
        expander.PropertyChanged += (_, change) =>
        {
            if (change.Property != Expander.IsExpandedProperty) return;
            if (_applyingPresentation) return;
            _detailOverrides.Add(provider.Id);
            if (expander.IsExpanded) _expanded.Add(provider.Id); else _expanded.Remove(provider.Id);
        };
        _detailExpanders.Add((provider.Id, expander));
        var card = Ui.Card(new StackPanel { Spacing = 8, Children = { heading, actions, expander } });
        card.Padding = new Thickness(_presentation.Density == ViewDensity.Compact ? 10 : 16);
        _cardBorders.Add(card);
        return card;
    }

    private void AddLink(Panel target, CatalogCard provider, CatalogLink link, bool primary = false)
    {
        if (!Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return;
        var button = Ui.Button(link.Label, () => _openLink?.Invoke(uri));
        if (primary) button.Classes.Add("primary");
        button.IsEnabled = _openLink is not null;
        button.Margin = new Thickness(0, 0, 8, 6);
        ToolTip.SetTip(button, uri.AbsoluteUri);
        AutomationProperties.SetName(button, $"{provider.Name}: {link.Label}");
        target.Children.Add(button);
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
        string AccountUrl, string DocumentationUrl, string Readiness, bool LiveVerified, string[] AccessTypes, string[] Protocols, CatalogFact[] Access, CatalogFact[] Budgets, CatalogFact[] Limits,
        CatalogRoute[] Routes, string[] Steps, string[] Notes, CatalogLink[] Links, string Verification);
    private sealed record CatalogFact(string Label, string Value);
    private sealed record CatalogRoute(string Name, CatalogFact[] Facts);
    private sealed record CatalogLink(string Label, string Url);
}
