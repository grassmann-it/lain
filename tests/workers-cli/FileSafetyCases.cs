using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Lain.Cli;
using Lain.Core;
using Microsoft.Win32.SafeHandles;

/// <summary>Real Linux special-file regressions in private fixtures. No provider, client inference or real configuration.</summary>
public static class FileSafetyCases
{
    public static async Task RunAsync(string root, Action<bool, string> require)
    {
        if (!OperatingSystem.IsLinux()) return;
        var fixtures = Path.Combine(root, "file safety " + Guid.NewGuid().ToString("N"));
        WorkerProjectFiles.PrivateDirectory(fixtures);
        require((File.GetUnixFileMode(fixtures) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) == 0, "special-file fixtures are private");
        var project = Path.Combine(fixtures, "project");
        WorkerProjectFiles.PrivateDirectory(project);
        var policyDirectory = Path.Combine(project, ".lain");
        WorkerProjectFiles.PrivateDirectory(policyDirectory);
        var policyPath = Path.Combine(policyDirectory, "workers.json");
        var policy = new WorkerProjectPolicy(1, "profile", 1, []);
        var validJson = JsonSerializer.Serialize(policy, WorkerProjectFiles.Json);
        var regular = Path.Combine(fixtures, "regular ü file.json");
        File.WriteAllText(regular, validJson);
        WorkerProjectFiles.AssertRegular(regular);
        WorkerProjectFiles.AssertRegular(project);
        WorkerProjectFiles.AssertRegular(Path.GetPathRoot(project)!);
        WorkerProjectFiles.AssertRegular(Path.Combine(fixtures, "missing", "new.json"));
        require(WorkerProjectFiles.Read<WorkerProjectPolicy>(regular).ProfileId == "profile", "regular UTF-8 filename reads succeed");
        Reject(() => WorkerProjectFiles.Read<WorkerProjectPolicy>(project), "directories are allowed as paths but rejected as JSON read descriptors");

        var originalMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute;
        File.SetUnixFileMode(project, originalMode);
        WorkerProjectFiles.PrivateDirectory(project);
        require(File.GetUnixFileMode(project) == originalMode, "private-directory helper does not chmod an existing project directory");

        // Exercise the public CLI in a killable child. A regression must fail the suite,
        // not leave the parent test process synchronously blocked in open() forever.
        await Mkfifo(policyPath, fixtures);
        var blockedPolicy = await Cli(project, "validate");
        require(!blockedPolicy.TimedOut && blockedPolicy.Exit == 3 && blockedPolicy.Error.Contains("regular files or directories", StringComparison.Ordinal),
            "actual mkfifo policy is rejected within five seconds before Host discovery");
        Reject(() => WorkerProjectFiles.AssertRegular(policyPath), "AssertRegular rejects FIFO nodes");
        Reject(() => WorkerProjectFiles.CreatePrivate(policyPath, FileMode.OpenOrCreate).Dispose(), "private file creation refuses an existing FIFO");
        Reject(() => WorkerProjectFiles.PrivateDirectory(policyPath), "private directory creation refuses an existing FIFO");
        File.Delete(policyPath);
        File.WriteAllText(policyPath, validJson);
        var taskPipe = Path.Combine(fixtures, "tasks.fifo");
        await Mkfifo(taskPipe, fixtures);
        var blockedTasks = await Cli(project, "plan", "--tasks", taskPipe, "--revision", "7");
        require(!blockedTasks.TimedOut && blockedTasks.Exit == 3 && blockedTasks.Error.Contains("regular files or directories", StringComparison.Ordinal),
            "actual mkfifo task file is rejected within five seconds before Host discovery");

        var socketPath = Path.Combine(fixtures, "socket");
        // sockaddr_un is short even when the caller's private evidence root is long.
        using (var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified))
        {
            // Bind relative to the fixture directory without changing process-global cwd.
            // A short abstract endpoint cannot test filesystem validation, so use a short
            // /proc descriptor alias to this same private directory for the bind only.
            using var directoryHandle = OpenDirectory(fixtures);
            var socketAlias = "/proc/self/fd/" + directoryHandle.DangerousGetHandle().ToInt64() + "/socket";
            socket.Bind(new UnixDomainSocketEndPoint(socketAlias));
            Reject(() => WorkerProjectFiles.AssertRegular(socketPath), "AssertRegular rejects Unix socket nodes");
            Reject(() => WorkerProjectFiles.Read<WorkerProjectPolicy>(socketPath), "JSON reads reject Unix sockets");
        }
        File.Delete(socketPath);
        Reject(() => WorkerProjectFiles.AssertRegular("/dev/null"), "AssertRegular rejects character devices");
        Reject(() => WorkerProjectFiles.Read<WorkerProjectPolicy>("/dev/null"), "JSON reads reject character devices");

        var leafLink = Path.Combine(fixtures, "leaf-link.json");
        File.CreateSymbolicLink(leafLink, regular);
        Reject(() => WorkerProjectFiles.Read<WorkerProjectPolicy>(leafLink), "leaf symlinks remain rejected");
        var parentLink = Path.Combine(fixtures, "parent-link");
        Directory.CreateSymbolicLink(parentLink, policyDirectory);
        Reject(() => WorkerProjectFiles.AssertRegular(Path.Combine(parentLink, "workers.json")), "existing symlink ancestors remain rejected");
        Reject(() => WorkerProjectFiles.Read<WorkerProjectPolicy>(Path.Combine(parentLink, "workers.json")), "reads through symlink ancestors remain rejected");
        Reject(() => WorkerProjectFiles.AssertRegular(Path.Combine(parentLink, "missing.json")), "symlink ancestors of a missing file remain rejected");

