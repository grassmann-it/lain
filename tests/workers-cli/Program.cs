using System.Net;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Lain.Cli;
using Lain.Client;
using Lain.Core;

if (args.Length == 3 && args[0] == "--native-run")
{
    await NativeRunnerCase.RunAsync(args[1], args[2]);
    return;
}
var assertions = 0;
if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Worker subprocess fixtures require Linux.");
void Require(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
void Reject(Action action, string message)
{
    var rejected = false;
    try { action(); } catch (Exception error) when (error is IOException or ArgumentException) { rejected = true; }
    Require(rejected, message);
}
var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "lain-workers-tests", Guid.NewGuid().ToString("N"));
WorkerProjectFiles.PrivateDirectory(root);
var project = Path.Combine(root, "project with spaces"); Directory.CreateDirectory(project);
var calls = 0;
using var client = new LainApiClient(new HttpClient(new Handler(request =>
{
    calls++;
    if (request.RequestUri!.AbsolutePath == "/api/v1/configuration")
        return new(HttpStatusCode.OK) { Content = JsonContent.Create(new ConfigurationDto(7, 7, [], [], [])) };
    Require(request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath == "/api/v1/workers/plan", "typed worker plan endpoint");
    var requestDto = request.Content!.ReadFromJsonAsync<WorkerPlanRequest>().GetAwaiter().GetResult()!;
    Require(requestDto.Revision == 7 && requestDto.Policy.ProfileId == "profile-a", "revision and stable profile identity sent");
    return new(HttpStatusCode.OK) { Content = JsonContent.Create(new WorkerPlan(1, 7, "profile-a", 2,
        requestDto.Tasks.Select(task => new WorkerResolvedTask(task.Id, task.Purpose, task.Prompt, "route-a", "alias-a", "codex", ["account-a"], "low", task.WritePaths ?? [])).ToArray(), [])) };
}))) ;
async Task<(int Exit, string Out, string Error)> Cli(params string[] arguments)
{
    var output = new StringWriter(); var error = new StringWriter();
    var code = await CliApplication.RunAsync(arguments, output, error, () => client);
    return (code, output.ToString(), error.ToString());
}
var setup = await Cli("workers", "init", "--project", project, "--profile", "profile-a", "--json");
Require(setup.Exit == 0 && calls == 0, "initialization works without Host discovery");
var policyPath = WorkerProjectFiles.PolicyPath(project);
var original = File.ReadAllText(policyPath);
Require(WorkerProjectFiles.Read<WorkerProjectPolicy>(policyPath).Purposes.Length == 0, "initial template grants no purpose routes");
Require((await Cli("workers", "init", "--project", project, "--profile", "different")).Exit != 0 && File.ReadAllText(policyPath) == original, "init preserves existing rules");
Require((await Cli("workers", "status", "--project", project, "--json")).Out.Contains("not_started") && calls == 0, "status is local and distinguishes no prior run");
Require((await Cli("workers", "--help")).Exit == 0, "worker help without Host");
var skill = await Cli("workers", "install-skill", "--project", project, "--json");
Require(skill.Exit == 0 && calls == 0 && File.Exists(Path.Combine(project, ".agents", "skills", "lain-workers", "SKILL.md")), "packaged skill installs independently of source checkout and Host");
var installedSkill = Path.Combine(project, ".agents", "skills", "lain-workers", "SKILL.md");
File.AppendAllText(installedSkill, "\nLocal project customization.\n");
Require((await Cli("workers", "install-skill", "--project", project)).Exit != 0 && File.ReadAllText(installedSkill).Contains("Local project customization"), "skill installer preserves customization");
File.WriteAllText(policyPath, "{\"schemaVersion\":1,\"schemaVersion\":2}");
Require((await Cli("workers", "validate", "--project", project)).Exit == 2 && calls == 0, "duplicate policy controls rejected before networking");
File.WriteAllText(policyPath, "{\"schemaVersion\":1,\"unrecognized\":true}");
Require((await Cli("workers", "validate", "--project", project)).Exit == 2 && calls == 0, "unknown policy field rejected before networking");
File.WriteAllText(policyPath, original);
Require((await Cli("workers", "validate", "--project", project, "--json")).Exit == 0 && calls == 2, "validation resolves a no-task Host plan without launching clients");
var tasksFile = Path.Combine(root, "tasks.json");
File.WriteAllText(tasksFile, JsonSerializer.Serialize(new WorkerTaskFile(1, [new("review", "review", "Read the contract.")]), WorkerProjectFiles.Json));
var planned = await Cli("workers", "plan", "--project", project, "--tasks", tasksFile, "--revision", "7", "--json");
Require(planned.Exit == 0 && JsonDocument.Parse(planned.Out).RootElement.GetProperty("tasks")[0].GetProperty("model").GetString() == "alias-a", "JSON plan returns wire alias from Host");
var before = calls;
Require((await Cli("workers", "run", "--project", project, "--tasks", tasksFile, "--client", "relative-codex")).Exit == 2 && calls == before, "invalid client rejected before Host planning");
var duplicateTasks = Path.Combine(root, "duplicate-tasks.json"); File.WriteAllText(duplicateTasks, "{\"schemaVersion\":1,\"tasks\":[],\"tasks\":[]}");
Reject(() => WorkerProjectFiles.ReadTasks(duplicateTasks), "duplicate task file controls rejected");
var linked = Path.Combine(root, "linked-policy.json"); File.CreateSymbolicLink(linked, policyPath);
Reject(() => WorkerProjectFiles.Read<WorkerProjectPolicy>(linked), "symlinked policy file rejected");

