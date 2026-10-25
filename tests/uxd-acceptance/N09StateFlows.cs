using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Lain.Core;

internal sealed partial class Acceptance
{
    // Fixture changes below represent Host responses, never production-field injection.
    // Every user action goes through the existing XTEST helpers; BringIntoView is
    // the existing harness positioning aid, not a claim of native scroll coverage.
    private async Task NativeN09ModelIdentityFlow()
    {
        using var input = new NativeInput(); input.Focus(window);
        var original = fixture.Config;
        fixture.Config = original with
        {
            Routes = original.Routes.Reverse().Select(route => route.Id == "r2"
                ? route with { Policy = new RoutingPolicyDto(FallbackRouteIds: ["r1"]) } : route).ToArray(),
            Profiles = original.Profiles.Append(new ProfileDto("n09-fallback", "N09 fallback", ["r2"], "r2",
                new RoutingPolicyDto(FallbackRouteIds: ["r1"]))).ToArray()
        };
        await NativePress(input, Need<Button>("Reload status"));
        await Wait(() => Need<Button>("Reload status").IsEnabled);
        await NativeGo(input, "Models");
        CheckN09PhysicalModelOrder("reversed-host-array");

        // A normal native edit changes physical alphabetical position, while the
        // revisioned request and all saved references continue to use r1.
        await NativePress(input, Need<Button>("Edit model access gpt-lain-01"));
        var alias = Controls<TextBox>().Single(value => value.Watermark == "client-facing-model");
        await NativeText(input, alias, "zz-n09-stable-model");
        var savedRevision = fixture.Config.Revision;
        await NativePress(input, Need<Button>("Save model access"));
        await Wait(() => fixture.Config.Revision == savedRevision + 1 && Find<Border>("Model row r1") is not null);
        Check(fixture.Config.Routes.Single(route => route.Id == "r1").Alias == "zz-n09-stable-model"
            && fixture.Config.Profiles.Take(3).All(profile => profile.RouteIds.Contains("r1")),
            "native rename changes label and position without retargeting saved profile references");
        var save = N09RequestBody("PUT", "/api/v1/routes/r1");
        Check(save.GetProperty("revision").GetInt64() == savedRevision
            && save.GetProperty("value").GetProperty("id").GetString() == "r1",
            "native rename submits exact stable ID and captured configuration revision");
        CheckN09PhysicalModelOrder("renamed-r1-last");
        var search = Need<TextBox>("Search configured model access");
        await NativeText(input, search, "zz-n09-stable-model");
        await NativePress(input, Need<Button>("Select model access zz-n09-stable-model"));
        Check(Find<Button>("Edit selected model access zz-n09-stable-model") is not null,
            "native selection after physical reorder resolves the renamed r1 details");
        Capture(FlowImage("reordered-selected-r1"));
        await NativePress(input, Need<Button>("Close model details zz-n09-stable-model"));

        var more = Need<Button>("Actions for model access zz-n09-stable-model");
        await NativePress(input, more);
        await N09MenuPress(input, more.ContextMenu!, "Remove model access…");
        Check(!Need<Button>("Remove model access").IsEnabled,
            "native removal review locks a model with saved references");
        var referenceText = string.Join("\n", Controls<TextBlock>().Select(value => value.Text));
        Check(new[] { "Profile member: Daily", "Profile member: Workspace 2", "Profile member: Workspace 3",
            "Profile fallback: N09 fallback", "Fallback from model: gpt-lain-02" }.All(referenceText.Contains),
            "native removal review names memberships, profile fallback and model fallback separately");
        Capture(FlowImage("reference-lock"));
        await NativePress(input, Need<Button>("Cancel"));

        // Open the actual menu while a user-requested reload is held in the
        // in-memory transport. Production retains this popup while accepting the
        // newer snapshot, so the subsequent OS click exercises the frozen guard.
        var held = fixture.Defer("GET", "/api/v1/configuration");
        await NativePress(input, Need<Button>("Reload status"));
        await Wait(() => held.Entered.Task.IsCompleted);
        more = Need<Button>("Actions for model access zz-n09-stable-model");
        await NativePress(input, more);
        await Wait(() => more.ContextMenu?.IsOpen == true);
        var before = Mutations;
        fixture.Config = fixture.Config with { Revision = fixture.Config.Revision + 1, Routes = fixture.Config.Routes.Reverse().ToArray() };
        held.Complete(fixture.Config);
        await Wait(() => Need<Button>("Reload status").IsEnabled);
        Check(more.ContextMenu!.IsOpen, "snapshot update leaves the captured native menu mounted");
        await N09MenuPress(input, more.ContextMenu!, "Disable at next Apply");
        Check(Mutations == before && fixture.Config.Routes.Single(route => route.Id == "r1").Enabled,
            "native click on revision-stale menu sends no PUT and leaves r1 enabled");
        Check(Controls<TextBlock>().Any(value => value.Text?.Contains("changed after these actions were opened") == true),
            "stale native action explains that actions must be reopened");
        Capture(FlowImage("stale-menu-blocked"));

        await NativePress(input, Need<Button>("Reload status"));
        await Wait(() => Need<Button>("Reload status").IsEnabled);
        await NativePress(input, Need<Button>("Edit model access zz-n09-stable-model"));
        alias = Controls<TextBox>().Single(value => value.Watermark == "client-facing-model");
        await NativeText(input, alias, "n09-kept-conflict-draft");
        var conflict = fixture.Defer("PUT", "/api/v1/routes/r1");
        await NativePress(input, Need<Button>("Save model access"));
        await Wait(() => conflict.Entered.Task.IsCompleted);
        conflict.Response.TrySetResult(new HttpResponseMessage(HttpStatusCode.Conflict)
            { Content = JsonContent.Create(new ApiError("revision_conflict", "Synthetic competing writer.")) });
        await Wait(() => Need<Button>("Save model access").IsEffectivelyEnabled);
        Check(ReferenceEquals(alias, Controls<TextBox>().Single(value => value.Watermark == "client-facing-model"))
            && alias.Text == "n09-kept-conflict-draft", "native Save HTTP409 retains identical mounted draft input");
        Capture(FlowImage("conflict-draft-retained"));

        var removed = fixture.Config.Routes.Single(route => route.Id == "r1");
        fixture.Config = fixture.Config with { Revision = fixture.Config.Revision + 1,
            Routes = fixture.Config.Routes.Where(route => route.Id != "r1").Append(removed with { Id = "n09-same-alias-replacement" }).ToArray() };
        await NativePress(input, Need<Button>("Reload status"));
        await Wait(() => Need<Button>("Reload status").IsEnabled);
        before = Mutations;
        await NativePress(input, Need<Button>("Save model access"));
        Check(Mutations == before && alias.Text == "n09-kept-conflict-draft"
            && fixture.Config.Routes.Single(route => route.Id == "n09-same-alias-replacement").Alias == removed.Alias,
            "native Save after removed ID preserves draft and never targets same-alias replacement");
        Check(Find<Button>("Review latest configuration") is not null,
            "removed-ID draft retains explicit latest-configuration review action");
        Capture(FlowImage("removed-id-blocked"));
        Check(fixture.Requests.Where(value => !value.StartsWith("GET ", StringComparison.Ordinal))
            .SequenceEqual(new[] { "PUT /api/v1/routes/r1", "PUT /api/v1/routes/r1" })
            && fixture.ApplyCalls == 0 && fixture.Config.AppliedRevision == original.AppliedRevision,
            "only explicit rename and rejected r1 Save reach mutation transport; no removal, Apply or inference");
    }

