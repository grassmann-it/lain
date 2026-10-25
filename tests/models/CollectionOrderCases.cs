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

internal static class CollectionOrderCases
{
    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lain-collection-order-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new OwnStatePaths(directory);
            var repository = new ConfigurationRepository(paths);
            var state = repository.Update(0, configuration => configuration with {
                Accounts = [new("account", "Fixture", "fixture", true, "api-key", "ready")],
                Routes = [new("a", "alias-a", "model", "fixture", ["account"]), new("b", "alias-b", "model", "fixture", ["account"])],
                Profiles = [new("profile", "Fixture", ["a", "b"], "a")],
                Collections = [new("security", "ITSec", ["b", "a"], "router"), new("million", "1M", ["a"]), new("order", "Existing ID", [])],
                CollectionRouters = [new("router", "auto-security", "collection:security", "profile")]
            });
            repository.MarkApplied(state.Revision);
            state = repository.Snapshot;
            var models = new ModelsService(repository);
            foreach (var invalid in new string[][] { ["security", "security", "order"], ["security", "million"],
                ["security", "million", "foreign"], ["security", "million", ""], ["security", "million", null!], [], null! })
            {
                var before = JsonSerializer.Serialize(repository.Snapshot);
                var disk = File.ReadAllBytes(paths.Configuration);
                Reject<ArgumentException>(() => models.ReorderCollections(state.Revision, invalid));
                Check(JsonSerializer.Serialize(repository.Snapshot) == before && File.ReadAllBytes(paths.Configuration).SequenceEqual(disk), "Invalid permutation must not mutate revision, memory or disk");
            }
            var original = state;
            state = models.ReorderCollections(state.Revision, ["order", "million", "security"]);
            Check(state.Collections!.Select(collection => collection.Id).SequenceEqual(new[] { "order", "million", "security" }), "Complete collection order is saved");
            Check(state.Revision == original.Revision + 1 &&
                JsonSerializer.Serialize(state.Collections!.OrderBy(collection => collection.Id, StringComparer.Ordinal)) == JsonSerializer.Serialize(original.Collections!.OrderBy(collection => collection.Id, StringComparer.Ordinal)) &&
                JsonSerializer.Serialize(state with { Revision = original.Revision, Collections = original.Collections }) == JsonSerializer.Serialize(original), "Reorder preserves applied revision, every collection payload, routers, accounts, routes and profiles");
            Reject<RevisionConflictException>(() => models.ReorderCollections(original.Revision, ["foreign"]));
            Check(JsonSerializer.Serialize(new ConfigurationRepository(paths).Snapshot) == JsonSerializer.Serialize(state), "Reorder survives restart without changing stable identities");

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
            using var reordered = await client.PostAsJsonAsync(Api.Prefix + "/collections/order", new RevisionRequest<CollectionOrderRequest>(state.Revision, new(["security", "order", "million"])));
            Check(reordered.StatusCode == HttpStatusCode.OK && (await reordered.Content.ReadFromJsonAsync<ConfigurationDto>())!.Collections!.Select(collection => collection.Id).SequenceEqual(new[] { "security", "order", "million" }), "HTTP reorder uses the complete CAS envelope and returns configuration");
            var current = models.Snapshot;
            foreach (var invalid in new string[][] { ["security", "security", "million"], ["order"], ["security", "order", "foreign"] })
            {
                using var rejected = await client.PostAsJsonAsync(Api.Prefix + "/collections/order", new RevisionRequest<CollectionOrderRequest>(current.Revision, new(invalid)));
                Check(rejected.StatusCode == HttpStatusCode.BadRequest && JsonSerializer.Serialize(models.Snapshot) == JsonSerializer.Serialize(current), "HTTP invalid permutation is rejected atomically");
            }
            using var stale = await client.PostAsJsonAsync(Api.Prefix + "/collections/order", new RevisionRequest<CollectionOrderRequest>(state.Revision, new(["foreign"])));
            Check(stale.StatusCode == HttpStatusCode.Conflict && (await stale.Content.ReadFromJsonAsync<ApiError>())!.Code == "revision_conflict", "Stale HTTP reorder returns 409 before validation");
            using var editedOrder = await client.PutAsJsonAsync(Api.Prefix + "/collections/order", new RevisionRequest<ModelCollectionDto>(current.Revision, current.Collections!.Single(collection => collection.Id == "order") with { Name = "Still editable" }));
            Check(editedOrder.IsSuccessStatusCode && models.Snapshot.Collections!.Single(collection => collection.Id == "order").Name == "Still editable", "The existing collection ID 'order' retains its PUT CRUD endpoint");
            var emptyRepository = new ConfigurationRepository(new OwnStatePaths(Path.Combine(directory, "empty")));
            Check(new ModelsService(emptyRepository).ReorderCollections(0, []).Collections is { Length: 0 }, "Empty collection sets accept the complete empty permutation");
            await app.StopAsync();
            Console.WriteLine("PASS collection reorder: full permutation, atomic CAS, preservation, persistence and HTTP contract");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
