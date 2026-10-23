#!/usr/bin/env python3
"""Reproducible synthetic Core qualification; never connects to an existing proxy.

Usage: python3 tests/backend/qualify.py --core /absolute/lain-proxy --report result.json
Only the supplied new child process and ephemeral loopback fixture are operated.
"""
from __future__ import annotations

import argparse
import concurrent.futures
import hashlib
import http.client
import json
import os
from pathlib import Path
import resource
import socket
import subprocess
import tempfile
import time
import traceback
from urllib.error import HTTPError
from urllib.request import Request, urlopen

from fake_provider import FakeProvider, KEYS, MODEL, marker

CLIENT_KEY = "lain-synthetic-client-inference"
MANAGEMENT_KEY = "lain-synthetic-private-management-token"


def expect(condition, message):
    if not condition:
        raise AssertionError(message)


class Run:
    def __init__(self, core, retry=0):
        self.core = str(Path(core).resolve(strict=True))
        self.temp = tempfile.TemporaryDirectory(prefix="lain-backend-")
        self.root = Path(self.temp.name)
        os.chmod(self.root, 0o700)
        self.provider = FakeProvider().__enter__()
        with socket.socket() as probe:
            probe.bind(("127.0.0.1", 0))
            self.port = probe.getsockname()[1]
        self.url = f"http://127.0.0.1:{self.port}"
        self.process = None
        self.log = (self.root / "core.log").open("wb")
        self.journal = self.root / "events.jsonl"
        self.accounts = {}
        for key, account in KEYS.items():
            kind = "openai-compatibility:fixture"
            digest = hashlib.sha256((kind + "\0" + key + "\0" + self.provider.url + "\0").encode()).hexdigest()[:12]
            self.accounts[account] = kind + ":" + digest
        self.bindings = {"accounts": self.accounts, "routes": {
            "fixture-a": {"id": "route-a", "model": MODEL, "accounts": ["account-a"], "selection": "strict"},
            "fixture-b": {"id": "route-b", "model": MODEL, "accounts": ["account-b"], "selection": "strict"},
            "fixture-shared": {"id": "route-shared", "model": MODEL, "accounts": list(self.accounts), "selection": "pool"}}}
        self.write_bindings()
        (self.root / "management-token").write_text(MANAGEMENT_KEY)
        config = f'''host: "127.0.0.1"
port: {self.port}
auth-dir: "{self.root / 'auth'}"
api-keys: ["{CLIENT_KEY}"]
request-retry: {retry}
max-retry-interval: 1
openai-compatibility:
  - name: fixture
    base-url: "{self.provider.url}"
    api-key-entries:
      - api-key: lain-synthetic-a
      - api-key: lain-synthetic-b
    models:
      - name: {MODEL}
        alias: {MODEL}
'''
        (self.root / "core.yaml").write_text(config)
        for path in self.root.iterdir():
            if path.is_file():
                path.chmod(0o600)

    def write_bindings(self):
        (self.root / "bindings.json").write_text(json.dumps(self.bindings))

    def command(self):
        return [self.core, "--config", str(self.root / "core.yaml"), "--bindings", str(self.root / "bindings.json"),
                "--journal", str(self.journal), "--management-token-file", str(self.root / "management-token")]

    def start(self):
        self.process = subprocess.Popen(self.command(), cwd=self.root, stdout=self.log, stderr=self.log,
                                        env={**os.environ, "NO_PROXY": "127.0.0.1,localhost"})
        deadline = time.monotonic() + 20
        while time.monotonic() < deadline:
            if self.process.poll() is not None:
                raise RuntimeError("isolated core exited during startup: " + (self.root / "core.log").read_text()[-3000:])
            try:
                if self.get("/lain/health")["ready"]:
                    return
            except (OSError, ValueError):
                pass
            time.sleep(.05)
        raise RuntimeError("isolated core did not become ready")

    def stop(self):
        if self.process and self.process.poll() is None:
            self.process.terminate()
            try:
                self.process.wait(timeout=8)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait(timeout=3)

    def __enter__(self):
        try:
            self.start()
        except Exception:
            self.__exit__()
            raise
        return self

    def __exit__(self, *_):
        self.stop()
        self.provider.__exit__()
        self.log.close()
        self.temp.cleanup()

    def get(self, path):
        with urlopen(Request(self.url + path, headers={"Authorization": "Bearer " + MANAGEMENT_KEY}), timeout=5) as response:
            return json.load(response)

    def events(self, after=0):
        return self.get(f"/lain/events?after={after}&limit=1000")

    def post(self, case, request_id, *, route="fixture-shared", account=None, stream=False, messages=None):
        body = {"model": route, "messages": messages or [{"role": "user", "content": marker(case, request_id)}], "stream": stream}
        if case == "tool":
            body["tools"] = [{"type": "function", "function": {"name": "fixture_echo", "description": "Harmless local echo", "parameters": {"type": "object", "properties": {"value": {"type": "string"}}, "required": ["value"], "additionalProperties": False}}}]
        if stream:
            body["stream_options"] = {"include_usage": True}
        headers = {"Authorization": "Bearer " + CLIENT_KEY, "Content-Type": "application/json"}
        if account:
            headers["X-Lain-Account-ID"] = account
        return urlopen(Request(self.url + "/v1/chat/completions", json.dumps(body).encode(), headers), timeout=15)

    def responses(self, body):
        return urlopen(Request(self.url + "/v1/responses", json.dumps(body).encode(),
                               {"Authorization": "Bearer " + CLIENT_KEY, "Content-Type": "application/json"}), timeout=15)


