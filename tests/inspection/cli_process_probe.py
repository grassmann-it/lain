#!/usr/bin/env python3
"""Probe an already built CLI process against a fake Host in a private netns.

No builds, installed configuration, real providers or service operations. The
outer process freezes only runtime artifacts; every CLI invocation runs with a
private LAIN_HOME and loopback isolated by unshare's user/network namespaces.
"""
from __future__ import annotations

import argparse
from contextlib import suppress
from datetime import datetime
import hashlib
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import shutil
import selectors
import signal
import socket
import subprocess
import sys
import tempfile
import threading
import time
from urllib.parse import parse_qs, urlsplit

HOST_SECRET = "CLI-PROBE-SYNTHETIC-HOST-CREDENTIAL"
PRIVATE_BODY = "CLI-PROBE-PRIVATE-RESPONSE-MUST-NOT-LEAK"
PREFIX = "/api/v1"
STAMP = "2026-10-24T00:00:00Z"


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def bounded_process(command, cwd, env, timeout=12):
    """Bound both pipes while running; terminate the entire test child on excess."""
    process = subprocess.Popen(command, cwd=cwd, env=env, stdout=subprocess.PIPE,
                               stderr=subprocess.PIPE, start_new_session=True)
    buffers = {"stdout": bytearray(), "stderr": bytearray()}
    limits = {"stdout": 65536, "stderr": 8192}
    deadline = time.monotonic() + timeout
    completed = False
    try:
        with selectors.DefaultSelector() as selected:
            for name in buffers:
                stream = getattr(process, name)
                os.set_blocking(stream.fileno(), False)
                selected.register(stream, selectors.EVENT_READ, name)
            while selected.get_map() or process.poll() is None:
                remaining = deadline - time.monotonic()
                require(remaining > 0, "CLI child exceeded runtime limit")
                for key, _ in selected.select(min(0.25, remaining)):
                    chunk = os.read(key.fileobj.fileno(), 4096)
                    if not chunk:
                        selected.unregister(key.fileobj)
                        continue
                    target = buffers[key.data]
                    require(len(target) + len(chunk) <= limits[key.data], "CLI child exceeded output limit")
                    target.extend(chunk)
            code = process.wait(timeout=max(0.01, deadline - time.monotonic()))
            completed = True
        return subprocess.CompletedProcess(command, code, buffers["stdout"].decode("utf-8", errors="replace"),
                                           buffers["stderr"].decode("utf-8", errors="replace"))
    finally:
        if not completed:
            with suppress(ProcessLookupError):
                os.killpg(process.pid, signal.SIGKILL)
            if process.poll() is None:
                process.wait(timeout=3)
        process.stdout.close()
        process.stderr.close()