    private void CheckN09PhysicalModelOrder(string stage)
    {
        var rows = Controls<Border>().Where(value => AutomationProperties.GetName(value)?.StartsWith("Model row ") == true)
            .OrderBy(value => Bounds(value).Top).ToArray();
        var expected = fixture.Config.Routes.OrderBy(value => value.Alias, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.Id, StringComparer.Ordinal).Select(value => "Model row " + value.Id).ToArray();
        Check(rows.Select(AutomationProperties.GetName).SequenceEqual(expected),
            "physical model row positions follow alias/ID order: " + stage);
        Check(rows.Zip(rows.Skip(1), (a, b) => Bounds(a).Bottom <= Bounds(b).Top + 1).All(value => value),
            "physical model rows do not overlap: " + stage);
        geometry.Add(new { currentCase, stage, revision = fixture.Config.Revision,
            physicalRows = rows.Select(value => new { name = AutomationProperties.GetName(value), bounds = Bounds(value).ToString() }).ToArray() });
    }

    private async Task N09MenuPress(NativeInput input, ContextMenu menu, string header)
    {
        await Wait(() => menu.IsOpen);
        var item = menu.Items.OfType<MenuItem>().Single(value => Equals(value.Header, header));
        Check(item.IsEffectivelyEnabled && item.Bounds.Width > 0 && item.Bounds.Height > 0, "native menu target is enabled and mounted: " + header);
        input.Click(item); await Task.Delay(160);
    }

