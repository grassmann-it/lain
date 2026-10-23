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
        var panel = Ui.Stack(Heading("Accounts", "Manage connections, credentials and provider budgets."));
        panel.Spacing = 12;
        var toolbar = Ui.Row(
            Action(_login is { State: "waiting" } ? "Continue Codex login" : "Connect Codex", StartCodexLoginAsync, "Complete sign-in in your browser.", stayOnPage: true),
            Ui.Button("Add API account", ShowAddAccount), Ui.Button("Import setup…", ShowImport));
        toolbar.Spacing = 6;
        panel.Children.Add(toolbar);
        if (_configuration is not { } cfg) { panel.Children.Add(Empty("Accounts are unavailable until the host responds.")); return panel; }
        if (cfg.Accounts.Length == 0)
        {
            panel.Children.Add(Empty("No accounts yet. Connect Codex, add an API key or import a supported configuration."));
            return panel;
        }

        var count = Ui.Text($"{cfg.Accounts.Length} accounts · {cfg.Accounts.Count(a => a.Enabled)} enabled", 12, Ui.Muted);
        panel.Children.Add(count);
        var selected = cfg.Accounts.FirstOrDefault(a => a.Id == _selectedAccountId) ?? cfg.Accounts[0];
        _selectedAccountId = selected.Id;
        var detail = new ContentControl { Content = AccountDetail(selected, cfg.Revision) };
        var list = new ListBox
        {
            ItemsSource = cfg.Accounts,
            ItemTemplate = new FuncDataTemplate<AccountDto>((account, _) => AccountRow(account!)),
            SelectedItem = selected,
            MaxHeight = 300,
            MinHeight = 52,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent
        };
        AutomationProperties.SetName(list, "Accounts");
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
                    new Border
                    {
                        Background = Ui.Surface, Padding = new Thickness(12, 8),
                        BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 0, 0, 1),
                        Child = AccountCells(Ui.Text("Account", 11, Ui.Muted), Ui.Text("Provider", 11, Ui.Muted),
                            Ui.Text("Credential", 11, Ui.Muted), Ui.Text("Enabled", 11, Ui.Muted), Ui.Text("Quota", 11, Ui.Muted))
                    },
                    list
                }
            }
        });
        panel.Children.Add(detail);
        return panel;
    }

    private Control AccountRow(AccountDto account)
    {
        var name = AccountCell(account.Name, 13);
        name.FontWeight = FontWeight.Medium;
        ToolTip.SetTip(name, account.Name);
        var credential = new StackPanel { Spacing = 2 };
        credential.Children.Add(AccountCell(account.CredentialKind, 12));
        credential.Children.Add(AccountCell(account.State, 11, Ui.Muted));
        var identity = new StackPanel { Spacing = 2 };
        identity.Children.Add(name);
        if (account.SharedWithLegacy) identity.Children.Add(AccountCell("Shared provider budget", 10, Ui.Muted));
        var quotas = _quotas.Where(q => q.AccountId == account.Id).ToArray();
        var quotaSummary = AccountCell(quotas.Length > 1 ? $"{quotas.Length} budget windows" : AccountQuotaSummary(quotas.FirstOrDefault()), 12, Ui.Muted);
        ToolTip.SetTip(quotaSummary, string.Join("\n", quotas.Select(q => $"{q.Source}: {AccountQuotaSummary(q)}")));
        var grid = AccountCells(identity, AccountCell(account.Provider, 12), credential,
            AccountCell(account.Enabled ? "Enabled" : "Disabled", 12, account.Enabled ? Ui.Accent : Ui.Muted),
            quotaSummary);
        grid.Margin = new Thickness(2, 5);
        grid.MinHeight = 32;
        return grid;
    }

    private static TextBlock AccountCell(string text, double size, IBrush? color = null) => new()
    {
        Text = text, FontSize = size, Foreground = color ?? Ui.Ink,
        TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
    };

    private static Grid AccountCells(params Control[] cells)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,1.15*,1.4*,0.85*,1.35*"), ColumnSpacing = 12 };
        for (var i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); grid.Children.Add(cells[i]); }
        return grid;
    }

    private static string AccountQuotaSummary(QuotaDto? quota)
    {
        if (quota is null) return "Unknown";
        if (quota.State is "stale" or "error" or "unavailable") return quota.State;
        if (quota.Remaining is { } remaining) return $"{remaining:0.##} {quota.Unit} left";
        if (quota.Used is { } used && quota.Limit is { } limit) return $"{used:0.##} / {limit:0.##} {quota.Unit}";
        return quota.State is "ok" or "available" ? "Amount unknown" : quota.State;
    }

    private Control AccountDetail(AccountDto account, long revision)
    {
        var content = new StackPanel { Spacing = 10 };
        var title = Ui.Text(account.Name, 15); title.FontWeight = FontWeight.SemiBold;
        content.Children.Add(title);
        var facts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 24 };
        var identity = new StackPanel { Spacing = 4 };
        identity.Children.Add(Ui.Text($"{account.Provider} · {account.CredentialKind} · {account.State}", 12, Ui.Muted));
        var id = new SelectableTextBlock { Text = account.Id, FontSize = 11, Foreground = Ui.Muted, TextWrapping = TextWrapping.Wrap };
        identity.Children.Add(id);
        var validity = new StackPanel { Spacing = 4 };
        validity.Children.Add(Ui.Text(account.ExpiresAt is { } expiry ? $"Credential expires {Ui.When(expiry)}" : "Credential expiry not supplied", 12, Ui.Muted));
        if (account.SharedWithLegacy) validity.Children.Add(Ui.Text("Shared provider budget with the imported account", 12, Ui.Accent));
        if (account.BudgetPoolId is { } pool) validity.Children.Add(Ui.Text($"Budget pool · {pool}", 11, Ui.Muted));
        facts.Children.Add(identity); Grid.SetColumn(validity, 1); facts.Children.Add(validity);
        content.Children.Add(facts);
        var quotas = _quotas.Where(q => q.AccountId == account.Id).ToArray();
        var budgets = new StackPanel { Spacing = 10 };
        if (quotas.Length == 0) budgets.Children.Add(QuotaDetails(null));
        foreach (var quota in quotas) budgets.Children.Add(QuotaDetails(quota));
        content.Children.Add(new Border { BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 10, 0, 0), Child = budgets });
        if (_quotaError is not null) content.Children.Add(Ui.Text("Quota snapshot may be stale · " + _quotaError, 11, Ui.Muted));
        var actions = Ui.Row(
            Action(account.Enabled ? "Disable" : "Enable", async () => { _configuration = await _client.SetAccountEnabledAsync(revision, account.Id, !account.Enabled, _lifetime.Token); }, "Account updated."),
            Action("Check credential", async () => { var result = await _client.CheckAccountAsync(account.Id, _lifetime.Token); _notice.Text = $"Credential status: {result.State}. Checked local presence and expiry only."; }, "Local credential check complete."),
            Action("Refresh quota", async () => { _quotas = await _client.RefreshQuotasAsync(account.Id, _lifetime.Token); }, "Quota refresh complete."),
            Ui.Button("Remove…", () => ConfirmRemoval(account.Name, async () => { _configuration = await _client.RemoveAccountAsync(revision, account.Id, _lifetime.Token); })));
        actions.Spacing = 6;
        content.Children.Add(actions);
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