def assert_complete(events, requests, attempts=None):
    starts = [event for event in events if event["kind"] == "attempt_started"]
    usages = [event for event in events if event["kind"] == "usage"]
    expect(len({event["requestId"] for event in starts}) == requests, "logical request count mismatch")
    expect(len(starts) == (attempts if attempts is not None else requests), "attempt count mismatch")
    expect(len(usages) == len(starts), "registered attempt missing terminal usage")
    expect(len({event["eventId"] for event in events}) == len(events), "duplicate source event IDs")
    expect(len({event["attemptId"] for event in starts}) == len(starts), "reused execution identity")
    for start in starts:
        terminal = [row for row in usages if row["attemptId"] == start["attemptId"]]
        expect(len(terminal) == 1, "attempt must have exactly one terminal usage")
        for key in ("requestId", "accountId", "credentialId", "routeId"):
            expect(terminal[0][key] == start[key], f"changed {key} within attempt")
    return starts, usages


def normal_and_restart(core):
    with Run(core) as run:
        def one(item):
            account, index = item
            with run.post("text", f"{account}-{index}", account=account) as response:
                expect(json.load(response)["usage"]["total_tokens"] == 18, "response usage lost")
        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as executor:
            list(executor.map(one, ((a, i) for a in run.accounts for i in range(100))))
        batch = run.events()
        starts, usages = assert_complete(batch["events"], 200)
        for account, credential in run.accounts.items():
            rows = [event for event in usages if event["accountId"] == account]
            expect(len(rows) == 100, "same alias account attribution mismatch")
            expect(all(row["credentialId"] == credential for row in rows), "credential mapping mismatch")
            expect(sum(row["totalTokens"] for row in rows) == 1800, "token aggregate mismatch")
        observations = run.provider.snapshot()
        expect(len(observations) == 200, "unexpected upstream attempts")
        for account in run.accounts:
            expect(sum(row["account"] == account for row in observations) == 100, "upstream account pin mismatch")
        expect(run.events() == batch, "replay consumed or altered source events")
        paged = []
        cursor = 0
        while cursor < batch["highWatermark"]:
            page = run.get(f"/lain/events?after={cursor}&limit=37")
            paged.extend(page["events"])
            cursor = page["nextCursor"]
        expect(paged == batch["events"], "cursor pagination differs from replay")
        raw = run.journal.read_bytes()
        for secret in (*KEYS, CLIENT_KEY, MANAGEMENT_KEY, "LAIN_FIXTURE:"):
            expect(secret.encode() not in raw, "journal stored request content or synthetic secret")
        run.stop()
        run.start()
        expect(run.events() == batch, "restart lost/changed persisted identity or events")
        return {"requests": 200, "attempts": len(starts), "events": len(batch["events"]), "tokensPerAccount": 1800,
                "distinctCredentials": len({row["credentialId"] for row in starts}), "replayEqual": True, "restartEqual": True,
                "eventExamples": [next(row for row in usages if row["accountId"] == account) for account in run.accounts], "batchExample": batch}


