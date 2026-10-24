#!/usr/bin/env python3
"""Actual Host + qualified Core: restored login bootstrap, apply/drain and owner exit.

Requires the serialized runtime slot and a private loopback-only network namespace.
No OAuth callback, browser, real credential, installed service or default product
port is used. Synthetic inference starts only after explicit profile application.
"""
from __future__ import annotations

import argparse
import concurrent.futures
import ctypes
import hashlib
import http.client
import json
import os
from pathlib import Path
import shutil
import signal
import socket
import subprocess
import sys
import tempfile
import threading
import time
import uuid

sys.path.insert(0, str(Path(__file__).parent))
from process_probe import HTTP, ROOT, expect, wait_for
from fake_provider import FakeProvider, MODEL, marker
from urllib.error import HTTPError
from urllib.request import Request

OLD_ACCESS = "synthetic-restored-access-never-use"
OLD_REFRESH = "synthetic-restored-refresh-never-use"
INFERENCE = "synthetic-restored-client-token"
MANAGEMENT = "synthetic-host-core-management-token"


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def free_port():
    with socket.socket() as reservation:
        reservation.bind(("127.0.0.1", 0))
        port = reservation.getsockname()[1]
    expect(port not in (18417, 18418, 1455), "fixture allocated a product port")
    return port


class ControlledGate:
    def __init__(self):
        self.event = threading.Event()
    def set(self):
        self.event.set()
    def clear(self):
        self.event.clear()
    def wait(self, timeout=None):
        # The SDK fixture normally caps its gate at 8 seconds. This bounded
        # gate permits observing the real Host's 20-second drain deadline.
        return self.event.wait(35)