var aliasParent = Path.Combine(root, "project alias");
Directory.CreateSymbolicLink(aliasParent, project);
Require(WorkerProjectFiles.ProjectRoot(aliasParent) == WorkerProjectFiles.ProjectRoot(project) && WorkerProjectFiles.RunRoot(aliasParent) == WorkerProjectFiles.RunRoot(project), "symlink project aliases share canonical identity and lock");
var originalLainHome = Environment.GetEnvironmentVariable("LAIN_HOME");
try
{
    var relocated = Path.Combine(root, "relocated lain state");
    Environment.SetEnvironmentVariable("LAIN_HOME", relocated);
    Require(WorkerProjectFiles.RunRoot(project).StartsWith(Path.Combine(relocated, "workers") + Path.DirectorySeparatorChar, StringComparison.Ordinal), "worker runtime respects LAIN_HOME outside project rules");
}
finally { Environment.SetEnvironmentVariable("LAIN_HOME", originalLainHome); }
async Task Git(params string[] arguments)
{
    using var process = new Process { StartInfo = new ProcessStartInfo("git") { WorkingDirectory = project, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true } };
    foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
    process.Start();
    var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync(); await output;
    if (process.ExitCode != 0) throw new Exception(await error);
}
await Git("init", "--quiet"); await Git("add", ".");
await Git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "commit", "--quiet", "-m", "fixture source");
var executable = Path.Combine(root, "fixture codex");
File.WriteAllText(executable, """
#!/usr/bin/env python3
import json, os, pathlib, subprocess, sys, time
if sys.argv[1:] == ['--version']:
    print('codex-cli 0.164.0')
    raise SystemExit(0)
args=sys.argv[1:]
out=pathlib.Path(args[args.index('--output-last-message')+1])
project=pathlib.Path(args[args.index('--cd')+1])
prompt=sys.stdin.read()
trace=project.parent/'trace.jsonl'
def log(event):
    with trace.open('a') as f:
        f.write(json.dumps(dict(event=event,pid=os.getpid(),args=args))+'\n')
log('start')
print(json.dumps(dict(type='thread.started',thread_id='thread-'+str(os.getpid()))),flush=True)
if 'orphan' in prompt:
    child=subprocess.Popen([sys.executable,'-c','import time; time.sleep(90)'])
    (out.parent/'child.pid').write_text(str(child.pid))
    raise SystemExit(0)
if 'slow' in prompt:
    child=subprocess.Popen([sys.executable,'-c','import time; time.sleep(90)'])
    (out.parent/'child.pid').write_text(str(child.pid))
    time.sleep(90)
if 'barrier' in prompt:
    deadline=time.monotonic()+5
    while sum(json.loads(line)['event']=='start' for line in trace.read_text().splitlines())<2:
        if time.monotonic()>deadline: raise SystemExit(9)
        time.sleep(.01)
if 'fail-task' in prompt:
    print(json.dumps(dict(type='turn.failed',error=dict(message='fixture failure'))),flush=True)
    raise SystemExit(4)
out.write_text('fixture result')
if 'incomplete' not in prompt:
    print(json.dumps(dict(type='turn.completed',usage=dict(input_tokens=1,output_tokens=1))),flush=True)
log('finish')
"""
);
File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
var unknown = new ModelCapabilityEvidenceDto("unknown", "unit_fixture");
var compatibility = new ModelCompatibilityDto("route-a", "codex", "upstream-a", null, "codex", "0.164.0", "responses", "responses", "unit_fixture",
    new(unknown, unknown, unknown, unknown, unknown, unknown, new(null, unknown), new(null, unknown), new(null, unknown)), []);
