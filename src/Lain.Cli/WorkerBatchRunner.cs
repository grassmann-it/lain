using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using Lain.Core;

namespace Lain.Cli;

public sealed record WorkerTaskResult(string Id, string RouteId, string RequestedModel, string[] ExpectedAccountIds,
    string State, string? ThreadId = null, int? ExitCode = null, string? EventsPath = null,
    string? DiagnosticPath = null, string? ResultPath = null, string? Error = null,
    WorkerGitWorktreeIdentity? VerifiedWorktree = null, WorkerNativeConfigurationProof? NativeConfiguration = null,
    string ResultReviewState = "not_reviewed");
public sealed record WorkerRunResult(int Version, string RunId, string Project, string ProfileId, long Revision,
    string State, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, WorkerTaskResult[] Tasks,
    string? SourceHead = null);

/// <summary>One explicitly supplied bounded batch of native clients. No decomposition, retry, route fallback or merge.</summary>
public static class WorkerBatchRunner
{
    public const string ClientVersion = "0.164.0";

    public static async Task<WorkerRunResult> RunAsync(string project, WorkerPlan plan, string clientPath,
        int timeoutSeconds, string runRoot, Func<CancellationToken, Task> preflight, CancellationToken ct = default,
        WorkerNativeBinding? nativeBinding = null)
    {
        project = WorkerProjectFiles.ProjectRoot(project);
        if (!Path.IsPathFullyQualified(clientPath) || !File.Exists(clientPath))
            throw new ArgumentException("--client must name an existing absolute Codex executable.");
        if (timeoutSeconds is < 1 or > 3600 || plan.ContractVersion != 1 || plan.MaxWorkers is < 1 or > 12 ||
            plan.Tasks is null || plan.Tasks.Length == 0 || plan.Tasks.Length > plan.MaxWorkers ||
            plan.Tasks.Select(task => task.Id).Distinct(StringComparer.Ordinal).Count() != plan.Tasks.Length ||
            plan.Tasks.Any(task => task.Id is not { Length: > 0 and <= 100 } || task.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')) ||
                string.IsNullOrWhiteSpace(task.Model) || string.IsNullOrWhiteSpace(task.Prompt) || task.WritePaths is null))
            throw new ArgumentException("The worker plan is not a valid bounded batch.");
        if (plan.Tasks.Any(task => task.ContextMode is not ("inherit" or "native") || task.ContextMode == "native" && nativeBinding is null))
            throw new ArgumentException("Native worker context requires a verified installed binding and public native configuration discovery.");
        if (plan.Tasks.Any(task => task.ReasoningEffort is null))
            throw new ArgumentException("A worker route has unverified reasoning settings; qualify its catalog before execution.");
        foreach (var task in plan.Tasks)
        {
            var compatibility = task.Compatibility;
            if (compatibility is null || compatibility.RouteId != task.RouteId || compatibility.Provider != task.Provider ||
                compatibility.Client != "codex" || compatibility.ClientVersion != ClientVersion)
                throw new ArgumentException("Worker compatibility evidence must match the selected route and client.");
            if (new[] { compatibility.Capabilities.Functions, compatibility.Capabilities.Streaming, compatibility.Capabilities.ToolContinuation }
                .Any(evidence => evidence.State == "unsupported"))
                throw new ArgumentException("The selected route does not support the tools/streaming required by a native worker.");
        }
        await VerifyClient(clientPath, ct);
        WorkerProjectFiles.PrivateDirectory(runRoot);
        using var runLock = AcquireLock(runRoot);
        await preflight(ct);
        var source = plan.Tasks.Any(task => task.WritePaths.Length > 0)
            ? await WorkerGitWorkspace.CaptureCleanSourceAsync(project, ct) : null;
        var id = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(runRoot, id);
        WorkerProjectFiles.PrivateDirectory(directory);
        WorkerProjectFiles.WritePrivate(Path.Combine(directory, "plan.json"), plan);
        var sync = new object();
        var result = new WorkerRunResult(1, id, project, plan.ProfileId, plan.Revision, "running", DateTimeOffset.UtcNow, null,
            plan.Tasks.Select(task => new WorkerTaskResult(task.Id, task.RouteId, task.Model, task.AccountIds, "pending")).ToArray(), source?.Head);
        void Save()
        {
            WorkerProjectFiles.WritePrivate(Path.Combine(directory, "status.json"), result);
            WorkerProjectFiles.WritePrivate(Path.Combine(runRoot, "latest.json"), result);
        }
        void Update(int index, WorkerTaskResult state)
        {
            lock (sync) { result.Tasks[index] = state; Save(); }
        }
        Save();
        var nativeProofs = new WorkerNativeConfigurationProof?[plan.Tasks.Length];
        var nativeOverrides = new string[]?[plan.Tasks.Length];
        if (nativeBinding is not null)
        {
            // Native 0.164 startup initializes shared state/skills. Concurrent
            // first app-server starts can fail before the RPC handshake. Finish
            // these no-turn checks serially before starting parallel worker turns.
            for (var index = 0; index < plan.Tasks.Length; index++)
            {
                var state = result.Tasks[index];
                try
                {
                    ct.ThrowIfCancellationRequested();
                    Update(index, state with { State = "checking_configuration" });
                    var task = plan.Tasks[index];
                    var prepared = await WorkerNativeConfiguration.PrepareAsync(clientPath, project, task, nativeBinding, ct,
                        sourceRepositoryRoot: source?.RepositoryRoot);
                    nativeOverrides[index] = prepared.Overrides;
                    nativeProofs[index] = prepared.Proof;
                    Update(index, state with { NativeConfiguration = nativeProofs[index] });
                }
                catch (OperationCanceledException)
                { Update(index, state with { State = ct.IsCancellationRequested ? "cancelled" : "failed", Error = ct.IsCancellationRequested ? "batch_cancelled" : "worker_preflight_timeout" }); }
                catch (WorkerNativeConfigurationException error)
                { Update(index, state with { State = "failed", Error = "native_config_" + error.Code }); }
                catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                { Update(index, state with { State = "failed", Error = "worker_preflight_or_start_failed" }); }
            }
        }
        await Task.WhenAll(plan.Tasks.Select(async (task, index) =>
        {
            var state = result.Tasks[index];
            if (state.State != "pending") return;
            try
            {
                var nativeProof = nativeProofs[index];
                await preflight(ct);
                if (source is not null && task.WritePaths.Length > 0)
                    await WorkerGitWorkspace.RecheckCleanSourceAsync(source, ct);
                ct.ThrowIfCancellationRequested();
                Update(index, state with { State = "starting" });
                var finished = await RunOne(project, task, clientPath, directory, timeoutSeconds, ct,
                    started => Update(index, started with { NativeConfiguration = nativeProof }), nativeBinding, nativeOverrides[index]);
                finished = finished with { NativeConfiguration = nativeProof };
                Update(index, finished);
                if (source is not null && task.WritePaths.Length > 0 && finished.ThreadId is not null && !ct.IsCancellationRequested)
                {
                    using var lookup = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    lookup.CancelAfter(TimeSpan.FromSeconds(10));
                    try { finished = finished with { VerifiedWorktree = await WorkerGitWorkspace.FindWorktreeAsync(source, finished.ThreadId, lookup.Token) }; }
                    catch (OperationCanceledException) { } // Keep the completed process outcome; checkout identity remains unknown.
                }
                Update(index, finished);
            }
            catch (OperationCanceledException) { Update(index, state with { State = ct.IsCancellationRequested ? "cancelled" : "failed", Error = ct.IsCancellationRequested ? "batch_cancelled" : "worker_preflight_timeout" }); }
            catch (WorkerNativeConfigurationException error)
            { Update(index, state with { State = "failed", Error = "native_config_" + error.Code }); }
            catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or HttpRequestException or Lain.Client.LainApiException or System.ComponentModel.Win32Exception)
            { Update(index, state with { State = "failed", Error = "worker_preflight_or_start_failed" }); }
        }));
        lock (sync)
        {
            result = result with { State = ct.IsCancellationRequested ? "cancelled" : result.Tasks.All(task => task.State == "succeeded") ? "succeeded" : "failed",
                FinishedAt = DateTimeOffset.UtcNow };
            Save();
        }
        return result;
    }

    public static WorkerRunResult? Status(string runRoot)
    {
        var path = Path.Combine(runRoot, "latest.json");
        if (!File.Exists(path)) return null;
        var result = WorkerProjectFiles.Read<WorkerRunResult>(path);
        if (result.Version != 1) throw new IOException("Unsupported worker receipt version.");
        if (result.State != "running") return result;
        try
        {
            using var available = AcquireLock(runRoot);
            result = WorkerProjectFiles.Read<WorkerRunResult>(path);
            if (result.State != "running") return result;
            // A crash is not evidence that all child processes were stopped or that tasks failed.
            return result with { State = "interrupted_unknown" };
        }
        catch (IOException) { return result; }
    }

    public static string[] Arguments(string project, WorkerResolvedTask task, string lastMessage, WorkerNativeBinding? binding = null,
        IReadOnlyList<string>? verifiedOverrides = null)
    {
        if (task.ContextMode is not ("inherit" or "native") || task.ContextMode == "native" && (binding is null || verifiedOverrides is null))
            throw new ArgumentException("Native context exec requires the exact overrides from its configuration gate.");
        var args = new List<string> { "--no-daemon", "exec", "--cd", project,
            "--model", task.Model, "--sandbox", task.WritePaths.Length == 0 ? "read-only" : "workspace-write",
            "--json", "--color", "never", "--output-last-message", lastMessage };
        // Product dispatch supplies the installed binding. Direct runner fixtures
        // exercise subprocess mechanics without opening personal Codex state.
        var overrides = verifiedOverrides ?? (binding is not null ? WorkerNativeConfiguration.ConfigurationOverrides(task, binding) : new[] {
            "model_provider=\"lain\"", "agents.enabled=false", "features.multi_agent=false", "features.multi_agent_v2=false",
            "web_search=\"disabled\"", "model_providers.lain.request_max_retries=0", "model_providers.lain.stream_max_retries=0",
            "model_reasoning_effort=" + JsonSerializer.Serialize(task.ReasoningEffort) });
        foreach (var value in overrides) { args.Add("-c"); args.Add(value); }
        if (task.WritePaths.Length > 0) args.Add("--worktree");
        args.Add("-");
        return args.ToArray();
    }

    private static FileStream AcquireLock(string root)
    {
        try { return WorkerProjectFiles.CreatePrivate(Path.Combine(root, "run.lock"), FileMode.OpenOrCreate); }
        catch (IOException) { throw new IOException("A worker batch is already running for this project, or its lock is unavailable."); }
    }

    private static async Task VerifyClient(string executable, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/setsid"))
            throw new IOException("The bounded worker runner requires Linux with /usr/bin/setsid for child containment.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using var process = Process.Start(new ProcessStartInfo("/usr/bin/setsid") {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            ArgumentList = { "--wait", "--", executable, "--version" }
        }) ?? throw new IOException("Could not start the worker client.");
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            var version = (await stdout).Trim(); await stderr;
            if (process.ExitCode != 0 || version != "codex-cli " + ClientVersion)
                throw new ArgumentException($"The bounded worker runner currently supports Codex CLI {ClientVersion} only.");
        }
        finally { KillContained(process); }
    }

    private static async Task<WorkerTaskResult> RunOne(string project, WorkerResolvedTask task, string client,
        string directory, int timeoutSeconds, CancellationToken ct, Action<WorkerTaskResult> started, WorkerNativeBinding? binding,
        IReadOnlyList<string>? verifiedOverrides)
    {
        var events = Path.Combine(directory, task.Id + ".events.jsonl");
        var diagnostic = Path.Combine(directory, task.Id + ".stderr.txt");
        var message = Path.Combine(directory, task.Id + ".result.txt");
        using (WorkerProjectFiles.CreatePrivate(message)) { }
        await using var eventFile = WorkerProjectFiles.CreatePrivate(events);
        await using var errorFile = WorkerProjectFiles.CreatePrivate(diagnostic);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/setsid"))
            throw new IOException("The bounded worker runner requires Linux with /usr/bin/setsid for child containment.");
        using var process = new Process { StartInfo = new ProcessStartInfo("/usr/bin/setsid") {
            WorkingDirectory = project, UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        } };
        process.StartInfo.ArgumentList.Add("--wait");
        process.StartInfo.ArgumentList.Add("--");
        process.StartInfo.ArgumentList.Add(client);
        foreach (var arg in Arguments(project, task, message, binding, verifiedOverrides)) process.StartInfo.ArgumentList.Add(arg);
        var state = new WorkerTaskResult(task.Id, task.RouteId, task.Model, task.AccountIds, "running",
            EventsPath: events, DiagnosticPath: diagnostic, ResultPath: message);
        string? thread = null;
        var completed = false;
        var failed = false;
        Task? drains = null;
        try
        {
            if (!process.Start()) throw new IOException("Could not start the worker client.");
            started(state);
            var outputDrain = Drain(process.StandardOutput, eventFile, line =>
            {
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (!root.TryGetProperty("type", out var type)) return;
                    switch (type.GetString())
                    {
                        case "thread.started":
                            if (root.TryGetProperty("thread_id", out var value) && value.ValueKind == JsonValueKind.String) thread = value.GetString();
                            break;
                        case "turn.completed": completed = true; break;
                        case "turn.failed": failed = true; break;
                    }
                }
                catch (JsonException) { } // Preserve raw events; unrecognized data never proves a successful turn.
            }, deadline.Token);
            drains = Task.WhenAll(outputDrain, Drain(process.StandardError, errorFile, null, deadline.Token));
            var prompt = task.Prompt + (task.WritePaths.Length == 0 ? "" :
                "\n\nWork in your native isolated worktree. Declared write scope: " + string.Join(", ", task.WritePaths) +
                ". Report changed files and worktree/branch for integration. Do not merge automatically.") +
                "\n\nDo not start additional workers. Return the task result and any limitations.";
            await process.StandardInput.WriteAsync(prompt.AsMemory(), deadline.Token);
            process.StandardInput.Close();
            var exited = process.WaitForExitAsync(deadline.Token);
            if (await Task.WhenAny(exited, drains) == drains) await drains;
            await exited; await drains;
            return state with { State = process.ExitCode == 0 && completed && !failed ? "succeeded" : "failed",
                ThreadId = thread, ExitCode = process.ExitCode,
                Error = process.ExitCode == 0 && completed && !failed ? null : "client_exit_or_incomplete_turn" };
        }
        catch (OperationCanceledException)
        { return state with { State = ct.IsCancellationRequested ? "cancelled" : "timed_out", ThreadId = thread,
            Error = ct.IsCancellationRequested ? "batch_cancelled" : "worker_deadline_exceeded" }; }
        catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return state with { State = "failed", ThreadId = thread, Error = "client_io_or_start_failed" }; }
        finally
        {
            KillContained(process);
            deadline.Cancel();
            if (drains is not null)
                try { await drains; } catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
            try { if (process.Id > 0) await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception error) when (error is InvalidOperationException or TimeoutException) { }
        }
    }

    private static async Task Drain(StreamReader reader, Stream stream, Action<string>? observe, CancellationToken ct)
    {
        await using var writer = new StreamWriter(stream, leaveOpen: true);
        var count = 0L;
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            count += line.Length;
            if (count > 16 * 1024 * 1024) throw new IOException("Worker event/diagnostic log exceeded its limit.");
            await writer.WriteLineAsync(line.AsMemory(), ct);
            observe?.Invoke(line);
        }
    }

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    // The process-group ID survives its leader, so exited native clients cannot
    // leave descendants holding pipes/locks alive until after the batch finishes.
    private static void KillContained(Process process)
    {
        Kill(process);
        if (!OperatingSystem.IsLinux()) return;
        try { if (process.Id > 1) _ = KillProcessGroup(-process.Id, 9); }
        catch (InvalidOperationException) { }
    }

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int KillProcessGroup(int pid, int signal);
}
