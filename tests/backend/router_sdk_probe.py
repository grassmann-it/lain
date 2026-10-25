#!/usr/bin/env python3
"""Exercise a supplied Core binary through its real SDK and loopback transport.

No build, installed service, user configuration or real provider is used. This
is a candidate qualification harness; passing evidence must come from a run.
"""
from __future__ import annotations

import argparse
import hashlib
import http.client
from http.server import BaseHTTPRequestHandler
import json
import os
from pathlib import Path
import tempfile
import time
from urllib.error import HTTPError
from urllib.request import ProxyHandler, Request, build_opener
import uuid

from fake_provider import FakeProvider, KEYS, MODEL, marker, scenario
from qualify import CLIENT_KEY, Run, assert_complete, expect

ALIAS = "fixture-ordered-router"
MODELS = {"account-a": "fixture-model-a", "account-b": "fixture-model-b"}
LOCAL_HTTP = build_opener(ProxyHandler({}))


class RouterProvider(FakeProvider):
    """Two independently authenticated models; all observed traffic is local."""

    def _handler(self):
        owner = self

        class Handler(BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.1"

            def log_message(self, *_):
                pass

            def reply(self, status, body):
                raw = json.dumps(body).encode()
                self.send_response(status)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(raw)))
                self.end_headers()
                self.wfile.write(raw)

            def do_GET(self):
                if self.path != "/v1/models":
                    return self.reply(404, {"error": "local fixture endpoint only"})
                self.reply(200, {"object": "list", "data": [
                    {"id": model, "object": "model", "owned_by": "local-router-fixture"}
                    for model in MODELS.values()]})

            def do_POST(self):
                if self.path != "/v1/chat/completions":
                    return self.reply(404, {"error": "expected real Chat translation"})
                account = KEYS.get(self.headers.get("Authorization", "").removeprefix("Bearer "))
                if account is None:
                    return self.reply(401, {"error": "synthetic credential required"})
                length = int(self.headers.get("Content-Length", "0"))
                if not 0 < length <= 1024 * 1024:
                    return self.reply(413, {"error": "fixture body limit"})
                body = json.loads(self.rfile.read(length))
                # The newest user marker identifies follow-ups with full history.
                case, request_id = scenario({"messages": list(reversed(body.get("messages", [])))})
                row = {"account": account, "model": body.get("model"), "case": case,
                       "request_id": request_id, "path": self.path, "stream": bool(body.get("stream"))}
                with owner.lock:
                    owner.observations.append(row)
                if body.get("model") != MODELS[account]:
                    row["status"] = 400
                    return self.reply(400, {"error": {"message": "model escaped account binding"}})
                if case == "rate_limit" and account == "account-a":
                    row["status"] = 429
                    return self.reply(429, {"error": {"message": "synthetic quota exceeded", "type": "rate_limit_error", "code": "rate_limit_exceeded"}})
                row["status"] = 200
                text = "partial-before-failure" if case == "partial_sse" else "synthetic response"
                response = {"id": "chatcmpl-" + request_id, "object": "chat.completion", "created": 1700000000,
                            "model": body["model"], "choices": [{"index": 0, "message": {"role": "assistant", "content": text}, "finish_reason": "stop"}],
                            "usage": {"prompt_tokens": 11, "completion_tokens": 7, "total_tokens": 18}}
                if not body.get("stream"):
                    return self.reply(200, response)
                self.send_response(200)
                self.send_header("Content-Type", "text/event-stream")
                self.send_header("Transfer-Encoding", "chunked")
                self.send_header("Connection", "close")
                self.end_headers()
                self.close_connection = True

                def chunk(raw):
                    self.wfile.write(f"{len(raw):x}\r\n".encode() + raw + b"\r\n")
                    self.wfile.flush()

                def event(delta, finish=None):
                    value = {"id": response["id"], "object": "chat.completion.chunk", "created": 1700000000,
                             "model": body["model"], "choices": [{"index": 0, "delta": delta, "finish_reason": finish}]}
                    chunk(b"data: " + json.dumps(value).encode() + b"\n\n")

                try:
                    event({"role": "assistant"})
                    event({"content": text})
                    if case == "partial_sse":
                        # A real mid-body network failure, not a complete SSE
                        # response that merely omits its final event.
                        time.sleep(.15)
                        self.wfile.write(b"100\r\ntruncated")
                        self.wfile.flush()
                        row["transportFailure"] = "truncated HTTP chunk after text delta"
                        return
                    event({}, "stop")
                    chunk(b"data: [DONE]\n\n")
                    self.wfile.write(b"0\r\n\r\n")
                    self.wfile.flush()
                except (BrokenPipeError, ConnectionResetError):
                    row["disconnected"] = True

        return Handler


