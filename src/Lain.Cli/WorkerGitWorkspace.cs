using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Lain.Cli;

public sealed record WorkerGitSource(string RepositoryRoot, string ProjectDirectory, string Head, string CommonDirectory);
public sealed record WorkerGitWorktreeIdentity(string Path, string Head, bool Detached);

/// <summary>
/// Native Codex 0.164 worktrees start from HEAD, without copying source WIP. Write batches must
/// capture a clean source and recheck it immediately before dispatch. Ignored files are also absent
/// from the new checkout; this check does not snapshot them or make them available to a worker.
/// Status checks disable configured filters; sources relying on content transformations may be
/// rejected conservatively rather than executing repository-configured filter commands.
/// These checks detect changes, but do not lock Git against unrelated writers between check and launch.
/// </summary>
public static class WorkerGitWorkspace
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static async Task<WorkerGitSource> CaptureCleanSourceAsync(string project, CancellationToken ct = default)
    {
        project = WorkerProjectFiles.ProjectRoot(project);
        var repository = await GitPath(project, ct, "rev-parse", "--show-toplevel");
        var common = await GitPath(repository, ct, "rev-parse", "--path-format=absolute", "--git-common-dir");
        var head = await Head(repository, ct);
        await RequireClean(repository, ct);
        if (head != await Head(repository, ct)) throw new IOException("The worker source HEAD changed during inspection.");
        return new(repository, project, head, common);
    }

    public static async Task RecheckCleanSourceAsync(WorkerGitSource source, CancellationToken ct = default)
    {
        var current = await CaptureCleanSourceAsync(source.ProjectDirectory, ct);
        if (!SamePath(current.RepositoryRoot, source.RepositoryRoot) || !SamePath(current.CommonDirectory, source.CommonDirectory) ||
            current.Head != source.Head)
            throw new IOException("The worker source repository or HEAD changed; create a fresh batch.");
    }

    /// <summary>
    /// Finds a unique registered linked checkout with native version-1 codex-thread.json ownership.
    /// Path, HEAD and detached state come from Git and its metadata, never model output. Missing,
    /// malformed, ambiguous or concurrently changing registrations return null. Cancellation propagates.
    /// The returned path is the checkout root; a nested project's cwd is relative to that root.
    /// </summary>
    public static async Task<WorkerGitWorktreeIdentity?> FindWorktreeAsync(WorkerGitSource source, string threadId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(threadId)) return null;
        try
        {
            if (!SamePath(await GitPath(source.RepositoryRoot, ct, "rev-parse", "--path-format=absolute", "--git-common-dir"), source.CommonDirectory))
                return null;
            var output = await Git(source.RepositoryRoot, ct, ["worktree", "list", "--porcelain", "-z"]);
            WorkerGitWorktreeIdentity? found = null;
            var matches = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var entry in output.Output.Split("\0\0", StringSplitOptions.RemoveEmptyEntries))
            {
                var identity = await InspectWorktree(source, threadId, entry.Split('\0', StringSplitOptions.RemoveEmptyEntries), ct);
                if (identity is null || !matches.Add(identity.Path)) continue;
                if (found is not null) return null;
                found = identity;
            }
            return found;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        { return null; }
    }

    private static async Task RequireClean(string repository, CancellationToken ct)
    {
        // Git status honors assume-unchanged/skip-worktree flags. Refuse those hidden-file modes
        // rather than accepting a source whose tracked contents were not actually checked.
        var tracked = await Git(repository, ct, ["ls-files", "-v", "-z"]);
        if (tracked.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Any(file => file[0] == 'S' || char.IsAsciiLetterLower(file[0])))
            throw new IOException("Worker write sources cannot hide tracked changes with assume-unchanged or skip-worktree flags.");
        var status = await Git(repository, ct,
            ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none"], disableFilters: true);
        if (status.Output.Length != 0)
            throw new IOException("Worker write tasks require a clean whole repository: commit or review staged, unstaged and untracked files first. Ignored files are not copied; no source snapshot is created.");
    }

    private static async Task<WorkerGitWorktreeIdentity?> InspectWorktree(WorkerGitSource source, string threadId,
        string[] fields, CancellationToken ct)
    {
        try
        {
            var paths = fields.Where(field => field.StartsWith("worktree ", StringComparison.Ordinal)).ToArray();
            var heads = fields.Where(field => field.StartsWith("HEAD ", StringComparison.Ordinal)).ToArray();
            var branches = fields.Where(field => field.StartsWith("branch ", StringComparison.Ordinal)).ToArray();
            var detached = fields.Count(field => field == "detached");
            if (paths.Length != 1 || heads.Length != 1 || branches.Length + detached != 1 || fields.Any(field => field == "bare" || field.StartsWith("prunable", StringComparison.Ordinal)))
                return null;
            var path = paths[0][9..];
            if (!System.IO.Path.IsPathFullyQualified(path) || !Directory.Exists(path)) return null;
            WorkerProjectFiles.AssertRegular(path);
            var root = WorkerProjectFiles.ProjectRoot(path);
            var gitFile = System.IO.Path.Combine(root, ".git");
            WorkerProjectFiles.AssertRegular(gitFile);
            if (!File.Exists(gitFile) || Directory.Exists(gitFile) ||
                !SamePath(await GitPath(root, ct, "rev-parse", "--show-toplevel"), root) ||
                !SamePath(await GitPath(root, ct, "rev-parse", "--path-format=absolute", "--git-common-dir"), source.CommonDirectory)) return null;
            var gitDirectory = await GitPath(root, ct, "rev-parse", "--absolute-git-dir");
            var registrations = System.IO.Path.Combine(source.CommonDirectory, "worktrees");
            if (!SamePath(System.IO.Path.GetDirectoryName(gitDirectory)!, registrations)) return null;
            var backlink = StripLineEnding(ReadMetadata(System.IO.Path.Combine(gitDirectory, "gitdir")));
            if (!SamePath(System.IO.Path.GetFullPath(backlink, gitDirectory), gitFile)) return null;
            var owner = ReadMetadata(System.IO.Path.Combine(gitDirectory, "codex-thread.json"));
            using var document = JsonDocument.Parse(owner);
            var record = document.RootElement;
            if (record.ValueKind != JsonValueKind.Object) return null;
            var properties = record.EnumerateObject().ToArray();
            if (properties.Length != 2 || properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != 2 ||
                !record.TryGetProperty("version", out var version) || !version.TryGetInt32(out var number) || number != 1 ||
                !record.TryGetProperty("ownerThreadId", out var ownerId) || ownerId.ValueKind != JsonValueKind.String || ownerId.GetString() != threadId)
                return null;
            var head = await Head(root, ct);
            if (head != heads[0][5..]) return null;
            var branch = await Git(root, ct, ["symbolic-ref", "--quiet", "HEAD"], allowExitOne: true);
            if (detached == 1 ? branch.ExitCode != 1 : branch.ExitCode != 0 || StripLineEnding(branch.Output) != branches[0][7..]) return null;
            return new(root, head, detached == 1);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException)
        { return null; }
    }

    private static string ReadMetadata(string path)
    {
        WorkerProjectFiles.AssertRegular(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (file.Length > 64 * 1024) throw new IOException("Worktree metadata exceeds its size limit.");
        using var reader = new StreamReader(file, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
        return reader.ReadToEnd();
    }

    private static async Task<string> Head(string directory, CancellationToken ct)
    {
        var result = StripLineEnding((await Git(directory, ct, ["rev-parse", "--verify", "--end-of-options", "HEAD^{commit}"])).Output);
        if (result.Length is not (40 or 64) || result.Any(c => !char.IsAsciiHexDigit(c))) throw new IOException("Worker source HEAD is invalid.");
        return result;
    }

    private static async Task<string> GitPath(string directory, CancellationToken ct, params string[] arguments)
    {
        var path = StripLineEnding((await Git(directory, ct, arguments)).Output);
        if (!System.IO.Path.IsPathFullyQualified(path)) throw new IOException("Git returned a non-absolute worker path.");
        return WorkerProjectFiles.ProjectRoot(path);
    }

    private static bool SamePath(string left, string right) => string.Equals(System.IO.Path.TrimEndingDirectorySeparator(left), System.IO.Path.TrimEndingDirectorySeparator(right), PathComparison);
    private static string StripLineEnding(string value)
    {
        if (!value.EndsWith('\n')) return value;
        value = value[..^1];
        return OperatingSystem.IsWindows() && value.EndsWith('\r') ? value[..^1] : value;
    }
    private sealed record GitResult(int ExitCode, string Output);

    private static async Task<GitResult> Git(string directory, CancellationToken ct, string[] arguments,
        bool allowExitOne = false, bool disableFilters = false)
    {
        ct.ThrowIfCancellationRequested();
        var filters = Array.Empty<string>();
        if (disableFilters)
        {
            var configuration = await Git(directory, ct, ["config", "--null", "--name-only", "--get-regexp", "^filter\\."], allowExitOne: true);
            filters = configuration.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Where(key => key.StartsWith("filter.", StringComparison.OrdinalIgnoreCase) && key.LastIndexOf('.') > 7)
                .Select(key => key[7..key.LastIndexOf('.')]).Distinct(StringComparer.Ordinal).ToArray();
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var process = new Process { StartInfo = new ProcessStartInfo("git") {
            WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false, true), StandardErrorEncoding = new UTF8Encoding(false, true)
        } };
        foreach (var variable in new[] { "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_CEILING_DIRECTORIES", "GIT_CONFIG", "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_COUNT", "GIT_OBJECT_DIRECTORY", "GIT_DIR", "GIT_WORK_TREE", "GIT_IMPLICIT_WORK_TREE", "GIT_GRAFT_FILE", "GIT_INDEX_FILE", "GIT_NO_REPLACE_OBJECTS", "GIT_REPLACE_REF_BASE", "GIT_PREFIX", "GIT_SHALLOW_FILE", "GIT_COMMON_DIR" })
            process.StartInfo.Environment.Remove(variable);
        foreach (var variable in process.StartInfo.Environment.Keys.Where(key => key.StartsWith("GIT_CONFIG_KEY_", StringComparison.Ordinal) || key.StartsWith("GIT_CONFIG_VALUE_", StringComparison.Ordinal)).ToArray())
            process.StartInfo.Environment.Remove(variable);
        process.StartInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        process.StartInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        process.StartInfo.Environment["GIT_NO_LAZY_FETCH"] = "1";
        foreach (var setting in new[] { "safe.bareRepository=explicit", "core.hooksPath=" + (OperatingSystem.IsWindows() ? "NUL" : "/dev/null"), "core.fsmonitor=", "core.attributesFile=" })
        { process.StartInfo.ArgumentList.Add("-c"); process.StartInfo.ArgumentList.Add(setting); }
        foreach (var filter in filters)
            foreach (var setting in new[] { "process=", "clean=", "smudge=", "required=false" })
            { process.StartInfo.ArgumentList.Add("-c"); process.StartInfo.ArgumentList.Add("filter." + filter + "." + setting); }
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        Task<string>? stdout = null;
        Task<string>? stderr = null;
        try
        {
            if (!process.Start()) throw new IOException("Could not start Git for worker workspace inspection.");
            stdout = ReadOutput(process.StandardOutput, deadline.Token);
            stderr = ReadOutput(process.StandardError, deadline.Token);
            var exited = process.WaitForExitAsync(deadline.Token);
            var first = await Task.WhenAny(exited, stdout, stderr);
            await first;
            await exited;
            var output = await stdout;
            await stderr;
            if (process.ExitCode != 0 && !(allowExitOne && process.ExitCode == 1)) throw new IOException("Git could not inspect the worker workspace.");
            return new(process.ExitCode, output);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new IOException("Git worker workspace inspection timed out."); }
        catch (System.ComponentModel.Win32Exception error)
        { throw new IOException("Could not start Git for worker workspace inspection.", error); }
        catch (DecoderFallbackException error)
        { throw new IOException("Git worker paths must be valid UTF-8.", error); }
        finally
        {
            deadline.Cancel();
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            if (stdout is not null && stderr is not null)
                try { await Task.WhenAll(stdout, stderr); }
                catch (Exception error) when (error is IOException or OperationCanceledException or DecoderFallbackException) { }
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (Exception error) when (error is InvalidOperationException or TimeoutException) { }
        }
    }

    private static async Task<string> ReadOutput(StreamReader reader, CancellationToken ct)
    {
        var output = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) != 0)
        {
            if (output.Length + count > 4 * 1024 * 1024) throw new IOException("Git worker workspace output exceeds its size limit.");
            output.Append(buffer, 0, count);
        }
        return output.ToString();
    }
}
