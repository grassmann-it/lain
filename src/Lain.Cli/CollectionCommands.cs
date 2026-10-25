using System.Net;
using Lain.Client;
using Lain.Core;

namespace Lain.Cli;

public static partial class CliApplication
{
    private sealed record CollectionListResult(long Revision, ModelCollectionDto[] Collections);
    private sealed record CollectionChangeResult(long Revision, long AppliedRevision, string Action, string Id, ModelCollectionDto? Collection);

    private static void ValidateCollections(Options options)
    {
        var action = options.Positionals.FirstOrDefault();
        if (action is null or "list")
        {
            options.RequireOnly("json");
            options.RequirePositionals(action is null ? 0 : 1);
            return;
        }
        if (action is not ("save" or "upsert" or "delete"))
            throw new ArgumentException("Collection action must be list, save, upsert or delete.");
        options.RequirePositionals(2);
        options.RequireOnly(action == "delete" ? ["json", "revision"] : ["json", "revision", "name", "routes"]);
        if (options.Get("revision") is not { } revision) throw new ArgumentException("--revision is required for collection changes. Read it with 'lain collections --json'.");
        _ = ParseLong(revision, "revision");
        var id = options.Positionals[1];
        if (string.IsNullOrEmpty(id) || id.Length > 100 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new ArgumentException("Use the collection ID (letters, digits, '-' or '_').");
        if (action == "delete") return;
        if (options.Get("name") is not { } name || string.IsNullOrWhiteSpace(name) || name.Length > 256 || name.Any(char.IsControl))
            throw new ArgumentException("--name requires a nonempty collection name of at most 256 characters.");
        if (!options.Has("routes")) throw new ArgumentException("--routes is required; provide ordered route IDs or unique aliases, separated by commas. Use an empty value for an empty collection.");
        _ = CollectionMembers(options.Get("routes")!);
    }

    private static string[] CollectionMembers(string text)
    {
        if (text.Length == 0) return [];
        var members = text.Split(',', StringSplitOptions.TrimEntries);
        if (members.Any(string.IsNullOrWhiteSpace) || members.Distinct(StringComparer.Ordinal).Count() != members.Length)
            throw new ArgumentException("--routes requires distinct, nonempty route IDs or unique aliases.");
        return members;
    }

    private static async Task<object> Collections(LainApiClient client, Options options, CancellationToken ct)
    {
        var action = options.Positionals.FirstOrDefault();
        if (action is null or "list")
        {
            var configuration = await client.GetConfigurationAsync(ct);
            return new CollectionListResult(configuration.Revision, configuration.Collections ?? []);
        }
        var revision = ParseLong(options.Get("revision")!, "revision");
        var id = options.Positionals[1];
        if (action == "delete")
        {
            var deleted = await client.RemoveCollectionAsync(revision, id, ct);
            return new CollectionChangeResult(deleted.Revision, deleted.AppliedRevision, "deleted", id, null);
        }
        var snapshot = await client.GetConfigurationAsync(ct);
        if (snapshot.Revision != revision)
            throw new LainApiException(HttpStatusCode.Conflict, "revision_conflict", "Configuration changed; reload collections and retry with its revision.");
        var routeIds = CollectionMembers(options.Get("routes")!).Select(member =>
        {
            var direct = snapshot.Routes.SingleOrDefault(route => route.Id == member);
            if (direct is not null) return direct.Id;
            var aliases = snapshot.Routes.Where(route => string.Equals(route.Alias, member, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (aliases.Length != 1) throw new ArgumentException(aliases.Length == 0
                ? $"Unknown route ID or alias '{member}'." : $"Route alias '{member}' is ambiguous; use its route ID.");
            return aliases[0].Id;
        }).ToArray();
        if (routeIds.Distinct(StringComparer.Ordinal).Count() != routeIds.Length)
            throw new ArgumentException("Multiple --routes entries resolve to the same route ID.");
        var previous = (snapshot.Collections ?? []).SingleOrDefault(collection => collection.Id == id);
        var saved = await client.SaveCollectionAsync(revision, new(id, options.Get("name")!, routeIds, previous?.RouterTargetId), ct);
        return new CollectionChangeResult(saved.Revision, saved.AppliedRevision, "saved", id,
            (saved.Collections ?? []).Single(collection => collection.Id == id));
    }

    private static async Task WriteCollections(TextWriter output, CollectionListResult result)
    {
        await HumanTable.WriteAsync(output, $"Collections ({result.Collections.Length}); revision {result.Revision}",
            [new("Collection"), new("ID", Identifier: true), new("Route IDs", Identifier: true)],
            result.Collections.Select(collection => new HumanRow([collection.Name, collection.Id, JoinIds(collection.RouteIds)])));
    }

    private static async Task WriteCollectionChange(TextWriter output, CollectionChangeResult result)
    {
        await HumanTable.WriteTextAsync(output, $"Collection {Cell(result.Id)} {result.Action}; revision {result.Revision}.");
        if (result.Collection is { } collection)
            await HumanTable.WriteTextAsync(output, $"{Cell(collection.Name)}: {JoinIds(collection.RouteIds)}");
    }
}
