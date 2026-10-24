namespace Lain.Cli;

public static partial class CliApplication
{
    private sealed class WizardCancelledException : Exception { }

    private static async Task<string> WizardValue(IAccountWizardTerminal terminal, string label, bool secret, CancellationToken ct,
        string? suggested = null, bool required = true)
    {
        while (true)
        {
            var prompt = label + (suggested is null ? "" : " [" + Cell(suggested) + "]") + (required ? "" : " (optional)");
            var value = await terminal.ReadAsync(prompt, secret, ct) ?? throw new WizardCancelledException();
            if (!secret) value = value.Trim();
            if (value.Length == 0 && suggested is not null) value = suggested;
            if (value.Length == 0 && !required) return "";
            if (value.Length > 0 && value.Length <= 4096 && !value.Any(char.IsControl) && (!secret || !value.Any(char.IsWhiteSpace))) return value;
        }
    }

    private static async Task<T> WizardChoice<T>(TextWriter output, IAccountWizardTerminal terminal, string label,
        IReadOnlyList<T> choices, Func<T, string> display, CancellationToken ct)
    {
        if (choices.Count == 0) throw new InvalidOperationException("The catalog has no available " + label.ToLowerInvariant() + ".");
        for (var index = 0; index < choices.Count; index++)
            await output.WriteLineAsync($"  {index + 1}. {Cell(display(choices[index]))}");
        while (true)
        {
            var value = await WizardValue(terminal, label + " number", false, ct);
            if (int.TryParse(value, out var selected) && selected > 0 && selected <= choices.Count) return choices[selected - 1];
            await output.WriteLineAsync("Choose one of the listed numbers, or press Esc to cancel.");
        }
    }
}
