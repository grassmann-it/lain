using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

/// <summary>XTEST sends real X11 input events, confined to the runner-owned display.</summary>
internal sealed class NativeInput : IDisposable
{
    public static Action<string>? Trace;
    private readonly nint display;
    public NativeInput()
    {
        AssertPrivateDisplay();
        display = XOpenDisplay(Environment.GetEnvironmentVariable("LAIN_TEST_DISPLAY"));
        if (display == 0) throw new InvalidOperationException("Cannot open private Xvfb for native input");
        if (XTestQueryExtension(display, out _, out _, out _, out _) == 0)
            throw new InvalidOperationException("XTEST unavailable on private Xvfb");
    }

    public static void AssertPrivateDisplay()
    {
        var pid = Environment.GetEnvironmentVariable("LAIN_TEST_XVFB_PID");
        var owned = Environment.GetEnvironmentVariable("LAIN_TEST_DISPLAY");
        if (!int.TryParse(pid, out _) || string.IsNullOrWhiteSpace(owned) ||
            Environment.GetEnvironmentVariable("DISPLAY") != owned ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) ||
            !PrivateSessionBus() ||
            !File.Exists($"/proc/{pid}/cmdline") ||
            Path.GetFileName(File.ReadAllText($"/proc/{pid}/cmdline").Split('\0')[0]) != "Xvfb")
            throw new InvalidOperationException("Refusing GUI/input: runner-owned private Xvfb required");
    }

    private static bool PrivateSessionBus()
    {
        var pid = Environment.GetEnvironmentVariable("LAIN_TEST_DBUS_PID");
        var address = Environment.GetEnvironmentVariable("LAIN_TEST_SESSION_BUS");
        return int.TryParse(pid, out _) && address?.StartsWith("unix:path=/", StringComparison.Ordinal) == true
            && Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") == address
            && File.Exists($"/proc/{pid}/cmdline")
            && Path.GetFileName(File.ReadAllText($"/proc/{pid}/cmdline").Split('\0')[0]) == "dbus-daemon";
    }

    public nint FocusedWindow()
    {
        AssertPrivateDisplay();
        XGetInputFocus(display, out var focused, out _);
        return focused;
    }

    public void Focus(Window window)
    {
        AssertPrivateDisplay();
        var handle = window.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("No native window");
        Trace?.Invoke("focus-window");
        XSetInputFocus(display, handle, 1, 0);
        XFlush(display);
    }

    public void Click(Control control, uint button = 1)
    {
        AssertPrivateDisplay();
        var point = control.PointToScreen(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2));
        Trace?.Invoke($"pointer-button-{button} at {point.X},{point.Y}");
        XTestFakeMotionEvent(display, -1, point.X, point.Y, 0);
        XTestFakeButtonEvent(display, button, 1, 0);
        XTestFakeButtonEvent(display, button, 0, 0);
        XFlush(display);
    }

    public void Key(nuint symbol, nuint modifier = 0)
    {
        AssertPrivateDisplay();
        Trace?.Invoke($"key-{symbol:x} modifier-{modifier:x}");
        var key = XKeysymToKeycode(display, symbol);
        if (key == 0) throw new InvalidOperationException($"No keycode for keysym {symbol:x}");
        var mod = modifier == 0 ? (byte)0 : XKeysymToKeycode(display, modifier);
        if (mod != 0) XTestFakeKeyEvent(display, mod, 1, 0);
        XTestFakeKeyEvent(display, key, 1, 0);
        XTestFakeKeyEvent(display, key, 0, 0);
        if (mod != 0) XTestFakeKeyEvent(display, mod, 0, 0);
        XFlush(display);
    }
    public void Type(string value)
    {
        const string shifted = "!@#$%^&*()_+{}|:\"<>?";
        const string plain = "1234567890-=[]\\;\",./";
        foreach (var letter in value)
        {
            if (char.IsAsciiLetterUpper(letter)) Key(char.ToLowerInvariant(letter), 0xffe1);
            else if (shifted.IndexOf(letter) is var index && index >= 0) Key(plain[index], 0xffe1);
            else Key(letter);
        }
    }
    public void Dispose() { if (display != 0) XCloseDisplay(display); }

    [DllImport("libX11.so.6")] private static extern nint XOpenDisplay(string? name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(nint display);
    [DllImport("libX11.so.6")] private static extern int XFlush(nint display);
    [DllImport("libX11.so.6")] private static extern int XGetInputFocus(nint display, out nint focus, out int revertTo);
    [DllImport("libX11.so.6")] private static extern int XSetInputFocus(nint display, nint focus, int revertTo, nuint time);
    [DllImport("libX11.so.6")] private static extern byte XKeysymToKeycode(nint display, nuint symbol);
    [DllImport("libXtst.so.6")] private static extern int XTestQueryExtension(nint display, out int eventBase, out int errorBase, out int major, out int minor);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeMotionEvent(nint display, int screen, int x, int y, nuint delay);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeButtonEvent(nint display, uint button, int pressed, nuint delay);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeKeyEvent(nint display, uint keycode, int pressed, nuint delay);
}
