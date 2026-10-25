using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Lain.Core;
using Lain.Desktop;

internal sealed partial class Scenario
{
    // Run on a freshly initialized scenario, before creating another section draft.
    // These are synthetic production-command checks; native menu/input evidence is
    // supplied by the acceptance owner's separate input matrix.
    internal async Task RunUxd19ModelCases()
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(MainWindow);
        var configuration = type.GetField("_configuration", instance)!;
        var selected = type.GetField("_selectedModelRouteId", instance)!;
        var section = type.GetField("_section", instance)!;
        var editing = type.GetField("_editing", instance)!;
        var original = (ConfigurationDto)configuration.GetValue(window)!;
        var fixtureOriginal = fixture.Config;
        var originalSection = section.GetValue(window);
        section.SetValue(window, "Models");
        var restoreNavigation = (Action)type.GetMethod("CaptureModelsNavigation", instance)!.Invoke(window, null)!;
        section.SetValue(window, originalSection);
        var pending = (Dictionary<string, long>)type.GetField("_modelPendingRevisions", instance)!.GetValue(window)!;
        var originalPending = pending.ToArray();
        object? Invoke(string name, params object?[] args) => type.GetMethod(name, instance)!.Invoke(window, args);
        Array Commands(ConfigurationDto cfg, RouteDto route, Action detail) => (Array)Invoke("ModelAccessCommands", cfg, route, detail)!;
        void Execute(Array commands, string key)
        {
            var command = commands.Cast<object>().Single(value => Equals(value.GetType().GetProperty("Key")!.GetValue(value), key));
            ((Action)command.GetType().GetProperty("Execute")!.GetValue(command)!)();
        }
        var route = original.Routes.Single(r => r.Id == "r1");
        var other = original.Routes.First(r => r.Id != route.Id);
        try
        {
            section.SetValue(window, "Models"); editing.SetValue(window, false);
            fixture.Config = original;
            Invoke("Render");
            await Task.Delay(80);

            var calls = 0;
            var commands = Commands(original, route, () => calls++);
            selected.SetValue(window, other.Id);
            configuration.SetValue(window, original with { Routes = original.Routes.Reverse().ToArray() });
            Execute(commands, "detail");
            Check(calls == 1, "UXD19 frozen model action retains its route after selection and ordering change");

            configuration.SetValue(window, original with { Revision = original.Revision + 1 });
            var before = fixture.Requests.Count;
            Execute(commands, "enabled"); Execute(commands, "visibility"); Execute(commands, "edit"); Execute(commands, "detail");
            Check(calls == 1 && fixture.Requests.Skip(before).All(request => !request.StartsWith("PUT ", StringComparison.Ordinal)),
                "UXD19 changed revision blocks old commands before mutation or detail execution");
            Check(!(bool)editing.GetValue(window)!, "UXD19 stale edit cannot open an editor from an old menu snapshot");

            var replacement = route with { Id = "uxd19-replacement-same-alias" };
            var missing = original with { Revision = original.Revision + 1,
                Routes = original.Routes.Where(r => r.Id != route.Id).Append(replacement).ToArray() };
            configuration.SetValue(window, missing);
            Execute(commands, "detail"); Execute(commands, "edit");
            Check(calls == 1 && !(bool)editing.GetValue(window)!,
                "UXD19 removed stable ID never retargets same-alias replacement");

            var references = type.GetMethod("ModelRouteReferences", BindingFlags.Static | BindingFlags.NonPublic)!;
            var referenced = original with
            {
                Profiles = original.Profiles.Select(p => p with { DefaultRouteId = other.Id }).Append(
                    new ProfileDto("uxd19-fallback-profile", "Synthetic fallback profile", [other.Id], other.Id,
                        new RoutingPolicyDto(FallbackRouteIds: [route.Id]))).ToArray(),
                Routes = original.Routes.Select(r => r.Id == other.Id
                    ? r with { Policy = new RoutingPolicyDto(FallbackRouteIds: [route.Id]) } : r).ToArray()
            };
            var uses = (string[])references.Invoke(null, [referenced, route.Id])!;
            Check(new[] { "Daily", "Workspace 2", "Workspace 3" }.All(name => uses.Contains("Profile member: " + name)),
                "UXD19 default change retains all three named profile memberships");
            Check(uses.Contains("Profile fallback: Synthetic fallback profile") && uses.Contains("Fallback from model: " + other.Alias),
                "UXD19 reference lock distinguishes profile fallback and model fallback");
            configuration.SetValue(window, referenced);
            Invoke("ShowModelRemoval", referenced, route);
            await Task.Delay(80);
            Check(!Controls<Button>().Single(b => Equals(b.Content, "Remove model access")).IsEnabled,
                "UXD19 named saved references disable removal");
            await Click("Cancel");

            configuration.SetValue(window, original); fixture.Config = original;
            selected.SetValue(window, null);
            type.GetField("_modelNarrowDetail", instance)!.SetValue(window, false);
            editing.SetValue(window, false); Invoke("Render");
            await Wait(() => Controls<TextBox>().Any(input => AutomationProperties.GetName(input) == "Search configured model access"
                && input.Bounds.Width > 0), "UXD19 model inventory search is mounted after rebuilding the viewport");
            Named<TextBox>("Search configured model access").Text = "no-match-demo";
            await Task.Delay(80);
            Check(HasText("Search query: no-match-demo") && Controls<Button>().Any(b => Equals(b.Content, "Clear filters")),
                "UXD19 zero filter matches retain the query and clear-filters recovery");
            await Click("Clear filters");
            Check(Named<TextBox>("Search configured model access").Text == "", "UXD19 clear filters restores an empty query");

            // Actual editor Save is exercised after the stable ID vanishes. The
            // mounted inputs must survive and no route PUT may reach the fixture.
            Invoke("EditRoute", route, null);
            await Task.Delay(80);
            Input("client-facing-model").Text = "uxd19-kept-missing-draft";
            configuration.SetValue(window, missing); fixture.Config = missing;
            before = fixture.Requests.Count;
            await Click("Save model access");
            Check(Input("client-facing-model").Text == "uxd19-kept-missing-draft"
                && fixture.Requests.Skip(before).All(request => !request.StartsWith("PUT /api/v1/routes/", StringComparison.Ordinal))
                && !fixture.Config.Routes.Any(r => r.Id == route.Id),
                "UXD19 missing-ID Save keeps draft and sends no create/update for the missing route or replacement");
            await Click("Cancel");

            // The fixture's revision validator and exact path verify both the ID
            // and revision of real enabled/visible command saves.
            configuration.SetValue(window, original); fixture.Config = original;
            editing.SetValue(window, false); Invoke("Render");
            before = fixture.Requests.Count;
            Execute(Commands(original, route, () => { }), "enabled");
            await Wait(() => fixture.Config.Revision == original.Revision + 1 && !(bool)type.GetField("_busy", instance)!.GetValue(window)!
                && !(bool)type.GetField("_refreshing", instance)!.GetValue(window)!,
                "UXD19 enabled command completes its revision-bound save");
            var afterEnable = fixture.Config;
            var enabledRoute = afterEnable.Routes.Single(r => r.Id == route.Id);
            Check(enabledRoute.Enabled != route.Enabled && enabledRoute.Visible == route.Visible
                && fixture.Requests.Skip(before).Contains("PUT /api/v1/routes/" + route.Id),
                "UXD19 enabled save targets exactly the fixed route without changing menu visibility");
            Execute(Commands(afterEnable, enabledRoute, () => { }), "visibility");
            await Wait(() => fixture.Config.Revision == afterEnable.Revision + 1 && !(bool)type.GetField("_busy", instance)!.GetValue(window)!
                && !(bool)type.GetField("_refreshing", instance)!.GetValue(window)!,
                "UXD19 visibility command completes its revision-bound save");
            var hiddenRoute = fixture.Config.Routes.Single(r => r.Id == route.Id);
            Check(hiddenRoute.Visible != route.Visible && hiddenRoute.Enabled == enabledRoute.Enabled
                && fixture.Config.AppliedRevision == original.AppliedRevision
                && !fixture.Requests.Skip(before).Any(request => request.Contains("/apply", StringComparison.Ordinal)
                    || request.Contains("/chat/completions", StringComparison.Ordinal) || request.Contains("/responses", StringComparison.Ordinal)),
                "UXD19 visibility is independent, saved changes remain unapplied, and commands perform no inference");
        }
        finally
        {
            fixture.Config = fixtureOriginal; configuration.SetValue(window, original);
            editing.SetValue(window, false);
            // Clear the temporary mounted model editor before restoring the section.
            section.SetValue(window, "Models"); Invoke("Render");
            pending.Clear(); foreach (var pair in originalPending) pending.Add(pair.Key, pair.Value);
            restoreNavigation(); section.SetValue(window, originalSection); Invoke("Render");
        }
    }
}
