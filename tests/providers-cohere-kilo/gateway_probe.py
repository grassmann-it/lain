#!/usr/bin/env python3
"""Real Core executor qualification against strict, loopback-only vendor contracts.

Uses the existing backend Run lifecycle/journal harness. Requires an explicitly
supplied Core binary; never locates an installed service or reads personal state.
"""
import argparse
import hashlib
import http.client
import json
from pathlib import Path
import sys
import time
from urllib.error import HTTPError
from urllib.request import Request, urlopen

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "backend"))
from fake_provider import FakeProvider, KEYS, USAGE, marker, scenario
from qualify import CLIENT_KEY, Run, assert_complete, expect

ROUTES = {
    "cohere": ("/compatibility/v1", "command-a-plus-05-2026"),
    "kilo": ("/api/v1", "google/gemma-4-26b-a4b-it:free"),
}
# Deliberately limited to the official Cohere compatibility parameter contract.
# A failure flags a required central executor boundary, never a vendor success.
COHERE_FIELDS = {"model", "messages", "stream", "reasoning_effort", "response_format",
                 "tools", "temperature", "max_tokens", "stop", "seed", "top_p",
                 "frequency_penalty", "presence_penalty"}
COHERE_RESPONSES_CASES = ("responses_json", "responses_stream", "responses_tool",
                          "responses_store_true", "responses_previous_response_id",
                          "responses_native_search", "responses_additional_native_search",
                          "responses_native_file_search")
FUNCTION_PARAMETERS = {"type": "object", "properties": {"value": {"type": "string"}},
                       "required": ["value"]}


def message_text(message):
    content = message.get("content", "")
    return content if isinstance(content, str) else "".join(part.get("text", "") for part in content or [])


