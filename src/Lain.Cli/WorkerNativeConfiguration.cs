using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lain.Core;

namespace Lain.Cli;

public sealed record WorkerNativeBinding(string CodexHome, string ProfileId, long Revision, string GenerationId,
    string ConfigurationHash, string CatalogHash, string CatalogPath, string BaseUrl, string AuthCommand,
    string[] AuthArguments, long AuthTimeoutMs, long AuthRefreshIntervalMs)
{
    // Fresh per binding/run: TOML tables merge recursively, even inline tables.
    // A predictable generation-only key could inherit project auth or headers.
    public string ProviderId { get; } = "lain_worker_" + Guid.NewGuid().ToString("N");
}
public sealed record WorkerNativeConfigurationProof(string ClientVersion, string GenerationId, string Cwd, string Model, string ReasoningEffort,
    string ContextMode = "inherit", string[]? ContextOverrides = null);
public sealed record WorkerNativePreparedConfiguration(string[] Overrides, WorkerNativeConfigurationProof Proof);
public sealed class WorkerNativeConfigurationException : IOException
{
    public string Code { get; }
    internal WorkerNativeConfigurationException(string code)
        : base("Worker native configuration verification refused: " + code + ". No worker turn was started.") => Code = code;
}

/// <summary>
/// No-turn native configuration gate. Never returns/logs raw configuration, RPC errors or stderr.
/// Protocol: public rust-v0.164.0, commit 6aaac694803004a63c6f393d153c96d22521a6b9,
/// app-server-protocol/src/protocol/v2/config.rs and app-server/src/config_manager_service.rs.
/// Both probe and exec omit native --profile (unsupported by app-server) and use
/// identical binding-derived session overrides with a fresh provider identity.
/// Native project/base/managed layers still apply; managed conflicts are refused.
/// No debug-only config hook, temporary replacement home, config/write or thread/turn RPC is used.
/// Native startup may initialize its own state. Write sources must already be trusted.
/// </summary>
public static class WorkerNativeConfiguration
{
    public const string ClientVersion = "0.164.0";
    private const int MaximumOutputChars = 2 * 1024 * 1024;
    // Verified in the pinned native ConfigToml/SkillsConfig schema. Zero is not a
    // valid max_context_tokens value. include_instructions=false suppresses the
    // automatic block/catalog; it does not disable loading AGENTS.md rules.
    private static readonly string[] NativeContextOverrides = [
        "features.plugins=false", "features.apps=false", "orchestrator.mcp.enabled=false",
        "cloud.skills.enabled=false", "skills.include_instructions=false", "skills.max_context_tokens=1"
    ];

    public static WorkerNativeBinding ReadInstalledBinding(string codexHome, string profileId, long revision)
    {
        var home = Path.GetFullPath(codexHome);
        var status = CodexArtifactInstaller.Inspect(home, profileId, revision);
        Require(status is { State: "installed", Stale: false, GenerationId: not null }, "installed_binding_unavailable");
        var configuration = Read(Path.Combine(home, "lain.config.toml"));
        var settings = GeneratedSettings(configuration);
        var supportedSettings = new HashSet<string>(StringComparer.Ordinal) {
            "model", "model_reasoning_effort", "model_provider", "model_catalog_json", "web_search",
            "model_providers.lain.name", "model_providers.lain.base_url", "model_providers.lain.wire_api",
            "model_providers.lain.auth.command", "model_providers.lain.auth.args", "model_providers.lain.auth.timeout_ms",
            "model_providers.lain.auth.refresh_interval_ms"
        };
        Require(settings.Keys.All(supportedSettings.Contains), "unsupported_installed_setting");
        string Setting(string key) => String(settings.GetValueOrDefault(key));
        Require(Setting("model_provider") == "lain" && Setting("model_providers.lain.wire_api") == "responses" &&
            Setting("model_providers.lain.name") == "lain" && Setting("web_search") == "disabled", "installed_provider_mismatch");
        var catalogPath = Path.Combine(home, ".lain-artifacts", status.GenerationId!, "lain.models.json");
        Require(SamePath(Setting("model_catalog_json"), catalogPath), "installed_catalog_mismatch");
        var url = Setting("model_providers.lain.base_url");
        Require(Uri.TryCreate(url, UriKind.Absolute, out var endpoint) && endpoint.IsLoopback && endpoint.Scheme == "http" &&
            endpoint.UserInfo.Length == 0 && endpoint.Query.Length == 0 && endpoint.Fragment.Length == 0, "installed_endpoint_invalid");
        var command = Setting("model_providers.lain.auth.command");
        Require(Path.IsPathFullyQualified(command), "installed_auth_invalid");
        var args = Strings(settings.GetValueOrDefault("model_providers.lain.auth.args"));
        Require(args.SequenceEqual(new[] { "auth", "token", "--audience", "codex" }), "installed_auth_invalid");
        var timeout = Number(settings.GetValueOrDefault("model_providers.lain.auth.timeout_ms"));
        var refresh = Number(settings.GetValueOrDefault("model_providers.lain.auth.refresh_interval_ms"));
        Require(timeout > 0 && refresh > 0, "installed_auth_invalid");
        var binding = new WorkerNativeBinding(home, profileId, revision, status.GenerationId!, Hash(configuration), Hash(Read(catalogPath)),
            catalogPath, url, command, args, timeout, refresh);
        AssertCurrent(binding);
        return binding;
    }

