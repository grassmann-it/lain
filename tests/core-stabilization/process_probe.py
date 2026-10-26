#!/usr/bin/env python3
"""Actual private Core processes against bounded loopback-only synthetic fixtures.

Never discovers an installed service, reads provider credentials, or uses fixed
product ports. Builds are a separate, integrator-coordinated step.
"""
from __future__ import annotations

import argparse
import ctypes
import hashlib
import http.client
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import time
from urllib.error import HTTPError
from urllib.request import HTTPRedirectHandler, ProxyHandler, Request, build_opener

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tests/backend"))
from qualify import CLIENT_KEY, MANAGEMENT_KEY, Run, assert_complete, expect
from fake_provider import MODEL, marker


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, *_args, **_kwargs):
        return None


HTTP = build_opener(ProxyHandler({}), NoRedirect())


def wait_for(test, message, timeout=8):
    end = time.monotonic() + timeout
    while time.monotonic() < end:
        if test():
            return
        time.sleep(.025)
    raise AssertionError(message)


class PrivateRun(Run):
    def __init__(self, core, *, parent_stdin=False):
        super().__init__(core)
        expect(self.port not in (18417, 18418), "fixture allocated a product port")
        expect(self.provider.server.server_port not in (18417, 18418), "provider allocated a product port")
        self.parent_stdin = parent_stdin

    def command(self):
        return super().command() + (["--parent-stdin"] if self.parent_stdin else [])

    def ready(self):
        def check():
            if self.process is not None and self.process.poll() is not None:
                raise RuntimeError("private Core exited during startup: " + (self.root / "core.log").read_text()[-1800:])
            try:
                health = self.get("/lain/health")
                return health["ready"] and self.events()["sourceId"] == Path(str(self.journal) + ".source").read_text()
            except (OSError, ValueError):
                return False
        wait_for(check, "private Core did not become ready", 20)

    def start(self, allow_unready=False):
        expect(not allow_unready, "this qualification requires ready bindings")
        self.process = subprocess.Popen(self.command(), cwd=self.root, stdout=self.log, stderr=self.log,
                                        stdin=subprocess.PIPE if self.parent_stdin else subprocess.DEVNULL,
                                        env=self.env)
        self.ready()

    def get(self, path):
        with HTTP.open(Request(self.url + path, headers={"Authorization": "Bearer " + MANAGEMENT_KEY}), timeout=5) as response:
            return json.load(response)

    def call(self, method, path, body=None, *, token=MANAGEMENT_KEY):
        request = Request(self.url + path, None if body is None else json.dumps(body).encode(),
                          {"Authorization": "Bearer " + token, "Content-Type": "application/json"}, method=method)
        try:
            response = HTTP.open(request, timeout=10)
        except HTTPError as error:
            response = error
        with response:
            data = response.read()
            return response.status, json.loads(data) if data else None, dict(response.headers)

    def infer(self, alias, fields=None, *, case="text", protocol="responses"):
        body = {"model": alias, "input": marker(case, "stabilization-" + alias), **(fields or {})}
        if protocol == "chat":
            body["messages"] = [{"role": "user", "content": body.pop("input")}]
        return self.call("POST", "/v1/responses" if protocol == "responses" else "/v1/chat/completions", body, token=CLIENT_KEY)