class ContractProvider(FakeProvider):
    def __init__(self, provider):
        self.provider = provider
        self.prefix, self.model = ROUTES[provider]
        self.contract_errors = []
        super().__init__(keys=KEYS)

    @property
    def url(self):
        return f"http://127.0.0.1:{self.server.server_port}" + self.prefix

    def _handler(self):
        owner = self
        base = super()._handler()

        class Handler(base):
            def do_POST(self):
                body = json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))))
                case, request_id = scenario(body)
                key = self.headers.get("Authorization", "").removeprefix("Bearer ")
                row = {"case": case, "request_id": request_id, "account": KEYS.get(key),
                       "model": body.get("model"), "path": self.path, "disconnected": False}
                with owner.lock:
                    owner.observations.append(row)
                errors = []
                if self.path != owner.prefix + "/chat/completions":
                    errors.append("incorrect vendor path")
                if key not in KEYS or self.headers.get("x-api-key") or self.headers.get("X-KiloCode-OrganizationId"):
                    errors.append("incorrect personal Bearer scope")
                if body.get("model") != owner.model:
                    errors.append("model pin changed")
                if body.get("max_tokens") != 64:
                    errors.append("bounded max_tokens lost")
                if owner.provider == "cohere":
                    if set(body) - COHERE_FIELDS:
                        errors.append("unsupported Cohere fields: " + ",".join(sorted(set(body) - COHERE_FIELDS)))
                    if "reasoning_effort" in body and body["reasoning_effort"] not in ("none", "high"):
                        errors.append("unsupported Cohere reasoning effort")
                    if case.startswith("responses_"):
                        if body.get("reasoning_effort") != "none":
                            errors.append("explicit Responses reasoning none lost")
                        row["statelessChat"] = "messages" in body and "store" not in body and "input" not in body and "previous_response_id" not in body
                        row["reasoningNone"] = body.get("reasoning_effort") == "none"
                        row["bounded64"] = body.get("max_tokens") == 64
                        if case == "responses_tool":
                            expected_tool = {"type": "function", "function": {"name": "fixture_echo", "description": "fixture echo",
                                                                            "parameters": FUNCTION_PARAMETERS}}
                            if body.get("tools") != [expected_tool]:
                                errors.append("Responses function definition/schema changed")
                            messages = body.get("messages", [])
                            tool_messages = [m for m in messages if m.get("role") == "tool"]
                            if tool_messages:
                                calls = [call for m in messages if m.get("role") == "assistant" for call in m.get("tool_calls", [])]
                                valid = (len(messages) == 3 and [m.get("role") for m in messages] == ["user", "assistant", "tool"]
                                         and message_text(messages[0]) == marker("responses_tool", "responses_tool")
                                         and len(calls) == 1 and calls[0].get("id") == "call_owned"
                                         and calls[0].get("type") == "function"
                                         and calls[0].get("function") == {"name": "fixture_echo", "arguments": '{"value":"safe"}'}
                                         and len(tool_messages) == 1 and tool_messages[0].get("tool_call_id") == "call_owned"
                                         and tool_messages[0].get("content") == "result-safe")
                                row["completeFunctionHistory"] = valid
                                if not valid:
                                    errors.append("full Responses function/result history changed")
                if errors:
                    with owner.lock:
                        owner.contract_errors.extend(errors)
                    return self.reply(400, {"error": {"message": "strict synthetic contract rejected payload"}})
                if case.startswith("status_"):
                    code = int(case.removeprefix("status_"))
                    return self.reply(code, {"error": {"message": "synthetic vendor refusal", "code": code}})
                tool_result = any(m.get("role") == "tool" for m in body.get("messages", []))
                tool = case in ("tool", "responses_tool") and not tool_result
                message = {"role": "assistant", "content": "synthetic tool result accepted" if tool_result else "synthetic response"}
                if tool:
                    message = {"role": "assistant", "content": None, "tool_calls": [{"id": "call_owned", "type": "function",
                               "function": {"name": "fixture_echo", "arguments": '{"value":"safe"}'}}]}
                response = {"id": "chatcmpl-" + request_id, "object": "chat.completion", "created": 1700000000,
                            "model": owner.model, "choices": [{"index": 0, "message": message,
                            "finish_reason": "tool_calls" if tool else "stop"}], "usage": USAGE}
                if not body.get("stream"):
                    return self.reply(200, response)
                self.send_response(200)
                self.send_header("Content-Type", "text/event-stream")
                self.send_header("Connection", "close")
                self.end_headers()
                self.close_connection = True

                def event(delta, finish=None, usage=None):
                    payload = {"id": response["id"], "object": "chat.completion.chunk", "created": 1700000000,
                               "model": owner.model, "choices": [{"index": 0, "delta": delta, "finish_reason": finish}]}
                    if usage is not None:
                        payload["usage"] = usage
                        payload["choices"] = []
                    self.wfile.write(b"data: " + json.dumps(payload).encode() + b"\n\n")
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
                    if case == "truncated":
                        return
                    event({}, "tool_calls" if tool else "stop")
                    event({}, usage=USAGE)
                    self.wfile.write(b"data: [DONE]\n\n")
                    self.wfile.flush()
                except (BrokenPipeError, ConnectionResetError):
                    row["disconnected"] = True
        return Handler


