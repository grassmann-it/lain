using Lain.Client;
using Lain.Core;

namespace Lain.Cli;

public static partial class CliApplication
{
    private static async Task<int> Workers(Options options, TextWriter output, TextWriter error,
        Func<LainApiClient>? clientFactory, CancellationToken ct)
    {
        if (options.Positionals.Count == 0 && options.Has("help") || options.Positionals.SequenceEqual(new[] { "help" }))
        {
            options.RequireOnly("help");
            await WriteHelp(output);
            return 0;
        }
        options.RequirePositionals(1);
        var action = options.Positionals[0];
        if (action is not ("init" or "install-skill" or "validate" or "plan" or "run" or "status"))
            throw new ArgumentException("Use workers init, install-skill, validate, plan, run or status.");
        options.RequireOnly(action switch
        {
            "init" => ["project", "profile", "json"],
            "install-skill" or "status" => ["project", "json"],
            "validate" => ["project", "revision", "json"],
            "run" => ["project", "tasks", "client", "timeout-seconds", "revision", "json"],
            _ => ["project", "tasks", "revision", "json"]
        });
        var project = WorkerProjectFiles.ProjectRoot(options.Get("project") ?? Directory.GetCurrentDirectory());
        if (action == "init")
        {
            var path = WorkerProjectFiles.Initialize(project, options.Get("profile") ?? throw new ArgumentException("--profile is required."));
            await Write(output, new { state = "created", path, nextAction = "Add permitted purpose/route IDs, then run workers validate. No routes are permitted by the empty template." }, options.Json);
            return 0;
        }
        if (action == "install-skill")
        {
            var path = WorkerSkillInstaller.Install(project);
            await Write(output, new { state = "installed", path, nextAction = "Discover the lain-workers skill in the client; start a new session if necessary." }, options.Json);
            return 0;
        }
        if (action == "status")
        {
            var status = WorkerBatchRunner.Status(WorkerProjectFiles.RunRoot(project));
            if (status is null) await Write(output, new { state = "not_started", project }, options.Json);
            else await WriteWorkers(output, status, options.Json);
            return 0;
        }
        // Parse local files before acquiring credentials or contacting the Host.
        var policyPath = WorkerProjectFiles.PolicyPath(project);
        if (!File.Exists(policyPath)) throw new ArgumentException("Missing .lain/workers.json. Run workers init --profile ID first.");
        var policy = WorkerProjectFiles.Read<WorkerProjectPolicy>(policyPath);
        var tasks = action == "validate" ? [] : WorkerProjectFiles.ReadTasks(options.Get("tasks") ?? throw new ArgumentException("--tasks is required."));
        var timeout = options.Get("timeout-seconds") is { } timeoutText ? ParseLong(timeoutText, "timeout-seconds") : 900;
        if (timeout is < 1 or > 3600) throw new ArgumentException("--timeout-seconds must be between 1 and 3600.");
        var executable = options.Get("client");
        if (action == "run" && (executable is null || !Path.IsPathFullyQualified(executable) || !File.Exists(executable)))
            throw new ArgumentException("workers run requires --client /absolute/path/to/codex.");
        using var client = (clientFactory ?? LainApiClient.CreateLocal)();
        var revision = options.Get("revision") is { } text ? ParseLong(text, "revision") : (await client.GetConfigurationAsync(ct)).Revision;
        var plan = await client.PlanWorkersAsync(new WorkerPlanRequest(revision, policy, tasks), ct);
        if (plan.ContractVersion != 1 || plan.Revision != revision || plan.ProfileId != policy.ProfileId ||
            plan.MaxWorkers != policy.MaxWorkers || plan.Tasks.Length != tasks.Length ||
            plan.Tasks.Where((task, index) => task.Id != tasks[index].Id || task.Prompt != tasks[index].Prompt || task.Purpose != tasks[index].Purpose).Any())
            throw new IOException("The Host returned a mismatched worker plan.");
        if (action != "run")
        {
            if (options.Json) await Write(output, plan, true);
            else
            {
                await output.WriteLineAsync($"Worker {(action == "validate" ? "rules validated" : "plan")}: profile {Cell(plan.ProfileId)}, revision {plan.Revision}; {plan.Tasks.Length}/{plan.MaxWorkers} tasks.");
                foreach (var task in plan.Tasks)
                    await output.WriteLineAsync($"  {Cell(task.Id)}: {Cell(task.RouteId)} / {Cell(task.Model)}, reasoning {Cell(task.ReasoningEffort ?? "unknown")}");
                foreach (var note in plan.Notes) await output.WriteLineAsync("  " + Cell(note));
            }
            return 0;
        }
        var codexDirectory = Environment.GetEnvironmentVariable("CODEX_HOME") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        var nativeBinding = WorkerNativeConfiguration.ReadInstalledBinding(codexDirectory, plan.ProfileId, plan.Revision);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ConsoleCancelEventHandler cancel = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            async Task Preflight(CancellationToken token)
            {
                var status = await client.StatusAsync(token);
                if (!status.ProxyReady || status.RuntimeMode != "gateway" || status.Revision != plan.Revision ||
                    status.AppliedRevision != plan.Revision || status.ActiveProfileId != plan.ProfileId || status.ActiveProfileRevision != plan.Revision)
                    throw new IOException("The active Lain profile changed or is unavailable; create a fresh worker plan.");
                var installation = CodexArtifactInstaller.Inspect(codexDirectory, plan.ProfileId, plan.Revision);
                if (installation.State != "installed" || installation.Stale != false || installation.ProfileId != plan.ProfileId || installation.Revision != plan.Revision)
                    throw new IOException("The installed Codex lain profile is missing, stale or changed. Export and install the selected profile before running workers.");
            }
            var result = await WorkerBatchRunner.RunAsync(project, plan, executable!, (int)timeout,
                WorkerProjectFiles.RunRoot(project), Preflight, cancellation.Token, nativeBinding);
            await WriteWorkers(output, result, options.Json);
            return result.State == "succeeded" ? 0 : result.State == "cancelled" ? 3 : 5;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }

    private static async Task WriteWorkers(TextWriter output, WorkerRunResult result, bool json)
    {
        if (json) { await Write(output, result, true); return; }
        await output.WriteLineAsync($"Worker batch {Cell(result.RunId)}: {Cell(result.State)}; profile {Cell(result.ProfileId)}, revision {result.Revision}.");
        foreach (var task in result.Tasks)
        {
            await output.WriteLineAsync($"  {Cell(task.Id)}: {Cell(task.State)}; requested {Cell(task.RequestedModel)}; route {Cell(task.RouteId)}; thread {Cell(task.ThreadId ?? "unknown")}");
            if (task.ResultPath is not null) await output.WriteLineAsync("    Result: " + Cell(task.ResultPath));
            if (task.Error is not null) await output.WriteLineAsync("    Error: " + Cell(task.Error));
        }
    }
}