def stream_tools(core):
    with Run(core) as run:
        with run.post("text", "sse", route="fixture-a", stream=True) as response:
            raw = response.read().decode()
        expect("[DONE]" in raw and "synthetic response" in raw, "SSE text/DONE lost")
        messages = [{"role": "user", "content": marker("tool", "tool")}]
        with run.post("tool", "tool", route="fixture-a", stream=True, messages=messages) as response:
            raw = response.read().decode()
        chunks = [json.loads(line[6:]) for line in raw.splitlines() if line.startswith("data: {")]
        calls = [chunk["choices"][0]["delta"]["tool_calls"][0] for chunk in chunks if chunk["choices"] and "tool_calls" in chunk["choices"][0]["delta"]]
        expect(bool(calls), "SSE tool call absent")
        call = {key: value for key, value in calls[0].items() if key != "index"}
        expect(json.loads(call["function"]["arguments"]) == {"value": "safe"}, "tool arguments altered")
        messages.extend([{"role": "assistant", "content": None, "tool_calls": [call]}, {"role": "tool", "tool_call_id": call["id"], "content": "safe"}])
        with run.post("tool", "tool-continuation", route="fixture-a", stream=True, messages=messages) as response:
            expect("synthetic tool result accepted" in response.read().decode(), "tool continuation failed")
        starts, usages = assert_complete(run.events()["events"], 3)
        expect(all(row["totalTokens"] == 18 for row in usages), "SSE usage not durable")
        expect(run.provider.snapshot()[-1]["tool_result"], "tool result never reached provider")
        return {"requests": 3, "attempts": len(starts), "sse": True, "toolContinuation": True, "eventExamples": usages[-1:]}


def strict_429(core):
    with Run(core, retry=1) as run:
        try:
            with run.post("rate_limit", "strict-429", account="account-a") as response:
                response.read()
                raise AssertionError("strict exhausted account unexpectedly succeeded")
        except HTTPError as error:
            status = error.code
            expect(status == 429, f"strict rate limit returned {status}")
            error.close()
        rows = run.provider.snapshot()
        expect(rows and all(row["account"] == "account-a" for row in rows), "strict request escaped to other account")
        starts, usages = assert_complete(run.events()["events"], 1, len(rows))
        expect(all(row["statusCode"] == 429 and row["totalTokens"] is None for row in usages), "failed usage invented tokens/status")
        return {"requests": 1, "attempts": len(starts), "status": status, "accounts": sorted({r["accountId"] for r in starts}), "unknownTokens": True, "eventExamples": usages}


