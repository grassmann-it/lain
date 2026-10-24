using System.Text;

namespace Lain.Cli;

/// <summary>A private local terminal. Secret input must never be echoed.</summary>
public interface IAccountWizardTerminal
{
    bool IsInteractive { get; }
    Task<string?> ReadAsync(string prompt, bool secret, CancellationToken ct);
}

internal sealed class ConsoleAccountWizardTerminal(TextWriter output) : IAccountWizardTerminal
{
    public bool IsInteractive => !Console.IsInputRedirected && !Console.IsOutputRedirected && ReferenceEquals(output, Console.Out);

    public async Task<string?> ReadAsync(string prompt, bool secret, CancellationToken ct)
    {
        if (!IsInteractive) throw new ArgumentException("Account setup requires an interactive terminal. Use accounts add-api --key-stdin or --key-env for automation.");
        await output.WriteAsync(prompt + ": ");
        await output.FlushAsync(ct);
        var value = new StringBuilder();
        var oldControlC = Console.TreatControlCAsInput;
        try
        {
            Console.TreatControlCAsInput = true;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (!Console.KeyAvailable) { await Task.Delay(20, ct); continue; }
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Escape || (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control)) ||
                    (key.Key == ConsoleKey.D && key.Modifiers.HasFlag(ConsoleModifiers.Control)))
                {
                    await output.WriteLineAsync();
                    return null;
                }
                if (key.Key == ConsoleKey.Enter) { await output.WriteLineAsync(); return value.ToString(); }
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (value.Length > 0) { value.Length--; if (!secret) await output.WriteAsync("\b \b"); }
                    continue;
                }
                if (char.IsControl(key.KeyChar) || value.Length >= 4096) continue;
                value.Append(key.KeyChar);
                if (!secret) await output.WriteAsync(key.KeyChar);
            }
        }
        finally { Console.TreatControlCAsInput = oldControlC; value.Clear(); }
    }
}
