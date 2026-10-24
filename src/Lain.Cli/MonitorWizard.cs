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
            await output.WriteLineAsync(current.State == "configured-unverified" ? "Saved locally; monitor access not checked." : ReadableState(current.State));
            if (current.ExpiresAt is { } expiresAt) await output.WriteLineAsync("Saved token expires " + Timestamp(expiresAt) + " UTC");
            if (!string.IsNullOrWhiteSpace(current.Guidance)) await output.WriteLineAsync(Cell(current.Guidance));
            var options = provider?.MonitorAccessOptions ?? [];
            if (current.State != "not-configured")
            {
                var operation = await WizardChoice(output, terminal, "Monitoring action", new[] { "Set or replace access", "Remove local monitoring access" }, item => item, ct);
                if (operation == "Remove local monitoring access")
                {
                    var remove = await WizardValue(terminal, "Remove local monitoring access? Type yes", false, ct, required: false);
                    if (!remove.Equals("yes", StringComparison.OrdinalIgnoreCase)) return false;
                    await client.RemoveMonitorAccessAsync(configuration.Revision, account.Id, ct);
                    await output.WriteLineAsync("Monitoring access removed locally. Inference credentials remain saved; external access was not revoked.");
                    return true;
                }
            }
            if (options.Length == 0)
            {
                await output.WriteLineAsync("No additional monitoring credential flow is implemented for this provider.");
                if ((current.DocumentationUrl ?? provider?.AccountUrl) is { } link) await output.WriteLineAsync("Official guidance: " + Cell(link));
                return false;
            }
            var selected = await WizardChoice(output, terminal, "Monitor access method", options, option => option.Name, ct);
            if (!string.IsNullOrWhiteSpace(selected.Description)) await output.WriteLineAsync(Cell(selected.Description));
            await output.WriteLineAsync("Official access instructions: " + Cell(selected.DocumentationUrl));
            await output.WriteLineAsync("Required read permissions: " + Cell(string.Join(", ", selected.Scopes)));
            if (!string.IsNullOrWhiteSpace(selected.ResourceHelp)) await output.WriteLineAsync(Cell(selected.ResourceHelp));
            var resource = string.IsNullOrWhiteSpace(selected.ResourceLabel) ? null
                : await WizardValue(terminal, selected.ResourceLabel, false, ct, current.ResourceId);
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
