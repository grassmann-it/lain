using Lain.Desktop.Tray;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    checks++;
}

Check(TrayAvailability.IsRegisteredHost("(<true>,)\n"), "true boolean variant accepted");
Check(!TrayAvailability.IsRegisteredHost("(<false>,)"), "unregistered host rejected");
Check(!TrayAvailability.IsRegisteredHost("error: true"), "error text cannot prove a host");
Check(!TrayAvailability.IsRegisteredHost("('true',)"), "string is not a boolean host property");
Check(TrayAvailability.IsOwnedBy("(uint32 1234,)\n", 1234), "native process ID accepted");
Check(!TrayAvailability.IsOwnedBy("(uint32 1235,)", 1234), "other process rejected");
Check(!TrayAvailability.IsOwnedBy("error1234", 1234), "arbitrary process output rejected");
Check(TrayAvailability.RegisteredServices("(<['org.kde.StatusNotifierItem-1234-0/StatusNotifierItem', ':1.24/StatusNotifierItem']>,)")
    .SequenceEqual(new[] { "org.kde.StatusNotifierItem-1234-0", ":1.24" }), "named and unique registrations parsed");
Check(TrayAvailability.RegisteredServices("(<@as []>,)").Length == 0, "empty typed registration array accepted as empty");
Check(TrayAvailability.RegisteredServices("(<[':1.24/UnexpectedPath']>,)").Length == 0, "unrelated object path rejected");
Check(TrayAvailability.RegisteredServices("(<[':1.24/StatusNotifierItem', invalid]>,)").Length == 0, "malformed array rejected in full");

var noHost = new FakeRunner(new DesktopCommandResult(false, ""));
Check(!await TrayAvailability.ProbeAsync(noHost, 1234, true, default) && noHost.Calls.Count == 1, "unavailable watcher cannot hide a window");
var falseHost = new FakeRunner(new DesktopCommandResult(true, "(<false>,)"));
Check(!await TrayAvailability.ProbeAsync(falseHost, 1234, true, default) && falseHost.Calls.Count == 1, "watcher without a host cannot hide a window");
var noSession = new FakeRunner();
Check(!await TrayAvailability.ProbeAsync(noSession, 1234, false, default) && noSession.Calls.Count == 0, "missing session never launches a bus command");
var noIcon = new FakeRunner(new DesktopCommandResult(true, "(<true>,)"), new DesktopCommandResult(true, "(<@as []>,)"));
Check(!await TrayAvailability.ProbeAsync(noIcon, 1234, true, default), "host alone does not prove an actual tray icon");
var ownIcon = new FakeRunner(new DesktopCommandResult(true, "(<true>,)"), new DesktopCommandResult(true, "(<[':1.24/StatusNotifierItem', ':1.25/StatusNotifierItem']>,)"),
    new DesktopCommandResult(true, "(uint32 9999,)"), new DesktopCommandResult(true, "(uint32 1234,)"));
Check(await TrayAvailability.ProbeAsync(ownIcon, 1234, true, default), "host and registered icon owned by current process prove availability");
Check(ownIcon.Calls.Count == 4 && ownIcon.Calls.All(x => x.Executable == "/usr/bin/gdbus" && x.Arguments.Contains("--session")), "all availability observations use explicit sessionbus arguments");

var sent = new FakeRunner(new DesktopCommandResult(true, ""));
var sink = new LinuxDesktopNotificationSink(sent, "/fixture/original cat.png");
Check(await sink.SendAsync(new("safe-id", "lain <ready>", "<a href='file'>state & quota</a>\0\u001b"), default), "native success returned");
var command = sent.Calls.Single();
Check(command.Executable == "/usr/bin/notify-send" && command.Arguments.Contains("/fixture/original cat.png"), "own artwork passed as one icon argument");
Check(command.Arguments.Contains("string:desktop-entry:lain") && command.Arguments.Contains("lain"), "application identity supplied");
Check(command.Arguments[^2] == "lain &lt;ready&gt;" && command.Arguments[^1] == "&lt;a href=&apos;file&apos;&gt;state &amp; quota&lt;/a&gt;", "content is escaped plaintext without control sequences");
Check(command.Arguments[^3] == "--", "notification text cannot become command options");
Check(LinuxDesktopNotificationSink.EscapePlainText("😀😀", 3) == "😀", "length limit preserves Unicode scalar boundaries");
Check(!await new LinuxDesktopNotificationSink(new FakeRunner(new DesktopCommandResult(false, "raw error deliberately discarded")), "/fixture/cat.png")
    .SendAsync(new("id", "Title", "Body"), default), "notification failure stays a boolean");

// These subprocess checks have no DBus or desktop access and never call
// notify-send/gdbus. They verify the adapter's actual process boundary.
var runner = new DesktopCommandRunner();
var literal = await runner.RunAsync("/usr/bin/printf", new[] { "%s", "$(unexpanded); <literal>" }, TimeSpan.FromSeconds(1), default);
Check(literal.Succeeded && literal.Output == "$(unexpanded); <literal>", "arguments are literal without a shell");
var bounded = await runner.RunAsync("/usr/bin/printf", new[] { "%020000d", "0" }, TimeSpan.FromSeconds(1), default);
Check(!bounded.Succeeded && bounded.Output.Length == 16384, "excess output is drained but bounded and rejected");
Check(!(await runner.RunAsync("/nonexistent/lain-fixture", [], TimeSpan.FromSeconds(1), default)).Succeeded, "missing executable is a safe failure");
Check(!(await runner.RunAsync("/usr/bin/sleep", new[] { "10" }, TimeSpan.FromMilliseconds(50), default)).Succeeded, "timeout terminates helper");
using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
var cancellationObserved = false;
try { await runner.RunAsync("/usr/bin/sleep", new[] { "10" }, TimeSpan.FromSeconds(2), cancelled.Token); }
catch (OperationCanceledException) { cancellationObserved = true; }
Check(cancellationObserved, "caller cancellation propagates while helper is terminated");
Console.WriteLine($"{checks} desktop-native checks passed; fake DBus/notification transport only.");

internal sealed class FakeRunner(params DesktopCommandResult[] replies) : IDesktopCommandRunner
{
    private readonly Queue<DesktopCommandResult> _replies = new(replies);
    public List<(string Executable, string[] Arguments)> Calls { get; } = [];
    public Task<DesktopCommandResult> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add((executable, arguments.ToArray()));
        return Task.FromResult(_replies.Dequeue());
    }
}
