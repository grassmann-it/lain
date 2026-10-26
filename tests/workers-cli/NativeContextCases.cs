using System.Text.Json;
using System.Text.Json.Nodes;
using Lain.Cli;
using Lain.Core;

// Offline RPC/process fixtures. Genuine namespace-member/byte comparison is owned by
// the isolated native outer harness and NativeRunnerCase, not this simulator.
internal static class NativeContextCases
{
    public static async Task RunAsync(string root, string cwd, WorkerNativeBinding binding, WorkerResolvedTask inherited,
        JsonObject initialize, JsonObject configuration, JsonObject requirements, Action<bool, string> require)
    {
        var native = inherited with { ContextMode = "native" };
        void Reject(Action action, string code)
        {
            try { action(); }
            catch (WorkerNativeConfigurationException error) { require(error.Code == code, "native context refusal: " + code); return; }
            throw new Exception("Expected context refusal: " + code);
        }
        async Task RejectAsync(Func<Task> action, string code)
        {
            try { await action(); }
            catch (WorkerNativeConfigurationException error) { require(error.Code == code, "native context RPC refusal: " + code); return; }
            throw new Exception("Expected context RPC refusal: " + code);
        }
        static JsonElement E(JsonNode node) => JsonSerializer.SerializeToElement(node);
        var resolved = configuration.DeepClone();
        var config = resolved["config"]!;
        config["features"]!["plugins"] = false;
        config["features"]!["apps"] = false;
        config["orchestrator"] = new JsonObject { ["mcp"] = new JsonObject { ["enabled"] = false } };
        config["cloud"] = new JsonObject { ["skills"] = new JsonObject { ["enabled"] = false } };
        config["skills"] = new JsonObject { ["include_instructions"] = false, ["max_context_tokens"] = 1 };
        config["mcp_servers"] = new JsonObject {
            ["context_fixture"] = new JsonObject { ["command"] = "/never/start/mcp", ["enabled"] = false },
            ["already-disabled"] = new JsonObject { ["command"] = "/never/start/mcp", ["enabled"] = false }
        };
        void Validate(JsonNode value, JsonNode? managed = null) => WorkerNativeConfiguration.ValidateResponses(binding, cwd, native,
            E(initialize), E(value), E(managed ?? requirements));
        Validate(resolved);
        void Mutate(Action<JsonNode> change, string code)
        { var copy = resolved.DeepClone(); change(copy["config"]!); Reject(() => Validate(copy), code); }
        Reject(() => WorkerNativeConfiguration.ConfigurationOverrides(native, binding), "context_discovery_required");
        Reject(() => WorkerNativeConfiguration.ConfigurationOverrides(native with { ContextMode = "compact" }, binding), "invalid_context_mode");
        Reject(() => WorkerNativeConfiguration.ConfigurationOverrides(native, binding, ["unsafe.name"]), "unsupported_mcp_server_name");
        foreach (var feature in new[] { "plugins", "apps" })
        {
            Mutate(c => c["features"]![feature] = true, "native_context_features_not_disabled");
            Mutate(c => c["features"]!.AsObject().Remove(feature), "native_context_features_not_disabled");
            var managed = new JsonObject { ["requirements"] = new JsonObject {
                ["featureRequirements"] = new JsonObject { [feature] = true } } };
            Reject(() => Validate(resolved, managed), "managed_context_conflict");
        }
        Mutate(c => c["orchestrator"]!["mcp"]!["enabled"] = true, "native_orchestrator_mcp_not_disabled");
        Mutate(c => c["cloud"]!["skills"]!["enabled"] = true, "native_cloud_skills_not_disabled");
        Mutate(c => c["skills"]!["include_instructions"] = true, "native_skill_context_not_suppressed");
        Mutate(c => c["skills"]!["max_context_tokens"] = 1200, "native_skill_context_not_suppressed");
        Mutate(c => c.AsObject().Remove("mcp_servers"), "native_mcp_inventory_missing");
        Mutate(c => c["mcp_servers"]!["context_fixture"]!["enabled"] = true, "native_mcp_server_not_disabled");
        Mutate(c => c["mcp_servers"]!["context_fixture"]!.AsObject().Remove("enabled"), "native_mcp_server_not_disabled");
        Mutate(c => c["mcp_servers"]!["unexpected"] = new JsonObject { ["enabled"] = true }, "native_mcp_server_not_disabled");

        var executable = Path.Combine(root, "context protocol.py");
        File.WriteAllText(executable, """
#!/usr/bin/env python3
import json,os,pathlib,sys
p=pathlib.Path(__file__)
data=json.loads(p.with_suffix('.json').read_text())
if sys.argv[1:]==['--version']:
    print('codex-cli 0.164.0');sys.exit(0)
args=sys.argv[1:]; assert os.getcwd()==data['cwd']
assert os.environ.get('HOME')==data['home'] and os.environ.get('CODEX_HOME')==data['codexHome']
phase='probe' if args[:3]==['--no-daemon','app-server','--stdio'] else 'exec'
values=[args[i+1] for i,a in enumerate(args) if a=='-c']
with p.with_suffix('.calls').open('a') as f: f.write(json.dumps({'phase':phase,'overrides':values})+'\n')
if phase=='exec':
    assert data['mode']=='valid'
    sys.stdin.read()
    print(json.dumps({'type':'thread.started','thread_id':'context-fixture-thread'}))
    print(json.dumps({'type':'turn.completed'}));sys.exit(0)
config=data['configuration']['config']
for setting in values:
    key,raw=setting.split('=',1)
    if key.startswith('features.') or key.startswith('orchestrator.') or key.startswith('cloud.') or key.startswith('skills.') or key.startswith('mcp_servers.'):
        target=config; parts=key.split('.')
        for part in parts[:-1]: target=target.setdefault(part,{})
        target[parts[-1]]=json.loads(raw)
final=any(v.startswith('mcp_servers.') for v in values)
if final and data['mode']=='ignore-mcp': config['mcp_servers']['context_fixture']['enabled']=True
if final and data['mode']=='new-server': config['mcp_servers']['late_server']={'enabled':True}
if data['mode']=='ignore-flags': config['features']['plugins']=True
if data['mode']=='unsafe-name': config['mcp_servers']['unsafe.name']={'enabled':True}
for line in sys.stdin:
    request=json.loads(line); method=request['method']
    assert method in ['initialize','initialized','config/read','configRequirements/read']
    if method=='initialized': continue
    if method=='config/read':
        assert request['params']=={'includeLayers':True,'cwd':data['cwd']}
        result=data['configuration']
    elif method=='initialize': result=data['initialize']
    else: result=data['requirements']
    print(json.dumps({'id':request['id'],'result':result}),flush=True)
""");
        File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        // Discovery starts with inherited direct servers, including one already
        // disabled. Only session flags change; project instructions stay present.
        var discovered = configuration.DeepClone();
        discovered["config"]!["mcp_servers"] = config["mcp_servers"]!.DeepClone();
        discovered["config"]!["mcp_servers"]!["context_fixture"]!["enabled"] = true;
        discovered["config"]!["project_doc_max_bytes"] = 32768;
        void Mode(string mode) => File.WriteAllText(Path.ChangeExtension(executable, ".json"), JsonSerializer.Serialize(new {
            mode, cwd, initialize, configuration = discovered, requirements,
            home = Environment.GetEnvironmentVariable("HOME"), codexHome = Environment.GetEnvironmentVariable("CODEX_HOME") }));
        JsonNode[] Calls() => File.ReadAllLines(Path.ChangeExtension(executable, ".calls")).Select(line => JsonNode.Parse(line)!).ToArray();
        static string[] Values(JsonNode call) => call["overrides"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray();
        Mode("valid");
        var prepared = await WorkerNativeConfiguration.PrepareAsync(executable, cwd, native, binding);
        var calls = Calls();
        require(calls.Length == 2 && Values(calls[0]).All(value => !value.StartsWith("mcp_servers.")) &&
            Values(calls[1]).SequenceEqual(prepared.Overrides), "native context discovers once then verifies final overrides");
        require(prepared.Overrides.Contains("mcp_servers.context_fixture.enabled=false") &&
            prepared.Overrides.Contains("mcp_servers.already-disabled.enabled=false") && !prepared.Overrides.Contains("mcp_servers={}"),
            "every discovered MCP entry gets an explicit disable including existing disabled entries");
        require(prepared.Proof is { ContextMode: "native", ContextOverrides: { Length: 8 } } &&
            prepared.Proof.ContextOverrides.Contains("skills.include_instructions=false"), "receipt contains only the actual verified context overrides");
        require(!prepared.Overrides.Any(value => value.StartsWith("project_doc") || value.StartsWith("shell") || value.StartsWith("skills.config")),
            "context selection preserves project rules and native shell/file configuration");
        var execArgs = WorkerBatchRunner.Arguments(cwd, native, "unused-result", binding, prepared.Overrides);
        require(execArgs.Where((_, index) => index > 0 && execArgs[index - 1] == "-c").SequenceEqual(prepared.Overrides),
            "exec receives the exact discovered and verified overrides");
        var legacy = await WorkerNativeConfiguration.PrepareAsync(executable, cwd, inherited, binding);
        require(legacy.Proof is { ContextMode: "inherit", ContextOverrides: null } &&
            legacy.Overrides.SequenceEqual(WorkerNativeConfiguration.ConfigurationOverrides(inherited, binding)) &&
            Values(Calls()[^1]).SequenceEqual(legacy.Overrides), "inherit keeps legacy overrides and needs only the original gate");
        foreach (var pair in new[] { ("ignore-mcp", "native_mcp_server_not_disabled"), ("new-server", "native_mcp_server_not_disabled"),
            ("ignore-flags", "native_context_features_not_disabled"), ("unsafe-name", "unsupported_mcp_server_name") })
        {
            Mode(pair.Item1);
            await RejectAsync(() => WorkerNativeConfiguration.PrepareAsync(executable, cwd, native, binding), pair.Item2);
        }
        var unknown = new ModelCapabilityEvidenceDto("unknown", "offline_context_fixture");
        var runnable = native with { Compatibility = new(native.RouteId, native.Provider, "synthetic-upstream", null,
            "codex", "0.164.0", "responses", "responses", "offline_context_fixture",
            new(unknown, unknown, unknown, unknown, unknown, unknown, new(null, unknown), new(null, unknown), new(null, unknown)), []) };
        var plan = new WorkerPlan(1, binding.Revision, binding.ProfileId, 1, [runnable], []);
        Mode("valid");
        var run = await WorkerBatchRunner.RunAsync(cwd, plan, executable, 5, Path.Combine(root, "context-run"),
            _ => Task.CompletedTask, nativeBinding: binding);
        calls = Calls();
        require(run.State == "succeeded" && run.Tasks.Single().NativeConfiguration?.ContextMode == "native" &&
            calls[^1]["phase"]!.GetValue<string>() == "exec" && Values(calls[^1]).SequenceEqual(Values(calls[^2])),
            "runner uses the final configuration probe's overrides verbatim for the child");
        var execCount = calls.Count(call => call["phase"]!.GetValue<string>() == "exec");
        Mode("ignore-mcp");
        var denied = await WorkerBatchRunner.RunAsync(cwd, plan, executable, 5, Path.Combine(root, "context-denied"),
            _ => Task.CompletedTask, nativeBinding: binding);
        require(denied.State == "failed" && denied.Tasks.Single().Error == "native_config_native_mcp_server_not_disabled" &&
            Calls().Count(call => call["phase"]!.GetValue<string>() == "exec") == execCount,
            "failed native context proof prevents an exec start");
    }
}
