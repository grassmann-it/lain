#!/usr/bin/env python3
"""Exact-Host restored Gateway regression using a fake local Core process.

No real Core/OAuth/provider qualification is claimed. Run only in Root's runtime
slot: python3 restored_gateway_probe.py /absolute/Lain.Host.dll
"""
import hashlib
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request


FAKE_CORE = r'''#!/usr/bin/env python3
import http.server, json, os, pathlib, re, sys, threading
args = sys.argv[1:]
def option(name): return pathlib.Path(args[args.index(name) + 1])
root = pathlib.Path(os.environ["LAIN_HOME"])
config, bindings = option("--config"), option("--bindings")
if config == root / "proxy/config.yaml": sys.exit(12)
assert config.is_relative_to(root) and bindings.is_relative_to(root)
assert option("--journal") != root / "proxy/events.jsonl"
assert option("--owned-auth-dir") == root / "proxy/auth-owned"
assert not list(option("--owned-auth-dir").iterdir())
assert "--parent-stdin" in args
binding = json.loads(bindings.read_text())
assert not binding["accounts"] and not binding["routes"]
port = int(re.search(r"(?m)^port:\s*[\"']?(\d+)", config.read_text()).group(1))
assert port == int(os.environ["LAIN_REFERENCE_PROXY_PORT"])
token = option("--management-token-file").read_text().strip()
def record(item):
    with (root / "fake-core-events.jsonl").open("a") as output:
        output.write(json.dumps(item) + "\n")
record({"event": "started", "pid": os.getpid(), "config": str(config), "bindings": str(bindings)})
class Handler(http.server.BaseHTTPRequestHandler):
    def log_message(self, *args): pass
    def respond(self, value, status=200):
        raw = json.dumps(value).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(raw)))
        self.end_headers()
        self.wfile.write(raw)
    def dispatch(self):
        if self.headers.get("Authorization") != "Bearer " + token:
            return self.respond({}, 401)
        record({"event": "request", "method": self.command, "path": self.path})
        if self.command == "GET" and self.path == "/lain/health":
            return self.respond({"ready": True})
        if self.command == "POST" and self.path == "/lain/login/codex":
            return self.respond({"state": "synthetic-state", "authorizationUrl": "https://invalid.example/authorize"})
        if self.path == "/lain/login/synthetic-state":
            return self.respond({"status": "cancelled" if self.command == "DELETE" else "pending"})
        return self.respond({}, 404)
    do_GET = do_POST = do_DELETE = dispatch
server = http.server.ThreadingHTTPServer(("127.0.0.1", port), Handler)
def parent():
    sys.stdin.buffer.read()
    server.shutdown()
threading.Thread(target=parent, daemon=True).start()
server.serve_forever()
'''


def require(condition, description):
    if not condition:
        raise AssertionError(description)


def free_port():
    with socket.socket() as reservation:
        reservation.bind(("127.0.0.1", 0))
        return reservation.getsockname()[1]


