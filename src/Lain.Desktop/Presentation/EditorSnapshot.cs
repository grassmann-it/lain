using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;

namespace Lain.Desktop.Presentation;

/// <summary>
/// A single-use, in-memory draft for an explicit editor reload. Pass only the
/// editor container, never the workspace or window. Values are never persisted.
/// </summary>
public sealed class EditorSnapshot
{
    private List<Field>? _fields;

    private EditorSnapshot(List<Field> fields) => _fields = fields;

    public static EditorSnapshot Capture(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var fields = new List<Field>();
        foreach (var control in Editors(root))
        {
            var field = new Field
            {
                Label = AutomationProperties.GetName(control)!,
                AutomationId = AutomationProperties.GetAutomationId(control),
                Kind = KindOf(control)
            };
            switch (control)
            {
                case TextBox text: field.Text = text.Text; break;
                case ComboBox combo:
                    field.HasSelection = combo.SelectedIndex >= 0;
                    field.Selection = Identity(combo.SelectedItem);
                    break;
                case ToggleButton toggle: field.Checked = toggle.IsChecked; break;
            }
            fields.Add(field);
        }
        return new(fields);
    }

    /// <summary>
    /// Restores into fresh editor controls and consumes this snapshot. Only field
    /// labels are returned when a field or option disappeared or could not be kept.
    /// No save action is invoked.
    /// </summary>
    public IReadOnlyList<string> Restore(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var fields = _fields;
        _fields = null;
        if (fields is null) return [];
        var failed = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            // Connection/catalog choices can rebuild settings or replace text.
            // Billing is restored after settings have established its choices.
            foreach (var field in fields.OrderBy(Priority))
            {
                try
                {
                    var control = Find(root, field);
                    switch (control)
                    {
                        case TextBox text: text.Text = field.Text; break;
                        case ToggleButton toggle: toggle.IsChecked = field.Checked; break;
                        case ComboBox combo:
                            if (!field.HasSelection) { combo.SelectedIndex = -1; break; }
                            var matches = combo.Items.Cast<object?>()
                                .Select((item, index) => (Identity: Identity(item), Index: index))
                                .Where(item => field.Selection is not null && item.Identity == field.Selection).ToArray();
                            if (matches.Length == 1) combo.SelectedIndex = matches[0].Index;
                            else failed.Add(field.Label);
                            break;
                        default: failed.Add(field.Label); break;
                    }
                }
                catch (Exception) { failed.Add(field.Label); }
            }
            // Later change handlers may replace controls restored earlier.
            foreach (var field in fields)
            {
                try
                {
                    var matches = Find(root, field) switch
                    {
                        TextBox text => text.Text == field.Text,
                        ToggleButton toggle => toggle.IsChecked == field.Checked,
                        ComboBox combo => field.HasSelection
                            ? combo.SelectedIndex >= 0 && field.Selection is not null && Identity(combo.SelectedItem) == field.Selection
                            : combo.SelectedIndex < 0,
                        _ => false
                    };
                    if (!matches) failed.Add(field.Label);
                }
                catch (Exception) { failed.Add(field.Label); }
            }
            return fields.Select(field => field.Label).Where(failed.Contains).Distinct(StringComparer.Ordinal).ToArray();
        }
        finally
        {
            foreach (var field in fields) { field.Text = null; field.Selection = null; }
            fields.Clear();
        }
    }

    private static IEnumerable<Control> Editors(Control root) => root.GetLogicalDescendants()
        .OfType<Control>().Prepend(root).Where(control => control.TemplatedParent is null &&
            !string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)) &&
            (control is TextBox { IsReadOnly: false } or ComboBox or ToggleButton));

    private static Control? Find(Control root, Field field)
    {
        // Ambiguous labels/IDs must never restore a draft into the wrong field.
        var matches = Editors(root).Where(control => KindOf(control) == field.Kind &&
            (!string.IsNullOrWhiteSpace(field.AutomationId)
                ? AutomationProperties.GetAutomationId(control) == field.AutomationId
                : AutomationProperties.GetName(control) == field.Label)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static int Priority(Field field) => field.Kind switch
    {
        FieldKind.Choice when field.Label.Contains("billing", StringComparison.OrdinalIgnoreCase) => 4,
        FieldKind.Choice when field.Label.Contains("connection", StringComparison.OrdinalIgnoreCase) => 0,
        FieldKind.Choice => 1,
        FieldKind.Toggle => 2,
        _ => 3
    };

    private static FieldKind KindOf(Control control) => control switch
    {
        TextBox => FieldKind.Text,
        ComboBox => FieldKind.Choice,
        _ => FieldKind.Toggle
    };

    private static string? Identity(object? item)
    {
        if (item is null) return null;
        if (item is string text) return "text:" + text;
        if (item is Control control)
        {
            var id = AutomationProperties.GetAutomationId(control);
            if (!string.IsNullOrWhiteSpace(id)) return "id:" + id;
            if (item is ComboBoxItem option) return Identity(option.Content);
            if (item is TextBlock display) return display.Text is { } label ? "text:" + label : null;
            var name = AutomationProperties.GetName(control);
            return string.IsNullOrWhiteSpace(name) ? null : "text:" + name;
        }
        // DTO choices use IDs; simple display values (including enums) use their
        // own string representation. These identities remain private in memory.
        if (item.GetType().GetProperty("Id")?.GetValue(item) is string stableId)
            return "id:" + stableId;
        return item.GetType().GetMethod(nameof(ToString), Type.EmptyTypes)?.DeclaringType != typeof(object)
            ? "text:" + item.ToString() : null;
    }

    private enum FieldKind { Text, Choice, Toggle }
    private sealed class Field
    {
        public required string Label;
        public string? AutomationId;
        public FieldKind Kind;
        public string? Text;
        public bool? Checked;
        public bool HasSelection;
        public string? Selection;
    }
}
