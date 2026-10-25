using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Models;
using Lain.Host.Operations;
using Lain.Host.Providers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

internal static class CollectionCases
{
    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lain-collections-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new OwnStatePaths(directory);
            const string legacy = "{\"Revision\":4,\"AppliedRevision\":4,\"Accounts\":[],\"Routes\":[],\"Profiles\":[],\"RuntimeMode\":\"gateway\"}";
            OwnStatePaths.WritePrivate(paths.Configuration, legacy);
            var repository = new ConfigurationRepository(paths);
            Check(repository.Snapshot.Collections is { Length: 0 } && repository.Snapshot.Revision == 4, "legacy JSON defaults collections without changing revision");
            Check(File.ReadAllText(paths.Configuration) == legacy, "reading legacy configuration does not rewrite it");
            var models = new ModelsService(repository);
            var state = repository.Update(4, configuration => configuration with
            {
                Accounts = [new("account-a", "Fixture", "codex", true, "oauth", "ready")],
                Routes = [new("route-a", "model-a", "upstream-a", "codex", ["account-a"]),
                    new("route-b", "model-b", "upstream-b", "codex", ["account-a"], Enabled: false)],
                Profiles = [new("lain", "lain", ["route-a"], "route-a")]
            });
            var originalRoutes = JsonSerializer.Serialize(state.Routes);
            var originalAccounts = JsonSerializer.Serialize(state.Accounts);
            var originalProfiles = JsonSerializer.Serialize(state.Profiles);
            Check(models.CollectionGroups().Groups is [{ Kind: "provider" }], "provider group is derived without creating collections or Free claims");
            state = models.PutCollection(state.Revision, new("security", "IT Sec", ["route-b", "route-a"]));
            state = models.PutCollection(state.Revision, new("free", "Free", ["route-b"]));
            Check(state.AppliedRevision == 4 && state.Collections!.Length == 2 && state.Routes.Length == 2 && state.Accounts.Length == 1,
                "overlapping views never duplicate routes or accounts or apply configuration");
            var groups = models.CollectionGroups();
            Check(groups.Revision == state.Revision && groups.Groups.Single(group => group.Id == "provider:codex").RouteIds.SequenceEqual(new[] { "route-a", "route-b" }) &&
                groups.Groups.Single(group => group.Id == "collection:security").RouteIds.SequenceEqual(new[] { "route-b", "route-a" }) &&
                groups.Groups.All(group => group.RouterTargetId is null), "groups retain central route identities and member ordering without a router");
            var staleRevision = state.Revision;
            state = models.PutCollection(state.Revision, new("security", "Security", ["route-a", "route-b"]));
            Throws<RevisionConflictException>(() => models.DeleteCollection(staleRevision, "free"));
            Check(state.Collections!.Select(collection => collection.Id).SequenceEqual(new[] { "security", "free" }), "renaming preserves collection order");
            var unchanged = JsonSerializer.Serialize(repository.Snapshot);
            foreach (var invalid in new ModelCollectionDto[]
            {
                new("bad/id", "Name", []), new("bad", "\n", []), new("bad", "Name", null!),
                new("bad", "Name", ["route-a", "route-a"]), new("bad", "Name", ["missing"]),
                new("bad", "Name", ["route-a"], "route-a")
            })
                Throws<ArgumentException>(() => models.PutCollection(state.Revision, invalid));
            Throws<ArgumentException>(() => repository.Update(state.Revision, configuration => configuration with
                { Collections = [new("duplicate", "One", []), new("duplicate", "Two", [])] }));
            Check(JsonSerializer.Serialize(repository.Snapshot) == unchanged, "invalid membership and unqualified routers cannot partially save");
            state.Collections![0].RouteIds[0] = "mutated-snapshot";
            Check(repository.Snapshot.Collections![0].RouteIds[0] == "route-a", "collection snapshot arrays are detached");
            var incoming = new ModelCollectionDto("empty", "Empty", []);
            state = models.PutCollection(repository.Snapshot.Revision, incoming);
            Check(state.Collections!.Last().RouteIds.Length == 0, "empty collections are valid");
            var restarted = new ConfigurationRepository(paths);
            Check(JsonSerializer.Serialize(restarted.Snapshot) == JsonSerializer.Serialize(repository.Snapshot), "ordered collections survive JSON restart");
            Throws<ArgumentException>(() => models.DeleteRoute(state.Revision, "route-a"));
            state = repository.Update(state.Revision, configuration => configuration with
                { Routes = configuration.Routes.Where(route => route.Id != "route-b").ToArray() });
            Check(state.Collections!.Single(collection => collection.Id == "security").RouteIds.SequenceEqual(new[] { "route-a" }) &&
                state.Collections!.Single(collection => collection.Id == "free").RouteIds.Length == 0,
                "repository prunes removed routes atomically across every collection");
            Throws<ArgumentException>(() => repository.Update(state.Revision, configuration => configuration with
                { Collections = [new("unknown", "Unknown", ["never-existed"])] }));
            state = models.DeleteCollection(state.Revision, "free");
            Check(JsonSerializer.Serialize(state.Accounts) == originalAccounts && JsonSerializer.Serialize(state.Profiles) == originalProfiles &&
                JsonSerializer.Serialize(state.Routes) == JsonSerializer.Serialize(JsonSerializer.Deserialize<RouteDto[]>(originalRoutes)!.Take(1)),
                "collection mutations preserve account identities, aliases and profile selection");
            await CheckEndpointsAsync(paths, models);
            Console.WriteLine("PASS model collections: legacy JSON, revisions, overlap, isolation, persistence, validation, route deletion and HTTP CRUD");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static async Task CheckEndpointsAsync(OwnStatePaths paths, ModelsService models)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(models);
        builder.Services.AddSingleton(new ProviderCatalogRegistry());
        builder.Services.AddSingleton(new ProxyConfigurationWriter(paths, new SecretStore(paths)));
        await using var app = builder.Build();
        app.MapModelEndpoints();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { BaseAddress = new(address) };
        var revision = models.Snapshot.Revision;
        using var mismatch = await client.PutAsJsonAsync(Api.Prefix + "/collections/wrong", new RevisionRequest<ModelCollectionDto>(revision, new("http", "HTTP", ["route-a"])));
        Check(mismatch.StatusCode == HttpStatusCode.BadRequest && models.Snapshot.Revision == revision, "HTTP rejects path identity mismatch");
        using var create = await client.PutAsJsonAsync(Api.Prefix + "/collections/http", new RevisionRequest<ModelCollectionDto>(revision, new("http", "HTTP", ["route-a"])));
        Check(create.IsSuccessStatusCode && models.Snapshot.Revision == revision + 1, "HTTP creates a collection at the shared revision gate");
        using var conflict = await client.DeleteAsync(Api.Prefix + "/collections/http?revision=" + revision);
        Check(conflict.StatusCode == HttpStatusCode.Conflict, "HTTP stale collection update returns 409");
        var collections = await client.GetFromJsonAsync<ModelCollectionDto[]>(Api.Prefix + "/collections");
        var groups = await client.GetFromJsonAsync<ModelCollectionGroupsDto>(Api.Prefix + "/model-groups");
        Check(collections!.Any(collection => collection.Id == "http") && groups!.Groups.Any(group => group.Id == "collection:http"), "HTTP exposes custom and provider views");
        using var missing = await client.DeleteAsync(Api.Prefix + "/collections/missing?revision=" + models.Snapshot.Revision);
        Check(missing.StatusCode == HttpStatusCode.NotFound, "HTTP missing collection returns 404");
        using var delete = await client.DeleteAsync(Api.Prefix + "/collections/http?revision=" + models.Snapshot.Revision);
        Check(delete.IsSuccessStatusCode && models.Snapshot.Collections!.All(collection => collection.Id != "http"), "HTTP removes only collection metadata");
        await app.StopAsync();
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
