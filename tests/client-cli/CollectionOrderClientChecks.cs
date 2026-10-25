using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lain.Client;
using Lain.Core;

internal static class CollectionOrderClientChecks
{
    public static async Task<int> RunAsync()
    {
        var assertions = 0;
        void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
        var collection = new ModelCollectionDto("security", "ITSec", ["route-b", "route-a"], "router");
        var router = new CollectionRouterDto("router", "auto-private", "account:private", "profile");
        var configuration = new ConfigurationDto(8, 4, [], [], [], Collections:[collection], CollectionRouters:[router]);
        var handler = new Handler();
        using var http = new HttpClient(handler) { BaseAddress = new("http://fixture.invalid") };
        using var client = new LainApiClient(http, "fixture-management-token");
        handler.Respond = request =>
        {
            Check(request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/v1/collections/order", "Reorder uses a separate POST action without shadowing collection CRUD");
            Check(request.Headers.Authorization?.Parameter == "fixture-management-token", "Collection reorder retains host authorization");
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Check(body.RootElement.GetProperty("revision").GetInt64() == 7 &&
                body.RootElement.GetProperty("value").GetProperty("collectionIds").EnumerateArray().Select(id => id.GetString()).SequenceEqual(new[] { "million", "security" }), "Typed reorder carries the complete ID order under the shared revision envelope");
            return Ok(configuration);
        };
        Check((await client.ReorderCollectionsAsync(7, ["million", "security"])).AppliedRevision == 4, "Client receives saved configuration without implying Apply");
        foreach (var operation in new[] { "read-collections", "read-groups", "read-routers", "save-collection", "remove-collection", "save-router", "remove-router" })
        {
            handler.Respond = request =>
            {
                var path = request.RequestUri!.PathAndQuery;
                switch (operation)
                {
                    case "read-collections": Check(request.Method == HttpMethod.Get && path == "/api/v1/collections", "Collection discovery path"); return Ok(new[] { collection });
                    case "read-groups": Check(request.Method == HttpMethod.Get && path == "/api/v1/model-groups", "Derived group discovery path"); return Ok(new ModelCollectionGroupsDto(1, 8, [new("account:private", "Codex (Private)", "account", ["route-a"], "codex")]));
                    case "read-routers": Check(request.Method == HttpMethod.Get && path == "/api/v1/collection-routers", "Router discovery path"); return Ok(new[] { router });
                    case "save-collection":
                        Check(request.Method == HttpMethod.Put && path == "/api/v1/collections/security", "Collection save path");
                        using (var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()))
                            Check(body.RootElement.GetProperty("revision").GetInt64() == 7 && body.RootElement.GetProperty("value").GetProperty("routeIds").EnumerateArray().Select(id => id.GetString()).SequenceEqual(new[] { "route-b", "route-a" }) &&
                                body.RootElement.GetProperty("value").GetProperty("routerTargetId").GetString() == "router", "Collection save preserves ordered members and router reference");
                        break;
                    case "save-router":
                        Check(request.Method == HttpMethod.Put && path == "/api/v1/collection-routers/router", "Router save path");
                        using (var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()))
                            Check(body.RootElement.GetProperty("revision").GetInt64() == 7 && body.RootElement.GetProperty("value").GetProperty("groupId").GetString() == "account:private" &&
                                body.RootElement.GetProperty("value").GetProperty("profileId").GetString() == "profile", "Router save retains explicit account and profile binding");
                        break;
                    case "remove-collection": Check(request.Method == HttpMethod.Delete && path == "/api/v1/collections/security?revision=7", "Collection delete keeps explicit CAS"); break;
                    case "remove-router": Check(request.Method == HttpMethod.Delete && path == "/api/v1/collection-routers/router?revision=7", "Router delete keeps explicit CAS"); break;
                }
                return Ok(configuration);
            };
            switch (operation)
            {
                case "read-collections": Check((await client.CollectionsAsync()).Single().RouterTargetId == "router", "Collections deserialize complete metadata"); break;
                case "read-groups": Check((await client.ModelGroupsAsync()).Groups.Single().Id == "account:private", "Derived account groups remain typed"); break;
                case "read-routers": Check((await client.CollectionRoutersAsync()).Single().GroupId == "account:private", "Account routers deserialize"); break;
                case "save-collection": await client.SaveCollectionAsync(7, collection); break;
                case "save-router": await client.SaveCollectionRouterAsync(7, router); break;
                case "remove-collection": await client.RemoveCollectionAsync(7, collection.Id); break;
                case "remove-router": await client.RemoveCollectionRouterAsync(7, router.Id); break;
            }
        }
        handler.Respond = _ => new(HttpStatusCode.Conflict) { Content = JsonContent.Create(new ApiError("revision_conflict", "Configuration changed; reload and retry.")) };
        try { await client.ReorderCollectionsAsync(7, ["security"]); throw new Exception("Stale reorder accepted"); }
        catch (LainApiException error) { Check(error.StatusCode == HttpStatusCode.Conflict && error.Code == "revision_conflict", "Reorder conflicts remain typed 409 errors for draft retention"); }
        handler.Respond = _ => new(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new ApiError("router_provider_accounts_ambiguous", "Select an explicit account group. Router: router. Profile: profile.")) };
        try { await client.ExportCodexProfileAsync(8, "profile", "/fixture/lain", "/fixture/models.json", enableLainModelPicker:true); throw new Exception("Ambiguous router accepted"); }
        catch (LainApiException error) { Check(error.Code == "router_provider_accounts_ambiguous" && error.Message.Contains("Router: router"), "Client preserves safe router code and reference diagnostics"); }
        return assertions;
    }

    private static HttpResponseMessage Ok<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private sealed class Handler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => throw new Exception("No fixture response");
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(Respond(request));
    }
}
