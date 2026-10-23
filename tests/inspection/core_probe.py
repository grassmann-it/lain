#!/usr/bin/env python3
"""Independent Inspector boundary probe against one supplied, isolated Core binary.

Uses the existing synthetic loopback provider and private child-process runner.
Does not build, discover a running proxy, read credentials, or contact a provider.
"""
from __future__ import annotations

import argparse
import hashlib
import http.client
import json
from pathlib import Path
import sys
import tempfile
import time
from urllib.error import HTTPError
from urllib.request import HTTPRedirectHandler, ProxyHandler, Request, build_opener

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "backend"))
from qualify import CLIENT_KEY, MANAGEMENT_KEY, ClaudeRun  # noqa: E402
from fake_provider import ALL_KEYS, marker  # noqa: E402


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, *_args, **_kwargs):
        return None


HTTP = build_opener(ProxyHandler({}), NoRedirect())


def expect(condition, message):
    if not condition:
        raise AssertionError(message)


class InspectorRun(ClaudeRun):
    def get(self, path):
        with HTTP.open(Request(self.url + path, headers={"Authorization": "Bearer " + MANAGEMENT_KEY}), timeout=5) as response:
            return json.load(response)

    def inspector(self, after=0):
        return self.get(f"/lain/inspector/events?after={after}&limit=1000")

    def send(self, protocol, body, headers=None):
        anthropic = protocol == "anthropic-messages"
        path = "/v1/messages" if anthropic else "/v1/responses" if protocol == "openai-responses" else "/v1/chat/completions"
        auth = {"x-api-key": CLIENT_KEY, "anthropic-version": "2023-06-01"} if anthropic else {"Authorization": "Bearer " + CLIENT_KEY}
        return HTTP.open(Request(self.url + path, json.dumps(body).encode(), {**auth, "Content-Type": "application/json", **(headers or {})}), timeout=15)

    def terminal_events(self, after, minimum=1):
        deadline = time.monotonic() + 5
        while True:
            page = self.inspector(after)
            rows = page["events"]
            if sum(row["kind"] == "attempt_finished" for row in rows) >= minimum:
                return page
            if time.monotonic() >= deadline:
                raise AssertionError("Inspector did not publish the expected terminal attempts")
            time.sleep(.025)


def dimensions(row):
    value = row.get("dimensions")
    expect(value is None or isinstance(value, dict), "dimension values must be an object or null")
    return value or {}


def check_dimensions(row, expected):
    observed = dimensions(row)
    for field in ("conversationId", "turnId", "workerId", "parentWorkerId", "previousResponseId", "toolCallId"):
        if field not in expected:
            continue
        expect(observed.get(field) == expected[field], f"{field}: expected {expected[field]!r}, got {observed.get(field)!r}")
        if expected[field] is not None:
            evidence = [item for item in observed.get("evidence", []) if item.get("field") == field]
            expect(evidence and all(item.get("source") for item in evidence), f"{field} lacks recorded provenance")
            if expected.get("evidence"):
                expect(any(item["source"] == expected["evidence"] for item in evidence), f"{field} provenance is not {expected['evidence']}")


def page_integrity(page):
    expect(isinstance(page.get("instanceId"), str) and page["instanceId"], "Core Inspector epoch is missing")
    expect(page["earliestCursor"] <= page["nextCursor"] + 1 <= page["highWatermark"] + 1, "Inspector cursor envelope is inconsistent")
    sequences = [row["sequence"] for row in page["events"]]
    expect(sequences == sorted(set(sequences)), "Inspector page contains duplicate or reordered event sequences")
    expect(all(row.get("requestId") and row.get("attemptId") for row in page["events"]), "Inspector event lost its request/attempt identity")


