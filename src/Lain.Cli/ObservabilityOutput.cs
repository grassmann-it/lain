using System.Globalization;
using Lain.Core;

namespace Lain.Cli;

public static partial class CliApplication
{
    private static async Task<bool> WriteExtended(TextWriter output, object value)
    {
        switch (value)
        {
            case UsageAnalysisDto analysis:
                await output.WriteLineAsync($"{analysis.From:O} to {analysis.To:O} ({Cell(analysis.TimeZone)})");
                await output.WriteLineAsync($"Observed {analysis.ObservedAt:O}; cursor {analysis.Cursor}; coverage {(analysis.Complete ? "complete" : "incomplete")}");
                await WriteAnalysisTotals(output, analysis.Totals);
                await WritePerformance(output, analysis.Performance);
                foreach (var dimension in analysis.Dimensions)
                {
                    await output.WriteLineAsync($"Group by {Cell(dimension.Dimension)}; groups {dimension.Groups.Length}; {(dimension.Truncated ? "truncated" : "complete")}");
                    foreach (var group in dimension.Groups)
                    {
                        await output.WriteLineAsync($"  {Cell(group.Key)}");
                        await WriteAnalysisTotals(output, group.Totals, "    ");
                        await WritePerformance(output, group.Performance, "    ");
                    }
                }
                return true;
            case InspectorSnapshotDto snapshot:
                await output.WriteLineAsync($"Inspector {Cell(snapshot.State)}; source {Cell(snapshot.SourceId)}; observed {snapshot.ObservedAt:O}; coverage {(snapshot.Complete ? "complete" : "incomplete")}");
                await output.WriteLineAsync($"Cursor {snapshot.Cursor}; earliest {snapshot.EarliestCursor}; dropped events {snapshot.DroppedEvents}; streams {snapshot.Streams.Length}");
                await output.WriteLineAsync($"Host content lifetime {snapshot.Limits.ContentTtlSeconds}s; terminal lifetime {snapshot.Limits.TerminalTtlSeconds}s; content buffer {snapshot.Limits.ContentBytes} bytes; policy {Cell(snapshot.Limits.ContentPolicy)}");
                foreach (var stream in snapshot.Streams)
                {
                    await output.WriteLineAsync($"{Cell(stream.Id)}\t{Cell(stream.Scope)}\trequest {Cell(stream.RequestId)}\tattempt {Cell(stream.AttemptId)}\t{Cell(stream.State)}{(stream.Truncated ? "; truncated" : "")}");
                    await output.WriteLineAsync($"  {Cell(stream.Provider)} / {Cell(stream.Model)}; requested {Cell(stream.RequestedModel)}; account {Cell(stream.AccountId)}; route {Cell(stream.RouteId)}; HTTP {Number(stream.StatusCode)}");
                    await output.WriteLineAsync($"  Started {stream.StartedAt:O}; updated {stream.UpdatedAt:O}; first output {Number(stream.Measurements?.FirstOutputLatencyMs)} ms; duration {Number(stream.Measurements?.TotalDurationMs)} ms; tokens {Number(stream.Tokens?.Total)}");
                    await WriteDimensions(output, stream.Dimensions);
                }
                if (snapshot.Error is { } snapshotError) await output.WriteLineAsync(Cell(snapshotError));
                return true;
            case InspectorEventPageDto events:
                await output.WriteLineAsync($"Inspector source {Cell(events.SourceId)}; next cursor {events.NextCursor}; high watermark {events.HighWatermark}; earliest {events.EarliestCursor}; dropped events {events.DroppedEvents}; {(events.Truncated ? "truncated" : "complete page")}");
                if (events.GapReason is { } gap) await output.WriteLineAsync("Gap: " + Cell(gap));
                foreach (var item in events.Events)
                {
                    await output.WriteLineAsync($"{item.Cursor}\t{item.ObservedAt:O}\t{Cell(item.Kind)}\trequest {Cell(item.RequestId)}\tattempt {Cell(item.AttemptId)}\t{Cell(item.State)}{(item.Truncated ? "; truncated" : "")}");
                    await WriteDimensions(output, item.Dimensions);
                    if (item.ToolCallId is not null || item.ToolName is not null)
                        await output.WriteLineAsync($"  Tool {Cell(item.ToolName)}; call {Cell(item.ToolCallId)}");
                    if (item.Text is { } text)
                        await output.WriteLineAsync($"  {Cell(item.Channel)} (redaction {Cell(item.RedactionState)}): {Cell(text.Length > 2000 ? text[..2000] : text)}{(text.Length > 2000 ? " [text preview truncated; use --json for the retained event]" : "")}");
                    if (item.GapReason is { } eventGap) await output.WriteLineAsync("  Gap: " + Cell(eventGap));
                }
                return true;
            case ProviderCatalogDto catalog:
                await WriteProviderCatalog(output, catalog);
                return true;
            case ProviderOnboardingDto onboarding:
                await output.WriteLineAsync($"{Cell(onboarding.ProviderId)}: {ReadableState(onboarding.State)}; live check {LiveCheck(onboarding.LiveVerification)}");
                await WriteProvider(output, onboarding.Descriptor);
                await output.WriteLineAsync($"Accounts: {(onboarding.AccountIds.Length == 0 ? "none" : string.Join(", ", onboarding.AccountIds.Select(Cell)))}");
                await output.WriteLineAsync($"Missing credentials: {(onboarding.MissingCredentialFields.Length == 0 ? "none" : string.Join(", ", onboarding.MissingCredentialFields.Select(Cell)))}");
                if (onboarding.Error is { } error) await output.WriteLineAsync(Cell(error));
                return true;
            case ProviderDescriptorDto provider:
                await WriteProvider(output, provider);
                return true;
            case ProviderRouteDescriptorDto[] routes:
                await HumanTable.WriteTextAsync(output, "Free-only eligibility describes catalog controls; account access is not verified.");
                foreach (var route in routes) await WriteProviderRoute(output, route);
                return true;
            case RouteCostDecisionDto decision:
                await output.WriteLineAsync($"{Cell(decision.RouteId)}: {(decision.Allowed ? "allowed" : "blocked")}; policy {Cell(decision.CostPolicy)}; pricing {Cell(decision.PricingClass)}");
                foreach (var reason in decision.Reasons) await output.WriteLineAsync("  " + Cell(reason));
                return true;
            default: return false;
        }
    }

