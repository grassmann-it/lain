using System.Security.Cryptography;

namespace Lain.Host.Accounts.CliConnections;

/// <summary>The tap commit pins publication/checksum, not the proprietary CLI's source.</summary>
public sealed record FireworksCliQualification(string ExecutablePath)
{
    public const string Version = "1.8.19";
    public const string ExecutableSha256 = "dfc6f83e3af156b44a3d27237b192efae431c00fce9e56e62b8c71ea5cf8802f";
    public const string ArchiveSha256 = "f6fac5dc456b526d9378af9d5240aa92f98c927a9ba2ca8a00fd5f41938c0353";
    public const string TapCommit = "a3ecce3207b1d2f1c5ecad15a5006478a000d86c";
    public const string Download = "https://storage.googleapis.com/fireworks-public/firectl/stable/linux-amd64.gz";
    public bool IsAvailable
    {
        get { try { Validate(); return true; } catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or PlatformNotSupportedException) { return false; } }
    }
    public void Validate()
    {
        if (!OperatingSystem.IsLinux() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64
            || !File.Exists("/usr/bin/bwrap")) throw new PlatformNotSupportedException("Fireworks CLI requires qualified Linux x64 isolation.");
        CliQualificationManifest.ValidateExecutable(ExecutablePath);
        using var file = File.OpenRead(ExecutablePath);
        if (!Convert.ToHexString(SHA256.HashData(file)).Equals(ExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Fireworks CLI artifact differs from its pinned release.");
    }
}