class ProviderRun(Run):
    def __init__(self, core, provider):
        fixture = ContractProvider(provider)
        super().__init__(core, provider=fixture)
        kind = "openai-compatibility:" + provider + "-openai-chat"
        self.accounts = {account: kind + ":" + hashlib.sha256(
            (kind + "\0" + key + "\0" + fixture.url + "\0").encode()).hexdigest()[:12]
            for key, account in KEYS.items()}
        self.bindings = {"accounts": self.accounts, "routes": {"owned-alias": {
            "id": provider + "-owned-route", "model": fixture.model,
            "accounts": ["account-a"], "selection": "strict"}}}
        self.write_bindings()
        self.provider_id = provider
        config = self.root / "core.yaml"
        # Match whole YAML lines: replacing the provider-name prefix also
        # changes "name: fixture-model" before its concrete-model substitution.
        text = config.read_text().replace("  - name: fixture\n", "  - name: " + provider + "-openai-chat\n")
        text = text.replace("      - name: fixture-model\n", "      - name: " + fixture.model + "\n")
        # Match Host writer: public aliases live in bindings; Core model names
        # and aliases both identify the concrete upstream model.
        text = text.replace("        alias: fixture-model\n", "        alias: " + fixture.model + "\n")
        text = text.replace("    base-url:", "    upstream-protocol: openai-chat\n    base-url:")
        config.write_text(text)

    def request(self, case, *, stream=False, messages=None, tools=False):
        body = {"model": "owned-alias", "messages": messages or [{"role": "user", "content": marker(case, case)}],
                "max_tokens": 64, "stream": stream}
        if tools:
            body["tools"] = [{"type": "function", "function": {"name": "fixture_echo", "parameters": {
                "type": "object", "properties": {"value": {"type": "string"}}, "required": ["value"]}}}]
        return Request(self.url + "/v1/chat/completions", json.dumps(body).encode(),
                       {"Authorization": "Bearer " + CLIENT_KEY, "Content-Type": "application/json"})

    def responses_request(self, case, *, stream=False, inputs=None, tools=None, controls=None):
        body = {"model": "owned-alias", "input": inputs if inputs is not None else [
            {"role": "user", "content": [{"type": "input_text", "text": marker(case, case)}]}],
            "store": False, "reasoning": {"effort": "none"}, "max_output_tokens": 64, "stream": stream}
        if tools is not None:
            body["tools"] = tools
        if controls:
            body.update(controls)
        return Request(self.url + "/v1/responses", json.dumps(body).encode(),
                       {"Authorization": "Bearer " + CLIENT_KEY, "Content-Type": "application/json"})


def complete_response(request, *, stream=False):
    with urlopen(request, timeout=10) as response:
        expect(response.status == 200, "Responses request did not return HTTP 200")
        expect(("text/event-stream" if stream else "application/json") in response.headers.get("Content-Type", ""),
               "Responses content type changed")
        payload = response.read()
    if stream:
        events = [json.loads(line[5:].strip()) for line in payload.decode().splitlines()
                  if line.startswith("data:") and line[5:].strip() and line[5:].strip() != "[DONE]"]
        expect(not any(e.get("type") in ("error", "response.error", "response.failed") for e in events),
               "Responses SSE contains a terminal error")
        completed = [e for e in events if e.get("type") == "response.completed"]
        expect(len(completed) == 1 and completed[0] is events[-1], "Responses SSE lacks one final completion")
        result = completed[0].get("response", {})
    else:
        result = json.loads(payload)
    expect(result.get("object") == "response" and result.get("status") == "completed" and result.get("error") is None,
           "Responses protocol did not complete successfully")
    return result


def response_text(response):
    return "".join(part.get("text", "") for item in response.get("output", []) if item.get("type") == "message"
                   for part in item.get("content", []) if part.get("type") == "output_text")


