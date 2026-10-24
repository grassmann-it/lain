using Avalonia;
using Avalonia.Automation;
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

    /// <summary>A cached sign-in page must reflect authorization that finished while it was away.</summary>
    private void RestoreLoginPresentation()
    {
        if (_login is { State: "waiting" })
        {
            ShowLoginPage();
            return;
        }
        _loginVisible = false;
        _editing = false;
        _section = "Accounts";
        Render();
        _notice.Text = _login?.State switch
        {
            "completed" => "Codex account connected.",
            "cancelled" => "Codex sign-in cancelled. No new account was connected.",
            "failed" => "Codex sign-in failed. Choose Add account to try again.",
            "unsupported" => "Codex sign-in is unavailable on this host. Choose Add account for another connection method.",
            _ => "This sign-in is no longer active. Choose Add account to sign in again."
        };
    }

    private void ShowLoginPage()
    {
        if (_login is not { } login) return;
        _loginVisible = true; _editing = true; _actions.Clear();
        var waiting = login.State == "waiting";
        var (label, guidance) = login.State switch
        {
            "completed" => ("Connected", "Your Codex account is ready. Return to accounts to manage the connection."),
            "cancelled" => ("Sign-in cancelled", "No new account was connected. You can start again from accounts."),
            "failed" => ("Sign-in failed", "Authorization could not finish. Return to accounts and try connecting again."),
            "unsupported" => ("Sign-in unavailable", "This host does not support this authorization. Return to accounts to choose another connection method."),
            "waiting" => ("Waiting for authorization", "Open the authorization page and finish signing in. lain checks for your response automatically."),
            _ => ("Sign-in status", "Return to accounts to check this connection.")
        };
        var status = Ui.Text(label, 14);
        status.FontWeight = FontWeight.SemiBold;
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var details = new StackPanel { Spacing = 12 };
        details.Children.Add(status);
        details.Children.Add(Ui.Text(guidance, 13, Ui.Muted));
        if (waiting)
        {
            details.Children.Add(new ProgressBar
            {
                IsIndeterminate = true, Height = 3, MinHeight = 3,
                Foreground = Ui.Accent, Background = Ui.Edge,
                Margin = new Thickness(0, 4)
            });
            if (_loginError is not null)
            {
                var feedback = Ui.Text(_loginError, 13, Ui.Muted);
                AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite);
                details.Children.Add(feedback);
            }
            if (login.Url is { } url)
            {
                var open = Action("Open authorization page", async () => {
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) throw new InvalidOperationException("The host returned an invalid authorization URL.");
                    if (!await Launcher.LaunchUriAsync(uri)) throw new InvalidOperationException("The browser could not be opened. Check your default browser and try again.");
                }, "Finish signing in in your browser. This page checks automatically.", stayOnPage: true);
                open.Classes.Add("primary");
                open.HorizontalAlignment = HorizontalAlignment.Left;
                AutomationProperties.SetHelpText(open, "Opens your default browser only when you activate this button. You can return here while authorization is pending.");
                details.Children.Add(open);
                details.Children.Add(Ui.Text("Already opened it? Continue in that browser tab; this page can stay open.", 12, Ui.Muted));
                var manualUrl = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
                AutomationProperties.SetName(manualUrl, "Authorization link");
                AutomationProperties.SetHelpText(manualUrl, "Select all and copy this link into your browser. The link is only shown when you expand this section.");
                var manual = new Expander
                {
                    Header = "Use the authorization link manually",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Content = Ui.Stack(Ui.Text("Copy this link into your browser if the button cannot open it. Keep the authorization link private.", 12, Ui.Muted), manualUrl)
                };
                manual.PropertyChanged += (_, change) =>
                {
                    if (change.Property == Expander.IsExpandedProperty)
                        manualUrl.Text = manual.IsExpanded ? url : "";
                };
                details.Children.Add(manual);
            }
            else
                details.Children.Add(Ui.Text("The host has not supplied an authorization link. You can cancel and try again from accounts.", 13, Ui.Muted));
        }
        var back = Ui.Button("Back to accounts", () => Navigate("Accounts"));
        AutomationProperties.SetHelpText(back, waiting ? "Return to accounts. Sign-in continues in the background." : "Return to the list of connected accounts.");
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8 };
        buttons.Children.Add(back);
        if (waiting)
        {
            var cancel = Action("Cancel sign-in", async () => {
                _login = await _client.CancelLoginAsync(login.Id, _lifetime.Token);
                _loginPolling?.Cancel(); _loginVisible = false;
                _notice.Text = $"Codex login {_login.State}.";
            }, "Codex sign-in cancelled.");
            AutomationProperties.SetHelpText(cancel, "Stops this authorization request and returns to accounts.");
            buttons.Children.Add(cancel);
        }
        var content = new StackPanel { Spacing = 20, MaxWidth = 680, HorizontalAlignment = HorizontalAlignment.Stretch };
        content.Children.Add(Heading("Sign in to Codex", "Add a Codex provider account through browser authorization."));
        var card = Ui.Card(details);
        card.Padding = new Thickness(20);
        content.Children.Add(card);
        content.Children.Add(Ui.Text("Authorization is saved in lain’s own account storage. Applying a proxy profile or exporting settings for the Codex client are separate actions in Profiles.", 12, Ui.Muted));
        content.Children.Add(buttons);
        _page.Content = content;
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
                    var feedbackChanged = _login.State != login.State || _login.Url != login.Url || _loginError is not null;
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
                    if (feedbackChanged && _loginVisible && _section == "Accounts" && !_busy) ShowLoginPage();
                    if (_login.State is "failed" or "cancelled" or "unsupported")
                    {
                        _notice.Text = $"Codex authorization {_login.State}.";
                        return;
                    }
                }
                catch (OperationCanceledException) when (polling.IsCancellationRequested) { return; }
                catch (Exception)
                {
                    // Host error text may contain authorization details; keep feedback local.
                    var feedbackChanged = _loginError is null;
                    _loginError = "Status is temporarily unavailable. Checking again automatically; you can continue in your browser.";
                    if (feedbackChanged && _loginVisible && _section == "Accounts" && !_busy) ShowLoginPage();
                }
            }
        }
        catch (OperationCanceledException) when (polling.IsCancellationRequested) { }
        finally { if (ReferenceEquals(_loginPolling, polling)) _loginPolling = null; polling.Dispose(); }
    }

}
