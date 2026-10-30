using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lain.Client;
using Lain.Cli;
using Lain.Core;

var assertions = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); assertions++; }
var handler = new RecordingHandler();
using var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:18418/") };
LainApiClient Client() => new(http, "SYNTHETIC_HOST_TOKEN");
async Task<(int Exit, string Output, string Error)> Run(params string[] args)
{
    using var output = new StringWriter(); using var error = new StringWriter();
    var exit = await CliApplication.RunAsync(args, output, error, Client);
    return (exit, output.ToString(), error.ToString());
}
var status = await Run("recording", "status", "--json");
Check(status.Exit == 0 && status.Output.Contains("contractVersion") && !status.Output.Contains("SYNTHETIC_HOST_TOKEN"), "status is typed and credentials remain out of output");
var invalid = await Run("recording", "enable", "--revision", "0", "--json");
Check(invalid.Exit == 2, "content or metadata consent must be explicit");
var enabled = await Run("recording", "enable", "--mode", "content", "--revision", "0", "--json");
Check(enabled.Exit == 0 && handler.Policy is { Enabled: true, Mode: "content", Revision: 1 }, "enable updates recording revision only");
var stale = await Run("recording", "off", "--revision", "0", "--json");
Check(stale.Exit == 5 && handler.Policy.Enabled, "stale mutation fails without changing policy");
var list = await Run("recording", "list", "--model", "model-a", "--limit", "1", "--json");
Check(list.Exit == 0 && handler.LastQuery.Contains("model=model-a") && handler.LastQuery.Contains("limit=1"), "CLI and desktop share bounded typed conversation query");
var shown = await Run("recording", "show", "chat-a", "--limit", "1", "--json");
Check(shown.Exit == 0 && shown.Output.Contains("chat-a"), "content read requires explicit selected show");
var path = await Run("recording", "show", "../outside", "--json");
Check(path.Exit == 2, "conversation path traversal rejected locally");
var oversized = await Run("recording", "list", "--limit", "101", "--json");
Check(oversized.Exit == 2, "CLI page count bound enforced before HTTP");
var auth = await Run("recording", "status", "--token", "never-argv");
Check(auth.Exit == 2, "recording commands accept no auth argument");
Check(handler.Paths.All(p => p.StartsWith("/api/v1/recording/")) && handler.Authenticated, "all commands use authenticated recording-only common client");
Console.WriteLine($"PASS {assertions} recording CLI/client assertions");

sealed class RecordingHandler : HttpMessageHandler
{
    public CapturePolicySnapshot Policy = new();
    public List<string> Paths = [];
    public string LastQuery = "";
    public bool Authenticated = true;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath; Paths.Add(path); LastQuery = request.RequestUri.Query;
        Authenticated &= request.Headers.Authorization?.Parameter == "SYNTHETIC_HOST_TOKEN";
        object value;
        if (path.EndsWith("/status")) value = new RecordingHealthDto(1, Policy, "off");
        else if (path.EndsWith("/policy"))
        {
            if (request.Method == HttpMethod.Put)
            {
                var update = await request.Content!.ReadFromJsonAsync<RecordingPolicyUpdate>(ct);
                if (update!.ExpectedRevision != Policy.Revision) return new(HttpStatusCode.Conflict) { Content = JsonContent.Create(new ApiError("stale_revision", "Reload recording state.")) };
                Policy = new(Policy.Revision + 1, update.Enabled, update.Mode, update.Scope, Policy.StopGeneration + 1);
            }
            value = Policy;
        }
        else if (path.EndsWith("/conversations")) value = new RecordingConversationPage([]);
        else if (path.EndsWith("/conversations/chat-a")) value = new RecordingConversationDetail(new("chat-a", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, 0, ["model-a"]), []);
        else return new(HttpStatusCode.NotFound) { Content = JsonContent.Create(new ApiError("not_found", "Synthetic route absent.")) };
        return new(HttpStatusCode.OK) { Content = JsonContent.Create(value, value.GetType()) };
    }
}