def run_case(assembly, recovery):
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    with tempfile.TemporaryDirectory(prefix="lain-independent-gateway-") as temporary:
        root = Path(temporary)
        state = root / "state"
        (state / "proxy/auth-owned").mkdir(parents=True, mode=0o700)
        secret = "synthetic-quarantined-refresh-never-use"
        (state / "proxy/auth-owned/old.json").write_text(json.dumps({
            "type": "codex", "account_id": "old-subject", "refresh_token": secret}))
        (state / "restore-requires-reconnect").write_text("synthetic-restore-epoch")
        proof = {
            "configuration.json": json.dumps({"Revision": 0, "AppliedRevision": 0,
                "Accounts": [], "Routes": [], "Profiles": [], "RuntimeMode": "gateway"}),
            "active-profile.json": json.dumps({"id": "prior-profile", "revision": 0}),
            "proxy/runtime.json": json.dumps({"valid": True, "revision": 0, "profileId": "prior-profile"}),
            "proxy/config.yaml": "# saved runtime references old owned grant\nport: 1\n",
            "proxy/bindings.json": json.dumps({"accounts": {"old-account": "old.json"}, "routes": {}}),
            "proxy/events.jsonl": "{\"preserve\":true}\n",
        }
        if recovery:
            proof["profile-application.json"] = json.dumps({"id": "interrupted", "profileId": "prior-profile",
                "revision": 0, "state": "recovery-required"})
        for relative, content in proof.items():
            (state / relative).write_text(content)
        expected = {relative: (state / relative).read_bytes() for relative in proof}
        fake = root / "fake-core"
        fake.write_text(FAKE_CORE)
        fake.chmod(0o700)
        digest = hashlib.sha256(fake.read_bytes()).hexdigest()
        (root / "binary-manifest.json").write_text(json.dumps({"sha256": digest}))
        (root / "qualification.json").write_text(json.dumps({"sha256": digest, "passed": False, "syntheticPassed": True}))
        host_port, core_port = free_port(), free_port()
        while core_port == host_port:
            core_port = free_port()
        environment = dict(os.environ, LAIN_HOME=str(state), LAIN_PROXY_PATH=str(fake),
            LAIN_REFERENCE_QUALIFICATION="1", LAIN_REFERENCE_DISABLE_QUOTA="1",
            LAIN_REFERENCE_HOST_PORT=str(host_port), LAIN_REFERENCE_PROXY_PORT=str(core_port))
        base = f"http://127.0.0.1:{host_port}/api/v1"

        def request(path, method="GET", value=None):
            headers = {"Content-Type": "application/json",
                "Authorization": "Bearer " + (state / "credentials/host.token").read_text().strip()}
            message = urllib.request.Request(base + path, method=method, headers=headers,
                data=None if value is None else json.dumps(value).encode())
            try:
                response = opener.open(message, timeout=25)
            except urllib.error.HTTPError as error:
                response = error
            with response:
                body = response.read()
                require(secret.encode() not in body, "quarantined secret leaked in response")
                return response.status, json.loads(body) if body else None

        with (root / "host.log").open("w+") as log:
            process = subprocess.Popen(["dotnet", str(assembly)], cwd=root, env=environment,
                stdin=subprocess.DEVNULL, stdout=log, stderr=log)
            try:
                deadline = time.monotonic() + 30
                while time.monotonic() < deadline:
                    require(process.poll() is None, "restored Gateway Host exited")
                    try:
                        if request("/status")[0] == 200:
                            break
                    except (OSError, urllib.error.URLError):
                        pass
                    time.sleep(0.05)
                else:
                    raise AssertionError("restored Gateway Host not reachable")
                require(not any((state / "proxy/auth-owned").iterdir()), "restore did not quarantine grant")
                require(not request("/status")[1]["proxyReady"], "restored Gateway falsely ready")
                code, operation = request("/accounts/login", "POST", {"revision": 0,
                    "provider": "codex", "method": "codex-owned", "purposes": ["inference", "quota"]})
                require(code == 200 and operation["state"] == "waiting", "restored Codex login could not begin")
                require(not request("/status")[1]["proxyReady"], "login-only management claims inference readiness")
                require(request("/configuration")[1]["revision"] == 0, "begin login changed configuration revision")
                for relative, content in expected.items():
                    require((state / relative).read_bytes() == content, "login changed active file: " + relative)
                code, cancelled = request("/accounts/login/" + operation["id"], "DELETE")
                require(code == 200 and cancelled["state"] == "cancelled", "login cancellation failed")
                if recovery:
                    require(request("/service/start", "POST")[0] == 409, "service start bypassed incomplete application proof")
                require(not request("/status")[1]["proxyReady"], "cancellation/start claimed inference readiness")
                for relative, content in expected.items():
                    require((state / relative).read_bytes() == content, "repair changed active file: " + relative)
                events = [json.loads(line) for line in (state / "fake-core-events.jsonl").read_text().splitlines()]
                require(sum(e["event"] == "started" for e in events) == 1, "login launched duplicate bootstrap children")
                require(sum(e.get("path") == "/lain/login/codex" for e in events) == 1, "unexpected login begin count")
            finally:
                process.terminate()
                try:
                    process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait(timeout=5)
                log.seek(0)
                require(secret not in log.read(), "quarantined secret leaked in Host output")


def main():
    os.umask(0o077)
    assembly = Path(sys.argv[1]).resolve(strict=True)
    for recovery in (False, True):
        run_case(assembly, recovery)
    print(json.dumps({"result": "passed", "hostAssemblySha256": hashlib.sha256(assembly.read_bytes()).hexdigest(),
        "scope": "restored Gateway legacy and interrupted runtime, fake Core bootstrap/login/cancel, active proof preserved",
        "actualCoreQualified": False, "providerRequests": 0}))


if __name__ == "__main__":
    main()
