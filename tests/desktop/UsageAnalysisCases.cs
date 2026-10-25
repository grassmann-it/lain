using System.Text.Json;
using Avalonia.Controls;
using Lain.Desktop.Presentation;

internal sealed partial class Scenario
{
    public async Task RunUsageAnalysisSmoke()
    {
        Directory.CreateDirectory(Output);
        // Reproduce the pre-existing analysis cases' configured inactive-account
        // inventory without running unrelated account/edit/profile scenarios.
        fixture.Config = fixture.Config with
        {
            Accounts = [.. fixture.Config.Accounts, fixture.Config.Accounts[4] with { Id = "a-new", Name = "New inactive account" }]
        };
        await Open();
        await SelectPresentation(InformationLevel.Advanced);
        await Click("Usage");
        await Wait(UsageConnected, "targeted Usage analysis connected");
        await Click("Usage additional filters");
        await SelectUsageBreakdown("Model family", "modelFamily", "fixture-family");
        await CheckUsageAnalysis();
        await File.WriteAllTextAsync(Path.Combine(Output, "usage-analysis-evidence.json"), JsonSerializer.Serialize(new
        {
            result = "PASS", scenario = "usage-analysis-smoke", checks, screenshots, requests = fixture.Requests,
            scope = "Existing central analysis assertions on production MainWindow and UsageView; private Xvfb and DBus; in-memory HTTP. Routed control actions, not native pointer evidence (covered by desktop-usage)."
        }, new JsonSerializerOptions { WriteIndented = true }));
        window.Close();
        foreach (var transport in transports) transport.Dispose();
        Console.WriteLine($"PASS usage analysis smoke: {checks.Count} checks, {screenshots.Count} screenshots. {Output}/usage-analysis-evidence.json");
    }
}
