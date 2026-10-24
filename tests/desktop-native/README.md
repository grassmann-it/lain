# Native Linux adapter boundary

Run `dotnet run --project tests/desktop-native/Lain.Desktop.Native.Tests.csproj`
in the integrator's build slot. No real notification, desktop, sessionbus or
provider calls occur: notification and availability transports are fakes. The
actual bounded subprocess runner is checked only with printf/sleep and a missing
fixture executable. No display is needed.

Availability requires the KDE watcher to report a registered host **and** an
actual registered StatusNotifierItem whose bus connection belongs to the current
process. Unsupported backends, asynchronous registration still pending, absent
sessionbus, malformed output, command failure and timeout all leave the window
reachable. There is deliberately no speculative XEmbed fallback.

Native behavior was checked against Avalonia 12.1.4's exact NuGet source commit
`2d79ba55fae1b1e71c69f93a668080c9029e6a31`:

- [TrayIcon.cs](https://github.com/AvaloniaUI/Avalonia/blob/2d79ba55fae1b1e71c69f93a668080c9029e6a31/src/Avalonia.Controls/TrayIcon.cs): attachment creates the native implementation; there is no public support/registration property.
- [X11Platform.cs](https://github.com/AvaloniaUI/Avalonia/blob/2d79ba55fae1b1e71c69f93a668080c9029e6a31/src/Avalonia.X11/X11Platform.cs): selects DBus or XEmbed implementation.
- [DBusTrayIconImpl.cs](https://github.com/AvaloniaUI/Avalonia/blob/2d79ba55fae1b1e71c69f93a668080c9029e6a31/src/Avalonia.FreeDesktop/DBusTrayIconImpl.cs): `IsActive` only establishes a DBus connection; registration is asynchronous, exports `/StatusNotifierItem`, and uses process-specific service names.
- [Watcher interface](https://github.com/AvaloniaUI/Avalonia/blob/2d79ba55fae1b1e71c69f93a668080c9029e6a31/src/Avalonia.FreeDesktop/DBusXml/org.kde.StatusNotifierWatcher.xml): host boolean and registered service array.

The notification adapter uses `/usr/bin/notify-send` without a shell, escapes
bounded plaintext, identifies the desktop entry as `lain`, and references the
approved contrast-adjusted cat photo at `Assets/Brand/lain-cat.png` beside the application.
Callers must compose non-sensitive messages; command errors are never forwarded.
