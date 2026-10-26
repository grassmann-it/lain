using Lain.Client;
using Lain.Core;
using System.Security.Cryptography;

namespace Lain.Cli.Diagnostics;

public sealed record DoctorRequest(string? Target = null, string? Project = null, string? Profile = null);
public sealed record DoctorFinding(string Code, string State, string Explanation, string Source, string NextStep);
public sealed record DoctorReport(int SchemaVersion, DateTimeOffset ObservedAt, string? Target, string Scope,
    IReadOnlyDictionary<string, string> Versions, IReadOnlyList<DoctorFinding> Findings);
public sealed record DoctorHost(StatusDto? Status, ConfigurationDto? Configuration);

/// <summary>Only metadata reads. Fixtures can replace all filesystem, clock and API access.</summary>
public interface IDoctorEnvironment
{
    DateTimeOffset UtcNow { get; }
    string CodexHome { get; }
    Task<DoctorHost> ReadHostAsync(CancellationToken ct);
    string? ReadText(string path);
    bool IsFile(string path);
    CodexInstallationStatus Inspect(string directory, string? profile, long? revision);
    string? MaintenanceDirectory => null;
    string? ExtensionsDirectory => null;
    string? Sha256(string path) => null;
    string? Sha256(string path, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Sha256(path); }
}

public sealed class DoctorEnvironment(Func<LainApiClient>? clientFactory = null) : IDoctorEnvironment
{
    public const long MaximumHashBytes = 512L * 1024 * 1024;
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public string CodexHome => Environment.GetEnvironmentVariable("CODEX_HOME") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    public string MaintenanceDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lain", "clients", "codex-profile-lain");
    public string ExtensionsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vscode", "extensions");
    public string? Sha256(string path) => Sha256(path, default);
    public string? Sha256(string path, CancellationToken ct)
    {
        CheckPath(path);
        if (!File.Exists(path)) return null;
        using var stream = WorkerProjectFiles.OpenRead(path);
        if (stream.Length > MaximumHashBytes) throw new IOException();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long total = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (clock.Elapsed > TimeSpan.FromSeconds(8)) throw new IOException();
            var count = stream.Read(buffer);
            if (count == 0) break;
            total += count;
            if (total > MaximumHashBytes) throw new IOException();
            hash.AppendData(buffer, 0, count);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    public async Task<DoctorHost> ReadHostAsync(CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(4));
        using var client = (clientFactory ?? LainApiClient.CreateLocal)();
        var status = await client.StatusAsync(budget.Token);
        try { return new(status, await client.GetConfigurationAsync(budget.Token)); }
        catch (Exception) when (!ct.IsCancellationRequested) { return new(status, null); }
    }
    public bool IsFile(string path)
    {
        CheckPath(path);
        return File.Exists(path) && (File.GetAttributes(path) & FileAttributes.Directory) == 0;
    }
    public string? ReadText(string path)
    {
        CheckPath(path);
        if (!File.Exists(path)) return null;
        using var stream = WorkerProjectFiles.OpenRead(path);
        if (stream.Length > 2 * 1024 * 1024) throw new IOException();
        using var reader = new StreamReader(stream);
        var buffer = new char[2 * 1024 * 1024 + 1];
        var count = reader.ReadBlock(buffer, 0, buffer.Length);
        if (count == buffer.Length) throw new IOException();
        return new string(buffer, 0, count);
    }
    public CodexInstallationStatus Inspect(string directory, string? profile, long? revision) => CodexArtifactInstaller.Inspect(directory, profile, revision);
    private static void CheckPath(string path)
    {
        WorkerProjectFiles.AssertRegular(path);
        for (var node = new FileInfo(Path.GetFullPath(path)); node is not null;
            node = node.Directory is { } parent ? new FileInfo(parent.FullName) : null)
            if (node.LinkTarget is not null || ((File.Exists(node.FullName) || Directory.Exists(node.FullName)) &&
                (File.GetAttributes(node.FullName) & FileAttributes.ReparsePoint) != 0)) throw new IOException();
    }
}
