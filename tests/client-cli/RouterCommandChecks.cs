using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lain.Client;
using Lain.Cli;
using Lain.Core;

internal static class RouterCommandChecks
{
    public static async Task<int> RunAsync()
    {
        var checks = 0;
        var configuration = new ConfigurationDto(7, 4, [], [], [new("host-work", "Work", [])],
            CollectionRouters: [new("security", "auto-security", "collection:it-sec", "host-work", Enabled: false)]);
        var calls = new List<string>();
        var handler = new Handler();
        using var http = new HttpClient(handler) { BaseAddress = new("http://fixture.invalid") };
        using var client = new LainApiClient(http);
        handler.Respond = request =>
        {
            calls.Add(request.Method + " " + request.RequestUri!.PathAndQuery);
            return request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/configuration" => Ok(configuration),
                "/api/v1/collection-routers" => Ok(configuration.CollectionRouters),
                _ => throw new Exception("Unexpected read")
            };
        };
        Require((await client.CollectionRoutersAsync()).Single().Id == "security", "typed router GET uses collection-routers"); checks++;
        calls.Clear();
        var listed = await Run(["routers", "--json"]);
        using (var json = JsonDocument.Parse(listed.Output))
            Require(listed.Exit == 0 && json.RootElement.GetProperty("revision").GetInt64() == 7 &&
                json.RootElement.GetProperty("routers")[0].GetProperty("groupId").GetString() == "collection:it-sec" &&
                calls.SequenceEqual(new[] { "GET /api/v1/configuration" }), "router list reads definitions with one revisioned snapshot");
        checks++;
        var human = await Run(["routers", "list"]);
        Require(human.Exit == 0 && human.Output.Contains("auto-security") && human.Output.Contains("revision 7") && human.Output.Contains("host-work"), "human router list identifies alias, group and host profile"); checks++;
        handler.Respond = request =>
        {
            calls.Add(request.Method + " " + request.RequestUri!.PathAndQuery);
            if (request.Method == HttpMethod.Get) return Ok(configuration);
            Require(request.Method == HttpMethod.Put && request.RequestUri!.AbsolutePath == "/api/v1/collection-routers/security", "typed save binds router ID");
            using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var value = payload.RootElement.GetProperty("value");
            Require(payload.RootElement.GetProperty("revision").GetInt64() == 7 && value.GetProperty("id").GetString() == "security" &&
                value.GetProperty("alias").GetString() == "auto-security" && value.GetProperty("groupId").GetString() == "collection:it-sec" &&
                value.GetProperty("profileId").GetString() == "host-work" && value.GetProperty("costPolicy").GetString() == "configured" &&
                !value.GetProperty("enabled").GetBoolean(), "router save preserves exact IDs, explicit cost/revision and disabled state");
            return Ok(configuration with { Revision = 8 });
        };
        calls.Clear();
        var saveArgs = new[] { "routers", "save", "security", "--alias", "auto-security", "--group", "collection:it-sec", "--profile", "host-work", "--cost", "configured", "--revision", "7", "--json" };
        var saved = await Run(saveArgs);
        Require(saved.Exit == 0 && JsonDocument.Parse(saved.Output).RootElement.GetProperty("appliedRevision").GetInt64() == 4 &&
            calls.SequenceEqual(new[] { "GET /api/v1/configuration", "PUT /api/v1/collection-routers/security" }), "save performs no profile application, export or inference requests"); checks++;
        calls.Clear();
        var staleArgs = saveArgs.ToArray(); staleArgs[12] = "6";
        var stale = await Run(staleArgs);
        Require(stale.Exit == 5 && stale.Error.Contains("revision_conflict") && calls.SequenceEqual(new[] { "GET /api/v1/configuration" }), "stale edit cannot mutate a newer snapshot"); checks++;
        var invalidArgs = new List<string[]>
        {
            ["routers", "delete", "security"],
            ["routers", "delete", "invalid/id", "--revision", "7"],
            ["routers", "list", "--revision", "7"]
        };
        foreach (var (index, value) in new[] { (2, "bad/id"), (4, ""), (6, "IT Sec"), (6, "provider:OpenAI"), (6, "collection:"), (6, "policy:free"), (8, "Work name"), (10, "cheapest"), (12, "-1") })
        {
            var invalid = saveArgs.ToArray(); invalid[index] = value; invalidArgs.Add(invalid);
        }
        invalidArgs.Add(["routers", "save", "security", "--alias", "auto", "--group", "collection:it-sec", "--profile", "host-work", "--cost", "configured"]);
        foreach (var args in invalidArgs)
        {
            var output = new StringWriter(); var error = new StringWriter();
            var exit = await CliApplication.RunAsync(args, output, error, () => throw new Exception("Must validate before discovery"));
            Require(exit == 2 && output.ToString() == "", "invalid router syntax is rejected before host discovery"); checks++;
        }
        handler.Respond = request =>
        {
            if (request.Method == HttpMethod.Get) return Ok(configuration);
            Require(request.Method == HttpMethod.Put && request.RequestUri!.AbsolutePath == "/api/v1/collection-routers/free", "new free router uses its explicit ID");
            using var payload = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var router = payload.RootElement.GetProperty("value");
            Require(router.GetProperty("groupId").GetString() == "policy:free" && router.GetProperty("costPolicy").GetString() == "free_only" && router.GetProperty("enabled").GetBoolean(),
                "new free router carries explicit free_only enforcement requirement");
            return Ok(configuration with { Revision = 8, CollectionRouters = [new("free", "auto-free", "policy:free", "host-work", "free_only")] });
        };
        var free = await Run(["routers", "save", "free", "--alias", "auto-free", "--group", "policy:free", "--profile", "host-work", "--cost", "free_only", "--revision", "7"]);
        Require(free.Exit == 0 && free.Output.Contains("free_only"), "free router saves its definition without performing inference"); checks++;
        handler.Respond = request =>
        {
            Require(request.Method == HttpMethod.Delete && request.RequestUri!.PathAndQuery == "/api/v1/collection-routers/security?revision=7", "typed delete uses exact ID and explicit revision");
            return Ok(configuration with { Revision = 8, CollectionRouters = [] });
        };
        var deleted = await Run(["routers", "delete", "security", "--revision", "7"]);
        Require(deleted.Exit == 0 && deleted.Output.Contains("security deleted") && deleted.Output.Contains("revision 8"), "router delete reports persisted revision"); checks++;
        handler.Respond = _ => new(HttpStatusCode.Conflict) { Content = JsonContent.Create(new ApiError("revision_conflict", "Reload first.")) };
        var conflict = await Run(["routers", "delete", "security", "--revision", "7", "--json"]);
        Require(conflict.Exit == 5 && conflict.Output == "" && conflict.Error.Contains("revision_conflict"), "router server CAS errors never report success"); checks++;
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
