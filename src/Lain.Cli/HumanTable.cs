using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("ClientCli.Tests")]

namespace Lain.Cli;

internal sealed record HumanColumn(string Heading, bool Numeric = false, bool Secondary = false,
    bool Identifier = false, bool Status = false);
internal sealed record HumanDetail(string Label, string? Value, bool Identifier = false);
internal sealed record HumanRow(string?[] Cells, HumanDetail[]? Details = null);

internal sealed record HumanOutputContext(int Width, bool Color)
{
    internal static bool ColorAllowed(bool terminal, string? noColor, string? term) =>
        terminal && noColor is null && !string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase);

    internal static HumanOutputContext Detect(TextWriter output)
    {
        var terminal = ReferenceEquals(output, Console.Out) && !Console.IsOutputRedirected;
        var width = 100;
        if (terminal)
        {
            try { if (Console.WindowWidth > 0) width = Console.WindowWidth; }
            catch (IOException) { }
            catch (PlatformNotSupportedException) { }
        }
        return new(Math.Clamp(width, 20, 500), ColorAllowed(terminal,
            Environment.GetEnvironmentVariable("NO_COLOR"), Environment.GetEnvironmentVariable("TERM")));
    }
}

/// <summary>Plain aligned tables, with full secondary fields below each row when space is tight.</summary>
internal static class HumanTable
{
    internal static async Task WriteAsync(TextWriter output, string title, HumanColumn[] columns,
        IEnumerable<HumanRow> source, HumanOutputContext? context = null)
    {
        context ??= HumanOutputContext.Detect(output);
        var rows = source.Select(row => row with { Cells = row.Cells.Select(Clean).ToArray() }).ToArray();
        if (rows.Any(row => row.Cells.Length != columns.Length)) throw new ArgumentException("Table column count does not match its rows.");
        await WriteTextAsync(output, title, context);
        if (rows.Length == 0) { await WriteTextAsync(output, "No entries.", context); return; }
        var widths = columns.Select((column, index) => Math.Max(Width(column.Heading), rows.Max(row => Width(row.Cells[index]!)))).ToArray();
        var visible = Enumerable.Range(0, columns.Length).ToList();
        int TableWidth() => visible.Sum(index => widths[index]) + Math.Max(0, visible.Count - 1) * 2;
        foreach (var index in visible.Where(index => columns[index].Secondary).Reverse().ToArray())
        {
            if (TableWidth() <= context.Width) break;
            visible.Remove(index);
        }
        var stacked = TableWidth() > context.Width;
        if (!stacked)
        {
            var header = string.Join("  ", visible.Select(index => Pad(Clean(columns[index].Heading), widths[index], columns[index].Numeric)));
            await output.WriteLineAsync(Paint(header.TrimEnd(), "36;1", context));
            await output.WriteLineAsync(string.Join("  ", visible.Select(index => new string('-', widths[index]))));
        }
        foreach (var row in rows)
        {
            if (stacked)
            {
                for (var index = 0; index < columns.Length; index++)
                    await WriteFieldAsync(output, columns[index].Heading, row.Cells[index], columns[index].Identifier, context, index == 0 ? "" : "  ");
            }
            else
            {
                var line = string.Join("  ", visible.Select(index =>
                {
                    var value = Pad(row.Cells[index]!, widths[index], columns[index].Numeric);
                    return columns[index].Status ? Paint(value, StatusColor(row.Cells[index]!), context) : value;
                }));
                await output.WriteLineAsync(line.TrimEnd());
                foreach (var index in Enumerable.Range(0, columns.Length).Except(visible))
                    await WriteFieldAsync(output, columns[index].Heading, row.Cells[index], columns[index].Identifier, context);
            }
            foreach (var detail in row.Details ?? [])
                await WriteFieldAsync(output, detail.Label, detail.Value, detail.Identifier, context);
            if (stacked || visible.Count < columns.Length || row.Details is { Length: > 0 }) await output.WriteLineAsync();
        }
    }

