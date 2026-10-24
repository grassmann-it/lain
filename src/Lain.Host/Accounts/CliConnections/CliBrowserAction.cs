namespace Lain.Host.Accounts.CliConnections;

/// <summary>Only call for a separately qualified provider output field; arbitrary stdout is never scanned for URLs.</summary>
public static class CliBrowserAction
{
    public static string? Validate(string candidate, IReadOnlySet<string> qualifiedHosts, IReadOnlySet<string>? qualifiedQueryNames = null)
    {
        if (candidate.Length > 8192 || candidate.Any(char.IsControl) || !Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0
            || !qualifiedHosts.Contains(uri.Host)) return null;
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string name;
            try { name = Uri.UnescapeDataString(part.Split('=')[0]); } catch (UriFormatException) { return null; }
            if (name.Contains("token", StringComparison.OrdinalIgnoreCase) || name.Contains("verifier", StringComparison.OrdinalIgnoreCase)
                || name.ToLowerInvariant() is "code" or "api_key" or "key" || qualifiedQueryNames?.Contains(name) != true) return null;
        }
        return uri.AbsoluteUri;
    }
}
