using System.Net;
using Lain.Client;
using Lain.Core;

namespace Lain.Cli;

public static partial class CliApplication
{
    private sealed record RouterListResult(long Revision, CollectionRouterDto[] Routers);
    private sealed record RouterChangeResult(long Revision, long AppliedRevision, string Action, string Id, CollectionRouterDto? Router);

    private static void ValidateRouters(Options options)
    {
        var action = options.Positionals.FirstOrDefault();
        if (action is null or "list")
        {
            options.RequireOnly("json"); options.RequirePositionals(action is null ? 0 : 1);
            return;
        }
        if (action is not ("save" or "delete")) throw new ArgumentException("Router action must be list, save or delete.");
        options.RequirePositionals(2);
        options.RequireOnly(action == "delete" ? ["json", "revision"] : ["json", "revision", "alias", "group", "profile", "cost"]);
        if (options.Get("revision") is not { } revision) throw new ArgumentException("--revision is required for router changes. Read it with 'lain routers --json'.");
        _ = ParseLong(revision, "revision");
        RouterIdentity(options.Positionals[1], "router ID");
        if (action == "delete") return;
        if (options.Get("alias") is not { } alias || string.IsNullOrWhiteSpace(alias) || alias.Length > 256 || alias.Any(char.IsControl))
            throw new ArgumentException("--alias requires a nonempty router alias of at most 256 characters.");
        if (options.Get("profile") is not { } profile) throw new ArgumentException("--profile requires a host profile ID.");
        RouterIdentity(profile, "host profile ID");
        if (options.Get("cost") is not ("configured" or "free_only")) throw new ArgumentException("--cost must be configured or free_only.");
        var group = options.Get("group");
        if (group is null) throw new ArgumentException("--group requires an exact group ID: collection:ID, account:ID, provider:ID or policy:free.");
        if (group.StartsWith("collection:", StringComparison.Ordinal)) RouterIdentity(group[11..], "collection group ID");
        else if (group.StartsWith("account:", StringComparison.Ordinal)) RouterIdentity(group[8..], "account group ID");
        else if (group.StartsWith("provider:", StringComparison.Ordinal))
        {
            RouterIdentity(group[9..], "provider group ID");
            if (group != group.ToLowerInvariant()) throw new ArgumentException("Provider group IDs must use the canonical lowercase provider ID.");
        }
        else if (group != "policy:free") throw new ArgumentException("--group must be collection:ID, account:ID, provider:ID or policy:free.");
        if (group == "policy:free" && options.Get("cost") != "free_only") throw new ArgumentException("policy:free requires --cost free_only.");
    }

    private static void RouterIdentity(string value, string label)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 100 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new ArgumentException($"Use the exact {label} (letters, digits, '-' or '_').");
    }

    private static async Task<object> Routers(LainApiClient client, Options options, CancellationToken ct)
    {
        var action = options.Positionals.FirstOrDefault();
        if (action is null or "list")
        {
            var configuration = await client.GetConfigurationAsync(ct);
            return new RouterListResult(configuration.Revision, configuration.CollectionRouters ?? []);
        }
        var revision = ParseLong(options.Get("revision")!, "revision");
        var id = options.Positionals[1];
        if (action == "delete")
        {
            var deleted = await client.RemoveCollectionRouterAsync(revision, id, ct);
            return new RouterChangeResult(deleted.Revision, deleted.AppliedRevision, "deleted", id, null);
        }
        var snapshot = await client.GetConfigurationAsync(ct);
        if (snapshot.Revision != revision)
            throw new LainApiException(HttpStatusCode.Conflict, "revision_conflict", "Configuration changed; reload routers and retry with its revision.");
        var previous = (snapshot.CollectionRouters ?? []).SingleOrDefault(router => router.Id == id);
        var definition = new CollectionRouterDto(id, options.Get("alias")!, options.Get("group")!, options.Get("profile")!, options.Get("cost")!, previous?.Enabled ?? true);
        var saved = await client.SaveCollectionRouterAsync(revision, definition, ct);
        return new RouterChangeResult(saved.Revision, saved.AppliedRevision, "saved", id,
            (saved.CollectionRouters ?? []).Single(router => router.Id == id));
    }

    private static Task WriteRouters(TextWriter output, RouterListResult result) =>
        HumanTable.WriteAsync(output, $"Routers ({result.Routers.Length}); revision {result.Revision}",
            [new("Alias"), new("ID", Identifier: true), new("Group", Identifier: true), new("Profile", Identifier: true), new("Cost"), new("Enabled")],
            result.Routers.Select(router => new HumanRow([router.Alias, router.Id, router.GroupId, router.ProfileId, router.CostPolicy, router.Enabled ? "yes" : "no"])));

    private static async Task WriteRouterChange(TextWriter output, RouterChangeResult result)
    {
        await HumanTable.WriteTextAsync(output, $"Router {Cell(result.Id)} {result.Action}; revision {result.Revision}.");
        if (result.Router is { } router)
            await HumanTable.WriteTextAsync(output, $"{Cell(router.Alias)}: {Cell(router.GroupId)}; profile {Cell(router.ProfileId)}; cost {Cell(router.CostPolicy)}.");
    }
}
