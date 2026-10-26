using System.Net;
using Lain.Client;
using Lain.Core;

namespace Lain.Cli;

public static partial class CliApplication
{
    private static async Task<MonitorAccessDto> ConfigureMonitorFromInput(LainApiClient client, Options options,
        TextReader input, CancellationToken ct)
    {
        var configuration = await client.GetConfigurationAsync(ct);
        var revision = options.Get("revision") is { } supplied ? ParseLong(supplied, "revision") : configuration.Revision;
        if (revision != configuration.Revision)
            throw new LainApiException(HttpStatusCode.Conflict, "revision_conflict", "Configuration changed; reload before configuring monitoring access.");
        var account = configuration.Accounts.FirstOrDefault(account => account.Id == options.Positionals[1])
            ?? throw new ArgumentException("Choose an existing account ID from 'lain accounts'.");
        var catalog = await client.ProvidersAsync(ct);
        var selected = catalog.Providers.FirstOrDefault(provider => provider.Id == account.Provider)?.MonitorAccessOptions
            ?.SingleOrDefault(option => option.Kind == options.Get("kind"))
            ?? throw new ArgumentException("The requested monitoring kind is not available for this account. Check the provider catalog.");
        if (!selected.SecretRequired)
            throw new ArgumentException("This monitoring option does not need a token. Use accounts monitor ID to configure it privately.");
        if (!selected.ResourceRequired && options.Has("resource"))
            throw new ArgumentException("This monitoring option has no resource selector. Omit --resource.");
        var resource = selected.ResourceRequired ? options.Get("resource")?.Trim() : null;
        if (selected.ResourceRequired && string.IsNullOrWhiteSpace(resource))
            throw new ArgumentException("--resource is required for this monitoring option. Use the provider's documented resource ID.");
        var expiry = ParseTime(options.Get("expires-at"));
        if (selected.Kind == "access-token-snapshot" && expiry is null)
            throw new ArgumentException("--expires-at is required for an access-token snapshot.");
        if (expiry <= DateTimeOffset.UtcNow)
            throw new ArgumentException("The token expiry must be in the future. No monitoring access was saved.");
        var secret = await ReadMonitorToken(input, ct);
        try
        {
            await client.SetMonitorAccessAsync(revision, account.Id, new(selected.Kind, secret, resource, expiry), ct);
        }
        finally { secret = ""; }
        return new(account.Id, account.Provider, "configured-unverified", selected.Kind, resource, expiry,
            "Saved locally. Refresh quota separately to check the configured source.", selected.DocumentationUrl);
    }

    private static async Task<string> ReadMonitorToken(TextReader input, CancellationToken ct)
    {
        // Bound private redirected input before allocating a credential string.
        var buffer = new char[4098];
        var length = 0;
        try
        {
            while (length < buffer.Length)
            {
                if (await input.ReadAsync(buffer.AsMemory(length, 1), ct) == 0 || buffer[length] == '\n') break;
                length++;
            }
            if (length > 0 && buffer[length - 1] == '\r') length--;
            if (length is 0 or > 4096)
                throw new ArgumentException("The monitoring token input is empty or too long.");
            var secret = new string(buffer, 0, length);
            if (secret.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
                throw new ArgumentException("The monitoring token input contains unsupported whitespace or control characters.");
            return secret;
        }
        finally { Array.Clear(buffer); }
    }
}