def run_probe(core, fixtures, evidence):
    with InspectorRun(core) as run:
        try:
            with HTTP.open(run.url + "/lain/inspector/events", timeout=5):
                raise AssertionError("Inspector accepted an unauthenticated management read")
        except HTTPError as error:
            expect(error.code == 401, f"Inspector unauthorized status was {error.code}")
            error.close()
        empty = run.inspector()
        page_integrity(empty)
        epoch = empty["instanceId"]
        evidence.append({"case": "management-auth-and-epoch", "passed": True})

        # Responses metadata-only fixtures are also consumed by Host boundary tests:
        # dispatching a fictitious previous_response_id would test backend session storage instead.
        for case in fixtures["cases"]:
            if case["protocol"] == "openai-responses":
                continue
            before = run.inspector()["highWatermark"]
            repeat = case.get("repeat", 1)
            for _ in range(repeat):
                with run.send(case["protocol"], case["body"], case["headers"]) as response:
                    expect(response.status == 200, f"{case['id']} did not dispatch")
                    response.read()
            page = run.terminal_events(before, repeat)
            page_integrity(page)
            expect(page["instanceId"] == epoch, "normal traffic changed the Inspector epoch")
            starts = [row for row in page["events"] if row["kind"] == "attempt_started"]
            expect(len(starts) == repeat, f"{case['id']} emitted an incorrect number of starts")
            for row in starts:
                check_dimensions(row, case["expected"])
            if case["expected"].get("distinctRequestIds"):
                expect(len({row["requestId"] for row in starts}) == repeat, "identical prompt content was collapsed into one request")
            serialized = json.dumps(page)
            expect(all(secret not in serialized for secret in case["expected"].get("forbiddenLiterals", [])), "unlisted request headers leaked into Inspector")
            evidence.append({"case": case["id"], "passed": True, "requests": len(starts)})

        before = run.inspector()["highWatermark"]
        headers = {"Thread-Id": "tool-thread", "X-Codex-Turn-Metadata": json.dumps({"thread_id": "tool-thread", "turn_id": "tool-turn", "agent_id": "tool-worker", "parent_agent_id": "tool-parent"})}
        user = {"role": "user", "content": marker("tool", "inspector-tool-cycle")}
        body = {"model": "fixture-a", "stream": True, "messages": [user], "tools": [{"type": "function", "function": {"name": "fixture_echo", "parameters": {"type": "object", "properties": {"value": {"type": "string"}}}}}]}
        with run.send("openai-chat", body, headers) as response:
            payload = response.read().decode()
        chunks = [json.loads(line[6:]) for line in payload.splitlines() if line.startswith("data: ") and line[6:] != "[DONE]"]
        calls = [call for chunk in chunks for choice in chunk.get("choices", []) for call in choice.get("delta", {}).get("tool_calls", [])]
        expect(len(calls) == 1, "synthetic upstream did not emit its tool call")
        call = {key: value for key, value in calls[0].items() if key != "index"}
        followup = {"model": "fixture-a", "messages": [user, {"role": "assistant", "content": None, "tool_calls": [call]}, {"role": "tool", "tool_call_id": call["id"], "content": "synthetic continuation"}]}
        with run.send("openai-chat", followup, headers) as response:
            response.read()
        page = run.terminal_events(before, 2)
        page_integrity(page)
        starts = [row for row in page["events"] if row["kind"] == "attempt_started"]
        expect(len(starts) == 2 and len({row["requestId"] for row in starts}) == 2, "tool continuation lost distinct logical requests")
        for row in starts:
            check_dimensions(row, {"conversationId": "tool-thread", "turnId": "tool-turn", "workerId": "tool-worker", "parentWorkerId": "tool-parent", "evidence": "harness_header"})
        expect(dimensions(starts[-1]).get("toolCallId") == call["id"], "explicit tool result lost its call identity")
        tools = [row for row in page["events"] if row["kind"] == "tool_delta"]
        expect(any(row.get("toolCallId") == call["id"] and row.get("toolName") == "fixture_echo" for row in tools), "tool events lost call/name metadata")
        expect('"arguments"' not in json.dumps(tools), "Inspector retained tool arguments")
        evidence.append({"case": "tool-continuation-one-conversation-one-turn-two-requests", "passed": True, "toolEvents": len(tools)})

        before = run.inspector()["highWatermark"]
        connection = http.client.HTTPConnection("127.0.0.1", run.port, timeout=10)
        body = {"model": "fixture-a", "stream": True, "messages": [{"role": "user", "content": marker("cancel", "inspector-cancel")}]}
        connection.request("POST", "/v1/chat/completions", json.dumps(body), {"Authorization": "Bearer " + CLIENT_KEY, "Content-Type": "application/json"})
        response = connection.getresponse()
        expect(response.status == 200 and b"data:" in response.readline(), "cancellation stream never started")
        response.close(); connection.close()
        page = run.terminal_events(before)
        terminal = [row for row in page["events"] if row["kind"] == "attempt_finished"]
        expect(len(terminal) == 1 and terminal[0].get("outcome") == "canceled", "client cancellation is not a single canceled terminal event")
        evidence.append({"case": "client-cancellation-terminal-identity", "passed": True})

        all_events = json.dumps(run.inspector())
        journal = run.journal.read_text()
        for secret in [CLIENT_KEY, MANAGEMENT_KEY, *ALL_KEYS, "synthetic-cookie-secret"]:
            expect(secret not in all_events and secret not in journal, "authentication material leaked into Inspector or durable usage")
        expect("synthetic response" not in journal and '"text_delta"' not in journal, "volatile response content leaked into durable usage")
        evidence.append({"case": "credentials-absent-and-payload-not-durable", "passed": True})

        run.stop()
        durable_before = run.journal.read_text()
        run.start()
        restarted = run.inspector()
        page_integrity(restarted)
        expect(restarted["instanceId"] != epoch, "Core restart reused its volatile Inspector epoch")
        expect(restarted["events"] == [] and restarted["highWatermark"] == 0, "Inspector response content survived the process lifetime")
        expect(run.journal.read_text() == durable_before, "Inspector restart rewrote durable usage history")
        evidence.append({"case": "new-process-epoch-no-payload-replay-durable-usage-unchanged", "passed": True})
    return evidence


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--core", required=True)
    parser.add_argument("--report", required=True)
    args = parser.parse_args()
    data = Path(args.core).resolve(strict=True).read_bytes()
    report = {"schemaVersion": 1, "syntheticOnly": True, "coreSha256": hashlib.sha256(data).hexdigest(), "passed": False, "checks": []}
    fixtures = json.loads((Path(__file__).parent / "fixtures" / "protocol-correlations.json").read_text())
    with tempfile.TemporaryDirectory(prefix="lain-inspector-qualified-") as temporary:
        frozen = Path(temporary) / "lain-proxy"
        frozen.write_bytes(data); frozen.chmod(0o700)
        try:
            run_probe(frozen, fixtures, report["checks"])
            report["passed"] = True
        except Exception as error:
            report["error"] = str(error)
    Path(args.report).write_text(json.dumps(report, indent=2) + "\n")
    print(f"{'PASS' if report['passed'] else 'FAIL'} independent Core Inspector probe: {len(report['checks'])} checks")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
