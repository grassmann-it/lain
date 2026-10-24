using System.Collections.Concurrent;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Operations;

namespace Lain.Host.Accounts;

public sealed record CoreCodexLoginStart(string State, string Url);
public sealed record CoreCodexLoginProgress(string State, string? OwnedAuthFile = null);

/// <summary>Authenticated narrow core API. Credentials remain in its own auth store.</summary>
public interface ICodexOwnedLoginBackend
{
    Task<CoreCodexLoginStart> BeginAsync(CancellationToken ct);
    Task<CoreCodexLoginProgress> StatusAsync(string state, CancellationToken ct);
    Task CancelAsync(string state, CancellationToken ct);
}

/// <summary>Adopts only newly issued credentials from the explicitly owned core login flow.</summary>
public sealed class CodexOwnedLoginAdapter(ICodexOwnedLoginBackend backend, OwnStatePaths paths,
    ConfigurationRepository configuration, SecretStore secrets, IReadOnlyList<string>? codexModelCatalog = null) : IQualifiedLoginAdapter, IAccountLoginAdapter
{
    private sealed record Operation(string CoreState, LoginDto Status);
    private readonly ConcurrentDictionary<string, Operation> operations = new();
    public string Provider => "codex";
    public string Method => "codex-owned";
    public bool RequiresCore => true;
    public async Task<LoginAdapterResult> BeginAsync(LoginOperationContext context, CancellationToken ct)
    {
        var started = await BeginAsync(context.OperationId, ct);
        return new("waiting", started.Url);
    }
    public async Task<LoginAdapterResult> StatusAsync(LoginOperationContext context, CancellationToken ct)
    {
        if (!operations.TryGetValue(context.OperationId, out var operation)) throw new KeyNotFoundException("Login operation not found.");
        var progress = await backend.StatusAsync(operation.CoreState, ct);
        if (progress.State is "waiting" or "pending") return new("waiting", operation.Status.Url);
        if (progress.State is "cancelled" or "failed") return new(progress.State);
        if (progress.State != "completed") throw new InvalidDataException("Core returned an unknown login state.");
        var filename = progress.OwnedAuthFile;
        if (filename is null || Path.GetFileName(filename) != filename || !filename.EndsWith(".json", StringComparison.Ordinal))
            throw new InvalidDataException("Core login did not return an explicitly owned credential.");
        var path = Path.Combine(paths.OwnedAuth, filename);
        if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024 || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Owned login credential is unavailable.");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var auth = document.RootElement;
        var subject = Read(auth, "account_id");
        if (Read(auth, "type") != "codex" || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(Read(auth, "access_token")))
            throw new InvalidDataException("Owned login credential is invalid.");
        DateTimeOffset? expiry = DateTimeOffset.TryParse(Read(auth, "expired"), out var parsed) ? parsed : null;
        var binding = new ResourceBindingDto(Provider, Subject: subject, Provenance: "verified");
        var descriptor = JsonSerializer.Serialize(new { kind = "codex-owned", provider = Provider, ownedAuthFile = filename,
            accountId = subject, expiresAt = expiry });
        return new("prepared", Credential: new("core", context.Request.Purposes, binding, descriptor, expiry, PreserveForRecovery: true));
    }
    public Task CancelAsync(LoginOperationContext context, CancellationToken ct) => CancelAsync(context.OperationId, ct);
    public Task ReleaseAsync(LoginOperationContext context, CancellationToken ct) => ReleaseAsync(context.OperationId, ct);
    public Task ReleaseAsync(string operationId, CancellationToken ct)
    {
        operations.TryRemove(operationId, out _);
        return Task.CompletedTask;
    }
    public async Task<LoginDto> BeginAsync(string operationId, CancellationToken ct)
    {
        var started = await backend.BeginAsync(ct);
        if (string.IsNullOrWhiteSpace(started.State) || started.State.Length > 512 ||
            !Uri.TryCreate(started.Url, UriKind.Absolute, out var url) || url.Scheme != "https" || !string.IsNullOrEmpty(url.UserInfo))
            throw new InvalidDataException("Core returned an invalid Codex login operation.");
        var status = new LoginDto(operationId, Provider, "waiting", started.Url);
        operations[operationId] = new(started.State, status);
        return status;
    }
    public async Task<LoginDto> StatusAsync(string operationId, CancellationToken ct)
    {
        if (!operations.TryGetValue(operationId, out var operation)) throw new KeyNotFoundException("Login operation not found.");
        if (operation.Status.State is "completed" or "cancelled" or "failed") return operation.Status;
        var progress = await backend.StatusAsync(operation.CoreState, ct);
        var status = progress.State switch
        {
            "waiting" or "pending" => operation.Status,
            "cancelled" => operation.Status with { State = "cancelled", Url = null },
            "failed" => operation.Status with { State = "failed", Url = null, Error = "Codex login failed." },
            "completed" => AdoptOwnedCredential(operation.Status, progress.OwnedAuthFile),
            _ => throw new InvalidDataException("Core returned an unknown login state.")
        };
        operations[operationId] = operation with { Status = status };
        return status;
    }
    public async Task CancelAsync(string operationId, CancellationToken ct)
    {
        if (!operations.TryGetValue(operationId, out var operation)) throw new KeyNotFoundException("Login operation not found.");
        if (operation.Status.State == "completed") throw new InvalidOperationException("Completed login cannot be cancelled.");
        if (operation.Status.State is "cancelled" or "failed") return;
        await backend.CancelAsync(operation.CoreState, ct);
        operations[operationId] = operation with { Status = operation.Status with { State = "cancelled", Url = null } };
    }
    private LoginDto AdoptOwnedCredential(LoginDto status, string? filename)
    {
        if (filename is null || Path.GetFileName(filename) != filename || !filename.EndsWith(".json", StringComparison.Ordinal))
            throw new InvalidDataException("Core login did not return an explicitly owned credential.");
        var file = Path.Combine(paths.OwnedAuth, filename);
        if (!File.Exists(file) || new FileInfo(file).Length > 1024 * 1024 || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Owned login credential is unavailable.");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        using var document = JsonDocument.Parse(File.ReadAllText(file));
        var auth = document.RootElement;
        if (Read(auth, "type") != "codex" || string.IsNullOrWhiteSpace(Read(auth, "access_token")) || string.IsNullOrWhiteSpace(Read(auth, "account_id")))
            throw new InvalidDataException("Owned login credential is invalid.");
        var providerAccountId = Read(auth, "account_id")!;
        var displayName = new[] { Read(auth, "display_name"), Read(auth, "name"), Read(auth, "label"), Read(auth, "email") }
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "Codex";
        DateTimeOffset? expires = DateTimeOffset.TryParse(Read(auth, "expired"), out var expiry) ? expiry : null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var snapshot = configuration.Snapshot;
            try
            {
                configuration.Update(snapshot.Revision, state =>
                {
                    var existing = state.Accounts.FirstOrDefault(a => a.Provider == Provider && secrets.HasAccount(a.Id) && MatchesProviderAccount(a.Id, providerAccountId));
                    var id = existing?.Id ?? AccountService.CredentialId(Provider, providerAccountId);
                    // Keep only an ownership reference. The core remains authoritative for rotated tokens.
                    secrets.WriteAccount(id, JsonSerializer.Serialize(new { kind = "codex-owned", provider = Provider, ownedAuthFile = filename, accountId = providerAccountId, expiresAt = expires }));
                    var account = new AccountDto(id, existing?.Name ?? displayName, Provider, true, "codex-owned", "unverified",
                        existing?.BudgetPoolId, expires, existing?.SharedWithLegacy ?? false);
                    var routes = state.Routes.ToList();
                    if (!routes.Any(r => r.AccountIds.Contains(id)))
                    foreach (var model in (codexModelCatalog ?? []).Distinct(StringComparer.Ordinal))
                    {
                        if (string.IsNullOrWhiteSpace(model) || model.Length > 200) continue;
                        var routeId = "route-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id + "\0" + model))).ToLowerInvariant()[..32];
                        var alias = routes.Any(r => r.Alias == model) ? model + "-" + id[^8..] : model;
                        var baseAlias = alias; var suffix = 2;
                        while (routes.Any(r => r.Alias == alias)) alias = baseAlias + "-" + suffix++;
                        routes.Add(new(routeId, alias, model, Provider, [id]));
                    }
                    return state with { Accounts = [.. state.Accounts.Where(a => a.Id != id), account], Routes = routes.ToArray() };
                });
                return status with { State = "completed", Url = null };
            }
            catch (RevisionConflictException) when (attempt < 9) { }
        }
        throw new InvalidOperationException("Could not adopt the completed login into the current configuration.");
    }
    private bool MatchesProviderAccount(string id, string providerAccountId)
    {
        using var document = JsonDocument.Parse(secrets.ReadAccountDescriptor(id));
        return Read(document.RootElement, "accountId") == providerAccountId;
    }
    private static string? Read(JsonElement node, string name) => node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
