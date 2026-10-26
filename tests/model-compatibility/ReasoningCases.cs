using System.Text.Json;
using Lain.Core;
using Lain.Host.Models;
using Lain.Host.Providers;

internal static class ReasoningCases
{
    public static int Run()
    {
        var checks = 0;
        void Check(bool value, string description) { checks++; if (!value) throw new Exception(description); }
        void Reject(Action action, string fragment)
        {
            checks++;
            try { action(); } catch (ArgumentException error) when (error.Message.Contains(fragment)) { return; }
            throw new Exception("Expected actionable reasoning rejection: " + fragment);
        }
        RouteDto Route(string model, string provider = "codex") => new("route", "arbitrary-alias", model, provider, ["account"]);
        ModelCompatibilityCatalog Native(string model, string protocol = "responses", string source = "compiled:codex-owned") =>
            new(transportBindings: new Dictionary<string, ModelCompatibilityTransportBinding> { ["route"] = new("codex", model, protocol, source) });
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "codex-0.164.0-reasoning.json")));
        foreach (var model in fixture.RootElement.GetProperty("models").EnumerateArray())
        {
            var slug = model.GetProperty("slug").GetString()!;
            var reasoning = Native(slug).ResolveReasoning(Route(slug));
            Check(reasoning.DefaultEffort == model.GetProperty("default_reasoning_level").GetString() &&
                reasoning.SupportedEfforts.SequenceEqual(model.GetProperty("supported_reasoning_levels").EnumerateArray().Select(level => level.GetProperty("effort").GetString())),
                "Exact pinned native reasoning metadata: " + slug);
            Check(reasoning.Evidence is { Qualification: "local_fixture", Scope: "client_model", ClientVersion: "0.164.0" } &&
                reasoning.Evidence.Source == fixture.RootElement.GetProperty("source").GetString(), "Native client facts retain version/source and do not certify provider inference");
        }
        var astra = Native("gpt-6-astra").ResolveReasoning(Route("gpt-6-astra"));
        var sol = Native("gpt-6-sol").ResolveReasoning(Route("gpt-6-sol"));
        var luna = Native("gpt-6-luna").ResolveReasoning(Route("gpt-6-luna"));
        Check(astra.Select() == "low" && sol.Select() == "medium" && luna.Select() == "medium", "Astra, Sol and Luna keep distinct supported client defaults");
        Check(astra.Select("ultra", inherited: true) == "ultra" && sol.Select("ultra") == "ultra" && luna.Select("max") == "max", "Legitimate explicit/inherited choices remain exact client choices");
        Reject(() => astra.Select("none", inherited: true), "Inherited reasoning effort");
        Reject(() => luna.Select("ultra", inherited: true), "Choose one of:");
        Reject(() => astra.Select("LOW"), "not advertised");
        foreach (var unknown in new[] {
            Native("gpt-6-astra").ResolveReasoning(Route("gpt-6-astra", "commandcode")),
            Native("gpt-6-astra").ResolveReasoning(Route("gpt-6-luna")),
            Native("gpt-6-astra", "openai").ResolveReasoning(Route("gpt-6-astra")),
            Native("gpt-6-astra", source: "compiled:import").ResolveReasoning(Route("gpt-6-astra")),
            Native("gpt-6-astra", source: "").ResolveReasoning(Route("gpt-6-astra")),
            Native("gpt-6-astra").ResolveReasoning(Route("gpt-6-astra"), "0.165.0"),
            new ModelCompatibilityCatalog().ResolveReasoning(Route("gpt-6-astra")) })
        {
            Check(unknown.DefaultEffort is null && unknown.SupportedEfforts.Length == 0 && unknown.Evidence.State == "unknown", "Changed provider/model/protocol, missing native provenance and future versions cannot borrow client choices");
            Reject(() => unknown.Select("none", inherited: true), "unqualified");
        }

        var source = new ProviderSourceDto("https://fixture.invalid/reasoning", "Fixture documentation", DateTimeOffset.Parse("2026-10-26T00:00:00Z"));
        ProviderCatalogRegistry Registry(string[] modes, ProviderSourceDto documentation) => new([new("fixture", "Fixture", "Fixture", "https://fixture.invalid/", "https://fixture.invalid/docs", [], [],
            [new("fixture-plan", "Fixture plan", "unknown", "model", "tokens")],
            [new("template", "model", "Model", "https://fixture.invalid/v1", "openai", ["fixture-plan"], new(Reasoning: true, ReasoningModes: modes), Sources: [documentation])], [], [])]);
        var route = new RouteDto("route", "gpt-6-astra", "model", "fixture", ["account"], ProviderRouteId: "template");
        var qualified = new ModelCompatibilityCatalog(Registry(["low", "high"], source), new HashSet<string> { "route" });
        var custom = qualified.ResolveReasoning(route);
        Check(custom.Select() == "low" && custom.Select("high") == "high" && custom.Evidence.Qualification == "documentation", "Documented effort strings are preserved without invented enum conversion");
        Check(new ModelReasoningCompatibility("instant", ["instant"], new("supported", "local_fixture")).Select("instant") == "instant", "Client custom effort strings can remain exact without enum conversion");
        Reject(() => custom.Select("ultra"), "not advertised");
        var noneCapable = new ModelCompatibilityCatalog(Registry(["none", "low", "medium"], source), new HashSet<string> { "route" }).ResolveReasoning(route);
        Check(noneCapable.Select("none", inherited: true) == "none", "A documented none-capable gateway keeps that legitimate choice; native Astra's restriction is not universal");
        Check(new ModelCompatibilityCatalog(Registry(["low", "medium"], source)).ResolveReasoning(route).DefaultEffort is null, "Template identity/alias alone never qualifies reasoning controls");
        Check(new ModelCompatibilityCatalog(Registry(["low"], source with { CheckedAt = null }), new HashSet<string> { "route" }).ResolveReasoning(route).DefaultEffort is null, "Unchecked source cannot qualify reasoning");
        var changed = new ModelCompatibilityCatalog(Registry(["low"], source), new HashSet<string> { "route" },
            new Dictionary<string, ModelCompatibilityTransportBinding> { ["route"] = new("fixture", "model", "responses", "compiled:changed") });
        Check(changed.ResolveReasoning(route).DefaultEffort is null, "Changed compiled protocol cannot borrow documented effort controls");
        foreach (var modes in new[] { new[] { "low", "low" }, new[] { "" }, new[] { "low\n" }, new[] { "always-on" } })
            Check(new ModelCompatibilityCatalog(Registry(modes, source), new HashSet<string> { "route" }).ResolveReasoning(route).DefaultEffort is null, "Malformed reasoning choices remain unqualified");
        var stale = new ModelCompatibilityCatalog(Registry(["low"], source), new HashSet<string> { "route" },
            new Dictionary<string, ModelCompatibilityTransportBinding> { ["route"] = new("fixture", "old-model", "openai", "compiled:stale") });
        Check(stale.ResolveReasoning(route).DefaultEffort is null, "A stale compiled transport cannot fall back to descriptor facts");
        var embedded = new ProviderCatalogRegistry();
        var nvidia = new RouteDto("nvidia", "preserved", "stepfun-ai/step-5-preview", "nvidia-build", ["account"], ProviderRouteId: "nvidia-build:step-5-preview");
        Check(new ModelCompatibilityCatalog(embedded, new HashSet<string> { "nvidia" }).ResolveReasoning(nvidia).DefaultEffort is null, "An always-on state in the real NVIDIA descriptor is not a selectable effort");
        var mercuryProvider = embedded.Find("inception")!;
        var mercury = mercuryProvider.Routes.Single(model => model.Id == "inception-mercury-3");
        var mercuryRoute = new RouteDto("mercury", "custom-mercury", mercury.Model, "inception", ["account"], ProviderRouteId: mercury.Id);
        Check(new ModelCompatibilityCatalog(embedded, new HashSet<string> { "mercury" }).ResolveReasoning(mercuryRoute).DefaultEffort is null,
            "Mercury's provider-wide sources do not substitute for missing checked per-route evidence");
        var mercuryFixture = new ProviderCatalogRegistry([mercuryProvider with { Routes = [mercury with { Sources = [source] }] }]);
        Check(new ModelCompatibilityCatalog(mercuryFixture, new HashSet<string> { "mercury" }).ResolveReasoning(mercuryRoute).Select("instant") == "instant",
            "A Mercury-compatible synthetic route with checked per-route evidence preserves the adapter's instant control");
        return checks;
    }
}
