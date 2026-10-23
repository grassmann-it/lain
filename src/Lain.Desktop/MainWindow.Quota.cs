using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Lain.Client;
using Lain.Core;
using Lain.Desktop.Usage;
using Avalonia.Platform.Storage;
using System.Text.Json;

namespace Lain.Desktop;

public sealed partial class MainWindow
{
    private static Control QuotaDetails(QuotaDto? quota)
    {
        if (quota is null) return Ui.Text("Quota · unknown", 12, Ui.Muted);
        static string Amount(double? value) => value?.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture) ?? "Unknown";
        var line = quota.Limit is null && quota.Remaining is null && quota.Used is null
            ? $"Quota · {quota.State} · amount unknown"
            : $"Quota · {quota.State} · {Amount(quota.Remaining)} {quota.Unit} remaining · {Amount(quota.Used)} / {Amount(quota.Limit)} used";
        return Ui.Stack(Ui.Text(line, 13, Ui.Accent), Ui.Text($"Source: {quota.Source} · observed {Ui.When(quota.ObservedAt)} · last success {Ui.When(quota.LastSuccessAt)} · reset {Ui.When(quota.ResetsAt)}", 11, Ui.Muted), Ui.Text(quota.Error ?? "", 12, Ui.Muted));
    }

}