def drain_probe(core):
    with PrivateRun(core) as run:
        connection = http.client.HTTPConnection("127.0.0.1", run.port, timeout=10)
        body = {"model": "fixture-a", "stream": True, "messages": [{"role": "user", "content": marker("gated_usage", "drain-tail")} ]}
        connection.request("POST", "/v1/chat/completions", json.dumps(body), {"Authorization": "Bearer " + CLIENT_KEY, "Content-Type": "application/json"})
        response = connection.getresponse()
        expect(response.status == 200 and b"data:" in response.readline(), "SSE did not reach its live boundary")
        expect(run.get("/lain/activity") == {"draining": False, "activeRequests": 1}, "active HTTP handler missing")
        status, activity, _ = run.call("POST", "/lain/drain")
        expect(status == 200 and activity == {"draining": True, "activeRequests": 1}, "drain lost active SSE")
        expect(run.call("GET", "/lain/activity", token=CLIENT_KEY)[0] == 401, "activity accepted inference token")
        expect(run.call("DELETE", "/lain/drain", token=CLIENT_KEY)[0] == 401, "drain accepted inference token")
        for path in ("/v1/responses", "/v1/responses/compact", "/v1/messages", "/v1/chat/completions"):
            status, payload, headers = run.call("POST", path, {"model": "fixture-a"}, token=CLIENT_KEY)
            expect(status == 503 and payload["error"]["code"] == "runtime_draining" and headers.get("Retry-After") == "1", "new inference crossed drain")
        expect(len(run.provider.snapshot()) == 1, "drained requests reached upstream")
        expect(run.call("GET", "/v1/models", token=CLIENT_KEY)[0] == 200, "drain hid model listing")
        expect(run.events()["events"][0]["kind"] == "attempt_started", "drain hid durable replay")
        # Simulate a Host's bounded wait expiring: reopening must preserve the
        # existing stream and allow another request on the same live process.
        status, activity, _ = run.call("DELETE", "/lain/drain")
        expect(status == 200 and activity == {"draining": False, "activeRequests": 1}, "timeout-style reopen lost old handler")
        expect(run.infer("fixture-b", protocol="chat")[0] == 200, "old runtime was unusable after reopening")
        expect(run.get("/lain/activity")["activeRequests"] == 1, "unfinished SSE disappeared from count")
        run.call("POST", "/lain/drain")
        run.provider.usage_gate.set()
        expect(b"[DONE]" in response.read(), "drain damaged the admitted SSE tail")
        response.close(); connection.close()
        wait_for(lambda: run.get("/lain/activity") == {"draining": True, "activeRequests": 0}, "handler did not leave activity after tail")
        assert_complete(run.events()["events"], 2)
        run.call("DELETE", "/lain/drain")
        expect(run.infer("fixture-a", protocol="chat")[0] == 200, "drained runtime did not resume")
        return {"actualSSEHandlerTracked": True, "blockedInferencePaths": 4, "timeoutReopenPreservedOldRuntime": True,
                "admittedStreamTailDelivered": True, "managementUnaffected": True, "completedRequests": 3}


def compatibility_run(core, *, override=None, legacy_default=False):
    run = PrivateRun(core)
    cases = {"mimo-pro": ("xiaomi-mimo", "mimo-v2.6-pro"), "mimo-flash": ("xiaomi-mimo", "mimo-v2.6-flash"),
             "glyph": ("commandcode", "stealth/glyph-cluster:free"), "beam": ("commandcode", "reflection/beam:free"),
             "unknown": ("unrelated", "stealth/glyph-cluster:free")}
    run.bindings["routes"] = {alias: {"id": "route-" + alias, "provider": provider, "upstreamProtocol": "openai",
        "model": model, "accounts": ["account-a"], "selection": "strict"} for alias, (provider, model) in cases.items()}
    run.write_bindings()
    config = run.root / "core.yaml"
    text = config.read_text()
    if not legacy_default:
        text = text.replace("  - name: fixture\n", "  - name: fixture\n    upstream-protocol: openai-chat\n")
    text = text[:text.index("    models:\n")] + "    models:\n"
    for model in sorted({model for _, model in cases.values()}):
        text += f"      - name: {json.dumps(model)}\n        alias: {json.dumps(model)}\n"
    if override:
        alias, tools = override
        raw = json.dumps(tools, separators=(",", ":")).replace("'", "''")
        text += f"payload:\n  override-raw:\n    - models:\n        - name: {json.dumps(alias)}\n          protocol: openai\n      params:\n        tools: '{raw}'\n"
    config.write_text(text)
    return run


def failed_attempt(run, after, category):
    rows = run.events(after)["events"]
    starts, usages = assert_complete(rows, 1)
    expect(len(starts) == 1 and starts[0]["accountId"] == "account-a", "preflight lost account attribution")
    failed = usages[0]
    expect(failed["outcome"] == "failed" and failed.get("errorCategory") == category, "explicit dispatch failure lost durable category")
    expect(not failed["usageReported"] and all(failed.get(name) is None for name in ("inputTokens", "outputTokens", "totalTokens")), "failed preflight invented usage")
    expect("errorCategory" not in starts[0] and all("errorCategory" not in row for row in rows if row["kind"] == "attempt_finished"), "failure category leaked into non-failure facts")
    return failed


