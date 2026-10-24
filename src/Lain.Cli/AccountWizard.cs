using Lain.Client;
using Lain.Core;

namespace Lain.Cli;

public static partial class CliApplication
{
    private static async Task<bool> AddAccountWizard(LainApiClient client, TextWriter output, IAccountWizardTerminal terminal, CancellationToken ct,
        AccountDto? reconnect = null)
    {
        string? activeLogin = null;
        try
        {
            await output.WriteLineAsync("Private account setup. Esc/Ctrl-C cancels. Credentials stay with your local host.");
            var catalog = await client.ConnectionMethodsAsync(reconnect?.Provider, ct);
            var cfg = await client.GetConfigurationAsync(ct);
            if (reconnect is not null) reconnect = cfg.Accounts.FirstOrDefault(account => account.Id == reconnect.Id)
                ?? throw new ArgumentException("This account is no longer configured.");
            var monitor = cfg.RuntimeMode == "monitor";
            bool Available(ProviderConnectionMethodDto method) => method.Availability == "available"
                && (!monitor || !method.RequiresCore)
                && (!(monitor || reconnect is not null) || method.Purposes.Any(p => p is "quota" or "cost"));
            var providers = catalog.Methods.Select(m => m.Provider).Distinct(StringComparer.Ordinal).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            var provider = reconnect is null ? await WizardChoice(output, terminal, "Provider", providers, ProviderDisplayNames.Get, ct)
                : providers.FirstOrDefault() ?? reconnect.Provider;
            var methods = catalog.Methods.Where(m => m.Provider == provider).OrderByDescending(m => m.Recommended).ToArray();
            foreach (var blocked in methods.Where(m => !Available(m)))
                await output.WriteLineAsync(Cell(blocked.Title + ": " + (blocked.Guidance ?? "Unavailable for this host mode. Choose another method.")));
            var available = methods.Where(Available).ToArray();
            var method = available.Length == 1 ? available[0] : await WizardChoice(output, terminal, "Connection method", available, m => m.Title, ct);
            if (method.Description is { } description) await output.WriteLineAsync(Cell(description));
            if (method.DocumentationUrl is { } url) await output.WriteLineAsync("Official guidance: " + Cell(url));
            var settings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in method.RequiredSettings ?? [])
            {
                if (field.Help is { } help) await output.WriteLineAsync(Cell(help));
                var value = field.Choices is { Length: > 0 } choices
                    ? choices.Length == 1 ? choices[0] : await WizardChoice(output, terminal, field.Label, choices, choice => choice, ct)
                    : await WizardValue(terminal, field.Label, false, ct, field.Id switch
                    {
                        "projectId" or "accountId" or "workspaceId" or "resourceId" => reconnect?.Binding?.ResourceId,
                        "region" => reconnect?.Binding?.Region, "product" => reconnect?.Binding?.Product, _ => null
                    }, required: field.Required);
                if (value.Length > 0) settings[field.Id] = value;
            }
            var suggested = AccountNameSuggestions.Suggest(provider, ProviderDisplayNames.Get(provider), cfg.Accounts.Select(a => a.Name));
            var name = reconnect?.Name ?? await WizardValue(terminal, "Account name", false, ct, suggested);
            var purposes = monitor || reconnect is not null ? method.Purposes.Where(p => p is "quota" or "cost").ToArray() : method.Purposes;
            var key = method.SecretRequired ? await WizardValue(terminal, (method.SecretKind ?? "Credential") + " (hidden)", true, ct) : "";
            DateTimeOffset? expiresAt = null;
            if (method.SecretKind == "access-token-snapshot")
            {
                var expiry = await WizardValue(terminal, "Snapshot expires at (ISO8601 with timezone)", false, ct);
                if (!DateTimeOffset.TryParse(expiry, out var suppliedExpiry) || suppliedExpiry <= DateTimeOffset.UtcNow) throw new ArgumentException("The snapshot requires its future expiry.");
                expiresAt = suppliedExpiry;
            }
            await output.WriteLineAsync("Connect " + Cell(name) + " · " + Cell(method.Title) + " · " + string.Join(", ", purposes.Select(Cell)) + ". Models are optional later.");
            var confirm = await WizardValue(terminal, "Connect? Type yes", false, ct, required: false);
            if (!confirm.Equals("yes", StringComparison.OrdinalIgnoreCase)) return false;
            var resourceKey = settings.ContainsKey("projectId") ? "projectId" : settings.ContainsKey("accountId") ? "accountId" : settings.ContainsKey("workspaceId") ? "workspaceId" : "resourceId";
            var binding = reconnect?.Binding ?? (settings.Count == 0 ? null : new ResourceBindingDto(provider, settings.GetValueOrDefault("subject"), settings.GetValueOrDefault("product"), settings.GetValueOrDefault("region"),
                resourceKey == "projectId" ? "project" : resourceKey == "accountId" ? "account" : resourceKey == "workspaceId" ? "workspace" : null, settings.GetValueOrDefault(resourceKey)));
            var operationKey = Guid.NewGuid().ToString("N");
            ConfigurationDto saved;
            if (method.SecretRequired)
            {
                saved = await client.ConnectAccountAsync(cfg.Revision, new(new(provider, method.Id, purposes, name, AccountId: reconnect?.Id, Binding: binding,
                    ExpectedGeneration: reconnect?.Generation, IdempotencyKey: operationKey), key,
                    method.SecretKind ?? "api-key", ExpiresAt: expiresAt, BaseUrl: method.DefaultBaseUrl, Settings: settings), ct);
                key = "";
            }
            else
            {
                var login = await client.LoginAsync(new LoginRequest(cfg.Revision, provider, method.Id, purposes, AccountId: reconnect?.Id, Binding: binding,
                    ExpectedGeneration: reconnect?.Generation, Name: name, IdempotencyKey: operationKey), ct);
                activeLogin = login.Id;
                while (login.State is "waiting" or "pending" or "resource-required" or "awaiting-resource")
                {
                    if (login.Url is { } authorization) await output.WriteLineAsync("Continue in your browser: " + Cell(authorization));
                    if (login.Resources is { Length: > 0 } resources)
                    {
                        var resource = await WizardChoice(output, terminal, "Verified resource", resources,
                            item => string.Join(" · ", new[] { item.Product, item.Region, item.ResourceType, item.ResourceId }.Where(x => x is not null)), ct);
                        login = await client.SelectLoginResourceAsync(login.Id, new(cfg.Revision, resource), ct);
                    }
                    else
                    {
                        await WizardValue(terminal, "Complete the provider step, then press Enter to check (Esc cancels)", false, ct, required: false);
                        login = await client.GetLoginAsync(login.Id, ct);
                    }
                }
                activeLogin = null;
                if (login.State != "completed") throw new InvalidOperationException("Connection " + Cell(login.State) + ". " + Cell(login.NextAction ?? login.Error));
                saved = await client.GetConfigurationAsync(ct);
            }
            var account = reconnect is not null ? saved.Accounts.FirstOrDefault(a => a.Id == reconnect.Id)
                : saved.Accounts.FirstOrDefault(a => !cfg.Accounts.Any(previous => previous.Id == a.Id));
            await output.WriteLineAsync("Credential connected locally. Provider access, quota observations and inference are separate facts.");
            if (account is not null)
            {
                foreach (var capability in account.Capabilities ?? []) await output.WriteLineAsync(Cell(capability.Purpose + ": " + capability.State));
                try
                {
                    var quotas = (await client.QuotasAsync(ct)).Where(q => q.AccountId == account.Id).ToArray();
                    if (quotas.Length == 0) await output.WriteLineAsync("No quota observation yet. Refresh quota explicitly from the account.");
                    else await WriteQuotas(output, quotas);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                { await output.WriteLineAsync("Quota observation unavailable. The account remains connected; check it from Accounts."); }
                if (!monitor && purposes.Contains("inference")) await output.WriteLineAsync("Optional: choose models for account " + Cell(account.Id) + ". No models or profiles were enabled.");
            }
            return true;
        }
        catch (WizardCancelledException)
        {
            if (activeLogin is not null) await client.CancelLoginAsync(activeLogin, CancellationToken.None);
            return false;
        }
        catch (OperationCanceledException)
        {
            if (activeLogin is not null) await client.CancelLoginAsync(activeLogin, CancellationToken.None);
            throw;
        }
    }
}
