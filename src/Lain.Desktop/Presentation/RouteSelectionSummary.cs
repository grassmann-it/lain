using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Lain.Core;

namespace Lain.Desktop.Presentation;

/// <summary>
/// A compact presentation of one observed execution. Its inputs deliberately exclude current
/// configuration, balances and quotas: none can explain a historical selection or skipped route.
/// </summary>
public sealed class RouteSelectionSummary
{
    public const string Unknown = "Unknown (not recorded)";
    public const int MaximumFieldCharacters = 120;
    public const int MaximumLines = 5;
    public const double MaximumControlHeight = 240;

    public string RequestedTarget { get; }
    public string ActualProvider { get; }
    public string ActualModel { get; }
    public string ActualAccount { get; }
    public string ActualRoute { get; }
    public string Outcome { get; }
    public string SelectionReason => Unknown;
    public string SkippedReason => Unknown;

    private RouteSelectionSummary(string? requested, string? provider, string? model,
        string? account, string? route, string outcome)
    {
        RequestedTarget = Field(requested);
        ActualProvider = Field(provider);
        ActualModel = Field(model);
        ActualAccount = Field(account);
        ActualRoute = Field(route);
        Outcome = outcome;
    }

    /// <summary>RequestedModel may identify an alias/router. Never substitute the executed model when absent.</summary>
    public static RouteSelectionSummary From(AttemptDto attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        var state = Field(attempt.State);
        var outcome = attempt.StatusCode is >= 100 and <= 599
            ? $"{state} · HTTP {attempt.StatusCode.Value.ToString(CultureInfo.InvariantCulture)}" : state;
        return new(attempt.RequestedModel, attempt.Provider, attempt.Model, attempt.AccountId, attempt.RouteId, outcome);
    }

    /// <summary>Capture completeness and a reported finish reason are not proof of successful inference or tool execution.</summary>
    public static RouteSelectionSummary From(RecordingNormalizedRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var outcome = string.IsNullOrWhiteSpace(record.FinishReason)
            ? Unknown : "Reported finish: " + Field(record.FinishReason);
        if (!record.Complete || record.GapReason is not null) outcome += " · capture partial";
        return new(record.RequestedModel, record.Provider, record.Model, record.AccountId, record.RouteId, outcome);
    }

    /// <summary>Fixed-size read-only text; no request/error body, tokens, tools or credential objects are inspected.</summary>
    public IReadOnlyList<string> Lines() => Array.AsReadOnly(new[]
    {
        "Requested alias / router / model: " + RequestedTarget,
        "Executed model: " + ActualModel,
        $"Provider: {ActualProvider} · account: {ActualAccount}",
        $"Route: {ActualRoute} · outcome: {Outcome}",
        $"Selection reason: {SelectionReason} · skipped reason: {SkippedReason}"
    });

    /// <summary>Embed above the existing selected-attempt/record details. No polling, navigation or mutations.</summary>
    public Control CreateControl()
    {
        var lines = new StackPanel { Spacing = 3 };
        lines.Children.Add(new TextBlock
        {
            Text = "Observed route", FontSize = 13, FontWeight = FontWeight.SemiBold,
            MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis
        });
        foreach (var line in Lines())
            lines.Children.Add(new TextBlock
            {
                Text = line, FontSize = 12, LineHeight = 18, MaxLines = 2,
                TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis
            });
        var result = new Border
        {
            Child = lines, Padding = new Thickness(8), MaxWidth = 760,
            MaxHeight = MaximumControlHeight, ClipToBounds = true
        };
        AutomationProperties.SetName(result, "Observed route selection");
        return result;
    }

    private static string Field(string? value)
    {
        if (string.IsNullOrEmpty(value)) return Unknown;
        var result = new StringBuilder(MaximumFieldCharacters);
        var whitespace = false;
        // Stop scanning after the display budget. Huge supplied identifiers do not create large UI work.
        foreach (var rune in value.AsSpan(0, Math.Min(value.Length, 1024)).EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (Rune.IsWhiteSpace(rune) || Rune.IsControl(rune) || category == UnicodeCategory.Format)
            { whitespace = result.Length > 0; continue; }
            var required = rune.Utf16SequenceLength + (whitespace ? 1 : 0);
            if (result.Length + required > MaximumFieldCharacters - 1)
            { result.Append('…'); break; }
            if (whitespace) { result.Append(' '); whitespace = false; }
            result.Append(rune.ToString());
        }
        return result.Length == 0 ? Unknown : result.ToString();
    }
}