    internal static Task WriteTextAsync(TextWriter output, string text, HumanOutputContext? context = null) =>
        WriteWrappedAsync(output, Clean(text), "", (context ?? HumanOutputContext.Detect(output)).Width);

    private static async Task WriteFieldAsync(TextWriter output, string label, string? value, bool identifier,
        HumanOutputContext context, string indent = "  ")
    {
        var prefix = indent + Clean(label) + ": ";
        var clean = Clean(value);
        // Command identifiers must remain copyable, including the uncommon ID
        // longer than the terminal itself. Never ellipsize or split an ID.
        if (identifier) { await output.WriteLineAsync(prefix + clean); return; }
        await WriteWrappedAsync(output, clean, prefix, context.Width);
    }

    private static async Task WriteWrappedAsync(TextWriter output, string value, string prefix, int width)
    {
        var line = prefix;
        var continuation = new string(' ', Math.Min(Width(prefix), Math.Max(0, width - 8)));
        foreach (var word in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.Length > 0 && line != prefix && line != continuation ? " " : "";
            if (Width(line) + Width(separator) + Width(word) > width && line != prefix && line != continuation)
            {
                await output.WriteLineAsync(line);
                line = continuation;
                separator = "";
            }
            line += separator + word;
        }
        await output.WriteLineAsync(line.TrimEnd());
    }

    private static string Pad(string text, int width, bool right) => right
        ? new string(' ', Math.Max(0, width - Width(text))) + text
        : text + new string(' ', Math.Max(0, width - Width(text)));

    private static string Paint(string value, string? color, HumanOutputContext context) =>
        context.Color && color is not null ? $"\u001b[{color}m{value}\u001b[0m" : value;

    private static string? StatusColor(string value) => value switch
    {
        "Available" or "Ready" or "Enabled" or "Checked" => "32",
        "Prepared" or "Not checked" or "Last known" or "Stale" or "Reset due" or "Last known; reset due" => "33",
        "Failed" or "Expired" or "Error" => "31",
        _ => null
    };

    internal static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        var clean = new StringBuilder(value.Length);
        var pendingSpace = false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character is '\u001b' or '\u009b' or '\u009d' or '\u0090' or '\u009e' or '\u009f')
            {
                var kind = character == '\u001b' && index + 1 < value.Length ? value[++index] : character;
                if (kind is '[' or '\u009b')
                {
                    while (++index < value.Length && value[index] is not (>= '@' and <= '~')) { }
                }
                else if (kind is ']' or 'P' or 'X' or '^' or '_' or '\u009d' or '\u0090' or '\u009e' or '\u009f')
                {
                    while (++index < value.Length)
                    {
                        if (value[index] is '\a' or '\u009c') break;
                        if (value[index] == '\u001b' && index + 1 < value.Length && value[index + 1] == '\\') { index++; break; }
                    }
                }
                continue;
            }
            if (char.GetUnicodeCategory(character) == UnicodeCategory.Format) continue;
            if (char.IsControl(character) || char.IsWhiteSpace(character)) { pendingSpace = clean.Length > 0; continue; }
            if (pendingSpace) { clean.Append(' '); pendingSpace = false; }
            clean.Append(character);
        }
        return clean.Length == 0 ? "unknown" : clean.ToString();
    }

    // Count terminal cells rather than UTF-16 code units, including common CJK
    // and emoji. Combining marks do not consume their own display column.
    internal static int Width(string value)
    {
        var width = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format) continue;
            var point = rune.Value;
            width += point >= 0x1100 && (point <= 0x115f || point is 0x2329 or 0x232a
                || point is >= 0x2e80 and <= 0xa4cf || point is >= 0xac00 and <= 0xd7a3
                || point is >= 0xf900 and <= 0xfaff || point is >= 0xfe10 and <= 0xfe19
                || point is >= 0xfe30 and <= 0xfe6f || point is >= 0xff00 and <= 0xff60
                || point is >= 0xffe0 and <= 0xffe6 || point is >= 0x1f300 and <= 0x1faff
                || point is >= 0x20000 and <= 0x3fffd) ? 2 : 1;
        }
        return width;
    }
}
