using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    /// <summary>One entry to all account methods; provider discovery remains usable without credentials.</summary>
    private void ShowAddAccountChooser()
    {
        _editing = true;
        _loginVisible = false;
        _actions.Clear();
        _workspace.Offset = default;
        var methods = new StackPanel { Spacing = 12, MaxWidth = 740, HorizontalAlignment = HorizontalAlignment.Stretch };
        methods.Children.Add(Heading("Add account", "Choose your provider and how you want to connect it to lain."));
        methods.Children.Add(Ui.Card(Ui.Stack(
            Ui.Text("Choose a provider", 16),
            Ui.Text("Browse connection types, access plans and official credential instructions. You can browse before you have a key.", 13, Ui.Muted),
            Ui.Button("Discover providers", ShowProviderCatalog))));

        var signIn = Action(_login is { State: "waiting" } ? "Continue Codex sign-in" : "Sign in to Codex (browser)",
            StartCodexLoginAsync, "Complete sign-in in your browser.", stayOnPage: true);
        signIn.IsEnabled = _configuration is not null;
        AutomationProperties.SetHelpText(signIn, "Adds a Codex provider account through browser authorization. Client configuration is managed in Profiles.");
        methods.Children.Add(Ui.Card(Ui.Stack(
            Ui.Text("Codex account", 16),
            Ui.Text("Sign in with your own Codex account. Authorization is stored by lain; the browser opens when you choose the authorization action.", 13, Ui.Muted),
            signIn)));

        var manual = Ui.Button("Manual API account", ShowAddAccount);
        var import = Ui.Button("Import setup", ShowImport);
        manual.IsEnabled = import.IsEnabled = _configuration is not null;
        methods.Children.Add(Ui.Card(Ui.Stack(
            Ui.Text("Other connection methods", 16),
            Ui.Text("Enter a provider ID, API endpoint and key manually, or import an existing supported configuration. Imported Codex snapshots are not refreshed automatically.", 13, Ui.Muted),
            new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8, Children = { manual, import } })));
        if (_configuration is null)
            methods.Children.Add(Ui.Text("Connect to the local host to save credentials, sign in or import. Provider discovery is still available.", 13, Ui.Muted));
        methods.Children.Add(Ui.Text("Adding credentials does not apply a proxy profile or verify provider access. Profiles shows the applied proxy policy and client export actions.", 12, Ui.Muted));
        methods.Children.Add(Ui.Button("Back to accounts", () => Navigate("Accounts")));
        _page.Content = methods;
    }
}
