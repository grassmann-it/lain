using Lain.Core;

namespace Lain.Client;

/// <summary>Compact labels for presentation only; saved names and identities remain unchanged.</summary>
public static class AccountDisplayNames
{
    // Pass the complete inventory, before filters/sorts, so a label always identifies
    // the same account in CLI and desktop. Normalization is renderer-owned (e.g. ANSI).
    public static string Get(AccountDto account, IReadOnlyList<AccountDto> accounts,
        Func<string, string>? normalize = null)
    {
        normalize ??= value => value;
        var inventory = accounts.Any(item => item.Id == account.Id) ? accounts.ToArray() : accounts.Append(account).ToArray();
        var labels = inventory.Select(item => Compact(item, inventory, normalize)).ToArray();
        var occupied = labels.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < inventory.Length; index++)
        {
            var label = labels[index];
            if (labels.Count(candidate => candidate.Equals(label, StringComparison.OrdinalIgnoreCase)) > 1)
            {
                // A custom label can also collide with another account's generated
                // label. Keep both readable without exposing IDs or changing storage.
                var suffix = index + 1;
                do { label = $"{labels[index]} [{suffix++}]"; }
                while (occupied.Contains(label) || unique.Contains(label));
            }
            unique.Add(label);
            if (inventory[index].Id == account.Id) return label;
        }
        return normalize(account.Name);
    }

    private static string Compact(AccountDto account, AccountDto[] accounts, Func<string, string> normalize)
    {
        var provider = account.Provider;
        var display = normalize(provider.ToLowerInvariant() switch
        {
            "nvidia" or "nvidia-build" => "NVIDIA",
            "cloudflare" or "cloudflare-workers-ai" => "Cloudflare",
            "google" or "google-gemini" or "gemini" or "gemini-api" => "Gemini",
            "xiaomi-mimo" or "mimo" => "MiMo",
            _ => ProviderDisplayNames.Get(provider)
        });
        var name = normalize(account.Name);
        var siblings = accounts.Where(item => string.Equals(item.Provider, provider, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (siblings.Length == 1 && System.Net.Mail.MailAddress.TryCreate(name, out var address)
            && string.Equals(address.Address, name, StringComparison.OrdinalIgnoreCase)) return display;
        var known = new[] { provider, display, ProviderDisplayNames.Get(provider),
            "My " + provider + " account", "My " + display + " account", "My " + ProviderDisplayNames.Get(provider) + " account" };
        if (!known.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            var sameName = accounts.Where(item => string.Equals(normalize(item.Name), name, StringComparison.OrdinalIgnoreCase)).ToArray();
            return sameName.Length == 1 ? name : name + " (" + display + " "
                + (Array.FindIndex(sameName, item => item.Id == account.Id) + 1) + ")";
        }
        return siblings.Length == 1 ? display : display + " " + (Array.FindIndex(siblings, item => item.Id == account.Id) + 1);
    }
}