class HostRun:
    def __init__(self, root, host, core, dotnet, variant):
        self.root, self.host, self.core, self.dotnet, self.variant = root, host, core, dotnet, variant
        self.state = root / "state"
        self.process = None
        self.children = {}
        self.version = None
        self.startup_core_observed = False
        self.startup_inference_status = None
        self.provider = FakeProvider()
        self.provider.usage_gate = ControlledGate()
        self.provider.__enter__()
        self.host_port, self.core_port = free_port(), free_port()
        while self.core_port == self.host_port:
            self.core_port = free_port()
        self.base = f"http://127.0.0.1:{self.host_port}/api/v1"
        self.core_base = f"http://127.0.0.1:{self.core_port}"
        self.log = (root / "host.log").open("w+")
        self.env = {name: os.environ[name] for name in ("PATH", "DOTNET_ROOT", "DOTNET_ROOT_X64") if name in os.environ}
        self.env.update(LANG="C.UTF-8", LAIN_HOME=str(self.state), LAIN_PROXY_PATH=str(core),
                        LAIN_REFERENCE_QUALIFICATION="1", LAIN_REFERENCE_DISABLE_QUOTA="1",
                        LAIN_REFERENCE_HOST_PORT=str(self.host_port), LAIN_REFERENCE_PROXY_PORT=str(self.core_port),
                        DOTNET_CLI_TELEMETRY_OPTOUT="1")
        self.seed()

    def seed(self):
        for directory in ("credentials", "proxy/auth", "proxy/auth-owned"):
            (self.state / directory).mkdir(parents=True, mode=0o700, exist_ok=True)
        old = {"type": "codex", "account_id": "synthetic-restored-subject", "access_token": OLD_ACCESS,
               "refresh_token": OLD_REFRESH, "expired": "2020-01-01T00:00:00Z", "last_refresh": "2020-01-01T00:00:00Z"}
        self.grants = {"old.json": json.dumps(old).encode(), "ownership": b'{"old.json":true}'}
        for name, data in self.grants.items():
            (self.state / "proxy/auth-owned" / name).write_bytes(data)
        (self.state / "restore-requires-reconnect").write_text("synthetic-restore-epoch")
        (self.state / "credentials/inference.token").write_text(INFERENCE)
        (self.state / "credentials/management.token").write_text(MANAGEMENT)
        bindings = {"accounts": {"old-account": "owned/old.json"}, "routes": {}}
        config = "# unusable saved legacy configuration; repair must preserve these bytes\nport: 1\n"
        if self.variant == "valid-restored":
            config = f'host: "127.0.0.1"\nport: {self.core_port}\nauth-dir: "{self.state / "proxy/auth"}"\napi-keys: ["{INFERENCE}"]\nrequest-retry: 0\n'
            bindings["routes"]["old-alias"] = {"id": "old-route", "model": "gpt-5", "accounts": ["old-account"], "selection": "strict", "provider": "codex", "upstreamProtocol": "responses"}
        files = {
            "configuration.json": json.dumps({"Revision": 0, "AppliedRevision": 0, "Accounts": [], "Routes": [], "Profiles": [], "RuntimeMode": "gateway"}),
            "active-profile.json": json.dumps({"id": "prior-profile", "revision": 0}),
            "proxy/runtime.json": json.dumps({"valid": True, "revision": 0, "profileId": "prior-profile"}),
            "proxy/config.yaml": config, "proxy/bindings.json": json.dumps(bindings),
            "proxy/events.jsonl": "", "proxy/events.jsonl.source": str(uuid.uuid4()),
        }
        if self.variant == "interrupted":
            files["profile-application.json"] = json.dumps({"id": "interrupted", "profileId": "prior-profile", "revision": 0, "state": "recovery-required"})
        for name, content in files.items():
            (self.state / name).write_text(content)
        self.original = {name: (self.state / name).read_bytes() for name in files}

    def request(self, path, method="GET", value=None, *, core=False, inference=False, timeout=35):
        token = (self.state / "credentials/host.token").read_text().strip() if not core else (
            (self.state / "credentials/inference.token").read_text().strip() if inference else MANAGEMENT)
        request = Request((self.core_base if core else self.base) + path, method=method,
                          headers={"Authorization": "Bearer " + token, "Content-Type": "application/json"},
                          data=None if value is None else json.dumps(value).encode())
        try:
            response = HTTP.open(request, timeout=timeout)
        except HTTPError as error:
            response = error
        with response:
            raw = response.read()
            expect(OLD_ACCESS.encode() not in raw and OLD_REFRESH.encode() not in raw, "quarantined grant leaked in response")
            return response.status, json.loads(raw) if raw else None

    def core_children(self):
        if self.process is None or self.process.poll() is not None:
            return []
        pids = set()
        for item in Path(f"/proc/{self.process.pid}/task").glob("*/children"):
            try:
                pids.update(map(int, item.read_text().split()))
            except FileNotFoundError:
                pass
        found = []
        for pid in pids:
            try:
                args = Path(f"/proc/{pid}/cmdline").read_bytes().split(b"\0")
                args = [arg.decode() for arg in args if arg]
                if args and args[0] == str(self.core):
                    expect("--parent-stdin" in args, "Host did not retain Core parent pipe")
                    start = Path(f"/proc/{pid}/stat").read_text().split()[21]
                    self.children[pid] = start
                    found.append((pid, args))
            except FileNotFoundError:
                pass
        return found

    def start(self):
        self.process = subprocess.Popen([self.dotnet, str(self.host)], env=self.env, cwd=self.root,
                                        stdin=subprocess.DEVNULL, stdout=self.log, stderr=self.log)
        def ready():
            expect(self.process.poll() is None, "actual Host exited during startup")
            self.core_children()
            if self.variant == "valid-restored" and not self.startup_core_observed:
                try:
                    code, accounts = self.request("/lain/accounts", core=True, timeout=1)
                    if code == 200:
                        self.startup_core_observed = True
                        expect(accounts == [], "restored owned grant was registered before reconnect")
                        self.startup_inference_status = self.request("/v1/chat/completions", "POST", {"model": "old-alias", "messages": [{"role": "user", "content": "synthetic old runtime must not dispatch"}]}, core=True, inference=True, timeout=2)[0]
                        expect(self.startup_inference_status >= 400, "restored saved inference served before explicit Apply")
                except OSError:
                    pass
            try:
                code, status = self.request("/status", timeout=1)
                if code == 200:
                    self.version = status["version"]
                    return True
            except OSError:
                pass
            return False
        wait_for(ready, "actual restored Host did not become reachable", 45)

    def assert_original(self, *, desired=True):
        for name, content in self.original.items():
            if not desired and name == "configuration.json":
                continue
            expect((self.state / name).read_bytes() == content, "bootstrap changed saved active file: " + name)

    def assert_quarantine(self):
        expect(not list((self.state / "proxy/auth-owned").iterdir()), "old grant remained in the enumerable owned directory")
        epoch = hashlib.sha256(b"synthetic-restore-epoch").hexdigest().upper()
        directory = self.state / "credentials/quarantine" / epoch
        expect((directory / "prepared.json").is_file(), "quarantine publication missing")
        for name, original in self.grants.items():
            expect((directory / "auth-owned" / name).read_bytes() == original, "quarantined grant/ownership changed")

    def bootstrap(self):
        self.start()
        self.assert_original(); self.assert_quarantine()
        expect(not self.request("/status")[1]["proxyReady"], "restored Gateway falsely ready")
        code, login = self.request("/accounts/login", "POST", {"revision": 0, "provider": "codex", "method": "codex-owned", "purposes": ["inference", "quota"]})
        expect(code == 200 and login["state"] == "waiting", f"actual Core login could not begin: {code} {login}")
        expect(self.request("/accounts/login/" + login["id"])[1]["state"] == "waiting", "actual Core login polling failed")
        status = self.request("/status")[1]
        expect(status["state"] == "login-only" and not status["proxyReady"], "management bootstrap claims inference readiness")
        children = self.core_children()
        expect(len(children) == 1, "Host did not own exactly one bootstrap Core")
        pid, args = children[0]
        option = lambda name: Path(args[args.index(name) + 1])
        expect(option("--config").is_relative_to(self.state / "proxy/login-bootstrap"), "bootstrap used saved active configuration")
        expect(option("--bindings").is_relative_to(self.state / "proxy/login-bootstrap"), "bootstrap used saved route bindings")
        expect(option("--journal") != self.state / "proxy/events.jsonl", "bootstrap used active inference journal")
        expect(option("--owned-auth-dir") == self.state / "proxy/auth-owned", "bootstrap enumerated quarantine instead of fresh grants")
        binding = json.loads(option("--bindings").read_text())
        expect(not binding["accounts"] and not binding["routes"], "bootstrap has inference bindings")
        expect(self.request("/lain/accounts", core=True)[1] == [], "bootstrap loaded an old grant")
        expect(self.request("/lain/events?after=0&limit=1000", core=True)[1]["events"] == [], "bootstrap created inference history")
        self.assert_original(); self.assert_quarantine()
        code, cancelled = self.request("/accounts/login/" + login["id"], "DELETE")
        expect(code == 200 and cancelled["state"] == "cancelled", "actual Core login cancellation failed")
        expect(self.request("/service/start", "POST")[0] >= 400, "service start bypassed management-only bootstrap")
        self.assert_original(); self.assert_quarantine()
        expect(not self.provider.snapshot(), "bootstrap/cancel caused synthetic provider traffic")
        return {"bootstrapPid": pid, "separateEmptyRuntime": True, "realLoginBeginPollCancel": True,
                "activeFilesByteIdentical": sorted(self.original), "quarantinedOldGrantUnchanged": True,
                "providerRequestsBeforeApply": 0, "savedRuntimeRegisteredNoOldGrant": self.startup_core_observed,
                "savedInferenceStatus": self.startup_inference_status}

    def configure_and_apply(self):
        code, config = self.request("/accounts", "POST", {"revision": 0, "value": {"name": "Synthetic actual Host/Core", "provider": "fixture", "apiKey": "lain-synthetic-a", "baseUrl": self.provider.url}})
        expect(code == 200, f"synthetic key enrollment failed: {code} {config}")
        account = config["accounts"][0]["id"]
        self.route = {"id": "fixture-route", "alias": "host-core-fixture", "upstreamModel": MODEL, "provider": "fixture", "accountIds": [account], "pooled": False, "visible": True, "enabled": True}
        code, config = self.request("/routes/fixture-route", "PUT", {"revision": config["revision"], "value": self.route})
        expect(code == 200, "synthetic route save failed")
        profile = {"id": "fixture-profile", "name": "Synthetic profile", "routeIds": ["fixture-route"], "defaultRouteId": "fixture-route"}
        code, config = self.request("/profiles/fixture-profile", "PUT", {"revision": config["revision"], "value": profile})
        expect(code == 200, "synthetic profile save failed")
        self.assert_original(desired=False)
        expect(not self.request("/status")[1]["proxyReady"] and not self.provider.snapshot(), "saving activated inference")
        old_pid = self.core_children()[0][0]
        code, config = self.apply(config["revision"])
        expect(code == 200 and config["appliedRevision"] == config["revision"], f"explicit Apply failed: {code} {config}")
        status = self.request("/status")[1]
        expect(status["proxyReady"] and status["activeProfileId"] == "fixture-profile" and status["application"]["state"] == "completed", "Apply did not publish complete active identity")
        children = self.core_children()
        expect(len(children) == 1 and children[0][0] != old_pid, "Apply did not replace bootstrap process")
        accounts = self.request("/lain/accounts", core=True)[1]
        expect(len(accounts) == 1 and accounts[0]["credentialId"] != "owned/old.json", "Apply registered a quarantined grant")
        expect((self.state / "proxy/events.jsonl").read_bytes() == b"", "Apply itself dispatched inference")
        expect(not self.provider.snapshot(), "Apply itself contacted synthetic provider")
        self.revision = config["revision"]
        self.assert_quarantine()
        return {"savedChangesDidNotActivate": True, "explicitApplyPublished": True, "bootstrapReplaced": True,
                "activeRevision": self.revision, "quarantinedGrantNotRegistered": True}

    def apply(self, revision):
        return self.request("/profiles/fixture-profile/apply", "POST", {"revision": revision}, timeout=40)

    def stream(self):
        self.provider.usage_gate.clear()
        connection = http.client.HTTPConnection("127.0.0.1", self.core_port, timeout=40)
        token = (self.state / "credentials/inference.token").read_text().strip()
        body = {"model": self.route["alias"], "stream": True, "messages": [{"role": "user", "content": marker("gated_usage", "actual-host-drain")} ]}
        connection.request("POST", "/v1/chat/completions", json.dumps(body), {"Authorization": "Bearer " + token, "Content-Type": "application/json"})
        response = connection.getresponse()
        expect(response.status == 200 and b"data:" in response.readline(), "actual Host-bound SSE did not start")
        return connection, response

    def drain(self, *, timeout):
        connection, response = self.stream()
        old_pid = self.core_children()[0][0]
        current = self.request("/configuration")[1]
        next_route = {**self.route, "alias": "host-core-timeout-retry" if timeout else "host-core-next"}
        code, saved = self.request("/routes/fixture-route", "PUT", {"revision": current["revision"], "value": next_route})
        expect(code == 200, "pending route save failed")
        protected = {name: (self.state / name).read_bytes() for name in ("active-profile.json", "applied-profile-snapshot.json", "profile-application.json", "proxy/runtime.json", "proxy/config.yaml", "proxy/bindings.json")}
        with concurrent.futures.ThreadPoolExecutor(max_workers=1) as pool:
            future = pool.submit(self.apply, saved["revision"])
            wait_for(lambda: self.request("/lain/activity", core=True)[1] == {"draining": True, "activeRequests": 1}, "Host did not close admission around active SSE", 8)
            expect(not future.done(), "Apply completed while SSE remained active")
            expect(self.request("/v1/chat/completions", "POST", {"model": self.route["alias"]}, core=True, inference=True)[0] == 503, "new request crossed Host drain")
            for name, data in protected.items():
                if name != "profile-application.json":
                    expect((self.state / name).read_bytes() == data, "Apply replaced active proof before stream drain: " + name)
            if timeout:
                code, result = future.result(timeout=30)
                expect(code >= 400, "deadline did not refuse Apply")
                expect(self.core_children()[0][0] == old_pid, "deadline killed the old Core")
                expect(self.request("/lain/activity", core=True)[1] == {"draining": False, "activeRequests": 1}, "deadline did not confirm admission reopening")
                for name, data in protected.items():
                    expect((self.state / name).read_bytes() == data, "deadline changed prior runtime/proof: " + name)
                expect(self.request("/configuration")[1]["appliedRevision"] == current["appliedRevision"], "deadline advanced applied revision")
            self.provider.usage_gate.set()
            expect(b"[DONE]" in response.read(), "Host Apply damaged admitted stream tail")
            response.close(); connection.close()
            if not timeout:
                code, result = future.result(timeout=30)
                expect(code == 200 and result["appliedRevision"] == saved["revision"], "drained Apply failed")
        if timeout:
            refused_status = code
            code, result = self.apply(saved["revision"])
            expect(code == 200, "explicit retry after drain timeout failed")
        else:
            refused_status = None
        self.route, self.revision = next_route, saved["revision"]
        expect(self.core_children()[0][0] != old_pid, "completed Apply did not replace previous Core")
        expect(self.request("/v1/chat/completions", "POST", {"model": self.route["alias"], "messages": [{"role": "user", "content": marker("text", "after-explicit-apply")}]}, core=True, inference=True)[0] == 200, "new applied alias unusable")
        return {"actualHostDrain": True, "admittedSSETailPreserved": True, "newRequestsRejected": True,
                "timeoutPreservedRuntimeAndProof": timeout, "refusedApplyStatus": refused_status, "explicitRetryOrApplySucceeded": True}

    def shutdown(self, *, abrupt):
        children = self.core_children()
        expect(len(children) == 1, "shutdown did not have exactly one owned Core")
        pid = children[0][0]
        protected = {name: (self.state / name).read_bytes() for name in (
            "active-profile.json", "applied-profile-snapshot.json", "profile-application.json",
            "proxy/runtime.json", "proxy/config.yaml", "proxy/bindings.json",
            "proxy/events.jsonl", "proxy/events.jsonl.source")}
        if abrupt:
            self.process.kill()
        else:
            self.process.terminate()
        self.process.wait(timeout=12)
        expect(self.process.returncode == (-signal.SIGKILL if abrupt else 0), "unexpected Host shutdown status")
        child_status = None
        def stopped():
            nonlocal child_status
            try:
                got, value = os.waitpid(pid, os.WNOHANG)
                if got:
                    child_status = os.waitstatus_to_exitcode(value)
                    return True
            except ChildProcessError:
                pass
            return not Path(f"/proc/{pid}").exists()
        wait_for(stopped, "actual Host left an orphan Core", 10)
        with socket.socket() as check:
            expect(check.connect_ex(("127.0.0.1", self.core_port)) != 0, "Core listener survived Host exit")
        for name, data in protected.items():
            expect((self.state / name).read_bytes() == data, "Host shutdown changed journal/configuration/proof: " + name)
        return {"hostKilledAbruptly": abrupt, "hostExitCode": self.process.returncode,
                "noOrphanCore": True, "listenerReleased": True, "reparentedCoreExitCode": child_status,
                "reparentedCoreSignal": signal.Signals(-child_status).name if child_status is not None and child_status < 0 else None,
                "journalConfigurationProofUnchanged": True}

    def close(self):
        self.provider.usage_gate.set()
        if self.process is not None and self.process.poll() is None:
            self.process.terminate()
            try:
                self.process.wait(timeout=12)
            except subprocess.TimeoutExpired:
                self.process.kill(); self.process.wait(timeout=5)
        # Cleanup only exact children previously observed under this Host, with
        # matching PID start time and executable identity; never a port owner.
        for pid, start in self.children.items():
            try:
                if Path(f"/proc/{pid}/stat").read_text().split()[21] == start and Path(f"/proc/{pid}/exe").resolve() == self.core:
                    os.kill(pid, 9)
                    try: os.waitpid(pid, 0)
                    except ChildProcessError: pass
            except (FileNotFoundError, ProcessLookupError):
                pass
        self.provider.__exit__()
        self.log.flush(); self.log.seek(0)
        expect(OLD_ACCESS not in self.log.read(), "old access grant leaked into Host log")
        self.log.seek(0)
        expect(OLD_REFRESH not in self.log.read(), "old refresh grant leaked into Host log")
        self.log.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", required=True, type=Path)
    parser.add_argument("--host-source-commit", required=True)
    parser.add_argument("--core", required=True, type=Path)
    parser.add_argument("--dotnet", default=shutil.which("dotnet"))
    parser.add_argument("--report", required=True, type=Path)
    parser.add_argument("--case", action="append", choices=("legacy-repair", "interrupted", "valid-restored"))
    args = parser.parse_args()
    expect([name for _, name in socket.if_nameindex()] == ["lo"], "private loopback-only network namespace required")
    os.umask(0o077)
    host, original_core = args.host.resolve(strict=True), args.core.resolve(strict=True)
    expect(args.dotnet is not None, "explicit dotnet executable required")
    qualification = json.loads((ROOT / "proxy/qualification.json").read_text())
    expect(digest(original_core) == qualification["sha256"] and qualification["passed"], "Core is not the retained qualified adapter3")
    host_files = [host, host.with_suffix(".deps.json"), host.with_suffix(".runtimeconfig.json"), host.parent / "Lain.Core.dll"]
    hashes = {path.name: digest(path) for path in host_files if path.exists()}
    libc = ctypes.CDLL(None, use_errno=True)
    expect(libc.prctl(36, 1, 0, 0, 0) == 0, "Linux fixture subreaper unavailable")
    checks = []
    try:
        with tempfile.TemporaryDirectory(prefix="lain-actual-host-core-") as temporary:
            root = Path(temporary)
            artifact = root / "artifact"; artifact.mkdir(mode=0o700)
            core = artifact / "lain-proxy"; shutil.copyfile(original_core, core); core.chmod(0o700)
            (artifact / "binary-manifest.json").write_text(json.dumps({"sha256": qualification["sha256"]}))
            (artifact / "qualification.json").write_text(json.dumps(qualification))
            for variant in args.case or ("legacy-repair", "interrupted", "valid-restored"):
                case_root = root / variant; case_root.mkdir(mode=0o700)
                run = HostRun(case_root, host, core, args.dotnet, variant)
                try:
                    evidence = {"bootstrap": run.bootstrap(), "apply": run.configure_and_apply()}
                    if variant == "interrupted":
                        evidence["drainSuccess"] = run.drain(timeout=False)
                        evidence["drainTimeout"] = run.drain(timeout=True)
                    evidence["shutdown"] = run.shutdown(abrupt=variant == "valid-restored")
                    evidence["hostVersion"] = run.version
                    evidence["syntheticInferenceRequestsAfterApply"] = len(run.provider.snapshot())
                    checks.append({"name": variant, "passed": True, "evidence": evidence})
                except Exception as error:
                    checks.append({"name": variant, "passed": False, "error": type(error).__name__, "detail": str(error)})
                finally:
                    run.close()
                print(json.dumps(checks[-1]), flush=True)
    finally:
        libc.prctl(36, 0, 0, 0, 0)
    unchanged = hashes == {path.name: digest(path) for path in host_files if path.exists()}
    report = {"schemaVersion": 1, "actualHostAndCoreProcesses": True, "syntheticOnly": True,
              "networkIsolation": "private_network_namespace_loopback_only", "hostSourceCommit": args.host_source_commit,
              "hostInputSha256": hashes, "hostInputsUnchanged": unchanged, "coreSha256": digest(original_core),
              "coreSourceManifestSha256": qualification["sourceManifestSha256"], "checks": checks,
              "selectedCases": args.case or ["legacy-repair", "interrupted", "valid-restored"],
              "passed": unchanged and len(checks) == len(args.case or [0, 1, 2]) and all(check["passed"] for check in checks),
              "notQualified": ["real OAuth callback/token exchange", "live-provider credentials", "installed services", "full Host backup/restore transaction"]}
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + "\n")
    args.report.with_suffix(args.report.suffix + ".sha256").write_text(digest(args.report) + "\n")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
