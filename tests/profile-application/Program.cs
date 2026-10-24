using System.Text.Json;
using Lain.Core;
using Lain.Host.Models;
using Lain.Host.Operations;
using Lain.Cli;
using Artifacts = Lain.Client.CodexProfileArtifacts;

var root = Path.Combine(Path.GetTempPath(), "lain-profile-application-" + Guid.NewGuid().ToString("N"));
var checks = 0;
try
{
    // Upgrade compatibility: the previously applied runtime is independent of pending saves.
    var legacyPaths = new OwnStatePaths(Path.Combine(root, "legacy"));
    var legacyRepository = new ConfigurationRepository(legacyPaths);
    legacyRepository.Update(0, c => c);
    legacyRepository.Update(1, c => c);
    legacyRepository.MarkApplied(2);
    for (var revision = 2; revision < 11; revision++) legacyRepository.Update(revision, c => c);
    var legacyActivePath = Path.Combine(legacyPaths.Root, "active-profile.json");
    var legacyRuntimePath = Path.Combine(legacyPaths.Proxy, "runtime.json");
    var legacyOperationPath = Path.Combine(legacyPaths.Root, "profile-application.json");
    const string legacyActive = "{\"id\":\"legacy\",\"revision\":2}";
    const string legacyRuntime = "{\"valid\":true,\"revision\":2}";
    File.WriteAllText(legacyActivePath, legacyActive);
    File.WriteAllText(legacyRuntimePath, legacyRuntime);
    File.WriteAllText(legacyPaths.ProxyConfig, "PRESERVED LEGACY RUNTIME");
    var legacyConfiguration = File.ReadAllText(legacyPaths.Configuration);
    var legacyService = new ProfileApplicationService(legacyPaths, legacyRepository, new ModelsService(legacyRepository),
        _ => throw new Exception("Legacy validation must not drain"),
        (_, _, _) => throw new Exception("Legacy validation must not apply"),
        _ => throw new Exception("Legacy validation must not stop"));
    Check(legacyService.Status is { State: "legacy-preserved", ProfileId: null, Revision: 2 },
        "unknown legacy profile retains explicit preserved applied revision independently of desired revision11");
    legacyService.RequireCompletedRuntime();
    Check(AppliedProfileStatus.Read(legacyActivePath, legacyRuntimePath, 2).Id is null,
        "permitting legacy startup never invents an active profile identity");
    Check(legacyRepository.Snapshot is { Revision: 11, AppliedRevision: 2 } &&
        File.ReadAllText(legacyPaths.Configuration) == legacyConfiguration &&
        File.ReadAllText(legacyPaths.ProxyConfig) == "PRESERVED LEGACY RUNTIME" &&
        File.ReadAllText(legacyActivePath) == legacyActive && File.ReadAllText(legacyRuntimePath) == legacyRuntime &&
        !File.Exists(legacyOperationPath), "legacy startup validation preserves pending saves and every existing runtime file without publication");
    File.WriteAllText(legacyRuntimePath, "{\"valid\":true,\"revision\":2,\"profileId\":\"legacy\"}");
    Check(legacyService.Status is { State: "legacy-preserved", ProfileId: "legacy" },
        "independently proven legacy profile identity remains available");
    File.WriteAllText(legacyRuntimePath, "{\"valid\":true,\"revision\":2,\"profileId\":null}");
    Check(legacyService.Status is { State: "legacy-preserved", ProfileId: null }, "explicitly unknown legacy identity stays unknown");
    legacyService.RequireCompletedRuntime();
    File.WriteAllText(legacyRuntimePath, legacyRuntime);
    foreach (var invalidRuntime in new[]
    {
        "{\"valid\":false,\"revision\":2}", "{\"valid\":true,\"revision\":11}",
        "{\"valid\":true,\"revision\":2,\"profileId\":\"contradictory\"}",
        "{\"valid\":true,\"revision\":2,\"profileId\":42}",
        "{\"valid\":true,\"revision\":2,\"profileId\":\" \"}",
        "{\"valid\":true,\"revision\":2,\"generationId\":null}",
        "{\"valid\":true,\"revision\":2,\"generationId\":\"new\"}", "null", "{broken"
    })
    {
        File.WriteAllText(legacyRuntimePath, invalidRuntime);
        Check(legacyService.Status.State == "recovery-required", "invalid or generation-bearing runtime cannot enter the legacy compatibility path");
        Reject(legacyService.RequireCompletedRuntime);
    }
    File.WriteAllText(legacyRuntimePath, legacyRuntime);
    foreach (var invalidActive in new[]
    {
        "{\"revision\":11}", "{\"revision\":2,\"id\":42}", "{\"revision\":2,\"id\":\"\"}",
        "{\"revision\":2,\"generationId\":null}",
        "{\"revision\":2,\"generationId\":\"new\"}", "[]"
    })
    {
        File.WriteAllText(legacyActivePath, invalidActive);
        Check(legacyService.Status.State == "recovery-required", "inconsistent or generation-bearing active metadata cannot enter legacy compatibility");
        Reject(legacyService.RequireCompletedRuntime);
    }
    File.WriteAllText(legacyActivePath, legacyActive);
    foreach (var marker in new[] { "{}", "{\"state\":\"draining\"}", "{\"state\":\"completed\",\"id\":\"new\",\"profileId\":\"legacy\",\"revision\":2}" })
    {
        File.WriteAllText(legacyOperationPath, marker);
        Check(legacyService.Status.State == "recovery-required", "any new operation marker blocks fallback to otherwise valid legacy files");
        Reject(legacyService.RequireCompletedRuntime);
    }
    File.Delete(legacyOperationPath);
    Directory.CreateDirectory(legacyOperationPath);
    Reject(legacyService.RequireCompletedRuntime);
    Directory.Delete(legacyOperationPath);
    if (!OperatingSystem.IsWindows())
    {
        File.CreateSymbolicLink(legacyOperationPath, Path.Combine(legacyPaths.Root, "missing-marker-target"));
        Reject(legacyService.RequireCompletedRuntime);
        File.Delete(legacyOperationPath);
    }
    File.Delete(legacyActivePath);
    Reject(legacyService.RequireCompletedRuntime);
    File.WriteAllText(legacyActivePath, legacyActive);
    legacyService.RequireCompletedRuntime();

    var paths = new OwnStatePaths(Path.Combine(root, "state"));
    var repository = new ConfigurationRepository(paths);
    var configuration = repository.Update(0, c => c with
    {
        Accounts = [new("one", "One", "commandcode", true, "api-key", "unverified"), new("two", "Two", "commandcode", true, "api-key", "unverified")],
        Routes = [new("first", "first", "model-1", "commandcode", ["one"]), new("second", "second", "model-2", "commandcode", ["two"])],
        Profiles = [new("profile", "Profile", ["first", "second"], "first"), new("other", "Other", ["second"], "second")]
    });
    var models = new ModelsService(repository);
    var starts = 0; var stops = 0; var running = false; var refuseDrain = false; var uncertainDrain = false;
    TaskCompletionSource? drainEntered = null; TaskCompletionSource? allowDrain = null;
    var service = new ProfileApplicationService(paths, repository, models,
        async ct =>
        {
            drainEntered?.TrySetResult();
            if (allowDrain is not null) await allowDrain.Task.WaitAsync(ct);
            if (refuseDrain) throw new InvalidOperationException("Synthetic drain timeout");
            if (uncertainDrain) throw new ProfileDrainRecoveryRequiredException();
            running = false;
        },
        (runtime, profile, ct) => { starts++; running = true; return Task.CompletedTask; },
        ct => { stops++; running = false; return Task.CompletedTask; });
    var preview = service.Preview("profile", configuration.Revision);
    Check(!preview.BaselineKnown && preview.AddedRouteIds.Length == 2 && preview.AffectedAccountIds.Length == 2, "first preview explicitly has no applied comparison baseline");
    Check(repository.Snapshot.AppliedRevision == 0 && starts == 0, "preview never applies");
    await service.ApplyAsync("profile", configuration.Revision);
    Check(service.Status.State == "completed" && running, "apply publishes a committed running runtime");
    service.RequireCompletedRuntime();
    var active = AppliedProfileStatus.Read(Path.Combine(paths.Root, "active-profile.json"), Path.Combine(paths.Proxy, "runtime.json"), configuration.Revision);
    Check(active.Id == "profile" && active.GenerationId is not null, "exact profile and generation identify the applied runtime");
    preview = service.Preview("profile", configuration.Revision);
    Check(preview.BaselineKnown && preview.AddedRouteIds.Length == 0 && preview.ChangedRouteIds.Length == 0 && !preview.ProfileChanged, "unchanged effective snapshot produces no invented changes");

    configuration = models.PutProfile(configuration.Revision, configuration.Profiles.Single(p => p.Id == "profile") with { DefaultRouteId = "second" });
    preview = service.Preview("profile", configuration.Revision);
    Check(preview.ProfileChanged && preview.DefaultRouteId == "second" && preview.ActiveDefaultRouteId == "first" && preview.ChangedRouteIds.Length == 0,
        "default-only edit has explicit before/after identity even without route changes");
    configuration = models.PutProfile(configuration.Revision, configuration.Profiles.Single(p => p.Id == "profile") with { DefaultRouteId = "first" });
    configuration = models.PutRoute(configuration.Revision, configuration.Routes[1] with { Enabled = false });
    configuration = models.PutRoute(configuration.Revision, configuration.Routes[0] with { Alias = "changed" });
    Check(starts == 1 && repository.Snapshot.AppliedRevision < configuration.Revision, "two route saves remain pending and do not apply unrelated changes");
    preview = service.Preview("profile", configuration.Revision);
    Check(preview.ChangedRouteIds.SequenceEqual(["first"]) && preview.RemovedRouteIds.SequenceEqual(["second"]) && preview.AffectedAccountIds.SequenceEqual(["one", "two"]), "preview includes all pending effective route/account changes");
    refuseDrain = true;
    await RejectAsync(() => service.ApplyAsync("profile", configuration.Revision));
    Check(running && starts == 1 && stops == 0 && service.Status.State == "completed", "drain refusal leaves prior running runtime and transaction proof intact");
    Check(AppliedProfileStatus.Read(Path.Combine(paths.Root, "active-profile.json"), Path.Combine(paths.Proxy, "runtime.json"), repository.Snapshot.AppliedRevision) == active, "drain refusal preserves the exact old generation");
    refuseDrain = false;
    drainEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    allowDrain = new(TaskCreationOptions.RunContinuationsAsynchronously);
    var beforeCanceledDrain = File.ReadAllText(Path.Combine(paths.Proxy, "runtime.json"));
    using (var cancelDrain = new CancellationTokenSource())
    {
        var canceledApply = service.ApplyAsync("profile", configuration.Revision, cancelDrain.Token);
        await drainEntered.Task;
        cancelDrain.Cancel();
        await RejectAsync(() => canceledApply);
    }
    Check(running && starts == 1 && stops == 0 && service.Status.State == "completed" &&
        File.ReadAllText(Path.Combine(paths.Proxy, "runtime.json")) == beforeCanceledDrain,
        "canceling active drain preserves the prior runtime byte-for-byte and does not start or stop it");
    drainEntered = null; allowDrain = null;

    // Block the active metadata publication after the synthetic Core has started.
    File.Delete(Path.Combine(paths.Root, "active-profile.json"));
    Directory.CreateDirectory(Path.Combine(paths.Root, "active-profile.json"));
    await RejectAsync(() => service.ApplyAsync("profile", configuration.Revision));
    Check(!running && stops == 1 && service.Status.State == "recovery-required", "publication write failure stops the new Core and demands recovery");
    Reject(service.RequireCompletedRuntime);
    Directory.Delete(Path.Combine(paths.Root, "active-profile.json"));
    await service.ApplyAsync("profile", configuration.Revision);
    Check(running && service.Status.State == "completed", "explicit reapply repairs failed publication");

    var preservedRuntime = File.ReadAllText(Path.Combine(paths.Proxy, "runtime.json"));
    var preservedActive = File.ReadAllText(Path.Combine(paths.Root, "active-profile.json"));
    var preservedStarts = starts; var preservedStops = stops;
    uncertainDrain = true;
    await RejectAsync(() => service.ApplyAsync("profile", configuration.Revision));
    Check(service.Status.State == "recovery-required" && running && starts == preservedStarts && stops == preservedStops &&
        File.ReadAllText(Path.Combine(paths.Proxy, "runtime.json")) == preservedRuntime &&
        File.ReadAllText(Path.Combine(paths.Root, "active-profile.json")) == preservedActive,
        "unconfirmed reopen retains prior runtime files and active streams without restoring a completed operation marker");
    Reject(service.RequireCompletedRuntime);
    Check(AppliedProfileStatus.Read(Path.Combine(paths.Root, "active-profile.json"), Path.Combine(paths.Proxy, "runtime.json"), configuration.Revision).Id is null,
        "uncertain admission cannot advertise an active profile success");
    uncertainDrain = false;
    await service.ApplyAsync("profile", configuration.Revision);
    Check(service.Status.State == "completed", "explicit apply can recover an uncertain drain without bypassing startup guards");

    var journal = Path.Combine(paths.Root, "profile-application.json");
    var completed = File.ReadAllText(journal);
    File.WriteAllText(journal, completed.Replace("completed", "publishing", StringComparison.Ordinal));
    Check(service.Status.State == "recovery-required", "restart detects interrupted final publication even with matching runtime files");
    Check(AppliedProfileStatus.Read(Path.Combine(paths.Root, "active-profile.json"), Path.Combine(paths.Proxy, "runtime.json"), configuration.Revision).Id is null, "interrupted transaction cannot report active success");
    Reject(service.RequireCompletedRuntime);
    refuseDrain = true;
    await RejectAsync(() => service.ApplyAsync("profile", configuration.Revision));
    Check(service.Status.State == "recovery-required", "refused recovery attempt cannot accidentally bless a previous partial transaction");
    refuseDrain = false;
    await service.ApplyAsync("profile", configuration.Revision);

    configuration = models.PutProfile(configuration.Revision, configuration.Profiles.Single(p => p.Id == "profile") with { Name = "Renamed" });
    preview = service.Preview("profile", configuration.Revision);
    Check(preview.ProfileChanged && preview.ChangedRouteIds.Length == 0, "profile-only edits are visible even with unchanged routes");
    Reject(() => service.Preview("other", configuration.Revision)); // Its default was disabled above.
    Reject(() => service.Preview("profile", configuration.Revision - 1));

    drainEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    allowDrain = new(TaskCreationOptions.RunContinuationsAsynchronously);
    var first = service.ApplyAsync("profile", configuration.Revision);
    await drainEntered.Task;
    Check(service.Status.State == "draining", "ongoing stream drain is visible");
    using var cancellation = new CancellationTokenSource();
    var second = service.ApplyAsync("profile", configuration.Revision, cancellation.Token);
    cancellation.Cancel();
    await RejectAsync(() => second);
    allowDrain.SetResult();
    await first;
    Check(service.Status.State == "completed", "canceling queued apply cannot corrupt the active operation");

    File.Delete(journal);
    Check(service.Status.State == "recovery-required", "missing commit marker cannot authenticate a generated runtime");
    Reject(service.RequireCompletedRuntime);
    await service.ApplyAsync("profile", configuration.Revision);
    File.WriteAllText(Path.Combine(paths.Root, "applied-profile-snapshot.json"), "{\"profile\":null,\"routes\":null}");
    Check(!service.Preview("profile", configuration.Revision).BaselineKnown, "malformed comparison snapshot falls back to unknown without inventing a diff baseline");

    var codex = Path.Combine(root, "codex");
    Directory.CreateDirectory(codex);
    File.WriteAllText(Path.Combine(codex, "config.toml"), "USER CONFIG");
    var exported = new Artifacts("lain.config.toml", "model = \"first\"\nmodel_catalog_json = \"/ignored/lain.models.json\"\n", "lain.models.json", "{\"models\":[{\"slug\":\"first\"}]}", [], 7,
        ProfileId: "profile", GenerationId: "export-generation-one");
    var installed = CodexArtifactInstaller.Install(exported, codex);
    Check(File.ReadAllText(installed.ConfigurationPath).Contains(JsonSerializer.Serialize(installed.CatalogPath)), "stable config references the exact immutable catalog generation");
    Check(File.ReadAllText(Path.Combine(codex, "config.toml")) == "USER CONFIG", "ordinary Codex configuration is preserved");
    Check(CodexArtifactInstaller.Inspect(codex, "profile", 7) is { State: "installed", Stale: false, ProfileId: "profile", Revision: 7 }, "installed identity matches the applied profile");
    Check(CodexArtifactInstaller.Inspect(codex, "other", 7).Stale == true && CodexArtifactInstaller.Inspect(codex, "profile", 8).Stale == true, "same-revision profile switch and changed revision both expose stale exports");
    var oldCatalog = File.ReadAllText(installed.CatalogPath);
    var next = CodexArtifactInstaller.Install(exported with { Revision = 8, Catalog = "{\"models\":[]}" }, codex);
    Check(installed.GenerationId != next.GenerationId && File.ReadAllText(installed.CatalogPath) == oldCatalog, "running clients retain immutable old catalogs across export");
    var privateRoot = Path.Combine(codex, ".lain-artifacts");
    File.WriteAllText(Path.Combine(privateRoot, "pending.json"), "{}");
    Check(CodexArtifactInstaller.Inspect(codex).State == "recovery-required", "interrupted export is never marked installed");
    CodexArtifactInstaller.Install(exported, codex);
    Check(CodexArtifactInstaller.Inspect(codex).State == "installed", "explicit reinstall repairs interrupted export");
    var beforeFailure = File.ReadAllText(Path.Combine(codex, "lain.config.toml"));
    File.Delete(Path.Combine(codex, "lain.models.json"));
    Directory.CreateDirectory(Path.Combine(codex, "lain.models.json"));
    Reject(() => CodexArtifactInstaller.Install(exported with { Revision = 9 }, codex));
    Check(File.ReadAllText(Path.Combine(codex, "lain.config.toml")) == beforeFailure && CodexArtifactInstaller.Inspect(codex).State == "recovery-required", "write failure before config commit preserves the prior usable pair and exposes interruption");
    Directory.Delete(Path.Combine(codex, "lain.models.json"));
    installed = CodexArtifactInstaller.Install(exported, codex);
    File.AppendAllText(installed.CatalogPath, " ");
    Check(CodexArtifactInstaller.Inspect(codex).State == "recovery-required", "catalog content mismatch is detected");
    if (!OperatingSystem.IsWindows())
    {
        File.Delete(Path.Combine(privateRoot, "pending.json"));
        File.CreateSymbolicLink(Path.Combine(privateRoot, "pending.json"), Path.Combine(root, "external"));
        Reject(() => CodexArtifactInstaller.Install(exported, codex));
        Check(!File.Exists(Path.Combine(root, "external")), "symbolic links cannot redirect export writes");
    }
    Console.WriteLine($"Profile application and artifact recovery: {checks} checks passed; synthetic lifecycle only.");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
void Check(bool condition, string description) { checks++; if (!condition) throw new Exception(description); }
void Reject(Action action) { try { action(); } catch (Exception) { checks++; return; } throw new Exception("Expected rejection"); }
async Task RejectAsync(Func<Task> action) { try { await action(); } catch (Exception) { checks++; return; } throw new Exception("Expected rejection"); }
