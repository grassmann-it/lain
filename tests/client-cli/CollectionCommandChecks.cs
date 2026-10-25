using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lain.Client;
using Lain.Cli;
using Lain.Core;

internal static class CollectionCommandChecks
{
    public static async Task<int> RunAsync()
    {
        var checks = 0;
        var configuration = new ConfigurationDto(7, 4, [],
            [new("route-a", "first", "upstream-a", "codex", ["account"]), new("route-b", "second", "upstream-b", "codex", ["account"])],
            [new("lain", "lain", ["route-a"])], Collections: [new("it-sec", "IT Sec", ["route-a"])]);
        var handler = new Handler();
        using var http = new HttpClient(handler) { BaseAddress = new("http://fixture.invalid") };
        using var client = new LainApiClient(http);
        var calls = new List<string>();
        handler.Respond = request =>
        {
            calls.Add(request.Method + " " + request.RequestUri!.PathAndQuery);
            return request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/configuration" => Ok(configuration),
                "/api/v1/collections" => Ok(configuration.Collections),
                "/api/v1/model-groups" => Ok(new ModelCollectionGroupsDto(1, 7, [new("collection:it-sec", "IT Sec", "custom", ["route-a"])])),
                _ => throw new Exception("Unexpected read")
            };
        };
        Require((await client.CollectionsAsync()).Single().Id == "it-sec", "typed collection list"); checks++;
        Require((await client.ModelGroupsAsync()).Revision == 7, "typed revisioned groups"); checks++;
        calls.Clear();
        var listed = await Run(["collections", "--json"]);
        using (var json = JsonDocument.Parse(listed.Output))
            Require(listed.Exit == 0 && json.RootElement.GetProperty("revision").GetInt64() == 7 &&
                json.RootElement.GetProperty("collections")[0].GetProperty("routeIds")[0].GetString() == "route-a" &&
                calls.SequenceEqual(new[] { "GET /api/v1/configuration" }), "CLI lists definitions and revision in one snapshot");
        checks++;
        var human = await Run(["collections", "list"]);
        Require(human.Exit == 0 && human.Output.Contains("IT Sec") && human.Output.Contains("revision 7") && human.Output.Contains("route-a"), "human collection list preserves IDs"); checks++;

        handler.Respond = request =>
        {
            calls.Add(request.Method + " " + request.RequestUri!.PathAndQuery);
            if (request.Method == HttpMethod.Get) return Ok(configuration);
            Require(request.Method == HttpMethod.Put && request.RequestUri!.AbsolutePath == "/api/v1/collections/it-sec", "save binds the collection ID");
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var value = body.RootElement.GetProperty("value");
            Require(body.RootElement.GetProperty("revision").GetInt64() == 7 && value.GetProperty("id").GetString() == "it-sec" &&
                value.GetProperty("name").GetString() == "IT Sec" && value.GetProperty("routeIds").EnumerateArray().Select(item => item.GetString()).SequenceEqual(new[] { "route-b", "route-a" }),
                "save sends explicit revision and ordered central route IDs after alias resolution");
            return Ok(configuration with { Revision = 8, Collections = [new("it-sec", "IT Sec", ["route-b", "route-a"])] });
        };
        calls.Clear();
        var saved = await Run(["collections", "save", "it-sec", "--name", "IT Sec", "--routes", "SECOND,route-a", "--revision", "7", "--json"]);
        Require(saved.Exit == 0 && JsonDocument.Parse(saved.Output).RootElement.GetProperty("appliedRevision").GetInt64() == 4 &&
            calls.SequenceEqual(new[] { "GET /api/v1/configuration", "PUT /api/v1/collections/it-sec" }), "save performs no apply, activation, export or extra mutations"); checks++;
        calls.Clear();
        var stale = await Run(["collections", "upsert", "it-sec", "--name", "IT Sec", "--routes", "first", "--revision", "6"]);
        Require(stale.Exit == 5 && stale.Error.Contains("revision_conflict") && calls.SequenceEqual(new[] { "GET /api/v1/configuration" }), "stale alias snapshot never mutates"); checks++;
        foreach (var routes in new[] { "missing", "first,route-a", "first,", "first,first" })
        {
            calls.Clear();
            var invalid = await Run(["collections", "save", "it-sec", "--name", "IT Sec", "--routes", routes, "--revision", "7"]);
            Require(invalid.Exit == 2 && !calls.Any(call => call.StartsWith("PUT", StringComparison.Ordinal)), "invalid route members never mutate"); checks++;
        }
        var ambiguous = configuration with { Routes = configuration.Routes.Append(new("route-c", "FIRST", "upstream-c", "codex", ["account"])).ToArray() };
        handler.Respond = _ => Ok(ambiguous);
        var collision = await Run(["collections", "save", "it-sec", "--name", "IT Sec", "--routes", "first", "--revision", "7"]);
        Require(collision.Exit == 2 && collision.Error.Contains("ambiguous"), "ambiguous alias requires a route ID"); checks++;
        foreach (var args in new[]
        {
            new[] { "collections", "save", "it-sec", "--name", "IT Sec", "--routes", "first" },
            new[] { "collections", "delete", "it-sec" },
            new[] { "collections", "delete", "IT Sec", "--revision", "7" },
            new[] { "collections", "save", "it-sec", "--name", "IT Sec", "--revision", "7" }
        })
        {
            var output = new StringWriter(); var error = new StringWriter();
            var exit = await CliApplication.RunAsync(args, output, error, () => throw new Exception("Must validate before client discovery"));
            Require(exit == 2 && output.ToString() == "", "mutation syntax and explicit revision are required before discovery"); checks++;
        }
        handler.Respond = request =>
        {
            Require(request.Method == HttpMethod.Delete && request.RequestUri!.PathAndQuery == "/api/v1/collections/it-sec?revision=7", "delete carries explicit ID and revision");
            return Ok(configuration with { Revision = 8, Collections = [] });
        };
        var deleted = await Run(["collections", "delete", "it-sec", "--revision", "7"]);
        Require(deleted.Exit == 0 && deleted.Output.Contains("it-sec deleted") && deleted.Output.Contains("revision 8"), "CLI deletes only definition with typed API"); checks++;
        handler.Respond = _ => new(HttpStatusCode.Conflict) { Content = JsonContent.Create(new ApiError("revision_conflict", "Reload first.")) };
        var rejected = await Run(["collections", "delete", "it-sec", "--revision", "7", "--json"]);
        Require(rejected.Exit == 5 && rejected.Output == "" && rejected.Error.Contains("revision_conflict"), "server conflict is preserved without success output"); checks++;
        return checks;

        async Task<(int Exit, string Output, string Error)> Run(string[] args)
        {
            var output = new StringWriter(); var error = new StringWriter();
            var exit = await CliApplication.RunAsync(args, output, error, () => client);
            return (exit, output.ToString(), error.ToString());
        }
    }

    private static HttpResponseMessage Ok<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Handler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => throw new Exception("No fixture response");
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(Respond(request));
    }
}