def fixtures():
    route = {"id": "catalog-route", "model": "vendor/model", "name": "Model",
             "baseUrl": "https://provider.invalid/v1", "protocol": "openai-chat",
             "billingPlanIds": ["trial"], "capabilities": {"tools": None, "streaming": True},
             "pricingClass": "unknown", "freeOnlyEligible": False,
             "freeOnlyEnforcementKind": "unsupported"}
    provider = {"id": "catalog-provider", "name": "Prepared provider", "summary": "Synthetic fixture",
                "accountUrl": "https://provider.invalid/account", "documentationUrl": "https://provider.invalid/docs",
                "credentials": [{"id": "apiKey", "label": "API key", "kind": "api-key", "required": True, "secret": True}],
                "setupSteps": [{"title": "Get access", "description": "Use your own account", "url": "https://provider.invalid/setup"}],
                "billingPlans": [{"id": "trial", "name": "Trial", "kind": "trial", "scope": "workspace", "unit": "credits"}],
                "routes": [route], "sources": [], "restrictions": [],
                "adapterState": "prepared", "liveVerification": "not_tested", "quotaSupport": "unsupported"}
    metric = {"samples": 2, "eligible": 4, "minimum": 1, "mean": 2, "p50": 2, "p95": 3, "maximum": 3}
    unknown = {"samples": 0, "eligible": 4, "minimum": None, "mean": None, "p50": None, "p95": None, "maximum": None}
    tokens = {"input": None, "output": 20, "cached": None, "reasoning": None, "total": None}
    totals = {"requests": 3, "attempts": 4, "errors": 1, "tokens": tokens, "unknownUsageAttempts": 2}
    performance = {"firstOutputLatencyMs": metric, "totalDurationMs": metric,
                   "outputTokensPerSecond": unknown, "rateBasis": "reported_output_tokens_per_total_duration_second"}
    return {
        "catalog": {"contractVersion": 1, "observedAt": STAMP, "providers": [provider]},
        "onboarding": {"contractVersion": 1, "providerId": provider["id"], "state": "missing_credentials",
                       "descriptor": provider, "accountIds": [], "missingCredentialFields": ["apiKey"], "liveVerification": "not_tested"},
        "analysis": {"contractVersion": 1, "from": "2026-10-23T00:00:00Z", "to": STAMP, "timeZone": "UTC",
                     "observedAt": STAMP, "cursor": 10, "complete": False, "totals": totals, "performance": performance,
                     "dimensions": [{"dimension": "modelFamily", "groups": [{"key": None, "totals": totals, "performance": performance}], "truncated": True}]},
        "snapshot": {"contractVersion": 1, "sourceId": "source-one", "observedAt": STAMP, "state": "live", "complete": False,
                     "cursor": 5, "earliestCursor": 2, "droppedEvents": 1,
                     "limits": {"maxStreams": 256, "maxEvents": 8192, "contentBytes": 33554432, "perStreamBytes": 1048576,
                                "maxEventBytes": 262144, "contentTtlSeconds": 120, "terminalTtlSeconds": 60,
                                "maxPageSize": 500, "contentPolicy": "redacted_output_only_v1"},
                     "streams": [{"id": "attempt-one", "scope": "attempt", "requestId": "request-one", "attemptId": "attempt-one",
                                  "startedAt": STAMP, "updatedAt": STAMP, "state": "streaming", "firstCursor": 2, "lastCursor": 5,
                                  "dimensions": {"conversationId": "conversation-one", "turnId": "turn-one", "workerId": "worker-one",
                                                 "parentRequestId": None, "evidence": [{"field": "workerId", "source": "harness", "key": "worker_id"}]}}]},
        "events": {"sourceId": "new-source", "events": [], "nextCursor": 0, "highWatermark": 0,
                   "earliestCursor": 1, "droppedEvents": 0, "truncated": True, "gapReason": "source_changed"},
        "cost": {"routeId": "local-route", "allowed": False, "costPolicy": "free_only", "pricingClass": "unknown", "reasons": ["upstream_cost_controls_unavailable"]},
    }


