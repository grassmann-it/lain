using System.Net;
using System.Text;
using System.Text.Json;
using Lain.Core;
using Lain.Host.Quota;
using Lain.Host.Providers.Poe;
using Lain.Host.Providers.OpenCode;

static class Tests
{
    private static int assertions;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); assertions++; }
    private static T Parse<T>(string json, Func<JsonElement, T> parse) { using var doc = JsonDocument.Parse(json); return parse(doc.RootElement); }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { assertions++; return; }
        throw new Exception(message);
    }
    private static async Task RejectAsync(Func<Task> action, string message)
    {
        try { await action(); } catch (Exception e) when (e is InvalidOperationException || e.GetType().Name == "QuotaFetchException") { assertions++; return; }
        throw new Exception(message);
    }

    public static async Task Main()
    {
        var account = new AccountDto("poe-account", "Poe", "poe", true, "api-key", "ready");
        var http = new FakeHttp(request =>
        {
            Check(request.Method == HttpMethod.Get, "Read-only provider calls");
            Check(request.Headers.Authorization?.Parameter == "synthetic-key", "Own credential only");
            if (request.RequestUri!.AbsolutePath == "/usage/current_balance") return """{"current_point_balance":295932027}""";
            Check(request.RequestUri.AbsolutePath == "/usage/points_history", "Official usage path");
            Check(request.RequestUri.Query.Contains("starting_after=id%2F%3F%26%3D"), "Cursor is escaped");
            return """{"has_more":true,"data":[{"creation_time":1761523099056852,"bot_name":"Claude-Sonnet-4.5","query_id":"query-1","usage_type":"API","cost_points":6554},{"creation_time":1761523092991619,"bot_name":"Claude-Sonnet-4.5","query_id":"query-2","usage_type":"Chat","cost_points":6683}]}""";
        });
        using var client = new HttpClient(http);
        var credential = new QuotaCredential("synthetic-key", baseUrl: "https://api.poe.com/v1");
        var adapter = new PoeQuotaAdapter(client);
        var row = (await adapter.FetchAsync(account, credential)).Readings.Single();
        Check(row.Remaining == 295932027 && row.Unit == "points", "Points remain points");
        Check(row.Limit is null && row.Used is null && row.ResetsAt is null && row.UsedPercent is null, "Combined balance does not invent allocation, resets or percent");
        var zero = Parse("""{"current_point_balance":0}""", PoeQuotaAdapter.Normalize).Single();
        Check(zero.Remaining == 0, "Measured zero is known");
        foreach (var json in new[] { "{}", "{\"current_point_balance\":null}", "{\"current_point_balance\":\"123\"}", "{\"current_point_balance\":-1}", "{\"current_point_balance\":1.5}", "{\"current_point_balance\":9007199254740992}" })
            Check(Parse(json, PoeQuotaAdapter.Normalize).Count == 0, "Invalid/unsafe balance stays unknown");
        foreach (var origin in new string?[] { null, "https://evil.invalid/v1", "https://api.poe.com.evil.invalid/v1", "http://api.poe.com/v1", "https://api.poe.com/v1?q=evil", "https://api.poe.com:444/v1" })
            await RejectAsync(async () => { await adapter.FetchAsync(account, new("must-not-leak", baseUrl: origin)); }, "Unqualified credential origin rejected");
        await RejectAsync(async () => { await adapter.FetchAsync(account with { CredentialKind = "codex-owned" }, credential); }, "Other OAuth token kind rejected");
        Check(http.Calls == 1, "Rejected origins/kinds make no requests");
        var page = await new PoeUsageHistory(client).FetchPageAsync(credential, 100, "id/?&=");
        Check(page.Entries.Count == 2 && page.HasMore == true && page.NextCursor == "query-2" && page.CompletePage, "History is explicitly paged");
        Check(page.Entries[0].CreatedAt == DateTimeOffset.UnixEpoch.AddTicks(17615230990568520), "History timestamps use microseconds, preserving precision");
        Check(page.Entries[1].UsageType == "Chat" && page.Entries[0].CostPoints == 6554, "Account history usage types are preserved, not treated as lain API totals");
        var partial = Parse("""{"has_more":true,"data":[{"query_id":"last-row","cost_points":"bad"}]}""", PoeUsageHistory.Normalize);
        Check(!partial.CompletePage && partial.Entries.Count == 0 && partial.NextCursor == "last-row", "Malformed measurements never fabricate history usage");
        var missingCursor = Parse("""{"has_more":true,"data":[{}]}""", PoeUsageHistory.Normalize);
        Check(missingCursor.NextCursor is null && !missingCursor.CompletePage, "Missing final cursor stays missing");
        Check(!Parse("{}", PoeUsageHistory.Normalize).CompletePage, "Unknown history schema stays unknown");
        var model = Parse("""{"data":[{"id":"step-5-preview-el","owned_by":"Community publisher","pricing":null,"architecture":{"input_modalities":["text","image"]},"context_window":{"context_length":1024000}}]}""", PoeModelCatalog.Normalize).Single();
        Check(model.Manufacturer is null && model.InferenceProvider is null && model.PublisherClaim == "Community publisher", "Publisher never becomes manufacturer/inference provider");
        Check(model.PricingClass == "unknown" && model.AdvertisedEndpoints.Length == 0 && model.Tools is null, "Null pricing and missing capability flags remain unknown");

        var attempt = PoeOAuthAttempt.Create("client-fixture", new("http://localhost:32123/callback"));
        var query = Query(attempt.AuthorizationUri.Query);
        Check(query["scope"] == "apikey:create" && query["code_challenge_method"] == "S256", "Official OAuth grant and mandatory S256 PKCE");
        Reject(() => attempt.CreateTokenRequest("code", "wrong-state"), "CSRF state mismatch rejected");
        using var tokenRequest = attempt.CreateTokenRequest("synthetic-code", query["state"]);
        Check(tokenRequest.RequestUri!.AbsoluteUri == PoeOAuthAttempt.TokenEndpoint && tokenRequest.Method == HttpMethod.Post, "Fixed official token endpoint");
        var form = Query(await tokenRequest.Content!.ReadAsStringAsync());
        var challenge = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"]))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Check(challenge == query["code_challenge"] && form["grant_type"] == "authorization_code", "Verifier matches challenge, no client secret or refresh flow");
        Reject(() => attempt.CreateTokenRequest("code", query["state"]), "Authorization attempts are one-use");
        Reject(() => PoeOAuthAttempt.Create("id", new("http://example.invalid/callback")), "Non-loopback insecure callback rejected");
        var issued = Parse("""{"api_key":"synthetic-secret","api_key_expires_in":60}""", e => PoeOAuthAttempt.ParseTokenResponse(e, DateTimeOffset.UnixEpoch));
        Check(issued?.ExpiresAt == DateTimeOffset.UnixEpoch.AddSeconds(60) && !issued.ToString().Contains("synthetic-secret"), "Issued key expiry parsed, diagnostics redacted");
        var noExpiry = Parse("""{"api_key":"x","api_key_expires_in":null}""", e => PoeOAuthAttempt.ParseTokenResponse(e, DateTimeOffset.UnixEpoch));
        Check(noExpiry?.ApiKey == "x" && noExpiry.ExpiresAt is null, "Explicit no-expiry preserved");
        Check(Parse("""{"error":"invalid_grant","api_key":"untrusted","api_key_expires_in":60}""", e => PoeOAuthAttempt.ParseTokenResponse(e, DateTimeOffset.UnixEpoch)) is null, "Error envelope never yields a key");

        Check(OpenCodeGoAdapter.Resolve("step-5-preview")?.Protocol == "openai-chat", "Step uses documented Chat protocol");
        Check(OpenCodeGoAdapter.Resolve("gpt-6-luna")?.Endpoint.AbsolutePath.EndsWith("/responses") == true, "GPT uses Responses");
        Check(OpenCodeGoAdapter.Resolve("qwen3.8-max")?.Protocol == "anthropic-messages", "Manufacturer does not imply protocol");
        Check(OpenCodeGoAdapter.Resolve("gemini-4-argon-contributor", true)?.Endpoint.AbsoluteUri.EndsWith(":streamGenerateContent?alt=sse") == true, "Gemini has distinct streaming endpoint");
        Check(OpenCodeGoAdapter.Resolve("unknown-model") is null && OpenCodeGoAdapter.QuotaState == "unsupported", "Unknown routes/quota are not guessed");
        using var request = OpenCodeGoAdapter.CreateRequest("step-5-preview", "openai-chat", """{"model":"step-5-preview","messages":[]}""", "synthetic-key", "conversation-1");
        Check(request.Headers.UserAgent.ToString() == "lain/1.0" && request.Headers.GetValues("x-opencode-session").Single() == "conversation-1", "Go receives explicit agent and stable conversation ID");
        Reject(() => OpenCodeGoAdapter.CreateRequest("step-5-preview", "openai-responses", "{}", "x", "s"), "Protocol mismatch rejected");
        Reject(() => OpenCodeGoAdapter.CreateRequest("step-5-preview", "openai-chat", """{"model":"paid-fallback"}""", "x", "s"), "Payload cannot silently choose another route");
        Reject(() => OpenCodeGoAdapter.CreateRequest("step-5-preview", "openai-chat", "{}", "x", "bad\r\nheader"), "Session header injection rejected");
        Check(Parse("""{"object":"list","data":[{"id":"step-5-preview","owned_by":"opencode"},{"id":"step-5-preview"},{}]}""", OpenCodeGoAdapter.NormalizeModels).SequenceEqual(["step-5-preview"]), "Model list proves IDs only, does not infer protocols from owned_by");
        Console.WriteLine($"Poe/OpenCode: {assertions} assertions passed (synthetic only; no credentials).");
    }

    private static Dictionary<string, string> Query(string query) => query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(p => p.Split('=', 2)).ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));
}

sealed class FakeHttp(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respond(request), Encoding.UTF8, "application/json") });
    }
}
