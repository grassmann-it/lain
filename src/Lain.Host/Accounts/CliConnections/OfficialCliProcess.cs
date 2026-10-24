using System.Diagnostics;
using System.Text;

namespace Lain.Host.Accounts.CliConnections;

public sealed record CliProcessIdentity(int ProcessId, DateTimeOffset StartedAt, string Executable);

/// <summary>No shell, inherited credentials, installation or provider-independent HOME rewriting.</summary>
public sealed class OfficialCliProcess(bool allowFixture = false)
{
    public const int MaximumOutputBytes = 1024 * 1024;
    public async Task<CliPrivateOutput> RunAsync(CliQualificationManifest manifest, IReadOnlyList<string> arguments,
        string ownedDirectory, IReadOnlyDictionary<string, string> isolationEnvironment, TimeSpan timeout,
        CancellationToken ct = default, Action<CliProcessIdentity>? started = null, bool versionProbe = false)
    {
        ct.ThrowIfCancellationRequested();
        manifest.Validate(manifest.Provider, allowFixture);
        CliPrivatePaths.Directory(ownedDirectory);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(15) || arguments.Count > 64
            || arguments.Any(a => a.Length > 16384 || a.Any(char.IsControl))) throw new CliConnectionException("invalid-cli-invocation");
        if (!versionProbe && !manifest.TestedCommands.Any(c => arguments.Take(c.Split(' ').Length).SequenceEqual(c.Split(' '))))
            throw new CliConnectionException("unqualified-cli-command");
        if (versionProbe && !arguments.SequenceEqual(["--version"])) throw new CliConnectionException("unqualified-cli-version-command");
        var info = new ProcessStartInfo(manifest.ExecutablePath) { UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, RedirectStandardInput = true, CreateNoWindow = true, WorkingDirectory = ownedDirectory };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        info.Environment.Clear();
        info.Environment["PATH"] = "/usr/bin:/bin";
        info.Environment["LANG"] = "C.UTF-8";
        foreach (var item in isolationEnvironment)
        {
            // Complete environment replacement prevents inherited API keys, proxy hosts,
            // default-profile/config overrides, Node preload and browser-command injection.
            if (item.Key is not ("BAILIAN_CONFIG_DIR" or "LAIN_CLI_FIXTURE_DIR") || item.Value != ownedDirectory)
                throw new CliConnectionException("unqualified-cli-environment");
            if (item.Key == "LAIN_CLI_FIXTURE_DIR" && !allowFixture) throw new CliConnectionException("unqualified-cli-isolation");
            info.Environment[item.Key] = item.Value;
        }
        if (!versionProbe && isolationEnvironment.Count == 0) throw new CliConnectionException("unqualified-cli-isolation");
        using var process = new Process { StartInfo = info };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        try
        {
            if (!process.Start()) throw new CliConnectionException("cli-start-failed");
            process.StandardInput.Close();
            started?.Invoke(new(process.Id, new DateTimeOffset(process.StartTime.ToUniversalTime()), manifest.ExecutablePath));
            var stdout = CaptureAsync(process.StandardOutput.BaseStream, deadline.Token);
            var stderr = CaptureAsync(process.StandardError.BaseStream, deadline.Token);
            // A faulted drain cancels the sibling and wait immediately, rather than
            // deadlocking when the child fills the other pipe.
            _ = stdout.ContinueWith(t => { if (t.IsFaulted) deadline.Cancel(); }, TaskScheduler.Default);
            _ = stderr.ContinueWith(t => { if (t.IsFaulted) deadline.Cancel(); }, TaskScheduler.Default);
            try { await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(deadline.Token)); }
            catch
            {
                Stop(process);
                try { await Task.WhenAll(stdout, stderr, process.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
                if (stdout.Exception?.GetBaseException() is CliConnectionException || stderr.Exception?.GetBaseException() is CliConnectionException)
                    throw new CliConnectionException("cli-output-too-large");
                throw new CliConnectionException(ct.IsCancellationRequested ? "cli-cancelled" : "cli-timeout-or-pipe-failure");
            }
            var output = new CliPrivateOutput(process.ExitCode, await stdout, await stderr);
            if (versionProbe && Encoding.UTF8.GetString(output.Stdout.Span).Trim() != manifest.ExpectedVersionOutput.Trim())
            { output.Dispose(); throw new CliConnectionException("cli-version-mismatch"); }
            return output;
        }
        catch (CliConnectionException) { Stop(process); throw; }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or ArgumentException)
        { Stop(process); throw new CliConnectionException("cli-process-unavailable"); }
    }

    private static async Task<byte[]> CaptureAsync(Stream pipe, CancellationToken ct)
    {
        using var output = new MemoryStream();
        var bytes = new byte[8192]; int count;
        while ((count = await pipe.ReadAsync(bytes, ct)) != 0)
        { if (output.Length + count > MaximumOutputBytes) throw new CliConnectionException("cli-output-too-large"); await output.WriteAsync(bytes.AsMemory(0, count), ct); }
        return output.ToArray();
    }
    private static void Stop(Process process)
    { try { if (!process.HasExited) { process.Kill(entireProcessTree: true); _ = process.WaitForExit(3000); } } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { } }

    public static bool ReconcileOwnedProcess(CliProcessIdentity identity)
    {
        try
        {
            using var process = Process.GetProcessById(identity.ProcessId);
            if (process.HasExited || new DateTimeOffset(process.StartTime.ToUniversalTime()) != identity.StartedAt
                || process.MainModule?.FileName != identity.Executable) return false;
            Stop(process);
            return process.WaitForExit(3000);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }
}
