using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Operations;

namespace Lain.Host.Accounts.CliConnections;

internal static class CliPrivatePaths
{
    public static void RefuseLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            FileSystemInfo info = System.IO.Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is not null || info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("CLI owned paths cannot contain links.");
        }
    }
    public static void Directory(string path) { RefuseLinks(path); OwnStatePaths.EnsurePrivateDirectory(path); RefuseLinks(path); }
    public static byte[] Read(string path, int maximumBytes)
    {
        RefuseLinks(path);
        if (!File.Exists(path) || new FileInfo(path).Length > maximumBytes) throw new InvalidDataException("CLI owned file unavailable or too large.");
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            throw new InvalidDataException("CLI owned file must be private.");
        using var stream = File.OpenRead(path);
        using var output = new MemoryStream();
        var buffer = new byte[8192]; int count;
        while ((count = stream.Read(buffer)) != 0)
        { if (output.Length + count > maximumBytes) throw new InvalidDataException("CLI owned file too large."); output.Write(buffer, 0, count); }
        return output.ToArray();
    }
    public static void Id(string id)
    { if (id.Length is < 1 or > 100 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) throw new ArgumentException("Invalid CLI reference."); }
}

public sealed record CliConnectionState(string OperationId, string AccountId, string Provider, string Method,
    long Revision, long AccountGeneration, string ContextDigest, string ProfileReference, string Phase,
    DateTimeOffset ExpiresAt, ResourceBindingDto? Binding, CliProcessIdentity? Process = null, string? RemoteKeyId = null);

/// <summary>Metadata journal only. Secrets stay in the CLI-owned profile or the shared credential owner.</summary>
public sealed class CliConnectionStateStore
{
    private readonly string root;
    private readonly object gate = new();
    private readonly Func<CliConnectionState, bool>? isPublished;
    public CliConnectionStateStore(string root, Func<CliConnectionState, bool>? isPublished = null)
    { this.root = Path.GetFullPath(root); this.isPublished = isPublished; CliPrivatePaths.Directory(this.root); }
    public string ProfileDirectory(string reference)
    { CliPrivatePaths.Id(reference); var path = Path.Combine(root, "profiles", reference); CliPrivatePaths.Directory(path); return path; }
    private string StatePath(string operationId)
    { CliPrivatePaths.Id(operationId); var path = Path.Combine(root, "operations"); CliPrivatePaths.Directory(path); return Path.Combine(path, operationId + ".json"); }
    public FileStream AcquireConnection(string accountId)
    {
        CliPrivatePaths.Id(accountId);
        var directory = Path.Combine(root, "connections"); CliPrivatePaths.Directory(directory);
        var path = Path.Combine(directory, accountId + ".lock"); CliPrivatePaths.RefuseLinks(path);
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            return new FileStream(path, options);
        }
        catch (IOException) { throw new CliConnectionException("cli-connection-already-active"); }
    }
    public static string Digest(LoginOperationContext context) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { context.OperationId, context.AccountId, context.Revision, context.AccountGeneration, context.Request, context.ExpiresAt }))));
    public CliConnectionState Create(LoginOperationContext context)
    {
        lock (gate)
        {
            var path = StatePath(context.OperationId);
            if (File.Exists(path)) throw new CliConnectionException("cli-operation-already-journaled-review-recovery");
            var priorPaths = System.IO.Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.json").Take(1025).ToArray();
            if (priorPaths.Length > 1024) throw new CliConnectionException("too-many-cli-recovery-operations");
            foreach (var priorPath in priorPaths)
            {
                var prior = Read(Path.GetFileNameWithoutExtension(priorPath))!;
                if (prior.AccountId != context.AccountId || prior.Phase is "adopted" or "partial" or "cancelled" or "failed") continue;
                if (isPublished?.Invoke(prior) == true) Save(prior with { Phase = "adopted" });
                else throw new CliConnectionException("cli-prior-issuance-requires-recovery");
            }
            var state = new CliConnectionState(context.OperationId, context.AccountId, context.Request.Provider, context.Request.Method,
                context.Revision, context.AccountGeneration, Digest(context), "profile-" + Guid.NewGuid().ToString("N"),
                "prepared", context.ExpiresAt, context.Request.Binding);
            _ = ProfileDirectory(state.ProfileReference);
            Save(state); return state;
        }
    }
    public CliConnectionState? Read(string operationId)
    {
        lock (gate)
        {
            var path = StatePath(operationId);
            if (!File.Exists(path)) return null;
            var state = JsonSerializer.Deserialize<CliConnectionState>(CliPrivatePaths.Read(path, 32768)) ?? throw new InvalidDataException("Invalid CLI operation journal.");
            if (state.OperationId != operationId) throw new InvalidDataException("CLI operation journal mismatch.");
            CliPrivatePaths.Id(state.ProfileReference);
            return state;
        }
    }
    public CliConnectionState Update(LoginOperationContext context, string phase, CliProcessIdentity? process = null, string? remoteKeyId = null)
    {
        lock (gate)
        {
            var state = Read(context.OperationId) ?? throw new CliConnectionException("cli-operation-not-found");
            if (state.ContextDigest != Digest(context)) throw new CliConnectionException("cli-operation-context-mismatch");
            if (phase is not ("browser" or "result-private" or "adopted" or "partial" or "cancelled" or "failed" or "recovery-required"))
                throw new ArgumentException("Invalid CLI journal phase.");
            if (remoteKeyId is not null) CliPrivatePaths.Id(remoteKeyId);
            state = state with { Phase = phase, Process = process ?? state.Process, RemoteKeyId = remoteKeyId ?? state.RemoteKeyId };
            Save(state); return state;
        }
    }
    private void Save(CliConnectionState state) { CliPrivatePaths.RefuseLinks(StatePath(state.OperationId)); OwnStatePaths.WritePrivate(StatePath(state.OperationId), JsonSerializer.Serialize(state)); }
    public IReadOnlyList<CliConnectionState> ReconcileAfterRestart(Func<CliConnectionState, bool>? isPublished = null)
    {
        lock (gate)
        {
            var directory = Path.Combine(root, "operations"); CliPrivatePaths.Directory(directory);
            var result = new List<CliConnectionState>();
            foreach (var file in System.IO.Directory.EnumerateFiles(directory, "*.json").Take(1025))
            {
                if (result.Count >= 1024) throw new CliConnectionException("too-many-cli-recovery-operations");
                var state = Read(Path.GetFileNameWithoutExtension(file))!;
                if (state.Phase is "adopted" or "partial" or "cancelled" or "failed") continue;
                if ((isPublished ?? this.isPublished)?.Invoke(state) == true)
                { state = state with { Phase = "adopted" }; Save(state); continue; }
                // PID reuse or a different executable is never enough to terminate another process.
                if (state.Process is { } process) _ = OfficialCliProcess.ReconcileOwnedProcess(process);
                state = state with { Phase = "recovery-required" }; Save(state); result.Add(state);
            }
            return result;
        }
    }
}
