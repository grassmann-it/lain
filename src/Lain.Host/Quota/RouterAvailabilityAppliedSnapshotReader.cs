using System.Text.Json;
using Lain.Core;
using Lain.Host.Models;
using Lain.Host.Operations;

namespace Lain.Host.Quota;

/// <summary>Reads only the immutable applied snapshot after validating all publication markers.</summary>
public sealed class RouterAvailabilityAppliedSnapshotReader(OwnStatePaths paths, Func<ConfigurationDto> current)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 64 };
    public async Task<RouterAvailabilityAppliedSnapshot?> ReadAsync(CancellationToken ct = default)
    {
        try
        {
            var before = current();
            var activePath = Path.Combine(paths.Root, "active-profile.json");
            var runtimePath = Path.Combine(paths.Proxy, "runtime.json");
            var snapshotPath = Path.Combine(paths.Root, "applied-profile-snapshot.json");
            foreach (var path in new[] { activePath, runtimePath, snapshotPath, Path.Combine(paths.Root, "profile-application.json") }) RejectLinks(path);
            var first = AppliedProfileStatus.Read(activePath, runtimePath, before.AppliedRevision);
            if (first.Id is null || first.GenerationId is null) return null;
            await using var input = new FileStream(snapshotPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (input.Length > 4 * 1024 * 1024) return null;
            var snapshot = await JsonSerializer.DeserializeAsync<Snapshot>(input, Json, ct);
            if (snapshot is null || snapshot.Revision != before.AppliedRevision || snapshot.GenerationId != first.GenerationId ||
                snapshot.Profile?.Id != first.Id || snapshot.Routes is null || snapshot.Accounts is null) return null;
            var after = current(); var second = AppliedProfileStatus.Read(activePath, runtimePath, after.AppliedRevision);
            if (after.AppliedRevision != before.AppliedRevision || second != first) return null;
            var configuration = new ConfigurationDto(snapshot.Revision, snapshot.Revision, snapshot.Accounts, snapshot.Routes,
                [snapshot.Profile], "gateway", CollectionRouters: snapshot.Routers?.Select(r => r.Router).ToArray() ?? []);
            return new(snapshot.Revision, snapshot.GenerationId, configuration);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        { return null; }
    }
    private static void RejectLinks(string path)
    {
        if (new FileInfo(path).LinkTarget is not null) throw new IOException("Invalid applied snapshot path.");
        for (var d = new DirectoryInfo(Path.GetDirectoryName(path)!); d is not null; d = d.Parent)
            if (d.LinkTarget is not null) throw new IOException("Invalid applied snapshot path.");
    }
    private sealed record AppliedRouter(CollectionRouterDto Router, string[] Members);
    private sealed record Snapshot(long Revision, string GenerationId, ProfileDto Profile, RouteDto[] Routes, AccountDto[] Accounts, AppliedRouter[]? Routers = null);
}
