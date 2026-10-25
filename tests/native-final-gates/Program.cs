using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lain.Client;
using Lain.Desktop;
using Lain.Desktop.Presentation;

internal static class Program
{
    [STAThread] public static int Main(string[] args)
    {
        NativeInput.AssertPrivateDisplay();
        return AppBuilder.Configure<GateApp>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime(args);
    }
}

internal sealed class GateApp : Application
{
    public override void Initialize()
    {
        var production = new App(); production.Initialize(); RequestedThemeVariant = production.RequestedThemeVariant;
        var resources = production.Resources; production.Resources = new ResourceDictionary(); Resources = resources;
        foreach (var style in production.Styles.ToArray()) { production.Styles.Remove(style); Styles.Add(style); }
    }
    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Dispatcher.UIThread.Post(async () =>
        {
            var gates = new Gates(lifetime);
            try { await gates.Run(); } catch (Exception error) { gates.Fatal(error); }
            await gates.Save(); lifetime.Shutdown(gates.Failed ? 1 : 0);
        });
        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class Gates(IClassicDesktopStyleApplicationLifetime lifetime)
{
    private readonly string output = Environment.GetEnvironmentVariable("LAIN_TEST_OUTPUT")!;
    private readonly List<object> checks = [], cases = [], images = [], focus = [], theme = [], requests = [], controls = [];
    private readonly List<string> nativeTrace = [];
    private MainWindow window = null!;
    private Fixture fixture = null!;
    private NativeInput input = null!;
    private string current = "startup";
    public bool Failed { get; private set; }
    public void Fatal(Exception error) { Failed = true; cases.Add(new { name = current, result = "error", error = error.GetType().Name, message = error.Message }); }
    private void Check(bool condition, string claim)
    {
        checks.Add(new { caseName = current, condition, claim });
        if (!condition) Failed = true;
        Console.WriteLine($"{(condition ? "PASS" : "FAIL")}: {current}: {claim}");
    }
    public async Task Run()
    {
        Directory.CreateDirectory(output);
        NativeInput.Trace = value => nativeTrace.Add(current + ": " + value);
        foreach (var page in new[] { "Models", "Profiles" })
        foreach (var size in new[] { (1120, 780), (800, 560) })
        {
            current = $"native-{page.ToLowerInvariant()}-editor-{size.Item1}x{size.Item2}";
            if (Environment.GetEnvironmentVariable("LAIN_NATIVE_GATES_ONLY") is { Length: > 0 } filter && !current.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
            var start = checks.Count;
            fixture = new Fixture(); fixture.UseUxdInventory(); fixture.Scenario = current;
            var prefs = Path.Combine(output, current + "-preferences.json");
            var store = new WorkspacePreferencesStore(prefs);
            store.Save(store.Current with { Level = InformationLevel.Advanced });
            using var http = new HttpClient(fixture) { BaseAddress = new Uri("http://fixture.invalid/api/v1/") };
            window = new MainWindow(new LainApiClient(http), prefs) { Width = size.Item1, Height = size.Item2 };
            window.Show(); using var native = new NativeInput(); input = native;
            try
            {
                await Wait(() => fixture.StatusCalls > 0 && FindOrNull<Button>("Open account details for Personal Codex") is not null
                    && FindOrNull<Button>(page) is { Bounds.Width: > 20, Bounds.Height: > 10 });
                window.UpdateLayout(); await Task.Delay(100);
                input.Focus(window); input.Click(Find<Button>(page));
                await Wait(() => page == "Models" ? FindOrNull<Button>("Edit model access gpt-lain-01") is not null
                    : FindOrNull<Button>("Select profile Daily") is not null || FindOrNull<Button>("Edit selection") is not null);
                if (page == "Models") await Model(); else await Profile();
                Check(fixture.ApplyCalls == 0 && fixture.ExportCalls == 0, "editor flow sends no Apply or client export");
                Check(fixture.Unexpected.Count == 0, "all requests remain in the synthetic fixture allowlist");
                cases.Add(new { name = current, result = checks.Skip(start).All(CheckPassed) ? "pass" : "fail", assertions = checks.Count - start });
            }
            catch (Exception error) { Fatal(error); Capture("failure"); }
            requests.AddRange(fixture.RequestDetails);
            foreach (var child in lifetime.Windows.Where(w => w != window).ToArray()) child.Close();
            window.Close(); await Task.Delay(60);
        }
    }
    private static bool CheckPassed(object item) => (bool)item.GetType().GetProperty("condition")!.GetValue(item)!;
    private async Task Model()
    {
        var original = fixture.Config.Routes.Single(r => r.Id == "r1");
        var revision = fixture.Config.Revision;
        await Activate(Find<Button>("Edit model access " + original.Alias));
        await Wait(() => FindOrNull<TextBox>("Client name") is not null);
        await Expand("Known model suggestions (optional)");
        await Traverse("model", Find<TextBox>("Client name"));
        await SetText(Find<TextBox>("Client name"), "cancelled-native-model");
        await Activate(Find<Button>("Cancel"));
        await Wait(() => FindOrNull<TextBox>("Client name") is null);
        Check(fixture.Config.Revision == revision && fixture.Config.Routes.Single(r => r.Id == "r1") == original, "Cancel preserves model identity, fields and saved revision");
        await ReturnFocus("Edit selected model access " + original.Alias, "model Cancel");
        await Activate(Find<Button>("Edit selected model access " + original.Alias));
        await Wait(() => FindOrNull<TextBox>("Client name") is not null);
        Check(Find<TextBox>("Client name").Text == original.Alias, "model Cancel discards draft on reopen");
        var spending = Find<ComboBox>("Spending policy");
        await TabTo(spending); input.Key(0xff54, 0xffe9); input.Key(0xff50); input.Key(0xff54); input.Key(0xff0d); await Task.Delay(120);
        Check(!Find<CheckBox>("Allow paid fallback routes").IsEnabled, "native free-only selection disables paid-fallback permission");
        // The disabled checkbox cannot receive Tab focus. Its following real
        // action scrolls the same production editor far enough to expose it.
        await TabTo(Find<Button>("Check spending policy")); await Task.Delay(60);
        var disabledPermission = Find<CheckBox>("Allow paid fallback routes");
        var disabledPoint = disabledPermission.TranslatePoint(default, window);
        Check(disabledPoint is { } dp && dp.Y >= 0 && dp.Y + disabledPermission.Bounds.Height <= window.Bounds.Height,
            "disabled paid-fallback control is fully inside the native viewport for measurement");
        MeasureTheme("model-free-only-disabled"); Capture("disabled");
        await TabTo(spending); input.Key(0xff54, 0xffe9); input.Key(0xff50); input.Key(0xff0d); await Task.Delay(100);
        var paid = Find<CheckBox>("Allow paid fallback routes");
        if (paid.IsChecked != original.Policy?.AllowPaidFallback) await Activate(paid);
        await SetText(Find<TextBox>("Client name"), "native-model-saved");
        await Activate(Find<Button>("Save model access"));
        await Wait(() => fixture.Config.Revision > revision && FindOrNull<TextBox>("Client name") is null);
        var saved = fixture.Config.Routes.Single(r => r.Id == "r1");
        Check(saved.Alias == "native-model-saved" && saved.UpstreamModel == original.UpstreamModel && saved.Provider == original.Provider
            && saved.AccountIds.SequenceEqual(original.AccountIds) && saved.ProviderRouteId == original.ProviderRouteId
            && saved.Enabled == original.Enabled && saved.Visible == original.Visible && saved.Pooled == original.Pooled
            && JsonSerializer.Serialize(saved.Policy) == JsonSerializer.Serialize(original.Policy),
            "native Save updates exact route r1 while retaining identity, binding, flags, provenance and complete policy");
        await ReturnFocus("Edit selected model access native-model-saved", "model Save");
        Capture("saved");
        await Activate(Find<Button>("Edit selected model access native-model-saved"));
        await Wait(() => FindOrNull<TextBox>("Client name") is not null);
        Check(Find<TextBox>("Client name").Text == saved.Alias, "saved model value reloads from the fixture configuration");
        await Activate(Find<Button>("Cancel"));
    }
    private async Task Profile()
    {
        var original = fixture.Config.Profiles.Single(p => p.Id == "p1");
        var revision = fixture.Config.Revision;
        if (FindOrNull<Button>("Edit selection") is null) await Activate(Find<Button>("Select profile " + original.Name));
        await Activate(Find<Button>("Edit selection"));
        await Wait(() => FindOrNull<TextBox>("Profile name") is not null);
        await Expand("Profile fallback policy");
        await Traverse("profile", Find<TextBox>("Profile name"));
        await SetText(Find<TextBox>("Profile name"), "Cancelled native profile");
        await Activate(Find<Button>("Cancel"));
        await Wait(() => FindOrNull<TextBox>("Profile name") is null);
        Check(fixture.Config.Revision == revision && fixture.Config.Profiles.Single(p => p.Id == "p1") == original, "Cancel preserves profile identity, full selection/policy and revision");
        await ReturnFocus("Edit selection", "profile Cancel");
        await Activate(Find<Button>("Edit selection"));
        await Wait(() => FindOrNull<TextBox>("Profile name") is not null);
        Check(Find<TextBox>("Profile name").Text == original.Name, "profile Cancel discards draft on reopen");
        await SetText(Find<TextBox>("Profile name"), "Native profile saved");
        await Activate(Find<Button>("Save profile"));
        await Wait(() => fixture.Config.Revision > revision && FindOrNull<TextBox>("Profile name") is null);
        var saved = fixture.Config.Profiles.Single(p => p.Id == "p1");
        Check(saved.Name == "Native profile saved" && saved.RouteIds.SequenceEqual(original.RouteIds) && saved.DefaultRouteId == original.DefaultRouteId
            && JsonSerializer.Serialize(saved.Policy) == JsonSerializer.Serialize(original.Policy), "native Save changes exact profile p1, preserves complete route/default/policy state");
        await ReturnFocus("Edit selection", "profile Save"); Capture("saved");
        await Activate(Find<Button>("Edit selection"));
        await Wait(() => FindOrNull<TextBox>("Profile name") is not null);
        Check(Find<TextBox>("Profile name").Text == saved.Name, "saved profile value reloads from the fixture configuration");
        await Activate(Find<Button>("Cancel"));
    }
    private async Task Expand(string name)
    {
        var expander = Find<Expander>(name);
        if (expander.IsExpanded) return;
        await TabTo(expander); input.Key(0x20); await Wait(() => expander.IsExpanded);
    }
    private async Task Traverse(string editor, TextBox start)
    {
        await TabTo(start);
        // Capture the actual mounted input inventory, including dynamically revealed policy fields.
        var expected = All<Control>().Where(c => c.IsEffectivelyVisible && c.IsEffectivelyEnabled &&
            c is TextBox or ComboBox or CheckBox && IsEditorField(c, editor)).ToArray();
        var seen = new HashSet<Control>();
        for (var index = 0; index < 500; index++)
        {
            var active = Focused();
            foreach (var field in expected) if (Within(active, field)) seen.Add(field);
            focus.Add(new { caseName = current, phase = "traverse", index, control = Label(active), type = active?.GetType().Name });
            input.Key(0xff09); await Task.Delay(18);
            if (index > 2 && Within(Focused(), start)) break;
        }
        foreach (var field in expected)
        {
            controls.Add(new { caseName = current, editor, name = Label(field), type = field.GetType().Name, reachedWithNativeTab = seen.Contains(field) });
            Check(seen.Contains(field), "native Tab reaches editor field: " + Label(field));
        }
        Check(expected.Length >= (editor == "model" ? 12 : 50), "mounted editor inventory contains full fixture fields");
        input.Key(0xff09, 0xffe1); await Task.Delay(40);
        Check(!Within(Focused(), start), "native Shift+Tab moves focus backwards");
        await TabTo(start); MeasureTheme(editor + "-editor"); Capture("editor");
    }
    private static bool IsEditorField(Control c, string editor)
    {
        var name = Label(c);
        return editor == "model" ? name is "Client name" or "Provider model ID" or "Provider" or "Provider route provenance ID"
            or "Provider catalog route" or "Known model suggestion" or "Spending policy" or "Allow paid fallback routes"
            || name.StartsWith("Model access ") || name.StartsWith("Bind account ") || name.StartsWith("Fallback position ")
            : name is "Profile name" or "Search profile model access" or "Default client name" or "Spending policy" or "Allow paid profile fallback"
            || name.StartsWith("Profile fallback ") || name.StartsWith("Include model access ");
    }
    private async Task ReturnFocus(string expected, string phase)
    {
        await Task.Delay(180);
        var active = Focused(); focus.Add(new { caseName = current, phase, control = Label(active), type = active?.GetType().Name });
        Check(Within(active, FindOrNull<Button>(expected)), phase + " restores focus to the current stable editor trigger");
    }
    private async Task Activate(Control control)
    {
        await TabTo(control);
        var label = Label(control);
        if (label is "Cancel" or "Save model access" or "Save profile")
        {
            var point = control.TranslatePoint(default, window);
            Check(point is { } p && p.Y >= 0 && p.Y + control.Bounds.Height <= window.Bounds.Height,
                "native keyboard scroll makes " + label + " fully visible before activation");
            controls.Add(new { caseName = current, phase = "action-target", name = label, width = control.Bounds.Width, height = control.Bounds.Height });
            Capture("action-" + images.Count);
        }
        input.Key(control is CheckBox ? 0x20u : 0xff0du); await Task.Delay(100);
    }
    private async Task SetText(TextBox box, string value)
    {
        await TabTo(box); input.Key('a', 0xffe3); input.Key(0xff08);
        foreach (var character in value) { input.Type(character.ToString()); await Task.Delay(10); }
        await Wait(() => box.Text == value);
    }
    private async Task TabTo(Control target)
    {
        for (var i = 0; i < 500; i++)
        {
            if (Within(Focused(), target)) return;
            input.Key(0xff09); await Task.Delay(12);
        }
        throw new InvalidOperationException("Native Tab cannot reach " + Label(target));
    }
    private Control? Focused() => window.FocusManager?.GetFocusedElement() as Control;
    private static bool Within(Control? active, Control? target) => active is not null && target is not null &&
        (ReferenceEquals(active, target) || active.GetVisualAncestors().Any(parent => ReferenceEquals(parent, target)));
    private IEnumerable<T> All<T>() where T : Control => window.GetVisualDescendants().OfType<T>();
    private T? FindOrNull<T>(string name) where T : Control => All<T>().FirstOrDefault(c => c.IsEffectivelyVisible && Label(c) == name);
    private T Find<T>(string name) where T : Control => FindOrNull<T>(name) ?? throw new InvalidOperationException("Missing mounted control " + name);
    private static string Label(Control? control) => control is null ? "<none>" : AutomationProperties.GetName(control) ?? control switch
    { Button b when b.Tag is string tag => tag, ContentControl c when c.Content is string text => text, Expander e when e.Header is string text => text, TextBlock t => t.Text ?? "", TextBox t => t.PlaceholderText ?? "", _ => control.GetType().Name };
    private static async Task Wait(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(8);
        while (!condition()) { if (DateTime.UtcNow >= until) throw new TimeoutException("Native gate condition timed out"); await Task.Delay(25); }
    }
    private void MeasureTheme(string phase)
    {
        foreach (var text in All<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text) && t.Foreground is ISolidColorBrush))
        {
            var point = text.TranslatePoint(default, window); if (point is null || point.Value.Y < 0 || point.Value.Y >= window.Bounds.Height) continue;
            var chain = text.GetVisualAncestors().Reverse().Append(text).ToArray();
            var background = Colors.Black; var opacity = 1d; var layers = new List<object>();
            foreach (var visual in chain)
            {
                opacity *= visual.Opacity;
                var brush = visual switch { Border b => b.Background, Panel p => p.Background, TemplatedControl t => t.Background, _ => null };
                if (brush is ISolidColorBrush solid && solid.Color.A > 0)
                {
                    var alpha = solid.Color.A / 255d * solid.Opacity * opacity;
                    background = Blend(solid.Color, background, alpha);
                    layers.Add(new { type = visual.GetType().Name, color = solid.Color.ToString(), brushOpacity = solid.Opacity, cumulativeVisualOpacity = opacity });
                }
            }
            var foreground = (ISolidColorBrush)text.Foreground!;
            var composite = Blend(foreground.Color, background, foreground.Color.A / 255d * foreground.Opacity * opacity);
            var l1 = Luminance(composite); var l2 = Luminance(background);
            theme.Add(new { caseName = current, phase, text = text.Text, foreground = foreground.Color.ToString(), foregroundOpacity = foreground.Opacity,
                effectiveVisualOpacity = opacity, compositedForeground = composite.ToString(), compositedBackground = background.ToString(),
                solidLayerContrast = (Math.Max(l1, l2) + .05) / (Math.Min(l1, l2) + .05), text.FontSize,
                effectivelyEnabled = text.IsEffectivelyEnabled, bounds = new { x = point.Value.X, y = point.Value.Y, width = text.Bounds.Width, height = text.Bounds.Height }, layers });
        }
    }
    private static Color Blend(Color front, Color back, double alpha) => Color.FromRgb((byte)Math.Round(front.R * alpha + back.R * (1 - alpha)),
        (byte)Math.Round(front.G * alpha + back.G * (1 - alpha)), (byte)Math.Round(front.B * alpha + back.B * (1 - alpha)));
    private static double Luminance(Color color)
    {
        static double C(byte value) { var v = value / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
        return .2126 * C(color.R) + .7152 * C(color.G) + .0722 * C(color.B);
    }
    private void Capture(string phase)
    {
        var path = Path.Combine(output, current + "-" + phase + ".png");
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96));
        bitmap.Render(window); bitmap.Save(path);
        var nativePath = Path.Combine(output, current + "-" + phase + "-x11.png");
        var info = new ProcessStartInfo("import") { UseShellExecute = false };
        foreach (var arg in new[] { "-display", Environment.GetEnvironmentVariable("LAIN_TEST_DISPLAY")!, "-window", "root", nativePath }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!; if (!process.WaitForExit(10000) || process.ExitCode != 0) throw new IOException("Private X11 capture failed");
        images.Add(new { caseName = current, phase, path, sha256 = Hash(path), nativePath, nativeSha256 = Hash(nativePath), window.Width, window.Height, window.RenderScaling, visualReview = "required" });
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    public Task Save() => File.WriteAllTextAsync(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(new
    {
        source = Environment.GetEnvironmentVariable("LAIN_TEST_COMMIT"), baseline = "e48bc118a5ecd2ada80648370ed2b8ef102fd01e",
        assemblies = new[] { typeof(Gates).Assembly, typeof(MainWindow).Assembly, typeof(LainApiClient).Assembly, typeof(Lain.Core.AccountDto).Assembly }
            .Select(a => new { path = a.Location, sha256 = Hash(a.Location) }),
        failed = Failed, cases, checks, images, focus, controls, theme, nativeTrace, requests,
        limits = "Synthetic accounts only. Theme ratios use actual runtime brushes/opacity and solid ancestor composition; overlapping overlays, gradients, anti-aliased glyph pixels and screenreader accessibility are not certified. No screenshots accepted until individual review."
    }, new JsonSerializerOptions { WriteIndented = true }));
}