def responses_tools(core):
    """Client Responses over Core's OpenAI Chat provider adapter; no Codex live claim."""
    def events(response):
        return [json.loads(line[6:]) for line in response.read().decode().splitlines() if line.startswith("data: {")]
    with Run(core) as run:
        with run.responses({"model": "fixture-a", "input": marker("text", "responses-text"), "stream": True}) as response:
            text_events = events(response)
        expect(any(row.get("type") == "response.completed" for row in text_events), "Responses stream never completed")
        expect(any(row.get("type") == "response.output_text.delta" and "synthetic response" in row.get("delta", "") for row in text_events), "Responses text translation lost")
        inputs = [{"role": "user", "content": [{"type": "input_text", "text": marker("tool", "responses-tool")}]}]
        tool = {"type": "function", "name": "fixture_echo", "description": "Harmless local echo", "strict": True,
                "parameters": {"type": "object", "properties": {"value": {"type": "string"}}, "required": ["value"], "additionalProperties": False}}
        with run.responses({"model": "fixture-a", "input": inputs, "tools": [tool], "stream": True}) as response:
            tool_events = events(response)
        completed = [row for row in tool_events if row.get("type") == "response.completed"]
        expect(len(completed) == 1, "Responses tool stream completion absent")
        outputs = completed[0]["response"]["output"]
        calls = [item for item in outputs if item.get("type") == "function_call"]
        expect(len(calls) == 1, "Responses function_call absent")
        call = calls[0]
        expect(call["name"] == "fixture_echo" and json.loads(call["arguments"]) == {"value": "safe"}, "Responses tool call metadata altered")
        inputs.extend(outputs)
        inputs.append({"type": "function_call_output", "call_id": call["call_id"], "output": "safe"})
        with run.responses({"model": "fixture-a", "input": inputs, "tools": [tool], "stream": True}) as response:
            continuation = events(response)
        expect(any(row.get("type") == "response.output_text.delta" and "synthetic tool result accepted" in row.get("delta", "") for row in continuation), "Responses tool continuation failed")
        _, usages = assert_complete(run.events()["events"], 3)
        expect(all(row["totalTokens"] == 18 for row in usages), "Responses translated usage missing")
        expect(run.provider.snapshot()[-1]["tool_result"], "Responses tool output never reached Chat provider")
        return {"requests": 3, "clientProtocol": "OpenAI Responses SSE", "providerProtocol": "OpenAI Chat Completions SSE", "toolContinuation": True}


def pool_429(core):
    with Run(core) as run:
        for index in range(4):
            with run.post("rate_limit", f"pool-{index}") as response:
                expect(response.status == 200, "pool fallback failed")
                response.read()
            events = run.events()["events"]
            starts = [row for row in events if row["kind"] == "attempt_started"]
            groups = {}
            for row in starts:
                groups.setdefault(row["requestId"], []).append(row)
            retried = [rows for rows in groups.values() if len(rows) > 1]
            if retried:
                expect({row["accountId"] for row in retried[0]} == {"account-a", "account-b"}, "pool retry attribution wrong")
                _, usages = assert_complete(events, index + 1, len(starts))
                expect(any(row["statusCode"] == 429 and row["totalTokens"] is None for row in usages), "missing failed pool attempt")
                expect(any(row["totalTokens"] == 18 for row in usages), "missing successful pool usage")
                return {"requests": index + 1, "attempts": len(starts), "retryAccounts": [row["accountId"] for row in retried[0]],
                        "eventExamples": [row for row in events if row["requestId"] == retried[0][0]["requestId"]]}
        raise AssertionError("pool requests never proved same-request cross-account retry")


def cancel(core):
    with Run(core) as run:
        connection = http.client.HTTPConnection("127.0.0.1", run.port, timeout=10)
        body = json.dumps({"model": "fixture-a", "stream": True, "messages": [{"role": "user", "content": marker("cancel", "cancel")}]})
        connection.request("POST", "/v1/chat/completions", body, {"Authorization": "Bearer " + CLIENT_KEY, "Content-Type": "application/json"})
        response = connection.getresponse()
        expect(response.status == 200, "cancel stream did not start")
        expect(b"data:" in response.readline(), "cancel stream had no first output")
        response.close()
        connection.close()
        deadline = time.monotonic() + 5
        batch = run.events()
        while time.monotonic() < deadline:
            batch = run.events()
            if any(row["kind"] == "usage" for row in batch["events"]) and run.provider.snapshot()[0]["disconnected"]:
                break
            time.sleep(.03)
        starts, usages = assert_complete(batch["events"], 1)
        expect(usages[0]["outcome"] == "canceled", "client abort not reported as canceled")
        expect(usages[0]["totalTokens"] is None, "cancellation invented usage without provider usage")
        expect(run.provider.snapshot()[0]["disconnected"], "upstream stream was not canceled")
        return {"attempts": len(starts), "outcome": "canceled", "tokens": None, "eventExamples": usages}


def unknown_token_components(core):
    with Run(core) as run:
        with run.post("text", "unknown-components", route="fixture-a") as response:
            response.read()
        _, usages = assert_complete(run.events()["events"], 1)
        row = usages[0]
        expect([row["inputTokens"], row["outputTokens"], row["totalTokens"]] == [11, 7, 18], "reported token values altered")
        for field in ("cacheReadTokens", "cacheCreationTokens", "reasoningTokens"):
            expect(row[field] is None, f"provider omitted {field} but journal invented {row[field]}")
        return {"inputTokens": 11, "outputTokens": 7, "totalTokens": 18, "unreportedComponents": None}


