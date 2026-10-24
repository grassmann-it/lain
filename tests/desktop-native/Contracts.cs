// The adapter tests compile without Avalonia and never create a desktop session.
namespace Lain.Desktop.Tray;
public sealed record DesktopAlert(string Id, string Title, string Body);
public interface IDesktopNotificationSink
{
    Task<bool> SendAsync(DesktopAlert alert, CancellationToken cancellationToken);
}