WorkerResolvedTask TaskFor(string id, string prompt, params string[] writes) => new(id, "review", prompt, "route-a", "alias-a", "codex", ["account-a"], "low", writes, compatibility);
WorkerPlan Plan(params WorkerResolvedTask[] tasks) => new(1, 7, "profile-a", 2, tasks, []);
var runRoot = Path.Combine(root, "runs");
var unknownReasoningDenied = false;
try { await WorkerBatchRunner.RunAsync(project, Plan(TaskFor("unknown", "work") with { ReasoningEffort = null }), executable, 5, runRoot, _ => Task.CompletedTask); }
catch (ArgumentException) { unknownReasoningDenied = true; }
Require(unknownReasoningDenied, "unknown reasoning cannot inherit a global default during dispatch");
var unsupportedDenied = false;
try
{
    var unsupported = compatibility with { Capabilities = compatibility.Capabilities with { Functions = new("unsupported", "unit_fixture") } };
    await WorkerBatchRunner.RunAsync(project, Plan(TaskFor("unsupported", "work") with { Compatibility = unsupported }), executable, 5, runRoot, _ => Task.CompletedTask);
}
catch (ArgumentException) { unsupportedDenied = true; }
Require(unsupportedDenied, "known unsupported native worker functions are denied");
var preflights = 0;
Task Preflight(CancellationToken _) { Interlocked.Increment(ref preflights); return Task.CompletedTask; }
var run = await WorkerBatchRunner.RunAsync(project, Plan(TaskFor("one", "barrier"), TaskFor("two", "barrier", "docs/guide.md")), executable, 15, runRoot, Preflight);
Require(run.State == "succeeded" && preflights == 3 && run.Tasks.All(task => task.ThreadId is not null), "two native clients run concurrently and both retain thread IDs");
Require(run.Tasks.All(task => task.ResultReviewState == "not_reviewed"), "successful native turns do not certify task result quality");
Require(run.SourceHead is { Length: 40 } && run.Tasks.All(task => task.VerifiedWorktree is null), "source HEAD retained; fixture output cannot invent a verified native checkout");
Require(run.Tasks.All(task => File.ReadAllText(task.ResultPath!) == "fixture result"), "per-task results retained");
var traces = File.ReadAllLines(Path.Combine(root, "trace.jsonl")).Select(line => JsonDocument.Parse(line)).ToArray();
var starts = traces.Where(document => document.RootElement.GetProperty("event").GetString() == "start").ToArray();
Require(starts.Length == 2, "each task starts exactly once");
var allArgs = starts.Select(document => document.RootElement.GetProperty("args").EnumerateArray().Select(value => value.GetString()!).ToArray()).ToArray();
Require(allArgs.Any(args => args.Contains("read-only")) && allArgs.Any(args => args.Contains("--worktree") && args.Contains("workspace-write")), "write task uses native worktree while read task is read-only");
Require(allArgs.All(args => args.Contains("agents.enabled=false") && args.Contains("features.multi_agent=false") && args.Contains("features.multi_agent_v2=false") && !args.Contains("--dangerously-bypass-approvals-and-sandbox")), "children disable native delegation without bypass flags");
Require(!starts.Any(document => document.RootElement.GetProperty("args").EnumerateArray().Any(arg => arg.GetString()!.Contains("barrier"))), "prompts are not placed on argv");
Require(WorkerBatchRunner.Status(runRoot)!.State == "succeeded", "durable status reports completed batch");
Require((File.GetUnixFileMode(run.Tasks[0].EventsPath!) & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) == 0, "raw events are private");
File.WriteAllText(Path.Combine(root, "trace.jsonl"), "");
var mixed = await WorkerBatchRunner.RunAsync(project, Plan(TaskFor("bad", "fail-task"), TaskFor("good", "independent")), executable, 10, runRoot, Preflight);
Require(mixed.State == "failed" && mixed.Tasks.Single(task => task.Id == "good").State == "succeeded", "failure preserves successful independent task");
Require(File.ReadAllLines(Path.Combine(root, "trace.jsonl")).Count(line => JsonDocument.Parse(line).RootElement.GetProperty("event").GetString() == "start") == 2, "failed task is not retried");
var incomplete = await WorkerBatchRunner.RunAsync(project, Plan(TaskFor("incomplete", "incomplete")), executable, 5, runRoot, Preflight);
Require(incomplete.State == "failed", "exit zero without completed turn does not prove success");
var timeout = await WorkerBatchRunner.RunAsync(project, Plan(TaskFor("timeout", "slow")), executable, 1, runRoot, Preflight);
Require(timeout.Tasks[0].State == "timed_out", "deadline recorded independently of client error");
async Task AssertChildStopped(WorkerRunResult receipt)
{
    var pidFile = Path.Combine(Path.GetDirectoryName(receipt.Tasks[0].EventsPath!)!, "child.pid");
    Require(File.Exists(pidFile), "fixture child was actually started");
    var pid = File.ReadAllText(pidFile);
    bool Active()
    {
        var stat = "/proc/" + pid + "/stat";
        return File.Exists(stat) && File.ReadAllText(stat).Split(')')[1].TrimStart()[0] != 'Z';
    }
    for (var attempt = 0; attempt < 30 && Active(); attempt++) await Task.Delay(50);
    Require(!Active(), "cancellation terminates native client descendants");
}
await AssertChildStopped(timeout);
var orphan = await WorkerBatchRunner.RunAsync(project, Plan(TaskFor("orphan", "orphan")), executable, 1, runRoot, Preflight);
Require(orphan.Tasks[0].State == "timed_out", "exited parent with surviving pipe holder remains bounded by deadline");
await AssertChildStopped(orphan);
using var cancel = new CancellationTokenSource();
var ongoing = WorkerBatchRunner.RunAsync(project, Plan(TaskFor("cancel", "slow")), executable, 30, runRoot, Preflight, cancel.Token);
for (var attempt = 0; attempt < 100; attempt++)
{
    var status = WorkerBatchRunner.Status(runRoot);
    if (status?.State == "running" && File.Exists(Path.Combine(runRoot, status.RunId, "child.pid"))) break;
    await Task.Delay(20);
}
var locked = false;
try { await WorkerBatchRunner.RunAsync(project, Plan(TaskFor("other", "work")), executable, 5, runRoot, Preflight); }
catch (IOException) { locked = true; }
Require(locked, "second batch for same project rejected while first is active");
cancel.Cancel();
var cancelled = await ongoing;
Require(cancelled.State == "cancelled" && cancelled.Tasks[0].State == "cancelled", "batch cancellation persists task outcome");
await AssertChildStopped(cancelled);
WorkerProjectFiles.WritePrivate(Path.Combine(runRoot, "latest.json"), cancelled with { State = "running", FinishedAt = null });
Require(WorkerBatchRunner.Status(runRoot)!.State == "interrupted_unknown", "unlocked incomplete receipt does not imply children stopped");
var blockedStarts = File.ReadAllLines(Path.Combine(root, "trace.jsonl")).Length;
var denied = false;
try { await WorkerBatchRunner.RunAsync(project, Plan(TaskFor("denied", "work")), executable, 5, runRoot, _ => throw new IOException("stale runtime")); }
catch (IOException) { denied = true; }
Require(denied && File.ReadAllLines(Path.Combine(root, "trace.jsonl")).Length == blockedStarts, "stale runtime fails before any task process");
var transportCalls = 0;
var transport = await WorkerBatchRunner.RunAsync(project, Plan(TaskFor("transport", "work")), executable, 5, runRoot,
    _ => Interlocked.Increment(ref transportCalls) == 1 ? Task.CompletedTask : throw new HttpRequestException("offline fixture"));
