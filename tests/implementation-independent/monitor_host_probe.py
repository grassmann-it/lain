#!/usr/bin/env python3
"""Independent exact-Host Monitor probe. Synthetic temporary state; no provider calls.

Run only in Root's serialized runtime slot: python3 monitor_host_probe.py /absolute/Lain.Host.dll
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


def require(condition, description):
    if not condition:
        raise AssertionError(description)


def main():
    os.umask(0o077)
    assembly = Path(sys.argv[1]).resolve(strict=True)
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    with tempfile.TemporaryDirectory(prefix="lain-independent-monitor-") as temporary:
        root = Path(temporary)
        state = root / "state"
        state.mkdir(mode=0o700)
        (state / "initial-runtime-mode").write_text("monitor\n")
        (state / "maintainer").mkdir(mode=0o700)
        (state / "maintainer/poe-login.json").write_text(json.dumps({
            "clientId": "synthetic-maintainer", "callbackUri": "https://invalid.example/callback",
            "registrationQualified": True}))
        (state / "proxy/auth-owned").mkdir(parents=True, mode=0o700)
        secret = "synthetic-restored-refresh-never-use"
        (state / "proxy/auth-owned/synthetic.json").write_text(json.dumps({
            "type": "codex", "account_id": "synthetic-subject", "refresh_token": secret}))
        (state / "restore-requires-reconnect").write_text("synthetic-restore-epoch")
        # Reserve a random loopback port rather than touching installed Host/Core ports.
        with socket.socket() as reservation:
            reservation.bind(("127.0.0.1", 0))
            port = reservation.getsockname()[1]
        base = f"http://127.0.0.1:{port}/api/v1"
        environment = dict(os.environ, LAIN_HOME=str(state), LAIN_PROXY_PATH=str(root / "absent-core"),
                           LAIN_REFERENCE_QUALIFICATION="1", LAIN_REFERENCE_DISABLE_QUOTA="1",
                           LAIN_REFERENCE_HOST_PORT=str(port))

        def request(path, method="GET", value=None, authenticated=True):
            headers = {"Content-Type": "application/json"}
            if authenticated:
                headers["Authorization"] = "Bearer " + (state / "credentials/host.token").read_text().strip()
            message = urllib.request.Request(base + path, method=method, headers=headers,
                data=None if value is None else json.dumps(value).encode())
            try:
                response = opener.open(message, timeout=3)
            except urllib.error.HTTPError as error:
                response = error
            with response:
                body = response.read()
                require(secret.encode() not in body, "secret leaked in Host response")
                return response.status, json.loads(body) if body else None

        with (root / "host.log").open("w+") as log:
            process = subprocess.Popen(["dotnet", str(assembly)], env=environment, cwd=root,
                                       stdout=log, stderr=log, stdin=subprocess.DEVNULL)
            try:
                deadline = time.monotonic() + 25
                while time.monotonic() < deadline:
                    require(process.poll() is None, "Monitor exited on optional malformed Poe metadata or restored credentials")
                    try:
                        if request("/status", authenticated=False)[0] == 401:
                            break
                    except (OSError, urllib.error.URLError):
                        pass
                    time.sleep(0.05)
                else:
                    raise AssertionError("isolated Monitor never became reachable")
                code, status = request("/status")
                require(code == 200 and status["runtimeMode"] == "monitor" and not status["proxyReady"], "Monitor status incorrect")
                require(status["state"] == "monitoring", "safe restore quarantine should preserve Monitor health")
                require(not any((state / "proxy/auth-owned").iterdir()), "restored Core grants remained enumerable")
                require(not (state / "credentials/inference.token").exists(), "Monitor created an inference credential")
                code, methods = request("/connection-methods")
                require(code == 200, "connection method catalog unavailable")
                methods = methods["methods"]
                require(len({(m["provider"], m["id"]) for m in methods}) == len(methods), "duplicate method identities")
                poe = next(m for m in methods if m["provider"] == "poe" and m["id"] == "browser")
                require(poe["availability"] != "available", "malformed Poe registration advertised as available")
                codex = next(m for m in methods if m["provider"] == "codex" and m["id"] == "codex-owned")
                require(codex["availability"] != "available", "Core login advertised in Monitor")
                for method in methods:
                    if method["provider"] == "stepfun" and "/step_plan" in (method.get("defaultBaseUrl") or ""):
                        require("quota" not in method["purposes"], "Step Plan falsely borrows PAYG quota capability")
                code, _ = request("/accounts/login", "POST", {"revision": 0, "provider": "kimi", "method": "kimi-device", "purposes": ["quota"]})
                require(code == 409, "unqualified managed login did not fail before adapter effects")
                require(request("/configuration")[1]["revision"] == 0, "unavailable login mutated account configuration")
                code, _ = request("/service/start", "POST")
                require(code == 409, "Monitor accepted Core start")
                require(request("/usage")[0] == 200, "history unavailable in restored Monitor")
                require(request("/status")[1]["state"] == "monitoring", "unavailable actions damaged Monitor health")
            finally:
                process.terminate()
                try:
                    process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait(timeout=5)
                log.seek(0)
                require(secret not in log.read(), "secret leaked in Host output")
    print(json.dumps({"result": "passed", "hostAssemblySha256": hashlib.sha256(assembly.read_bytes()).hexdigest(),
                      "scope": "isolated restored Monitor, absent Core, malformed optional registration, method availability, history", "providerRequests": 0}))


if __name__ == "__main__":
    main()
