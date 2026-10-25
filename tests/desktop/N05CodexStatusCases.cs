using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Lain.Desktop;

internal sealed partial class Scenario
{
    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int N05CreateFifo([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);

    private async Task RunN05CodexStatusInputChecks(string scratch)
    {
        if (!OperatingSystem.IsLinux()) return;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var read = typeof(MainWindow).GetMethod("ReadCodexStatusFile", flags)!;
        var match = typeof(MainWindow).GetMethod("CodexReceiptMatchesTarget", flags)!;
        var regular = Path.Combine(scratch, "regular-status.json");
        var link = Path.Combine(scratch, "linked-status.json");
        var fifo = Path.Combine(scratch, "fifo-status.json");
        bool Rejected(string path)
        {
            try { read.Invoke(null, [path]); return false; }
            catch (TargetInvocationException error) when (error.InnerException is IOException) { return true; }
        }
        try
        {
            await File.WriteAllTextAsync(regular, "{\"test\":true}");
            Check(Encoding.UTF8.GetString((byte[])read.Invoke(null, [regular])!) == "{\"test\":true}",
                "N05 descriptor status reader accepts bounded regular files");
            File.CreateSymbolicLink(link, regular);
            Check(Rejected(link) && Rejected(scratch), "N05 status reader refuses symbolic links and directories before reading");
            if (N05CreateFifo(fifo, 384) != 0) throw new IOException("Could not create the private FIFO fixture.");
            Check(await Task.Run(() => Rejected(fifo)).WaitAsync(TimeSpan.FromSeconds(2)),
                "N05 FIFO status input is refused without blocking or executing a checker");
            var syntheticHome = Path.Combine(scratch, "synthetic-home");
            var state = Path.Combine(syntheticHome, ".local/share/lain/clients/codex-profile-lain");
            var receipt = new Dictionary<string, object>
            {
                ["schemaVersion"] = 1, ["entry"] = Path.Combine(syntheticHome, ".local/bin/codex"), ["state"] = state,
                ["maintenance"] = Path.Combine(state, "maintenance.py"), ["grammarVersion"] = "codex-0.164.0"
            };
            bool Matches(Dictionary<string, object> value) => (bool)match.Invoke(null, [JsonSerializer.SerializeToElement(value), syntheticHome])!;
            Check(Matches(receipt), "N05 receipt matches only the explicit supported default target");
            foreach (var field in new[] { "schemaVersion", "entry", "state", "maintenance", "grammarVersion" })
            {
                var changed = new Dictionary<string, object>(receipt) { [field] = field == "schemaVersion" ? 2 : "/unrelated/target" };
                Check(!Matches(changed), "N05 receipt refuses changed " + field + " before checker execution");
            }
        }
        finally
        {
            foreach (var path in new[] { link, fifo, regular }) File.Delete(path);
        }
    }
}
