namespace Lain.Host.Providers.NanoGpt;

/// <summary>The documented management secret is never an inference credential.</summary>
public static class NanoGptCredentialPolicy
{
    public static bool IsManagementToken(string? value) => Valid(value, "sk-nano-mgmt-");
    public static bool IsInferenceKey(string? value) => Valid(value, "sk-nano-")
        && !value!.StartsWith("sk-nano-mgmt-", StringComparison.Ordinal);
    private static bool Valid(string? value, string prefix) => value is not null
        && value.Length > prefix.Length && value.Length <= 16384
        && value.StartsWith(prefix, StringComparison.Ordinal)
        && !value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c));
}
