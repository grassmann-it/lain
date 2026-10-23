using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Lain.Client;
using Lain.Core;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private string? _selectedAccountId;

    private Control AccountsPage()
    {
        var panel = Ui.Stack(Ui.PageHeader("Accounts", "Provider budgets and account controls.",
            Action(_login is { State: "waiting" } ? "Continue Codex login" : "Connect Codex", StartCodexLoginAsync, "Complete sign-in in your browser.", stayOnPage: true),
            Ui.Button("Add API account", ShowAddAccount), Ui.Button("Import setup…", ShowImport)));
        panel.Spacing = 10;
        if (_configuration is not { } cfg) { panel.Children.Add(Empty("Accounts are unavailable until the host responds.")); return panel; }
        if (cfg.Accounts.Length == 0)
        {
            panel.Children.Add(Empty("No accounts yet. Connect Codex, add an API key or import a supported configuration."));
            return panel;
        }

        var count = Ui.Text($"{cfg.Accounts.Length} accounts · {cfg.Accounts.Count(a => a.Enabled)} enabled · Select an account for controls and details.", 12, Ui.Muted);
        panel.Children.Add(count);
        var selected = cfg.Accounts.FirstOrDefault(a => a.Id == _selectedAccountId) ?? cfg.Accounts[0];
        _selectedAccountId = selected.Id;
        var detail = new ContentControl { Content = AccountDetail(selected, cfg.Revision) };
        var list = new ListBox
        {
            ItemsSource = cfg.Accounts,
            // Virtualized containers clear their content while being recycled
            // during scrolling or a resize; that template call has no account.
            ItemTemplate = new FuncDataTemplate<AccountDto>((account, _) => account is null ? null : AccountRow(account)),
            SelectedItem = selected,
            MaxHeight = 328,
            MinHeight = 52,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent
        };
        AutomationProperties.SetName(list, "Accounts");
        ScrollViewer.SetHorizontalScrollBarVisibility(list, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not AccountDto account) return;
            _selectedAccountId = account.Id;
            // Keep the list and its keyboard focus stable when inspecting another account.
            detail.Content = AccountDetail(account, cfg.Revision);
        };
        panel.Children.Add(new Border
        {
            BorderBrush = Ui.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Child = new StackPanel
            {
                Spacing = 0,
                Children =
                {
                    BudgetTableHeader(),
                    list
                }
            }
        });
        panel.Children.Add(detail);
        return panel;
    }

    private Control AccountRow(AccountDto account)
    {
        var quotas = _quotas.Where(q => q.AccountId == account.Id).ToArray();
        var windows = new StackPanel { Spacing = 7 };
        if (quotas.Length == 0) windows.Children.Add(BudgetWindowRow(account, null));
        foreach (var quota in quotas) windows.Children.Add(BudgetWindowRow(account, quota));
        var grid = BudgetAccountCells(BudgetIdentity(account), windows);
        return new Border
        {
            Padding = new Thickness(2, 7), BorderBrush = Ui.Edge,
            BorderThickness = new Thickness(0, 0, 0, 1), Child = grid
        };
    }

    private static TextBlock AccountCell(string text, double size, IBrush? color = null) => new()
    {
        Text = text, FontSize = size, Foreground = color ?? Ui.Ink,
        TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
    };

    private Control AccountDetail(AccountDto account, long revision)
    {
        var content = new StackPanel { Spacing = 8 };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        var title = AccountCell(AccountDisplayName(account), 14); title.FontWeight = FontWeight.SemiBold;
        title.Classes.Add("section-title");
        ToolTip.SetTip(title, account.Name);
        header.Children.Add(title);
        var enabled = Ui.Badge(account.Enabled ? "Enabled for routing" : "Disabled", account.Enabled ? "accent" : null);
        Grid.SetColumn(enabled, 1); header.Children.Add(enabled);
        content.Children.Add(header);

        var quotas = _quotas.Where(q => q.AccountId == account.Id).ToArray();
        var actions = OverviewActions(
            Action(account.Enabled ? "Disable" : "Enable", async () => { _configuration = await _client.SetAccountEnabledAsync(revision, account.Id, !account.Enabled, _lifetime.Token); }, "Account updated."),
            Action("Check credential", async () => { var result = await _client.CheckAccountAsync(account.Id, _lifetime.Token); _notice.Text = $"Credential status: {result.State}. Checked local presence and expiry only."; }, "Local credential check complete."),
            Action("Refresh quota", async () => { _quotas = await _client.RefreshQuotasAsync(account.Id, _lifetime.Token); }, "Quota refresh complete."),
            Ui.Button("Remove…", () => ConfirmRemoval(account.Name, async () => { _configuration = await _client.RemoveAccountAsync(revision, account.Id, _lifetime.Token); })));
        content.Children.Add(actions);

        var notes = new List<string>();
        if (account.ExpiresAt is { } expiry) notes.Add($"Credential expires {expiry.ToLocalTime():dd MMM · HH:mm}");
        if (account.SharedWithLegacy) notes.Add("Shares the provider budget with the imported connection");
        if (notes.Count > 0) content.Children.Add(Ui.Text(string.Join(" · ", notes), 11, Ui.Muted));
        if (IsMiMo(account))
        {
            content.Children.Add(Ui.Text("Quota lookup for MiMo is not supported here yet. View the coding-plan allowance in the provider console.", 12, Ui.Muted));
            content.Children.Add(Action("Check in provider console", async () =>
            {
                if (!await Launcher.LaunchUriAsync(new Uri("https://platform.xiaomimimo.com/#/console/plan-manage")))
                    throw new InvalidOperationException("The browser could not be opened. Check your default browser and try again.");
            }, "MiMo provider console opened."));
        }
        if (!_hostAvailable || _quotaError is not null) content.Children.Add(Ui.Text(BudgetSnapshotNote(), 11, Ui.Warning));

        var technical = new StackPanel { Spacing = 9 };
        technical.Children.Add(QuotaTechnicalField("Account ID", account.Id));
        technical.Children.Add(QuotaTechnicalField("Credential", $"{account.CredentialKind} · local state: {account.State}"));
        technical.Children.Add(Ui.Text("Credential checks confirm local presence and expiry only; provider access is not verified.", 11, Ui.Muted));
        if (account.ExpiresAt is null) technical.Children.Add(Ui.Text("Credential expiry not supplied.", 11, Ui.Muted));
        if (account.BudgetPoolId is { } pool) technical.Children.Add(QuotaTechnicalField("Account pool", pool));
        if (quotas.Length == 0) technical.Children.Add(QuotaDetails(null));
        foreach (var quota in quotas) technical.Children.Add(QuotaDetails(quota));
        if (_quotaError is not null) technical.Children.Add(Ui.Text(_quotaError, 11, Ui.Warning));
        content.Children.Add(new Expander
        {
            Header = "Technical details", HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new Border { Padding = new Thickness(0, 8), Child = technical }
        });
        return new Border { Padding = new Thickness(12), BorderBrush = Ui.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = content };
    }

    private void ShowAddAccount()
    {
        if (_configuration is not { } cfg) { _notice.Text = "Connect to the host first."; return; }
        _editing = true;
        var name = Ui.Input(hint: "My API account");
        var provider = Ui.Input(hint: "e.g. command-code");
        var endpoint = Ui.Input(hint: "https://…/v1");
        var key = Ui.Input(hint: "API key"); key.PasswordChar = '●';
        var form = AccountForm("Add API account", "Credentials stay with your local host and are excluded from exports.",
            AccountFormRow("Display name", name), AccountFormRow("Provider", provider),
            AccountFormRow("Base URL", endpoint), AccountFormRow("API key", key));
        form.Children.Add(Ui.Row(Action("Add account", async () => {
            if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(provider.Text) || string.IsNullOrWhiteSpace(key.Text) || string.IsNullOrWhiteSpace(endpoint.Text)) throw new InvalidOperationException("Enter a name, provider, base URL and API key.");
            if (!Uri.TryCreate(endpoint.Text, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) throw new InvalidOperationException("Enter a valid HTTP or HTTPS base URL.");
            _configuration = await _client.AddApiAccountAsync(cfg.Revision, new AddApiAccountRequest(name.Text.Trim(), provider.Text.Trim(), key.Text, endpoint.Text.Trim()), _lifetime.Token); key.Text = "";
        }, "Account added."), Ui.Button("Cancel", () => { key.Text = ""; _editing = false; Render(); })));
        _page.Content = form;
    }

    private void ShowImport()
    {
        if (_configuration is not { } cfg) { _notice.Text = "Connect to the host first."; return; }
        _editing = true;
        var path = Ui.Input(hint: "/path/to/config.yaml"); var auth = Ui.Input(hint: "Optional auth directory");
        var form = AccountForm("Import setup", "Import supported API configuration and Codex access-token snapshots.",
            AccountFormRow("Configuration path", path), AccountFormRow("Auth directory", auth));
        form.Children.Add(Ui.Text("Source files stay unchanged. Imported OAuth snapshots are never refreshed or revoked.", 12, Ui.Muted));
        form.Children.Add(Ui.Row(Action("Import", async () => {
            if (string.IsNullOrWhiteSpace(path.Text)) throw new InvalidOperationException("Enter a configuration path.");
            var result = await _client.ImportAsync(new ImportRequest(cfg.Revision, path.Text.Trim(), string.IsNullOrWhiteSpace(auth.Text) ? null : auth.Text.Trim()), _lifetime.Token);
            _notice.Text = $"Imported {result.Added}, updated {result.Updated}. {string.Join(" ", result.Warnings)}";
        }, "Import complete. Refresh to inspect the imported accounts."), Ui.Button("Cancel", () => { _editing = false; Render(); })));
        _page.Content = form;
    }

    private StackPanel AccountForm(string title, string description, params Control[] rows)
    {
        var form = new StackPanel { Spacing = 12, MaxWidth = 740, HorizontalAlignment = HorizontalAlignment.Left };
        form.Children.Add(Heading(title, description));
        var fields = new StackPanel { Spacing = 0 };
        foreach (var row in rows) fields.Children.Add(row);
        form.Children.Add(fields);
        return form;
    }

    private static Control AccountFormRow(string label, Control input)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), ColumnSpacing = 16, MinWidth = 570 };
        var text = Ui.Text(label, 12, Ui.Muted); text.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(input, label);
        grid.Children.Add(text); Grid.SetColumn(input, 1); grid.Children.Add(input);
        return new Border { Padding = new Thickness(0, 9), BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
    }
}