def credential_lifecycle(core):
    with Run(core) as run:
        with run.post("text", "original-key", route="fixture-a") as response:
            response.read()
        original = run.events()["events"]
        first_credential = run.accounts["account-a"]
        run.stop()
        config = run.root / "core.yaml"
        config.write_text(config.read_text().replace("api-key: lain-synthetic-a\n", "api-key: lain-synthetic-a-rotated\n"))
        kind = "openai-compatibility:fixture"
        rotated = kind + ":" + hashlib.sha256((kind + "\0lain-synthetic-a-rotated\0" + run.provider.url + "\0").encode()).hexdigest()[:12]
        run.bindings["accounts"]["account-a"] = rotated
        run.write_bindings()
        run.start()
        with run.post("text", "rotated-key", route="fixture-a") as response:
            response.read()
        rows = run.events()["events"]
        expect(rows[:len(original)] == original, "key change rewrote historical identity")
        expect(rows[-1]["accountId"] == "account-a" and rows[-1]["credentialId"] == rotated, "rotated key lost stable account identity")
        expect(first_credential != rotated, "rotation did not change credential identity")
        expect(run.provider.snapshot()[-1]["account"] == "account-a-rotated", "rotated credential not used upstream")
        run.stop()
        # Remove the old account and re-add the same rotated credential with a new stable ID.
        del run.bindings["accounts"]["account-a"]
        run.bindings["accounts"]["account-new"] = rotated
        for route in run.bindings["routes"].values():
            route["accounts"] = ["account-new" if value == "account-a" else value for value in route["accounts"]]
        run.write_bindings()
        run.start()
        with run.post("text", "new-account-same-key", route="fixture-a") as response:
            response.read()
        rows = run.events()["events"]
        _, usages = assert_complete(rows, 3)
        expect([row["accountId"] for row in usages] == ["account-a", "account-a", "account-new"], "remove/re-add relabeled past account")
        expect([row["credentialId"] for row in usages] == [first_credential, rotated, rotated], "credential history not retained")
        return {"requests": 3, "stableAccountAcrossKeyChange": True, "credentialChanged": True, "removeReaddPreservesPastAccount": True,
                "scope": "synthetic adapter binding revisions across restart; display-name/OAuth lifecycle excluded"}


def crash_inflight(core):
    with Run(core) as run:
        connection = http.client.HTTPConnection("127.0.0.1", run.port, timeout=10)
        body = json.dumps({"model": "fixture-a", "stream": True, "messages": [{"role": "user", "content": marker("cancel", "crash")}]})
        connection.request("POST", "/v1/chat/completions", body, {"Authorization": "Bearer " + CLIENT_KEY, "Content-Type": "application/json"})
        response = connection.getresponse()
        expect(response.status == 200 and b"data:" in response.readline(), "inflight fixture did not start")
        before = run.events()
        expect(len(before["events"]) == 1 and before["events"][0]["kind"] == "attempt_started", "dispatch registration was not durable before output")
        run.process.kill()
        run.process.wait(timeout=3)
        response.close()
        connection.close()
        run.start()
        expect(run.events() == before, "crash lost dispatch registration or invented terminal usage")
        with run.post("text", "after-crash", route="fixture-a") as response:
            response.read()
        after = run.events()
        old_attempt = before["events"][0]["attemptId"]
        expect(len(after["events"]) == 3, "crash restart lost or duplicated source events")
        expect(sum(row["attemptId"] == old_attempt for row in after["events"]) == 1, "unknown crashed attempt misreported as completed")
        expect(len({row["requestId"] for row in after["events"]}) == 2, "postrestart work reused crashed request identity")
        return {"killSignal": "SIGKILL", "durableStartedBeforeOutput": True, "unresolvedAttempts": 1,
                "missingProviderUsage": "detectable from unmatched attempt_started; exact consumed tokens unknown", "postRestartRequestSucceeded": True,
                "eventExamples": before["events"]}