    private JsonElement N09RequestBody(string method, string path)
    {
        var record = fixture.RequestDetails.Select(value => JsonSerializer.SerializeToElement(value))
            .Last(value => value.GetProperty("method").GetString() == method && value.GetProperty("path").GetString() == path);
        using var body = JsonDocument.Parse(record.GetProperty("body").GetString()!);
        return body.RootElement.Clone();
    }

    // Separate regression so the confirmed hidden-input defect cannot prevent
    // independent Cancel/target-change evidence from being collected.
    private async Task NativeN09PreparedReviewFlow()
    {
        using var input = new NativeInput(); input.Focus(window);
        await NativeGo(input, "Profiles");
        await NativePress(input, Need<Button>("Select profile Daily"));
        await NativePress(input, Need<Button>("Set up Codex client…"));
        var parent = Path.Combine(output, currentCase + "-parent");
        Directory.CreateDirectory(parent);
        var executable = Path.Combine(output, currentCase + "-synthetic-lain");
        await File.WriteAllTextAsync(executable, "#!/bin/sh\nexit 99\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        await NativeText(input, Need<TextBox>("lain command-line executable"), executable);
        await NativeText(input, Need<TextBox>("Destination parent folder"), parent);
        await NativePress(input, Need<Button>("Prepare Codex files"));
        await Wait(() => Find<TextBlock>("Prepared Codex artifact") is not null);
        fixture.Config = fixture.Config with { Revision = fixture.Config.Revision + 1 };
        await NativePress(input, Need<Button>("Reload status"));
        await Wait(() => Need<Button>("Reload status").IsEnabled);
        Check(!Need<Button>("Install client files").IsEnabled && Find<Button>("Review latest configuration") is not null,
            "newly observed revision disables old prepared installation and offers review");
        Capture(FlowImage("stale-prepared"));
        await NativePress(input, Need<Button>("Review latest configuration"));
        Check(Need<TextBox>("lain command-line executable").Text == executable
            && Need<TextBox>("Destination parent folder").Text == parent
            && Need<ComboBox>("Installation target").SelectedIndex == 0
            && Need<CheckBox>("Include Codex model groups").IsChecked != true,
            "native Review latest from Prepared preserves hidden setup inputs rather than restoring defaults");
        Check(Find<TextBlock>("Prepared Codex artifact") is null && fixture.ExportCalls == 1
            && Directory.GetFileSystemEntries(parent).Length == 0 && fixture.ApplyCalls == 0,
            "native Prepared review invalidates old artifacts without export retry, files or Apply");
        Capture(FlowImage("review-restored-inputs"));
        await NativePress(input, Need<Button>("Cancel"));
    }

    private async Task NativeN09PreparedCancelTargetFlow()
    {
        using var input = new NativeInput(); input.Focus(window);
        var initial = JsonSerializer.Serialize(fixture.Config);
        // Keep an unrelated actual editor so Cancel/target changes must not
        // silently clear another section's unsaved input.
        await NativeGo(input, "Models");
        await NativePress(input, Need<Button>("Edit model access gpt-lain-01"));
        var draft = Need<CheckBox>("Model access enabled");
        await NativePress(input, draft);
        await NativeGo(input, "Profiles");
        await NativePress(input, Need<Button>("Select profile Daily"));
        await NativePress(input, Need<Button>("Set up Codex client…"));
        var scratch = Path.Combine(output, currentCase + "-targets");
        var parentA = Path.Combine(scratch, "parent-a"); var parentB = Path.Combine(scratch, "parent-b");
        Directory.CreateDirectory(parentA); Directory.CreateDirectory(parentB);
        var executable = Path.Combine(scratch, "synthetic-lain");
        await File.WriteAllTextAsync(executable, "#!/bin/sh\nexit 99\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var executableField = Need<TextBox>("lain command-line executable");
        var folder = Need<TextBox>("Destination parent folder");
        await NativeText(input, executableField, executable);
        await NativeText(input, folder, parentA);
        Check(Need<CheckBox>("Include Codex model groups").IsChecked != true, "native default private setup has explicit groups-off choice");
        await NativePress(input, Need<Button>("Prepare Codex files"));
        await Wait(() => Find<TextBlock>("Prepared Codex artifact") is not null);
        Check(N09RequestBody("POST", "/api/v1/profiles/p1/codex/export").GetProperty("enableLainModelPicker").GetBoolean() == false,
            "native Prepare sends explicit false group flag");
        var firstCatalog = N09RequestBody("POST", "/api/v1/profiles/p1/codex/export").GetProperty("catalogPath").GetString()!;
        Check(firstCatalog.StartsWith(parentA + Path.DirectorySeparatorChar, StringComparison.Ordinal), "first prepared target belongs to parent A");
        CheckPreparedFileFree("first-prepared");
        await NativePress(input, Need<Button>("Change prepared Codex target"));
        Check(ReferenceEquals(folder, Need<TextBox>("Destination parent folder")) && folder.Text == parentA
            && ReferenceEquals(executableField, Need<TextBox>("lain command-line executable")),
            "native Change setup restores exact input controls and destination A");
        var target = Need<ComboBox>("Installation target");
        await NativePress(input, target); input.Key(0xff57); input.Key(0xff0d); await Task.Delay(120);
        Check(target.SelectedIndex == 1 && Find<TextBox>("Destination parent folder") is null && fixture.ExportCalls == 1,
            "native shared-target choice hides parent and causes no export or installation");
        await NativePress(input, target); input.Key(0xff50); input.Key(0xff0d); await Task.Delay(120);
        Check(target.SelectedIndex == 0 && ReferenceEquals(folder, Need<TextBox>("Destination parent folder")),
            "native private-target return retains the same parent input");
        await NativeText(input, folder, parentB);
        await NativePress(input, Need<Button>("Prepare Codex files"));
        await Wait(() => Find<TextBlock>("Prepared Codex artifact") is not null && fixture.ExportCalls == 2);
        var secondCatalog = N09RequestBody("POST", "/api/v1/profiles/p1/codex/export").GetProperty("catalogPath").GetString()!;
        Check(secondCatalog != firstCatalog && secondCatalog.StartsWith(parentB + Path.DirectorySeparatorChar, StringComparison.Ordinal),
            "native second Prepare freezes a fresh target under parent B");
        CheckPreparedFileFree("changed-target-prepared");
        // Reach Cancel only through native focus traversal, then verify the
        // reverse step and Enter activation against the mounted prepared view.
        var cancel = Need<Button>("Cancel");
        cancel.BringIntoView(); await Task.Delay(60);
        for (var attempt = 0; attempt < 40 && !cancel.IsFocused; attempt++)
        { input.Key(0xff09); await Task.Delay(35); }
        Check(cancel.IsFocused, "native Tab reaches Prepared Cancel");
        input.Key(0xff09, 0xffe1); await Task.Delay(60);
        Check(!cancel.IsFocused, "native Shift+Tab leaves Prepared Cancel");
        input.Key(0xff09); await Task.Delay(60);
        Check(cancel.IsFocused, "native Tab returns to Prepared Cancel");
        input.Key(0xff0d); await Task.Delay(120);
        await Wait(() => Find<TextBlock>("Prepared Codex artifact") is null);
        Check(Directory.GetFileSystemEntries(parentA).Length == 0 && Directory.GetFileSystemEntries(parentB).Length == 0,
            "native Prepared Cancel writes no files to either target");
        Check(fixture.ApplyCalls == 0 && fixture.ActiveProfileId == "p2" && JsonSerializer.Serialize(fixture.Config) == initial,
            "native Prepare, target changes and Cancel preserve configuration and applied Workspace 2");
        await NativePress(input, Need<Button>("Return to Models · gpt-lain-01 draft")); page = "Models";
        Check(ReferenceEquals(draft, Need<CheckBox>("Model access enabled")) && draft.IsChecked == false,
            "native Prepared Cancel preserves the exact unrelated Models draft");
        Check(fixture.Requests.Where(value => !value.StartsWith("GET ", StringComparison.Ordinal))
            .All(value => value == "POST /api/v1/profiles/p1/codex/export"), "only deliberate pure exports reach mutation transport");
        Capture(FlowImage("cancelled-retained-model-draft"));

        void CheckPreparedFileFree(string stage)
        {
            Check(Need<TextBlock>("Codex setup status").Text == "No files installed yet"
                && Directory.GetFileSystemEntries(parentA).Length == 0 && Directory.GetFileSystemEntries(parentB).Length == 0,
                "native preparation remains file-free: " + stage);
            Capture(FlowImage(stage));
        }
    }
}