        // Invoke only the descriptor-opening step to deterministically model a replacement
        // AFTER AssertRegular. This also prevents the first path check from masking a
        // missing O_NONBLOCK/O_NOFOLLOW flag or missing opened-descriptor type check.
        var openRead = typeof(WorkerProjectFiles).GetMethod("OpenRead", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new Exception("Descriptor-read regression seam is missing.");
        using (var stream = (FileStream)openRead.Invoke(null, [regular])!)
        {
            require(stream.CanRead && stream.CanSeek && !stream.CanWrite, "verified regular descriptor is transferred to an owning read-only FileStream");
            var flagsLine = File.ReadLines("/proc/self/fdinfo/" + stream.SafeFileHandle.DangerousGetHandle().ToInt64())
                .Single(line => line.StartsWith("flags:", StringComparison.Ordinal));
            var flags = Convert.ToInt32(flagsLine[6..].Trim(), 8);
            require((flags & (0x800 | 0x20000 | 0x80000)) == (0x800 | 0x20000 | 0x80000),
                "opened Linux descriptor retains O_NONBLOCK, O_NOFOLLOW and O_CLOEXEC");
        }
        var swapped = Path.Combine(fixtures, "swapped.json");
        File.WriteAllText(swapped, validJson);
        WorkerProjectFiles.AssertRegular(swapped);
        File.Delete(swapped);
        await Mkfifo(swapped, fixtures);
        await RejectOpenedDescriptor(openRead, swapped, "FIFO swapped in after path validation is rejected without blocking");
        File.Delete(swapped);
        File.WriteAllText(swapped, validJson);
        WorkerProjectFiles.AssertRegular(swapped);
        File.Delete(swapped);
        File.CreateSymbolicLink(swapped, regular);
        await RejectOpenedDescriptor(openRead, swapped, "symlink swapped in after path validation is rejected by O_NOFOLLOW");
        await RejectOpenedDescriptor(openRead, "/dev/null", "opened-descriptor type check independently rejects a character device");
        await RejectOpenedDescriptor(openRead, project, "opened-descriptor type check independently rejects directories");

        // Repeated failures must not keep any opened FIFO/device descriptor alive.
        for (var attempt = 0; attempt < 16; attempt++)
            await RejectOpenedDescriptor(openRead, taskPipe, "repeated FIFO descriptor rejection closes its SafeFileHandle");
        require(!Directory.EnumerateFiles("/proc/self/fd").Any(path => new FileInfo(path).LinkTarget == taskPipe),
            "no rejected FIFO SafeFileHandle remains open");

        void Reject(Action action, string message)
        {
            var rejected = false;
            try { action(); } catch (IOException) { rejected = true; }
            require(rejected, message);
        }

        async Task RejectOpenedDescriptor(MethodInfo method, string path, string message)
        {
            var operation = Task.Run(() =>
            {
                try { using var stream = (FileStream)method.Invoke(null, [path])!; return false; }
                catch (TargetInvocationException error) when (error.InnerException is IOException) { return true; }
            });
            var timedOut = false;
            bool rejected;
            try { rejected = await operation.WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (TimeoutException)
            {
                timedOut = true;
                // Rescue an accidentally blocking FIFO open before failing the test, so
                // a broken implementation cannot strand a blocked thread in the suite.
                using var rescue = new SafeFileHandle((IntPtr)Open(path, 0x2 | 0x800 | 0x80000), ownsHandle: true);
                rejected = await operation.WaitAsync(TimeSpan.FromSeconds(3));
            }
            require(!timedOut && rejected, message);
        }

        async Task<(bool TimedOut, int Exit, string Error)> Cli(string directory, string action, params string[] extra)
        {
            var arguments = new[] { typeof(CliApplication).Assembly.Location, "workers", action, "--project", directory, "--json" }.Concat(extra).ToArray();
            return await Run("dotnet", fixtures, arguments);
        }

        async Task Mkfifo(string path, string directory)
        {
            var result = await Run("mkfifo", directory, ["--mode=600", "--", path]);
            require(!result.TimedOut && result.Exit == 0, "mkfifo creates an actual private FIFO fixture");
        }

        async Task<(bool TimedOut, int Exit, string Error)> Run(string executable, string directory, string[] arguments)
        {
            using var process = new Process { StartInfo = new ProcessStartInfo(executable)
                { WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true } };
            process.StartInfo.Environment["LAIN_HOME"] = Path.Combine(fixtures, "unused state");
            foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var timedOut = false;
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                timedOut = true;
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                await process.WaitForExitAsync();
            }
            await output;
            return (timedOut, process.ExitCode, await error);
        }
    }

    private static SafeFileHandle OpenDirectory(string path)
    {
        var descriptor = Open(path, 0x10000 | 0x20000 | 0x80000); // O_DIRECTORY | O_NOFOLLOW | O_CLOEXEC
        if (descriptor < 0) throw new IOException("Cannot open private socket fixture directory.");
        return new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
}
