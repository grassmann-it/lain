using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lain.Cli;
using Lain.Client;
using Lain.Core;

internal static class NativeConfigurationCases
{
    // Called by the parent-owned test entry point. All subprocesses below are
    // offline protocol/process fixtures; genuine native gates run separately.
    public static async Task RunAsync(string root, Action<bool, string> require)
    {
        root = Path.Combine(root, "native-config-" + Guid.NewGuid().ToString("N"));
        WorkerProjectFiles.PrivateDirectory(root);
        var home = Path.Combine(root, "fixture home");
        var cwd = Path.Combine(root, "project with spaces");
        Directory.CreateDirectory(cwd);
        var helper = Path.Combine(root, "never invoke auth");
        Executable(helper, "#!/bin/sh\ntouch '" + Path.Combine(root, "auth-invoked").Replace("'", "'\\''") + "'\nexit 97\n");
        string Q(string text) => JsonSerializer.Serialize(text);
        var config = "model=\"fixture-astra\"\nmodel_reasoning_effort=\"low\"\nmodel_provider=\"lain\"\nmodel_catalog_json=\"/unused/models.json\"\nweb_search=\"disabled\"\n" +
            "[model_providers.lain]\nname=\"lain\"\nbase_url=\"http://127.0.0.1:1/v1\"\nwire_api=\"responses\"\n" +
            "[model_providers.lain.auth]\ncommand=" + Q(helper) + "\nargs=[\"auth\",\"token\",\"--audience\",\"codex\"]\ntimeout_ms=5000\nrefresh_interval_ms=300000\n";
        // Synthetic aliases mirror the distinct controls in the pinned client
        // fixture; they do not certify an upstream provider accepting these.
        object Model(string slug, string initial, string[] efforts) => new {
            slug, display_name = slug, description = "Offline configuration fixture", default_reasoning_level = initial,
            supported_reasoning_levels = efforts.Select(effort => new { effort, description = effort }),
            shell_type = "unified_exec", visibility = "list", supported_in_api = true, priority = 0,
            base_instructions = "You are a coding assistant.", supports_reasoning_summaries = false,
            support_verbosity = false, truncation_policy = new { mode = "tokens", limit = 10000 },
            supports_search_tool = false, tool_mode = (string?)null, experimental_supported_tools = Array.Empty<string>(), input_modalities = new[] { "text" }
        };
        string[] full = ["low", "medium", "high", "xhigh", "max", "ultra"];
        var catalog = JsonSerializer.Serialize(new { models = new[] {
            Model("fixture-astra", "low", full), Model("fixture-sol", "medium", full), Model("fixture-luna", "medium", full[..^1]) } });
        var export = new CodexProfileArtifacts("lain.config.toml", config, "lain.models.json", catalog, [], 7, ProfileId: "profile");
        static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        export = export with { GenerationId = Hash("profile\n7\n" + config + "\n" + catalog) };
        CodexArtifactInstaller.Install(export, home, "profile", 7);
        var binding = WorkerNativeConfiguration.ReadInstalledBinding(home, "profile", 7);
        var second = WorkerNativeConfiguration.ReadInstalledBinding(home, "profile", 7);
        require(binding.ProviderId != second.ProviderId && binding.GenerationId == second.GenerationId &&
            binding.ProviderId.StartsWith("lain_worker_", StringComparison.Ordinal) && Guid.TryParseExact(binding.ProviderId[12..], "N", out _),
            "each run gets a random provider identity even for the same installed generation");
        WorkerResolvedTask TaskFor(string model = "fixture-astra", string effort = "low", params string[] writes) =>
            new("config", "review", "Read only", "route", model, "codex", ["account"], effort, writes);
        var task = TaskFor();
        var overrides = WorkerNativeConfiguration.ConfigurationOverrides(task, binding);
        var args = WorkerNativeConfiguration.ProbeArguments(task, overrides);
        require(!args.Contains("--profile") && args.Take(3).SequenceEqual(new[] { "--no-daemon", "app-server", "--stdio" }) &&
            args.Skip(3).Where((_, index) => index % 2 == 1).SequenceEqual(overrides), "probe uses exactly the shared overrides without a native profile");
        require(overrides.Contains("model=\"fixture-astra\"") && overrides.Contains("model_reasoning_effort=\"low\"") &&
            overrides.Any(value => value.StartsWith("model_providers." + binding.ProviderId + "={", StringComparison.Ordinal) &&
                value.Contains("http_headers={}") && value.Contains("requires_openai_auth=false") && value.Contains("cwd=" + Q(home))),
            "complete fresh provider table pins auth cwd and disables inherited headers and OpenAI auth");
        foreach (var pair in new[] { ("fixture-astra", "low"), ("fixture-sol", "medium"), ("fixture-luna", "max"), ("fixture-astra", "ultra") })
            require(WorkerNativeConfiguration.ConfigurationOverrides(TaskFor(pair.Item1, pair.Item2), binding).Contains("model_reasoning_effort=" + Q(pair.Item2)),
                "qualified explicit efforts survive configuration export: " + pair.Item1 + "/" + pair.Item2);

        void Reject(Action action, string code)
        {
            try { action(); }
            catch (WorkerNativeConfigurationException error) { require(error.Code == code, "safe native refusal code: " + code); return; }
            throw new Exception("Expected native refusal: " + code);
        }
        async Task RejectAsync(Func<Task> action, string code)
        {
            try { await action(); }
            catch (WorkerNativeConfigurationException error) { require(error.Code == code && !error.Message.Contains("SECRET"), "bounded safe native refusal: " + code); return; }
            throw new Exception("Expected native refusal: " + code);
        }
        Reject(() => WorkerNativeConfiguration.ConfigurationOverrides(TaskFor("fixture-astra", "none"), binding), "installed_reasoning_unqualified");
        Reject(() => WorkerNativeConfiguration.ConfigurationOverrides(TaskFor("fixture-luna", "ultra"), binding), "installed_reasoning_unqualified");
        Reject(() => WorkerNativeConfiguration.ConfigurationOverrides(TaskFor("stale-alias"), binding), "installed_model_missing");
        Reject(() => WorkerNativeConfiguration.ReadInstalledBinding(home, "profile", 8), "installed_binding_unavailable");

        var initialize = new JsonObject { ["codexHome"] = home, ["userAgent"] = "lain_worker_config_probe/0.164.0 (Linux fixture)" };
        JsonObject Provider() => new() {
            ["name"] = "lain worker", ["base_url"] = binding.BaseUrl, ["wire_api"] = "responses",
            ["request_max_retries"] = 0, ["stream_max_retries"] = 0,
            ["model_catalog_url"] = null, ["env_key"] = null, ["experimental_bearer_token"] = null,
            ["gateway_oauth"] = null, ["aws"] = null, ["capabilities"] = null,
            ["http_headers"] = new JsonObject(), ["query_params"] = new JsonObject(), ["env_http_headers"] = new JsonObject(),
            ["requires_openai_auth"] = false, ["supports_websockets"] = false, ["supports_standalone_web_search"] = false,
            ["auth"] = new JsonObject { ["command"] = helper, ["args"] = JsonSerializer.SerializeToNode(binding.AuthArguments),
                ["timeout_ms"] = 5000, ["refresh_interval_ms"] = 300000, ["cwd"] = home }
        };
        var configuration = new JsonObject {
            ["config"] = new JsonObject { ["model"] = task.Model, ["model_provider"] = binding.ProviderId,
                ["model_reasoning_effort"] = task.ReasoningEffort, ["model_catalog_json"] = binding.CatalogPath,
                ["web_search"] = "disabled", ["sandbox_mode"] = "read-only", ["approval_policy"] = "on-request",
                ["agents"] = new JsonObject { ["enabled"] = false },
                ["features"] = new JsonObject { ["multi_agent"] = false, ["multi_agent_v2"] = false },
                ["model_providers"] = new JsonObject { [binding.ProviderId] = Provider() } },
            ["layers"] = new JsonArray(new JsonObject { ["name"] = new JsonObject { ["type"] = "sessionFlags" }, ["version"] = "fixture" })
        };
        var requirements = new JsonObject { ["requirements"] = null };
        static JsonElement E(JsonNode node) => JsonSerializer.SerializeToElement(node);
        void Validate(JsonNode? c = null, JsonNode? r = null, JsonNode? i = null) =>
            WorkerNativeConfiguration.ValidateResponses(binding, cwd, task, E(i ?? initialize), E(c ?? configuration), E(r ?? requirements));
        Validate(); require(true, "native-shaped config/read result validates with null requirements");
        var writeTask = TaskFor("fixture-astra", "low", "result.txt");
        var writeConfiguration = configuration.DeepClone();
        writeConfiguration["config"]!["sandbox_mode"] = "workspace-write";
        void ValidateWrite(string? repository = null) => WorkerNativeConfiguration.ValidateResponses(binding, cwd, writeTask,
            E(initialize), E(writeConfiguration), E(requirements), repository);
        Reject(() => ValidateWrite(), "source_trust_required");
        writeConfiguration["config"]!["projects"] = new JsonObject { [cwd] = new JsonObject { ["trust_level"] = "untrusted" } };
        Reject(() => ValidateWrite(), "source_trust_required");
        writeConfiguration["config"]!["projects"]![cwd]!["trust_level"] = "trusted";
        ValidateWrite(); require(true, "write source must already be trusted before native worktree creation");
        var repository = Path.GetDirectoryName(cwd)!;
        writeConfiguration["config"]!["projects"] = new JsonObject { [repository] = new JsonObject { ["trust_level"] = "trusted" } };
        ValidateWrite(repository); require(true, "repository-root trust is used only when no exact cwd entry exists");
        writeConfiguration["config"]!["projects"]![cwd] = new JsonObject();
        Reject(() => ValidateWrite(repository), "source_trust_required");
        require(true, "an exact cwd entry without trust prevents borrowing repository trust");
        void Mutate(Action<JsonNode> mutate, string code)
        { var copy = configuration.DeepClone(); mutate(copy["config"]!); Reject(() => Validate(copy), code); }
        Mutate(c => c["model_provider"] = "lain", "native_model_or_provider_mismatch");
        Mutate(c => c["model_catalog_json"] = "/tmp/stale.json", "native_catalog_mismatch");
        Mutate(c => c["model_reasoning_effort"] = "none", "native_reasoning_mismatch");
        Mutate(c => c["agents"]!["enabled"] = true, "native_agents_not_disabled");
        foreach (var feature in new[] { "multi_agent", "multi_agent_v2" })
            Mutate(c => c["features"]![feature] = true, "native_agents_not_disabled");
        Mutate(c => c["web_search"] = "live", "native_search_mismatch");
        Mutate(c => c["sandbox_mode"] = "danger-full-access", "native_sandbox_mismatch");
        JsonNode P(JsonNode c) => c["model_providers"]![binding.ProviderId]!;
        Mutate(c => P(c)["base_url"] = "https://unbound.invalid/v1", "native_endpoint_mismatch");
        Mutate(c => P(c)["auth"]!["command"] = "/tmp/unbound-helper", "native_auth_mismatch");
        Mutate(c => P(c)["auth"]!["cwd"] = cwd, "native_auth_mismatch");
        Mutate(c => P(c)["stream_max_retries"] = 2, "native_retries_mismatch");
        foreach (var extra in new[] { "env_key", "experimental_bearer_token", "model_catalog_url", "env_key_instructions" })
            Mutate(c => P(c)[extra] = "SECRET", "native_extra_provider_setting");
        foreach (var extra in new[] { "aws", "gateway_oauth", "capabilities", "http_headers", "env_http_headers", "query_params", "future_transport" })
            Mutate(c => P(c)[extra] = new JsonObject { ["extra"] = "SECRET" }, "native_extra_provider_setting");
        Mutate(c => P(c)["supports_websockets"] = true, "native_extra_provider_setting");
        Mutate(c => P(c)["auth"]!["env"] = new JsonObject(), "native_extra_auth_setting");
        void Managed(JsonObject r, string code) => Reject(() => Validate(r: new JsonObject { ["requirements"] = r }), code);
        Managed(new() { ["modelProvider"] = "managed" }, "managed_provider_conflict");
        Managed(new() { ["modelCatalogJson"] = "/tmp/managed.json" }, "managed_catalog_conflict");
        Managed(new() { ["featureRequirements"] = new JsonObject { ["multi_agent"] = true } }, "managed_agents_conflict");
        Managed(new() { ["allowedWebSearchModes"] = new JsonArray("live") }, "managed_search_conflict");
        Managed(new() { ["allowedSandboxModes"] = new JsonArray("danger-full-access") }, "managed_sandbox_conflict");
        var replaced = Provider(); replaced["auth"]!["command"] = "/tmp/managed-helper";
        Managed(new() { ["modelProviders"] = new JsonObject { [binding.ProviderId] = replaced } }, "native_auth_mismatch");
        Validate(r: new JsonObject { ["requirements"] = new JsonObject { ["models"] = new JsonObject {
            ["newThread"] = new JsonObject { ["model"] = "different-default", ["modelReasoningEffort"] = "none" } } } });
        require(true, "managed new-thread defaults do not reject an explicit model and effort selection");
        var missingLayers = configuration.DeepClone(); missingLayers["layers"] = new JsonArray();
        Reject(() => Validate(missingLayers), "native_session_flags_not_proven");
        Reject(() => WorkerNativeConfiguration.ValidateResponses(binding, cwd, task, E(initialize),
            JsonDocument.Parse("{\"config\":{},\"config\":{}}").RootElement, E(requirements)), "duplicate_native_control");

        var executable = Path.Combine(root, "protocol fixture.py");
        var script = """
#!/usr/bin/env python3
import json,os,pathlib,subprocess,sys,time
p=pathlib.Path(__file__); data=json.loads(p.with_suffix('.json').read_text()); mode=data['mode']
assert sys.argv[1:]==data['args'] and os.getcwd()==data['cwd']
if mode=='orphan':
    child=subprocess.Popen([sys.executable,'-c','import time;time.sleep(90)'])
    p.with_suffix('.pid').write_text(str(child.pid)); os._exit(0)
if mode=='hang': time.sleep(90)
if mode=='stdout-limit': sys.stdout.write('x'*(2*1024*1024+1));sys.stdout.flush();time.sleep(90)
if mode=='stderr-limit': sys.stderr.write('SECRET'*(400000));sys.stderr.flush();time.sleep(90)
methods=[]
for line in sys.stdin:
    request=json.loads(line); method=request['method']; methods.append(method)
    p.with_suffix('.requests').write_text(json.dumps(methods))
    assert method in ['initialize','initialized','config/read','configRequirements/read']
    if method=='initialized': continue
    if mode=='rpc-error': print(json.dumps({'id':request['id'],'error':{'code':-32000,'message':'SECRET'}}),flush=True);continue
    if mode=='duplicate': print('{"id":1,"id":1,"result":{}}',flush=True);continue
    if method=='initialize':
        assert request['params']['capabilities']=={'experimentalApi':True,'explicitGatewayOauth':True}
        result=data['initialize']
    elif method=='config/read':
        assert request['params']=={'includeLayers':True,'cwd':data['cwd']}
        result=data['configuration']
    else: result=data['requirements']
    print(json.dumps({'id':request['id'],'result':result}),flush=True)
""";
        Executable(executable, script);
        void Mode(string mode) => File.WriteAllText(Path.ChangeExtension(executable, ".json"), JsonSerializer.Serialize(new {
            mode, args, cwd, initialize, configuration, requirements }));
        Mode("valid");
        var proof = await WorkerNativeConfiguration.VerifyAsync(executable, cwd, task, binding, overrides);
        require(proof.GenerationId == binding.GenerationId && proof.Model == task.Model && proof.Cwd == cwd &&
            !JsonSerializer.Serialize(proof).Contains(helper), "native proof retains safe identity without raw config or helper data");
        require(JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.ChangeExtension(executable, ".requests")))!
            .SequenceEqual(new[] { "initialize", "initialized", "config/read", "configRequirements/read" }), "probe sends only exact configuration RPCs and no thread or turn");
        await RejectAsync(() => WorkerNativeConfiguration.VerifyAsync(executable, cwd, task, binding, overrides.Append("model=\"other\"").ToArray()), "probe_overrides_mismatch");
        foreach (var mode in new[] { "rpc-error", "duplicate", "stdout-limit", "stderr-limit", "hang", "orphan" })
        {
            Mode(mode);
            var code = mode switch { "rpc-error" => "native_config_read_refused", "duplicate" => "duplicate_native_control",
                "stdout-limit" or "stderr-limit" => "probe_output_limit", _ => "probe_timeout" };
            await RejectAsync(() => WorkerNativeConfiguration.VerifyAsync(executable, cwd, task, binding, overrides, timeoutMilliseconds: mode == "orphan" ? 700 : 1500), code);
        }
        var orphanPid = File.ReadAllText(Path.ChangeExtension(executable, ".pid"));
        bool Active()
        {
            var stat = "/proc/" + orphanPid + "/stat";
            return File.Exists(stat) && File.ReadAllText(stat).Split(')')[1].TrimStart()[0] != 'Z';
        }
        for (var attempt = 0; attempt < 30 && Active(); attempt++) await Task.Delay(20);
        require(!Active(), "probe kills descendants after the session leader exits");
        Mode("hang");
        using (var cancel = new CancellationTokenSource(300))
        {
            var cancelled = false;
            try { await WorkerNativeConfiguration.VerifyAsync(executable, cwd, task, binding, overrides, cancel.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            require(cancelled, "caller cancellation remains distinct from a probe timeout");
        }

        await NativeContextCases.RunAsync(root, cwd, binding, task, initialize, configuration, requirements, require);
        require(!File.Exists(Path.Combine(root, "auth-invoked")), "configuration probes never invoke the auth helper");
        var saved = File.ReadAllText(binding.CatalogPath);
        File.AppendAllText(binding.CatalogPath, " ");
        Reject(() => WorkerNativeConfiguration.AssertCurrent(binding), "installed_binding_changed");
        File.WriteAllText(binding.CatalogPath, saved);
        CodexArtifactInstaller.Install(export, home, "profile", 7);
        Reject(() => WorkerNativeConfiguration.AssertCurrent(binding), "installed_binding_changed");
        require(WorkerNativeConfiguration.ReadInstalledBinding(home, "profile", 7).GenerationId != binding.GenerationId,
            "reinstalling the same profile and revision invalidates the old generation binding");
    }

    private static void Executable(string path, string content)
    {
        File.WriteAllText(path, content);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
