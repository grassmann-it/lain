using Lain.Core;

namespace Lain.Host.Accounts.Monitoring;

/// <summary>Verified authentication choices, separate from the existence of a reporting source.</summary>
public static class MonitorAccessCatalog
{
    public const string GoogleMonitoringScope = "https://www.googleapis.com/auth/monitoring.read";
    public const string GoogleMonitoringDocs = "https://docs.cloud.google.com/monitoring/api/ref_v3/rest/v3/projects.timeSeries/list";
    public const string CloudflareAnalyticsDocs = "https://developers.cloudflare.com/analytics/graphql-api/getting-started/authentication/api-token-auth/";
    public const string FireworksQuotaDocs = "https://docs.fireworks.ai/api-reference/list-quotas";

    public static MonitorAccessOptionDto[] Options(string providerId) => Provider(providerId) switch
    {
        "fireworks" =>
        [
            new("inference-key", "Use existing Fireworks API key", false, "Fireworks account ID",
                "Account ID from Fireworks account settings, without the accounts/ prefix.", FireworksQuotaDocs, [],
                "Uses the current inference key for read-only quota requests. The key must have quota-read access; inference alone does not prove that permission."),
            new("api-token", "Separate Fireworks API key", true, "Fireworks account ID",
                "Use the same Fireworks account as the inference key.", FireworksQuotaDocs, [],
                "Optional when a separate key with suitable quota-read access is needed. The provider's minimum quota-read role is not documented.")
        ],
        "gemini-api" =>
        [
            new("access-token-snapshot", "Google Monitoring access-token snapshot", true, "Google project ID",
                "The Google Cloud project used by this Gemini inference account.", GoogleMonitoringDocs, [GoogleMonitoringScope],
                "Usage only when the project exports matching metrics. Requires a future token expiry, Monitoring API access and monitoring.timeSeries.list permission. External tokens are never refreshed or revoked; desktop OAuth setup is not implemented.")
        ],
        "cloudflare-workers-ai" =>
        [
            new("inference-key", "Use existing Cloudflare API token", false, "Cloudflare account ID",
                "The 32-character account ID must match this account's inference route.", CloudflareAnalyticsDocs, ["Account Analytics Read"],
                "Authentication capability only: a Workers AI usage dataset and reader are not yet qualified. An existing token with this permission needs no replacement."),
            new("api-token", "Separate Cloudflare Analytics token", true, "Cloudflare account ID",
                "Restrict the token to this account and Account > Account Analytics > Read.", CloudflareAnalyticsDocs, ["Account Analytics Read"],
                "Optional least-privilege analytics access. A Workers AI usage dataset and reader are not yet qualified; saving this token does not enable reporting.")
        ],
        _ => []
    };

    internal static string Provider(string providerId) => providerId switch
    {
        "gemini" or "gemini-api" or "generativelanguage.googleapis.com" => "gemini-api",
        "cloudflare" or "cloudflare-workers-ai" or "api.cloudflare.com" => "cloudflare-workers-ai",
        "fireworks" or "fireworks-ai" or "api.fireworks.ai" => "fireworks",
        _ => providerId
    };
}
