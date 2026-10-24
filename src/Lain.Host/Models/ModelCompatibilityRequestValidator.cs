using System.Text.Json;
using Lain.Core;

namespace Lain.Host.Models;

public sealed record ModelCompatibilityRejectionDto(string Code, string Message, string Action);
public sealed record ModelRequestToolSummaryDto(int TopLevelToolCount, int TotalDeclarationCount, string[] Types);

/// <summary>Read-only preview of known restrictions. Core must repeat these checks at actual dispatch and after translation.</summary>
public static class ModelCompatibilityRequestValidator
{
    public static ModelRequestToolSummaryDto SummarizeTools(JsonElement request)
    {
        var types = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        var topLevelCount = 0;
        void Walk(JsonElement tools, int depth)
        {
            if (depth > 32 || tools.ValueKind != JsonValueKind.Array) throw new ArgumentException("Invalid or excessively nested tools array.");
            foreach (var tool in tools.EnumerateArray())
            {
                if (tool.ValueKind != JsonValueKind.Object) throw new ArgumentException("Every tool must be an object.");
                checked { count++; }
                var kind = Single(tool, "type");
                if (kind is null || kind.Value.ValueKind != JsonValueKind.String) throw new ArgumentException("Every tool needs an explicit type.");
                types.Add(kind.Value.GetString()!);
                var children = Single(tool, "tools");
                if (children is not null) Walk(children.Value, depth + 1);
            }
        }
        var tools = Single(request, "tools");
        if (tools is not null && tools.Value.ValueKind != JsonValueKind.Null)
        {
            if (tools.Value.ValueKind != JsonValueKind.Array) throw new ArgumentException("tools must be an array.");
            topLevelCount = tools.Value.GetArrayLength();
            Walk(tools.Value, 0);
        }
        return new(topLevelCount, count, types.Order(StringComparer.Ordinal).ToArray());
    }

    public static ModelCompatibilityRejectionDto[] Validate(ModelCompatibilityDto compatibility, JsonElement request, bool finalWireRequest = false)
    {
        var errors = new List<ModelCompatibilityRejectionDto>();
        var summary = SummarizeTools(request);
        var capabilities = compatibility.Capabilities;
        void Unsupported(bool requested, ModelCapabilityEvidenceDto evidence, string code, string message, string action)
        {
            if (requested && evidence.State == "unsupported") errors.Add(new(code, message, action));
        }
        var toolChoice = Single(request, "tool_choice");
        var nativeChoice = toolChoice switch
        {
            { ValueKind: JsonValueKind.String } choice => choice.GetString(),
            { ValueKind: JsonValueKind.Object } choice => StringControl(choice, "type"),
            _ => null
        };
        Unsupported(summary.Types.Any(IsSearch) || IsSearch(nativeChoice), capabilities.NativeSearch,
            "native_search_unsupported", "This route uses a local Responses-to-Chat translator that rejects native web search.", "Set web_search = \"disabled\" in the separate lain profile; ordinary function tools are unaffected.");
        Unsupported(summary.Types.Contains("namespace"), capabilities.NamespaceTools,
            "namespace_tools_unsupported", "This route rejects provider-defined namespace tools.", "Choose a route qualified for namespaces or explicitly disable features.multi_agent in the separate profile; that disables delegation. Review other namespace sources too.");
        Unsupported(summary.Types.Contains("custom"), capabilities.CustomTools,
            "custom_tools_unsupported", "This route rejects provider-defined custom tools.", "Choose a route qualified for custom tools or explicitly disable custom patch advertising; no tool will be removed automatically.");
        Unsupported(summary.Types.Contains("function"), capabilities.Functions,
            "function_tools_unsupported", "This route does not support ordinary function tools.", "Choose a route with qualified function tool support or explicitly start a tool-free session.");
        var stream = Single(request, "stream");
        Unsupported(stream?.ValueKind == JsonValueKind.True, capabilities.Streaming,
            "streaming_unsupported", "This route does not support streaming.", "Choose a streaming route or explicitly use a nonstreaming client request.");
        if (finalWireRequest && capabilities.MaximumTools.Value is { } maximum && summary.TopLevelToolCount > maximum)
            errors.Add(new("tool_limit_exceeded", $"This route accepts at most {maximum} transmitted tools; the top-level tools array contains {summary.TopLevelToolCount}.",
                "Explicitly select needed MCP/plugin tools with enabled_tools/disabled_tools, including native client tools in the count. lain will not truncate tools or choose a paid fallback."));
        var output = Single(request, "max_output_tokens") ?? Single(request, "max_tokens");
        if (capabilities.MaximumOutput.Value is { } maximumOutput && output is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out var requested) && requested > maximumOutput)
            errors.Add(new("output_limit_exceeded", $"Requested output limit exceeds the source-scoped route limit of {maximumOutput} tokens.", "Explicitly lower the output budget or choose a qualified larger route."));
        // Context token counts cannot be derived accurately from serialized JSON or UTF-8 byte counts.
        return errors.ToArray();
    }

    private static string? StringControl(JsonElement parent, string name)
    {
        var value = Single(parent, name);
        if (value is null) return null;
        if (value.Value.ValueKind != JsonValueKind.String) throw new ArgumentException($"{name} must be a string.");
        return value.Value.GetString();
    }

    private static bool IsSearch(string? type) => type is "web_search" or "web_search_preview";
    private static JsonElement? Single(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object) throw new ArgumentException("Request/tool controls require an object.");
        JsonElement? found = null;
        foreach (var property in parent.EnumerateObject())
        {
            if (property.Name != name) continue;
            if (found is not null) throw new ArgumentException($"Duplicate {name} control is ambiguous.");
            found = property.Value;
        }
        return found;
    }
}