def journal_failures(core):
    with Run(core) as run:
        duplicate = subprocess.run(run.command(), cwd=run.root, capture_output=True, timeout=8)
        expect(duplicate.returncode != 0 and b"journal already owned" in duplicate.stderr, "second journal writer was not rejected")
        # Linux per-process file limit affects only our child. No filesystem/device manipulation.
        resource.prlimit(run.process.pid, resource.RLIMIT_FSIZE, (1, 1))
        try:
            with run.post("text", "journal-write-failure", route="fixture-a") as response:
                response.read()
                raise AssertionError("dispatch succeeded despite unavailable durable journal")
        except HTTPError as error:
            expect(error.code == 503, f"durability failure returned {error.code}")
            error.close()
        expect(not run.provider.snapshot(), "upstream dispatched without durable registration")
        try:
            run.get("/lain/health")
            raise AssertionError("faulted journal reported ready")
        except HTTPError as error:
            expect(error.code == 503, "faulted journal health not unavailable")
            error.close()
        run.stop()
        # Preserve the torn append as evidence; startup must refuse silent truncation.
        original = run.journal.read_bytes()
        expect(original, "file-limit failure did not produce testable partial append")
        failed = subprocess.run(run.command(), cwd=run.root, capture_output=True, timeout=8)
        expect(failed.returncode != 0 and b"journal incomplete" in failed.stderr, "torn journal was silently accepted")
        expect(run.journal.read_bytes() == original, "startup altered torn journal")
        # Recovery is explicit: this fixture had no dispatched work; restore its known-empty journal.
        run.journal.write_bytes(b"")
        run.start()
        with run.post("text", "recovered", route="fixture-a") as response:
            response.read()
        assert_complete(run.events()["events"], 1)
        return {"duplicateWriterRejected": True, "predispatchFailure": 503, "upstreamAttemptsDuringFailure": 0,
                "tornJournalRejectedUnchanged": True, "explicitRecovery": True}


def startup_failures(core):
    run = Run(core)
    try:
        config = run.root / "core.yaml"
        original = config.read_text()
        config.write_text(original.replace('host: "127.0.0.1"', 'host: "0.0.0.0"'))
        failed = subprocess.run(run.command(), cwd=run.root, capture_output=True, timeout=8)
        expect(failed.returncode != 0 and b"loopback" in failed.stderr, "unsafe host config was accepted")
        expect(not run.provider.snapshot(), "invalid config dispatched upstream work")
        config.write_text(original)
        run.bindings["accounts"]["account-b"] = run.accounts["account-a"]
        run.write_bindings()
        failed = subprocess.run(run.command(), cwd=run.root, capture_output=True, timeout=8)
        expect(failed.returncode != 0 and b"unique" in failed.stderr, "two aliases of same credential accepted as two accounts")
        # Reconstruct account-b's distinct fixture binding after deliberate invalid revision.
        kind = "openai-compatibility:fixture"
        run.bindings["accounts"]["account-b"] = kind + ":" + hashlib.sha256((kind + "\0lain-synthetic-b\0" + run.provider.url + "\0").encode()).hexdigest()[:12]
        run.write_bindings()
        run.start()
        with run.post("text", "recovered-config", route="fixture-a") as response:
            response.read()
        assert_complete(run.events()["events"], 1)
        return {"nonLoopbackRejected": True, "duplicateCredentialRejected": True, "validConfigRecovery": True}
    finally:
        run.__exit__()


