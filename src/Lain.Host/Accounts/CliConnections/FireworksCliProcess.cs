using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Lain.Host.Accounts.CliConnections;

public enum FireworksCommand { Version, Signin, Accounts, CreateKey }
public interface IFireworksCliProcess
{
    Task<CliPrivateOutput> RunAsync(FireworksCommand command, string directory, string? accountId, string? keyName,
        TimeSpan timeout, CancellationToken ct, Action<CliProcessIdentity>? started = null);
    string? AuthorizationUrl(string directory);
}

/// <summary>Provider-local Linux mount namespace. HOME's value is inherited unchanged; its filesystem is private.</summary>
public sealed class FireworksCliProcess(FireworksCliQualification qualification) : IFireworksCliProcess
{
    public static IReadOnlyList<string> Arguments(FireworksCommand command, string? accountId = null, string? keyName = null)
    {
        if (accountId is not null) FireworksCliResult.Identifier(accountId);
        return command switch
        {
            FireworksCommand.Version when accountId is null && keyName is null => ["version"],
            FireworksCommand.Signin when keyName is null => accountId is null ? ["signin"] : ["signin", accountId],
            FireworksCommand.Accounts when accountId is null && keyName is null => ["account", "list", "-o", "json"],
            FireworksCommand.CreateKey when accountId is not null && keyName is not null && FireworksCliResult.Identifier(keyName) == keyName
                => ["api-key", "create", "--account-id", accountId, "--key-name", keyName, "-o", "json"],
            _ => throw new CliConnectionException("invalid-fireworks-command")
        };
    }
    public async Task<CliPrivateOutput> RunAsync(FireworksCommand command, string directory, string? accountId, string? keyName,
        TimeSpan timeout, CancellationToken ct, Action<CliProcessIdentity>? started = null)
    {
        qualification.Validate(); ct.ThrowIfCancellationRequested();
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(10)) throw new CliConnectionException("invalid-fireworks-timeout");
        var arguments = Arguments(command, accountId, keyName);
        var home = Environment.GetEnvironmentVariable("HOME");
        // Qualification covers a direct /home/<user> layout, never symlinked or relocated HOME values.
        if (home is null || !Path.IsPathFullyQualified(home) || Path.GetDirectoryName(home) != "/home" || !Directory.Exists(home))
            throw new CliConnectionException("unqualified-fireworks-home-layout");
        CliPrivatePaths.RefuseLinks(home); CliPrivatePaths.Directory(directory);
        var privateHome = Path.Combine(directory, "home"); CliPrivatePaths.Directory(privateHome);
        if (command == FireworksCommand.Version && Directory.EnumerateFileSystemEntries(privateHome).Any())
            throw new CliConnectionException("fireworks-login-requires-fresh-private-home");
        var tools = Path.Combine(directory, "tools"); CliPrivatePaths.Directory(tools);
        var shim = Path.Combine(tools, "xdg-open");
        CliPrivatePaths.RefuseLinks(shim);
        File.WriteAllText(shim, BrowserCapture);
        File.SetUnixFileMode(shim, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var info = new ProcessStartInfo("/usr/bin/bwrap") { UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = directory, CreateNoWindow = true };
        List<string> plan = ["--unshare-all", "--die-with-parent", "--new-session"];
        if (command != FireworksCommand.Version) plan.Add("--share-net");
        plan.AddRange(["--ro-bind", "/", "/", "--tmpfs", "/opt", "--dir", "/opt/lain-fireworks",
            "--ro-bind", qualification.ExecutablePath, "/opt/lain-fireworks/firectl", "--ro-bind", tools, "/opt/lain-fireworks/bin",
            "--proc", "/proc", "--dev", "/dev", "--tmpfs", "/run", "--dir", "/run/lain-fireworks",
            "--bind", directory, "/run/lain-fireworks", "--tmpfs", "/home", "--dir", home, "--bind", privateHome, home,
            "--tmpfs", "/root", "--tmpfs", "/tmp", "--tmpfs", "/var/tmp", "--tmpfs", "/mnt", "--chdir", home,
            "/opt/lain-fireworks/firectl"]);
        plan.AddRange(arguments); foreach (var arg in plan) info.ArgumentList.Add(arg);
        info.Environment.Clear(); info.Environment["HOME"] = home; // same value, private mount; no HOME rewrite
        info.Environment["LANG"] = "C.UTF-8"; info.Environment["PATH"] = "/opt/lain-fireworks/bin:/usr/bin:/bin";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(timeout);
        using var process = new Process { StartInfo = info };
        try
        {
            if (!process.Start()) throw new CliConnectionException("fireworks-start-failed");
            process.StandardInput.Close(); started?.Invoke(new(process.Id, new(process.StartTime.ToUniversalTime()), "/usr/bin/bwrap"));
            // Each command runs once; exclusive output files preserve ambiguous creation results for recovery.
            var label = command.ToString().ToLowerInvariant();
            var stdout = Capture(process.StandardOutput.BaseStream, Path.Combine(directory, label + ".stdout"), deadline.Token);
            var stderr = Capture(process.StandardError.BaseStream, Path.Combine(directory, label + ".stderr"), deadline.Token);
            _ = stdout.ContinueWith(t => { if (t.IsFaulted) deadline.Cancel(); }, TaskScheduler.Default);
            _ = stderr.ContinueWith(t => { if (t.IsFaulted) deadline.Cancel(); }, TaskScheduler.Default);
            try { await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(deadline.Token)); }
            catch
            {
                Stop(process);
                try { await Task.WhenAll(stdout, stderr, process.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
                throw new CliConnectionException(ct.IsCancellationRequested ? "fireworks-cancelled-review-private-result" : "fireworks-timeout-or-output-failure");
            }
            return new(process.ExitCode, await stdout, await stderr);
        }
        catch { Stop(process); throw; }
    }
    private static async Task<byte[]> Capture(Stream input, string path, CancellationToken ct)
    {
        CliPrivatePaths.RefuseLinks(path);
        await using var output = new FileStream(path, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
        using var memory = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = await input.ReadAsync(buffer, ct)) != 0)
        {
            if (memory.Length + count > OfficialCliProcess.MaximumOutputBytes) throw new CliConnectionException("fireworks-output-too-large");
            await output.WriteAsync(buffer.AsMemory(0, count), ct); await output.FlushAsync(ct);
            memory.Write(buffer, 0, count);
        }
        return memory.ToArray();
    }
    private static void Stop(Process process)
    { try { if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(3000); } } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { } }
    public string? AuthorizationUrl(string directory)
    {
        var path = Path.Combine(directory, "authorization-url.json");
        if (!File.Exists(path)) return null;
        using var json = JsonDocument.Parse(CliPrivatePaths.Read(path, 16384));
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("url", out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Fireworks browser capture is malformed.");
        return FireworksCliResult.AuthorizationUrl(value.GetString()!);
    }
    // The official github.com/pkg/browser Linux provider invokes xdg-open first.
    // Only capture its original URL; never create state, PKCE, redirect, code or token.
    private const string BrowserCapture = """
#!/usr/bin/python3
import json,os,sys
from urllib.parse import urlsplit
assert len(sys.argv)==2
u=sys.argv[1]; p=urlsplit(u)
assert len(u)<=8192 and not any(ord(c)<32 for c in u)
assert p.scheme=='https' and p.username is None and p.password is None and p.port in (None,443) and not p.fragment
assert p.hostname and p.hostname.startswith('fireworks-') and p.hostname.endswith('.auth.us-west-2.amazoncognito.com')
assert p.path in ('/oauth2/authorize','/login','/logout')
os.umask(0o077)
t='/run/lain-fireworks/.authorization-url.'+str(os.getpid())
f=os.open(t,os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o600)
with os.fdopen(f,'w') as o: json.dump({'url':u},o);o.flush();os.fsync(o.fileno())
os.link(t,'/run/lain-fireworks/authorization-url.json');os.unlink(t)
""";
}