    private static async Task WriteAnalysisTotals(TextWriter output, UsageTotals totals, string indent = "")
    {
        await output.WriteLineAsync($"{indent}Requests {totals.Requests}; attempts {totals.Attempts}; errors {totals.Errors}; unknown usage attempts {totals.UnknownUsageAttempts}");
        await output.WriteLineAsync($"{indent}Tokens input {Number(totals.Tokens.Input)}; output {Number(totals.Tokens.Output)}; cached {Number(totals.Tokens.Cached)}; reasoning {Number(totals.Tokens.Reasoning)}; total {Number(totals.Tokens.Total)}");
    }

    private static async Task WritePerformance(TextWriter output, PerformanceSummaryDto performance, string indent = "")
    {
        await WriteMetric(output, "First output latency (ms)", performance.FirstOutputLatencyMs, indent);
        await WriteMetric(output, "Total duration (ms)", performance.TotalDurationMs, indent);
        await WriteMetric(output, "Output tokens per second", performance.OutputTokensPerSecond, indent);
        await output.WriteLineAsync(indent + "Rate basis: " + Cell(performance.RateBasis));
    }

    private static Task WriteMetric(TextWriter output, string name, MetricStatisticsDto metric, string indent) =>
        output.WriteLineAsync($"{indent}{name}: samples {metric.Samples}/{metric.Eligible} eligible; min {Number(metric.Minimum)}; mean {Number(metric.Mean)}; p50 {Number(metric.P50)}; p95 {Number(metric.P95)}; max {Number(metric.Maximum)}");

    private static async Task WriteDimensions(TextWriter output, AttemptDimensionsDto? dimensions)
    {
        if (dimensions is null) { await output.WriteLineAsync("  Group identities: unknown"); return; }
        await output.WriteLineAsync($"  Conversation {Cell(dimensions.ConversationId)}; turn {Cell(dimensions.TurnId)}; worker {Cell(dimensions.WorkerId)}; parent worker {Cell(dimensions.ParentWorkerId)}; parent request {Cell(dimensions.ParentRequestId)}; tool call {Cell(dimensions.ToolCallId)}");
        await output.WriteLineAsync($"  Family {Cell(dimensions.ModelFamily)}; manufacturer {Cell(dimensions.Manufacturer)}; inference {Cell(dimensions.InferenceProvider)}; gateway {Cell(dimensions.Gateway)}; billing account {Cell(dimensions.BillingAccountId)}; requested reasoning {Cell(dimensions.ReasoningEffort)}; effective reasoning {Cell(dimensions.EffectiveReasoningEffort)}; mode {Cell(dimensions.Mode)}");
        if (dimensions.ResponseId is not null || dimensions.PreviousResponseId is not null)
            await output.WriteLineAsync($"  Response {Cell(dimensions.ResponseId)}; previous response {Cell(dimensions.PreviousResponseId)}");
        if (dimensions.Evidence is { Length: > 0 } evidence)
            await output.WriteLineAsync("  Evidence: " + string.Join(", ", evidence.Select(e => $"{Cell(e.Field)} ({Cell(e.Source)}; {Cell(e.Key)})")));
    }

