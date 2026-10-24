using System.Security.Cryptography;
using System.Text.Json;

namespace Lain.Host.Accounts.ConsoleConnections;

// Provider-local private contract. No runtime browser transport is qualified yet.
// Session cookies, arbitrary URLs/JS, API keys and refresh operations are absent.
public enum ConsoleReadOperation { MiMoIdentity, MiMoIndividualDetail, MiMoIndividualUsage }
public sealed record ConsoleResourceBinding(string Subject, string Region, string Product);
public sealed record ConsoleReadRoute(string Origin, string Path, string Method);
public static class ConsoleReadRoutes
{
    public static ConsoleReadRoute Resolve(ConsoleReadOperation operation) => operation switch
    {
        ConsoleReadOperation.MiMoIdentity => new("https://platform.xiaomimimo.com", "/api/v1/userProfile", "GET"),
        ConsoleReadOperation.MiMoIndividualDetail => new("https://platform.xiaomimimo.com", "/api/v1/tokenPlan/detail", "GET"),
        ConsoleReadOperation.MiMoIndividualUsage => new("https://platform.xiaomimimo.com", "/api/v1/tokenPlan/usage", "GET"),
        _ => throw new InvalidOperationException("Console read is not qualified.")
    };
}

// Created within an existing AC-01 operation, never persisted as an account or
// credential store. The host must supply its current revision/liveness at Accept.
public sealed class ConsoleReadLease
{
    private int consumed;
    public string OperationId { get; }
    public string Nonce { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public string AccountId { get; }
    public long Revision { get; }
    public ConsoleResourceBinding Binding { get; }
    public ConsoleReadOperation Operation { get; }
    public DateTimeOffset ExpiresAt { get; }
    public ConsoleReadRoute Route => ConsoleReadRoutes.Resolve(Operation);
    public ConsoleReadLease(string operationId, string accountId, long revision, ConsoleResourceBinding binding,
        ConsoleReadOperation operation, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(operationId) || string.IsNullOrWhiteSpace(accountId) || revision < 0
            || !MiMo.MiMoConsoleIdentity.IsIdentifier(binding.Subject)
            || binding.Region is not ("CN" or "EU") || binding.Product != "individual-token-plan")
            throw new InvalidOperationException("Console binding is not qualified.");
        _ = ConsoleReadRoutes.Resolve(operation);
        (OperationId, AccountId, Revision, Binding, Operation, ExpiresAt) =
            (operationId, accountId, revision, binding, operation, now.AddMinutes(2));
    }
    public JsonDocument Accept(ConsoleReadReply reply, long currentRevision, bool operationActive, DateTimeOffset now)
    {
        if (!operationActive || currentRevision != Revision || now >= ExpiresAt || reply.OperationId != OperationId
            || reply.AccountId != AccountId || reply.Revision != Revision || reply.Nonce != Nonce
            || reply.Binding != Binding || reply.Operation != Operation || reply.Origin != Route.Origin)
            throw new InvalidOperationException("Console response binding is invalid or expired.");
        if (Interlocked.Exchange(ref consumed, 1) != 0)
            throw new InvalidOperationException("Console response already consumed.");
        if (reply.Status != 200 || reply.Utf8Json.Length > 256 * 1024)
            throw new InvalidOperationException("Console read unavailable.");
        try { return JsonDocument.Parse(reply.Utf8Json, new JsonDocumentOptions { MaxDepth = 24 }); }
        catch (JsonException) { throw new InvalidOperationException("Console response schema invalid."); }
    }
    public override string ToString() => "ConsoleReadLease [private]";
}
public sealed class ConsoleReadReply(string operationId, string accountId, long revision, string nonce,
    ConsoleResourceBinding binding, ConsoleReadOperation operation, string origin, int status, byte[] utf8Json)
{
    public string OperationId { get; } = operationId;
    public string AccountId { get; } = accountId;
    public long Revision { get; } = revision;
    public string Nonce { get; } = nonce;
    public ConsoleResourceBinding Binding { get; } = binding;
    public ConsoleReadOperation Operation { get; } = operation;
    public string Origin { get; } = origin;
    public int Status { get; } = status;
    public ReadOnlyMemory<byte> Utf8Json { get; } = utf8Json.ToArray();
    public override string ToString() => "ConsoleReadReply [private]";
}
