using System.Text.Json.Serialization;

namespace Lain.Core;

/// <summary>Project preferences reference existing profile routes; they do not grant execution or spending authority.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkerProjectPolicy(int SchemaVersion, string ProfileId, int MaxWorkers,
    WorkerPurposePolicy[] Purposes, string CostPolicy = "configured");

/// <summary>Null/omitted context inherits the existing client context. Explicit native context
/// suppresses automatic skill guidance and external integrations, subject to the runner's configuration gate.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkerPurposePolicy(string Name, string[] RouteIds, string? ReasoningEffort = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ContextMode = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkerTask(string Id, string Purpose, string Prompt, string[]? WritePaths = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkerPlanRequest(long Revision, WorkerProjectPolicy Policy, WorkerTask[] Tasks);

/// <summary>Model is the exported Codex alias, not the upstream model name. Compatibility carries route-scoped evidence,
/// not provider-tested acceptance. Explicitly unsupported worker capabilities are denied; unknowns stay unqualified.
/// Unknown capabilities alone do not veto bounded execution of an explicitly selected route. Null reasoning blocks execution.
/// Paths are declarations, not sandbox guarantees. ContextMode is the resolved choice; native context needs a verified CLI gate.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkerResolvedTask(string Id, string Purpose, string Prompt, string RouteId,
    string Model, string Provider, string[] AccountIds, string? ReasoningEffort, string[] WritePaths,
    ModelCompatibilityDto? Compatibility = null, string ContextMode = "inherit");

/// <summary>A read-only snapshot, not a credential, reservation, hard spend cap, or permission to execute.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkerPlan(int ContractVersion, long Revision, string ProfileId, int MaxWorkers,
    WorkerResolvedTask[] Tasks, string[] Notes);