def compatibility_probe(core):
    rejected = []
    with compatibility_run(core) as run:
        for alias, tool, code in [("mimo-pro", {"type": "web_search"}, "native_search_unsupported"),
                                 ("mimo-flash", {"type": "web_search_preview"}, "native_search_unsupported"),
                                 ("glyph", {"type": "namespace", "name": "agents", "tools": [{"type": "function", "name": "spawn"}]}, "namespace_tools_unsupported"),
                                 ("glyph", {"type": "custom", "name": "apply_patch"}, "custom_tools_unsupported")]:
            after = run.events()["highWatermark"]
            status, body, _ = run.infer(alias, {"tools": [tool]})
            expect(status == 400 and code in json.dumps(body), "original controls escaped compatibility preflight")
            failed_attempt(run, after, "unsupported_tool")
            rejected.append(code)
        expect(not run.provider.snapshot(), "original rejected tool dispatched upstream")
        function = {"type": "function", "name": "web_search", "parameters": {"type": "object"}}
        expect(run.infer("mimo-pro", {"tools": [function]})[0] == 200, "function named web_search was treated as native")
        expect(run.provider.snapshot()[-1]["functionToolNames"] == ["web_search"], "ordinary function was silently removed")
        for count in (128, 129):
            after = run.events()["highWatermark"]
            tools = [{"type": "function", "name": f"fixture_{n}", "parameters": {"type": "object"}} for n in range(count)]
            status, body, _ = run.infer("beam", {"tools": tools})
            if count == 128:
                expect(status == 200 and len(run.provider.snapshot()[-1]["functionToolNames"]) == 128, "128 actual top-level tools did not arrive intact")
            else:
                expect(400 <= status <= 599 and "tool_limit_exceeded" in json.dumps(body), "129 final tools reached provider")
                failed_attempt(run, after, "tool_limit")
        expect(len(run.provider.snapshot()) == 2, "unexpected tool-limit upstream attempts")
        after = run.events()["highWatermark"]
        expect(run.infer("glyph", case="rate_limit")[0] == 429, "synthetic429 missing")
        rows = run.events(after)["events"]
        _, usages = assert_complete(rows, 1)
        expect(usages[0]["statusCode"] == 429 and all("errorCategory" not in row for row in rows), "429 was classified without explicit evidence")
    overrides = []
    for alias, tools, category in [("glyph", [{"type": "custom", "name": "injected"}], "unsupported_tool"),
                                    ("beam", [{"type": "function", "function": {"name": f"injected_{n}"}} for n in range(129)], "tool_limit")]:
        with compatibility_run(core, override=(alias, tools)) as run:
            status, body, _ = run.infer(alias)
            expect(400 <= status <= 599, "final override was accepted")
            failed_attempt(run, 0, category)
            expect(not run.provider.snapshot(), "configuration override reached provider transport")
            overrides.append({"route": alias, "status": status, "category": category})
    with compatibility_run(core, legacy_default=True) as run:
        status, _, _ = run.infer("mimo-pro", {"tools": [{"type": "web_search"}]})
        expect(status == 400 and not run.provider.snapshot(), "legacy synthesized Chat default escaped restriction")
        failed_attempt(run, 0, "unsupported_tool")
    return {"originalRejections": rejected, "finalOverrideRejections": overrides, "beam128ArrivedIntact": True,
            "beam129Blocked": True, "functionNamePreserved": True, "durableAttribution": True, "http429Unclassified": True,
            "legacyChatDefaultChecked": True}


def native_protocol_probe(core):
    evidence = []
    for native, protocol, provider, model, expected_path in [
        ("codex", "responses", "xiaomi-mimo", "mimo-v2.6-pro", "/v1/responses"),
        ("claude", "anthropic", "commandcode", "stealth/glyph-cluster:free", "/v1/messages"),
        ("gemini", "gemini", "commandcode", "reflection/beam:free", "/v1beta/models/reflection/beam:free:generateContent"),
    ]:
        run = PrivateRun(core)
        try:
            base = run.provider.url if native == "codex" else run.provider.url.removesuffix("/v1")
            kind = native + ":apikey"
            key = "lain-synthetic-a"
            credential = kind + ":" + hashlib.sha256("\0".join([kind, key, base, "", "", ""]).encode()).hexdigest()[:12]
            run.bindings["accounts"] = {"native-account": credential}
            run.bindings["routes"] = {"native-route": {"id": "native-route", "provider": provider, "upstreamProtocol": protocol,
                "model": model, "accounts": ["native-account"], "selection": "strict"}}
            run.write_bindings()
            config = run.root / "core.yaml"
            config.write_text(config.read_text() + f"{native}-api-key:\n  - api-key: {key}\n    base-url: {json.dumps(base)}\n    models:\n      - name: {json.dumps(model)}\n        alias: {json.dumps(model)}\n")
            run.start()
            fields = {"tools": [{"type": "web_search"}]} if native == "codex" else {}
            status, body, _ = run.infer("native-route", fields)
            expect(status == 200, f"native {protocol} inherited Chat guard: {status} {body}")
            rows = run.provider.snapshot()
            expect(len(rows) == 1 and rows[0]["path"] == expected_path, "native executor did not use its declared endpoint")
            starts, usages = assert_complete(run.events()["events"], 1)
            expect(starts[0]["accountId"] == "native-account" and all("errorCategory" not in row for row in run.events()["events"]), "native attribution/category changed")
            evidence.append({"protocol": protocol, "nativeEndpoint": expected_path, "chatRestrictionInherited": False})
        finally:
            run.__exit__()
    return {"cases": evidence, "scope": "actual executor/protocol binding; does not certify arbitrary native provider tool capabilities"}


