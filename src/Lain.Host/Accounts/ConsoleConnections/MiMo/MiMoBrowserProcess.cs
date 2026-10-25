using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("MiMo.BrowserStartup.Tests")]

namespace Lain.Host.Accounts.ConsoleConnections.MiMo;

public enum MiMoBrowserStartReason
{
    BrowserUnavailable, DesktopUnavailable, ProcessLaunchFailed, DisplayUnavailable, SandboxUnavailable,
    BrowserLibraryUnavailable, OwnedProfileLocked, ProcessExited, EndpointTimeout, EndpointInvalid,
    OwnedStateUnavailable, ProcessReceiptUnavailable, StartupInterrupted, CleanupIncomplete, StartupRejected
}

/// <summary>Only fixed local startup reasons and numeric status; never browser output or profile paths.</summary>
public sealed class MiMoConsoleBrowserStartException : InvalidOperationException
{
    public MiMoBrowserStartReason Reason { get; }
    public string ReasonCode => CodeFor(Reason);
    public string PublicMessage => Message;
    public int? ExitCode { get; }
    public int? NativeErrorCode { get; }

    internal MiMoConsoleBrowserStartException(MiMoBrowserStartReason reason, int? exitCode = null, int? nativeErrorCode = null)
        : base(MessageFor(reason) + $" [mimo-browser:{CodeFor(reason)}" + (exitCode is { } exit ? $"; exit={exit}" : "")
            + (nativeErrorCode is { } native ? $"; os={native}" : "") + "]")
    { Reason = Enum.IsDefined(reason) ? reason : MiMoBrowserStartReason.StartupRejected; ExitCode = exitCode; NativeErrorCode = nativeErrorCode; }

    private static string CodeFor(MiMoBrowserStartReason reason) => reason switch
    {
        MiMoBrowserStartReason.BrowserUnavailable => "browser-unavailable",
        MiMoBrowserStartReason.DesktopUnavailable => "desktop-unavailable",
        MiMoBrowserStartReason.ProcessLaunchFailed => "process-launch-failed",
        MiMoBrowserStartReason.DisplayUnavailable => "display-unavailable",
        MiMoBrowserStartReason.SandboxUnavailable => "sandbox-unavailable",
        MiMoBrowserStartReason.BrowserLibraryUnavailable => "browser-library-unavailable",
        MiMoBrowserStartReason.OwnedProfileLocked => "owned-profile-locked",
        MiMoBrowserStartReason.ProcessExited => "process-exited",
        MiMoBrowserStartReason.EndpointTimeout => "endpoint-timeout",
        MiMoBrowserStartReason.EndpointInvalid => "endpoint-invalid",
        MiMoBrowserStartReason.OwnedStateUnavailable => "owned-state-unavailable",
        MiMoBrowserStartReason.ProcessReceiptUnavailable => "process-receipt-unavailable",
        MiMoBrowserStartReason.StartupInterrupted => "startup-interrupted",
        MiMoBrowserStartReason.CleanupIncomplete => "cleanup-incomplete",
        _ => "startup-rejected"
    };
    private static string MessageFor(MiMoBrowserStartReason reason) => reason switch
    {
        MiMoBrowserStartReason.BrowserUnavailable => "Install a supported Chromium browser to connect MiMo monitoring.",
        MiMoBrowserStartReason.DesktopUnavailable => "Open lain in a desktop session to connect MiMo monitoring.",
        MiMoBrowserStartReason.ProcessLaunchFailed => "The MiMo browser process could not be launched. Check the local browser installation.",
        MiMoBrowserStartReason.DisplayUnavailable => "The MiMo browser reported an unavailable display. Check the desktop session and retry monitoring.",
        MiMoBrowserStartReason.SandboxUnavailable => "The MiMo browser reported an unavailable sandbox. Check the supported browser installation; lain does not disable its sandbox.",
        MiMoBrowserStartReason.BrowserLibraryUnavailable => "The MiMo browser reported a missing runtime library. Check the local browser installation.",
        MiMoBrowserStartReason.OwnedProfileLocked => "The MiMo browser reported that its new private profile could not be locked. Retry monitoring.",
        MiMoBrowserStartReason.ProcessExited => "The MiMo browser exited before its local endpoint was ready. The underlying cause was not identified.",
        MiMoBrowserStartReason.EndpointTimeout => "The MiMo browser did not publish its local endpoint within 15 seconds. Check local browser startup and retry monitoring.",
        MiMoBrowserStartReason.EndpointInvalid => "The MiMo browser published an invalid local endpoint. Reconnect monitoring.",
        MiMoBrowserStartReason.OwnedStateUnavailable => "The MiMo browser's private startup state could not be prepared or read. Check local state permissions and retry monitoring.",
        MiMoBrowserStartReason.ProcessReceiptUnavailable => "The MiMo browser's owned process receipt could not be established. Retry monitoring.",
        MiMoBrowserStartReason.StartupInterrupted => "MiMo browser startup was interrupted because its owned session closed. Reconnect monitoring.",
        MiMoBrowserStartReason.CleanupIncomplete => "MiMo browser startup failed and its owned process or profile could not be fully cleaned up. Retry local cleanup before reconnecting monitoring.",
        _ => "MiMo browser startup was rejected. The underlying cause was not identified."
    };
}

