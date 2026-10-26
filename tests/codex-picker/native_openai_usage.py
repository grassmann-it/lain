#!/usr/bin/env python3
"""Isolated real-client checks for native OpenAI usage alongside Lain auth.

Uses synthetic credentials exclusively. HOME stays unchanged; bwrap mounts a
private home at that path and isolates network/PIDs. No live provider is used.
"""
import argparse
import base64
import hashlib
import http.server
import importlib.util
import json
import os
from pathlib import Path
import pty
import select
import shutil
import struct
import subprocess
import sys
import termios
import fcntl
import threading
import time


def need(value, message):
    if not value:
        raise AssertionError(message)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def fixture_auth(kind):
    if kind == "signed-out":
        return None
    if kind == "api-key":
        return {"auth_mode": "apikey", "OPENAI_API_KEY": "synthetic-openai-api-key"}
    def encode(value):
        return base64.urlsafe_b64encode(json.dumps(value).encode()).decode().rstrip("=")
    claims = {"email": "usage-fixture@example.test", "exp": int(time.time()) + 86400,
              "https://api.openai.com/auth": {"chatgpt_plan_type": "pro",
                "chatgpt_account_id": "usage-fixture", "chatgpt_user_id": "fixture-user"}}
    token = encode({"alg": "none"}) + "." + encode(claims) + ".c3ludGhldGlj"
    return {"auth_mode": "chatgpt", "OPENAI_API_KEY": None,
            "tokens": {"id_token": token, "access_token": "synthetic-native-chatgpt",
                       "refresh_token": "synthetic-no-refresh", "account_id": "usage-fixture"},
            "last_refresh": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}


