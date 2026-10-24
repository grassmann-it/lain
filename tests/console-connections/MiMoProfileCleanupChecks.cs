using System.Diagnostics;
using System.Text.Json;
using Lain.Host.Accounts.ConsoleConnections.MiMo;
using Lain.Host.Operations;

internal static class MiMoProfileCleanupChecks
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "lain-mimo-cleanup-fixture-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new OwnStatePaths(root);
            await using var browser = new MiMoConsoleBrowser(paths);
            var profiles = Path.Combine(paths.Credentials, "console", "mimo");
            string Profile(string name, string? receipt)
            {
                var directory = Path.Combine(profiles, name);
                OwnStatePaths.EnsurePrivateDirectory(directory);
                OwnStatePaths.WritePrivate(Path.Combine(directory, "synthetic-cookie-state"), "synthetic-cookie-fixture");
                if (receipt is not null) OwnStatePaths.WritePrivate(Path.Combine(directory, "lain-process.json"), receipt);
                return directory;
            }
            var legacy = Profile(new string('a', 32), "{\"pid\":2147483647,\"startedAt\":1}");
            var stale = Profile(new string('b', 32), JsonSerializer.Serialize(new {
                kind = "lain-mimo-console-profile", pid = int.MaxValue, startedAt = 1, hostPid = int.MaxValue, hostStartedAt = 1 }));
            // Deliberately mismatched process start time must never kill the test process.
            var reusedPid = Profile(new string('c', 32), JsonSerializer.Serialize(new { pid = Environment.ProcessId, startedAt = 1 }));
            using var host = Process.GetCurrentProcess();
            var recovery = Profile(new string('d', 32), JsonSerializer.Serialize(new {
                kind = "lain-mimo-console-profile", pid = int.MaxValue, startedAt = 1, hostPid = host.Id, hostStartedAt = host.StartTime.ToUniversalTime().Ticks }));
            var unknown = Profile(new string('e', 32), null);
            var nonRandom = Profile("personal-browser", "{\"pid\":2147483647,\"startedAt\":1}");
            var invalid = Profile(new string('f', 32), "{\"kind\":\"some-other-owner\",\"pid\":2147483647,\"startedAt\":1}");
            var large = Profile(new string('1', 32), new string('x', 4097));
            string? external = null;
            string? linkedProfile = null;
            string? linkedReceipt = null;
            if (!OperatingSystem.IsWindows())
            {
                external = Path.Combine(root, "external-synthetic-browser");
                OwnStatePaths.EnsurePrivateDirectory(external);
                OwnStatePaths.WritePrivate(Path.Combine(external, "sentinel"), "untouched");
                linkedProfile = Path.Combine(profiles, new string('2', 32));
                Directory.CreateSymbolicLink(linkedProfile, external);
                linkedReceipt = Profile(new string('3', 32), null);
                File.CreateSymbolicLink(Path.Combine(linkedReceipt, "lain-process.json"), Path.Combine(external, "sentinel"));
            }
            var result = await browser.ReconcileAsync();
            Check(result.Removed == 3 && !result.Truncated, "only stale known-owned receipts are removed");
            Check(!Directory.Exists(legacy) && !Directory.Exists(stale) && !Directory.Exists(reusedPid), "stale profile cookie state is erased without killing a reused PID");
            Check(Directory.Exists(recovery) && File.Exists(Path.Combine(recovery, "synthetic-cookie-state")), "live owner host retains an adoptable recovery session");
            Check(Directory.Exists(unknown) && Directory.Exists(nonRandom) && Directory.Exists(invalid) && Directory.Exists(large), "unowned and invalid receipts are preserved");
            Check(result.Failed == (OperatingSystem.IsWindows() ? 2 : 4), "invalid/oversize/symlink candidates fail closed");
            if (external is not null)
            {
                Check(File.ReadAllText(Path.Combine(external, "sentinel")) == "untouched", "cleanup never traverses an external profile or receipt link");
                Directory.Delete(linkedProfile!); File.Delete(Path.Combine(linkedReceipt!, "lain-process.json"));
            }
            var boundedPaths = new OwnStatePaths(Path.Combine(root, "bounded"));
            await using var boundedBrowser = new MiMoConsoleBrowser(boundedPaths);
            var boundedProfiles = Path.Combine(boundedPaths.Credentials, "console", "mimo");
            for (var index = 0; index <= MiMoConsoleBrowser.MaximumReconciliationProfiles; index++)
                OwnStatePaths.EnsurePrivateDirectory(Path.Combine(boundedProfiles, index.ToString("x32")));
            var bounded = await boundedBrowser.ReconcileAsync();
            Check(bounded.Examined == MiMoConsoleBrowser.MaximumReconciliationProfiles && bounded.Truncated && bounded.Removed == 0,
                "startup reconciliation bounds enumeration/work and reports remaining profiles");
            Console.WriteLine("PASS MiMo synthetic profile receipts: stale cleanup, PID reuse, live recovery preservation, directory/receipt symlink guards and bounded startup work");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); }
}
