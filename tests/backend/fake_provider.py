#!/usr/bin/env python3
"""Loopback-only synthetic OpenAI fixture. Never reads real credentials.

The marker survives Core protocol translation; request headers need not survive.
Only synthetic account identity and scenario metadata are retained in observations.
"""
from __future__ import annotations

import argparse
import json
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

MODEL = "fixture-model"
KEYS = {"lain-synthetic-a": "account-a", "lain-synthetic-b": "account-b"}
ALL_KEYS = {**KEYS, "lain-synthetic-a-rotated": "account-a-rotated"}
USAGE = {"prompt_tokens": 11, "completion_tokens": 7, "total_tokens": 18}


def marker(case: str, request_id: str) -> str:
    return "LAIN_FIXTURE:" + json.dumps({"case": case, "id": request_id}, separators=(",", ":"))


def scenario(body: dict) -> tuple[str, str]:
    for message in body.get("messages", []):
        content = message.get("content", "")
        if isinstance(content, list):
            content = "".join(part.get("text", "") for part in content if isinstance(part, dict))
        if isinstance(content, str) and content.startswith("LAIN_FIXTURE:"):
            value = json.loads(content[len("LAIN_FIXTURE:"):])
            return value["case"], value["id"]
    return "text", "unmarked"


class FakeProvider:
    def __init__(self):
        self.lock = threading.Lock()
        self.usage_gate = threading.Event()
        self.observations: list[dict] = []
        self.server = ThreadingHTTPServer(("127.0.0.1", 0), self._handler())
        self.server.daemon_threads = True
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)

    @property
    def url(self):
        return f"http://127.0.0.1:{self.server.server_port}/v1"

    def __enter__(self):
        self.thread.start()
        return self

    def __exit__(self, *_):
        self.usage_gate.set()
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=3)

    def snapshot(self):
        with self.lock:
            return [dict(row) for row in self.observations]

    def _handler(self):
        owner = self

        class Handler(BaseHTTPRequestHandler):
            protocol_version = "HTTP/1.1"

            def log_message(self, *_):
                pass

            def reply(self, status, body):
                payload = json.dumps(body).encode()
                self.send_response(status)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(payload)))
                self.end_headers()
                self.wfile.write(payload)

            def do_GET(self):
                if self.path in ("/v1/models", "/models"):
                    return self.reply(200, {"object": "list", "data": [{"id": MODEL, "object": "model", "owned_by": "lain-fixture"}]})
                self.reply(404, {"error": {"message": "fixture endpoint not found"}})

            def do_POST(self):
                if self.path not in ("/v1/chat/completions", "/chat/completions"):
                    return self.reply(404, {"error": {"message": "fixture endpoint not found"}})
                account = ALL_KEYS.get(self.headers.get("Authorization", "").removeprefix("Bearer "))
                if account is None:
                    return self.reply(401, {"error": {"message": "synthetic key required"}})
                try:
                    body = json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))))
                    case, request_id = scenario(body)
                except (ValueError, KeyError, TypeError):
                    return self.reply(400, {"error": {"message": "invalid fixture input"}})
                tool_result = any(m.get("role") == "tool" for m in body.get("messages", []))
                row = {"account": account, "case": case, "request_id": request_id,
                       "model": body.get("model"), "stream": bool(body.get("stream")),
                       "tool_result": tool_result, "disconnected": False}
                with owner.lock:
                    previous = sum(r["account"] == account and r["request_id"] == request_id
                                   for r in owner.observations)
                    owner.observations.append(row)
                if (case == "rate_limit" and account == "account-a") or (case == "retry_once" and previous == 0):
                    row["status"] = 429
                    return self.reply(429, {"error": {"message": "synthetic quota exceeded", "type": "rate_limit_error", "code": "rate_limit_exceeded"}})
                row["status"] = 200
                usage = dict(USAGE)
                if case == "partial_usage":
                    usage.pop("total_tokens")
                elif case == "zero_usage":
                    usage = {key: 0 for key in usage}
                tool = case == "tool" and not tool_result
                message = {"role": "assistant", "content": "synthetic tool result accepted" if tool_result else "synthetic response"}
                if tool:
                    message = {"role": "assistant", "content": None, "tool_calls": [{"id": "call_fixture_1", "type": "function", "function": {"name": "fixture_echo", "arguments": '{"value":"safe"}'}}]}
                response = {"id": "chatcmpl-" + request_id, "object": "chat.completion", "created": 1700000000,
                            "model": MODEL, "choices": [{"index": 0, "message": message, "finish_reason": "tool_calls" if tool else "stop"}], "usage": usage}
                if not body.get("stream"):
                    return self.reply(200, response)
                self.send_response(200)
                self.send_header("Content-Type", "text/event-stream")
                self.send_header("Cache-Control", "no-cache")
                self.send_header("Connection", "close")
                self.end_headers()
                self.close_connection = True

                def event(delta, finish=None, usage=None):
                    chunk = {"id": response["id"], "object": "chat.completion.chunk", "created": 1700000000,
                             "model": MODEL, "choices": [{"index": 0, "delta": delta, "finish_reason": finish}]}
                    if usage is not None:
                        chunk["usage"] = usage
                        chunk["choices"] = []
                    self.wfile.write(b"data: " + json.dumps(chunk).encode() + b"\n\n")
                    self.wfile.flush()
                try:
                    event({"role": "assistant"})
                    if case == "cancel":
                        for _ in range(100):
                            event({"content": "synthetic "})
                            time.sleep(.02)
                    elif tool:
                        event({"tool_calls": [{"index": 0, **message["tool_calls"][0]}]})
                    else:
                        event({"content": message["content"]})
                    if case == "gated_usage":
                        owner.usage_gate.wait(timeout=8)
                    event({}, "tool_calls" if tool else "stop")
                    event({}, usage=usage)
                    self.wfile.write(b"data: [DONE]\n\n")
                    self.wfile.flush()
                except (BrokenPipeError, ConnectionResetError):
                    row["disconnected"] = True

        return Handler


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ready-file", help="write ephemeral URL only; keys are fixed synthetic fixture values")
    args = parser.parse_args()
    with FakeProvider() as provider:
        print(provider.url, flush=True)
        if args.ready_file:
            with open(args.ready_file, "w") as handle:
                json.dump({"url": provider.url}, handle)
        try:
            threading.Event().wait()
        except KeyboardInterrupt:
            pass


if __name__ == "__main__":
    main()