Require(transport.State == "failed" && transport.Tasks[0].Error == "worker_preflight_or_start_failed", "per-task transport failure finalizes durable status");
var timeoutCalls = 0;
var preflightTimeout = await WorkerBatchRunner.RunAsync(project, Plan(TaskFor("preflight", "work")), executable, 5, runRoot,
    _ => Interlocked.Increment(ref timeoutCalls) == 1 ? Task.CompletedTask : throw new OperationCanceledException());
Require(preflightTimeout.State == "failed" && preflightTimeout.Tasks[0].Error == "worker_preflight_timeout", "per-task preflight timeout finalizes status");
File.WriteAllText(Path.Combine(project, "untracked.txt"), "WIP");
var dirtyDenied = false;
try { await WorkerBatchRunner.RunAsync(project, Plan(TaskFor("write", "work", "docs/file.md")), executable, 5, runRoot, Preflight); }
catch (IOException) { dirtyDenied = true; }
Require(dirtyDenied, "write batch refuses source WIP instead of silently dropping it from native HEAD checkout");
File.Delete(Path.Combine(project, "untracked.txt"));
var recheckCalls = 0;
var rechecked = await WorkerBatchRunner.RunAsync(project, Plan(TaskFor("recheck", "work", "docs/file.md")), executable, 5, runRoot, _ =>
{
    if (Interlocked.Increment(ref recheckCalls) == 2) File.WriteAllText(Path.Combine(project, "new-wip.txt"), "changed after capture");
    return Task.CompletedTask;
});
Require(rechecked.State == "failed" && rechecked.Tasks[0].EventsPath is null, "fresh source changes after capture prevent the write process start");
await GitWorkspaceCases.RunAsync(root, Require);
await FileSafetyCases.RunAsync(root, Require);
await NativeConfigurationCases.RunAsync(root, Require);
Console.WriteLine($"workers-cli: {assertions} assertions passed; private fixture evidence: {root}");

sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(handler(request));
}