class FakeHost(ThreadingHTTPServer):
    daemon_threads = True

    def __init__(self):
        self.scenario = ""
        self.receipts = []
        self.errors = []
        self.values = fixtures()
        super().__init__(("127.0.0.1", 18418), Handler)


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_args):
        pass

    def do_GET(self):
        self.respond()

    def do_POST(self):
        self.respond()

    def bounded_body(self):
        # JsonContent from the actual HttpClient can use chunked transfer.
        if self.headers.get("Transfer-Encoding", "").lower() != "chunked":
            length = int(self.headers.get("Content-Length", "0"))
            require(0 < length <= 8192, "profile request body exceeded bounds")
            body = self.rfile.read(length)
            require(len(body) == length, "incomplete request body")
            return body
        body = bytearray()
        while True:
            line = self.rfile.readline(128)
            require(line.endswith(b"\r\n"), "invalid chunk header")
            length = int(line.split(b";", 1)[0], 16)
            require(0 <= length <= 8192 - len(body), "chunked request exceeded bounds")
            if length == 0:
                require(self.rfile.readline(128) == b"\r\n", "unexpected request trailers")
                return bytes(body)
            chunk = self.rfile.read(length)
            require(len(chunk) == length and self.rfile.read(2) == b"\r\n", "invalid chunk body")
            body.extend(chunk)

    def respond(self):
        host = self.server
        try:
            self.connection.settimeout(3)
            require(self.headers.get("Authorization") == "Bearer " + HOST_SECRET, "local management authorization missing")
            url = urlsplit(self.path)
            query = parse_qs(url.query, keep_blank_values=True)
            host.receipts.append({"method": self.command, "path": url.path, "queryKeys": sorted(query)})
            require(len(host.receipts) <= 40, "unexpected request fanout")
            scenario = host.scenario
            status = 200
            extra = {}
            if scenario in ("catalog", "onboarding", "routes"):
                endpoint = PREFIX + ("/providers/catalog-provider/onboarding" if scenario == "onboarding" else "/providers")
                require(self.command == "GET" and url.path == endpoint, "provider endpoint changed")
                body = host.values["onboarding" if scenario == "onboarding" else "catalog"]
            elif scenario == "analysis":
                require(self.command == "GET" and url.path == PREFIX + "/usage/analysis", "analysis endpoint changed")
                require(query.get("groupBy") == ["modelFamily,accountId,reasoningEffort"] and query.get("effectiveReasoningEffort") == ["medium"], "analysis dimensions were lost")
                require(len(query.get("from", [])) == 1 and len(query.get("to", [])) == 1 and
                        datetime.fromisoformat(query["from"][0].replace("Z", "+00:00")) == datetime.fromisoformat("2026-10-23T00:00:00+00:00") and
                        datetime.fromisoformat(query["to"][0].replace("Z", "+00:00")) == datetime.fromisoformat("2026-10-24T00:00:00+00:00") and
                        query.get("timeZone") == ["UTC"], "analysis window was changed")
                body = host.values["analysis"]
            elif scenario == "snapshot":
                require(self.command == "GET" and url.path == PREFIX + "/inspector" and query.get("requestId") == ["request-one"] and query.get("limit") == ["5"], "snapshot query changed")
                body = host.values["snapshot"]
            elif scenario == "events":
                require(url.path == PREFIX + "/inspector/events" and query.get("sourceId") == ["old-source"] and query.get("after") == ["5"], "inspector cursor source was lost")
                body = host.values["events"]
            elif scenario == "cost":
                require(url.path == PREFIX + "/routes/local-route/cost-policy" and query.get("costPolicy") == ["free_only"], "cost-policy inspection changed")
                body = host.values["cost"]
            elif scenario in ("policy_rejection", "revision_conflict"):
                require(self.command == "POST" and url.path == PREFIX + "/profiles/strict-free/apply", "profile error used alternate action")
                require(json.loads(self.bounded_body()) == {"revision": 7}, "profile revision was lost")
                status = 400 if scenario == "policy_rejection" else 409
                body = {"code": "validation" if status == 400 else "revision_conflict",
                        "message": "Route cost policy rejected." if status == 400 else "Configuration changed; reload and retry."}
            elif scenario in ("unauthorized", "server_unavailable"):
                require(url.path == PREFIX + "/status", "error probe changed endpoint")
                status = 401 if scenario == "unauthorized" else 503
                body = "malformed response " + PRIVATE_BODY
            elif scenario == "redirect":
                require(url.path == PREFIX + "/status", "redirect probe changed endpoint")
                status, body = 302, ""
                extra["Location"] = "http://127.0.0.1:18419/must-not-follow"
            else:
                raise AssertionError("unexpected fake-host scenario")
            encoded = (body if isinstance(body, str) else json.dumps(body)).encode()
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(encoded)))
            for key, value in extra.items():
                self.send_header(key, value)
            self.end_headers()
            self.wfile.write(encoded)
        except Exception as error:
            host.errors.append(type(error).__name__ + ": " + str(error))
            self.send_error(500, "Fake Host contract assertion failed")


