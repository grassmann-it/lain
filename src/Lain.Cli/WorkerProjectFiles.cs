using System.Security.Cryptography;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32.SafeHandles;
using Lain.Core;

namespace Lain.Cli;

public sealed record WorkerTaskFile(int SchemaVersion, WorkerTask[] Tasks);

/// <summary>Project preferences are versionable; prompts, events and receipts live in private local state.</summary>
public static class WorkerProjectFiles
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true, PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static string ProjectRoot(string path)
    {
        var root = Path.GetFullPath(path);
        if (!Directory.Exists(root)) throw new ArgumentException("The worker project directory does not exist.");
        var resolved = Path.GetPathRoot(root)!;
        foreach (var component in root[resolved.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Path.Combine(resolved, component);
            resolved = new DirectoryInfo(next).ResolveLinkTarget(true)?.FullName ?? next;
        }
        return resolved;
    }

    public static string PolicyPath(string project) => Path.Combine(ProjectRoot(project), ".lain", "workers.json");

    public static string Initialize(string project, string profile)
    {
        if (string.IsNullOrWhiteSpace(profile) || profile.Length > 100 || profile.Any(char.IsControl))
            throw new ArgumentException("--profile requires an existing host profile ID.");
        var path = PolicyPath(project);
        var parent = Path.GetDirectoryName(path)!;
        AssertRegular(parent); Directory.CreateDirectory(parent); AssertRegular(path);
        var template = new WorkerProjectPolicy(1, profile, 2, [], "configured");
        using var file = new FileStream(path, PrivateOptions(FileMode.CreateNew));
        JsonSerializer.Serialize(file, template, Json);
        return path;
    }

    public static T Read<T>(string path)
    {
        AssertRegular(path);
        using var stream = OpenRead(path);
        if (stream.Length > 1024 * 1024) throw new ArgumentException("Worker files must not exceed 1 MiB.");
        try
        {
            using var document = JsonDocument.Parse(stream);
            UniqueProperties(document.RootElement);
            return document.RootElement.Deserialize<T>(Json) ?? throw new ArgumentException("Worker JSON must not be null.");
        }
        catch (JsonException) { throw new ArgumentException("Invalid worker JSON: use the versioned schema, exact field names and no unknown fields."); }
    }

    public static WorkerTask[] ReadTasks(string path)
    {
        var file = Read<WorkerTaskFile>(Path.GetFullPath(path));
        if (file.SchemaVersion != 1 || file.Tasks is null || file.Tasks.Length == 0)
            throw new ArgumentException("A task file needs schemaVersion 1 and at least one explicit task.");
        return file.Tasks;
    }

    public static string RunRoot(string project) => Path.Combine(LocalDiscoveryRoot(), "workers",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ProjectRoot(project)))).ToLowerInvariant());

    private static string LocalDiscoveryRoot() => Lain.Client.LocalDiscovery.StateDirectory;

    public static void PrivateDirectory(string path)
    {
        AssertRegular(path);
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public static FileStream CreatePrivate(string path, FileMode mode = FileMode.CreateNew)
    {
        AssertRegular(path);
        return new FileStream(path, PrivateOptions(mode));
    }

    public static void WritePrivate(string path, object value)
    {
        AssertRegular(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = CreatePrivate(temporary)) JsonSerializer.Serialize(file, value, Json);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void AssertRegular(string path)
    {
        if (new FileInfo(path).LinkTarget is not null ||
            (File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Worker files and state directories must not be symbolic links.");
        if (OperatingSystem.IsLinux())
        {
            if (LinuxStatPath(AtCurrentWorkingDirectory, path, AtSymlinkNoFollow, StatxType, out var status) == 0)
            {
                RequireType(status, allowDirectory: true);
            }
            else
            {
                var error = Marshal.GetLastPInvokeError();
                if (error != NoEntry) throw FileTypeError(error);
            }
        }
        for (var parentPath = Path.GetDirectoryName(Path.GetFullPath(path)); parentPath is not null; parentPath = Path.GetDirectoryName(parentPath))
        {
            var parent = new DirectoryInfo(parentPath);
            if (parent.LinkTarget is not null)
                throw new IOException("Worker file and state parents must not be symbolic links.");
        }
    }

    public static FileStream OpenRead(string path)
    {
        if (!OperatingSystem.IsLinux()) return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        // Path validation is advisory: the entry may be swapped immediately afterwards.
        // A FIFO swapped in here must not block open(), and neither links nor special
        // files may reach JSON parsing. Check the descriptor that will actually be read.
        var descriptor = LinuxOpen(path, OpenNonBlocking | OpenNoFollow | OpenCloseOnExec);
        if (descriptor < 0) throw FileTypeError(Marshal.GetLastPInvokeError());
        var handle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
        try
        {
            if (LinuxStatHandle(handle, "", AtEmptyPath, StatxType, out var status) != 0)
                throw FileTypeError(Marshal.GetLastPInvokeError());
            RequireType(status, allowDirectory: false);
            // FileStream takes ownership only after descriptor validation succeeds.
            return new FileStream(handle, FileAccess.Read);
        }
        catch { handle.Dispose(); throw; }
    }

    private static void RequireType(LinuxStatx status, bool allowDirectory)
    {
        var type = status.Mode & FileTypeMask;
        if ((status.Mask & StatxType) == 0 || type != RegularFile && (!allowDirectory || type != DirectoryFile))
            throw new IOException(allowDirectory
                ? "Worker paths must be regular files or directories, not links, FIFOs, sockets or devices."
                : "Worker JSON must be read from a regular file, not a directory, link, FIFO, socket or device.");
    }

    private static IOException FileTypeError(int error) => new("Worker file type or safe read access could not be verified.", new Win32Exception(error));

    private const int AtCurrentWorkingDirectory = -100;
    private const int AtSymlinkNoFollow = 0x100;
    private const int AtEmptyPath = 0x1000;
    private const int NoEntry = 2;
    private const uint StatxType = 0x1;
    private const int OpenNonBlocking = 0x800;
    private const int OpenNoFollow = 0x20000;
    private const int OpenCloseOnExec = 0x80000;
    private const int FileTypeMask = 0xf000;
    private const int RegularFile = 0x8000;
    private const int DirectoryFile = 0x4000;

    // Linux statx has a fixed 256-byte ABI on both 32- and 64-bit architectures.
    // Only the returned mask and mode are needed; never marshal libc's variable struct stat.
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxStatx
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(28)] public ushort Mode;
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int LinuxOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int LinuxStatPath(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags, uint mask, out LinuxStatx status);

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int LinuxStatHandle(SafeFileHandle directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags, uint mask, out LinuxStatx status);

    private static FileStreamOptions PrivateOptions(FileMode mode)
    {
        var options = new FileStreamOptions { Mode = mode, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return options;
    }

    private static void UniqueProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new ArgumentException("Duplicate worker JSON fields are ambiguous.");
                UniqueProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var element in value.EnumerateArray()) UniqueProperties(element);
    }
}
