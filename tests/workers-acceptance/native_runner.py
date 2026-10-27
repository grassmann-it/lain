#!/usr/bin/env python3
"""Prepare isolated genuine WorkerBatchRunner acceptance; execution needs --run.

Use the parent's serial runtime flock outside this process. No build, production
installation, credential copy, HOME/CODEX_HOME assignment or external provider.
The C# receiver installs the product-shaped synthetic catalog and configuration.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import threading
import tomllib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


COMMON_PATH = (Path("/mnt/common.py") if sys.argv[1:] == ["--inner"] else
               Path(__file__).resolve().parents[1] / "codex-picker/native_code_mode_host.py")
SPEC = importlib.util.spec_from_file_location("native_helper_common", COMMON_PATH)
common = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(common)
expect, save, digest = common.expect, common.save, common.digest
PORT = 18580
TOKEN = "lain-workers-native-synthetic-only"
MODELS = ("fixture-read", "fixture-write")
CONTEXT_MODEL = "fixture-inherit"
PROJECT_SENTINEL = "CONTEXT_PROJECT_RULES_SENTINEL"
SKILL_SENTINEL = "CONTEXT_OPTIONAL_SKILL_SENTINEL"


def context_mcp_source():
    """Owned stdio server: discovery only, no external network or real tools."""
    return '''#!/usr/bin/python3
import json,sys
tools=[{"name":"context_fixture_%03d"%i,
 "description":"Synthetic context-volume fixture. " + "Unused discovery definition for the native client boundary. "*8,
 "inputSchema":{"type":"object","properties":{"value":{"type":"string"}}}}
 for i in range(160)]
for line in sys.stdin:
 try: request=json.loads(line)
 except ValueError: continue
 if "id" not in request: continue
 method=request.get("method")
 if method=="initialize": result={"protocolVersion":"2024-11-05","capabilities":{"tools":{}},"serverInfo":{"name":"context_fixture","version":"1.0.0"}}
 elif method=="tools/list": result={"tools":tools}
 elif method=="ping": result={}
 else:
  print(json.dumps({"jsonrpc":"2.0","id":request["id"],"error":{"code":-32601,"message":"fixture discovery only"}}),flush=True);continue
 print(json.dumps({"jsonrpc":"2.0","id":request["id"],"result":result}),flush=True)
'''


def child_environment(environ):
    env = common.child_environment(environ)
    env.pop("LAIN_AP1_TEST_KEY", None)
    env.update(DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1",
               DOTNET_NOLOGO="1", GIT_CONFIG_NOSYSTEM="1", GIT_TERMINAL_PROMPT="0")
    return env


def prepare(args):
    environment = common.environment_contract(os.environ)
    for candidate in (args.binary_sha256, args.helper_sha256, args.assembly_sha256):
        if candidate is not None:
            common.pin(candidate)
    root = args.output
    root.mkdir(parents=True, mode=0o700, exist_ok=False)
    for name in ("private-home", "runtime", "assembly", "project"):
        (root / name).mkdir(mode=0o700)
    home, codex = Path(environment["homePath"]), Path(environment["codexPath"])
    private_codex = (root / "private-home" / codex.relative_to(home)
                     if codex == home or home in codex.parents else root / "private-codex")
    private_codex.mkdir(parents=True, mode=0o700, exist_ok=True)
    config = private_codex / "config.toml"
    config.write_text('check_for_update_on_startup = false\ncli_auth_credentials_store = "file"\n'
                      'web_search = "disabled"\n[features]\nshell_snapshot = false\n'
                      '[history]\npersistence = "none"\n'
                      '[projects."/mnt/project"]\ntrust_level = "trusted"\n')
    context_fixture = getattr(args, "context_fixture", False)
    if context_fixture:
        with config.open("a") as stream:
            stream.write('[mcp_servers.context_fixture]\ncommand="/usr/bin/python3"\nargs=["/mnt/context-mcp.py"]\n')
        (root / "context-mcp.py").write_text(context_mcp_source())
        (root / "context-mcp.py").chmod(0o400)
    config.chmod(0o600)
    save(root / "native-runner-sentinel.json", {"syntheticOnly": True, "environment": environment})
    shutil.copyfile(__file__, root / "native_runner.py")
    shutil.copyfile(COMMON_PATH, root / "common.py")
    auth = root / "auth"
    auth.write_text('#!/usr/bin/python3\nimport json,os,sys\n'
                    'assert sys.argv[1:] == ["auth","token","--audience","codex"]\n'
                    'with open("/mnt/auth.calls.jsonl","a") as f:\n'
                    '    f.write(json.dumps({"argv":sys.argv[1:],"cwd":os.getcwd()})+"\\n")\n'
                    'print(' + repr(TOKEN) + ')\n')
    auth.chmod(0o500)
    plan = {"schemaVersion": 1, "environment": environment, "contextFixture": context_fixture,
            "privateCodex": str(private_codex.relative_to(root)),
            "binary": str(args.binary.absolute()), "binarySha256": args.binary_sha256,
            "helper": str(args.helper.absolute()), "helperSha256": args.helper_sha256,
            "assembly": str(args.assembly.absolute()), "assemblySha256": args.assembly_sha256,
            "hostNetworkNamespace": os.readlink("/proc/self/ns/net"),
            "hostPidNamespace": os.readlink("/proc/self/ns/pid"),
            "baseConfigSha256": digest(config),
            "baseConfig": tomllib.loads(config.read_text()),
            "fixturesSha256": {name: digest(root / name) for name in
                               ("native_runner.py", "common.py", "auth", "native-runner-sentinel.json")}}
    if context_fixture:
        plan["fixturesSha256"]["context-mcp.py"] = digest(root / "context-mcp.py")
    save(root / "plan.json", plan)
    save(root / "prepared.json", {"status": "prepared-not-executed", "nativeExecuted": False})
    return plan


def freeze_assembly(source, destination, expected):
    common.pin(expected)
    expect(source.name == "Workers.Cli.Tests.dll" and not source.is_symlink(), "select the genuine receiver DLL")
    expect(digest(source) == expected, "receiver DLL differs from explicit pin")
    files = {}
    for item in sorted(source.parent.rglob("*")):
        expect(not item.is_symlink(), "assembly tree must not contain symlinks")
        relative = item.relative_to(source.parent)
        target = destination / relative
        if item.is_dir():
            target.mkdir(mode=0o700, exist_ok=True)
        elif item.is_file():
            before = digest(item)
            with item.open("rb") as incoming, target.open("xb") as outgoing:
                shutil.copyfileobj(incoming, outgoing)
            expect(digest(target) == before, "assembly changed while copying: " + str(relative))
            target.chmod(0o400)
            files[str(relative)] = before
        else:
            raise ValueError("unsupported assembly-tree entry")
    expect(files[source.name] == expected, "receiver changed while snapshotting")
    for name in ("Workers.Cli.Tests.deps.json", "Workers.Cli.Tests.runtimeconfig.json"):
        expect(name in files, "incomplete receiver output tree: " + name)
    return files


def bubblewrap_command(root, plan):
    command = ["/usr/bin/bwrap", "--die-with-parent", "--new-session", "--unshare-pid",
               "--unshare-net", "--unshare-ipc", "--unshare-uts", "--ro-bind", "/usr", "/usr"]
    for name in ("bin", "sbin", "lib", "lib64"):
        system = Path("/") / name
        if system.is_symlink():
            command += ["--symlink", os.readlink(system), str(system)]
        elif system.exists():
            command += ["--ro-bind", str(system), str(system)]
    command += ["--dir", "/etc"]
    for name in ("ld.so.cache", "passwd", "group", "nsswitch.conf", "localtime"):
        system = Path("/etc") / name
        if system.is_file():
            command += ["--ro-bind", str(system), str(system)]
    command += ["--proc", "/proc", "--dev", "/dev", "--tmpfs", "/tmp", "--dir", "/run",
                "--bind", str(root), "/mnt", "--bind", str(root / "private-home"), plan["environment"]["homePath"]]
    if plan["privateCodex"] == "private-codex":
        command += ["--bind", str(root / "private-codex"), plan["environment"]["codexPath"]]
    for name in ("runtime", "assembly", "plan.json", "native_runner.py", "common.py", "auth", "native-runner-sentinel.json"):
        command += ["--ro-bind", str(root / name), "/mnt/" + name]
    if plan.get("contextFixture"):
        command.extend(["--ro-bind", str(root / "context-mcp.py"), "/mnt/context-mcp.py"])
    return command + ["--chdir", "/mnt", "/usr/bin/python3", "/mnt/native_runner.py", "--inner"]


def select_tool(body):
    choices = []
    for tool in common.advertised_tools(body):
        namespace = tool.get("name") if tool.get("type") == "namespace" else None
        for child in tool.get("tools", []) if namespace else [tool]:
            choices.append((namespace, child.get("name"), child.get("type")))
    if ("functions", "exec", "custom") in choices:
        return "custom_tool_call", "functions", "exec"
    for namespace, name, kind in choices:
        if name == "exec_command" and kind == "function" and namespace in (None, "functions"):
            return "function_call", namespace, name
    raise ValueError("client advertised neither functions.exec nor direct exec_command")


def shell_command(model):
    action = ('if touch read-only-denied.txt 2>/dev/null; then echo READ_ONLY_WRITE_ALLOWED; exit 81; '
              'else echo DENIED; fi' if model == "fixture-read" else
              "printf '%s\\n' NATIVE_WRITE_OK > result.txt; echo NATIVE_WRITE_OK")
    return (action + '; printf "NATIVE_CWD=%s\\nNATIVE_HOME=%s\\nNATIVE_CODEX_HOME=%s\\n" '
            '"$PWD" "$HOME" "${CODEX_HOME-__UNSET__}"')


def shell_output(item, kind):
    texts = common.output_texts(item.get("output"))
    if kind == "custom_tool_call":
        for text in texts:
            try:
                record = json.loads(text)
            except ValueError:
                continue
            if isinstance(record, dict) and record.get("exit_code") == 0 and isinstance(record.get("output"), str):
                return record["output"]
        raise ValueError("code-mode continuation lacks successful real shell output")
    text = "\n".join(texts)
    expect(re.search(r"(?m)^(?:Process exited with code|Exit code:) 0$", text),
           "direct shell continuation lacks exit zero")
    expect("Output:\n" in text, "direct shell result lacks native output section")
    return text.split("Output:\n", 1)[1]


def check_base_config(before, after):
    expect(before == after, "unexpected synthetic base configuration change")
    return {"unchanged": True}


def tool_surface(tools):
    # Native Codex groups one MCP server's tools in a namespace. Keep top-level
    # entries (Beam's observed limit) distinct from callable members and bytes.
    def leaves(items):
        return sum(leaves(tool.get("tools", [])) if tool.get("type") == "namespace" else 1
                   for tool in items)
    return {"topLevelTools": len(tools), "leafTools": leaves(tools),
            "toolBytes": len(json.dumps(tools).encode())}


class Scenario:
    def __init__(self, root, plan):
        self.root, self.plan = root, plan
        self.requests = {model: [] for model in MODELS + ((CONTEXT_MODEL,) if plan.get("contextFixture") else ())}
        self.proofs, self.tools, self.errors = {}, {}, []
        self.lock = threading.Lock()

    def respond(self, body):
        with self.lock:
            model = body.get("model")
            expect(model in self.requests, "request left the synthetic worker aliases")
            self.requests[model].append(body)
            count = len(self.requests[model])
            save(self.root / f"{model}-request-{count}.json", body)
            if model == CONTEXT_MODEL:
                expect(count == 1, "unexpected context baseline retry")
                return common.events("resp_context_baseline", {"id":"message_context_baseline",
                    "type":"message", "role":"assistant", "content":[{"type":"output_text", "text":"CONTEXT_BASELINE_OK"}]})
            call_id = "call_native_" + model
            if count == 1:
                kind, namespace, name = select_tool(body)
                self.tools[model] = (kind, namespace, name)
                arguments = {"cmd": shell_command(model), "shell": "/bin/sh", "login": False,
                             "yield_time_ms": 10000, "max_output_tokens": 2000}
                item = {"id": "tool_" + model, "type": kind, "name": name, "call_id": call_id}
                if namespace:
                    item["namespace"] = namespace
                if kind == "custom_tool_call":
                    item["input"] = 'text(JSON.stringify(await tools.exec_command(' + json.dumps(arguments) + ')));'
                else:
                    item["arguments"] = json.dumps(arguments)
                return common.events("resp_tool_" + model, item)
            expect(count == 2, "unexpected retry or extra turn for " + model)
            kind = self.tools[model][0]
            output_kind = "custom_tool_call_output" if kind == "custom_tool_call" else "function_call_output"
            outputs = [item for item in body.get("input", []) if item.get("type") == output_kind and item.get("call_id") == call_id]
            expect(len(outputs) == 1, "missing matching native tool continuation")
            output = shell_output(outputs[0], kind)
            values = dict(re.findall(r"(?m)^NATIVE_(CWD|HOME|CODEX_HOME)=(.*)$", output))
            expect(values.get("HOME") == self.plan["environment"]["HOME"], "shell changed HOME")
            expect(values.get("CODEX_HOME") == (self.plan["environment"]["CODEX_HOME"] or "__UNSET__"), "shell changed CODEX_HOME")
            cwd = values.get("CWD")
            expect(cwd and Path(cwd).is_absolute(), "shell did not report its actual cwd")
            if model == "fixture-read":
                expect("DENIED\n" in output and "READ_ONLY_WRITE_ALLOWED" not in output, "native read-only write was not denied")
                expect(cwd == "/mnt/project", "read worker left source cwd")
            else:
                expect("NATIVE_WRITE_OK\n" in output and cwd != "/mnt/project", "write did not use native worktree cwd")
                expect(Path(cwd, "result.txt").read_text().strip() == "NATIVE_WRITE_OK", "native write side effect missing")
            expect(not (self.root / "project/read-only-denied.txt").exists(), "read worker modified source")
            expect(not (self.root / "project/result.txt").exists(), "write worker modified source")
            proof = {"tool": self.tools[model], "cwd": cwd, "stdout": output}
            if kind == "custom_tool_call":
                proof["helper"] = common.observed_helper(self.plan["helperSha256"])
            self.proofs[model] = proof
            return common.events("resp_final_" + model, {"id": "message_" + model, "type": "message", "role": "assistant",
                                 "content": [{"type": "output_text", "text": "NATIVE_READ_OK" if model == "fixture-read" else "NATIVE_WRITE_OK"}]})


def handler_for(scenario):
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_):
            pass

        def do_POST(self):
            try:
                expect(self.path == "/v1/responses", "unexpected synthetic endpoint")
                expect(self.headers.get("Authorization") == "Bearer " + TOKEN, "non-fixture credential")
                expect(not self.headers.get("Content-Encoding"), "unexpected compressed request")
                size = int(self.headers.get("Content-Length", "0"))
                expect(0 < size <= 4 * 1024 * 1024, "invalid request length")
                events = scenario.respond(json.loads(self.rfile.read(size)))
                self.send_response(200)
                self.send_header("Content-Type", "text/event-stream")
                self.send_header("Connection", "close")
                self.end_headers()
                for event in events:
                    self.wfile.write(("event: " + event["type"] + "\ndata: " + json.dumps(event) + "\n\n").encode())
                self.wfile.flush()
            except Exception as error:
                scenario.errors.append(f"{type(error).__name__}: {error}")
                save(scenario.root / "provider-errors.json", scenario.errors)
                self.send_error(400, "native fixture rejected request")

        def do_GET(self):
            scenario.errors.append("unexpected GET " + self.path)
            self.send_error(400, "native fixture rejects discovery")
    return Handler


def inner():
    root = Path("/mnt")
    plan = json.loads((root / "plan.json").read_text())
    report = {"status": "failed", "syntheticOnly": True, "nativeExecuted": False}
    scenario = Scenario(root, plan)
    process = server = None
    try:
        for name in ("HOME", "CODEX_HOME"):
            expect(os.environ.get(name) == plan["environment"][name], name + " changed")
        for namespace in ("net", "pid"):
            key = "hostNetworkNamespace" if namespace == "net" else "hostPidNamespace"
            expect(os.readlink("/proc/self/ns/" + namespace) != plan[key], "namespace isolation missing")
        sentinel = json.loads((root / "native-runner-sentinel.json").read_text())
        expect(sentinel["syntheticOnly"] and sentinel["environment"] == plan["environment"], "private sentinel mismatch")
        for name, expected in plan["fixturesSha256"].items():
            expect(digest(root / name) == expected, "fixture changed: " + name)
        for name, expected in plan["assemblyFilesSha256"].items():
            expect(digest(root / "assembly" / name) == expected, "assembly changed: " + name)
        expect(digest(root / "runtime/codex-lain") == plan["binarySha256"], "CLI pin mismatch")
        expect(digest(root / "runtime/codex-code-mode-host") == plan["helperSha256"], "helper pin mismatch")
        config = Path(plan["environment"]["codexPath"]) / "config.toml"
        expect(digest(config) == plan["baseConfigSha256"], "base synthetic config mismatch")
        project = root / "project"
        (project / "writable-before-native").write_text("source is writable outside native sandbox\n")
        (project / "writable-before-native").unlink()
        report["sourceWritableBeforeNative"] = True
        (project / "README.md").write_text("Committed synthetic native WorkerBatchRunner fixture.\n")
        if plan.get("contextFixture"):
            (project / "AGENTS.md").write_text(PROJECT_SENTINEL + ": Preserve project rules in every context mode.\n")
            skill = project / ".agents/skills/context-synthetic/SKILL.md"
            skill.parent.mkdir(parents=True)
            skill.write_text("---\nname: context-synthetic\ndescription: " + SKILL_SENTINEL + "\n---\nSynthetic discovery only.\n")
        env = child_environment(os.environ)
        for args in (("init", "-b", "main"), ("add", "."),
                     ("-c", "user.name=Lain Native Fixture", "-c", "user.email=native@fixture.invalid",
                      "-c", "commit.gpgsign=false", "commit", "-m", "Synthetic fixture")):
            subprocess.run(["/usr/bin/git", *args], cwd=project, env=env, check=True, capture_output=True, timeout=10)
        server = ThreadingHTTPServer(("127.0.0.1", PORT), handler_for(scenario))
        server.daemon_threads = True
        threading.Thread(target=server.serve_forever, daemon=True).start()
        argv = ["/usr/bin/dotnet", "/mnt/assembly/Workers.Cli.Tests.dll", "--native-run", "/mnt", "/mnt/runtime/codex-lain"]
        report.update(nativeExecuted=True, argv=argv)
        with (root / "receiver.stdout").open("wb") as stdout, (root / "receiver.stderr").open("wb") as stderr:
            process = subprocess.Popen(argv, cwd="/mnt", env=env, stdin=subprocess.DEVNULL,
                                       stdout=stdout, stderr=stderr, start_new_session=True)
            report["exitCode"] = process.wait(timeout=100)
        expect(report["exitCode"] == 0, "genuine runner failed; inspect native-runner-result.json and receiver.stderr")
        expect(not scenario.errors and all(len(scenario.requests[model]) == 2 for model in MODELS), "workers did not complete exactly two requests each")
        if plan.get("contextFixture"):
            baseline = scenario.requests[CONTEXT_MODEL]
            expect(len(baseline) == 1, "missing single inherited-context baseline")
            before = baseline[0]
            namespaces = [tool for tool in before.get("tools", [])
                          if tool.get("type") == "namespace" and tool.get("name") == "mcp__context_fixture"]
            expect(len(namespaces) == 1 and len(namespaces[0].get("tools", [])) == 160,
                   "synthetic MCP baseline did not advertise its 160 namespace members")
            expect(SKILL_SENTINEL in json.dumps(before), "inherited baseline omitted the synthetic skill catalog")
            context_proof = {"baseline":tool_surface(before["tools"]), "native":{}}
            for model in MODELS:
                after = scenario.requests[model][0]
                text = json.dumps(after)
                expect(len(after.get("tools", [])) <= 128, "compact worker still exceeds Beam boundary")
                expect(not any("context_fixture" in json.dumps(t) for t in after.get("tools", [])), "compact worker retained synthetic MCP tools")
                expect(PROJECT_SENTINEL in text, "compact context lost project rules")
                expect(SKILL_SENTINEL not in text, "compact context retained optional skill catalog")
                context_proof["native"][model] = {**tool_surface(after["tools"]),
                    "projectRulesPreserved":True,"optionalSkillCatalogOmitted":True}
            report["contextProof"] = context_proof
        expect(set(scenario.proofs) == set(MODELS), "native shell proofs incomplete")
        receipt = json.loads((root / "native-runner-result.json").read_text())
        expect(receipt["state"] == "succeeded" and len(receipt["tasks"]) == 2, "runner receipt not successful")
        expect(all(task.get("nativeConfiguration") for task in receipt["tasks"]), "real native config gates missing")
        write = next(task for task in receipt["tasks"] if task["id"] == "write")
        expect(write["verifiedWorktree"]["path"] == scenario.proofs["fixture-write"]["cwd"], "receipt worktree differs from real shell cwd")
        config_change = check_base_config(plan["baseConfig"], tomllib.loads(config.read_text()))
        expect(digest(config) == plan["baseConfigSha256"], "base synthetic config bytes changed")
        expect((root / "auth.calls.jsonl").is_file(), "synthetic auth executable was not invoked")
        report.update(status="passed", providerRequests={model: len(items) for model, items in scenario.requests.items()},
                      shellProofs=scenario.proofs, runnerReceiptSha256=digest(root / "native-runner-result.json"),
                      baseConfigChange=config_change, productionStateChanged=False)
    except Exception as error:
        report["error"] = f"{type(error).__name__}: {error}"
    finally:
        if process is not None and process.poll() is None:
            common.kill_owned_group(process)
        if server is not None:
            server.shutdown()
            server.server_close()
        report["providerErrors"] = scenario.errors
        save(root / "result.json", report)
    return 0 if report["status"] == "passed" else 1


def run(args, plan):
    report = {"status": "failed", "syntheticOnly": True, "nativeAttempted": False}
    try:
        common.freeze(args.binary, args.output / "runtime/codex-lain", plan["binarySha256"])
        common.freeze(args.helper, args.output / "runtime/codex-code-mode-host", plan["helperSha256"])
        plan["assemblyFilesSha256"] = freeze_assembly(args.assembly, args.output / "assembly", plan["assemblySha256"])
        save(args.output / "plan.json", plan)
        command = bubblewrap_command(args.output, plan)
        save(args.output / "launch.json", {"argv": command, "environmentNames": sorted(child_environment(os.environ))})
        report["nativeAttempted"] = True
        with (args.output / "supervisor.stdout").open("wb") as stdout, (args.output / "supervisor.stderr").open("wb") as stderr:
            process = subprocess.Popen(command, env=child_environment(os.environ), stdin=subprocess.DEVNULL,
                                       stdout=stdout, stderr=stderr, start_new_session=True)
            try:
                exit_code = process.wait(timeout=120)
            finally:
                if process.poll() is None:
                    common.kill_owned_group(process)
        result = json.loads((args.output / "result.json").read_text())
        expect(exit_code == 0 and result.get("status") == "passed", "isolated native runner failed; retained result has details")
        report.update(status="passed", nativeExecuted=True, resultSha256=digest(args.output / "result.json"),
                      binarySha256=plan["binarySha256"], helperSha256=plan["helperSha256"],
                      assemblySha256=plan["assemblySha256"], providerRequests=result["providerRequests"])
    except Exception as error:
        report["error"] = f"{type(error).__name__}: {error}"
    save(args.output / "summary.json", report)
    print(json.dumps(report, indent=2))
    return 0 if report["status"] == "passed" else 1


def main():
    if sys.argv[1:] == ["--inner"]:
        return inner()
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("binary", "helper", "assembly", "output"):
        parser.add_argument("--" + name, type=Path, required=True)
    for name in ("binary", "helper", "assembly"):
        parser.add_argument("--" + name + "-sha256")
    parser.add_argument("--run", action="store_true")
    parser.add_argument("--context-fixture", action="store_true", help="Compare inherited MCP namespace with 160 members against explicit native context")
    args = parser.parse_args()
    if args.run and not all((args.binary_sha256, args.helper_sha256, args.assembly_sha256)):
        parser.error("--run requires explicit binary, helper and receiver DLL SHA256 pins")
    args.output = args.output.absolute()
    try:
        plan = prepare(args)
        if args.run:
            return run(args, plan)
        print(json.dumps({"status": "prepared-not-executed", "nativeExecuted": False, "output": str(args.output)}))
        return 0
    except (OSError, ValueError, KeyError) as error:
        parser.exit(2, f"Native runner harness: {error}\n")


if __name__ == "__main__":
    raise SystemExit(main())
