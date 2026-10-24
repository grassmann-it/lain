using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Lain.Core;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private async Task ShowAccountModelsAsync(string accountId)
    {
        if (IsMonitorMode || _busy) return;
        _editing = false;
        await NavigateToAsync(new("Models", AccountId: accountId));
        _editing = true;
        var loading = Ui.Stack(Heading("Choose provider models", "Loading documented model templates…"));
        _page.Content = loading;
        _workspace.Offset = default;
        try
        {
            var catalog = await _client.ProvidersAsync(_lifetime.Token);
            // A late catalog response must not replace another task or mounted draft.
            if (!ReferenceEquals(_page.Content, loading) || _section != "Models") return;
            _providerCatalog = catalog;
            BuildAccountModelPicker(accountId, catalog);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!ReferenceEquals(_page.Content, loading)) return;
            _page.Content = Ui.Stack(Heading("Models unavailable", Friendly(error)),
                Ui.Button("Retry", () => _ = ShowAccountModelsAsync(accountId)), Ui.Button("Back to models", ClosePicker));
        }
        void ClosePicker() { _editing = false; Render(); }
    }

    private void BuildAccountModelPicker(string accountId, ProviderCatalogDto catalog)
    {
        if (_configuration is not { } cfg || cfg.Accounts.FirstOrDefault(a => a.Id == accountId) is not { } account)
            throw new InvalidOperationException("This account is no longer in the saved configuration. Return to Models and choose an existing account.");
        var providerId = account.Provider switch { "command-code" => "commandcode", "mimo" => "xiaomi-mimo", _ => account.Provider };
        var descriptor = catalog.Providers.FirstOrDefault(p => p.Id == providerId);
        var connections = descriptor?.Connections?.Where(c => c.ProviderRouteIds.Any(id => descriptor.Routes.Any(r => r.Id == id))).ToArray() ?? [];
        RegisterEditor(() => BuildAccountModelPicker(accountId, catalog), cfg.Revision);
        _editing = true;
        _actions.Clear();
        var content = Ui.Stack(Heading("Choose models for " + account.Name,
            "Use this account's saved private key. Choosing templates configures access; it does not verify live entitlement."));
        void ClosePicker() { _editing = false; Render(); }
        if (descriptor is null || connections.Length == 0)
        {
            content.Children.Add(Ui.Text("No documented model templates are installed for this account. You can add model access manually in Models.", 13, Ui.Muted));
            content.Children.Add(Ui.Button("Back to models", ClosePicker));
            _page.Content = content;
            return;
        }

        var connection = new ComboBox { ItemsSource = connections.Select(c => $"{c.Name} · {c.Protocol}").ToArray(),
            SelectedIndex = connections.Length == 1 ? 0 : -1, PlaceholderText = "Choose the endpoint used by this key", HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(connection, "Provider model connection");
        // Commercial access is declared only when the selected connection needs it.
        var plan = new ComboBox { PlaceholderText = "Choose your existing access plan", HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(plan, "Model access plan");
        var planField = Ui.Stack(Ui.Field("Existing access plan", plan),
            Ui.Text("Choose access you already have. This declaration does not detect a subscription or purchase anything.", 12, Ui.Muted));
        var endpoint = Ui.Text("", 12, Ui.Muted);
        var choices = new StackPanel { Spacing = 5 };
        var settingsPanel = Ui.Stack();
        var boxes = new Dictionary<string, CheckBox>(StringComparer.Ordinal);
        var inputs = new Dictionary<string, TextBox>(StringComparer.Ordinal);
        var consent = new CheckBox { Content = "I have enabled Contributor endpoints that train on request data in this workspace", IsChecked = false };
        AutomationProperties.SetName(consent, "Allow Contributor training endpoints");
        var consentPanel = Ui.Stack(consent, Ui.Text("Required only for the selected Gemini Contributor model. Review the workspace privacy setting before granting permission.", 12, Ui.Warning));
        ProviderConnectionDto? SelectedConnection() => connection.SelectedIndex >= 0 && connection.SelectedIndex < connections.Length ? connections[connection.SelectedIndex] : null;
        string? SelectedPlan() => SelectedConnection() is { } selected
            ? selected.BillingPlanIds.Length == 1 ? selected.BillingPlanIds[0]
                : plan.SelectedIndex >= 0 && plan.SelectedIndex < selected.BillingPlanIds.Length ? selected.BillingPlanIds[plan.SelectedIndex] : null
            : null;
        bool NeedsConsent() => descriptor.Id == "opencode-go" && descriptor.Routes.Any(r => r.Model == "gemini-4-argon-contributor" && boxes.TryGetValue(r.Id, out var box) && box.IsEnabled && box.IsChecked == true);
        void UpdateSettings()
        {
            settingsPanel.IsVisible = boxes.Values.Any(b => b.IsEnabled && b.IsChecked == true);
            consentPanel.IsVisible = NeedsConsent();
            consent.IsVisible = consentPanel.IsVisible;
            if (!consentPanel.IsVisible) consent.IsChecked = false;
        }
        void PopulateModels()
        {
            boxes.Clear(); choices.Children.Clear();
            if (SelectedConnection() is not { } selected) { choices.Children.Add(Ui.Text("Choose a connection to see its models.", 13, Ui.Muted)); UpdateSettings(); return; }
            if (SelectedPlan() is not { } billing) { choices.Children.Add(Ui.Text("Choose your existing access plan to see its model templates.", 13, Ui.Muted)); UpdateSettings(); return; }
            foreach (var route in descriptor.Routes.Where(r => selected.ProviderRouteIds.Contains(r.Id, StringComparer.Ordinal) && r.BillingPlanIds.Contains(billing, StringComparer.Ordinal)))
            {
                var configured = cfg.Routes.Any(r => r.AccountIds.Contains(accountId, StringComparer.Ordinal) && r.ProviderRouteId == route.Id);
                var box = new CheckBox { Content = Ui.Text($"{route.Name} · {route.Model} · {route.PricingClass}" + (configured ? " · Already configured" : "")), IsEnabled = !configured, IsChecked = false };
                AutomationProperties.SetName(box, "Choose provider model " + route.Id);
                box.IsCheckedChanged += (_, _) => UpdateSettings();
                boxes.Add(route.Id, box); choices.Children.Add(box);
                foreach (var restriction in route.Restrictions ?? []) choices.Children.Add(Ui.Text(restriction, 11, Ui.Muted));
            }
            if (boxes.Count == 0) choices.Children.Add(Ui.Text("No model templates belong to this connection and access plan.", 13, Ui.Muted));
            UpdateSettings();
        }
        void PopulateConnection()
        {
            var selected = SelectedConnection();
            endpoint.Text = selected is null ? "" : selected.BaseUrl;
            planField.IsVisible = selected?.BillingPlanIds.Length > 1;
            plan.IsVisible = planField.IsVisible;
            plan.ItemsSource = selected?.BillingPlanIds.Select(id => descriptor.BillingPlans.FirstOrDefault(p => p.Id == id)?.Name ?? id).ToArray() ?? [];
            plan.SelectedIndex = -1;
            PopulateModels();
        }
        // Only transport settings accepted by the host belong here. Maintainer OAuth IDs
        // and all secret credential fields are excluded from the model task.
        foreach (var field in descriptor.Credentials.Where(f => !f.Secret &&
            ((descriptor.Id == "cloudflare-workers-ai" && f.Id == "accountId") ||
             (descriptor.Id == "alibaba-model-studio" && f.Id is "region" or "workspaceId" or "workspace_id" or "serviceDeploymentScope" or "service_deployment_scope"))))
        {
            var existing = field.Id == "region" ? account.Binding?.Region
                : field.Id == "accountId" && account.Binding?.ResourceType == "account" ? account.Binding.ResourceId
                : (field.Id is "workspaceId" or "workspace_id") && account.Binding?.ResourceType == "workspace" ? account.Binding.ResourceId : null;
            var input = Ui.Input(existing, "Keep saved value");
            AutomationProperties.SetName(input, "Model setting " + field.Id);
            inputs.Add(field.Id, input);
            settingsPanel.Children.Add(Ui.Field(field.Label, input));
            if (field.Help is { } help) settingsPanel.Children.Add(Ui.Text(help, 11, Ui.Muted));
        }
        settingsPanel.Children.Add(consentPanel);
        if (inputs.Count > 0) settingsPanel.Children.Insert(0, Ui.Text("Resource settings · leave unchanged to reuse saved settings.", 12, Ui.Muted));
        var save = Action("Add selected models", async () => {
            if (SelectedConnection() is not { } selected || SelectedPlan() is not { } billing) throw new InvalidOperationException("Choose the connection and its existing access plan.");
            var ids = boxes.Where(p => p.Value.IsEnabled && p.Value.IsChecked == true).Select(p => p.Key).ToArray();
            if (ids.Length == 0) throw new InvalidOperationException("Choose at least one model.");
            if (NeedsConsent() && consent.IsChecked != true) throw new InvalidOperationException("The selected Contributor model requires explicit workspace training permission.");
            var settings = inputs.Where(p => !string.IsNullOrWhiteSpace(p.Value.Text)).ToDictionary(p => p.Key, p => p.Value.Text!.Trim(), StringComparer.Ordinal);
            if (selected.BillingPlanIds.Length > 1) settings["billingPlan"] = billing;
            if (NeedsConsent()) settings["allowTrainingData"] = "true";
            _configuration = await _client.AttachAccountModelsAsync(cfg.Revision, account.Id,
                new(account.Generation, selected.Id, ids, settings.Count == 0 ? null : settings), _lifetime.Token);
        }, "Model access added. Choose a profile when you want to apply it to the proxy.");
        save.Classes.Add("primary");
        connection.SelectionChanged += (_, _) => PopulateConnection();
        plan.SelectionChanged += (_, _) => PopulateModels();
        content.Children.Add(Ui.Field("Connection", connection)); content.Children.Add(endpoint); content.Children.Add(planField);
        content.Children.Add(new ScrollViewer { MaxHeight = 340, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = choices });
        content.Children.Add(settingsPanel);
        content.Children.Add(Ui.Text("No models are selected by default. Catalog prices may be unknown; eligibility does not prove remaining quota. Adding models does not apply a profile or send an inference request.", 12, Ui.Muted));
        content.Children.Add(OverviewActions(save, Ui.Button("Cancel", ClosePicker)));
        _page.Content = content;
        PopulateConnection();
    }
}
