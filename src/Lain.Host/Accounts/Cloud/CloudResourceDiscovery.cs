using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lain.Core;

namespace Lain.Host.Accounts.Cloud;

public sealed class CloudResourceDiscovery(CloudProviderHttp http)
{
    public const string GoogleProvider = "gemini-api";
    public const string CloudflareProvider = "cloudflare-workers-ai";
    public static bool ProjectId(string? value) => value is not null && Regex.IsMatch(value, "^[a-z][a-z0-9-]{4,28}[a-z0-9]$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public static bool ProjectNumber(string? value) => value is { Length: > 0 and <= 30 } && value.All(char.IsAsciiDigit);
    public static bool CloudflareAccount(string? value) => value is { Length: 32 } && value.All(char.IsAsciiHexDigit);

    public async Task<ResourceBindingDto[]> GoogleProjectsAsync(string accessToken, CancellationToken ct = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30)); ct = deadline.Token;
        var projects = new Dictionary<string, ResourceBindingDto>(StringComparer.Ordinal);
        var tokens = new HashSet<string>(StringComparer.Ordinal); string? token = null;
        for (var page = 0; page < 8; page++)
        {
            using var request = CloudProviderHttp.Get("https://cloudresourcemanager.googleapis.com/v3/projects:search?pageSize=100" +
                (token is null ? "" : "&pageToken=" + Uri.EscapeDataString(token)), accessToken);
            using var document = await http.ReadAsync(request, ct);
            var root = document.RootElement;
            if (root.TryGetProperty("projects", out var rows))
            {
                if (rows.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid project discovery response.");
                foreach (var row in rows.EnumerateArray())
                {
                    if (Text(row, "state") != "ACTIVE") continue;
                    var id = Text(row, "projectId"); var name = Text(row, "name");
                    var number = name?.StartsWith("projects/", StringComparison.Ordinal) == true ? name[9..] : null;
                    if (!ProjectId(id) || !ProjectNumber(number)) throw new InvalidDataException("Invalid discovered project identity.");
                    // Subject is the immutable project number, never the browser user's identity.
                    var binding = new ResourceBindingDto(GoogleProvider, Subject: number, Product: "generative-language", ResourceType: "project", ResourceId: id, Provenance: "provider-discovery");
                    if (projects.TryGetValue(id!, out var prior) && prior != binding) throw new InvalidDataException("Project identity changed during discovery.");
                    projects[id!] = binding;
                    if (projects.Count > 64) throw new InvalidDataException("Project discovery exceeds the selection bound. Use a narrower authorized grant.");
                }
            }
            token = Text(root, "nextPageToken");
            if (string.IsNullOrEmpty(token)) return projects.Values.OrderBy(p => p.ResourceId, StringComparer.Ordinal).ToArray();
            if (token.Length > 4096 || !tokens.Add(token)) throw new InvalidDataException("Project discovery pagination is invalid.");
        }
        throw new InvalidDataException("Project discovery exceeds the page bound.");
    }

    public async Task<ResourceBindingDto[]> CloudflareAccountsAsync(string accessToken, CancellationToken ct = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30)); ct = deadline.Token;
        var accounts = new Dictionary<string, ResourceBindingDto>(StringComparer.Ordinal);
        for (var page = 1; page <= 8; page++)
        {
            using var request = CloudProviderHttp.Get("https://api.cloudflare.com/client/v4/accounts?per_page=50&page=" + page.ToString(CultureInfo.InvariantCulture), accessToken);
            using var document = await http.ReadAsync(request, ct);
            var root = document.RootElement;
            if (!root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True ||
                !root.TryGetProperty("result", out var rows) || rows.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Cloudflare account discovery was not successful.");
            foreach (var row in rows.EnumerateArray())
            {
                var id = Text(row, "id");
                if (!CloudflareAccount(id)) throw new InvalidDataException("Invalid discovered Cloudflare account identity.");
                accounts[id!] = new(CloudflareProvider, Subject: id, Product: "workers-ai", ResourceType: "account", ResourceId: id, Provenance: "provider-discovery");
                if (accounts.Count > 64) throw new InvalidDataException("Account discovery exceeds the selection bound. Use a narrower authorized grant.");
            }
            if (!root.TryGetProperty("result_info", out var info) || !info.TryGetProperty("total_pages", out var pages) || !pages.TryGetInt32(out var totalPages)
                || totalPages < 1 || !info.TryGetProperty("page", out var current) || !current.TryGetInt32(out var currentPage) || currentPage != page)
                throw new InvalidDataException("Cloudflare discovery pagination is incomplete.");
            if (page >= totalPages) return accounts.Values.OrderBy(a => a.ResourceId, StringComparer.Ordinal).ToArray();
        }
        throw new InvalidDataException("Account discovery exceeds the page bound.");
    }
    internal static string? Text(JsonElement node, string name) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