def terminal_write_failure(core, protocol="chat"):
    with Run(core) as run:
        if protocol == "responses":
            response = run.responses({"model": "fixture-a", "input": marker("gated_usage", "terminal-write-failure"), "stream": True})
        else:
            response = run.post("gated_usage", "terminal-write-failure", route="fixture-a", stream=True)
        first_line = response.readline()
        expect(b"data:" in first_line, "gated stream did not produce first output")
        before = run.events()
        expect(len(before["events"]) == 1 and before["events"][0]["kind"] == "attempt_started", "gated dispatch registration missing")
        resource.prlimit(run.process.pid, resource.RLIMIT_FSIZE, (run.journal.stat().st_size + 1,) * 2)
        run.provider.usage_gate.set()
        try:
            remainder = response.read().decode()
        finally:
            response.close()
        expect("[DONE]" not in remainder, "client received successful DONE before durable terminal usage")
        expect("response.completed" not in remainder, "client received Responses completion before durable terminal usage")
        expect('"finish_reason":"stop"' not in remainder and '"finish_reason": "stop"' not in remainder, "client received successful finish before durable terminal usage")
        try:
            with run.post("text", "after-terminal-fault", route="fixture-a") as response:
                response.read()
                raise AssertionError("subsequent request succeeded after terminal persistence failure")
        except HTTPError as error:
            expect(error.code == 503, "subsequent request did not fail closed")
            error.close()
        expect(len(run.provider.snapshot()) == 1, "later work dispatched after terminal journal failure")
        after = run.events()
        expect(after == before, "failed terminal append was exposed as a persisted source event")
        return {"clientProtocol": protocol, "startedPersisted": True, "clientCompletionSuppressed": True, "nextRequestStatus": 503,
                "upstreamAttempts": 1, "unresolvedAttempts": 1, "eventExamples": before["events"]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--core", required=True)
    parser.add_argument("--report", required=True)
    parser.add_argument("--examples", help="optional sanitized observed-event fixture output JSON")
    parser.add_argument("--batch-fixture", help="optional full contiguous 200-request source envelope for consumer qualification")
    args = parser.parse_args()
    core_data = Path(args.core).resolve(strict=True).read_bytes()
    # Freeze the artifact once: another worker rebuilding its path cannot change
    # the executable between checks while retaining the initial reported hash.
    frozen = tempfile.TemporaryDirectory(prefix="lain-qualified-artifact-")
    core_path = Path(frozen.name) / "lain-proxy"
    core_path.write_bytes(core_data)
    core_path.chmod(0o700)
    core = str(core_path)
    report = {"schemaVersion": 1, "syntheticOnly": True, "coreSha256": hashlib.sha256(core_data).hexdigest(), "checks": []}
    examples = {"schemaVersion": 1, "syntheticOnly": True, "coreSha256": report["coreSha256"], "examples": {}}
    for name, function in [("same-alias-200-replay-restart", normal_and_restart), ("sse-tool-continuation", stream_tools), ("responses-tool-translation", responses_tools),
                           ("strict-429", strict_429), ("pool-429-retry", pool_429), ("cancel", cancel),
                           ("unknown-token-components", unknown_token_components), ("credential-lifecycle", credential_lifecycle),
                           ("crash-inflight", crash_inflight), ("journal-failures", journal_failures), ("startup-failures", startup_failures),
                           ("terminal-write-failure", terminal_write_failure),
                           ("responses-terminal-write-failure", lambda core: terminal_write_failure(core, "responses"))]:
        try:
            evidence = function(core)
            if "eventExamples" in evidence:
                examples["examples"][name] = evidence.pop("eventExamples")
            batch_example = evidence.pop("batchExample", None)
            if batch_example is not None and args.batch_fixture:
                Path(args.batch_fixture).write_text(json.dumps(batch_example, indent=2) + "\n")
            report["checks"].append({"name": name, "passed": True, "evidence": evidence})
            print(f"PASS {name}", flush=True)
        except Exception as error:
            report["checks"].append({"name": name, "passed": False, "error": str(error)})
            print(f"FAIL {name}: {error}", flush=True)
            traceback.print_exc()
    report["passed"] = all(check["passed"] for check in report["checks"])
    report["pending"] = ["real Codex/ChatGPT and Command Code provider probes", "display-name and real OAuth credential lifecycle", "consumer transactional SQLite acknowledgement", "production service before/after isolation evidence", "product acceptance of unresolved in-flight usage after hard crash"]
    Path(args.report).write_text(json.dumps(report, indent=2) + "\n")
    if args.examples:
        Path(args.examples).write_text(json.dumps(examples, indent=2) + "\n")
    frozen.cleanup()
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
