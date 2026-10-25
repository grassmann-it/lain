#!/usr/bin/env python3
"""Synthetic context rejection through a supplied, isolated Core binary.

Requires the serialized runtime slot. Reuses only the disposable loopback Core
runner, with private state, synthetic keys and an owned local provider. Does not
build, discover installed services, read user credentials, or call real providers.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import sys
from urllib.error import HTTPError
from urllib.request import HTTPRedirectHandler, ProxyHandler, Request, build_opener

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "backend"))
from qualify import CLIENT_KEY, Run, expect  # noqa: E402
from fake_provider import FakeProvider, KEYS, MODEL  # noqa: E402


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, *_args, **_kwargs):
        return None


HTTP = build_opener(ProxyHandler({}), NoRedirect())
PROMPT = "synthetic complete context Grüße🐈 " * 4096
TOOL_RESULT = "synthetic complete tool result " * 1024
TOOLS = [{"type": "function", "function": {
    "name": f"fixture_echo_{n}", "description": f"Complete fixture tool {n}",
    "parameters": {"type": "object", "properties": {"value": {"type": "string"}},
                   "required": ["value"], "additionalProperties": False}}} for n in range(3)]


class ContextProvider(FakeProvider):
    def __init__(self):
        self.status = 400
        self.error_body = b""
        super().__init__()

    def _handler(self):
        owner = self
        base = super()._handler()

        class Handler(base):
            def do_POST(self):
                if self.path not in ("/v1/chat/completions", "/chat/completions"):
                    return self.reply(404, {"error": {"code": "wrong_fixture_endpoint"}})
                key = self.headers.get("Authorization", "").removeprefix("Bearer ")
                if key not in KEYS:
                    return self.reply(401, {"error": {"code": "synthetic_key_required"}})
                length = int(self.headers.get("Content-Length", "0"))
                if not 0 < length <= 2 * 1024 * 1024:
                    return self.reply(400, {"error": {"code": "fixture_body_limit"}})
                raw = self.rfile.read(length)
                body = json.loads(raw)
                with owner.lock:
                    owner.observations.append({"body": body, "path": self.path,
                                               "bytes": len(raw), "account": KEYS[key]})
                self.send_response(owner.status)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(owner.error_body)))
                self.end_headers()
                self.wfile.write(owner.error_body)

        return Handler


def request_body(protocol, stream):
    calls = [{"id": "call_context_1", "type": "function", "function": {
        "name": "fixture_echo_0", "arguments": '{"value":"complete arguments"}'}}]
    if protocol == "chat":
        return {"model": "fixture-a", "stream": stream, "max_tokens": 2048, "tools": TOOLS,
                "messages": [{"role": "user", "content": PROMPT},
                             {"role": "assistant", "content": None, "tool_calls": calls},
                             {"role": "tool", "tool_call_id": "call_context_1", "content": TOOL_RESULT}]}
    return {"model": "fixture-a", "stream": stream, "max_output_tokens": 2048,
            "tools": [{"type": "function", **tool["function"]} for tool in TOOLS],
            "input": [{"type": "message", "role": "user", "content": [{"type": "input_text", "text": PROMPT}]},
                      {"type": "function_call", "call_id": "call_context_1", "name": "fixture_echo_0",
                       "arguments": calls[0]["function"]["arguments"]},
                      {"type": "function_call_output", "call_id": "call_context_1", "output": TOOL_RESULT}]}


def string_values(value):
    if isinstance(value, str):
        yield value
    elif isinstance(value, dict):
        for item in value.values():
            yield from string_values(item)
    elif isinstance(value, list):
        for item in value:
            yield from string_values(item)


def send(run, protocol, body):
    path = "/v1/chat/completions" if protocol == "chat" else "/v1/responses"
    request = Request(run.url + path, json.dumps(body, ensure_ascii=False).encode(),
                      {"Authorization": "Bearer " + CLIENT_KEY, "Content-Type": "application/json"})
    try:
        response = HTTP.open(request, timeout=15)
    except HTTPError as error:
        response = error
    with response:
        return response.status, response.read(128 * 1024 + 1)


def probe(core):
    checks = []
    provider = ContextProvider()
    with Run(core, retry=2, provider=provider) as run:
        # Both client protocols use the actual Responses-to-Chat bridge. The
        # direct Go transport fixture separately asserts byte-exact final input.
        for protocol in ("chat", "responses"):
            for stream in (False, True):
                for status in (400, 413, 422):
                    before = len(provider.snapshot())
                    provider.status = status
                    provider.error_body = json.dumps({"error": {
                        "type": "invalid_request_error", "code": "context_length_exceeded",
                        "param": "messages", "message": "synthetic provider detail 🐈 " * 1024,
                        "debug": "synthetic diagnostic must not reach client"}}, ensure_ascii=False).encode()
                    body = request_body(protocol, stream)
                    actual_status, raw = send(run, protocol, body)
                    expect(actual_status == status, f"{protocol}/{stream}: upstream status changed")
                    expect(len(raw) <= 1024, "context error was not bounded")
                    error = json.loads(raw)["error"]
                    expect(error["code"] == "context_length_exceeded" and error["param"] == "messages",
                           "known context code/parameter lost")
                    expect("Shorten the conversation" in error["message"], "no actionable context explanation")
                    expect("provider detail" not in raw.decode() and "debug" not in raw.decode(),
                           "upstream diagnostic leaked through client envelope")
                    sent = provider.snapshot()[before:]
                    expect(len(sent) == 1, "rejected request retried or routed to another account")
                    wire = sent[0]["body"]
                    leaves = list(string_values(wire))
                    expect(PROMPT in leaves and TOOL_RESULT in leaves and "complete arguments" in " ".join(leaves),
                           "conversation/tool continuation was shortened")
                    expect(wire.get("tools") == TOOLS, "complete toolset/schema changed")
                    expect(wire.get("model") == MODEL and wire.get("stream") == stream,
                           "selected model/stream intent changed")
                    expect(wire.get("max_tokens", wire.get("max_completion_tokens")) == 2048,
                           "output budget changed")
                    checks.append({"protocol": protocol, "stream": stream, "status": status,
                                   "upstreamRequests": len(sent), "wireRequestBytes": sent[0]["bytes"],
                                   "clientErrorBytes": len(raw), "contextCodePreserved": True,
                                   "completeConversationAndTools": True})

        for kind in ("oversize", "unrelated-small"):
            provider.status = 400
            provider.error_body = (b'{"error":{"code":"context_length_exceeded","message":"' + b"x" * (128 * 1024) + b'"}}'
                                   if kind == "oversize" else b' {"error":{"code":"other_error","message":"synthetic unrelated error"}}\n')
            before = len(provider.snapshot())
            status, raw = send(run, "chat", request_body("chat", False))
            expect(status == 400, "diagnostic handling changed rejection status")
            if kind == "oversize":
                error = json.loads(raw)["error"]
                expect(len(raw) <= 1024 and error["code"] == "error_details_too_large" and "param" not in error,
                       "oversized error not generic/bounded")
            else:
                expect(json.loads(raw) == json.loads(provider.error_body), "small unrelated error semantics changed")
            expect(len(provider.snapshot()) - before == 1, "diagnostic case dispatched multiple times")
            checks.append({"case": kind, "status": status, "clientErrorBytes": len(raw), "passed": True})
    return checks


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--core", required=True, type=Path)
    parser.add_argument("--report", required=True, type=Path)
    args = parser.parse_args()
    core = args.core.resolve(strict=True)
    before = hashlib.sha256(core.read_bytes()).hexdigest()
    report = {"syntheticOnly": True, "coreSha256": before, "checks": [], "passed": False}
    try:
        report["checks"] = probe(core)
        report["binaryUnchanged"] = before == hashlib.sha256(core.read_bytes()).hexdigest()
        report["passed"] = report["binaryUnchanged"]
    except Exception as error:
        report["error"] = type(error).__name__
        report["detail"] = str(error) if isinstance(error, AssertionError) else "isolated probe execution failed"
    args.report.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