def probe_cohere_responses(run, case):
    if case in ("responses_json", "responses_stream", "responses_tool"):
        streaming = case in ("responses_stream", "responses_tool")
        tools = [{"type": "function", "name": "fixture_echo", "description": "fixture echo",
                  "parameters": FUNCTION_PARAMETERS}] if case == "responses_tool" else None
        response = complete_response(run.responses_request(case, stream=streaming, tools=tools), stream=streaming)
        requests = 1
        if case == "responses_tool":
            calls = [item for item in response.get("output", []) if item.get("type") == "function_call"]
            expect(len(calls) == 1 and calls[0].get("call_id") == "call_owned" and calls[0].get("name") == "fixture_echo"
                   and calls[0].get("arguments") == '{"value":"safe"}', "streamed Responses function call changed")
            history = [{"role": "user", "content": [{"type": "input_text", "text": marker(case, case)}]}, calls[0],
                       {"type": "function_call_output", "call_id": "call_owned", "output": "result-safe"}]
            response = complete_response(run.responses_request(case, inputs=history, tools=tools))
            expect("tool result accepted" in response_text(response), "Responses function continuation lost")
            requests = 2
        else:
            expect(response_text(response) == "synthetic response", "Responses text output changed")
        observations = run.provider.snapshot()
        expect(len(observations) == requests and all(row.get("statelessChat") and row.get("reasoningNone") and row.get("bounded64") for row in observations),
               "Responses intent did not reach the bounded stateless Chat wire")
        if case == "responses_tool":
            expect(observations[-1].get("completeFunctionHistory") is True, "complete function history was not observed upstream")
        events = run.events()
        _, usages = assert_complete(events["events"], requests)
        expect(all(u["accountId"] == "account-a" and u["routeId"] == "cohere-owned-route" for u in usages), "Responses attribution changed")
        expect(all(u.get("inputTokens") == 11 and u.get("outputTokens") == 7 and u.get("totalTokens") == 18 for u in usages),
               "Responses token coverage changed")
        finished = [e for e in events["events"] if e["kind"] == "attempt_finished"]
        expect(all(e.get("completionState") == "success" for e in finished), "completed Responses attempt persisted failure")
        run.stop()
        run.start()
        expect(run.events() == events, "Responses replay/source identity changed after restart")
        return {"statelessChat": True, "reasoningNone": True, "maxTokens": 64,
                "fullResponseCompletion": True, "completeFunctionHistory": case == "responses_tool", "durableReplay": True}
    controls, tools = {}, None
    if case == "responses_store_true":
        controls = {"store": True}
    elif case == "responses_previous_response_id":
        controls = {"previous_response_id": "resp_prior"}
    elif case == "responses_native_search":
        tools = [{"type": "web_search"}]
    elif case == "responses_additional_native_search":
        controls = {"input": [{"role": "user", "content": marker(case, case)},
                              {"type": "additional_tools", "tools": [{"type": "web_search"}]}]}
    elif case == "responses_native_file_search":
        tools = [{"type": "file_search", "vector_store_ids": ["vs_synthetic"]}]
    else:
        raise AssertionError("unknown Cohere Responses case")
    try:
        with urlopen(run.responses_request(case, tools=tools, controls=controls), timeout=10) as response:
            response.read()
        raise AssertionError("unsupported Responses intent became success")
    except HTTPError as error:
        expect(error.code == 400, "unsupported Responses intent did not return bounded HTTP 400")
        expect(len(error.read(65537)) <= 65536, "local Responses rejection is unbounded")
        error.close()
    expect(not run.provider.snapshot(), "unsupported Responses intent reached provider")
    events = run.events()
    _, usages = assert_complete(events["events"], 1)
    expect(usages[0].get("outcome") == "failed" and usages[0].get("completionState") == "failed"
           and usages[0].get("statusCode") == 400 and usages[0].get("totalTokens") is None,
           "local Responses rejection lacks attributed failed/unknown-usage history")
    expect(usages[0].get("accountId") == "account-a" and usages[0].get("routeId") == "cohere-owned-route",
           "local Responses rejection lost account/route attribution")
    run.stop()
    run.start()
    expect(run.events() == events, "local Responses rejection changed after restart")
    return {"rejectedBeforeProvider": True, "statusCode": 400, "durableFailure": True, "durableReplay": True}


