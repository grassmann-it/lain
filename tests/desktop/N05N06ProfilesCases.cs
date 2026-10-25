using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Lain.Client;
using Lain.Core;
using Lain.Desktop;

internal sealed partial class Scenario
{
    // Production controls + in-memory Host. Dispatcher readiness is injected;
    // its real receipt/native launch is independently qualified by N08/N09.
    public async Task RunN05N06ProfilesCases()
    {
        Directory.CreateDirectory(Output);
        var scratch = Path.Combine(Output, "n05-files"); Directory.CreateDirectory(scratch);
        var executable = Path.Combine(scratch, "lain-fixture");
        await File.WriteAllTextAsync(executable, "#!/bin/sh\nexit 0\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        fixture.Config = fixture.Config with
        {
            Collections = [new("one", "First group", ["r1", "r2"]), new("two", "Overlapping group", ["r2", "r3"])],
            CollectionRouters = [new("provider-router", "auto-provider", "provider:openai", "p1")]
        };
        using var handler = new N05N06ProfilesHandler(fixture);
        var http = new HttpClient(handler, disposeHandler: false) { BaseAddress = new("http://fixture.invalid") };
        transports.Add(http);
        window = new MainWindow(new LainApiClient(http), Path.Combine(Output, "n05-n06-preferences.json")) { Width = 1120, Height = 780 };
        lifetime.MainWindow = window; window.Show();
        await Wait(() => HasText("ready", true), "N05/N06 fixture window ready");
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(MainWindow);
        var configField = type.GetField("_configuration", fields)!;
        object? Invoke(string method, params object?[] args) => type.GetMethod(method, fields)!.Invoke(window, args);
        void Snapshot() => configField.SetValue(window, fixture.Config);
        void InjectDispatcher(string receipt)
        {
            var probe = type.GetField("_codexDispatcherProbe", fields)!;
            var proofType = type.GetNestedType("CodexDispatcherCheck", BindingFlags.NonPublic)!;
            var proof = Activator.CreateInstance(proofType, true, "Synthetic qualified dispatcher", executable, receipt)!;
            var task = typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(proofType).Invoke(null, [proof])!;
            var parameter = Expression.Parameter(typeof(CancellationToken), "ct");
            probe.SetValue(window, Expression.Lambda(probe.FieldType, Expression.Constant(task, task.GetType()), parameter).Compile());
        }
        void Setup()
        {
            Snapshot(); Invoke("ShowCodexExport", fixture.Config.Profiles[0]);
            Named<TextBox>("lain command-line executable").Text = executable;
            Named<TextBox>("Destination parent folder").Text = scratch;
        }
        async Task Press(Control owner, string name)
        {
            var button = owner.GetVisualDescendants().OfType<Button>().First(value => Equals(value.Content, name) && value.IsEnabled);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(180);
        }
        try
        {
            await Click("Profiles"); InjectDispatcher("proof-a"); Setup();
            Named<CheckBox>("Include Codex model groups").IsChecked = true;
            var filesBefore = Directory.GetFileSystemEntries(scratch).Order().ToArray();
            await Click("Prepare Codex files");
            await Wait(() => HasText("No files installed yet"), "N05 grouped preparation completed");
            Check(handler.LastExport?.EnableLainModelPicker == true, "N05 explicit group opt-in reaches Host export");
            Check(Directory.GetFileSystemEntries(scratch).Order().SequenceEqual(filesBefore), "N05 Prepare writes no files");
            Check(HasText("Model groups: 2", true), "N05 prepared scope comes from actual exported groups");
            await Click("Change prepared Codex target");
            Check(Named<CheckBox>("Include Codex model groups").IsChecked == true, "N05 change setup retains explicit group choice");
            await Click("Cancel");
            Check(Directory.GetFileSystemEntries(scratch).Order().SequenceEqual(filesBefore), "N05 prepared Cancel does not install files");

            Setup(); await Click("Prepare Codex files");
            await Wait(() => HasText("No files installed yet"), "N05 private ordinary export prepared");
            Check(handler.LastExport?.EnableLainModelPicker == false, "N05 ordinary export keeps model groups off");
            fixture.Config = fixture.Config with { Revision = fixture.Config.Revision + 1 };
            Snapshot(); Invoke("RefreshProfileReviewState");
            Check(!Controls<Button>().Single(button => Equals(button.Content, "Install client files")).IsEnabled,
                "N05 known revision change disables prepared installation");
            Check(Directory.GetFileSystemEntries(scratch).Order().SequenceEqual(filesBefore), "N05 stale preparation writes no files");
            await Click("Cancel");

            Setup(); handler.RejectExport = true; await Click("Prepare Codex files");
            await Wait(() => HasText("draft is kept", true), "N05 HTTP 409 remains visible in setup draft");
            Check(Named<TextBox>("Destination parent folder").Text == scratch, "N05 revision conflict preserves target inputs");
            handler.RejectExport = false; await Click("Cancel");

            Setup(); Named<CheckBox>("Include Codex model groups").IsChecked = true;
            await Click("Prepare Codex files"); await Wait(() => HasText("No files installed yet"), "N05 receipt-bound setup prepared");
            InjectDispatcher("proof-b"); await Click("Install client files");
            await Wait(() => HasText("prerequisite changed", true), "N05 changed dispatcher receipt prevents installation");
            Check(Directory.GetFileSystemEntries(scratch).Order().SequenceEqual(filesBefore), "N05 changed dispatcher writes no files");
            await Click("Cancel");

            Setup(); await Click("Prepare Codex files"); await Click("Install client files");
            await Wait(() => HasText("Codex files installed"), "N05 explicit private installation completed");
            Check(Directory.GetDirectories(scratch).Length == 1, "N05 explicit Install creates one private target");
            var installedRoot = Directory.GetDirectories(scratch).Single();
            Check(File.Exists(Path.Combine(installedRoot, "lain.config.toml")) && !File.Exists(Path.Combine(installedRoot, "config.toml")),
                "N05 installer writes separate lain files and preserves ordinary config");
            await Click("Back to profiles");

            Snapshot(); Invoke("ShowProfileGroups", "p1"); await Task.Delay(100);
            Check(HasText("multiple exported accounts", true), "N06 provider ambiguity explains explicit account repair");
            await Click("Create group"); Named<TextBox>("Custom model group name").Text = "Research";
            await Click("Add model gpt-lain-01 to group"); await Click("Add model gpt-lain-02 to group");
            // Shared menu/button action reorders identities, never clones routes.
            await Press(Controls<StackPanel>().Single(panel => Equals(panel.Tag, "r2")), "Move up");
            await Click("Save group");
            await Wait(() => fixture.Config.Collections!.Any(value => value.Name == "Research"), "N06 global group saved");
            var research = fixture.Config.Collections!.Single(value => value.Name == "Research");
            Check(research.RouteIds.SequenceEqual(new[] { "r2", "r1" }) && fixture.Config.Collections.Single(value => value.Id == "one").RouteIds.Contains("r2"),
                "N06 overlapping memberships preserve member order and shared route IDs");
            Check(fixture.Config.Routes.Length == Fixture.InitialRoutes, "N06 membership changes do not duplicate routes");
            await Click("Change group order");
            await Press(Controls<StackPanel>().Single(panel => Equals(panel.Tag, research.Id)), "Move group up");
            await Click("Save group order");
            await Wait(() => fixture.Config.Collections![1].Id == research.Id, "N06 global collection permutation persisted");

            await Click("Choose account target…");
            var targets = Named<ComboBox>("Automatic target group or account");
            Check(targets.SelectedItem?.ToString()?.Contains("choose a specific account") == true,
                "N06 repair does not silently preselect one account");
            await Click("Save automatic target");
            Check(handler.RouterWrites == 0, "N06 ambiguous provider cannot be saved as implicit account merge");
            targets.SelectedItem = targets.Items.Cast<object>().Single(value => value.ToString()!.Contains(" · a1", StringComparison.Ordinal));
            await Click("Save automatic target");
            await Wait(() => fixture.Config.CollectionRouters!.Single().GroupId == "account:a1", "N06 explicit account repair saved");
            Check(fixture.Config.CollectionRouters!.Single().Id == "provider-router", "N06 account repair preserves router identity");

            Invoke("EditProfileCollection", "p1", "one");
            Named<TextBox>("Custom model group name").Text = "Unsaved retained name";
            var writes = handler.CollectionWrites;
            fixture.Config = fixture.Config with { Revision = fixture.Config.Revision + 1 }; Snapshot();
            await Click("Save group");
            Check(handler.CollectionWrites == writes && Named<TextBox>("Custom model group name").Text == "Unsaved retained name",
                "N06 stale collection save preserves draft and sends no mutation");
            await Click("Accounts");
            Check(HasText("Unsaved retained name", true) || HasText("draft", true), "N06 navigation retains a groups draft notice");
            Check(fixture.ApplyCalls == 0 && fixture.EnableCalls == 0 && fixture.LoginBegins == 0,
                "N05/N06 actions send no Apply, route enable or login");
            Check(fixture.Unexpected.Count == 0, "N05/N06 no unexpected Host requests");
            await File.WriteAllTextAsync(Path.Combine(Output, "n05-n06-evidence.json"), JsonSerializer.Serialize(new
            {
                result = "PASS", checks, handler.Requests, handler.LastExport,
                scope = "Production controls with routed events and in-memory Host. No OS-input claims, live Host, providers or real dispatcher. Private install writes only runner output. Real dispatcher receipt/native launch remain N08/N09 gates."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { window.Close(); foreach (var transport in transports) transport.Dispose(); }
        Console.WriteLine($"PASS N05/N06 profiles: {checks.Count} checks. {Output}/n05-n06-evidence.json");
    }
}

internal sealed class N05N06ProfilesHandler(Fixture fixture) : DelegatingHandler(fixture)
{
    public readonly List<string> Requests = [];
    public CodexExportRequest? LastExport;
    public bool RejectExport;
    public int CollectionWrites, RouterWrites;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (!(path.StartsWith("/api/v1/collections/", StringComparison.Ordinal) || path.StartsWith("/api/v1/collection-routers/", StringComparison.Ordinal)
            || path.EndsWith("/codex/export", StringComparison.Ordinal))) return await base.SendAsync(request, ct);
        Requests.Add(request.Method + " " + path);
        HttpResponseMessage Result(object value, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = JsonContent.Create(value) };
        HttpResponseMessage Conflict() => Result(new ApiError("revision_conflict", "Synthetic saved revision changed."), HttpStatusCode.Conflict);
        if (path.EndsWith("/codex/export", StringComparison.Ordinal))
        {
            LastExport = await request.Content!.ReadFromJsonAsync<CodexExportRequest>(cancellationToken: ct);
            if (RejectExport || LastExport!.Revision != fixture.Config.Revision) return Conflict();
            var catalog = LastExport.EnableLainModelPicker
                ? "{\"models\":[],\"lain_model_picker\":{\"groups\":[{\"id\":\"one\"},{\"id\":\"two\",\"router_model\":\"auto\"}]}}" : "{\"models\":[]}";
            return Result(new CodexProfileArtifacts("lain.config.toml", "model_catalog_json = \"fixture.json\"\n", "lain.models.json", catalog,
                ["--no-daemon", "--profile", "lain"], fixture.Config.Revision, ProfileId: path.Split('/')[4], GenerationId: "n05-export-" + fixture.Config.Revision));
        }
        if (path == "/api/v1/collections/order")
        {
            var body = (await request.Content!.ReadFromJsonAsync<RevisionRequest<CollectionOrderRequest>>(cancellationToken: ct))!;
            if (body.Revision != fixture.Config.Revision) return Conflict();
            fixture.Config = fixture.Config with { Revision = fixture.Config.Revision + 1,
                Collections = body.Value.CollectionIds.Select(id => fixture.Config.Collections!.Single(value => value.Id == id)).ToArray() };
        }
        else if (path.StartsWith("/api/v1/collections/", StringComparison.Ordinal))
        {
            var body = (await request.Content!.ReadFromJsonAsync<RevisionRequest<ModelCollectionDto>>(cancellationToken: ct))!;
            if (body.Revision != fixture.Config.Revision) return Conflict();
            CollectionWrites++;
            var existing = fixture.Config.Collections ?? [];
            fixture.Config = fixture.Config with { Revision = fixture.Config.Revision + 1,
                Collections = existing.Any(value => value.Id == body.Value.Id) ? existing.Select(value => value.Id == body.Value.Id ? body.Value : value).ToArray() : [..existing, body.Value] };
        }
        else
        {
            var body = (await request.Content!.ReadFromJsonAsync<RevisionRequest<CollectionRouterDto>>(cancellationToken: ct))!;
            if (body.Revision != fixture.Config.Revision) return Conflict();
            RouterWrites++;
            fixture.Config = fixture.Config with { Revision = fixture.Config.Revision + 1,
                CollectionRouters = (fixture.Config.CollectionRouters ?? []).Where(value => value.Id != body.Value.Id).Append(body.Value).ToArray() };
        }
        return Result(fixture.Config);
    }
}