    private static async Task WriteProvider(TextWriter output, ProviderDescriptorDto provider)
    {
        await output.WriteLineAsync($"{Cell(provider.Name)} ({Cell(provider.Id)}) — {Cell(provider.Summary)}");
        await output.WriteLineAsync($"Account: {Cell(provider.AccountUrl)}");
        await output.WriteLineAsync($"Documentation: {Cell(provider.DocumentationUrl)}");
        await output.WriteLineAsync($"Adapter {ReadableState(provider.AdapterState)}; live check {LiveCheck(provider.LiveVerification)}; quota support {ReadableState(provider.QuotaSupport)}");
        foreach (var field in provider.Credentials)
            await output.WriteLineAsync($"Credential: {Cell(field.Id)} ({Cell(field.Label)}); {(field.Required ? "required" : "optional")}; {Cell(field.Kind)}{(field.Secret ? "; secret" : "")}");
        foreach (var step in provider.SetupSteps)
            await output.WriteLineAsync($"Setup: {Cell(step.Title)} — {Cell(step.Description)}{(step.Url is null ? "" : " " + Cell(step.Url))}");
        foreach (var plan in provider.BillingPlans)
        {
            await output.WriteLineAsync($"Budget: {Cell(plan.Id)}; {Cell(plan.Kind)}; scope {Cell(plan.Scope)}; {Number(plan.IncludedAmount)} {Cell(plan.Unit)}; reset {Cell(plan.ResetDescription)}; expires {plan.ExpiresAt?.ToString("O", CultureInfo.InvariantCulture) ?? "unknown"}");
            if (plan.RecurringPrice is { } price)
                await output.WriteLineAsync($"  Price {price.ToString(CultureInfo.InvariantCulture)} {Cell(plan.Currency)} / {Cell(plan.PriceInterval)}");
            foreach (var limit in plan.Limits ?? [])
                await output.WriteLineAsync($"  Limit {Cell(limit.Kind)}: {Number(limit.Value)} {Cell(limit.Unit)}; window {Cell(limit.Window)}; scope {Cell(limit.Scope)}");
            if (plan.Notes is { } notes) await output.WriteLineAsync("  " + Cell(notes));
        }
        if (provider.Routes.Length > 0) await HumanTable.WriteTextAsync(output, "Free-only eligibility describes catalog controls; account access is not verified.");
        foreach (var route in provider.Routes) await WriteProviderRoute(output, route);
        foreach (var restriction in provider.Restrictions) await output.WriteLineAsync("Restriction: " + Cell(restriction));
        foreach (var source in provider.Sources)
            await output.WriteLineAsync($"Source: {Cell(source.Url)}; checked {source.CheckedAt?.ToString("O", CultureInfo.InvariantCulture) ?? "unknown"}");
    }

    private static async Task WriteProviderRoute(TextWriter output, ProviderRouteDescriptorDto route)
    {
        var capabilities = route.Capabilities;
        await output.WriteLineAsync($"Route {Cell(route.Id)}: {Cell(route.Model)}; family {Cell(route.ModelFamily)}; protocol {Cell(route.Protocol)}; pricing {Cell(route.PricingClass)}; free-only {(route.FreeOnlyEligible ? "eligible" : "blocked")}; enforcement {Cell(route.FreeOnlyEnforcementKind)}");
        await output.WriteLineAsync($"  Manufacturer {Cell(route.Manufacturer)}; inference {Cell(route.InferenceProvider)}; gateway {Cell(route.Gateway)}; publisher {Cell(route.Publisher)} ({Cell(route.PublisherType)})");
        await output.WriteLineAsync($"  Context {Number(capabilities.ContextWindow)}; max output {Number(capabilities.MaximumOutput)}; tools {Known(capabilities.Tools)}; streaming {Known(capabilities.Streaming)}; reasoning {Known(capabilities.Reasoning)}; images {Known(capabilities.Images)}; structured output {Known(capabilities.StructuredOutput)}");
        if (capabilities.ReasoningModes is { Length: > 0 } modes)
            await output.WriteLineAsync("  Reasoning modes: " + string.Join(", ", modes.Select(Cell)));
        if (route.FreeOnlyEnforcement is { } enforcement) await output.WriteLineAsync("  Free-only enforcement: " + Cell(enforcement));
    }

    private static string Known(bool? value) => value is null ? "unknown" : value.Value ? "yes" : "no";
}
