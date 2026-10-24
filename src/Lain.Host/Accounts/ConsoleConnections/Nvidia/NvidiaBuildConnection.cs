using Lain.Core;

namespace Lain.Host.Accounts.ConsoleConnections.Nvidia;

/// <summary>Build's website login is not a qualified delegated application login.</summary>
public static class NvidiaBuildConnection
{
    public const string Provider = "nvidia-build";
    public const string Documentation = "https://docs.api.nvidia.com/nim/docs/api-quickstart";
    public const string ReportingPrerequisite = "NVIDIA Build account reporting requires a verified account-bound usage/remaining endpoint and an authorized owned-session handoff. Neither is established by the Build inference key or NGC CLI configuration.";
    public const string LoginPrerequisite = "NVIDIA Build has no qualified public OAuth/device flow or CLI browser login for lain. Its website provisions inference keys; an owned console connection needs NVIDIA's supported third-party session/callback contract and account identity before it can be enabled.";

    public static QuotaSupportDto QuotaSupport(string? publicReference = null) => new("setup-required",
        (publicReference is null ? "" : publicReference + " ") + ReportingPrerequisite, Documentation);

    public static ProviderConnectionMethodDto Method() => new(Provider, "console", "Connect NVIDIA Build reporting", "browser",
        ["quota"], "setup-required", false, false, Description: "Separate account reporting for an existing NVIDIA Build inference account.",
        DocumentationUrl: Documentation, Guidance: LoginPrerequisite + " " + ReportingPrerequisite);
}
