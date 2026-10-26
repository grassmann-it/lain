using System.Text.Json;
using System.Text.Json.Nodes;
using Lain.Core;
using Lain.Host.Models;
using Lain.Host.Operations;
using Lain.Host.Providers;

namespace Lain.Host.Workers;

/// <summary>Offline, read-only resolution against the currently applied global runtime. Never applies a profile or probes providers.</summary>
public sealed class WorkerPlanningService(OwnStatePaths paths, ModelsService models,
    ProxyConfigurationWriter compiler, ProviderCostPolicy costPolicy, ProviderCatalogRegistry registry)
{
    private string BindingsPath => Path.Combine(paths.Proxy, "bindings.json");
    private AppliedProfileStatus Applied(long revision) => AppliedProfileStatus.Read(
        Path.Combine(paths.Root, "active-profile.json"), Path.Combine(paths.Proxy, "runtime.json"), revision);

    public WorkerPlan Plan(WorkerPlanRequest request)
    {
        try { return PlanCore(request); }
        catch (WorkerPlanException) { throw; }
        catch (RevisionConflictException) { throw new WorkerPlanException("revision_conflict", "Configuration changed; reload and replan.", 409); }
        catch (UnauthorizedAccessException) { throw new WorkerPlanException("provider_credential_unavailable", "The selected inference credential cannot be read; reconnect the affected account."); }
        catch (InvalidDataException) { throw new WorkerPlanException("provider_transport_invalid", "The selected inference transport is invalid; reconnect the affected account."); }
        catch (IOException) { throw new WorkerPlanException("provider_credential_unavailable", "The selected inference credential cannot be read; reconnect the affected account."); }
        catch (JsonException) { throw new WorkerPlanException("provider_transport_invalid", "The selected inference metadata is invalid; reconnect the affected account."); }
        catch (InvalidOperationException) { throw new WorkerPlanException("provider_transport_unavailable", "The selected inference transport is unavailable or ambiguous."); }
        catch (ArgumentException) { throw new WorkerPlanException("invalid_profile", "The saved profile cannot be used for worker planning; check routes, accounts and policies."); }
        catch (KeyNotFoundException) { throw new WorkerPlanException("invalid_profile", "The saved profile references unavailable configuration."); }
    }

    private WorkerPlan PlanCore(WorkerPlanRequest request)
    {
        WorkerPlanValidation.Validate(request);
        var snapshot = models.Snapshot;
        if (snapshot.Revision != request.Revision) throw new RevisionConflictException(request.Revision, snapshot.Revision);
        if (snapshot.RuntimeMode != "gateway") throw new WorkerPlanException("gateway_required", "Worker planning requires Gateway mode.");
        var profile = snapshot.Profiles.SingleOrDefault(item => item.Id == request.Policy.ProfileId)
            ?? throw new WorkerPlanException("unknown_profile", "The worker profile does not exist.", 404);
        var applied = Applied(snapshot.AppliedRevision);
        if (snapshot.AppliedRevision != request.Revision || applied.Id != profile.Id || applied.Revision != request.Revision)
            throw new WorkerPlanException("runtime_drift", "The requested profile and revision must already be applied to the current runtime.", 409);

        // Validate every reference, including unused purposes and later alternatives. Unknown IDs
        // never turn into aliases, virtual routers, implicit defaults, or silently ignored entries.
        foreach (var purpose in request.Policy.Purposes)
            foreach (var id in purpose.RouteIds)
            {
                var route = snapshot.Routes.SingleOrDefault(item => item.Id == id)
                    ?? throw new WorkerPlanException("unknown_route", "A worker purpose references an unknown configured route.");
                if (!profile.RouteIds.Contains(id, StringComparer.Ordinal))
                    throw new WorkerPlanException("route_not_in_profile", "Every worker route must belong to the selected profile.");
                if (IsInternalReview(route)) throw new WorkerPlanException("internal_route", "Internal Codex review models cannot execute worker tasks.");
            }
        foreach (var id in profile.RouteIds)
        {
            var route = snapshot.Routes.Single(item => item.Id == id);
            if (!costPolicy.Evaluate(snapshot, route, profile.Policy).Allowed)
                throw new WorkerPlanException("cost_policy_denied", "The selected profile's effective route cost policy rejects worker planning.");
        }
        var resolved = models.ResolveForApply(profile.Id, request.Revision);
        var activeRoutes = resolved.Routes.Where(route => route.Enabled).ToArray();
        var accountIds = activeRoutes.SelectMany(route => route.AccountIds).ToHashSet(StringComparer.Ordinal);
        var runtime = resolved.Configuration with { Routes = activeRoutes,
            Accounts = resolved.Configuration.Accounts.Where(account => accountIds.Contains(account.Id)).ToArray() };
        var description = compiler.Describe(runtime);
        var expectedBindings = RouteBindings.Compile(runtime, description.CredentialIds, description.RouteCredentials,
            registry, profile.Id, description.QualifiedProviderRouteIds, description.TransportBindings);
        var bindingBytes = ReadBindings();
        RequireMatchingBindings(expectedBindings, bindingBytes);
        var compatibility = new ModelCompatibilityCatalog(registry, description.QualifiedProviderRouteIds, description.TransportBindings);
        // Generate uses absolute artifact paths only as strings; it writes no files and invokes no client.
        var artifacts = new CodexProfileAdapter(models, compatibility, registry).Generate(profile.Id, request.Revision,
            Path.Combine(paths.Root, "worker-plan-lain"), Path.Combine(paths.Root, "worker-plan.models.json"), compiler.InferenceBaseUrl);
        var catalog = WorkerCatalog.Read(artifacts.Catalog);
        var selected = new Dictionary<string, (RouteDto Route, string? Reasoning, ModelCompatibilityDto Compatibility)>(StringComparer.Ordinal);
        foreach (var purpose in request.Policy.Purposes)
        {
            var failure = new WorkerPlanException("route_unavailable", "No enabled, visible worker route with enabled accounts is available for a purpose.");
            foreach (var id in purpose.RouteIds)
            {
                var route = resolved.Routes.Single(item => item.Id == id);
                if (!route.Enabled || !route.Visible || route.AccountIds.Length == 0 ||
                    !route.AccountIds.All(accountId => snapshot.Accounts.Any(account => account.Id == accountId && account.Enabled))) continue;
                if (!expectedBindings.Routes.TryGetValue(route.Alias, out var binding) || binding.Id != route.Id ||
                    !catalog.TryGetValue(route.Alias, out var entry) || entry.Hidden) continue;
                // Evaluate only the actual, profile-resolved policy. A hypothetical project override
                // cannot enable free-only on a permissive alias already running in the global proxy.
                if (!costPolicy.Evaluate(runtime, route).Allowed ||
                    request.Policy.CostPolicy == "free_only" &&
                    (route.Policy?.CostPolicy != "free_only" || binding.CostPolicy != "free_only" ||
                     binding.FreeOnlyEnforcement is null || binding.FreeOnlyEnforcement != costPolicy.RuntimeEnforcement(route)))
                {
                    failure = new("cost_policy_denied", "Project free_only requires an already enforced free_only route in the applied runtime.");
                    continue;
                }
                var routeCompatibility = compatibility.Resolve(route);
                if (WorkerCapabilityPolicy.Unsupported(routeCompatibility).Length > 0)
                {
                    failure = new("worker_capability_unsupported", "The route does not support the functions, streaming or tool continuation required by native coding workers.");
                    continue;
                }
                if (purpose.ReasoningEffort is { } effort && !entry.Supported.Contains(effort))
                {
                    failure = new("reasoning_mismatch", "The requested reasoning effort is not supported by the route's current exported Codex catalog.");
                    continue;
                }
                selected.Add(purpose.Name, (route, purpose.ReasoningEffort ?? entry.Default, routeCompatibility));
                break;
            }
            if (!selected.ContainsKey(purpose.Name)) throw failure;
        }
        var notes = new List<string>
        {
            "Read-only snapshot; revalidate profile and revision before dispatch. This plan is not a credential, reservation or hard spend cap.",
            "Routes are selected once in declared order. No cross-route retry, task splitting or autonomous scheduling.",
            "Write paths are declarations for conflict checks, not per-path access controls. Native client sandbox/worktree isolation must be verified by the runner.",
            "Reasoning defaults and supported values come from the current Codex profile export; unknown defaults remain null. No live provider or quota checks were performed.",
            "Explicitly unsupported functions, streaming or tool continuation are denied. Unknown capability evidence remains unqualified and permits bounded execution of explicitly selected routes without claiming provider-tested acceptance. An executable runner must reject null reasoning effort."
        };
        var tasks = request.Tasks.Select(task =>
        {
            var choice = selected[task.Purpose];
            var contextMode = request.Policy.Purposes.Single(purpose => purpose.Name == task.Purpose).ContextMode ?? "inherit";
            if (contextMode == "native")
                notes.Add($"Task {task.Id}: explicit native context retains native file/shell tools and project rules. External plugins, apps, orchestrator MCP and individually discovered direct MCP servers must be disabled and verified before execution. Direct MCP names must contain only ASCII letters, digits, '_' or '-'; other names block execution. Automatic skill instructions and the skill catalog are explicitly suppressed, including guidance normally supplied by skills; tasks requiring that guidance or external tools must use inherit. This is not a guarantee of a tool count below 128, a token budget or provider acceptance.");
            var unqualified = WorkerCapabilityPolicy.Unqualified(choice.Compatibility);
            if (unqualified.Length > 0)
            {
                var capabilities = choice.Compatibility.Capabilities;
                notes.Add($"Task {task.Id} (route {choice.Route.Id}): capability evidence: functions={capabilities.Functions.State}, streaming={capabilities.Streaming.State}, toolContinuation={capabilities.ToolContinuation.State}. Unknown values remain unqualified and do not by themselves block execution. No provider-tested acceptance is claimed.");
            }
            if (choice.Reasoning is null)
                notes.Add($"Task {task.Id} (route {choice.Route.Id}): execution blocked; the exported reasoning default is unknown. Do not invent none or inherit an unchecked global effort.");
            return new WorkerResolvedTask(task.Id, task.Purpose, task.Prompt, choice.Route.Id, choice.Route.Alias,
                choice.Route.Provider, choice.Route.AccountIds.ToArray(), choice.Reasoning, (task.WritePaths ?? []).ToArray(), choice.Compatibility, contextMode);
        }).ToArray();
        var current = models.Snapshot;
        if (current.Revision != request.Revision || current.AppliedRevision != request.Revision ||
            current.RuntimeMode != "gateway" || Applied(current.AppliedRevision) != applied || !ReadBindings().AsSpan().SequenceEqual(bindingBytes))
            throw new WorkerPlanException("runtime_drift", "The profile or runtime changed while planning; reload and replan.", 409);
        return new(1, request.Revision, profile.Id, request.Policy.MaxWorkers, tasks, notes.ToArray());
    }

    private byte[] ReadBindings()
    {
        try
        {
            using var stream = File.OpenRead(BindingsPath);
            if (stream.Length is < 2 or > 4 * 1024 * 1024) throw new InvalidDataException();
            var bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            return bytes;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { throw new WorkerPlanException("runtime_drift", "The applied runtime bindings are missing or unreadable; apply the selected profile before planning.", 409); }
    }

    private static void RequireMatchingBindings(CoreBindings expected, byte[] actual)
    {
        try
        {
            using var document = JsonDocument.Parse(actual);
            RejectDuplicates(document.RootElement);
            if (!JsonNode.DeepEquals(JsonSerializer.SerializeToNode(expected), JsonNode.Parse(actual))) throw new JsonException();
        }
        catch (JsonException) { throw new WorkerPlanException("runtime_drift", "Applied runtime bindings do not match the selected profile, policies and compiled transports.", 409); }
    }

    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new JsonException();
                RejectDuplicates(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RejectDuplicates(item);
    }

    private static bool IsInternalReview(RouteDto route) => route.Provider == "codex" && route.UpstreamModel == "codex-auto-review";
}

internal sealed record WorkerCatalogEntry(bool Hidden, string? Default, HashSet<string> Supported);

internal static class WorkerCatalog
{
    internal static Dictionary<string, WorkerCatalogEntry> Read(string catalog)
    {
        using var document = JsonDocument.Parse(catalog);
        var entries = new Dictionary<string, WorkerCatalogEntry>(StringComparer.Ordinal);
        foreach (var model in document.RootElement.GetProperty("models").EnumerateArray())
        {
            var slug = model.GetProperty("slug").GetString() ?? throw new JsonException();
            var supported = new HashSet<string>(StringComparer.Ordinal);
            if (model.TryGetProperty("supported_reasoning_levels", out var levels) && levels.ValueKind == JsonValueKind.Array)
                foreach (var level in levels.EnumerateArray())
                    if (level.ValueKind == JsonValueKind.Object && level.TryGetProperty("effort", out var effort) &&
                        effort.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(effort.GetString())) supported.Add(effort.GetString()!);
            var exportedDefault = model.TryGetProperty("default_reasoning_level", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            // Empty/unknown supported metadata cannot prove even an adapter's legacy placeholder "none".
            var knownDefault = exportedDefault is not null && supported.Contains(exportedDefault) ? exportedDefault : null;
            var hidden = !model.TryGetProperty("visibility", out var visibility) || visibility.GetString() != "list";
            if (!entries.TryAdd(slug, new(hidden, knownDefault, supported))) throw new JsonException();
        }
        return entries;
    }
}