internal interface IMiMoBrowserProcess : IDisposable
{
    int Id { get; }
    DateTime StartTime { get; }
    bool HasExited { get; }
    int ExitCode { get; }
    TextReader StandardOutput { get; }
    TextReader StandardError { get; }
    void Kill(bool entireProcessTree);
    Task WaitForExitAsync(CancellationToken ct);
}

internal interface IMiMoBrowserRuntime
{
    string? Executable { get; }
    bool HasDesktop { get; }
    IMiMoBrowserProcess Start(ProcessStartInfo start);
}

internal sealed class MiMoBrowserRuntime : IMiMoBrowserRuntime
{
    public string? Executable => new[] { "/usr/bin/chromium", "/usr/bin/chromium-browser", "/usr/bin/google-chrome", "/opt/google/chrome/chrome" }.FirstOrDefault(File.Exists);
    public bool HasDesktop => OperatingSystem.IsLinux() &&
        (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")));
    public IMiMoBrowserProcess Start(ProcessStartInfo start) => new OwnedProcess(Process.Start(start)
        ?? throw new Win32Exception(0));

    private sealed class OwnedProcess(Process process) : IMiMoBrowserProcess
    {
        public int Id => process.Id;
        public DateTime StartTime => process.StartTime;
        public bool HasExited => process.HasExited;
        public int ExitCode => process.ExitCode;
        public TextReader StandardOutput => process.StandardOutput;
        public TextReader StandardError => process.StandardError;
        public void Kill(bool entireProcessTree) => process.Kill(entireProcessTree);
        public Task WaitForExitAsync(CancellationToken ct) => process.WaitForExitAsync(ct);
        public void Dispose() => process.Dispose();
    }
}

internal sealed class MiMoBrowserStartupDiagnostic
{
    private const int MaximumCharacters = 16 * 1024;
    private readonly StringBuilder prefix = new();
    public void Append(ReadOnlySpan<char> text)
    {
        lock (prefix)
            if (prefix.Length < MaximumCharacters) prefix.Append(text[..Math.Min(text.Length, MaximumCharacters - prefix.Length)]);
    }
    public MiMoBrowserStartReason ExitReason()
    {
        lock (prefix)
        {
            var text = prefix.ToString();
            if (text.Contains("Missing X server or $DISPLAY", StringComparison.Ordinal)
                || text.Contains("cannot open display", StringComparison.OrdinalIgnoreCase)
                || text.Contains("Failed to connect to Wayland display", StringComparison.Ordinal)) return MiMoBrowserStartReason.DisplayUnavailable;
            if (text.Contains("No usable sandbox!", StringComparison.Ordinal)
                || text.Contains("Running as root without --no-sandbox is not supported", StringComparison.Ordinal)) return MiMoBrowserStartReason.SandboxUnavailable;
            if (text.Contains("error while loading shared libraries:", StringComparison.Ordinal)) return MiMoBrowserStartReason.BrowserLibraryUnavailable;
            if (text.Contains("Failed to create a ProcessSingleton", StringComparison.Ordinal)
                || text.Contains("profile appears to be in use", StringComparison.OrdinalIgnoreCase)) return MiMoBrowserStartReason.OwnedProfileLocked;
            return MiMoBrowserStartReason.ProcessExited;
        }
    }
}
