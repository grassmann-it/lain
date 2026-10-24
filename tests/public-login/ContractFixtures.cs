// Frozen adapter seam, independent of host storage/runtime. Host integration is checked by AC-01.
using Lain.Core;
namespace Lain.Host.Accounts;
public interface IAccountLoginAdapter
{
    string Provider { get; }
    string Method { get; }
    bool RequiresCore => false;
    Task<LoginAdapterResult> BeginAsync(LoginOperationContext context, CancellationToken ct);
    Task<LoginAdapterResult> StatusAsync(LoginOperationContext context, CancellationToken ct);
    Task CancelAsync(LoginOperationContext context, CancellationToken ct);
    Task ReleaseAsync(LoginOperationContext context, CancellationToken ct) => Task.CompletedTask;
}
public sealed record LoginOperationContext(string OperationId, string AccountId, long Revision, long AccountGeneration,
    ConnectAccountRequest Request, DateTimeOffset ExpiresAt)
{
    public Func<PreparedLoginCredential, CancellationToken, Task>? CheckpointCredentialAsync { get; init; }
}
public sealed record LoginAdapterResult(string State, string? Url = null, string? Error = null,
    PreparedLoginCredential? Credential = null, ResourceBindingDto[]? Resources = null, string? NextAction = null);
public sealed record PreparedLoginCredential(string Owner, string[] Purposes, ResourceBindingDto Binding, string PrivateJson,
    DateTimeOffset? ExpiresAt = null, bool PreserveForRecovery = false, string? RemoteId = null);
