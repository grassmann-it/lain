using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Lain.Host.Accounts.CliConnections;

/// <summary>Trusted maintainer evidence, never a user-supplied enable switch or download URL.</summary>
public sealed record CliQualificationManifest(string Provider, string Release, string SourceCommit, Uri Source,
    string ArtifactSha256, string License, string Platform, string ExecutablePath, string ExpectedVersionOutput,
    string[] TestedCommands, string OutputSchema, string Isolation, string Owner, DateTimeOffset CheckedAt,
    bool ExecutableQualified = false, bool IsolationQualified = false, bool IdentityQualified = false,
    bool FixtureOnly = false)
{
    public bool IsQualified => ExecutableQualified && IsolationQualified && IdentityQualified;
    public override string ToString() => $"CLI qualification {Provider}/{Release}";

    public void Validate(string provider, bool allowFixture = false)
    {
        if (Provider != provider || !IsQualified || FixtureOnly && !allowFixture || Platform != "linux-x64"
            || !OperatingSystem.IsLinux() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64
            || Source.Scheme != "https" || Source.UserInfo.Length != 0 || Source.Query.Length != 0 || Source.Fragment.Length != 0
            || Source.Host != "github.com" || SourceCommit.Length != 40 || !SourceCommit.All(Uri.IsHexDigit)
            || ArtifactSha256.Length != 64 || !ArtifactSha256.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(License)
            || string.IsNullOrWhiteSpace(Release) || string.IsNullOrWhiteSpace(OutputSchema) || string.IsNullOrWhiteSpace(Isolation)
            || string.IsNullOrWhiteSpace(Owner) || CheckedAt == default || TestedCommands.Length == 0
            || ExpectedVersionOutput.Length is 0 or > 4096 || ExpectedVersionOutput.Any(c => char.IsControl(c) && c is not '\n' and not '\r'))
            throw new InvalidDataException("CLI qualification is incomplete or incompatible; use an existing API key.");
        ValidateExecutable(ExecutablePath);
        using var file = File.OpenRead(ExecutablePath);
        var hash = Convert.ToHexString(SHA256.HashData(file));
        if (!hash.Equals(ArtifactSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("CLI executable does not match the qualified artifact; use an existing API key.");
    }

    internal static void ValidateExecutable(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) throw new InvalidDataException("Qualified CLI executable is missing; use an existing API key.");
        CliPrivatePaths.RefuseLinks(path);
        var info = new FileInfo(path);
        if (info.Length is <= 0 or > 512 * 1024 * 1024) throw new InvalidDataException("Invalid qualified CLI executable.");
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(path);
            if ((mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0
                || (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0)
                throw new InvalidDataException("Qualified CLI executable has unsafe permissions.");
        }
    }
}

public sealed class CliPrivateOutput(int exitCode, byte[] stdout, byte[] stderr) : IDisposable
{
    public int ExitCode { get; } = exitCode;
    [JsonIgnore] public ReadOnlyMemory<byte> Stdout => stdout;
    [JsonIgnore] public ReadOnlyMemory<byte> Stderr => stderr;
    public override string ToString() => "CLI output [redacted]";
    public void Dispose() { CryptographicOperations.ZeroMemory(stdout); CryptographicOperations.ZeroMemory(stderr); }
}

public sealed class CliConnectionException(string category) : Exception(category);
