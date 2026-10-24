using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lain.Core;

namespace Lain.Desktop.Providers;

public sealed partial class ProviderCatalogView
{
    private string? _snapshot;
    private ConnectionMethodCatalogDto? _installedMethods;

    public void SetConnectionMethods(ConnectionMethodCatalogDto methods) => _installedMethods = methods;

    /// <summary>Render only secret-free descriptors and account counts; never infer a connection from a configured key.</summary>
    public void SetCatalog(ProviderCatalogDto catalog, IReadOnlyList<AccountDto>? accounts = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        catalog = ProviderInventory.Merge(catalog, _installedMethods);
        var counts = (accounts ?? []).GroupBy(a => ProviderInventory.Canonical(a.Provider), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        // The source observation time can change without changing the documentation.
        // Keep focused controls and expanded cards on identical passive refreshes.
        var snapshot = JsonSerializer.Serialize(new { catalog.Providers, Methods = _installedMethods?.Methods, Accounts = counts.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase) });
        if (_loaded && snapshot == _snapshot) return;
        _snapshot = snapshot;
        ApplyCatalog(catalog.Providers.Select(provider => Present(provider, counts.GetValueOrDefault(provider.Id))).ToArray());
    }

    private CatalogCard Present(ProviderDescriptorDto provider, int accountCount)
    {
        var methods = (_installedMethods?.Methods ?? []).Where(method => method.Provider == provider.Id).Select(method => new CatalogFact(method.Title,
            Join(method.Availability == "available" ? "Installed method available; account access not verified" : "Unavailable on this host",
                "Purposes: " + string.Join(", ", method.Purposes), method.RequiresCore ? "Requires Gateway Core" : null,
                method.Availability == "available" ? method.Description : method.Guidance))).ToArray();
        var access = new List<CatalogFact>
        {
            new("Account setup", accountCount == 0 ? "No account configured. You can review requirements before adding credentials." : $"{accountCount} account{(accountCount == 1 ? "" : "s")} configured; connection status is shown separately."),
            new("Adapter", Adapter(provider.AdapterState)),
            new("Integration verification", VerificationState(provider.LiveVerification))
        };
        access.AddRange(provider.Credentials.Select(field => new CatalogFact(
            $"{field.Label} · {(field.Required ? "required" : "optional")}",
            Join(Human(field.Kind), field.Secret ? "Private credential" : null, field.Help))));
        if (provider.Credentials.Length == 0) access.Add(new("Credentials", "Requirements not reported"));

        var budgets = new List<CatalogFact>
        {
            new("Live remaining budget", provider.QuotaSupport == "unsupported"
                ? "Unknown — no automatic budget source is available." : "Unknown — not checked with a live account."),
            new("Automatic budget checks", QuotaSupport(provider.QuotaSupport))
        };
        foreach (var plan in provider.BillingPlans)
        {
            var lines = new List<string> { AccessType(plan.Kind), $"Scope: {Known(plan.Scope)}", $"Accounting unit: {Known(plan.Unit)}" };
            lines.Add(plan.IncludedAmount is { } amount && double.IsFinite(amount) && amount >= 0
                ? $"Documented allowance: {Number(amount)} {Known(plan.Unit)}" : "Documented allowance: Unknown");
            if (!string.IsNullOrWhiteSpace(plan.ResetDescription)) lines.Add($"Reset / availability: {plan.ResetDescription}");
            else if (plan.Kind is "free" or "subscription") lines.Add("Reset: Unknown");
            if (plan.ExpiresAt is { } expiry) lines.Add($"Expires: {expiry.ToLocalTime():dd MMM yyyy · HH:mm zzz}");
            else if (plan.Kind == "trial") lines.Add("Expiry: Unknown unless stated in the setup notes");
            if (plan.RecurringPrice is { } price)
                lines.Add($"Documented price: {price.ToString("0.##", CultureInfo.InvariantCulture)} {Known(plan.Currency)}{(string.IsNullOrWhiteSpace(plan.PriceInterval) ? "" : $" / {plan.PriceInterval}")}");
            else if (plan.Kind is "subscription" or "payg" or "unknown") lines.Add("Price / rates: Not reported");
            if (!string.IsNullOrWhiteSpace(plan.Notes)) lines.Add(plan.Notes);
            budgets.Add(new(plan.Name, string.Join("\n", lines)));
        }
        if (provider.BillingPlans.Length == 0) budgets.Add(new("Access type", "Unknown pricing"));

        var limits = provider.BillingPlans.SelectMany(plan => (plan.Limits ?? []).Select(limit => new CatalogFact(
            $"{plan.Name} · {Human(limit.Kind)}", Join(
                limit.Value is { } value && double.IsFinite(value) && value >= 0 ? $"{Number(value)} {Known(limit.Unit)}" : $"Unknown {Known(limit.Unit)}",
                string.IsNullOrWhiteSpace(limit.Window) ? null : $"Window: {limit.Window}",
                string.IsNullOrWhiteSpace(limit.Scope) ? null : $"Scope: {limit.Scope}", limit.Notes)))).ToArray();
        if (limits.Length == 0) limits = [new("Rate and parallelism limits", "Unknown — no verified limit information is available in this catalog entry.")];

        var routes = provider.Routes.Select(route => new CatalogRoute(route.Name, RouteFacts(provider, route))).ToArray();
        var links = new List<CatalogLink> { new("Account / API console", provider.AccountUrl), new("Documentation", provider.DocumentationUrl) };
        links.AddRange(provider.SetupSteps.Where(step => !string.IsNullOrWhiteSpace(step.Url)).Select(step => new CatalogLink(step.Title, step.Url!)));
        links.AddRange(provider.Sources.Select(source => new CatalogLink(source.Title, source.Url)));
        links.AddRange(provider.Routes.SelectMany(route => route.Sources ?? []).Select(source => new CatalogLink(source.Title, source.Url)));
        var allSources = provider.Sources.Concat(provider.Routes.SelectMany(route => route.Sources ?? []))
            .DistinctBy(source => (source.Url, source.CheckedAt, source.Notes)).ToArray();
        var verification = allSources.Length == 0 ? "Official source checks have not been reported." : string.Join("\n\n", allSources.Select(source =>
            Join(source.Title, source.CheckedAt is { } checkedAt ? $"Checked {checkedAt:dd MMM yyyy}" : "Check date unknown", source.Notes)));
        var protocols = provider.Routes.Select(route => Protocol(route.Protocol)).Distinct(StringComparer.Ordinal).ToArray();
        var accessTypes = provider.BillingPlans.Select(plan => AccessType(plan.Kind)).Distinct(StringComparer.Ordinal).DefaultIfEmpty("Unknown pricing").ToArray();
        var search = string.Join(" ", new[] { provider.Id, provider.Name, provider.Summary }
            .Concat(protocols).Concat(accessTypes).Concat(provider.Routes.Select(route => $"{route.Model} {route.Name} {route.ModelFamily} {route.Manufacturer} {route.InferenceProvider} {route.Gateway}")));
        return new(provider.Id, provider.Name, provider.Summary, search, accountCount,
            provider.AccountUrl, provider.DocumentationUrl, Readiness(provider), IsLiveVerified(provider.LiveVerification), accessTypes, protocols,
            access.ToArray(), budgets.ToArray(), limits, routes, methods,
            provider.SetupSteps.Select(step => Join(step.Title, step.Description)).ToArray(), provider.Restrictions,
            links.DistinctBy(link => link.Url).ToArray(), verification);
    }

    private static CatalogFact[] RouteFacts(ProviderDescriptorDto provider, ProviderRouteDescriptorDto route)
    {
        var capabilities = route.Capabilities;
        var facts = new List<CatalogFact>
        {
            new("Model / family", Join(route.Model, route.ModelFamily)),
            new("Provider roles", Join($"Model manufacturer: {Known(route.Manufacturer)}", $"Inference provider: {Known(route.InferenceProvider)}", $"Gateway: {Known(route.Gateway)}")),
            new("Protocol / endpoint", Join(Protocol(route.Protocol), Known(route.BaseUrl))),
            new("Billing plans", route.BillingPlanIds.Length == 0 ? "Not reported" : string.Join(", ", route.BillingPlanIds.Select(id => provider.BillingPlans.FirstOrDefault(plan => plan.Id == id)?.Name ?? id))),
            new("Price class", Human(route.PricingClass)),
            new("Free-only routing", Join(
                route.FreeOnlyEligible && route.PricingClass == "free" ? "Documented free access" : "Paid or unknown pricing is excluded",
                "Strict free-only access requires qualified runtime controls; availability differs by connection. Catalog documentation does not enable it or change the applied policy.",
                route.FreeOnlyEnforcement)),
            new("Context window / maximum output", $"{Tokens(capabilities.ContextWindow)} / {Tokens(capabilities.MaximumOutput)}"),
            new("Tools / streaming / reasoning", $"{Support(capabilities.Tools)} / {Support(capabilities.Streaming)} / {Support(capabilities.Reasoning)}"),
            new("Images / structured output", $"{Support(capabilities.Images)} / {Support(capabilities.StructuredOutput)}"),
            new("Reasoning modes", capabilities.ReasoningModes is { Length: > 0 } modes ? string.Join(", ", modes) : "Unknown")
        };
        if (!string.IsNullOrWhiteSpace(route.Publisher) || !string.IsNullOrWhiteSpace(route.PublisherType))
            facts.Add(new("Model / bot publisher", Join(Known(route.Publisher), Human(route.PublisherType))));
        if (route.Restrictions is { Length: > 0 }) facts.Add(new("Connection restrictions", string.Join("\n", route.Restrictions)));
        if (route.Sources is { Length: > 0 }) facts.Add(new("Connection evidence", string.Join("\n", route.Sources.Select(source =>
            Join(source.Title, source.CheckedAt is { } date ? $"Checked {date:dd MMM yyyy}" : "Check date unknown", source.Notes)))));
        return facts.ToArray();
    }

    private static string AccessType(string? kind) => kind?.ToLowerInvariant() switch
    {
        "free" => "Free access", "trial" => "One-time trial", "subscription" => "Subscription", "payg" => "Pay as you go", _ => "Unknown pricing"
    };
    private static bool IsLiveVerified(string? state) => state is "verified" or "live_verified";
    private static string Readiness(ProviderDescriptorDto provider)
    {
        if (IsLiveVerified(provider.LiveVerification)) return "Live verified";
        return provider.AdapterState?.ToLowerInvariant() switch
        {
            "mock_tested" or "mock-tested" => "Mock tested · live unverified",
            "documented" or "documentation_only" => "Documented · live unverified",
            "prepared" => "Prepared · live unverified",
            "available" => "Adapter available · live unverified",
            "unsupported" => "Documented · adapter unavailable",
            _ => $"{Human(provider.AdapterState)} · live unverified"
        };
    }
    private static string Adapter(string? state) => state?.ToLowerInvariant() switch
    {
        "available" => "Available; live access still requires credentials and verification",
        "prepared" => "Prepared; not yet verified with a live account",
        "documented" or "documentation_only" => "Documented; adapter execution is not verified",
        "mock_tested" or "mock-tested" => "Mock tested; no live provider connection has been verified",
        "unsupported" => "Not supported", "unavailable" => "Not available", _ => Known(Human(state))
    };
    private static string VerificationState(string? state) => state?.ToLowerInvariant() switch
    {
        "not_tested" or "unverified" => "Not tested with live credentials",
        "verified" or "live_verified" => "Live integration verified; check each configured account separately", "failed" => "Last verification failed", _ => Known(Human(state))
    };
    private static string QuotaSupport(string? state) => state?.ToLowerInvariant() switch
    {
        "unsupported" => "Not supported; refer to the provider account console",
        "supported" => "Supported where the provider exposes account budget data",
        "prepared" => "Prepared; not checked with live credentials", _ => Known(Human(state))
    };
    private static string Protocol(string? value) => value?.ToLowerInvariant() switch
    {
        "openai_chat" or "openai_chat_completions" or "openai-chat" or "openai-chat-completions" => "OpenAI Chat Completions",
        "openai_responses" or "openai-responses" => "OpenAI Responses", "anthropic_messages" or "anthropic-messages" => "Anthropic Messages",
        "gemini" or "google_gemini" => "Gemini API", _ => Known(Human(value))
    };
    private static string Tokens(long? value) => value is >= 0 ? $"{value.Value.ToString("N0", CultureInfo.InvariantCulture)} tokens" : "Unknown";
    private static string Number(double value) => value.ToString("#,0.###", CultureInfo.InvariantCulture);
    private static string Support(bool? value) => value switch { true => "Supported", false => "Not supported", _ => "Unknown" };
    private static string Known(string? value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : value;
    private static string Human(string? value) => string.IsNullOrWhiteSpace(value) ? "Unknown" : Regex.Replace(value.Replace('_', ' '), "(?<=[a-z])(?=[A-Z])", " ");
    private static string Join(params string?[] values) => string.Join(" · ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
}
