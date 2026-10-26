#!/usr/bin/env python3
"""Prepare an independent Codex 0.164.0 helper proof; native execution needs --run.

Run only after Root releases the bounded runtime lane. --binary selects the
installed patched CLI bytes; --helper selects a candidate, never a discovered
system helper. --run additionally requires both explicit SHA256 pins. Snapshots
are siblings in a read-only private mount. No HOME/CODEX_HOME reassignment,
production installation, credential read, external provider, or build occurs.

Evidence proves functions.exec -> tools.exec_command -> real shell -> matching
custom_tool_call_output -> provider continuation -> final assistant message.
Preparation and offline unit tests do not qualify a native helper.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import threading
import uuid

COMMIT = "6aaac694803004a63c6f393d153c96d22521a6b9"
MODEL = "fixture-ap1-code-mode"
PORT = 18579
CALL_ID = "call_ap1_shell"
FINAL = "AP1 native code-mode shell and continuation verified."
TOKEN = "lain-ap1-synthetic-only"
HELPER = "codex-code-mode-host"
SOURCE_FILES = (
    "codex-rs/code-mode-protocol/src/lib.rs",
    "codex-rs/protocol/src/tool_name.rs",
    "codex-rs/core/src/tools/code_mode/execute_spec.rs",
    "codex-rs/core/tests/common/responses.rs",
    "codex-rs/core/tests/suite/code_mode.rs",
    "codex-rs/models-manager/models.json",
)


def expect(condition, message):
    if not condition:
        raise ValueError(message)


def digest(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def save(path, value):
    Path(path).write_text(json.dumps(value, indent=2) + "\n")
    Path(path).chmod(0o600)


def pin(value):
    expect(isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value), "a full lowercase SHA256 pin is required")
    return value


def source_contract(source):
    provenance = source / "lain-picker-source.json"
    manifest = json.loads(provenance.read_text())
    expect(manifest["commit"] == COMMIT, "source is not the pinned 0.164.0 commit")
    raw = {name: (source / name).read_text() for name in SOURCE_FILES}
    names = {}
    for constant, filename in (("PUBLIC_TOOL_NAME", SOURCE_FILES[0]), ("WAIT_TOOL_NAME", SOURCE_FILES[0]),
                               ("DEFAULT_FUNCTION_NAMESPACE", SOURCE_FILES[1])):
        match = re.search(r"pub const " + constant + r':\s*&str\s*=\s*"([^"]+)"', raw[filename])
        expect(match is not None, "source tool name declaration missing: " + constant)
        names[constant] = match[1]
    expect(names == {"PUBLIC_TOOL_NAME": "exec", "WAIT_TOOL_NAME": "wait", "DEFAULT_FUNCTION_NAMESPACE": "functions"},
           "unreviewed source tool names; do not guess another wire protocol")
    expect("ToolSpec::Freeform" in raw[SOURCE_FILES[2]] and '"custom_tool_call"' in raw[SOURCE_FILES[3]]
           and "tools.exec_command" in raw[SOURCE_FILES[4]], "source does not contain the reviewed code-mode protocol")
    model = copy.deepcopy(json.loads(raw[SOURCE_FILES[5]])["models"][0])
    model.update(slug=MODEL, display_name="AP1 synthetic code-mode proof", description="Loopback fixture only",
                 visibility="list", supported_in_api=True, priority=0, prefer_websockets=False,
                 use_responses_lite=True, tool_mode="code_mode_only", shell_type="unified_exec",
                 multi_agent_version=None, multi_agent_reasoning_effort=None, node_repl_auto_review_required=False,
                 requires_sandboxed_review=False, supports_reasoning_summaries=False,
                 default_reasoning_level="none", supported_reasoning_levels=[], support_verbosity=False,
                 base_instructions="Use the synthetic fixture tool, then return its final message.")
    return {"path": str(source), "commit": COMMIT, "provenance": manifest, "provenanceSha256": digest(provenance), "toolNames": names,
            "filesSha256": {name: digest(source / name) for name in SOURCE_FILES}}, {"models": [model]}


def environment_contract(environ):
    home_raw = environ.get("HOME")
    expect(bool(home_raw), "HOME must already be set; the harness never assigns it")
    home = Path(home_raw)
    expect(home.is_absolute() and home != Path("/") and ".." not in home.parts, "unsafe original HOME")
    codex_raw = environ.get("CODEX_HOME")
    codex = Path(codex_raw) if codex_raw is not None else home / ".codex"
    expect(codex.is_absolute() and codex != Path("/") and ".." not in codex.parts, "CODEX_HOME must already be absolute when set")
    expect(codex not in home.parents, "CODEX_HOME cannot be an ancestor of HOME in this isolated layout")
    for directory in (home, codex):
        expect(not any(directory == Path(base) or Path(base) in directory.parents
                       for base in ("/usr", "/bin", "/lib", "/lib64", "/sbin", "/etc", "/dev", "/proc", "/run", "/mnt")),
               "HOME/CODEX_HOME would overlap an isolated runtime mount")
    return {"HOME": home_raw, "CODEX_HOME": codex_raw, "homePath": str(home), "codexPath": str(codex)}


def child_environment(environ):
    # Copy these values byte-for-byte. Absence of CODEX_HOME remains absence.
    result = {key: environ[key] for key in ("HOME", "CODEX_HOME", "USER", "LOGNAME", "LANG", "LC_ALL", "TZ") if key in environ}
    result.update(PATH="/usr/bin:/bin", SHELL="/bin/sh", PYTHONDONTWRITEBYTECODE="1",
                  LAIN_AP1_TEST_KEY=TOKEN, NO_PROXY="127.0.0.1,localhost")
    return result


def fixture_config():
    return f'''# AP1 synthetic sentinel; no user configuration is copied.
model = "{MODEL}"
model_provider = "lain"
model_catalog_json = "/mnt/models.json"
approval_policy = "never"
sandbox_mode = "danger-full-access"
web_search = "disabled"
check_for_update_on_startup = false
cli_auth_credentials_store = "file"

[model_providers.lain]
name = "AP1 isolated loopback fixture"
base_url = "http://127.0.0.1:{PORT}/v1"
wire_api = "responses"
env_key = "LAIN_AP1_TEST_KEY"
requires_openai_auth = false
request_max_retries = 0
stream_max_retries = 0

[features]
code_mode = true
code_mode_only = true
code_mode_host = true
unified_exec = true
multi_agent = false
shell_snapshot = false

[shell_environment_policy]
inherit = "all"

[history]
persistence = "none"

[tui]
screen_reader_detection_done = true
'''


def prepare(args, environ=None):
    environ = os.environ if environ is None else environ
    environment = environment_contract(environ)
    source, models = source_contract(args.source)
    for candidate in (args.binary_sha256, args.helper_sha256):
        if candidate is not None:
            pin(candidate)
    output = args.output
    output.mkdir(parents=True, mode=0o700, exist_ok=False)
    private_home = output / "private-home"
    private_home.mkdir(mode=0o700)
    home, codex = Path(environment["homePath"]), Path(environment["codexPath"])
    if codex == home or home in codex.parents:
        private_codex = private_home / codex.relative_to(home)
    else:
        private_codex = output / "private-codex"
    private_codex.mkdir(parents=True, mode=0o700, exist_ok=True)
    for name in ("work", "runtime"):
        (output / name).mkdir(mode=0o700)
    nonce = uuid.uuid4().hex
    save(private_home / ".ap1-synthetic-home.json", {"nonce": nonce, "syntheticOnly": True})
    for filename in ("config.toml", "lain.config.toml"):
        (private_codex / filename).write_text(fixture_config())
        (private_codex / filename).chmod(0o600)
    save(output / "models.json", models)
    shutil.copyfile(__file__, output / "harness.py")
    (output / "harness.py").chmod(0o600)
    script = '''import json, os
from pathlib import Path
record = {"nonce": NONCE, "HOME": os.environ.get("HOME"), "CODEX_HOME": os.environ.get("CODEX_HOME"),
          "cwd": os.getcwd(), "pid": os.getpid(), "pidNamespace": os.readlink("/proc/self/ns/pid"),
          "networkNamespace": os.readlink("/proc/self/ns/net")}
Path("/mnt/work/shell-proof.json").write_text(json.dumps(record) + "\\n")
print("AP1_SHELL_PROOF:" + NONCE)
'''.replace("NONCE", repr(nonce))
    (output / "shell_proof.py").write_text(script)
    (output / "shell_proof.py").chmod(0o600)
    plan = {"schemaVersion": 1, "environment": environment, "source": source, "nonce": nonce,
            "binary": str(args.binary.absolute()), "helper": str(args.helper.absolute()),
            "binarySha256": args.binary_sha256, "helperSha256": args.helper_sha256,
            "hostNetworkNamespace": os.readlink("/proc/self/ns/net"), "hostPidNamespace": os.readlink("/proc/self/ns/pid"),
            "privateCodex": str(private_codex.relative_to(output)),
            "fixtureSha256": {name: digest(output / name) for name in ("models.json", "harness.py", "shell_proof.py")},
            "configSha256": {name: digest(private_codex / name) for name in ("config.toml", "lain.config.toml")}}
    save(output / "plan.json", plan)
    save(output / "prepared.json", {"status": "prepared-not-executed", "nativeExecuted": False, "planSha256": digest(output / "plan.json")})
    return plan


def freeze(source, destination, expected):
    pin(expected)
    resolved = source.resolve(strict=True)
    expect(resolved.is_file() and os.access(resolved, os.X_OK), "candidate must be an executable file: " + str(source))
    expect(digest(resolved) == expected, "candidate SHA256 differs from explicit pin: " + str(source))
    with resolved.open("rb") as incoming, destination.open("xb") as outgoing:
        shutil.copyfileobj(incoming, outgoing)
    destination.chmod(0o500)
    expect(digest(destination) == expected, "candidate changed while making its private snapshot")


def bubblewrap_command(output, plan):
    command = ["/usr/bin/bwrap", "--die-with-parent", "--new-session", "--unshare-pid", "--unshare-net", "--unshare-ipc", "--unshare-uts"]
    # Start with an empty root, rather than exposing the user's other files via /.
    command += ["--ro-bind", "/usr", "/usr"]
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
                "--bind", str(output), "/mnt", "--bind", str(output / "private-home"), plan["environment"]["homePath"]]
    if plan["privateCodex"] == "private-codex":
        command += ["--bind", str(output / "private-codex"), plan["environment"]["codexPath"]]
    for name in ("runtime", "plan.json", "models.json", "harness.py", "shell_proof.py"):
        command += ["--ro-bind", str(output / name), "/mnt/" + name]
    return command + ["--chdir", "/mnt/work", "/usr/bin/python3", "/mnt/harness.py", "--inner"]


def observed_helper(expected):
    for process in Path("/proc").iterdir():
        if not process.name.isdigit():
            continue
        try:
            executable = os.readlink(process / "exe")
            if executable != "/mnt/runtime/" + HELPER:
                continue
            expect(digest(process / "exe") == expected, "running helper differs from its candidate pin")
            arguments = (process / "cmdline").read_bytes().decode().split("\0")[:-1]
            # The default process-owned transport starts this executable without
            # flags; only the gRPC variant passes --listen. Identity, pinned
            # bytes and the real code-mode continuation establish which ran.
            expect(bool(arguments), "candidate helper has no process arguments")
            return {"pid": int(process.name), "executable": executable, "sha256": expected, "argv": arguments}
        except (OSError, ProcessLookupError):
            continue
    raise ValueError("no live candidate sibling helper was observed during continuation")


def output_texts(value):
    if isinstance(value, str):
        return [value]
    if isinstance(value, list):
        return [item["text"] for item in value if isinstance(item, dict) and isinstance(item.get("text"), str)]
    return []


def advertised_tools(body):
    # Responses Lite moves tool schemas into history; the tools request field is absent.
    tools = list(body.get("tools", []))
    for item in body.get("input", []):
        if item.get("type") == "additional_tools":
            tools.extend(item.get("tools", []))
    return tools


def kill_owned_group(process):
    """Only for our Popen(start_new_session=True) children, never a discovered PID."""
    try:
        os.killpg(process.pid, signal.SIGKILL)
    except ProcessLookupError:
        if process.poll() is None:
            process.kill()
    process.wait(timeout=5)


class Scenario:
    def __init__(self, plan, root, observe=observed_helper):
        self.plan, self.root, self.observe = plan, root, observe
        self.requests, self.errors = [], []
        self.continuation = self.helper = None

    def respond(self, body):
        expect(body.get("model") == MODEL, "request left the synthetic model")
        self.requests.append(body)
        save(self.root / f"request-{len(self.requests)}.json", body)
        if len(self.requests) == 1:
            advertised = advertised_tools(body)
            namespaces = [tool for tool in advertised if tool.get("type") == "namespace" and tool.get("name") == "functions"]
            expect(len(namespaces) == 1, "actual client did not advertise the functions namespace")
            tools = namespaces[0].get("tools", [])
            expect(any(tool.get("name") == "exec" and tool.get("type") == "custom" for tool in tools), "actual client did not advertise custom functions.exec")
            expect(not any(tool.get("name") in ("exec_command", "shell_command") for tool in [*tools, *advertised]),
                   "direct shell tools are exposed; this is not the required code-mode-only path")
            arguments = {"cmd": "/usr/bin/python3 /mnt/shell_proof.py", "workdir": "/mnt/work", "shell": "/bin/sh",
                         "login": False, "yield_time_ms": 10000, "max_output_tokens": 1000}
            code = '// @exec: {"yield_time_ms": 10000, "max_output_tokens": 2000}\ntext(JSON.stringify(await tools.exec_command(' + json.dumps(arguments) + ')));'
            item = {"id": "tool_ap1", "type": "custom_tool_call", "namespace": "functions", "name": "exec", "call_id": CALL_ID, "input": code}
            return events("resp_ap1_tool", item)
        expect(len(self.requests) == 2, "unexpected retry or additional inference request")
        outputs = [item for item in body.get("input", []) if item.get("type") == "custom_tool_call_output" and item.get("call_id") == CALL_ID]
        expect(len(outputs) == 1, "second provider request lacks matching custom_tool_call_output")
        records = []
        for text in output_texts(outputs[0].get("output")):
            try:
                records.append(json.loads(text))
            except ValueError:
                pass
        command = next((row for row in records if isinstance(row, dict) and row.get("exit_code") == 0
                        and "AP1_SHELL_PROOF:" + self.plan["nonce"] in row.get("output", "")), None)
        expect(command is not None, "continuation lacks successful real shell stdout from code mode")
        proof = json.loads((self.root / "work/shell-proof.json").read_text())
        expect(proof["nonce"] == self.plan["nonce"] and proof["cwd"] == "/mnt/work", "real shell side effect missing or wrong")
        for name in ("HOME", "CODEX_HOME"):
            expect(proof[name] == self.plan["environment"][name], "real shell changed " + name)
        expect(proof["networkNamespace"] != self.plan["hostNetworkNamespace"] and proof["pidNamespace"] != self.plan["hostPidNamespace"],
               "shell ran outside the isolated namespaces")
        self.helper = self.observe(self.plan["helperSha256"])
        self.continuation = {"callId": CALL_ID, "shellResult": command, "proof": proof}
        return events("resp_ap1_final", {"id": "msg_ap1", "type": "message", "role": "assistant",
                                        "content": [{"type": "output_text", "text": FINAL}]})


def events(response_id, item):
    return [{"type": "response.created", "response": {"id": response_id}},
            {"type": "response.output_item.done", "item": item},
            {"type": "response.completed", "response": {"id": response_id, "usage": {"input_tokens": 1, "output_tokens": 1, "total_tokens": 2}}}]


def handler_for(scenario):
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_):
            pass

        def do_POST(self):
            try:
                expect(self.path == "/v1/responses", "unexpected provider endpoint: " + self.path)
                expect(self.headers.get("Authorization") == "Bearer " + TOKEN, "provider did not receive only the synthetic credential")
                expect(not self.headers.get("Content-Encoding"), "unexpected compressed request")
                size = int(self.headers.get("Content-Length", "0"))
                expect(0 < size <= 4 * 1024 * 1024, "invalid provider request size")
                payload = scenario.respond(json.loads(self.rfile.read(size)))
                self.send_response(200)
                self.send_header("Content-Type", "text/event-stream")
                self.send_header("Connection", "close")
                self.end_headers()
                for event in payload:
                    self.wfile.write(("event: " + event["type"] + "\ndata: " + json.dumps(event) + "\n\n").encode())
                self.wfile.flush()
            except Exception as error:
                scenario.errors.append(str(error))
                save(scenario.root / "provider-errors.json", scenario.errors)
                self.send_error(400, "AP1 fixture rejected request")

        def do_GET(self):
            scenario.errors.append("unexpected GET: " + self.path)
            self.send_error(400, "AP1 fixture rejects discovery")
    return Handler


def inner():
    root = Path("/mnt")
    plan = json.loads((root / "plan.json").read_text())
    report = {"status": "failed", "syntheticOnly": True, "nativeExecuted": False}
    process = server = thread = None
    scenario = Scenario(plan, root)
    try:
        for name in ("HOME", "CODEX_HOME"):
            expect(os.environ.get(name) == plan["environment"][name], name + " changed before client launch")
        expect(os.readlink("/proc/self/ns/net") != plan["hostNetworkNamespace"], "network isolation absent")
        expect(os.readlink("/proc/self/ns/pid") != plan["hostPidNamespace"], "PID isolation absent")
        home = Path(plan["environment"]["homePath"])
        expect(json.loads((home / ".ap1-synthetic-home.json").read_text())["nonce"] == plan["nonce"], "synthetic home is not mounted at original HOME")
        codex = Path(plan["environment"]["codexPath"])
        for name, expected in plan["configSha256"].items():
            expect(digest(codex / name) == expected, "synthetic config pin differs")
        for name, expected in plan["fixtureSha256"].items():
            expect(digest(root / name) == expected, "fixture input changed: " + name)
        for name, expected in (("codex-lain", plan["binarySha256"]), (HELPER, plan["helperSha256"])):
            expect(digest(root / "runtime" / name) == expected, "runtime snapshot pin differs: " + name)
        server = ThreadingHTTPServer(("127.0.0.1", PORT), handler_for(scenario))
        server.daemon_threads = True
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        command = ["/mnt/runtime/codex-lain", "--no-daemon", "--profile", "lain", "exec", "--skip-git-repo-check",
                   "--ephemeral", "--ignore-rules", "--json", "--output-last-message", "/mnt/last-message.txt",
                   "Perform the AP1 local synthetic code-mode shell proof."]
        report.update(nativeExecuted=True, argv=command)
        with (root / "client.jsonl").open("wb") as stdout, (root / "client.stderr").open("wb") as stderr:
            process = subprocess.Popen(command, cwd="/mnt/work", env=child_environment(os.environ), stdin=subprocess.DEVNULL,
                                       stdout=stdout, stderr=stderr, start_new_session=True)
            report["exitCode"] = process.wait(timeout=60)
        expect(report["exitCode"] == 0, "native CLI exited unsuccessfully; inspect client.stderr")
        expect(not scenario.errors and len(scenario.requests) == 2 and scenario.continuation is not None, "real shell/continuation protocol did not complete")
        expect((root / "last-message.txt").read_text().strip() == FINAL, "native CLI did not finish with the fixture's final message")
        expect(all(digest(codex / name) == expected for name, expected in plan["configSha256"].items()), "synthetic config sentinel changed")
        report.update(status="passed", continuation=scenario.continuation, helperProcess=scenario.helper, providerRequests=2,
                      customTool="functions.exec", nestedTool="tools.exec_command", configSentinelsUnchanged=True)
    except Exception as error:
        report["error"] = f"{type(error).__name__}: {error}"
    finally:
        if process is not None and process.poll() is None:
            kill_owned_group(process)
        if server is not None:
            server.shutdown()
            server.server_close()
        if thread is not None:
            thread.join(timeout=3)
        report["providerErrors"] = scenario.errors
        save(root / "result.json", report)
    return 0 if report["status"] == "passed" else 1


def run(args, plan):
    report = {"status": "failed", "syntheticOnly": True, "nativeAttempted": False,
              "binarySha256": plan["binarySha256"], "helperSha256": plan["helperSha256"]}
    try:
        freeze(args.binary, args.output / "runtime/codex-lain", plan["binarySha256"])
        freeze(args.helper, args.output / "runtime" / HELPER, plan["helperSha256"])
        expect(Path("/usr/bin/bwrap").is_file(), "bubblewrap is required; no unsandboxed fallback")
        command = bubblewrap_command(args.output, plan)
        save(args.output / "launch.json", {"argv": command, "environmentNames": sorted(child_environment(os.environ))})
        report["nativeAttempted"] = True
        # Killing the owned supervisor group also kills bwrap's PID-namespace init;
        # the kernel then kills every remaining client/helper/tool in that namespace.
        with (args.output / "supervisor.stdout").open("wb") as stdout, (args.output / "supervisor.stderr").open("wb") as stderr:
            supervisor = subprocess.Popen(command, env=child_environment(os.environ), stdin=subprocess.DEVNULL,
                                          stdout=stdout, stderr=stderr, start_new_session=True)
            try:
                returncode = supervisor.wait(timeout=80)
            finally:
                if supervisor.poll() is None:
                    kill_owned_group(supervisor)
        inner_report = json.loads((args.output / "result.json").read_text())
        expect(returncode == 0 and inner_report.get("status") == "passed", "isolated native proof failed; inspect retained result/logs")
        proof = json.loads((args.output / "work/shell-proof.json").read_text())
        expect(proof["nonce"] == plan["nonce"] and inner_report["continuation"]["proof"] == proof, "outer shell proof does not match inner evidence")
        expect(inner_report["helperProcess"]["sha256"] == plan["helperSha256"], "inner helper pin differs")
        expect((args.output / "last-message.txt").read_text().strip() == FINAL, "outer final-message verification failed")
        for name, expected in plan["configSha256"].items():
            expect(digest(args.output / plan["privateCodex"] / name) == expected, "outer synthetic config check failed")
        expect(len(list(args.output.glob("request-*.json"))) == 2, "outer provider-request count differs")
        report.update(status="passed", nativeExecuted=True, resultSha256=digest(args.output / "result.json"), source=plan["source"],
                      customTool="functions.exec", nestedTool="tools.exec_command", providerRequests=2)
    except Exception as error:
        report["error"] = f"{type(error).__name__}: {error}"
    save(args.output / "summary.json", report)
    print(json.dumps(report, indent=2))
    return 0 if report["status"] == "passed" else 1


def main():
    if sys.argv[1:] == ["--inner"]:
        return inner()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--binary", type=Path, required=True, help="explicit installed patched codex-lain artifact")
    parser.add_argument("--helper", type=Path, required=True, help="explicit candidate helper artifact, not the production symlink")
    parser.add_argument("--binary-sha256", help="reviewed CLI pin; mandatory for --run")
    parser.add_argument("--helper-sha256", help="reviewed candidate pin; mandatory for --run")
    parser.add_argument("--source", type=Path, required=True, help="prepared pinned 0.164.0 source")
    parser.add_argument("--output", type=Path, required=True, help="new retained private evidence directory")
    parser.add_argument("--run", action="store_true", help="explicit native execution only after Root releases the runtime lane")
    args = parser.parse_args()
    if args.run and (args.binary_sha256 is None or args.helper_sha256 is None):
        parser.error("--run needs both --binary-sha256 and --helper-sha256")
    args.source, args.output = args.source.absolute(), args.output.absolute()
    try:
        plan = prepare(args)
        if args.run:
            return run(args, plan)
        print(json.dumps({"status": "prepared-not-executed", "nativeExecuted": False, "output": str(args.output),
                          "toolNames": plan["source"]["toolNames"], "binarySha256": args.binary_sha256, "helperSha256": args.helper_sha256}, indent=2))
        return 0
    except (OSError, ValueError, KeyError) as error:
        parser.exit(2, f"AP1 native helper harness: {error}\n")


if __name__ == "__main__":
    raise SystemExit(main())
