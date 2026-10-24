using Lain.Client;
using Lain.Core;

namespace Lain.Cli;

public static partial class CliApplication
{
    private static async Task<bool> AddAccountWizard(LainApiClient client, TextWriter output, IAccountWizardTerminal terminal, CancellationToken ct)
    {
        try
        {
            await output.WriteLineAsync("Private account setup. Press Esc or Ctrl-C to cancel. Credentials stay with your local lain host.");
            var catalog = await client.ProvidersAsync(ct);
            var configuration = await client.GetConfigurationAsync(ct);
            var provider = await WizardChoice(output, terminal, "Provider", catalog.Providers, item => item.Name, ct);
            await output.WriteLineAsync(Cell(provider.Summary));
            await output.WriteLineAsync("Official credentials: " + Cell(provider.AccountUrl));
            foreach (var step in provider.SetupSteps)
                await output.WriteLineAsync(Cell(step.Title + ": " + step.Description + (step.Url is null ? "" : " " + step.Url)));
            var connections = provider.Connections ?? [];
            if (connections.Length == 0) throw new InvalidOperationException("This provider has no prepared API credential connection. Review its official setup guide or use its qualified login command.");
            var connection = connections.Length == 1 ? connections[0]
                : await WizardChoice(output, terminal, "Account connection", connections, item => item.Name, ct);
            await output.WriteLineAsync("Account connection: " + Cell(connection.Name));
            var plans = provider.BillingPlans.Where(plan => connection.BillingPlanIds.Contains(plan.Id, StringComparer.Ordinal)).ToArray();
            var plan = await WizardChoice(output, terminal, "Access / billing plan", plans,
                item => item.Name + " (" + ReadableState(item.Kind) + ")", ct);
            await output.WriteLineAsync("This is your selected plan; no entitlement, balance or provider access is verified.");
            var settings = new Dictionary<string, string>(StringComparer.Ordinal) { ["billingPlan"] = plan.Id };
            foreach (var field in provider.Credentials.Where(field => !field.Secret && (field.Kind != "client-id" || field.Required)))
            {
                if (!string.IsNullOrWhiteSpace(field.Help)) await output.WriteLineAsync(Cell(field.Help));
                var value = await WizardValue(terminal, field.Label + (field.Kind == "consent" ? " (type yes to allow)" : ""), false, ct, required: field.Required);
                if (field.Kind == "consent")
                {
                    if (value.Equals("yes", StringComparison.OrdinalIgnoreCase)) settings[field.Id] = "true";
                    else if (field.Required) throw new ArgumentException("Required provider consent was not granted. No account was saved.");
                }
                else if (value.Length > 0) settings[field.Id] = value;
            }
            var secrets = provider.Credentials.Where(field => field.Secret && field.Required).ToArray();
            if (secrets.Length != 1) throw new InvalidOperationException("This provider requires a credential flow that is not prepared in this wizard.");
            if (!string.IsNullOrWhiteSpace(secrets[0].Help)) await output.WriteLineAsync(Cell(secrets[0].Help));
            var key = await WizardValue(terminal, secrets[0].Label + " (hidden)", true, ct);
            var suggested = AccountNameSuggestions.Suggest(provider.Id, provider.DefaultAccountName ?? provider.Name, configuration.Accounts.Select(account => account.Name));
            var name = await WizardValue(terminal, "Display name", false, ct, suggested);
            var candidates = provider.Routes.Where(route => connection.ProviderRouteIds.Contains(route.Id, StringComparer.Ordinal) && route.BillingPlanIds.Contains(plan.Id, StringComparer.Ordinal)).ToArray();
            await output.WriteLineAsync("Optional models: leave blank to save only the account. Compatible models can use this credential later without re-entering it.");
            for (var index = 0; index < candidates.Length; index++) await output.WriteLineAsync($"  {index + 1}. {Cell(candidates[index].Name)}");
            string[] selected;
            while (true)
            {
                var choices = await WizardValue(terminal, "Model numbers (comma-separated)", false, ct, required: false);
                var parts = choices.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var numbers = parts.Select(part => int.TryParse(part, out var number) ? number : 0).ToArray();
                if (numbers.All(number => number > 0 && number <= candidates.Length)) { selected = numbers.Distinct().Select(number => candidates[number - 1].Id).ToArray(); break; }
                await output.WriteLineAsync("Enter listed model numbers or leave blank.");
            }
            await output.WriteLineAsync($"Save {Cell(name)} · {Cell(provider.Name)} · {Cell(plan.Name)} · {selected.Length} model routes.");
            await output.WriteLineAsync("Saving is separate from quota refresh and real inference. Saving performs neither provider action.");
            var confirm = await WizardValue(terminal, "Save locally? Type yes", false, ct, required: false);
            if (!confirm.Equals("yes", StringComparison.OrdinalIgnoreCase)) return false;
            static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
            var endpoint = System.Text.RegularExpressions.Regex.Replace(connection.BaseUrl, "\\{([A-Za-z0-9_]+)\\}", match =>
                settings.FirstOrDefault(pair => Normalize(pair.Key) == Normalize(match.Groups[1].Value)).Value ?? match.Value);
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || endpoint.Contains('{'))
                throw new ArgumentException("Complete the account settings to resolve the official HTTPS endpoint. No account was saved.");
            var saved = await client.AddApiAccountAsync(configuration.Revision, new(name, provider.Id, key, endpoint,
                Settings: settings, ProviderConnectionId: connection.Id, SelectedModelRouteIds: selected), ct);
            key = "";
            await output.WriteLineAsync("Account saved locally. Provider access and live budgets remain unverified. Use 'lain usage' to inspect reported data.");
            if (provider.MonitorAccessOptions is { Length: > 0 })
            {
                await output.WriteLineAsync("Optional monitoring access belongs to this same account; it does not require entering the inference key again.");
                foreach (var option in provider.MonitorAccessOptions) if (!string.IsNullOrWhiteSpace(option.Description)) await output.WriteLineAsync(Cell(option.Description));
                var added = saved.Accounts.FirstOrDefault(account => !configuration.Accounts.Any(existing => existing.Id == account.Id));
                if (added is not null) await output.WriteLineAsync("Set up optional monitoring access: lain accounts monitor " + Cell(added.Id));
            }
            return true;
        }
        catch (WizardCancelledException) { return false; }
    }
}
