namespace Lain.Desktop.Tray;

/// <summary>Bounded, plain-text desktop information; never provider credentials or payloads.</summary>
public sealed record DesktopAlert(string Id, string Title, string Body);

public sealed record TrayStatusSnapshot(string Tooltip, string[] Lines, DesktopAlert[] Alerts, bool Connected);

public interface IDesktopNotificationSink
{
    Task<bool> SendAsync(DesktopAlert alert, CancellationToken cancellationToken = default);
}