class RouterRun(Run):
    def __init__(self, core):
        super().__init__(core, retry=1, provider=RouterProvider())
        # Explicitly isolate discovery/home as well as credentials/config.
        home = self.root / "home"
        home.mkdir(mode=0o700)
        self.env.update(HOME=str(home), XDG_CONFIG_HOME=str(home / "config"),
                        XDG_CACHE_HOME=str(home / "cache"), XDG_DATA_HOME=str(home / "data"))
        config = self.root / "core.yaml"
        config.write_text(config.read_text().replace(
            f"      - name: {MODEL}\n        alias: {MODEL}\n",
            "".join(f"      - name: {model}\n        alias: {model}\n" for model in MODELS.values())))
        self.bindings["routes"] = {
            "fixture-" + suffix: {"id": "route-" + suffix, "model": MODELS["account-" + suffix],
                "accounts": ["account-" + suffix], "selection": "strict", "provider": "fixture",
                "upstreamProtocol": "openai", "costPolicy": "configured"}
            for suffix in ("a", "b")}
        fingerprints = {route["id"]: hashlib.sha256("\n".join((route["id"], "fixture", route["model"],
            "openai", route["accounts"][0], "configured", "")).encode()).hexdigest()
            for route in self.bindings["routes"].values()}
        self.bindings["routers"] = {ALIAS: {"id": "router-fixture", "profileId": "profile-fixture",
            "selection": "ordered_sticky_v1", "costPolicy": "configured", "routeIds": ["route-a", "route-b"],
            "memberFingerprints": fingerprints}}
        self.write_bindings()

    def get(self, path):
        # Never use parent-process HTTP(S)_PROXY even for loopback requests.
        from qualify import MANAGEMENT_KEY
        with LOCAL_HTTP.open(Request(self.url + path, headers={"Authorization": "Bearer " + MANAGEMENT_KEY}), timeout=5) as response:
            return json.load(response)

    def request(self, thread, case, request_id, *, continuation=False, stream=False, prior_user="earlier synthetic user turn", prior_assistant="synthetic response"):
        text = marker(case, request_id)
        inputs = [{"role": "user", "content": text}]
        if continuation:
            inputs = [{"role": "user", "content": prior_user},
                      {"role": "assistant", "content": prior_assistant}, *inputs]
        body = {"model": ALIAS, "input": inputs, "store": False, "stream": stream}
        headers = {"Authorization": "Bearer " + CLIENT_KEY, "Content-Type": "application/json", "Thread-Id": thread}
        chunks, error = [], None
        try:
            with LOCAL_HTTP.open(Request(self.url + "/v1/responses", json.dumps(body).encode(), headers), timeout=20) as response:
                status = response.status
                try:
                    while raw := response.readline():
                        chunks.append(raw)
                except http.client.IncompleteRead as problem:
                    chunks.append(problem.partial)
                    error = type(problem).__name__
        except HTTPError as problem:
            status = problem.code
            chunks.append(problem.read())
            problem.close()
        return {"status": status, "body": b"".join(chunks).decode(errors="replace"), "transportError": error}

    def reverse_and_restart(self):
        self.stop()
        self.bindings["routers"][ALIAS]["routeIds"] = ["route-b", "route-a"]
        self.write_bindings()
        self.start()

    def pins(self):
        return json.loads(Path(str(self.journal) + ".router-pins.json").read_text())


def settle(run, requests):
    deadline = time.monotonic() + 5
    last = None
    while time.monotonic() < deadline:
        events = run.events()["events"]
        starts = [event for event in events if event["kind"] == "attempt_started"]
        try:
            assert_complete(events, requests, attempts=len(starts))
            return events
        except AssertionError as error:
            last = error
            time.sleep(.05)
    raise AssertionError(f"journal did not settle: {last}")


def evidence(run):
    return {"upstream": run.provider.snapshot(), "events": run.events()["events"], "pins": run.pins(),
            "coreLogTail": (run.root / "core.log").read_text(errors="replace")[-12000:]}


def check_identities(run, events):
    starts = [event for event in events if event["kind"] == "attempt_started"]
    for event in starts:
        account = event["accountId"]
        expect(account in MODELS, "journal used a non-fixture account")
        expect(event["requestedModel"] == ALIAS, "journal lost requested router alias")
        expect(event["routeId"] == "route-" + account[-1], "journal substituted virtual route identity")
        expect(event["model"] == MODELS[account], "journal model/account differs from pinned binding")
        expect(event["credentialId"] == run.accounts[account], "journal credential differs from real account")
    rows = run.provider.snapshot()
    expect(len(starts) == len(rows), "SDK attempts and actual local upstream sends differ")
    expect(sorted((event["accountId"], event["model"]) for event in starts) ==
           sorted((row["account"], row["model"]) for row in rows), "wire and journal identities diverged")


