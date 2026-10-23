using System.Text.RegularExpressions;

namespace Lain.Host.Providers;

/// <summary>Non-secret configuration; a provider offer does not prove a live free balance.</summary>
public sealed record TrialProviderConfiguration(
    string ProviderId,
    string Model,
    string BillingPlan = "payg",
    string CostPolicy = "configured",
    string? Region = null,
    string? WorkspaceId = null,
    string? ServiceDeploymentScope = null,
    string? BaseUrl = null);

public static partial class TrialProviderConfigurationValidator
{
    private static readonly HashSet<string> AlibabaRegions = new(StringComparer.Ordinal)
    {
        "cn-beijing", "ap-southeast-1", "eu-central-1", "ap-northeast-1", "cn-hongkong", "us-east-1"
    };

    public static Uri Validate(TrialProviderConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.Model) || configuration.Model != configuration.Model.Trim() ||
            configuration.Model.Any(char.IsControl))
            throw new ArgumentException("Select a concrete model ID.");
        if (configuration.CostPolicy == "free_only")
            throw new ArgumentException("This trial route has no qualified provider-enforced free-only billing cutoff.");
        if (configuration.CostPolicy != "configured")
            throw new ArgumentException("Unknown cost policy.");

        string expected;
        switch (configuration.ProviderId)
        {
            case "inception":
                RequirePlan(configuration, "new-account-trial", "payg");
                if (configuration.Model != "mercury-3")
                    throw new ArgumentException("This prepared Inception adapter supports only the documented mercury-3 route.");
                RejectRegionalFields(configuration);
                expected = "https://api.inceptionlabs.ai/v1";
                break;
            case "nebius":
                RequirePlan(configuration, "signup-trial", "builder-offer", "payg");
                RejectRegionalFields(configuration);
                expected = "https://api.tokenfactory.nebius.com/v1";
                break;
            case "alibaba-model-studio":
                // Coding Plan's official terms exclude this application's autonomous/backend use.
                RequirePlan(configuration, "new-user-model-trial", "payg");
                if (configuration.Region is null || !AlibabaRegions.Contains(configuration.Region))
                    throw new ArgumentException("Choose a documented Model Studio region.");
                if (configuration.WorkspaceId is null || !WorkspaceLabel().IsMatch(configuration.WorkspaceId))
                    throw new ArgumentException("Copy a valid workspace ID from Model Studio Workspace Management.");
                if (string.IsNullOrWhiteSpace(configuration.ServiceDeploymentScope))
                    throw new ArgumentException("Select the workspace's service deployment scope.");
                if (configuration.BillingPlan == "new-user-model-trial" &&
                    (configuration.Region != "ap-southeast-1" ||
                     !string.Equals(configuration.ServiceDeploymentScope, "International", StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException("The documented Model Studio trial requires Singapore and International scope.");
                expected = $"https://{configuration.WorkspaceId}.{configuration.Region}.maas.aliyuncs.com/compatible-mode/v1";
                break;
            default:
                throw new ArgumentException("Unsupported trial provider.");
        }

        // An exact allowlist prevents accidental region mixing, coding-plan reuse and credential forwarding.
        if (configuration.BaseUrl is not null &&
            !string.Equals(configuration.BaseUrl.TrimEnd('/'), expected, StringComparison.Ordinal))
            throw new ArgumentException("The endpoint does not match the provider, region and workspace configuration.");
        return new Uri(expected + "/", UriKind.Absolute);
    }

    private static void RequirePlan(TrialProviderConfiguration configuration, params string[] plans)
    {
        if (!plans.Contains(configuration.BillingPlan, StringComparer.Ordinal))
            throw new ArgumentException("This billing plan is not supported for autonomous backend use.");
    }

    private static void RejectRegionalFields(TrialProviderConfiguration configuration)
    {
        if (configuration.Region is not null || configuration.WorkspaceId is not null || configuration.ServiceDeploymentScope is not null)
            throw new ArgumentException("This provider does not use Model Studio region/workspace fields.");
    }

    [GeneratedRegex("\\A[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\\z", RegexOptions.CultureInvariant)]
    private static partial Regex WorkspaceLabel();
}
