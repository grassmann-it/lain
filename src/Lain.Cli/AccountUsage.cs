using Lain.Client;
using Lain.Core;

namespace Lain.Cli;

// CLI composition only: the Host's AccountDto and QuotaDto contracts remain intact.
internal sealed record AccountUsageOverview(int Version, string Kind, AccountUsageItem[] Accounts);
internal sealed record AccountUsageItem(AccountDto Account, QuotaDto[] Quotas);

public static partial class CliApplication
{
    private static bool IsAccountUsage(Options options) => options.Positionals.Count == 0 && !options.HasAnyExcept("json", "full");

    private static async Task<AccountUsageOverview> AccountUsage(LainApiClient client, CancellationToken ct)
    {
        var accounts = await client.AccountsAsync(ct);
        var quotas = await client.QuotasAsync(ct);
        return new(1, "account-usage", accounts.Select(account => new AccountUsageItem(account,
            quotas.Where(quota => quota.AccountId == account.Id).ToArray())).ToArray());
    }

    internal static Task WriteAccountUsage(TextWriter output, AccountUsageOverview overview, HumanOutputContext? context = null, bool full = false) =>
        full ? WriteFullAccountUsage(output, overview, context) : WriteCompactAccountUsage(output, overview, context);

    private static async Task WriteFullAccountUsage(TextWriter output, AccountUsageOverview overview, HumanOutputContext? context)
    {
        context ??= HumanOutputContext.Detect(output);
        await HumanTable.WriteTextAsync(output, $"Account usage ({overview.Accounts.Length} accounts)", context);
        if (overview.Accounts.Length == 0) { await HumanTable.WriteTextAsync(output, "No accounts configured.", context); return; }
        foreach (var item in overview.Accounts.OrderByDescending(item => QuotaPresentation.HasQuotaReading(item.Quotas)))
        {
            var account = item.Account;
            await output.WriteLineAsync();
            await HumanTable.WriteTextAsync(output, $"{Cell(account.Name)} · {Cell(account.Provider)} · {(account.Enabled ? "Enabled" : "Disabled")}", context);
            await output.WriteLineAsync("  ID: " + Cell(account.Id));
            if (account.State is not ("ready" or "ok" or "available"))
                await HumanTable.WriteTextAsync(output, "Credential state: " + (account.State == "unverified" ? "Not checked" : ReadableState(account.State)), context, "  ");
            if (account.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
                await HumanTable.WriteTextAsync(output, "Credential expired.", context, "  ");
            if (account.SharedWithLegacy || item.Quotas.Any(quota => quota.SharedWithLegacy))
                await HumanTable.WriteTextAsync(output, "Shared budget with legacy usage; these are the same provider funds.", context, "  ");
            else if (account.BudgetPoolId is not null || item.Quotas.Any(quota => quota.BudgetPoolId is not null))
                await HumanTable.WriteTextAsync(output, "Provider budget pool · balances may be shared.", context, "  ");
            if (item.Quotas.Length == 0)
            {
                await HumanTable.WriteTextAsync(output, "Quota not reported. See provider setup for available access.", context, "  ");
            }
            else await WriteQuotaWindows(output, QuotaPresentation.Order(item.Quotas), context, disabled: !account.Enabled, checkAge: true);
        }
    }
}
