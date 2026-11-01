using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lain.Core;

/// <summary>A durable maintenance admission refusal or a currently held admission gate.</summary>
public sealed class LifecycleMaintenanceException(string code, string message) : IOException(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Cross-process startup admission, shared with packaging/lifecycle_coordination.py.
/// The OS lock is held through a local operation or Host initial writes, never for
/// the Host lifetime. Maintenance writes a durable marker before stopping writers.
/// </summary>
public static class LifecycleCoordination
{
    // Feature detection only. Package manifests and source qualification remain
    // the integrity proof; old programs do not participate in this protocol.
    public const string Protocol = "LainLifecycleCoordinationV1";
    public const string LockFileName = ".lifecycle.lock";
    public const string MaintenanceFileName = ".lifecycle-maintenance.json";
    public const string IdentityFileName = ".lifecycle.identity";
    private static readonly UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static string ResolveStateRoot(string? stateRoot = null) => Path.GetFullPath(stateRoot ??
        Environment.GetEnvironmentVariable("LAIN_HOME") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lain"));

    /// <summary>
    /// Hold for local state writes. Release before a service-manager start:
    /// the new Host must acquire its own admission before touching owned state.
    /// </summary>
    public static IDisposable AcquireOperation(string? stateRoot = null) => Acquire(stateRoot, hostStartup: false);

    /// <summary>
    /// Call before OwnStatePaths. Hold through host.lock and bounded initial Core
    /// startup, then dispose. During maintenance only the pinned post-swap Host
    /// may enter; no environment variable bypass is accepted.
    /// </summary>
    public static IDisposable AcquireHostStartup(string? stateRoot = null) => Acquire(stateRoot, hostStartup: true);

    private static IDisposable Acquire(string? selectedRoot, bool hostStartup)
    {
        var root = ResolveStateRoot(selectedRoot);
        RefuseLinks(root);
        if (!Directory.Exists(root))
        {
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(root);
            else Directory.CreateDirectory(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        RefuseLinks(root);
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(root) &
            (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
            throw new IOException("Lifecycle state must be protected from other writers.");
        var lease = Gate.Open(Path.Combine(root, LockFileName));
        try
        {
            var marker = Path.Combine(root, MaintenanceFileName);
            if (File.Exists(marker))
            {
                if (!hostStartup || !AllowsHostRestart(root, marker))
                    throw new LifecycleMaintenanceException("maintenance-active", "Installation maintenance requires completion or explicit recovery.");
            }
            else ReadOrCreateIdentity(root);
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    private static bool AllowsHostRestart(string root, string markerPath)
    {
        try
        {
            using var document = JsonDocument.Parse(ReadPrivate(markerPath));
            var value = document.RootElement;
            if (value.GetProperty("format").GetInt32() != 1 || value.GetProperty("protocol").GetString() != Protocol ||
                !string.Equals(value.GetProperty("stateRoot").GetString(), root, PathComparison) ||
                value.GetProperty("stateIdentity").GetString() != ReadIdentity(root) ||
                value.GetProperty("phase").GetString() != "host-restart") return false;
            var host = value.GetProperty("host");
            var expected = host.GetProperty("executablePath").GetString();
            var running = Environment.ProcessPath;
            if (running is null || expected is null || !Path.IsPathFullyQualified(expected) ||
                !string.Equals(Path.GetFullPath(running), expected, PathComparison)) return false;
            RefuseLinks(expected);
            var files = host.GetProperty("files");
            if (files.ValueKind != JsonValueKind.Object) return false;
            var hasExecutable = false; var hasHost = false; var hasCore = false;
            foreach (var file in files.EnumerateObject())
            {
                if (!Path.IsPathFullyQualified(file.Name)) return false;
                RefuseLinks(file.Name);
                var checksum = file.Value.GetString();
                if (checksum is null || checksum.Length != 64 || checksum.Any(character => !char.IsAsciiHexDigit(character))) return false;
                using var stream = File.OpenRead(file.Name);
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(stream)), checksum, StringComparison.OrdinalIgnoreCase)) return false;
                hasExecutable |= string.Equals(file.Name, expected, PathComparison);
                hasHost |= string.Equals(file.Name, Path.Combine(AppContext.BaseDirectory, "Lain.Host.dll"), PathComparison);
                hasCore |= string.Equals(file.Name, typeof(LifecycleCoordination).Assembly.Location, PathComparison);
            }
            if (!hasExecutable || !hasHost || !hasCore) return false;
            // Hash the running Linux image too: its path may now name replacement
            // bytes, while /proc/self/exe still names the actual executing inode.
            if (OperatingSystem.IsLinux())
            {
                using var executing = File.OpenRead("/proc/self/exe");
                if (!string.Equals(Convert.ToHexString(SHA256.HashData(executing)),
                    files.GetProperty(expected).GetString(), StringComparison.OrdinalIgnoreCase)) return false;
            }
            // Consume the narrowly pinned admission while still holding the
            // gate. A failed replacement cannot leave an unlimited restart
            // permission behind after its updater exits.
            var consumed = JsonNode.Parse(value.GetRawText())!.AsObject();
            consumed["phase"] = "host-admitted";
            consumed["host"] = null;
            WritePrivate(markerPath, consumed.ToJsonString());
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or
            InvalidOperationException or KeyNotFoundException or ArgumentException)
        { return false; }
    }

    private static string ReadOrCreateIdentity(string root)
    {
        var path = Path.Combine(root, IdentityFileName);
        if (!File.Exists(path))
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.Read };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = PrivateFile;
            using var stream = new FileStream(path, options);
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant()) + "\n");
            stream.Write(bytes); stream.Flush(flushToDisk: true);
        }
        return ReadIdentity(root);
    }

    private static string ReadIdentity(string root)
    {
        var identity = JsonSerializer.Deserialize<string>(ReadPrivate(Path.Combine(root, IdentityFileName)));
        if (identity is null || identity.Length != 64 || identity.Any(character => !char.IsAsciiHexDigit(character)))
            throw new IOException("Invalid lifecycle state identity; explicit recovery required.");
        return identity;
    }

    private static string ReadPrivate(string path)
    {
        RefuseLinks(path);
        if (!OperatingSystem.IsWindows() && File.GetUnixFileMode(path) != PrivateFile)
            throw new IOException("Lifecycle metadata must be private.");
        var info = new FileInfo(path);
        if (info.Length > 128 * 1024) throw new IOException("Lifecycle metadata exceeds the allowed size.");
        return File.ReadAllText(path);
    }

    private static void WritePrivate(string path, string value)
    {
        RefuseLinks(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = PrivateFile;
            using (var stream = new FileStream(temporary, options))
            {
                stream.Write(Encoding.UTF8.GetBytes(value + "\n"));
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void RefuseLinks(string path)
    {
        for (FileSystemInfo? current = new FileInfo(Path.GetFullPath(path)); current is not null;
            current = current is DirectoryInfo directory ? directory.Parent : ((FileInfo)current).Directory)
            if (current.LinkTarget is not null || current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Lifecycle paths cannot contain symbolic links.");
    }

    private sealed class Gate(FileStream stream) : IDisposable
    {
        private int disposed;
        public static Gate Open(string path)
        {
            RefuseLinks(path);
            var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.ReadWrite };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = PrivateFile;
            var stream = new FileStream(path, options);
            try
            {
                RefuseLinks(path);
                if (!OperatingSystem.IsWindows() && File.GetUnixFileMode(path) != PrivateFile)
                    throw new IOException("Lifecycle lock must be private.");
                if (OperatingSystem.IsWindows()) stream.Lock(0, 1);
                else if (Flock(stream.SafeFileHandle.DangerousGetHandle().ToInt32(), 2 | 4) != 0)
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error is 11 or 35) throw new LifecycleMaintenanceException("lifecycle-busy", "Another startup or maintenance admission is in progress.");
                    throw new IOException("Lifecycle admission lock failed.", new Win32Exception(error));
                }
                return new(stream);
            }
            catch (IOException error) when (OperatingSystem.IsWindows() && error is not LifecycleMaintenanceException)
            { stream.Dispose(); throw new LifecycleMaintenanceException("lifecycle-busy", "Another startup or maintenance admission is in progress."); }
            catch { stream.Dispose(); throw; }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            try
            {
                if (OperatingSystem.IsWindows()) stream.Unlock(0, 1);
                else Flock(stream.SafeFileHandle.DangerousGetHandle().ToInt32(), 8);
            }
            finally { stream.Dispose(); }
        }
        [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
        private static extern int Flock(int descriptor, int operation);
    }
}
