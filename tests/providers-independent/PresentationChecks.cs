using System.Reflection;
using Lain.Core;

internal static class PresentationChecks
{
    internal static Task Run()
    {
        var account = new AccountDto("presentation-a", "Personal name", "gemini-api", true, "api-key", "unverified");
        var desktop = typeof(Lain.Desktop.MainWindow);
        var cli = typeof(Lain.Cli.CliApplication);
        object? Call(Type owner, string method, object value) =>
            (owner.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new Exception("Missing presentation seam: " + method)).Invoke(null, [value]);
        void Check(bool valid, string label)
        { Checks.Assertions++; if (!valid) throw new Exception(label); }
        Check(Call(desktop, "AccountCredentialWarning", account) is null, "stored unverified credentials must be neutral");
        foreach (var state in new[] { "missing-credential", "expired", "unauthorized", "error" })
            Check(Call(desktop, "AccountCredentialWarning", account with { State = state }) is string { Length: > 0 }, "actual credential problem remains visible: " + state);
        Check(Call(desktop, "AccountCredentialWarning", account with { ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1) }) is string { Length: > 0 }, "expired unverified credential must still warn");

        var noSamples = new QuotaDto(account.Id, null, "gemini:cloud-monitoring:api-requests", "unavailable",
            null, null, null, "unknown", null, null, null, false,
            Support: new("unavailable", "No series returned; this does not prove zero usage.", "https://aistudio.google.com/usage"));
        foreach (var (owner, method) in new[] { (desktop, "QuotaUnavailable"), (cli, "QuotaHasSupportOnly") })
        {
            Check(Call(owner, method, noSamples) is true, owner.Name + " must show successful empty Monitoring response as neutral guidance");
            Check(Call(owner, method, noSamples with { State = "error", Error = "Quota HTTP 403." }) is false,
                owner.Name + " must not hide genuine permission failure behind guidance");
            Check(Call(owner, method, noSamples with { State = "stale", Used = 15, LastSuccessAt = DateTimeOffset.UtcNow.AddHours(-1) }) is false,
                owner.Name + " must retain stale measured data instead of support-only rendering");
        }
        return Task.CompletedTask;
    }
}
