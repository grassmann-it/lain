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
    private async Task StartCodexLoginAsync()
    {
        if (_configuration is not { } cfg) throw new InvalidOperationException("Connect to the host first.");
        if (_login is not { State: "waiting" })
        {
            _loginPolling?.Cancel();
            _login = await _client.LoginAsync(cfg.Revision, "codex", _lifetime.Token);
            _loginError = null;
            if (_login.State == "waiting")
            {
                var polling = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                _loginPolling = polling;
                _ = PollLoginAsync(_login.Id, polling);
            }
        }
        ShowLoginPage();
    }

    private void ShowLoginPage()
    {
        if (_login is not { } login) return;
        _loginVisible = true; _editing = true; _actions.Clear();
        var guidance = login.State switch
        {
            "completed" => "Your Codex account is connected.",
            "cancelled" => "Authorization was cancelled. Return to accounts to start again.",
            "failed" or "unsupported" => "Authorization could not finish. Return to accounts to retry.",
            _ => "Finish the Codex authorization in your browser. lain checks the result automatically."
        };
        var details = Ui.Stack(Ui.Text($"Sign-in · {login.State}", 20), Ui.Text(login.Error ?? _loginError ?? guidance, 14, Ui.Muted));
        if (login.Url is { } url)
        {
            details.Children.Add(new TextBox { Text = url, IsReadOnly = true, TextWrapping = TextWrapping.Wrap });
            details.Children.Add(Action("Open authorization page", async () => {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) throw new InvalidOperationException("The host returned an invalid authorization URL.");
                if (!await Launcher.LaunchUriAsync(uri)) throw new InvalidOperationException("The browser could not be opened. Copy the authorization URL into your browser.");
            }, "Complete sign-in in your browser.", stayOnPage: true));
        }
        var buttons = Ui.Row(Ui.Button("Back to accounts", () => Navigate("Accounts")));
        if (login.State == "waiting") buttons.Children.Add(Action("Cancel login", async () => {
            _login = await _client.CancelLoginAsync(login.Id, _lifetime.Token);
            _loginPolling?.Cancel(); _loginVisible = false;
            _notice.Text = $"Codex login {_login.State}.";
        }, "Codex login cancelled."));
        _page.Content = Ui.Stack(Heading("Connect your Codex account", "This opens a new authorization for lain's own account storage."), Ui.Card(details), buttons);
    }

    private async Task PollLoginAsync(string id, CancellationTokenSource polling)
    {
        try
        {
            while (!polling.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), polling.Token);
                try
                {
                    var login = await _client.GetLoginAsync(id, polling.Token);
                    if (_login?.Id != id) return;
                    _login = login; _loginError = null;
                    if (_login.State == "completed")
                    {
                        _configuration = await _client.GetConfigurationAsync(polling.Token);
                        if (_loginVisible && _section == "Accounts") { _editing = false; _loginVisible = false; }
                        await RefreshAsync(false);
                        if (!_editing) Render();
                        _notice.Text = "Codex account connected.";
                        return;
                    }
                    if (_loginVisible && _section == "Accounts" && !_busy) ShowLoginPage();
                    if (_login.State is "failed" or "cancelled" or "unsupported")
                    {
                        _notice.Text = $"Codex authorization {_login.State}.";
                        return;
                    }
                }
                catch (OperationCanceledException) when (polling.IsCancellationRequested) { return; }
                catch (Exception error)
                {
                    _loginError = "Sign-in status is unavailable. Retrying automatically. " + Friendly(error);
                    if (_loginVisible && _section == "Accounts" && !_busy) ShowLoginPage();
                }
            }
        }
        catch (OperationCanceledException) when (polling.IsCancellationRequested) { }
        finally { if (ReferenceEquals(_loginPolling, polling)) _loginPolling = null; polling.Dispose(); }
    }

}