def probe(core, mode, destination):
    run = RouterRun(core)
    result = {"name": mode, "passed": False}
    try:
        run.start()
        thread = str(uuid.uuid4())
        response = run.request(thread, mode, "first-" + mode, stream=mode == "partial_sse")
        result["firstResponse"] = response
        if mode == "rate_limit":
            expect(response["status"] == 429, "first route 429 was hidden or replaced by fallback")
        else:
            expect(response["status"] == 200, "first request failed before dispatch")
        if mode == "partial_sse":
            expect("partial-before-failure" in response["body"], "client did not receive partial output before failure")
            expect('"type":"response.completed"' not in response["body"].replace(" ", ""), "truncated stream was reported complete")
        first_events = settle(run, 1)
        expect(run.provider.snapshot() and all(row["account"] == "account-a" for row in run.provider.snapshot()),
               "router switched upstream account after 429/partial output or ignored first order")
        pins = run.pins()
        expect(len(pins["pins"]) == 1 and pins["pins"][0]["routeId"] == "route-a" and
               pins["pins"][0]["accountId"] == "account-a", "first durable pin not bound to real first member")
        if mode in ("rate_limit", "partial_sse"):
            terminals = [event for event in first_events if event.get("completionState") is not None]
            expect(terminals and all(event["completionState"] == "failed" for event in terminals), "failed upstream attempt not durably marked failed")
        run.reverse_and_restart()
        expect(run.pins() == pins, "restart/reorder changed durable pins")
        start = len(run.provider.snapshot())
        followup = run.request(thread, "text", "followup-" + mode, continuation=True,
                               prior_user=marker(mode, "first-" + mode),
                               prior_assistant="partial-before-failure" if mode == "partial_sse" else "synthetic response")
        result["followupResponse"] = followup
        expect(followup["status"] == 200, "pinned continuation failed after restart/reorder")
        expect([row["account"] for row in run.provider.snapshot()[start:]] == ["account-a"], "continuation moved from pinned account")
        fresh = run.request(str(uuid.uuid4()), "text", "new-" + mode)
        expect(fresh["status"] == 200 and run.provider.snapshot()[-1]["account"] == "account-b", "new thread ignored reordered first member")
        events = settle(run, 3)
        check_identities(run, events)
        # Existing history with no pin must not allocate a new conversation.
        before = run.pins()
        count = len(run.provider.snapshot())
        rejected = run.request(str(uuid.uuid4()), "text", "unpinned-" + mode, continuation=True)
        expect(rejected["status"] == 409, "unbound existing conversation was adopted")
        expect(run.pins() == before and len(run.provider.snapshot()) == count and run.events()["events"] == events,
               "rejected existing conversation mutated pins or dispatched")
        result.update(passed=True, unpinnedContinuationStatus=rejected["status"],
                      initialThread=thread, firstOrder=["route-a", "route-b"], restartedOrder=["route-b", "route-a"])
    except Exception as error:
        result["error"] = f"{type(error).__name__}: {error}"
    finally:
        try:
            result["evidence"] = evidence(run)
        except Exception as error:
            result["evidenceError"] = str(error)
            result["coreLogTail"] = (run.root / "core.log").read_text(errors="replace")[-12000:]
        run.__exit__()
        destination.write_text(json.dumps(result, indent=2) + "\n")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--core", required=True, type=Path)
    parser.add_argument("--evidence-dir", required=True, type=Path, help="new directory, refuses existing evidence")
    args = parser.parse_args()
    args.evidence_dir.mkdir(parents=True, mode=0o700, exist_ok=False)
    data = args.core.resolve(strict=True).read_bytes()
    report = {"schemaVersion": 1, "syntheticOnly": True, "coreSha256": hashlib.sha256(data).hexdigest(),
              "scope": "real supplied binary, SDK executor, Responses-to-Chat translation, loopback HTTP and durable journal; no live provider or Free qualification",
              "passed": False, "checks": []}
    with tempfile.TemporaryDirectory(prefix="lain-router-sdk-binary-") as temporary:
        frozen = Path(temporary) / "lain-proxy"
        frozen.write_bytes(data)
        frozen.chmod(0o700)
        for mode in ("text", "rate_limit", "partial_sse"):
            result = probe(frozen, mode, args.evidence_dir / (mode + ".json"))
            report["checks"].append({"name": mode, "passed": result["passed"], "evidence": mode + ".json",
                                     **({"error": result["error"]} if "error" in result else {})})
        report["passed"] = all(check["passed"] for check in report["checks"])
    (args.evidence_dir / "summary.json").write_text(json.dumps(report, indent=2) + "\n")
    print(f"{'PASS' if report['passed'] else 'FAIL'} router SDK transport: {args.evidence_dir / 'summary.json'}", flush=True)
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
