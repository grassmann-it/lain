using Avalonia;
using Avalonia.Controls;
using Lain.Core;

namespace Lain.Desktop.Recording;

public sealed partial class RecordingView
{
    private readonly Dictionary<string, RecordingConversationSummary> _summaries = new(StringComparer.Ordinal);
    private void AddConversation(RecordingConversationSummary item)
    {
        _summaries[item.Id] = item;
        if (_choices.ContainsKey(item.Id)) return;
        var label = string.IsNullOrWhiteSpace(item.Title) ? item.Id : item.Title;
        var text = $"{label}\n{string.Join(", ", item.Models)} · {item.MessageCount} messages · {item.State}\n{item.UpdatedAt.ToLocalTime():g}";
        var button = Ui.Button(text, async () => await SelectConversationAsync(item.Id));
        button.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        button.Content = Ui.Text(text, 12);
        Name(button, $"Open saved conversation {item.Id}");
        _choices.Add(item.Id, button); _list.Children.Add(button);
    }
    private void AppendRecord(RecordingNormalizedRecord record)
    {
        if (!_renderedRecords.Add(record.Id)) return;
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(Ui.Text($"{record.ObservedAt.ToLocalTime():g} · {record.Model ?? "Model unknown"} · request {record.RequestId} · attempt {record.AttemptId ?? "unknown"}", 12, Ui.Muted));
        if (record.RequestedModel != record.Model && record.RequestedModel is not null)
            panel.Children.Add(Ui.Text($"Requested model: {record.RequestedModel}. Observed model revision: {record.ModelRevision ?? "unknown"}.", 12, Ui.Muted));
        if (!record.Complete || record.GapReason is not null)
            panel.Children.Add(Ui.Text($"Incomplete / gap: {record.GapReason ?? "source did not establish completeness"}", 12, Ui.Warning));
        if (record.FinishReason is not null) panel.Children.Add(Ui.Text($"Finish: {record.FinishReason}", 11, Ui.Muted));
        if (record.Messages.Length > 0)
            panel.Children.Add(new RecordingPages<RecordingMessage>(record.Messages, 8, "messages", record.Id, MessageContent));
        if (record.WorkerId is not null)
        {
            var worker = new Expander { Header = $"Worker {record.WorkerId} (evidenced)", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            Name(worker, $"Worker evidence {record.WorkerId}");
            worker.PropertyChanged += (_, e) =>
            {
                if (e.Property != Expander.IsExpandedProperty || !worker.IsExpanded || worker.Content is not null) return;
                worker.Content = Ui.Text($"Worker ID: {record.WorkerId}\nParent worker: {record.ParentWorkerId ?? "unknown / unassigned"}\nParent request: {record.ParentRequestId ?? "unknown / unassigned"}\nRelationships use supplied source evidence; tool names and timing do not establish parentage.", 12, Ui.Muted);
            };
            panel.Children.Add(worker);
        }
        var api = new Expander { Header = "API observation and source details", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        Name(api, $"API source details {record.Id}");
        api.PropertyChanged += (_, e) =>
        {
            if (e.Property != Expander.IsExpandedProperty || !api.IsExpanded || api.Content is not null) return;
            var details = new StackPanel { Spacing = 6 };
            details.Children.Add(Ui.Text($"Normalized parsed representation · {record.Protocol}\nRecord {record.Id}\nCapture {record.CaptureId}\nProvider {record.Provider ?? "unknown"} · route {record.RouteId ?? "unknown"}\nResponse {record.ResponseId ?? "unknown"} · previous response {record.PreviousResponseId ?? "unknown"}\nReasoning requested {record.ReasoningEffort ?? "unknown"} / effective {record.EffectiveReasoningEffort ?? "unknown"}\nModel revision {record.ModelRevision ?? "unknown"}. This normalized view is not original wire bytes.", 12, Ui.Muted));
            details.Children.Add(SourcePages(record.Sources));
            if (record.Usage is { } usage)
                details.Children.Add(Ui.Text($"Reported usage ({usage.Source ?? "source unknown"}): input {Number(usage.Input)}, output {Number(usage.Output)}, cached {Number(usage.Cached)}, reasoning {Number(usage.Reasoning)}, total {Number(usage.Total)}. These observations do not establish vendor billing or task success.", 12, Ui.Muted));
            api.Content = details;
        };
        panel.Children.Add(api);
        // Metadata-only lifecycle observations stay compact and do not create a full conversation card.
        if (record.Messages.Length == 0)
        {
            panel.Spacing = 3; Name(panel, $"Timeline record {record.Id}"); _timeline.Children.Add(panel); return;
        }
        var surface = Ui.Card(panel); surface.Padding = new Thickness(10);
        Name(surface, $"Timeline record {record.Id}");
        _timeline.Children.Add(surface);
    }
    private static Control MessageContent(RecordingMessage message)
    {
        var panel = new StackPanel { Spacing = 5 };
        panel.Children.Add(Ui.Text($"{message.Role} · {message.State ?? "observed"}{(message.Complete ? "" : " · incomplete")}", 13));
        if (message.GapReason is not null) panel.Children.Add(Ui.Text(message.GapReason, 12, Ui.Warning));
        panel.Children.Add(new RecordingPages<RecordingContentPart>(message.Parts, 4, "parts", message.Id, PartContent));
        var sources = new Expander { Header = $"Message source references ({message.Sources.Length})" };
        sources.PropertyChanged += (_, e) => { if (e.Property == Expander.IsExpandedProperty && sources.IsExpanded && sources.Content is null) sources.Content = SourcePages(message.Sources); };
        panel.Children.Add(sources);
        return panel;
    }
    private static Control PartContent(RecordingContentPart part)
    {
        var panel = new StackPanel { Spacing = 5 };
        if (part.Text is not null) panel.Children.Add(new RecordingText(part.Text, "Retained message text page"));
        if (part.ToolName is not null || part.ToolCallId is not null)
            panel.Children.Add(Ui.Text($"Requested tool: {part.ToolName ?? "unknown"} · call {part.ToolCallId ?? "unknown"} · {part.Evidence}", 12));
        if (part.Arguments is not null) panel.Children.Add(LazyText("Tool arguments", part.Arguments));
        if (part.Result is not null)
        {
            panel.Children.Add(Ui.Text("Result supplied by client; local execution is not established by this record.", 12, Ui.Muted));
            panel.Children.Add(LazyText("Client-supplied tool result", part.Result));
        }
        if (part.Text is null && part.Arguments is null && part.Result is null && part.ToolName is null)
            panel.Children.Add(Ui.Text($"{part.Kind} · unsupported or omitted content · {part.Evidence}", 12, Ui.Warning));
        if (!part.Complete) panel.Children.Add(Ui.Text("Content part incomplete", 11, Ui.Warning));
        return panel;
    }
    private static string Number(long? value) => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown";
    private static Expander LazyText(string title, string value)
    {
        var control = new Expander { Header = title, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        control.PropertyChanged += (_, e) => { if (e.Property == Expander.IsExpandedProperty && control.IsExpanded && control.Content is null) control.Content = new RecordingText(value, title); };
        return control;
    }
    private static Control SourcePages(RecordingSourceReference[] sources)
    {
        var body = new StackPanel { Spacing = 4 };
        var rows = new StackPanel { Spacing = 3 };
        var offset = 0;
        var more = Ui.Button("Next source page", () => { });
        var previous = Ui.Button("Previous source page", () => { });
        void Render()
        {
            rows.Children.Clear();
            foreach (var source in sources.Skip(offset).Take(25)) rows.Children.Add(Ui.Text($"{source.SourceEpoch} · sequence {source.Sequence} · event {source.EventId}", 11, Ui.Muted));
            previous.IsEnabled = offset > 0; more.IsEnabled = offset + 25 < sources.Length;
        }
        previous.Click += (_, _) => { offset = Math.Max(0, offset - 25); Render(); };
        more.Click += (_, _) => { offset += 25; Render(); };
        body.Children.Add(Ui.Text($"{sources.Length} retained source references · 25 per page", 11, Ui.Muted));
        body.Children.Add(Wrap(previous, more)); body.Children.Add(rows); Render();
        return body;
    }
    private void RenderManifests(RecordingRunManifest[]? manifests)
    {
        _variants.Children.Clear();
        if (manifests is not { Length: > 0 }) { _variants.Children.Add(Ui.Text("No run manifest retained. Skill version and loaded instructions remain unknown.", 12, Ui.Muted)); return; }
        foreach (var manifest in manifests)
        {
            var evidence = manifest.Evidence is "configured" or "reported" or "observed" ? manifest.Evidence : "unknown";
            _variants.Children.Add(Ui.Text($"Run {manifest.Id} · variant {manifest.Variant ?? "unknown"} · evidence: {evidence}\nConfigured revision {manifest.ConfiguredRevision ?? "unknown"}\nSkill hash {manifest.SkillHash ?? "unknown"} · prompt hash {manifest.PromptHash ?? "unknown"}\nObserved instruction hash {manifest.ObservedInstructionHash ?? "unknown"}\nA configured label or hash does not establish that a skill was loaded or followed.", 12, Ui.Muted));
        }
    }
}
