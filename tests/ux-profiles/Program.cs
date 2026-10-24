using System.Net;
using System.Net.Http.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lain.Client;
using Lain.Core;
using Lain.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var pid = Environment.GetEnvironmentVariable("LAIN_TEST_XVFB_PID");
        if (!int.TryParse(pid, out _) || Environment.GetEnvironmentVariable("DISPLAY") != Environment.GetEnvironmentVariable("LAIN_TEST_DISPLAY") ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) || !File.Exists($"/proc/{pid}/cmdline") ||
            Path.GetFileName(File.ReadAllText($"/proc/{pid}/cmdline").Split('\0')[0]) != "Xvfb") return 2;
        return AppBuilder.Configure<ProfileTestApp>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime(args);
    }
}
internal sealed class ProfileTestApp : Application
{
    public override void Initialize()
    {
        var production = new App(); production.Initialize(); RequestedThemeVariant = production.RequestedThemeVariant;
        // Detach the production dictionary before attaching it to this fixture app.
        var resources = production.Resources;
        production.Resources = new ResourceDictionary();
        Resources = resources;
        var styles = production.Styles.ToArray(); production.Styles.Clear(); foreach (var style in styles) Styles.Add(style);
    }
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Dispatcher.UIThread.Post(async () =>
        {
            try { await Scenario.Run(lifetime); lifetime.Shutdown(0); }
            catch (Exception error) { Console.Error.WriteLine(error); lifetime.Shutdown(1); }
        });
        base.OnFrameworkInitializationCompleted();
    }
}
internal static class Scenario
{
    private static int checks;
    private static string Output => Environment.GetEnvironmentVariable("LAIN_TEST_OUTPUT")!;
    public static async Task Run(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        using var handler = new ProfileFixture();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://fixture.invalid") };
        var window = new MainWindow(new LainApiClient(http), Path.Combine(Output, "preferences.json"));
        lifetime.MainWindow = window; window.Show();
        await Wait(() => handler.Data.StatusCalls > 0);
        await Click(window, "Profiles");
        Check(Text(window).Contains("Viewing Daily") && Text(window).Contains("Workspace 2"), "viewing Daily and applied Workspace2 remain distinct");
        Check(handler.ApplyRequests == 0, "viewing does not apply");
        Check(Controls<Button>(window).Count(b => (AutomationProperties.GetName(b) ?? "").StartsWith("Open model access")) == 5, "five complete linked model rows");
        Capture(window, "profiles-wide.png");
        var trigger = Find<Button>(window, "Apply to proxy…"); trigger.Focus();
        await Click(window, "Apply to proxy…");
        var modal = await Modal(lifetime);
        await Wait(() => Find<Button>(modal, "Apply to proxy").IsEnabled);
        Check(Text(modal).Contains("GLOBAL") && Text(modal).Contains("fixture-stream-behavior"), "modal shows actual host scope and stream behavior");
        Capture(modal, "apply-wide.png");
        await Click(modal, "Cancel");
        Check(handler.ApplyRequests == 0 && ReferenceEquals(window.FocusManager?.GetFocusedElement(), trigger), "cancel keeps runtime unchanged and restores trigger focus");

        await Click(window, "Apply to proxy…"); modal = await Modal(lifetime);
        await Wait(() => Find<Button>(modal, "Apply to proxy").IsEnabled);
        var previews = handler.Previews;
        handler.Data.Config = handler.Data.Config with { Revision = handler.Data.Config.Revision + 1 };
        await Click(modal, "Apply to proxy");
        await Wait(() => handler.Previews > previews && Find<Button>(modal, "Apply to proxy").IsEnabled);
        Check(handler.ApplyRequests == 0 && Text(modal).Contains("confirm again"), "stale preview refreshes and requires a second explicit confirmation");
        handler.ApplyGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await Click(modal, "Apply to proxy");
        await Wait(() => handler.ApplyRequests == 1);
        Find<Button>(modal, "Apply to proxy").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(handler.ApplyRequests == 1, "double confirmation submits exactly one apply");
        handler.ApplyGate.SetResult();
        await Wait(() => !lifetime.Windows.Contains(modal));
        Check(handler.Data.ActiveProfileId == "p1" && Text(window).Contains("Daily"), "only confirmed host identity completes apply");

        window.Width = 800; window.Height = 560; await Task.Delay(180);
        await Click(window, "Select profile Daily");
        await Click(window, "Back to profiles");
        var list = Find<ScrollViewer>(window, "Profile inventory");
        list.Offset = new Vector(0, 20);
        await Click(window, "Select profile Workspace 3");
        Check(Find<Button>(window, "Back to profiles").IsEffectivelyVisible, "narrow detail has local back instead of cramped columns");
        var detail = Find<ScrollViewer>(window, "Profile details"); detail.Offset = new Vector(0, 110); await Task.Delay(60);
        var before = detail.Offset.Y;
        await Click(window, "Back to profiles"); await Click(window, "Select profile Workspace 3");
        Check(Math.Abs(Find<ScrollViewer>(window, "Profile details").Offset.Y - before) < 2, "narrow return retains per-profile scroll");
        Find<ScrollViewer>(window, "Profile details").Offset = default; await Task.Delay(60);
        Capture(window, "profiles-narrow.png");
        await Click(window, "Apply to proxy…"); modal = await Modal(lifetime);
        await Wait(() => Find<Button>(modal, "Apply to proxy").IsEnabled);
        Check(Find<Button>(modal, "Apply to proxy").TranslatePoint(default, modal) is { } point && point.Y < modal.Bounds.Height - 20,
            "narrow modal actions remain inside its viewport");
        Capture(modal, "apply-narrow.png");
        await Click(modal, "Cancel");

        await Click(window, "Edit selection");
        Find<TextBox>(window, "Profile name").Text = "Draft preserved";
        Find<Expander>(window, "Profile fallback policy").IsExpanded = true;
        await Task.Delay(80);
        Find<CheckBox>(window, "Profile fallback r2").IsChecked = true;
        Find<CheckBox>(window, "Allow paid profile fallback").IsChecked = true;
        await Click(window, "Move profile fallback r2 up");
        await Click(window, "Models"); await Click(window, "Profiles");
        Check(Find<TextBox>(window, "Profile name").Text == "Draft preserved" && Find<CheckBox>(window, "Profile fallback r2").IsChecked == true,
            "navigation retains unsaved profile and policy fields");
        var applyCount = handler.ApplyRequests;
        await Click(window, "Save profile"); await Wait(() => handler.LastSaved is not null);
        Check(handler.LastSaved!.Policy is { AllowPaidFallback: true } policy && policy.FallbackRouteIds!.Contains("r2") && handler.ApplyRequests == applyCount,
            "complete fallback policy is saved without implicit apply");
        // Save completion can remount the narrow page. Navigate using the current
        // saved name, rather than assuming the previous detail controls survived.
        await Wait(() => handler.Data.Config.Profiles.Any(profile => profile.Id == handler.LastSaved!.Id && profile.Name == "Draft preserved"));
        await Wait(() => Controls<Button>(window).Any(button => button.IsEffectivelyVisible && Equals(button.Content, "Back to profiles")) ||
            Controls<Button>(window).Any(button => button.IsEffectivelyVisible && AutomationProperties.GetName(button) == "Select profile Draft preserved"));
        if (Controls<Button>(window).Any(button => button.IsEffectivelyVisible && Equals(button.Content, "Back to profiles")))
            await Click(window, "Back to profiles");
        await Click(window, "Select profile Draft preserved");
        await Click(window, "Set up Codex client…");
        var executable = Path.Combine(Output, "fixture-lain"); await File.WriteAllTextAsync(executable, "fixture");
        var target = Path.Combine(Output, "client-export"); Directory.CreateDirectory(target);
        Find<TextBox>(window, "lain command-line executable").Text = executable;
        Find<TextBox>(window, "Destination parent folder").Text = target;
        await Click(window, "Prepare Codex files"); await Wait(() => Text(window).Contains("No files installed yet"));
        Check(handler.Data.ExportCalls == 1 && !Directory.EnumerateFileSystemEntries(target).Any(), "preparation exports in memory without installation or apply");
        await Click(window, "Install client files"); await Wait(() => Text(window).Contains("Codex files installed"));
        Check(Directory.GetFiles(target, "current.json", SearchOption.AllDirectories).Length == 1 && handler.ApplyRequests == applyCount,
            "explicit installation uses generation-safe installer and leaves runtime unchanged");
        Capture(window, "client-installed-narrow.png");
        window.Close();
        Console.WriteLine($"UXD05: {checks} native synthetic checks passed; no providers or installed user files.");
    }
    private static IEnumerable<T> Controls<T>(Visual root) where T : Control => root.GetVisualDescendants().OfType<T>();
    private static T Find<T>(Visual root, string name) where T : Control => Controls<T>(root).First(control =>
        AutomationProperties.GetName(control) == name || control is Button button && (Equals(button.Content, name) || Equals(button.Tag, name)));
    private static string Text(Visual root) => string.Join("\n", Controls<TextBlock>(root).Select(t => t.Text));
    private static async Task Click(Visual root, string name) { Find<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(120); }
    private static async Task Wait(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition()) { if (DateTime.UtcNow > until) throw new TimeoutException("Fixture condition timed out"); await Task.Delay(30); }
    }
    private static async Task<Window> Modal(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        await Wait(() => lifetime.Windows.Any(window => window.Title == "Review profile changes"));
        return lifetime.Windows.Single(window => window.Title == "Review profile changes");
    }
    private static void Capture(Window window, string name)
    {
        window.UpdateLayout();
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96));
        bitmap.Render(window); bitmap.Save(Path.Combine(Output, name));
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
}
internal sealed class ProfileFixture : HttpMessageHandler
{
    public Fixture Data { get; } = new();
    private readonly HttpMessageInvoker fallback;
    public int Previews, ApplyRequests;
    public TaskCompletionSource? ApplyGate;
    public ProfileDto? LastSaved;
    public ProfileFixture()
    {
        Data.Config = Data.Config with { Profiles = Data.Config.Profiles.Select(p => p with { Policy = null }).ToArray() };
        fallback = new(Data, false);
    }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/preview", StringComparison.Ordinal))
        {
            Previews++; var profile = path.Split('/')[4];
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new ProfileApplyPreviewDto(1, profile, Data.Config.Revision,
                Data.ActiveProfileId, Data.ActiveProfileRevision, [], ["r2"], [], ["a1"], ["all gateway clients"],
                "fixture-stream-behavior: drain then restart; timeout refuses apply", true, "r1", "r2", true)) };
        }
        if (path.EndsWith("/apply", StringComparison.Ordinal))
        {
            ApplyRequests++;
            if (ApplyGate is not null) await ApplyGate.Task.WaitAsync(ct);
        }
        var response = await fallback.SendAsync(request, ct);
        // The shared fixture owns request deserialization. Reading its content here
        // first consumes the same stream and can make a successful save impossible.
        if (request.Method == HttpMethod.Put && path.StartsWith("/api/v1/profiles/", StringComparison.Ordinal) && response.IsSuccessStatusCode)
            LastSaved = Data.Config.Profiles.Single(profile => profile.Id == path.Split('/')[4]);
        return response;
    }
    protected override void Dispose(bool disposing) { if (disposing) { fallback.Dispose(); Data.Dispose(); } base.Dispose(disposing); }
}
