using Lain.Client;
using Lain.Core;

namespace Lain.Cli;

public static partial class CliApplication
{
    private static async Task<bool> MonitorAccountWizard(LainApiClient client, string accountId, TextWriter output,
        IAccountWizardTerminal terminal, CancellationToken ct)
    {
        try
        {
            var configuration = await client.GetConfigurationAsync(ct);
            var account = configuration.Accounts.FirstOrDefault(account => account.Id == accountId)
                ?? throw new ArgumentException("Choose an existing account ID from 'lain accounts'.");
            var catalog = await client.ProvidersAsync(ct);
            var provider = catalog.Providers.FirstOrDefault(provider => provider.Id == account.Provider);
            var current = await client.MonitorAccessAsync(account.Id, ct);
            await output.WriteLineAsync("Monitoring access for " + Cell(account.Name) + ". Press Esc or Ctrl-C to cancel.");
            await output.WriteLineAsync(current.State == "configured-unverified" ? "Saved locally; monitor access not checked."
                : current.State is "not-configured" or "unsupported" ? "No separate monitoring access saved." : ReadableState(current.State));
            if (current.ExpiresAt is { } expiresAt) await output.WriteLineAsync("Saved token expires " + Timestamp(expiresAt) + " UTC");
            if (!string.IsNullOrWhiteSpace(current.Guidance)) await output.WriteLineAsync(Cell(current.Guidance));
            var options = provider?.MonitorAccessOptions ?? [];
            var methods = await client.ConnectionMethodsAsync(account.Provider, ct);
            var available = methods.Methods.Where(method => method.Availability == "available"
                && (configuration.RuntimeMode != "monitor" || !method.RequiresCore)
                && method.Purposes.Any(purpose => purpose is "quota" or "cost")).ToArray();
            var operations = new List<string>();
            if (options.Length > 0 || available.Length > 0 || current.State is not ("not-configured" or "unsupported"))
                operations.Add("Refresh quota with saved access");
            if (options.Length > 0) operations.Add("Configure additional monitoring access");
            if (available.Any(method => method.Kind != "monitor-token")) operations.Add("Connect or reconnect quota access on this account");
            if (current.State is not ("not-configured" or "unsupported")) operations.Add("Remove local monitoring access");
            if (operations.Count == 0)
            {
                await output.WriteLineAsync("No direct quota connection is available for this provider on the installed host.");
                if ((current.DocumentationUrl ?? provider?.AccountUrl) is { } link) await output.WriteLineAsync("Provider console: " + Cell(link));
                return false;
            }
            var operation = await WizardChoice(output, terminal, "Monitoring action", operations, item => item, ct);
            if (operation == "Refresh quota with saved access")
            {
                var quotas = await client.RefreshQuotasAsync(account.Id, ct);
                await WriteQuotas(output, quotas.Where(quota => quota.AccountId == account.Id).ToArray());
                return true;
            }
            if (operation == "Connect or reconnect quota access on this account")
                return await AddAccountWizard(client, output, terminal, ct, account);
            if (operation == "Remove local monitoring access")
            {
                var remove = await WizardValue(terminal, "Remove local monitoring access? Type yes", false, ct, required: false);
                if (!remove.Equals("yes", StringComparison.OrdinalIgnoreCase)) return false;
                await client.RemoveMonitorAccessAsync(configuration.Revision, account.Id, ct);
                await output.WriteLineAsync("Monitoring access removed locally. Inference credentials remain saved; external access was not revoked.");
                return true;
            }
            var selected = await WizardChoice(output, terminal, "Monitor access method", options, option => option.Name, ct);
            if (!string.IsNullOrWhiteSpace(selected.Description)) await output.WriteLineAsync(Cell(selected.Description));
            await output.WriteLineAsync("Official access instructions: " + Cell(selected.DocumentationUrl));
            await output.WriteLineAsync("Required read permissions: " + Cell(string.Join(", ", selected.Scopes)));
            if (!string.IsNullOrWhiteSpace(selected.ResourceHelp)) await output.WriteLineAsync(Cell(selected.ResourceHelp));
            var resource = string.IsNullOrWhiteSpace(selected.ResourceLabel) ? null
                : await WizardValue(terminal, selected.ResourceLabel, false, ct, current.ResourceId ?? account.Binding?.ResourceId);
            var secret = selected.SecretRequired ? await WizardValue(terminal, "Monitoring token (hidden)", true, ct) : null;
            DateTimeOffset? expiry = null;
            if (selected.Kind == "access-token-snapshot")
            {
                await output.WriteLineAsync("Use the token's actual expiry. lain never refreshes or revokes external snapshots.");
                expiry = ParseTime(await WizardValue(terminal, "Expires at (ISO timestamp with timezone)", false, ct));
                if (expiry <= DateTimeOffset.UtcNow) throw new ArgumentException("The token expiry must be in the future. No monitoring access was saved.");
            }
            await output.WriteLineAsync("Saving is local. Quota refresh and real inference are separate actions.");
            var confirm = await WizardValue(terminal, "Save monitoring access? Type yes", false, ct, required: false);
            if (!confirm.Equals("yes", StringComparison.OrdinalIgnoreCase)) return false;
            await client.SetMonitorAccessAsync(configuration.Revision, account.Id, new(selected.Kind, secret, resource, expiry), ct);
            secret = null;
            await output.WriteLineAsync("Monitoring access saved on the existing account. Refresh quota separately to check the configured source.");
            return true;
        }
        catch (WizardCancelledException) { return false; }
    }
}