def parent_pipe_probe(core):
    with PrivateRun(core, parent_stdin=True) as run:
        manifest = json.loads((ROOT / "proxy/source-manifest.json").read_text())
        expected = f"{manifest['snapshotVersion']}-lain-adapter.{manifest['lainPatchVersion']}"
        expect(run.get("/lain/health")["version"] == expected, "stale adapter identity")
        start = time.monotonic()
        run.process.stdin.close()
        run.process.wait(timeout=10)
        expect(run.process.returncode == 0, "parent EOF did not stop cleanly")
        with socket.socket() as probe:
            expect(probe.connect_ex(("127.0.0.1", run.port)) != 0, "Core still listens after parent EOF")
        return {"actualCoreExitedOnEOF": True, "exitCode": 0, "elapsedMilliseconds": round((time.monotonic() - start) * 1000, 3), "listenerReleased": True}


def parent_death_probe(core):
    # Make this *test process* reap its own grandchild after the synthetic owner
    # dies. This is not the production prctl/thread-lifetime implementation.
    libc = ctypes.CDLL(None, use_errno=True)
    expect(libc.prctl(36, 1, 0, 0, 0) == 0, "Linux test subreaper unavailable")
    run = PrivateRun(core, parent_stdin=True)
    helper = None
    child = None
    reaped = False
    try:
        code = "import json,subprocess,sys; p=subprocess.Popen(json.loads(sys.argv[1]),stdin=subprocess.PIPE,stdout=sys.stderr,stderr=sys.stderr); print(p.pid,flush=True); sys.stdin.buffer.read()"
        helper = subprocess.Popen([sys.executable, "-c", code, json.dumps(run.command())], cwd=run.root,
                                  stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=run.log, text=True, env=run.env)
        child = int(helper.stdout.readline().strip())
        run.ready()
        expect(run.infer("fixture-a", protocol="chat")[0] == 200, "owned grandchild failed positive control")
        helper.kill(); helper.wait(timeout=5)
        status = None
        def exited():
            nonlocal status, reaped
            pid, value = os.waitpid(child, os.WNOHANG)
            if pid == child:
                status, reaped = value, True
            return reaped
        wait_for(exited, "Core survived abrupt owner death", 10)
        expect(os.waitstatus_to_exitcode(status) == 0, "orphan shutdown was not clean")
        with socket.socket() as probe:
            expect(probe.connect_ex(("127.0.0.1", run.port)) != 0, "orphan Core retained listener")
        return {"syntheticOwningProcessKilled": True, "coreAutomaticallyExited": True, "coreReaped": True, "listenerReleased": True}
    finally:
        if helper is not None and helper.poll() is None:
            helper.kill(); helper.wait(timeout=5)
        if child is not None and not reaped:
            try:
                os.kill(child, 9); os.waitpid(child, 0)
            except ProcessLookupError:
                pass
        run.__exit__()
        libc.prctl(36, 0, 0, 0, 0)


