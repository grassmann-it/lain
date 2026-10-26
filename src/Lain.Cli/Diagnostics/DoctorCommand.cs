using System.Text.Json;
using Lain.Client;

namespace Lain.Cli.Diagnostics;

public static class DoctorCommand
{
    public const string Help = "Usage: lain doctor [--target codex-cli|codex-vscode] [--project DIR] [--profile NAME] [--json]\nProfile is the native Codex profile name. Read-only local diagnosis; no repair, login, refresh, inference or client startup.";
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error,
        Func<LainApiClient>? clientFactory = null, CancellationToken ct = default)
    {
        if (args is ["--help"] or ["help"]) { await output.WriteLineAsync(Help); return 0; }
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var json = false;
        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (key == "--json" && !json) { json = true; continue; }
            if (key is not ("--target" or "--project" or "--profile") || values.ContainsKey(key) ||
                i + 1 == args.Length || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                return await Usage(error);
            values[key] = args[++i];
        }
        var target = values.GetValueOrDefault("--target");
        if (target is not (null or "codex-cli" or "codex-vscode") ||
            (target is null && (values.ContainsKey("--project") || values.ContainsKey("--profile")))) return await Usage(error);
        try
        {
            var request = new DoctorRequest(target, values.GetValueOrDefault("--project"), values.GetValueOrDefault("--profile"));
            var report = await new DoctorEngine(new DoctorEnvironment(clientFactory)).DiagnoseAsync(request, ct);
            if (json) await output.WriteLineAsync(JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            else foreach (var finding in report.Findings)
                await output.WriteLineAsync($"{finding.State}: {finding.Code}: {finding.Explanation} [{finding.Source}] Next: {finding.NextStep}");
            // A completed diagnosis is successful even when its findings are blocked.
            return 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return 3; }
        catch (Exception) { await error.WriteLineAsync("lain doctor: diagnosis unavailable; no changes made."); return 3; }
    }
    private static async Task<int> Usage(TextWriter error) { await error.WriteLineAsync(Help); return 2; }
}
