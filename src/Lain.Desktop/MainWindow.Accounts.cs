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
    private Control AccountsPage()
    {
        var panel = Ui.Stack(Heading("Accounts", "Each connection keeps its own identity and history."),
            Ui.Row(Action(_login is { State: "waiting" } ? "Continue Codex login" : "Connect Codex", StartCodexLoginAsync, "Complete sign-in in your browser.", stayOnPage: true), Ui.Button("Add API account", ShowAddAccount), Ui.Button("Import existing setup", ShowImport)));
        if (_configuration is not { } cfg) { panel.Children.Add(Empty("Accounts are unavailable until the host responds.")); return panel; }
        if (cfg.Accounts.Length == 0) panel.Children.Add(Empty("No accounts yet. Add an API key or import a supported local configuration."));
        foreach (var account in cfg.Accounts)
        {
            var details = Ui.Stack(Ui.Text(account.Name, 20), Ui.Text($"{account.Provider} · {account.CredentialKind} · {account.State}", 13, Ui.Muted), Ui.Text($"{(account.Enabled ? "Enabled" : "Disabled")}  ·  {account.Id}", 12, Ui.Muted));
            if (account.ExpiresAt is { } expiry) details.Children.Add(Ui.Text($"Credential expires {Ui.When(expiry)}", 12, expiry < DateTimeOffset.Now ? Brush.Parse("#EAB879") : Ui.Muted));
            if (account.SharedWithLegacy) details.Children.Add(Ui.Text("Shared provider budget with the imported account", 12, Ui.Accent));
            var quota = _quotas.FirstOrDefault(q => q.AccountId == account.Id);
            details.Children.Add(QuotaDetails(quota));
            if (_quotaError is not null) details.Children.Add(Ui.Text("Quota snapshot may be stale · " + _quotaError, 12, Ui.Muted));
            details.Children.Add(Ui.Row(
                Action(account.Enabled ? "Disable" : "Enable", async () => { _configuration = await _client.SetAccountEnabledAsync(cfg.Revision, account.Id, !account.Enabled, _lifetime.Token); }, "Account updated."),
                Action("Check credential", async () => { var result = await _client.CheckAccountAsync(account.Id, _lifetime.Token); _notice.Text = $"Credential status: {result.State}. Checked local presence and expiry only."; }, "Local credential check complete."),
                Ui.Button("Remove…", () => ConfirmRemoval(account.Name, async () => { _configuration = await _client.RemoveAccountAsync(cfg.Revision, account.Id, _lifetime.Token); }))));
            details.Children.Add(Action("Refresh quota", async () => { _quotas = await _client.RefreshQuotasAsync(account.Id, _lifetime.Token); }, "Quota refresh complete."));
            panel.Children.Add(Ui.Card(details));
        }
        return panel;
    }

    private void ShowAddAccount()
    {
        if (_configuration is not { } cfg) { _notice.Text = "Connect to the host first."; return; }
        _editing = true;
        var name = Ui.Input(hint: "My API account");
        var provider = Ui.Input(hint: "Provider identifier, e.g. command-code");
        var endpoint = Ui.Input(hint: "https://…/v1");
        var key = Ui.Input(hint: "API key"); key.PasswordChar = '●';
        _page.Content = Ui.Stack(Heading("Add an API account", "Credentials are sent only to your local host and are not included in exports."),
            Ui.Card(Ui.Stack(Ui.Field("Display name", name), Ui.Field("Provider", provider), Ui.Field("OpenAI-compatible base URL", endpoint), Ui.Field("API key", key))),
            Ui.Row(Action("Add account", async () => {
                if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(provider.Text) || string.IsNullOrWhiteSpace(key.Text) || string.IsNullOrWhiteSpace(endpoint.Text)) throw new InvalidOperationException("Enter a name, provider, base URL and API key.");
                if (!Uri.TryCreate(endpoint.Text, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) throw new InvalidOperationException("Enter a valid HTTP or HTTPS base URL.");
                _configuration = await _client.AddApiAccountAsync(cfg.Revision, new AddApiAccountRequest(name.Text.Trim(), provider.Text.Trim(), key.Text, endpoint.Text.Trim()), _lifetime.Token); key.Text = "";
            }, "Account added."), Ui.Button("Cancel", () => { key.Text = ""; _editing = false; Render(); })));
    }

    private void ShowImport()
    {
        if (_configuration is not { } cfg) { _notice.Text = "Connect to the host first."; return; }
        _editing = true;
        var path = Ui.Input(hint: "/path/to/config.yaml"); var auth = Ui.Input(hint: "Optional auth directory");
        _page.Content = Ui.Stack(Heading("Import an existing setup", "Read supported API configuration and Codex access-token snapshots. Source files stay unchanged; imported OAuth snapshots are never refreshed or revoked."),
            Ui.Card(Ui.Stack(Ui.Field("Configuration path on this computer", path), Ui.Field("Auth directory (optional)", auth))),
            Ui.Row(Action("Import", async () => {
                if (string.IsNullOrWhiteSpace(path.Text)) throw new InvalidOperationException("Enter a configuration path.");
                var result = await _client.ImportAsync(new ImportRequest(cfg.Revision, path.Text.Trim(), string.IsNullOrWhiteSpace(auth.Text) ? null : auth.Text.Trim()), _lifetime.Token);
                _notice.Text = $"Imported {result.Added}, updated {result.Updated}. {string.Join(" ", result.Warnings)}";
            }, "Import complete. Refresh to inspect the imported accounts."), Ui.Button("Cancel", () => { _editing = false; Render(); })));
    }

}