def journal_probe(core):
    with PrivateRun(core) as run:
        expect(run.infer("fixture-a", protocol="chat")[0] == 200, "journal seed request failed")
        seed = run.events()["events"]
        assert_complete(seed, 1)
        source = run.events()["sourceId"]
        # Maintenance must refuse the live inode even when all requests ended.
        locked = subprocess.run([core, "journal-inspect", "--journal", str(run.journal)], capture_output=True, env=run.env, timeout=5)
        expect(locked.returncode != 0, "offline inspection bypassed live writer lock")
        run.stop()
        legacy = []
        for request in range(200):
            for template in seed:
                event = dict(template)
                event.update(requestId=f"legacy-request-{request}", attemptId=f"legacy-attempt-{request}", sequence=len(legacy) + 1)
                event["eventId"] = f"{source}:{event['sequence']}"
                expect("errorCategory" not in event, "legacy seed unexpectedly depends on new field")
                legacy.append(event)
        original = b"".join(json.dumps(event, separators=(",", ":")).encode() + b"\n" for event in legacy)
        with run.journal.open("wb") as output:
            output.write(original); output.flush(); os.fsync(output.fileno())
        run.start()
        storage = run.get("/lain/journal/storage")
        expect(storage["events"] == len(legacy) and storage["indexEntries"] == 3 and storage["indexStride"] == 256, "sparse index did not open legacy-format history")
        for cursor in (0, 254, 255, 256, 511, len(legacy) - 1, len(legacy)):
            page = run.get(f"/lain/events?after={cursor}&limit=7")
            expect(page["events"] == legacy[cursor:cursor + 7] and page["sourceId"] == source, "legacy replay changed across checkpoint")
        expect(run.journal.read_bytes() == original, "legacy history was rewritten")
        expect(run.infer("fixture-a", protocol="chat")[0] == 200, "append after legacy replay failed")
        before = run.events()
        run.stop()
        complete = run.journal.read_bytes()
        expect(complete.startswith(original), "append changed legacy bytes")
        torn = complete + b'{"schemaVersion":1,"sequence":'
        run.journal.write_bytes(torn)
        bad = subprocess.run(run.command(), cwd=run.root, capture_output=True, env=run.env, timeout=8)
        expect(bad.returncode != 0 and b"journal incomplete" in bad.stderr, "torn journal silently opened")
        destination = run.root / "recovered-copy"
        result = subprocess.run([core, "journal-recover-copy", "--journal", str(run.journal), "--destination", str(destination)],
                                capture_output=True, env=run.env, timeout=8, check=True)
        report = json.loads(result.stdout)
        expect(report["sourceId"] == source and report["verifiedPrefixBytes"] == len(complete) and report["uncertainTailBytes"] == len(torn) - len(complete), "copy report lost tail/source evidence")
        expect(run.journal.read_bytes() == torn, "recovery altered corrupt original")
        run.journal = destination / "events.jsonl"
        run.start()
        expect(run.events() == before, "recovered copy changed source/events/cursors")
        expect(run.infer("fixture-a", protocol="chat")[0] == 200, "recovered private copy could not continue")
        expect(run.events()["highWatermark"] == before["highWatermark"] + 3, "recovery reset or skipped sequence")
        return {"legacyFormatEvents": len(legacy), "checkpointBoundariesReplayed": 7, "originalBytesRetained": True,
                "sameSourceAndCursorAfterCopyAdoption": True, "tornTailRefusedAtStartup": True, "uncertainTailBytes": report["uncertainTailBytes"],
                "recoveredCopyContinued": True, "oldBinaryRollbackTested": False, "fullHostBackupRestoreTested": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--core", required=True, type=Path)
    parser.add_argument("--report", required=True, type=Path)
    args = parser.parse_args()
    expect([name for _, name in socket.if_nameindex()] == ["lo"],
           "run this harness in a private network namespace with only loopback enabled")
    core = str(args.core.resolve(strict=True))
    before = hashlib.sha256(Path(core).read_bytes()).hexdigest()
    checks = []
    for name, probe in [("drain-sse-reopen", drain_probe), ("qualified-dispatch-tools", compatibility_probe),
                        ("native-protocol-scope", native_protocol_probe),
                        ("parent-stdin-eof", parent_pipe_probe), ("abrupt-owner-death", parent_death_probe),
                        ("legacy-journal-recovery-copy", journal_probe)]:
        try:
            checks.append({"name": name, "passed": True, "evidence": probe(core)})
        except Exception as error:
            checks.append({"name": name, "passed": False, "error": type(error).__name__, "detail": str(error)})
        print(json.dumps(checks[-1]), flush=True)
    unchanged = before == hashlib.sha256(Path(core).read_bytes()).hexdigest()
    report = {"schemaVersion": 1, "syntheticOnly": True, "actualCoreProcesses": True, "coreSha256": before,
              "networkIsolation": "private_network_namespace_loopback_only",
              "binaryUnchanged": unchanged, "sourceManifestSha256": hashlib.sha256((ROOT / "proxy/source-manifest.json").read_bytes()).hexdigest(),
              "passed": unchanged and all(check["passed"] for check in checks), "checks": checks,
              "notQualified": ["live provider inference", "installed service", "old-binary rollback", "full Host config/credentials/SQLite backup restore", "sustained production growth"]}
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + "\n")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
