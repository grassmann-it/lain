using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Workers;

public sealed class WorkerPlanException(string code, string message, int statusCode = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public ApiError ToApiError() => new(Code, Message);
}

/// <summary>One strict wire contract for the endpoint. No unknown keys, duplicate keys, coercion or missing required fields.</summary>
public static class WorkerPlanValidation
{
    public const int SchemaVersion = 1;
    public const int MaximumWorkers = 12;
    public const int MaximumPurposes = 128;
    public const int MaximumRoutesPerPurpose = 128;
    public const int MaximumPromptLength = 32768;
    public const int MaximumWritePaths = 128;
    public const int MaximumRequestBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16
    };

    public static WorkerPlanRequest Parse(ReadOnlyMemory<byte> json)
    {
        if (json.Length > MaximumRequestBytes) throw new WorkerPlanException("request_too_large", "Worker plan request exceeds the size limit.", 413);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            Object(root, ["revision", "policy", "tasks"], ["revision", "policy", "tasks"]);
            var policy = root.GetProperty("policy");
            Object(policy, ["schemaVersion", "profileId", "maxWorkers", "purposes", "costPolicy"], ["schemaVersion", "profileId", "maxWorkers", "purposes"]);
            var purposes = policy.GetProperty("purposes");
            if (purposes.ValueKind != JsonValueKind.Array) Invalid();
            foreach (var purpose in purposes.EnumerateArray())
                Object(purpose, ["name", "routeIds", "reasoningEffort", "contextMode"], ["name", "routeIds"]);
            var tasks = root.GetProperty("tasks");
            if (tasks.ValueKind != JsonValueKind.Array) Invalid();
            foreach (var task in tasks.EnumerateArray())
                Object(task, ["id", "purpose", "prompt", "writePaths"], ["id", "purpose", "prompt"]);
            var request = root.Deserialize<WorkerPlanRequest>(Json) ?? throw new JsonException();
            Validate(request);
            return request;
        }
        catch (JsonException) { throw new WorkerPlanException("invalid_request", "Worker plan JSON has invalid, missing, duplicate or unknown fields."); }
    }

    public static void Validate(WorkerPlanRequest request)
    {
        if (request is null || request.Revision < 0 || request.Policy is null || request.Tasks is null) Invalid();
        var policy = request!.Policy;
        if (policy.SchemaVersion != SchemaVersion) throw new WorkerPlanException("unsupported_schema", "Worker policy schemaVersion must be 1.");
        Key(policy.ProfileId);
        if (policy.CostPolicy is not ("configured" or "free_only")) Invalid();
        if (policy.MaxWorkers is < 1 or > MaximumWorkers || policy.Purposes is null ||
            policy.Purposes.Length > MaximumPurposes || request.Tasks.Length > policy.MaxWorkers)
            throw new WorkerPlanException("worker_limits", "Worker count or policy limits exceeded; MaxWorkers must be between 1 and 12 and each batch must fit MaxWorkers.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var purpose in policy.Purposes!)
        {
            if (purpose is null) Invalid();
            Key(purpose!.Name);
            if (!names.Add(purpose.Name)) throw new WorkerPlanException("duplicate_purpose", "Worker purpose names must be distinct.");
            if (purpose.RouteIds is null || purpose.RouteIds.Length is < 1 or > MaximumRoutesPerPurpose) Invalid();
            var routes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in purpose.RouteIds!)
            {
                Key(id);
                if (!routes.Add(id)) throw new WorkerPlanException("duplicate_route", "Purpose route IDs must be distinct and ordered.");
            }
            if (purpose.ReasoningEffort is not null && (purpose.ReasoningEffort.Length is < 1 or > 32 ||
                purpose.ReasoningEffort.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-'))) Invalid();
            if (purpose.ContextMode is not (null or "inherit" or "native")) Invalid();
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var writes = new List<(string Task, string Path)>();
        foreach (var task in request.Tasks)
        {
            if (task is null) Invalid();
            Key(task!.Id); Key(task.Purpose);
            if (!ids.Add(task.Id)) throw new WorkerPlanException("duplicate_task", "Worker task IDs must be distinct.");
            if (!names.Contains(task.Purpose)) throw new WorkerPlanException("unknown_purpose", "A task references an unknown purpose.");
            if (string.IsNullOrWhiteSpace(task.Prompt) || task.Prompt.Length > MaximumPromptLength ||
                task.Prompt.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))) Invalid();
            if (task.WritePaths is { Length: > MaximumWritePaths }) Invalid();
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in task.WritePaths ?? [])
            {
                ValidatePath(path);
                if (!paths.Add(path)) throw new WorkerPlanException("duplicate_write_path", "Task write paths must be distinct.");
                foreach (var previous in writes)
                    if (previous.Task != task.Id && Overlap(previous.Path, path))
                        throw new WorkerPlanException("write_conflict", "Write paths overlap across worker tasks.");
                writes.Add((task.Id, path));
            }
        }
    }

    private static void Object(JsonElement value, string[] allowed, string[] required)
    {
        if (value.ValueKind != JsonValueKind.Object) Invalid();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name)) Invalid();
        if (required.Any(name => !seen.Contains(name))) Invalid();
    }

    private static void Key(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 100 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-')) Invalid();
    }

    private static void ValidatePath(string? path)
    {
        // Portable literal relative paths only. Reject ambiguous separators, drive/ADS syntax,
        // globs and dot components rather than normalizing declarations into broader write scopes.
        if (string.IsNullOrWhiteSpace(path) || path.Length > 512 || path.StartsWith('/') ||
            path.Any(c => char.IsControl(c) || "\\:*?\"<>|".Contains(c)) ||
            path.Split('/').Any(part => part is "" or "." or ".." || part != part.Trim() || part.EndsWith('.')))
            throw new WorkerPlanException("invalid_write_path", "Write paths must be literal relative paths without traversal or ambiguous components.");
    }

    private static bool Overlap(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase) ||
        a.StartsWith(b + "/", StringComparison.OrdinalIgnoreCase) || b.StartsWith(a + "/", StringComparison.OrdinalIgnoreCase);

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Invalid() => throw new WorkerPlanException("invalid_request", "Invalid worker policy or task fields.");
}