    /// <summary>Use these SAME -c values for the real exec invocation and the probe.</summary>
    public static string[] ConfigurationOverrides(WorkerResolvedTask task, WorkerNativeBinding binding,
        IReadOnlyList<string>? disabledMcpServers = null)
    {
        Require(task.ContextMode is "inherit" or "native", "invalid_context_mode");
        Require(task.ContextMode != "native" || disabledMcpServers is not null, "context_discovery_required");
        Require(task.ContextMode == "native" || disabledMcpServers is null or { Count: 0 }, "unexpected_context_overrides");
        AssertCurrent(binding);
        ValidateCatalog(binding, task);
        static string Q(string value) => JsonSerializer.Serialize(value);
        var provider = "{name=" + Q("lain worker") + ",base_url=" + Q(binding.BaseUrl) +
            ",wire_api=\"responses\",requires_openai_auth=false,supports_websockets=false,supports_standalone_web_search=false," +
            "query_params={},http_headers={},env_http_headers={},request_max_retries=0,stream_max_retries=0," +
            "auth={command=" + Q(binding.AuthCommand) + ",args=[" + string.Join(",", binding.AuthArguments.Select(Q)) +
            "],timeout_ms=" + binding.AuthTimeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ",refresh_interval_ms=" + binding.AuthRefreshIntervalMs.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ",cwd=" + Q(binding.CodexHome) + "}}";
        string[] overrides = ["model_provider=" + Q(binding.ProviderId), "model_providers." + binding.ProviderId + "=" + provider,
            "model_catalog_json=" + Q(binding.CatalogPath), "model=" + Q(task.Model),
            "model_reasoning_effort=" + Q(task.ReasoningEffort!),
            "sandbox_mode=" + Q(task.WritePaths.Length == 0 ? "read-only" : "workspace-write"),
            "agents.enabled=false", "features.multi_agent=false", "features.multi_agent_v2=false", "web_search=\"disabled\""];
        if (task.ContextMode == "inherit") return overrides;
        var names = disabledMcpServers!.ToArray();
        Require(names.Length <= 1024 && names.Distinct(StringComparer.Ordinal).Count() == names.Length, "invalid_mcp_server_names");
        // The 0.164 CLI override parser splits on dots (not TOML-aware quoted
        // segments). Refuse unsupported names instead of disabling the wrong key.
        Require(names.All(name => name.Length is > 0 and <= 256 &&
            name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')), "unsupported_mcp_server_name");
        return [.. overrides, .. NativeContextOverrides, .. names.Order(StringComparer.Ordinal).Select(name => "mcp_servers." + name + ".enabled=false")];
    }

    /// <summary>Discover direct server names via public config/read only, then
    /// verify the final overrides. The caller must reuse Overrides verbatim for exec.</summary>
    public static async Task<WorkerNativePreparedConfiguration> PrepareAsync(string clientPath, string cwd, WorkerResolvedTask task,
        WorkerNativeBinding expected, CancellationToken ct = default, int timeoutMilliseconds = 5000, string? sourceRepositoryRoot = null)
    {
        string[]? names = null;
        if (task.ContextMode == "native")
        {
            // Discovery starts no thread or turn. Plugins/apps,
            // cloud skills and orchestrator MCP are already off in this probe.
            var discovery = await ReadResponsesAsync(clientPath, cwd, task, ConfigurationOverrides(task, expected, []), ct, timeoutMilliseconds);
            ValidateResponsesCore(expected, cwd, task, discovery.Initialize, discovery.Configuration, discovery.Requirements,
                sourceRepositoryRoot, discoveringContext: true);
            names = McpServers(Get(discovery.Configuration, "config")).EnumerateObject().Select(server => server.Name).ToArray();
        }
        var overrides = ConfigurationOverrides(task, expected, names);
        var proof = await VerifyAsync(clientPath, cwd, task, expected, overrides, ct, timeoutMilliseconds, sourceRepositoryRoot, names);
        return new(overrides, proof);
    }

    /// <summary>--model/--sandbox exec options are represented by equivalent session config values.</summary>
    public static string[] ProbeArguments(WorkerResolvedTask task, IReadOnlyList<string> configurationOverrides)
    {
        var args = new List<string> { "--no-daemon", "app-server", "--stdio" };
        foreach (var value in configurationOverrides) { args.Add("-c"); args.Add(value); }
        return args.ToArray();
    }

    public static async Task<WorkerNativeConfigurationProof> VerifyAsync(string clientPath, string cwd, WorkerResolvedTask task,
        WorkerNativeBinding expected, IReadOnlyList<string> configurationOverrides, CancellationToken ct = default, int timeoutMilliseconds = 5000,
        string? sourceRepositoryRoot = null, IReadOnlyList<string>? disabledMcpServers = null)
    {
        AssertCurrent(expected);
        ValidateCatalog(expected, task);
        Require(configurationOverrides.SequenceEqual(ConfigurationOverrides(task, expected, disabledMcpServers)), "probe_overrides_mismatch");
        var responses = await ReadResponsesAsync(clientPath, cwd, task, configurationOverrides, ct, timeoutMilliseconds);
        ValidateResponses(expected, cwd, task, responses.Initialize, responses.Configuration, responses.Requirements, sourceRepositoryRoot);
        AssertCurrent(expected);
        var contextOverrides = task.ContextMode == "native"
            ? configurationOverrides.Where(value => NativeContextOverrides.Contains(value, StringComparer.Ordinal) || value.StartsWith("mcp_servers.", StringComparison.Ordinal)).ToArray()
            : null;
        return new(ClientVersion, expected.GenerationId, cwd, task.Model, task.ReasoningEffort!, task.ContextMode, contextOverrides);
    }

    private sealed record Responses(JsonElement Initialize, JsonElement Configuration, JsonElement Requirements);
    private static async Task<Responses> ReadResponsesAsync(string clientPath, string cwd, WorkerResolvedTask task,
        IReadOnlyList<string> configurationOverrides, CancellationToken ct, int timeoutMilliseconds)
    {
        if (!Path.IsPathFullyQualified(clientPath) || !File.Exists(clientPath) || !Path.IsPathFullyQualified(cwd) ||
            !Directory.Exists(cwd) || timeoutMilliseconds is < 1 or > 30000 || task.ReasoningEffort is null)
            throw Failure("invalid_probe_input");
        Require(OperatingSystem.IsLinux() && File.Exists("/usr/bin/setsid"), "probe_containment_unavailable");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeoutMilliseconds);
        using var process = new Process { StartInfo = new("/usr/bin/setsid") {
            WorkingDirectory = cwd, UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        } };
        process.StartInfo.ArgumentList.Add("--wait");
        process.StartInfo.ArgumentList.Add("--");
        process.StartInfo.ArgumentList.Add(clientPath);
        foreach (var argument in ProbeArguments(task, configurationOverrides)) process.StartInfo.ArgumentList.Add(argument);
        // Match the worker's inherited environment. A different CODEX_HOME cannot
        // prove this binding; the native initialize response is checked below.
        Task? stderr = null;
        try
        {
            Require(process.Start(), "probe_start_failed");
            stderr = DiscardStderr(process.StandardError, deadline);
            var reader = new RpcReader(process.StandardOutput);
            async Task<JsonElement> Request(int id, string method, object? parameters = null)
            {
                var request = new Dictionary<string, object?> { ["id"] = id, ["method"] = method };
                if (parameters is not null) request["params"] = parameters;
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), deadline.Token);
                await process.StandardInput.FlushAsync(deadline.Token);
                for (var notifications = 0; notifications < 64; notifications++)
                {
                    var line = await reader.Line(deadline.Token);
                    Require(line is not null, "probe_unavailable");
                    using var message = JsonDocument.Parse(line!);
                    Unique(message.RootElement);
                    var root = message.RootElement;
                    if (Get(root, "id") is not { } responseId)
                    {
                        Require(Get(root, "method") is { ValueKind: JsonValueKind.String } &&
                            Get(root, "result") is null && Get(root, "error") is null, "invalid_rpc_notification");
                        continue;
                    }
                    Require(responseId.ValueKind == JsonValueKind.Number && responseId.TryGetInt32(out var received) && received == id &&
                        Get(root, "method") is null, "unexpected_rpc_message");
                    Require(Get(root, "error") is null, "native_config_read_refused");
                    return (Get(root, "result") ?? throw Failure("missing_rpc_result")).Clone();
                }
                throw Failure("notification_limit");
            }
            var initialized = await Request(1, "initialize", new {
                clientInfo = new { name = "lain_worker_config_probe", version = "1.0.0" },
                capabilities = new { experimentalApi = true, explicitGatewayOauth = true }
            });
            await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\"}".AsMemory(), deadline.Token);
            await process.StandardInput.FlushAsync(deadline.Token);
            var configuration = await Request(2, "config/read", new { includeLayers = true, cwd });
            var requirements = await Request(3, "configRequirements/read");
            return new(initialized, configuration, requirements);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw Failure(stderr?.IsFaulted == true ? "probe_output_limit" : "probe_timeout"); }
        catch (JsonException) { throw Failure("invalid_native_json"); }
        catch (System.ComponentModel.Win32Exception) { throw Failure("probe_start_failed"); }
        catch (InvalidOperationException) { throw Failure("probe_unavailable"); }
        catch (IOException error) when (error is not WorkerNativeConfigurationException)
        { throw Failure("probe_io_failed"); }
        finally
        {
            deadline.Cancel();
            KillContained(process);
            if (stderr is not null)
                try { await stderr.WaitAsync(TimeSpan.FromSeconds(1)); }
                catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException or TimeoutException) { }
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(1)); }
            catch (Exception error) when (error is InvalidOperationException or TimeoutException) { }
        }
    }

    /// <summary>Pure protocol validation for offline fixtures. Inputs are RPC result objects, not envelopes.</summary>
    public static void ValidateResponses(WorkerNativeBinding expected, string cwd, WorkerResolvedTask task,
        JsonElement initialize, JsonElement configuration, JsonElement requirementsResponse, string? sourceRepositoryRoot = null)
        => ValidateResponsesCore(expected, cwd, task, initialize, configuration, requirementsResponse, sourceRepositoryRoot, discoveringContext: false);

    private static void ValidateResponsesCore(WorkerNativeBinding expected, string cwd, WorkerResolvedTask task,
        JsonElement initialize, JsonElement configuration, JsonElement requirementsResponse, string? sourceRepositoryRoot, bool discoveringContext)
    {
        Require(task.ContextMode is "inherit" or "native", "invalid_context_mode");
        Unique(initialize); Unique(configuration); Unique(requirementsResponse);
        Require(String(Get(initialize, "userAgent")).StartsWith("lain_worker_config_probe/" + ClientVersion + " ", StringComparison.Ordinal), "native_version_mismatch");
        Require(SamePath(String(Get(initialize, "codexHome")), expected.CodexHome), "native_home_mismatch");
        Require(Path.IsPathFullyQualified(cwd), "invalid_probe_cwd");
        var layers = Get(configuration, "layers");
        Require(layers is { ValueKind: JsonValueKind.Array } && layers.Value.EnumerateArray().Any(layer =>
            Get(layer, "name") is { ValueKind: JsonValueKind.Object } name && StringOrNull(Get(name, "type")) == "sessionFlags" &&
            Get(layer, "disabledReason") is null or { ValueKind: JsonValueKind.Null }), "native_session_flags_not_proven");
        var config = Get(configuration, "config") ?? throw Failure("missing_native_config");
        Require(String(Get(config, "model")) == task.Model && String(Get(config, "model_provider")) == expected.ProviderId, "native_model_or_provider_mismatch");
        Require(SamePath(String(Get(config, "model_catalog_json")), expected.CatalogPath), "native_catalog_mismatch");
        Require(String(Get(config, "model_reasoning_effort")) == task.ReasoningEffort, "native_reasoning_mismatch");
        Require(Get(Get(config, "agents"), "enabled") is { ValueKind: JsonValueKind.False }, "native_agents_not_disabled");
        foreach (var feature in new[] { "multi_agent", "multi_agent_v2" })
            Require(Get(Get(config, "features"), feature) is { ValueKind: JsonValueKind.False }, "native_agents_not_disabled");
        Require(String(Get(config, "web_search")) == "disabled", "native_search_mismatch");
        ValidateProvider(Get(Get(config, "model_providers"), expected.ProviderId), expected);
        Require(String(Get(config, "sandbox_mode")) == (task.WritePaths.Length == 0 ? "read-only" : "workspace-write"), "native_sandbox_mismatch");
        if (task.ContextMode == "native")
        {
            foreach (var feature in new[] { "plugins", "apps" })
                Require(Get(Get(config, "features"), feature) is { ValueKind: JsonValueKind.False }, "native_context_features_not_disabled");
            Require(Get(Get(Get(config, "orchestrator"), "mcp"), "enabled") is { ValueKind: JsonValueKind.False }, "native_orchestrator_mcp_not_disabled");
            Require(Get(Get(Get(config, "cloud"), "skills"), "enabled") is { ValueKind: JsonValueKind.False }, "native_cloud_skills_not_disabled");
            Require(Get(Get(config, "skills"), "include_instructions") is { ValueKind: JsonValueKind.False } &&
                Number(Get(Get(config, "skills"), "max_context_tokens")) == 1, "native_skill_context_not_suppressed");
            var servers = McpServers(config);
            if (!discoveringContext)
                foreach (var server in servers.EnumerateObject())
                    Require(Get(server.Value, "enabled") is { ValueKind: JsonValueKind.False }, "native_mcp_server_not_disabled");
        }
        if (task.WritePaths.Length > 0)
        {
            // Native ProjectTrustLookup selects an exact cwd entry before the
            // repository-root entry, including an entry without a trust level.
            // Requiring existing trust avoids --worktree silently persisting it.
            var projects = Get(config, "projects");
            var project = Get(projects, cwd);
            if (project is null && sourceRepositoryRoot is not null) project = Get(projects, sourceRepositoryRoot);
            Require(StringOrNull(Get(project, "trust_level")) == "trusted", "source_trust_required");
        }
        var requirements = Get(requirementsResponse, "requirements") ?? throw Failure("missing_native_requirements");
        if (requirements.ValueKind == JsonValueKind.Null) return;
        Require(requirements.ValueKind == JsonValueKind.Object, "invalid_native_requirements");
        if (Present(Get(requirements, "modelProvider")) is { } provider)
            Require(String(provider) == expected.ProviderId, "managed_provider_conflict");
        if (Present(Get(Get(requirements, "modelProviders"), expected.ProviderId)) is { } definition) ValidateProvider(definition, expected);
        if (Present(Get(requirements, "modelCatalogJson")) is { } catalog)
            Require(SamePath(String(catalog), expected.CatalogPath), "managed_catalog_conflict");
        foreach (var feature in new[] { "multi_agent", "multi_agent_v2" })
            if (Present(Get(Get(requirements, "featureRequirements"), feature)) is { } enabled)
                Require(enabled.ValueKind == JsonValueKind.False, "managed_agents_conflict");
        if (task.ContextMode == "native")
            foreach (var feature in new[] { "plugins", "apps" })
                if (Present(Get(Get(requirements, "featureRequirements"), feature)) is { } enabled)
                    Require(enabled.ValueKind == JsonValueKind.False, "managed_context_conflict");
        if (Present(Get(requirements, "allowedWebSearchModes")) is { } search)
            Require(Strings(search).Contains("disabled", StringComparer.Ordinal), "managed_search_conflict");
        if (Present(Get(requirements, "allowedSandboxModes")) is { } sandbox)
            Require(Strings(sandbox).Contains(task.WritePaths.Length == 0 ? "read-only" : "workspace-write", StringComparer.Ordinal), "managed_sandbox_conflict");
        // models.newThread is a default, not an exact requirement. In 0.164.0
        // explicit --model/-c effort ignore its model/effort pair; do not reject
        // harmless defaults as conflicts. config/read above checks the final values.
    }

    private static JsonElement McpServers(JsonElement? config)
    {
        var servers = Get(config, "mcp_servers");
        Require(servers is { ValueKind: JsonValueKind.Object }, "native_mcp_inventory_missing");
        return servers!.Value;
    }

    private static void ValidateProvider(JsonElement? value, WorkerNativeBinding expected)
    {
        var provider = value ?? throw Failure("native_provider_missing");
        Require(String(Get(provider, "name")) == "lain worker", "native_provider_name_mismatch");
        Require(String(Get(provider, "base_url")) == expected.BaseUrl && String(Get(provider, "wire_api")) == "responses", "native_endpoint_mismatch");
        Require(Number(Get(provider, "request_max_retries")) == 0 && Number(Get(provider, "stream_max_retries")) == 0, "native_retries_mismatch");
        var auth = Get(provider, "auth") ?? throw Failure("native_auth_mismatch");
        Require(SamePath(String(Get(auth, "command")), expected.AuthCommand) && Strings(Get(auth, "args")).SequenceEqual(expected.AuthArguments) &&
            Number(Get(auth, "timeout_ms")) == expected.AuthTimeoutMs && Number(Get(auth, "refresh_interval_ms")) == expected.AuthRefreshIntervalMs &&
            SamePath(String(Get(auth, "cwd")), expected.CodexHome), "native_auth_mismatch");
        foreach (var property in provider.EnumerateObject())
        {
            if (property.Name is "name" or "base_url" or "wire_api" or "auth" or "request_max_retries" or "stream_max_retries") continue;
            var harmless = property.Name switch {
                "requires_openai_auth" or "supports_websockets" or "supports_standalone_web_search" => property.Value.ValueKind == JsonValueKind.False,
                "query_params" or "http_headers" or "env_http_headers" => property.Value.ValueKind == JsonValueKind.Null ||
                    property.Value.ValueKind == JsonValueKind.Object && !property.Value.EnumerateObject().Any(),
                "model_catalog_url" or "env_key" or "env_key_instructions" or "experimental_bearer_token" or "gateway_oauth" or "aws" or
                    "capabilities" or "stream_idle_timeout_ms" or "websocket_connect_timeout_ms" => property.Value.ValueKind == JsonValueKind.Null,
                _ => false
            };
            Require(harmless, "native_extra_provider_setting");
        }
        foreach (var property in auth.EnumerateObject())
            Require(property.Name is "command" or "args" or "timeout_ms" or "refresh_interval_ms" or "cwd", "native_extra_auth_setting");
    }

    public static void AssertCurrent(WorkerNativeBinding expected)
    {
        var status = CodexArtifactInstaller.Inspect(expected.CodexHome, expected.ProfileId, expected.Revision);
        Require(status is { State: "installed", Stale: false } && status.GenerationId == expected.GenerationId &&
            Hash(Read(Path.Combine(expected.CodexHome, "lain.config.toml"))) == expected.ConfigurationHash &&
            Hash(Read(expected.CatalogPath)) == expected.CatalogHash, "installed_binding_changed");
    }
    private static void ValidateCatalog(WorkerNativeBinding expected, WorkerResolvedTask task)
    {
        using var catalog = JsonDocument.Parse(Read(expected.CatalogPath));
        Unique(catalog.RootElement);
        var models = Get(catalog.RootElement, "models") ?? throw Failure("installed_catalog_invalid");
        Require(models.ValueKind == JsonValueKind.Array, "installed_catalog_invalid");
        var matching = models.EnumerateArray().Where(model => StringOrNull(Get(model, "slug")) == task.Model).ToArray();
        Require(matching.Length == 1 && StringOrNull(Get(matching[0], "visibility")) != "hide" &&
            Get(matching[0], "supported_in_api") is not { ValueKind: JsonValueKind.False }, "installed_model_missing");
        var levels = Get(matching[0], "supported_reasoning_levels");
        Require(levels is { ValueKind: JsonValueKind.Array } && levels.Value.EnumerateArray().Any(level => StringOrNull(Get(level, "effort")) == task.ReasoningEffort), "installed_reasoning_unqualified");
    }
    private static Dictionary<string, JsonElement> GeneratedSettings(string text)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal); var table = "";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith('['))
            {
                Require(Regex.IsMatch(line, @"^\[[a-z_.]+\]$", RegexOptions.CultureInvariant), "unsupported_generated_config");
                table = line[1..^1] + "."; continue;
            }
            var assignment = Regex.Match(line, @"^([a-z_]+)\s*=\s*(.+)$", RegexOptions.CultureInvariant);
            Require(assignment.Success, "unsupported_generated_config");
            try
            {
                using var value = JsonDocument.Parse(assignment.Groups[2].Value);
                Require(result.TryAdd(table + assignment.Groups[1].Value, value.RootElement.Clone()), "duplicate_generated_config");
            }
            catch (JsonException) { throw Failure("unsupported_generated_config"); }
        }
        return result;
    }
    private static async Task DiscardStderr(StreamReader reader, CancellationTokenSource deadline)
    {
        try
        {
            var buffer = new char[4096]; var total = 0; int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), deadline.Token)) > 0)
            { total += count; Require(total <= MaximumOutputChars, "probe_output_limit"); }
        }
        catch { deadline.Cancel(); throw; }
    }
    // Kill the session's group even after the app-server leader has exited:
    // descendants can otherwise keep pipes open and outlive the no-turn probe.
    private static void KillContained(Process process)
    {
        try { if (process.Id > 1) _ = KillProcessGroup(-process.Id, 9); }
        catch (InvalidOperationException) { }
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int KillProcessGroup(int pid, int signal);
    private sealed class RpcReader(StreamReader reader)
    {
        private readonly char[] buffer = new char[4096];
        private int offset, length, total;
        public async Task<string?> Line(CancellationToken ct)
        {
            var line = new StringBuilder();
            while (true)
            {
                if (offset == length)
                {
                    length = await reader.ReadAsync(buffer.AsMemory(), ct); offset = 0;
                    if (length == 0) { Require(line.Length == 0, "truncated_native_json"); return null; }
                    total += length; Require(total <= MaximumOutputChars, "probe_output_limit");
                }
                var ch = buffer[offset++];
                if (ch == '\n') return line.ToString();
                line.Append(ch); // JSON permits CR before LF; raw CR inside a string must still fail parsing.
            }
        }
    }
    private static string Read(string path)
    {
        WorkerProjectFiles.AssertRegular(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Require(stream.Length <= 16 * 1024 * 1024, "installed_artifact_too_large");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var buffer = new char[4096]; var result = new StringBuilder(); int count;
        while ((count = reader.Read(buffer)) > 0)
        {
            Require(result.Length + count <= 16 * 1024 * 1024, "installed_artifact_too_large");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static bool SamePath(string left, string right)
    {
        if (Uri.TryCreate(left, UriKind.Absolute, out var uri) && uri.IsFile) left = uri.LocalPath;
        return Path.IsPathFullyQualified(left) && string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
    private static JsonElement? Present(JsonElement? value) => value is null or { ValueKind: JsonValueKind.Null } ? null : value;
    private static string String(JsonElement? value) => StringOrNull(value) is { Length: > 0 } text ? text : throw Failure("missing_native_string");
    private static string? StringOrNull(JsonElement? value) => value is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;
    private static long Number(JsonElement? value) => value is { ValueKind: JsonValueKind.Number } number && number.TryGetInt64(out var result) ? result : throw Failure("missing_native_number");
    private static string[] Strings(JsonElement? value) => value is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray().Select(item => String(item)).ToArray() : throw Failure("missing_native_array");
    private static JsonElement? Get(JsonElement? parent, string name)
    {
        if (parent is null or { ValueKind: JsonValueKind.Null }) return null;
        Require(parent.Value.ValueKind == JsonValueKind.Object, "invalid_native_object");
        return parent.Value.TryGetProperty(name, out var value) ? value : null;
    }
    private static void Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject()) { Require(seen.Add(property.Name), "duplicate_native_control"); Unique(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Unique(item);
    }
    private static void Require(bool condition, string code) { if (!condition) throw Failure(code); }
    private static WorkerNativeConfigurationException Failure(string code) => new(code);
}
