using Avalonia.Input.Platform;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Lain.Desktop.Diagnostics;

/// <summary>User-triggered, bounded, read-only Doctor UI. Opening/attaching the view performs no diagnosis.</summary>
public sealed class DiagnosticsView : UserControl
{
    private readonly IDoctorReportSource _source;
    private readonly ComboBox _target = new()
    {
        ItemsSource = new[] { "Service / root", "Codex CLI", "Codex VS Code" }, SelectedIndex = 0, Width = 200
    };
    private readonly Button _run = new() { Content = "Run diagnosis" };
    private readonly Button _cancel = new() { Content = "Cancel", IsEnabled = false };
    private readonly Button _copy = new() { Content = "Copy safe report", IsEnabled = false };
    private readonly TextBlock _status = Ui.Text("Run a local diagnosis to inspect service and client metadata.", 13, Ui.Muted);
    private readonly StackPanel _findings = new() { Spacing = 8 };
    private CancellationTokenSource? _operation;
    private long _generation;
    private bool _detached;
    public DoctorDisplayReport? Report { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string? SafeSupportText => Report is null ? null : DoctorPresentation.SupportText(Report);

    /// <param name="trustedCliPath">Absolute path selected by the application to its trusted installed Lain CLI.</param>
    public DiagnosticsView(string trustedCliPath) : this(new DoctorCliSource(trustedCliPath)) { }

    public DiagnosticsView(IDoctorReportSource source)
    {
        _source = source;
        AutomationProperties.SetName(_target, "Diagnosis target");
        AutomationProperties.SetName(_status, "Diagnosis status");
        _run.Click += async (_, _) => await RunAsync();
        _cancel.Click += (_, _) => _operation?.Cancel();
        _copy.Click += async (_, _) => await CopyAsync();
        var header = Ui.PageHeader("Diagnostics", "Read-only local service and client metadata. Findings do not establish provider authentication or response quality.");
        var actions = Ui.Row(_target, _run, _cancel, _copy);
        var top = Ui.Stack(header, actions, _status);
        var scroll = new ScrollViewer
        {
            Content = _findings, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 12 };
        grid.Children.Add(top);
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);
        Content = grid;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        if (_operation is not null || _detached) return;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _operation = operation;
        var generation = ++_generation;
        _run.IsEnabled = false; _target.IsEnabled = false; _cancel.IsEnabled = true; _copy.IsEnabled = false;
        Report = null; LastErrorCode = null; _findings.Children.Clear();
        _status.Text = "Reading local metadata…";
        try
        {
            var target = _target.SelectedIndex switch { 1 => DoctorTarget.CodexCli, 2 => DoctorTarget.CodexVscode, _ => DoctorTarget.Service };
            var result = await _source.ReadAsync(target, operation.Token);
            if (_detached || generation != _generation) return;
            if (operation.IsCancellationRequested) { ShowError("doctor_cancelled"); return; }
            if (result.Json is null) { ShowError(result.ErrorCode); return; }
            var presentation = DoctorPresentation.Parse(result.Json);
            if (presentation.Report is null) { ShowError(presentation.ErrorCode); return; }
            Report = presentation.Report;
            Render(Report);
            _copy.IsEnabled = true;
        }
        catch (OperationCanceledException) { if (!_detached && generation == _generation) ShowError("doctor_cancelled"); }
        catch (Exception) { if (!_detached && generation == _generation) ShowError("doctor_unavailable"); }
        finally
        {
            var wasCurrent = ReferenceEquals(_operation, operation);
            if (wasCurrent) _operation = null;
            if (!_detached && wasCurrent)
            { _run.IsEnabled = true; _target.IsEnabled = true; _cancel.IsEnabled = false; }
        }
    }

    private void Render(DoctorDisplayReport report)
    {
        var blocked = report.Findings.Count(f => f.State == "blocked");
        var warnings = report.Findings.Count(f => f.State == "warning");
        _status.Text = $"Observed {report.ObservedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {report.Findings.Count} findings · {blocked} blocked · {warnings} warnings"
            + (report.Truncated ? " · Display limit reached" : "");
        if (report.Versions.Count > 0) _findings.Children.Add(Ui.Text(string.Join(" · ", report.Versions.Select(p => p.Key + " " + p.Value)), 12, Ui.Muted));
        foreach (var finding in report.Findings)
        {
            var tone = finding.State switch { "ok" => "success", "warning" => "warning", "blocked" => "danger", _ => null };
            var heading = Ui.Row(Ui.Badge(finding.State, tone), Ui.Text(finding.Code, 14));
            _findings.Children.Add(Ui.Card(Ui.Stack(heading, Ui.Text(finding.Explanation),
                Ui.Text("Source: " + finding.Source, 12, Ui.Muted), Ui.Text("Next: " + finding.NextStep, 12))));
        }
        if (report.Findings.Count == 0) _findings.Children.Add(Ui.Text("No findings were returned.", 13, Ui.Muted));
    }

    private void ShowError(string? error)
    {
        LastErrorCode = error switch
        {
            "doctor_cancelled" or "doctor_timeout" or "doctor_output_limit" or "doctor_invalid_report" or
            "doctor_cli_failed" or "doctor_cli_unavailable" => error,
            _ => "doctor_unavailable"
        };
        _status.Text = LastErrorCode switch
        {
            "doctor_cancelled" => "Diagnosis cancelled.",
            "doctor_timeout" => "Diagnosis exceeded the local time limit. Try again when the service is responsive.",
            "doctor_output_limit" => "Diagnosis exceeded the safe output limit.",
            "doctor_invalid_report" => "The CLI returned an unsupported diagnosis report.",
            _ => "Local diagnosis is unavailable. Check the installed Lain CLI."
        };
        _status.Text += " (" + LastErrorCode + ")";
    }

    private async Task CopyAsync()
    {
        var text = SafeSupportText;
        if (text is null) return;
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            { _status.Text = "Clipboard unavailable."; return; }
            await clipboard.SetTextAsync(text);
            _status.Text = "Safe diagnosis report copied.";
        }
        catch (Exception) { _status.Text = "Clipboard unavailable."; }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _detached = false;
        _run.IsEnabled = _operation is null; _target.IsEnabled = _operation is null;
        _cancel.IsEnabled = _operation is not null;
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _detached = true; ++_generation;
        _operation?.Cancel();
        base.OnDetachedFromVisualTree(e);
    }
}