def inside(args):
    work = args.inside
    report = {"kind": "compiled_cli_process_probe", "passed": False, "cases": []}
    server = None
    thread = None
    try:
        require(os.readlink("/proc/self/ns/net") != args.parent_netns, "private network namespace was not created")
        require([name for _, name in socket.if_nameindex()] == ["lo"], "namespace contains unexpected network interfaces")
        subprocess.run(["ip", "link", "set", "lo", "up"], check=True, capture_output=True, timeout=5)
        state = work / "state"
        credentials = state / "credentials"
        credentials.mkdir(parents=True, mode=0o700)
        token = credentials / "host.token"
        token.write_text(HOST_SECRET)
        token.chmod(0o600)
        env = {"PATH": os.environ.get("PATH", "/usr/bin:/bin"), "LANG": "C.UTF-8", "LC_ALL": "C.UTF-8", "TZ": "UTC",
               "LAIN_HOME": str(state), "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1"}
        if os.environ.get("DOTNET_ROOT"):
            env["DOTNET_ROOT"] = os.environ["DOTNET_ROOT"]
        server = FakeHost()
        thread = threading.Thread(target=server.serve_forever, kwargs={"poll_interval": 0.05}, daemon=True)
        thread.start()
        dotnet = shutil.which("dotnet")
        require(dotnet is not None, "dotnet runtime is unavailable")

        def run(name, argv, expected, validate=None, expected_requests=1, diagnostic=None):
            server.scenario = name
            before = len(server.receipts)
            process = bounded_process([dotnet, str(args.cli), *argv], work, env)
            require(process.returncode == expected, name + ": exit " + str(process.returncode) + ", expected " + str(expected))
            require(not any(marker in process.stdout + process.stderr for marker in (HOST_SECRET, PRIVATE_BODY)), name + ": credential or raw response leaked")
            require(len(process.stdout.encode()) <= 65536 and len(process.stderr.encode()) <= 8192, name + ": output exceeded bounds")
            require(len(server.receipts) - before == expected_requests, name + ": unexpected retry or alternate request")
            require(not server.errors, "fake Host rejected the CLI request contract")
            if expected == 0:
                require(process.stderr == "", name + ": successful JSON command wrote diagnostics")
                result = json.loads(process.stdout)
                validate(result)
            else:
                require(process.stdout == "", name + ": failed JSON command wrote stdout")
                require(diagnostic in process.stderr, name + ": expected diagnostic missing")
            report["cases"].append({"name": name, "exitCode": process.returncode,
                                    "requests": len(server.receipts) - before,
                                    "stdoutBytes": len(process.stdout.encode()), "stderrBytes": len(process.stderr.encode())})

        run("catalog", ["providers", "catalog", "--json"], 0,
            lambda value: require(value["providers"][0]["adapterState"] == "prepared" and value["providers"][0]["liveVerification"] == "not_tested", "provider state was promoted"))
        run("onboarding", ["providers", "onboarding", "catalog-provider", "--json"], 0,
            lambda value: require(value["missingCredentialFields"] == ["apiKey"] and value["descriptor"]["setupSteps"][0]["url"].endswith("/setup"), "onboarding lost setup requirements"))
        run("routes", ["providers", "routes", "--provider", "catalog-provider", "--json"], 0,
            lambda value: require(value[0]["capabilities"]["tools"] is None and not value[0]["freeOnlyEligible"], "unknown capabilities or pricing were promoted"))
        run("analysis", ["usage", "analysis", "--from", "2026-10-23T00:00:00Z", "--to", STAMP, "--timezone", "UTC",
                         "--group-by", "modelFamily,accountId,reasoningEffort", "--effective-reasoning-effort", "medium", "--json"], 0,
            lambda value: require(not value["complete"] and value["totals"]["tokens"]["total"] is None and value["dimensions"][0]["groups"][0]["key"] is None and
                                  value["performance"]["firstOutputLatencyMs"]["samples"] == 2 and value["performance"]["outputTokensPerSecond"]["p50"] is None, "analysis evidence or unknown measurements changed"))
        run("snapshot", ["inspector", "snapshot", "--request", "request-one", "--limit", "5", "--json"], 0,
            lambda value: require(value["sourceId"] == "source-one" and value["droppedEvents"] == 1 and value["streams"][0]["scope"] == "attempt" and
                                  value["streams"][0]["dimensions"]["workerId"] == "worker-one", "snapshot lost source or provenance"))
        run("events", ["inspector", "events", "--source", "old-source", "--after", "5", "--limit", "5", "--json"], 0,
            lambda value: require(value["sourceId"] == "new-source" and value["gapReason"] == "source_changed" and value["nextCursor"] == 0, "source reset was concealed"))
        run("cost", ["providers", "check-free", "local-route", "--json"], 0,
            lambda value: require(not value["allowed"] and value["costPolicy"] == "free_only", "cost-policy denial was lost"))
        for name, code in (("policy_rejection", "validation"), ("revision_conflict", "revision_conflict")):
            run(name, ["profiles", "apply", "strict-free", "--revision", "7", "--json"], 5, diagnostic=code)
        run("unauthorized", ["status", "--json"], 4, diagnostic="http_error")
        run("server_unavailable", ["status", "--json"], 3, diagnostic="http_error")
        run("redirect", ["status", "--json"], 5, diagnostic="http_error")
        run("invalid_bound", ["usage", "analysis", "--group-limit", "501", "--json"], 2,
            expected_requests=0, diagnostic="--group-limit")
        server.shutdown()
        server.server_close()
        thread.join(timeout=3)
        require(not thread.is_alive(), "fake Host did not shut down")
        run("host_unavailable", ["status", "--json"], 3, expected_requests=0, diagnostic="host is unavailable")
        report.update(passed=True, privateNetworkNamespace=True, privateLainHome=True,
                      noBuild=True, noInstalledService=True, noRealProvider=True)
    except Exception as error:
        report["failure"] = type(error).__name__ + ": " + str(error)
    finally:
        if server:
            if thread and thread.is_alive():
                server.shutdown()
            server.server_close()
        if thread:
            thread.join(timeout=3)
        args.report.write_text(json.dumps(report, indent=2) + "\n")
    return 0 if report["passed"] else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cli", type=Path, required=True, help="already-built Lain.Cli.dll")
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--inside", type=Path, help=argparse.SUPPRESS)
    parser.add_argument("--parent-netns", help=argparse.SUPPRESS)
    args = parser.parse_args()
    args.cli = args.cli.resolve()
    args.report = args.report.resolve()
    if args.inside:
        return inside(args)
    require(sys.platform == "linux", "this probe requires Linux user/network namespaces")
    require(args.cli.name == "Lain.Cli.dll" and args.cli.is_file(), "supply an already-built Lain.Cli.dll")
    require(all(shutil.which(name) for name in ("unshare", "ip", "dotnet")), "unshare, ip and dotnet are required")
    names = ["Lain.Cli.dll", "Lain.Client.dll", "Lain.Core.dll", "Lain.Cli.deps.json", "Lain.Cli.runtimeconfig.json"]
    sources = {name: args.cli.parent / name for name in names}
    before = {name: digest(path) for name, path in sources.items()}
    with tempfile.TemporaryDirectory(prefix="lain-cli-process-") as directory:
        work = Path(directory)
        runtime = work / "runtime"
        runtime.mkdir(mode=0o700)
        for name, path in sources.items():
            shutil.copyfile(path, runtime / name)
        require(before == {name: digest(path) for name, path in sources.items()}, "runtime artifacts changed during freeze")
        require(before == {name: digest(runtime / name) for name in names}, "frozen artifacts differ from source")
        internal_report = work / "result.json"
        command = ["unshare", "--user", "--map-root-user", "--net", "--", sys.executable, str(Path(__file__).resolve()),
                   "--cli", str(runtime / "Lain.Cli.dll"), "--report", str(internal_report), "--inside", str(work),
                   "--parent-netns", os.readlink("/proc/self/ns/net")]
        process = subprocess.run(command, capture_output=True, text=True, timeout=200)
        if internal_report.is_file():
            report = json.loads(internal_report.read_text())
        else:
            report = {"passed": False, "failure": "private namespace probe could not start", "namespaceExitCode": process.returncode}
        report.update(cliPath=str(args.cli), cliSha256=before["Lain.Cli.dll"], artifactSha256=before,
                      commandMode="dotnet compiled DLL", claim="synthetic CLI runtime/packaging coverage; no live provider qualification")
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report, indent=2) + "\n")
        print(json.dumps({"passed": report["passed"], "cases": len(report.get("cases", [])),
                          "cliSha256": report["cliSha256"], "report": str(args.report),
                          **({"failure": report["failure"]} if "failure" in report else {})}, indent=2))
        return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
