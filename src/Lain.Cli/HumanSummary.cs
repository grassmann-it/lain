using System.Globalization;
using Lain.Core;

namespace Lain.Cli;

public static partial class CliApplication
{
    internal static async Task WriteStatus(TextWriter output, StatusDto status, HumanOutputContext? context = null)
    {
        context ??= HumanOutputContext.Detect(output);
        await HumanTable.WriteAsync(output, "lain status", [new("Item"), new("State", Status: true)],
            [new(["Mode", status.RuntimeMode]), new([status.RuntimeMode == "monitor" ? "Quota monitoring" : "Proxy", status.RuntimeMode == "monitor" ? (status.Capabilities?.QuotaMonitoring == true ? "Available" : "Not available") : status.ProxyReady ? "Ready" : "Unavailable"]), new(["Host", ReadableState(status.State)])], context);
        await HumanTable.WriteTextAsync(output, $"Configuration: {status.Revision} saved / {status.AppliedRevision} applied"
            + (status.Revision == status.AppliedRevision ? "" : " · changes pending"), context);
        await HumanTable.WriteTextAsync(output, $"Version {Cell(status.Version)} · updated {Timestamp(status.ObservedAt)} UTC", context, subtle: true);
        if (status.Application is { } application)
            await HumanTable.WriteTextAsync(output, $"Application {Cell(application.State)} · operation {Cell(application.OperationId)} · profile {Cell(application.ProfileId)} · revision {application.Revision?.ToString() ?? "unknown"}"
                + (application.RecoveryAction is null ? "" : " · next action " + Cell(application.RecoveryAction)), context);
        if (!string.IsNullOrWhiteSpace(status.Error)) await HumanTable.WriteTextAsync(output, "Error: " + Cell(status.Error), context);
    }

    internal static async Task WriteUsage(TextWriter output, UsageSummaryDto usage, HumanOutputContext? context = null)
    {
        context ??= HumanOutputContext.Detect(output);
        await HumanTable.WriteTextAsync(output, "Proxy history", context);
        await HumanTable.WriteTextAsync(output, $"From {usage.From:O}", context);
        await HumanTable.WriteTextAsync(output, $"To   {usage.To:O}", context);
        await HumanTable.WriteTextAsync(output, "Timezone " + Cell(usage.TimeZone) + " · coverage " + (usage.Complete ? "complete" : "incomplete"), context);
        await HumanTable.WriteAsync(output, "Whole-window totals", [new("Metric"), new("Count", Numeric: true)],
            [new(["Requests", usage.Totals.Requests.ToString(CultureInfo.InvariantCulture)]),
                new(["Attempts", usage.Totals.Attempts.ToString(CultureInfo.InvariantCulture)]),
                new(["Errors", usage.Totals.Errors.ToString(CultureInfo.InvariantCulture)]),
                new(["Tokens", Number(usage.Totals.Tokens.Total)]),
                new(["Unknown usage attempts", usage.Totals.UnknownUsageAttempts.ToString(CultureInfo.InvariantCulture)])], context);
        if (usage.Totals.Measurements is { } measurements)
            foreach (var (component, value) in Lain.Client.UsageDisplayFacts.Components(measurements))
                await HumanTable.WriteTextAsync(output, Lain.Client.UsageDisplayFacts.Describe(component, value), context);
        await WriteUsageGroups(output, "Account ID", usage.Accounts, context);
        await WriteUsageGroups(output, "Model", usage.Models, context);
        await WriteUsageGroups(output, "Day", usage.Daily, context);
        await HumanTable.WriteTextAsync(output, $"Observed {Timestamp(usage.ObservedAt)} UTC", context, subtle: true);
    }

    private static Task WriteUsageGroups(TextWriter output, string dimension, UsageGroup[] groups, HumanOutputContext context) =>
        HumanTable.WriteAsync(output, "By " + dimension.ToLowerInvariant(),
            [new(dimension, Identifier: dimension != "Day"), new("Requests", Numeric: true), new("Attempts", Numeric: true),
                new("Errors", Numeric: true), new("Tokens", Numeric: true), new("Unknown usage", Numeric: true, Secondary: true)],
            groups.Select(group => new HumanRow([group.Key, group.Totals.Requests.ToString(CultureInfo.InvariantCulture),
                group.Totals.Attempts.ToString(CultureInfo.InvariantCulture), group.Totals.Errors.ToString(CultureInfo.InvariantCulture),
                Number(group.Totals.Tokens.Total), group.Totals.UnknownUsageAttempts.ToString(CultureInfo.InvariantCulture)])), context);

    internal static async Task WriteHelp(TextWriter output, HumanOutputContext? context = null)
    {
        context ??= HumanOutputContext.Detect(output);
        foreach (var line in Help.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) { await output.WriteLineAsync(); continue; }
            var indent = new string(' ', line.TakeWhile(character => character == ' ').Count());
            await HumanTable.WriteTextAsync(output, line, context, indent);
        }
    }
}
