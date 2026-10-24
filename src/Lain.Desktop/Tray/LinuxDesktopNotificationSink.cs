using System.Diagnostics;
using System.Text;

namespace Lain.Desktop.Tray;

/// <summary>Best-effort native Linux notifications; never forwards command errors.</summary>
public sealed class LinuxDesktopNotificationSink : IDesktopNotificationSink
{
    private readonly IDesktopCommandRunner _runner;
    private readonly string _iconPath;

    public LinuxDesktopNotificationSink() : this(new DesktopCommandRunner(),
        Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "lain-cat.png")) { }

    internal LinuxDesktopNotificationSink(IDesktopCommandRunner runner, string iconPath)
    {
        _runner = runner;
        _iconPath = iconPath;
    }

    public async Task<bool> SendAsync(DesktopAlert alert, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsLinux()) return false;
        // Callers supply deliberately composed, non-sensitive messages. Escaping
        // ensures their plaintext cannot become links or notification markup.
        var result = await _runner.RunAsync("/usr/bin/notify-send", [
            "--app-name", "lain", "--icon", _iconPath,
            "--hint", "string:desktop-entry:lain", "--urgency", "normal",
            "--expire-time", "8000", "--", EscapePlainText(alert.Title, 160),
            EscapePlainText(alert.Body, 1024)
        ], TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }

    internal static string EscapePlainText(string value, int limit)
    {
        var plain = new StringBuilder();
        foreach (var rune in value.EnumerateRunes())
        {
            if (plain.Length + rune.Utf16SequenceLength > limit) break;
            if (Rune.IsControl(rune) && rune.Value is not ('\n' or '\t')) continue;
            plain.Append(rune.ToString());
        }
        return plain.ToString().Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal).Replace("'", "&apos;", StringComparison.Ordinal);
    }
}

internal sealed record DesktopCommandResult(bool Succeeded, string Output);

internal interface IDesktopCommandRunner
{
    Task<DesktopCommandResult> RunAsync(string executable, IReadOnlyList<string> arguments,
        TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>No shell, bounded captured output, and a deadline for every process.</summary>
internal sealed class DesktopCommandRunner : IDesktopCommandRunner
{
    private const int OutputLimit = 16384;

    public async Task<DesktopCommandResult> RunAsync(string executable, IReadOnlyList<string> arguments,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        Task<(string Text, bool Truncated)>? output = null;
        Task<(string Text, bool Truncated)>? errors = null;
        try
        {
            if (!process.Start()) return new(false, "");
            output = DrainAsync(process.StandardOutput, OutputLimit, deadline.Token);
            // Drain stderr without retaining provider, bus or desktop error text.
            errors = DrainAsync(process.StandardError, 0, deadline.Token);
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            var captured = await output.ConfigureAwait(false);
            await errors.ConfigureAwait(false);
            return new(process.ExitCode == 0 && !captured.Truncated, captured.Text);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(false, ""); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        { return new(false, ""); }
        finally
        {
            deadline.Cancel();
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
            if (output is not null && errors is not null)
            {
                try { await Task.WhenAll(output, errors).ConfigureAwait(false); }
                catch (Exception error) when (error is OperationCanceledException or IOException or ObjectDisposedException) { }
            }
        }
    }

    private static async Task<(string Text, bool Truncated)> DrainAsync(StreamReader reader, int limit, CancellationToken token)
    {
        var text = new StringBuilder();
        var buffer = new char[1024];
        var truncated = false;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
        {
            var keep = Math.Min(read, limit - text.Length);
            if (keep > 0) text.Append(buffer, 0, keep);
            truncated |= keep < read;
        }
        return (text.ToString(), truncated);
    }
}