def inner():
    root = Path("/mnt")
    plan = json.loads((root / "plan.json").read_text())
    need(os.environ["HOME"] == plan["home"], "HOME changed")
    for ns in ("net", "pid"):
        need(os.readlink("/proc/self/ns/" + ns) != plan[ns], "missing namespace isolation")
    home = Path.home() / ".codex"
    need(sha(root / "package/bin/codex") == plan["binarySha256"], "wrong binary")
    requests = []
    class Handler(http.server.BaseHTTPRequestHandler):
        def log_message(self, *_):
            pass

        def respond(self, value, status=200, content_type="application/json"):
            body = value if isinstance(value, bytes) else json.dumps(value).encode()
            self.send_response(status)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            requests.append({"method": "GET", "path": self.path,
                             "authorization": self.headers.get("Authorization")})
            if self.path.endswith("/usage"):
                self.respond({"plan_type": "pro", "rate_limit": {"allowed": True,
                    "limit_reached": False, "primary_window": {"used_percent": 12,
                    "limit_window_seconds": 18000, "reset_after_seconds": 3600,
                    "reset_at": int(time.time()) + 3600}},
                    "rate_limit_reset_credits": {"available_count": 2}})
            elif self.path.endswith("/rate-limit-reset-credits"):
                self.respond({"available_count": 2, "credits": []})
            else:
                self.respond({"error": "unexpected synthetic request"}, 404)

        def do_POST(self):
            body = self.rfile.read(int(self.headers.get("Content-Length", "0")))
            requests.append({"method": "POST", "path": self.path,
                             "authorization": self.headers.get("Authorization")})
            if self.path != "/v1/responses":
                self.respond({"error": "unexpected synthetic request"}, 404)
                return
            need(json.loads(body)["model"] == "synthetic/free-a", "inference model changed")
            events = [{"type": "response.created", "response": {"id": "resp_usage_fixture"}},
                {"type": "response.output_item.done", "item": {"id": "msg_fixture", "type": "message",
                 "role": "assistant", "content": [{"type": "output_text", "text": "USAGE_AUTH_SEPARATION_OK"}]}},
                {"type": "response.completed", "response": {"id": "resp_usage_fixture",
                 "usage": {"input_tokens": 1, "output_tokens": 1, "total_tokens": 2}}}]
            self.respond("".join("data: " + json.dumps(item) + "\n\n" for item in events).encode(),
                         content_type="text/event-stream")
    server = http.server.ThreadingHTTPServer(("127.0.0.1", 18523), Handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    env = {key: os.environ[key] for key in ("HOME", "PATH", "USER", "LOGNAME", "SHELL", "LANG", "TZ") if key in os.environ}
    env["TERM"] = "xterm-256color"
    binary = "/mnt/package/bin/codex"
    with (root / "rpc.stderr").open("w") as errors:
        process = subprocess.Popen([binary, "app-server"], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                   stderr=errors, env=env, text=True, bufsize=1)
        replies = []
        import queue
        lines = queue.Queue()
        def reader():
            for line in process.stdout:
                lines.put(line)
        threading.Thread(target=reader, daemon=True).start()
        def rpc(request_id, method, params):
            process.stdin.write(json.dumps(dict(id=request_id, method=method, params=params)) + "\n")
            process.stdin.flush()
            deadline = time.monotonic() + 35
            while time.monotonic() < deadline:
                try:
                    value = json.loads(lines.get(timeout=max(.01, deadline-time.monotonic())))
                except queue.Empty:
                    break
                replies.append(value)
                if value.get("id") == request_id:
                    return value
            raise AssertionError("RPC timeout: " + method)
        try:
            initialized = rpc(1, "initialize", {"clientInfo": {"name": "lain-usage-fixture", "version": "1"},
                                                "capabilities": {"experimentalApi": True}})
            need("result" in initialized, "initialize failed")
            process.stdin.write('{"method":"initialized"}\n')
            process.stdin.flush()
            account = rpc(2, "account/read", {"refreshToken": False})
            need("result" in account, "account read failed")
            result = account["result"]
            need(result["requiresOpenaiAuth"] is False, "inference auth contract changed")
            need(result.get("workspaceRouting") is None, "native account changed workspace routing")
            if plan["nativeVisible"]:
                need(result["account"]["type"] == "chatgpt", "native account missing")
                need(result["account"]["planType"] == "pro", "wrong native plan")
            else:
                need(result["account"] is None, "unexpected provider account fallback")
            limits = rpc(3, "account/rateLimits/read", {"excludeResetCreditDetails": True})
            if plan["auth"] == "chatgpt":
                need("result" in limits, "native usage read failed")
                need(limits["result"]["rateLimits"]["primary"]["usedPercent"] == 12, "wrong usage value")
            else:
                need("error" in limits, "native usage accepted non-ChatGPT auth")
        finally:
            process.terminate()
            process.wait(timeout=10)
            (root / "rpc.json").write_text(json.dumps(replies, indent=2) + "\n")
    if plan["nativeVisible"]:
        # Exercise the unchanged native /usage menu, not an emulated renderer.
        spec = importlib.util.spec_from_file_location("picker", root / "native_model_picker.py")
        picker = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(picker)
        terminal = picker.Terminal()
        master, slave = pty.openpty()
        fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack("HHHH", picker.ROWS, picker.COLS, 0, 0))
        command = [binary] if plan.get("daemon") else [binary, "--no-daemon", "--no-alt-screen"]
        if plan.get("profile"):
            command += ["--profile", plan["profile"]]
        process = subprocess.Popen(command, stdin=slave, stdout=slave, stderr=slave, env=env)
        os.close(slave)
        raw = bytearray()
        def wait_screen(text, timeout=35):
            deadline = time.monotonic() + timeout
            while time.monotonic() < deadline:
                if select.select([master], [], [], .1)[0]:
                    try:
                        data = os.read(master, 65536)
                    except OSError:
                        break
                    raw.extend(data)
                    terminal.feed(data)
                    for response in terminal.responses:
                        os.write(master, response)
                    terminal.responses.clear()
                screen = "\n".join("".join(row) for row in terminal.grid)
                if text in screen:
                    return screen
            raise AssertionError("native screen missing " + text)
        try:
            wait_screen("Visible Free")
            time.sleep(.4)
            os.write(master, b"/usage")
            wait_screen("/usage")
            os.write(master, b"\r")
            screen = wait_screen("Account usage and resets.")
            need("View analytics" in screen and "Redeem reset" in screen, "native menu changed")
            need("Sign in with ChatGPT to use /usage" not in screen, "usage login gate remained")
            if plan.get("daemon"):
                pid_files = [home / "app-server-daemon" / name for name in ("daemon.pid", "app-server.pid")]
                pid_file = next((p for p in pid_files if p.is_file()), None)
                need(pid_file is not None, "native daemon was not started")
                pid = json.loads(pid_file.read_text())["pid"]
                need(sha(f"/proc/{pid}/exe") == plan["binarySha256"], "daemon binary differs")
                need(os.readlink(f"/proc/{pid}/ns/net") == os.readlink("/proc/self/ns/net"), "daemon escaped network namespace")
            (root / "usage-screen.txt").write_text(screen)
        finally:
            process.terminate()
            process.wait(timeout=10)
            os.close(master)
            (root / "usage.raw").write_bytes(raw)
        run = subprocess.run([binary, "--no-daemon", "exec", "--skip-git-repo-check", "--json", "Reply with the fixture marker."],
                             capture_output=True, env=env, timeout=45)
        (root / "inference.stdout").write_bytes(run.stdout)
        (root / "inference.stderr").write_bytes(run.stderr)
        need(run.returncode == 0 and b"USAGE_AUTH_SEPARATION_OK" in run.stdout, "synthetic inference failed")
        inference = [r for r in requests if r["path"] == "/v1/responses"]
        need(inference and all(r["authorization"] == "Bearer synthetic-lain-inference" for r in inference),
             "native auth leaked to Lain inference")
    usage = [r for r in requests if r["path"].endswith("/usage")]
    need(all(r["authorization"] == "Bearer synthetic-native-chatgpt" for r in usage), "wrong native usage auth")
    need(not any(r["path"].endswith("/accounts/check") for r in requests), "unexpected workspace routing discovery")
    (root / "result.json").write_text(json.dumps({"status": "passed", "case": plan["case"],
        "binarySha256": plan["binarySha256"], "nativeVisible": plan["nativeVisible"], "requests": requests}, indent=2) + "\n")


