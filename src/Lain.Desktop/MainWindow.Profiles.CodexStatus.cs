using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    // Linux statx has an architecture-independent 256-byte ABI. Inspect the
    // opened descriptor, not a path whose type might change before the read.
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct CodexStatusStatx
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(40)] public ulong Size;
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int CodexStatusOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int CodexStatusFileType(int fd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags, uint mask, out CodexStatusStatx info);

    private static byte[] ReadCodexStatusFile(string path)
    {
        if (!OperatingSystem.IsLinux()) throw new IOException("Dispatcher status requires Linux.");
        for (FileSystemInfo? item = new FileInfo(path); item is not null;
            item = item is FileInfo file ? file.Directory : ((DirectoryInfo)item).Parent)
            if (item.LinkTarget is not null) throw new IOException("Client status paths must not be symbolic links.");
        // O_NONBLOCK prevents FIFO open from waiting for a writer. O_NOFOLLOW
        // rejects a final symlink, even if changed after the parent path check.
        var fd = CodexStatusOpen(path, 0x800 | 0x20000 | 0x80000);
        if (fd < 0) throw new IOException("The client status file cannot be opened safely.");
        using var handle = new SafeFileHandle((IntPtr)fd, ownsHandle: true);
        const uint required = 0x201; // STATX_TYPE | STATX_SIZE
        if (CodexStatusFileType(fd, "", 0x1000 | 0x100, required, out var info) != 0
            || (info.Mask & required) != required || (info.Mode & 0xf000) != 0x8000 || info.Size > 1024 * 1024)
            throw new IOException("Client status requires a regular file within its size limit.");
        using var stream = new FileStream(handle, FileAccess.Read);
        var bytes = new byte[(int)info.Size];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new IOException("Client status file changed while reading.");
        return bytes;
    }

    private static bool CodexReceiptMatchesTarget(JsonElement receipt, string home)
    {
        string? Text(string key) => receipt.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
        if (receipt.ValueKind != JsonValueKind.Object || !receipt.TryGetProperty("schemaVersion", out var schema)
            || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version) || version != 1) return false;
        var state = Path.Combine(home, ".local", "share", "lain", "clients", "codex-profile-lain");
        return Text("entry") == Path.Combine(home, ".local", "bin", "codex") && Text("state") == state
            && Text("maintenance") == Path.Combine(state, "maintenance.py") && Text("grammarVersion") == "codex-0.164.0";
    }
}
