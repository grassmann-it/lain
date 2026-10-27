using System.Diagnostics;
using System.Text.Json;
using Lain.Cli;

/// <summary>Real local Git fixtures, with native Codex ownership records supplied as fixtures; no inference.</summary>
public static class GitWorkspaceCases
{
    public static async Task RunAsync(string root, Action<bool, string> require)
    {
        var fixtures = Path.Combine(root, "git workspace cases " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtures);
        var repository = Path.Combine(fixtures, "source repository");
        await Git(fixtures, "init", "--initial-branch=main", repository);
        var project = Path.Combine(repository, "nested project");
        Directory.CreateDirectory(project);
        var tracked = Path.Combine(repository, "outside-project.txt");
        File.WriteAllText(tracked, "committed source\n");
        File.WriteAllText(Path.Combine(project, "inside-project.txt"), "committed nested source\n");
        File.WriteAllText(Path.Combine(repository, ".gitignore"), "ignored.txt\n");
        await Git(repository, "add", "--all");
        await Git(repository, "commit", "-m", "source fixture");

        var index = Path.Combine(repository, ".git", "index");
        var indexBefore = File.ReadAllBytes(index);
        var source = await WorkerGitWorkspace.CaptureCleanSourceAsync(project);
        require(source.RepositoryRoot == repository && source.ProjectDirectory == project &&
            source.CommonDirectory == Path.Combine(repository, ".git"), "nested project captures whole-repository Git identity");
        require(source.Head == await Git(repository, "rev-parse", "HEAD"), "source records the actual committed HEAD");
        await WorkerGitWorkspace.RecheckCleanSourceAsync(source);
        require(indexBefore.SequenceEqual(File.ReadAllBytes(index)), "source inspection does not rewrite the Git index");

        File.WriteAllText(Path.Combine(repository, "ignored.txt"), "not included in native HEAD checkout\n");
        await WorkerGitWorkspace.RecheckCleanSourceAsync(source);
        require(true, "ignored source files are permitted without claiming that they will be copied");

        var untracked = Path.Combine(repository, "untracked\nwith spaces.txt");
        File.WriteAllText(untracked, "outside the requested project\n");
        await Reject(() => WorkerGitWorkspace.CaptureCleanSourceAsync(project), "untracked files outside a nested project block write capture");
        await Reject(() => WorkerGitWorkspace.RecheckCleanSourceAsync(source), "untracked changes after capture block dispatch");
        File.Delete(untracked);
        File.WriteAllText(tracked, "unstaged change outside project\n");
        await Reject(() => WorkerGitWorkspace.CaptureCleanSourceAsync(project), "unstaged tracked changes outside the project block capture");
        await Git(repository, "add", "--", "outside-project.txt");
        await Reject(() => WorkerGitWorkspace.CaptureCleanSourceAsync(project), "staged tracked changes outside the project block capture");
        await Git(repository, "restore", "--staged", "--", "outside-project.txt");
        File.WriteAllText(tracked, "committed source\n");

        await Git(repository, "update-index", "--assume-unchanged", "--", "outside-project.txt");
        File.WriteAllText(tracked, "hidden tracked change\n");
        await Reject(() => WorkerGitWorkspace.CaptureCleanSourceAsync(project), "assume-unchanged cannot hide tracked source changes");
        await Git(repository, "update-index", "--no-assume-unchanged", "--", "outside-project.txt");
        File.WriteAllText(tracked, "committed source\n");
        await Git(repository, "update-index", "--skip-worktree", "--", "outside-project.txt");
        await Reject(() => WorkerGitWorkspace.CaptureCleanSourceAsync(project), "skip-worktree sources fail closed instead of silently omitting checks");
        await Git(repository, "update-index", "--no-skip-worktree", "--", "outside-project.txt");
        await WorkerGitWorkspace.RecheckCleanSourceAsync(source);

        var variables = new Dictionary<string, string?>
        {
            ["GIT_DIR"] = Path.Combine(fixtures, "not-a-repository"),
            ["GIT_WORK_TREE"] = fixtures,
            ["GIT_INDEX_FILE"] = Path.Combine(fixtures, "not-an-index"),
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_CONFIG_KEY_0"] = "core.bare",
            ["GIT_CONFIG_VALUE_0"] = "true"
        };
        var previous = variables.Keys.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var (name, value) in variables) Environment.SetEnvironmentVariable(name, value);
            await WorkerGitWorkspace.RecheckCleanSourceAsync(source);
            require(true, "inherited Git selectors and command config cannot redirect workspace inspection");
        }
        finally
        {
            foreach (var (name, value) in previous) Environment.SetEnvironmentVariable(name, value);
        }

        await Git(repository, "commit", "--allow-empty", "-m", "different source HEAD");
        await Reject(() => WorkerGitWorkspace.RecheckCleanSourceAsync(source), "clean HEAD movement after capture blocks dispatch");
        source = await WorkerGitWorkspace.CaptureCleanSourceAsync(project);

        // A malicious clean filter must not execute during a status check.
        File.WriteAllText(Path.Combine(repository, ".gitattributes"), "*.filtered filter=worker-fixture\n");
        File.WriteAllText(Path.Combine(repository, "sample.filtered"), "committed filter fixture\n");
        await Git(repository, "add", "--all");
        await Git(repository, "commit", "-m", "filter fixture");
        var marker = Path.Combine(fixtures, "filter-executed");
        await Git(repository, "config", "filter.worker-fixture.clean", "touch '" + marker.Replace("'", "'\\''", StringComparison.Ordinal) + "'");
        File.WriteAllText(Path.Combine(repository, "sample.filtered"), "changed filter fixture\n");
        await Reject(() => WorkerGitWorkspace.CaptureCleanSourceAsync(project), "filter-backed tracked changes still block capture");
        require(!File.Exists(marker), "workspace inspection disables configured clean-filter commands");
        await Git(repository, "config", "--unset", "filter.worker-fixture.clean");
        File.WriteAllText(Path.Combine(repository, "sample.filtered"), "committed filter fixture\n");
        source = await WorkerGitWorkspace.CaptureCleanSourceAsync(project);

        var checkout = Path.Combine(fixtures, "checkout\nwith spaces");
        await Git(repository, "worktree", "add", "--detach", checkout, source.Head);
        var gitDirectory = await Git(checkout, "rev-parse", "--absolute-git-dir");
        var ownerPath = Path.Combine(gitDirectory, "codex-thread.json");
        const string thread = "thread-fixture";
        require(await WorkerGitWorkspace.FindWorktreeAsync(source, thread) is null, "an unbound registered worktree is not guessed from its path");
        WriteOwner(ownerPath, thread);
        var identity = await WorkerGitWorkspace.FindWorktreeAsync(source, thread);
        require(identity is { Detached: true } && identity.Path == checkout && identity.Head == source.Head,
            "native ownership metadata joins a registered detached worktree to its actual path and HEAD");
        require(!File.Exists(Path.Combine(checkout, "ignored.txt")), "a real Git HEAD worktree omits ignored source content");
        require(await WorkerGitWorkspace.FindWorktreeAsync(source, "different-thread") is null, "ownership is matched to the requested thread ID");

        File.WriteAllText(Path.Combine(checkout, "outside-project.txt"), "worker output retained\n");
        require((await WorkerGitWorkspace.FindWorktreeAsync(source, thread))?.Path == checkout, "dirty worker output does not prevent identity recovery");
        await Git(checkout, "switch", "-c", "worker-result");
        await Git(checkout, "commit", "-am", "worker result");
        identity = await WorkerGitWorkspace.FindWorktreeAsync(source, thread);
        require(identity is { Detached: false } && identity.Path == checkout && identity.Head == await Git(checkout, "rev-parse", "HEAD") && identity.Head != source.Head,
            "identity reports the current worker HEAD and attached state after a worker commit");

        foreach (var malformed in new[]
        {
            "not JSON", "[]", "{\"version\":2,\"ownerThreadId\":\"thread-fixture\"}",
            "{\"version\":1,\"ownerThreadId\":\"thread-fixture\",\"extra\":true}",
            "{\"version\":1,\"version\":1,\"ownerThreadId\":\"thread-fixture\"}",
            "{\"version\":\"1\",\"ownerThreadId\":\"thread-fixture\"}"
        })
        {
            File.WriteAllText(ownerPath, malformed);
            require(await WorkerGitWorkspace.FindWorktreeAsync(source, thread) is null, "malformed or unsupported ownership metadata never proves identity");
        }
        var externalOwner = Path.Combine(fixtures, "external-owner.json");
        WriteOwner(externalOwner, thread);
        File.Delete(ownerPath);
        File.CreateSymbolicLink(ownerPath, externalOwner);
        require(await WorkerGitWorkspace.FindWorktreeAsync(source, thread) is null, "symlinked ownership metadata is not trusted");
        File.Delete(ownerPath);
        WriteOwner(ownerPath, thread);

        var backlinkPath = Path.Combine(gitDirectory, "gitdir");
        var backlink = File.ReadAllText(backlinkPath);
        File.WriteAllText(backlinkPath, Path.Combine(repository, ".git") + "\n");
        require(await WorkerGitWorkspace.FindWorktreeAsync(source, thread) is null, "a registration without a matching Git backlink does not prove checkout identity");
        File.WriteAllText(backlinkPath, backlink);
        require((await WorkerGitWorkspace.FindWorktreeAsync(source, thread))?.Path == checkout, "valid ownership resolves again after fixture backlink restoration");

        var duplicate = Path.Combine(fixtures, "second checkout");
        await Git(repository, "worktree", "add", "--detach", duplicate, source.Head);
        WriteOwner(Path.Combine(await Git(duplicate, "rev-parse", "--absolute-git-dir"), "codex-thread.json"), thread);
        require(await WorkerGitWorkspace.FindWorktreeAsync(source, thread) is null, "duplicate ownership is ambiguous and never selects the first worktree");
        await Git(repository, "worktree", "remove", duplicate);
        require((await WorkerGitWorkspace.FindWorktreeAsync(source, thread))?.Path == checkout, "unique identity remains discoverable after ambiguity is removed");

        var unrelated = Path.Combine(fixtures, "unrelated repository");
        await Git(fixtures, "init", "--initial-branch=main", unrelated);
        await Git(unrelated, "commit", "--allow-empty", "-m", "unrelated fixture");
        var unrelatedSource = await WorkerGitWorkspace.CaptureCleanSourceAsync(unrelated);
        require(await WorkerGitWorkspace.FindWorktreeAsync(unrelatedSource, thread) is null, "matching metadata from another repository cannot supply an identity");
        await Reject(() => WorkerGitWorkspace.CaptureCleanSourceAsync(fixtures), "a non-Git directory cannot become a write source");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancellationObserved = false;
        try { await WorkerGitWorkspace.CaptureCleanSourceAsync(project, cancelled.Token); }
        catch (OperationCanceledException) { cancellationObserved = true; }
        require(cancellationObserved, "source inspection honors caller cancellation");

        async Task Reject(Func<Task> action, string message)
        {
            var rejected = false;
            try { await action(); }
            catch (Exception error) when (error is IOException or ArgumentException) { rejected = true; }
            require(rejected, message);
        }
    }

    private static void WriteOwner(string path, string thread) =>
        File.WriteAllText(path, JsonSerializer.Serialize(new { version = 1, ownerThreadId = thread }));

    private static async Task<string> Git(string directory, params string[] arguments)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var process = new Process { StartInfo = new ProcessStartInfo("git") {
            WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        } };
        foreach (var variable in process.StartInfo.Environment.Keys.Where(name => name.StartsWith("GIT_", StringComparison.Ordinal)).ToArray())
            process.StartInfo.Environment.Remove(variable);
        process.StartInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        process.StartInfo.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        process.StartInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var setting in new[] { "user.name=Worker fixture", "user.email=worker-fixture@example.invalid", "commit.gpgsign=false", "core.hooksPath=/dev/null", "core.fsmonitor=" })
        { process.StartInfo.ArgumentList.Add("-c"); process.StartInfo.ArgumentList.Add(setting); }
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var output = await stdout;
            var diagnostic = await stderr;
            if (process.ExitCode != 0) throw new IOException("Git workspace fixture failed: " + diagnostic);
            return output.EndsWith('\n') ? output[..^1] : output;
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        }
    }
}