def probe(core, provider, case):
    with ProviderRun(core, provider) as run:
        responses_evidence = {}
        if case.startswith("responses_"):
            expect(provider == "cohere", "Responses extension is only qualified for Cohere")
            responses_evidence = probe_cohere_responses(run, case)
        elif case in ("json", "stream", "tool"):
            streaming = case == "stream"
            with urlopen(run.request("tool" if case == "tool" else "text", stream=streaming, tools=case == "tool"), timeout=10) as response:
                payload = response.read()
            expect(not run.provider.contract_errors, "; ".join(run.provider.contract_errors))
            expect(b"synthetic" in payload or b"fixture_echo" in payload, "response/tool did not cross Core")
            requests = 1
            if case == "tool":
                message = json.loads(payload)["choices"][0]["message"]
                messages = [{"role": "user", "content": marker("tool", "tool-result")}, message,
                            {"role": "tool", "tool_call_id": "call_owned", "content": "safe"}]
                with urlopen(run.request("tool", messages=messages), timeout=10) as response:
                    expect(b"tool result accepted" in response.read(), "tool continuation was lost")
                requests = 2
            events = run.events()
            _, usages = assert_complete(events["events"], requests)
            expect(all(u["accountId"] == "account-a" and u["routeId"] == provider + "-owned-route" for u in usages), "usage attributed to wrong account/route")
            expect(all(u["inputTokens"] == 11 and u["outputTokens"] == 7 and u["totalTokens"] == 18 for u in usages), "reported token coverage lost")
            expect(all(u.get("cost") is None for u in usages), "fixture invented a monetary charge")
            run.stop()
            run.start()
            expect(run.events() == events, "restart changed usage/source identity")
        elif case.startswith("status_"):
            code = int(case.removeprefix("status_"))
            try:
                with urlopen(run.request(case), timeout=10) as response:
                    response.read()
                raise AssertionError("refusal became successful completion")
            except HTTPError as error:
                expect(error.code == code, "vendor refusal status changed")
                error.close()
            expect(len(run.provider.snapshot()) == 1, "refusal triggered fallback/retry or another account")
            _, usages = assert_complete(run.events()["events"], 1)
            expect(all(u.get("totalTokens") is None for u in usages), "refusal invented token usage")
        elif case in ("cancel", "truncated"):
            connection = http.client.HTTPConnection("127.0.0.1", run.port, timeout=10)
            request = run.request(case, stream=True)
            connection.request("POST", "/v1/chat/completions", request.data, dict(request.header_items()))
            response = connection.getresponse()
            expect(response.status == 200 and b"data:" in response.readline(), "stream did not start")
            if case == "truncated":
                response.read()
            response.close()
            connection.close()
            for _ in range(80):
                events = run.events()["events"]
                if any(e["kind"] in ("usage", "attempt_finished") for e in events):
                    break
                time.sleep(.05)
            _, usages = assert_complete(events, 1)
            expect(usages[0].get("totalTokens") is None, "unfinished stream invented token usage")
            expect(usages[0].get("outcome") == ("canceled" if case == "cancel" else "failed"), "unfinished stream became success")
        expect(not run.provider.contract_errors, "; ".join(run.provider.contract_errors))
        journal = json.dumps(run.events())
        expect(all(key not in journal for key in KEYS), "synthetic credential escaped into durable history")
        expect(all(row["account"] == "account-a" for row in run.provider.snapshot()), "strict account scope changed")
        return {"upstreamCalls": len(run.provider.snapshot()), "vendorPath": ROUTES[provider][0] + "/chat/completions", "liveProvider": False,
                **responses_evidence}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--core", required=True)
    parser.add_argument("--report", required=True)
    args = parser.parse_args()
    digest = hashlib.sha256(Path(args.core).read_bytes()).hexdigest()
    checks = []
    for provider in ROUTES:
        # Kilo tool behavior here proves transport preservation only, not Gemma support.
        for case in ("json", "stream", "tool", "status_401", "status_403", "status_429", "status_404", "cancel", "truncated",
                     *(["status_402"] if provider == "kilo" else []), *(COHERE_RESPONSES_CASES if provider == "cohere" else ())):
            try:
                checks.append({"provider": provider, "case": case, "passed": True, "evidence": probe(args.core, provider, case)})
            except Exception as error:
                checks.append({"provider": provider, "case": case, "passed": False, "error": type(error).__name__,
                               "detail": str(error) if isinstance(error, AssertionError) else "synthetic gateway probe failed"})
    unchanged = digest == hashlib.sha256(Path(args.core).read_bytes()).hexdigest()
    report = {"syntheticOnly": True, "coreSha256": digest, "binaryUnchanged": unchanged,
              "passed": unchanged and all(c["passed"] for c in checks), "checks": checks}
    Path(args.report).write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
