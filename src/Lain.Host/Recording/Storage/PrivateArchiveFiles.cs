using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Lain.Host.Recording.Storage;

internal static class PrivateArchiveFiles
{
    private const UnixFileMode DirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PermissionMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public static void Directory(string path)
    {
        RejectLinks(path);
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Recording archive protection requires Linux.");
        System.IO.Directory.CreateDirectory(path, DirectoryMode);
        File.SetUnixFileMode(path, DirectoryMode);
        if (File.GetUnixFileMode(path) != DirectoryMode) throw new IOException("Recording directory protection failed.");
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("Recording archive protection currently requires Linux x64.");
        var fd = NativeOpen(path, 0x10000 | 0x80000 | 0x20000);
        if (fd < 0) throw new IOException("Recording private directory open failed.");
        try { if (Fstat(fd, out var status) != 0 || status.Uid != GetUid()) throw new IOException("Recording directories must be user-owned."); }
        finally { Close(fd); }
        RejectLinks(path);
    }

    public static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            var entry = new FileInfo(current);
            if (entry.LinkTarget is not null || (entry.Exists && (entry.Attributes & FileAttributes.ReparsePoint) != 0))
                throw new IOException("Recording paths cannot contain symbolic links.");
        }
    }

    public static FileStream Open(string path, FileMode mode, FileAccess access, FileShare share = FileShare.Read)
    {
        RejectLinks(path);
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("Recording archive protection currently requires Linux x64.");
        var flags = 0x80000 | 0x20000 | 0x800; // O_CLOEXEC | O_NOFOLLOW | O_NONBLOCK
        flags |= access == FileAccess.Read ? 0 : access == FileAccess.Write ? 1 : 2;
        flags |= mode switch {
            FileMode.CreateNew => 0x40 | 0x80,
            FileMode.OpenOrCreate => 0x40,
            FileMode.Open => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        var fd = NativeOpenCreate(path, flags, 0x180); // 0600 at creation, independent of inherited umask
        if (fd < 0) throw new IOException("Recording private file open failed.");
        var handle = new SafeFileHandle((IntPtr)fd, true);
        try {
            if (Fstat(fd, out var status) != 0 || (status.Mode & 0xf000) != 0x8000 || status.Uid != GetUid())
                throw new IOException("Recording files must be regular and user-owned.");
            if (Fchmod(fd, 0x180) != 0) throw new IOException("Recording file protection failed.");
            if (share == FileShare.None && Flock(fd, 2 | 4) != 0) throw new IOException("Recording archive already has a writer.");
            return new FileStream(handle, access, 4096, false);
        }
        catch { handle.Dispose(); throw; }
    }

    public static void AtomicWrite(string path, ReadOnlySpan<byte> bytes)
    {
        var temporary = path + ".new";
        if (File.Exists(temporary)) { RejectLinks(temporary); File.Delete(temporary); }
        using (var stream = Open(temporary, System.IO.FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
        File.Move(temporary, path, true);
        SyncDirectory(Path.GetDirectoryName(path)!);
    }

    public static void SyncDirectory(string path)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Recording directory sync requires Linux.");
        var fd = NativeOpen(path, 0x10000 | 0x80000 | 0x20000); // O_DIRECTORY | O_CLOEXEC
        if (fd < 0) throw new IOException("Recording directory sync failed.");
        try { if (Fsync(fd) != 0) throw new IOException("Recording directory sync failed."); }
        finally { Close(fd); }
    }

    [StructLayout(LayoutKind.Explicit, Size = 144)]
    private struct LinuxStat { [FieldOffset(24)] public uint Mode; [FieldOffset(28)] public uint Uid; }
    [DllImport("libc", EntryPoint = "open", CharSet = CharSet.Ansi)] private static extern int NativeOpenCreate(string path, int flags, uint mode);
    [DllImport("libc", EntryPoint = "fstat")] private static extern int Fstat(int fd, out LinuxStat status);
    [DllImport("libc", EntryPoint = "fchmod")] private static extern int Fchmod(int fd, uint mode);
    [DllImport("libc", EntryPoint = "flock")] private static extern int Flock(int fd, int operation);
    [DllImport("libc", EntryPoint = "getuid")] private static extern uint GetUid();
    [DllImport("libc", EntryPoint = "open", CharSet = CharSet.Ansi)] private static extern int NativeOpen(string path, int flags);
    [DllImport("libc", EntryPoint = "fsync")] private static extern int Fsync(int fd);
    [DllImport("libc", EntryPoint = "close")] private static extern int Close(int fd);
}
