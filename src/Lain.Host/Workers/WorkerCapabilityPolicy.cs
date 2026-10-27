using Lain.Core;

namespace Lain.Host.Workers;

/// <summary>Native coding workers require functions, streaming and tool continuation.
/// Only explicit unsupported evidence denies a candidate. Unknown evidence stays unqualified;
/// documenting tools alone never proves continuation or provider-tested acceptance.</summary>
public static class WorkerCapabilityPolicy
{
    public static string[] Unsupported(ModelCompatibilityDto compatibility) => Required(compatibility)
        .Where(item => item.Evidence.State == "unsupported").Select(item => item.Name).ToArray();

    public static string[] Unqualified(ModelCompatibilityDto compatibility) => Required(compatibility)
        .Where(item => item.Evidence.State != "supported").Select(item => item.Name).ToArray();

    private static (string Name, ModelCapabilityEvidenceDto Evidence)[] Required(ModelCompatibilityDto compatibility) =>
        [("functions", compatibility.Capabilities.Functions), ("streaming", compatibility.Capabilities.Streaming),
         ("toolContinuation", compatibility.Capabilities.ToolContinuation)];
}
