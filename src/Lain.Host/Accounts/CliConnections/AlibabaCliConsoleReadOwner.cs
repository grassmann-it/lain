using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Accounts.CliConnections;

public sealed record AlibabaConsoleScope(string AccountId, string ProfileReference, string BindingVersion,
    string Site, string Region, string WorkspaceId);

/// <summary>Private delegation; no generic Console API name or secret crosses this interface.</summary>
public interface IAlibabaConsoleReadOwner
{
    Task<JsonDocument> ReadTrialAsync(AlibabaConsoleScope scope, string[] models, CancellationToken ct);
    Task<JsonDocument> ReadTrialProtectionAsync(AlibabaConsoleScope scope, string[] models, CancellationToken ct);
    Task<JsonDocument> ReadUsageAsync(AlibabaConsoleScope scope, DateTimeOffset start, DateTimeOffset end, CancellationToken ct);
}

public sealed class AlibabaCliConsoleReadOwner(CliConnectionStateStore store, CliQualificationManifest qualification,
    Func<AlibabaConsoleScope, bool> isCurrentBinding) : IAlibabaConsoleReadOwner
{
    public const string TrialApi = "zeldaEasy.bailian-commerce.freeTrial.queryFreeTierQuota";
    public const string ProtectionApi = "zeldaEasy.bailian-commerce.freeTrial.queryFreeTierOnlyStatus";
    public const string UsageApi = "zeldaEasy.bailian-telemetry.model.getModelUsageStatistic";
    public Task<JsonDocument> ReadTrialAsync(AlibabaConsoleScope scope, string[] models, CancellationToken ct) =>
        ReadAsync(scope, TrialApi, new { queryFreeTierQuotaRequest = new { models = Models(models) } }, ct);
    public Task<JsonDocument> ReadTrialProtectionAsync(AlibabaConsoleScope scope, string[] models, CancellationToken ct) =>
        ReadAsync(scope, ProtectionApi, new { queryFreeTierOnlyStatusRequest = new { models = Models(models) } }, ct);
    public async Task<JsonDocument> ReadUsageAsync(AlibabaConsoleScope scope, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        if (start >= end || end - start > TimeSpan.FromDays(31)) throw new CliConnectionException("invalid-alibaba-usage-period");
        string? taskId = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var request = new Dictionary<string, object> { ["startTime"] = start.ToUnixTimeMilliseconds(), ["endTime"] = end.ToUnixTimeMilliseconds(),
                ["modelCallSource"] = "Online", ["filterWorkspaceId"] = scope.WorkspaceId };
            if (taskId is not null) request["asyncTaskId"] = taskId;
            var result = await ReadAsync(scope, UsageApi, new { reqDTO = request }, ct);
            var data = Lain.Host.Quota.AlibabaConsoleQuotaAdapter.Unwrap(result.RootElement);
            if (data.ValueKind != JsonValueKind.Object || data.EnumerateObject().Count() != 1 || AlibabaOwnedProfileReader.String(data, "taskId") is not { } next)
                return result;
            taskId = AlibabaOwnedProfileReader.Identifier(next); result.Dispose();
            if (taskId is null) throw new CliConnectionException("invalid-alibaba-task-reference");
            await Task.Delay(500, ct);
        }
        throw new CliConnectionException("alibaba-usage-read-pending");
    }
    private async Task<JsonDocument> ReadAsync(AlibabaConsoleScope scope, string api, object request, CancellationToken ct)
    {
        if (!isCurrentBinding(scope) || string.IsNullOrWhiteSpace(scope.BindingVersion)) throw new CliConnectionException("alibaba-credential-binding-changed");
        qualification.Validate("alibaba-model-studio");
        var profile = new AlibabaOwnedProfileReader(store).Read(scope.ProfileReference);
        if (profile.Site != scope.Site || profile.Region != scope.Region || profile.WorkspaceId != scope.WorkspaceId)
            throw new CliConnectionException("alibaba-profile-scope-mismatch");
        var directory = store.ProfileDirectory(scope.ProfileReference);
        using var output = await new OfficialCliProcess().RunAsync(qualification,
            ["console", "call", "--api", api, "--data", JsonSerializer.Serialize(request), "--config", "lain", "--output", "json",
                "--console-site", scope.Site, "--console-region", scope.Region, "--workspace-id", scope.WorkspaceId],
            directory, new Dictionary<string, string> { ["BAILIAN_CONFIG_DIR"] = directory }, TimeSpan.FromSeconds(5), ct);
        if (!isCurrentBinding(scope)) throw new CliConnectionException("alibaba-credential-binding-changed");
        if (output.ExitCode != 0) throw new CliConnectionException("alibaba-console-read-unavailable-renew-owned-session");
        try { return JsonDocument.Parse(output.Stdout, new JsonDocumentOptions { MaxDepth = 16 }); }
        catch (JsonException) { throw new CliConnectionException("invalid-alibaba-console-response"); }
    }
    private static string[] Models(string[] models)
    {
        if (models.Length is < 1 or > 100 || models.Any(m => AlibabaOwnedProfileReader.Identifier(m) is null))
            throw new CliConnectionException("invalid-alibaba-trial-model-selection");
        return models.Distinct(StringComparer.Ordinal).ToArray();
    }
}