def main():
    if sys.argv[1:] == ["--inner"]:
        inner()
        return
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--binary", type=Path, required=True)
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--case", action="append", help="Run only named cases for a bounded regression probe")
    args = parser.parse_args()
    need(not args.output.exists(), "output must be new")
    args.output.mkdir(mode=0o700, parents=True)
    spec = importlib.util.spec_from_file_location("picker", Path(__file__).with_name("native_model_picker.py"))
    picker = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(picker)
    results = []
    cases = [("default-native", "chatgpt", "lain", True, None),
             ("profile-native", "chatgpt", "lain", True, "lain"),
             ("daemon-native", "chatgpt", "lain", True, None),
             ("signed-out", "signed-out", "lain", True, None),
             ("api-key", "api-key", "lain", True, None),
             ("other-provider", "chatgpt", "fixture", True, None),
             ("no-command-auth", "chatgpt", "lain", False, None)]
    for name, auth, provider, command_auth, profile in cases:
        if args.case and name not in args.case:
            continue
        case = args.output / name
        home = case / "home"
        codex = home / ".codex"
        codex.mkdir(parents=True)
        (case / "work").mkdir()
        plan = dict(case=name, auth=auth, home=str(Path.home()), profile=profile,
                    daemon=name == "daemon-native",
                    nativeVisible=auth == "chatgpt" and provider == "lain" and command_auth,
                    binarySha256=sha(args.binary),
                    net=os.readlink("/proc/self/ns/net"), pid=os.readlink("/proc/self/ns/pid"))
        (case / "plan.json").write_text(json.dumps(plan, indent=2))
        (codex / "models.json").write_text(json.dumps(picker.catalog(args.source)))
        config = picker.config(Path.home(), provider).replace("18499", "18523")
        config = config.replace('env_key = "LAIN_NATIVE_TEST_KEY"\n', "")
        config = 'chatgpt_base_url = "http://127.0.0.1:18523/backend-api"\n' + config
        if command_auth:
            config += f'\n[model_providers.{provider}.auth]\ncommand = "/usr/bin/printf"\nargs = ["synthetic-lain-inference"]\ncwd = "/mnt/work"\n'
        (codex / "config.toml").write_text(config)
        (codex / "lain.config.toml").write_text("")
        account = fixture_auth(auth)
        if account:
            (codex / "auth.json").write_text(json.dumps(account))
            (codex / "auth.json").chmod(0o600)
        shutil.copyfile(__file__, case / "usage.py")
        shutil.copyfile(Path(__file__).with_name("native_model_picker.py"), case / "native_model_picker.py")
        command = ["bwrap", "--die-with-parent", "--new-session", "--unshare-pid", "--unshare-net",
                   "--unshare-ipc", "--unshare-uts", "--ro-bind", "/", "/", "--dev", "/dev", "--proc", "/proc",
                   "--tmpfs", "/tmp", "--tmpfs", "/run", "--bind", str(home), str(Path.home()),
                   "--bind", str(case), "/mnt", "--ro-bind", str(args.package.resolve()), "/mnt/package",
                   "--ro-bind", str(args.binary.resolve()), "/mnt/package/bin/codex",
                   "--chdir", "/mnt/work", sys.executable, "/mnt/usage.py", "--inner"]
        env = {key: os.environ[key] for key in ("HOME", "PATH", "USER", "LOGNAME", "SHELL", "LANG", "TZ") if key in os.environ}
        run = subprocess.run(command, env=env, capture_output=True, timeout=140)
        (case / "supervisor.stderr").write_bytes(run.stderr)
        result = {"case": name, "exitCode": run.returncode, "verified": False}
        if (case / "result.json").exists():
            observed = json.loads((case / "result.json").read_text())
            result["verified"] = observed["status"] == "passed" and observed["binarySha256"] == sha(args.binary)
        results.append(result)
        print(json.dumps(result), flush=True)
    need(results, "no matching cases")
    status = "passed" if all(r["exitCode"] == 0 and r["verified"] for r in results) else "failed"
    (args.output / "summary.json").write_text(json.dumps(dict(status=status, cases=results), indent=2) + "\n")
    need(status == "passed", "native usage matrix failed")


if __name__ == "__main__":
    main()
