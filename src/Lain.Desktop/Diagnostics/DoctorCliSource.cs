using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Lain.Desktop.Diagnostics;

public sealed record DoctorSourceResult(string? Json = null, string? ErrorCode = null);
public interface IDoctorReportSource
{
    Task<DoctorSourceResult> ReadAsync(DoctorTarget target, CancellationToken ct = default);
}

/// <summary>Runs the trusted installed CLI with fixed read-only arguments. Never retains or returns stderr.</summary>
public sealed class DoctorCliSource : IDoctorReportSource
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private readonly string _executablePath;
    public DoctorCliSource(string executablePath)
    {
        if (!Path.IsPathFullyQualified(executablePath)) throw new ArgumentException("An absolute trusted CLI path is required.", nameof(executablePath));
        _executablePath = executablePath;
    }

    public static string[] Arguments(DoctorTarget target) => target switch
    {
        DoctorTarget.Service => ["doctor", "--json"],
        DoctorTarget.CodexCli => ["doctor", "--target", "codex-cli", "--json"],
        DoctorTarget.CodexVscode => ["doctor", "--target", "codex-vscode", "--json"],
        _ => throw new ArgumentOutOfRangeException(nameof(target))
    };

    public async Task<DoctorSourceResult> ReadAsync(DoctorTarget target, CancellationToken ct = default)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Timeout);
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(_executablePath)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory
        };
        foreach (var argument in Arguments(target)) process.StartInfo.ArgumentList.Add(argument);
        var outputExceeded = 0;
        async Task<string?> Read(Stream stream, int limit, bool retain)
        {
            try { return await ReadBounded(stream, limit, retain, budget.Token); }
            catch (OutputLimitException)
            { Interlocked.Exchange(ref outputExceeded, 1); Kill(process); budget.Cancel(); throw; }
        }
        try
        {
            ct.ThrowIfCancellationRequested();
            if (!process.Start()) return new(ErrorCode: "doctor_cli_unavailable");
            var stdout = Read(process.StandardOutput.BaseStream, DoctorPresentation.MaximumOutputBytes, true);
            var stderr = Read(process.StandardError.BaseStream, 64 * 1024, false);
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(budget.Token));
            return process.ExitCode == 0 ? new(stdout.Result) : new(ErrorCode: "doctor_cli_failed");
        }
        catch (OperationCanceledException)
        { Kill(process); return new(ErrorCode: outputExceeded != 0 ? "doctor_output_limit" : ct.IsCancellationRequested ? "doctor_cancelled" : "doctor_timeout"); }
        catch (OutputLimitException)
        { Kill(process); return new(ErrorCode: "doctor_output_limit"); }
        catch (Exception e) when (e is Win32Exception or IOException or InvalidOperationException or DecoderFallbackException)
        { Kill(process); return new(ErrorCode: "doctor_cli_unavailable"); }
    }

    private static async Task<string?> ReadBounded(Stream stream, int limit, bool retain, CancellationToken ct)
    {
        using var bytes = retain ? new MemoryStream() : null;
        var buffer = new byte[4096];
        var total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), ct);
            if (read == 0) break;
            total += read;
            if (total > limit) throw new OutputLimitException();
            if (retain) bytes!.Write(buffer, 0, read);
        }
        return retain ? new UTF8Encoding(false, true).GetString(bytes!.ToArray()) : null;
    }
    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException) { }
    }
    private sealed class OutputLimitException : IOException { }
}
