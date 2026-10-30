using Lain.Client;
using Lain.Core;

namespace Lain.Cli;

public static partial class CliApplication
{
    private static void ValidateRecording(Options options)
    {
        var action = options.Positionals.FirstOrDefault() ?? "status";
        switch (action)
        {
            case "status": case "config":
                options.RequirePositionals(options.Positionals.Count == 0 ? 0 : 1); options.RequireOnly("json"); break;
            case "enable":
                options.RequirePositionals(1); options.RequireOnly("json", "revision", "mode");
                if (options.Get("mode") is not (RecordingContract.Content or RecordingContract.MetadataOnly))
                    throw new ArgumentException("Recording enable requires explicit --mode content|metadata_only.");
                RecordingRevision(options); break;
            case "off":
                options.RequirePositionals(1); options.RequireOnly("json", "revision"); RecordingRevision(options); break;
            case "list":
                options.RequirePositionals(1); options.RequireOnly("json", "limit", "cursor", "model", "route", "worker"); RecordingLimit(options); break;
            case "show":
                options.RequirePositionals(2); options.RequireOnly("json", "limit", "cursor"); RecordingIdentity(options.Positionals[1]); RecordingLimit(options); break;
            case "events":
                options.RequirePositionals(1); options.RequireOnly("json", "limit", "after"); RecordingLimit(options);
                if (options.Get("after") is { } after) _ = ParseLong(after, "after"); break;
            case "runs":
                options.RequirePositionals(2); options.RequireOnly("json"); RecordingIdentity(options.Positionals[1]); break;
            case "run-label":
                options.RequirePositionals(3); options.RequireOnly("json", "revision", "variant", "configured-revision", "prompt-hash", "skill-hash");
                RecordingIdentity(options.Positionals[1]); RecordingIdentity(options.Positionals[2]); RecordingRevision(options);
                foreach (var name in new[] { "prompt-hash", "skill-hash" })
                    if (options.Get(name) is { } hash && (hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigit(c))))
                        throw new ArgumentException("Run hashes require 64 SHA256 hex digits.");
                break;
            case "delete":
                options.RequirePositionals(2); options.RequireOnly("json", "revision");
                if (options.Positionals[1] != "archive") throw new ArgumentException("Use recording delete archive --revision N after recording off.");
                RecordingRevision(options); break;
            default: throw new ArgumentException("Recording action must be status, config, enable, off, list, show, events, runs, run-label or delete archive.");
        }
        foreach (var name in new[] { "model", "route", "worker", "variant", "configured-revision" }) ValidateScalarOption(options, name, 256);
        ValidateScalarOption(options, "cursor", 4096);
    }
    private static long RecordingRevision(Options options) => options.Get("revision") is { } value ? ParseLong(value, "revision") :
        throw new ArgumentException("--revision is required; read the independent recording revision with 'lain recording config --json'.");
    private static int RecordingLimit(Options options)
    {
        var limit = options.Get("limit") is { } value ? ParseInt(value, "limit") : 50;
        if (limit is < 1 or > 100) throw new ArgumentException("Recording --limit must be 1..100.");
        return limit;
    }
    private static void RecordingIdentity(string value)
    {
        if (value is not { Length: > 0 and <= 256 } || value is "." or ".." ||
            value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.' or ':' or '@' or '+')))
            throw new ArgumentException("Use a recording ID from the saved conversation list.");
    }
    private static async Task<object> Recording(LainApiClient client, Options options, CancellationToken ct)
    {
        switch (options.Positionals.FirstOrDefault() ?? "status")
        {
            case "status": return await client.RecordingStatusAsync(ct);
            case "config": return await client.RecordingPolicyAsync(ct);
            case "enable": case "off":
                var previous = await client.RecordingPolicyAsync(ct);
                return await client.UpdateRecordingPolicyAsync(new(RecordingRevision(options), options.Positionals[0] == "enable",
                    options.Get("mode") ?? previous.Mode, previous.Scope, previous.RouteIds, previous.Limits), ct);
            case "list": return await client.RecordingConversationsAsync(new(RecordingLimit(options), options.Get("cursor"),
                options.Get("model"), options.Get("route"), options.Get("worker")), ct);
            case "show": return await client.RecordingConversationAsync(options.Positionals[1], options.Get("cursor"), RecordingLimit(options), ct);
            case "events": return await client.RecordingEventsAsync(options.Get("after") is { } after ? ParseLong(after, "after") : 0, RecordingLimit(options), ct);
            case "runs": return await client.RecordingRunsAsync(options.Positionals[1], ct);
            case "run-label": return await client.LabelRecordingRunAsync(options.Positionals[1], RecordingRevision(options), new(options.Positionals[2],
                options.Get("variant"), options.Get("configured-revision"), options.Get("prompt-hash"), options.Get("skill-hash")), ct);
            case "delete": return await client.ClearRecordingArchiveAsync(RecordingRevision(options), ct);
            default: throw new ArgumentException("Unknown recording action.");
        }
    }
}
