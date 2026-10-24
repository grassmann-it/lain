using System.Text.Json;
using Lain.Core;
using Lain.Host.Accounts;
using Lain.Host.Operations;
using Lain.Host.Providers;

internal static class ConnectionChecks
{
    internal static int Run()
    {
        var count = 0;
        void Check(bool condition, string label) { if (!condition) throw new Exception(label); count++; }
        void Reject(Action action, string label)
        {
            try { action(); }
            catch (ArgumentException) { count++; return; }
            catch (InvalidDataException) { count++; return; }
            throw new Exception(label);
        }
        ProviderRouteDescriptorDto Route(string id, string plan = "free", string protocol = "openai-chat") =>
            new(id, id, id, protocol == "openai-chat" ? "https://connection.invalid/v1" : "https://connection.invalid/anthropic/v1",
                protocol, [plan], new());
        var descriptor = new ProviderDescriptorDto("connection-fixture", "Connection fixture", "Synthetic setup qualification",
            "https://connection.invalid/keys", "https://connection.invalid/docs", [], [],
            [new("free", "Free", "free", "account", "tokens"), new("payg", "Paid", "payg", "account", "usd")],
            [Route("model-a"), Route("model-b"), Route("paid-model", "payg"), Route("other-protocol", protocol: "anthropic-messages")], [], []);
        var catalog = new ProviderCatalogRegistry([descriptor]);
        var provider = catalog.Find(descriptor.Id)!;
        var connection = provider.Connections!.Single(c => c.Protocol == "openai-chat");
        Check(connection.ProviderRouteIds.Length == 3 && connection.BillingPlanIds.Length == 2, "Endpoint/protocol groups compatible templates and billing choices.");
        var reordered = new ProviderCatalogRegistry([descriptor with { Routes = descriptor.Routes.Reverse().ToArray() }]);
        Check(reordered.Find(descriptor.Id)!.Connections!.Single(c => c.Protocol == "openai-chat").Id == connection.Id, "Connection identity independent of route order.");
        var explicitDescriptor = descriptor with { Connections = provider.Connections };
        Check(new ProviderCatalogRegistry([explicitDescriptor]).Find(descriptor.Id)!.Connections!.Any(c => c.Id == connection.Id), "Valid explicit IDs preserved.");
        Reject(() => new ProviderCatalogRegistry([descriptor with { Connections = [] }]), "Missing explicit model coverage accepted.");
        Reject(() => new ProviderCatalogRegistry([descriptor with { Connections = [connection with { ProviderRouteIds = ["nonexistent"] }] }]), "Unknown connection template accepted.");
        Reject(() => new ProviderCatalogRegistry([descriptor with { Connections = [connection with { Protocol = "gemini" }] }]), "Mixed protocols accepted.");
        Reject(() => new ProviderCatalogRegistry([descriptor with { Connections = [connection with { BillingPlanIds = ["free"] }] }]), "Dropped model billing choice accepted.");
        Reject(() => new ProviderCatalogRegistry([descriptor with { Routes = [Route("same-model"), Route("same-model")] }]), "Duplicate model identity accepted.");
        Reject(() => new ProviderCatalogRegistry([descriptor with { Routes = [Route("model-a") with { BaseUrl = "https://connection.invalid/v1?key=forbidden" }] }]), "Endpoint query accepted.");
        connection.ProviderRouteIds[0] = "mutated";
        Check(!catalog.FindConnection(descriptor.Id, connection.Id)!.ProviderRouteIds.Contains("mutated"), "Catalog snapshots isolate nested arrays.");
        connection = catalog.FindConnection(descriptor.Id, connection.Id)!;
        Check(new ProviderCatalogRegistry().Find("gemini-api")!.MonitorAccessOptions!.Any(o => o.Kind == "access-token-snapshot"), "Verified monitor access guidance enriched.");
        Check(new ProviderCatalogRegistry().Find("gemini-api")!.DefaultAccountName == "Gemini", "Short default name enriched.");
        var directory = Path.Combine(Path.GetTempPath(), "lain-connection-fixture-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new OwnStatePaths(directory);
            var secrets = new SecretStore(paths);
            var repository = new ConfigurationRepository(paths);
            var service = new AccountService(repository, secrets, catalog);
            var input = new AddApiAccountRequest("Own name", descriptor.Id, "synthetic-connection-key", connection.BaseUrl,
                Settings: new() { ["billingPlan"] = "free" }, ProviderConnectionId: connection.Id);
            Check(!input.ToString().Contains("synthetic-connection-key"), "Add API request redacts private material.");
            var state = service.AddApi(0, input);
            var account = state.Accounts.Single();
            Check(state.Revision == 1 && state.AppliedRevision == 0 && state.Routes.Length == 0, "Empty selection saves only account once.");
            Check(account.Name == "Own name" && account.State == "unverified", "User name and unverified status preserved.");
            using (var stored = JsonDocument.Parse(secrets.ReadAccount(account.Id)))
            {
                var permitted = stored.RootElement.GetProperty("providerRouteIds").EnumerateArray().Select(p => p.GetString()).ToArray();
                Check(permitted.Order().SequenceEqual(new[] { "model-a", "model-b" }), "Private connection permits all compatible free templates, excluding paid ones.");
                Check(stored.RootElement.GetProperty("providerConnectionId").GetString() == connection.Id, "Private connection identity retained.");
            }
            var before = secrets.ReadAccount(account.Id);
            foreach (var invalid in new[]
            {
                input with { ApiKey = "rejected", SelectedModelRouteIds = ["paid-model"] },
                input with { ApiKey = "rejected", SelectedModelRouteIds = ["other-protocol"] },
                input with { ApiKey = "rejected", SelectedModelRouteIds = ["model-a", "model-a"] },
                input with { ApiKey = "rejected", ProviderRouteId = "model-a" },
                input with { ApiKey = "rejected", ProviderConnectionId = null, SelectedModelRouteIds = [] },
                input with { ApiKey = "rejected", Settings = new() { ["billingPlan"] = "unknown" } },
                input with { ApiKey = "rejected", Settings = new() { ["apiKey"] = "forbidden" } },
                input with { ApiKey = "rejected", Settings = null },
                input with { ApiKey = "bad\r\nheader" }
            })
            {
                var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Length;
                Reject(() => service.AddApi(state.Revision, invalid), "Invalid connection input accepted.");
                Check(repository.Snapshot.Revision == state.Revision && secrets.ReadAccount(account.Id) == before && files == Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Length, "Invalid input leaves public and private state untouched.");
            }
            state = service.AddApi(state.Revision, input with { Name = "Ignored replacement name", SelectedModelRouteIds = ["model-a", "model-b"] });
            Check(state.Revision == 2 && state.Accounts.Single().Id == account.Id && state.Accounts.Single().Name == "Own name", "Repeated credential retains account identity and custom name.");
            Check(state.Routes.Length == 2 && state.Routes.All(r => r.AccountIds.SequenceEqual(new[] { account.Id }) && r.ProviderRouteId is "model-a" or "model-b"), "Explicit model selection creates only chosen routes in one revision.");
            Check(state.AppliedRevision == 0 && state.Profiles.Length == 0, "Model selection never applies a profile.");
            var ids = state.Routes.Select(r => r.Id).ToArray();
            state = service.AddApi(state.Revision, input with { SelectedModelRouteIds = ["model-a"] });
            Check(state.Routes.Select(r => r.Id).SequenceEqual(ids), "Repeated selection preserves existing route IDs and aliases.");
            state = service.AddApi(state.Revision, input with { ApiKey = "synthetic-second-key", SelectedModelRouteIds = ["model-a", "model-b"] });
            Check(state.Accounts.Length == 2 && state.Routes.Length == 4 && state.Routes.Select(r => r.Alias).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 4, "Distinct accounts receive distinct route IDs and aliases.");
            before = secrets.ReadAccount(account.Id);
            try { service.ReplaceApiCredential(0, account.Id, "synthetic-replacement"); throw new Exception("Expected revision conflict."); }
            catch (RevisionConflictException) { count++; }
            Check(secrets.ReadAccount(account.Id) == before, "Revision conflict cannot replace private key.");
            state = service.ReplaceApiCredential(state.Revision, account.Id, "synthetic-replacement");
            Check(state.Accounts.Any(a => a.Id == account.Id) && state.Routes.Take(2).Select(r => r.Id).SequenceEqual(ids), "Credential replacement preserves stable model/account identity.");
            var legacy = input with { ApiKey = "synthetic-legacy-key", ProviderConnectionId = null, ProviderRouteId = "model-a" };
            state = service.AddApi(state.Revision, legacy);
            Check(state.Accounts.Length == 3 && state.Routes.Length == 4, "Legacy model setup remains account-only.");
            Reject(() => service.AddApi(state.Revision, legacy), "Duplicate legacy route accepted.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        return count;
    }
}
