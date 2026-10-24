using Lain.Client;
using Lain.Core;

namespace Lain.Desktop.Providers;

/// <summary>One presentation inventory: documentation plus installed methods, without invented access facts.</summary>
internal static class ProviderInventory
{
    public static string Canonical(string id) => id switch { "command-code" => "commandcode", "mimo" => "xiaomi-mimo", _ => id };

    public static ProviderCatalogDto Merge(ProviderCatalogDto? documentation, ConnectionMethodCatalogDto? methods)
    {
        var providers = (documentation?.Providers ?? []).ToList();
        foreach (var group in (methods?.Methods ?? []).GroupBy(m => m.Provider).Where(g => !providers.Any(p => p.Id == g.Key)))
        {
            var url = group.Select(m => m.DocumentationUrl).FirstOrDefault(u => u is not null) ?? "";
            providers.Add(new(group.Key, ProviderDisplayNames.Get(group.Key), "Installed methods: " + string.Join(" · ", group.Select(m => m.Title)),
                "", url, [], [], [], [], [], [], AdapterState: "installed-methods", QuotaSupport: "unknown"));
        }
        return new(documentation?.ContractVersion ?? 1, documentation?.ObservedAt ?? DateTimeOffset.UtcNow,
            providers.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
