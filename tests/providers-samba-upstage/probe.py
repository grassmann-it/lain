#!/usr/bin/env python3
"""Actual Core over Host-rendered synthetic SambaNova/Upstage routes. Loopback only."""
from __future__ import annotations

import argparse
import hashlib
import http.client
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import time
from urllib.error import HTTPError
from urllib.request import Request, urlopen

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "backend"))
import fake_provider
from fake_provider import FakeProvider, marker, scenario
from qualify import Run, assert_complete, expect


class Provider(FakeProvider):
    def __init__(self, model):
        self.model = model
        self.account_names = {"acct-fixture-a-key": "first", "acct-fixture-b-key": "second"}
        super().__init__(keys=self.account_names)

    def _handler(self):
        base = super()._handler()
        owner = self

        class Handler(base):
            def do_POST(self):
                payload = self.rfile.read(int(self.headers.get("Content-Length", 0)))
                body = json.loads(payload)
                case, identity = scenario(body)
                expect(body.get("model") == owner.model, "Pinned upstream model lost")
                expect(0 < body.get("max_tokens", 0) <= 64, "Request output was not bounded")
                expect(self.path == "/v1/chat/completions", "Chat transport used wrong path")
                status = {"unauthorized": 401, "forbidden": 403, "limited": 429, "missing_model": 404}.get(case)
                if status or case == "truncated":
                    key = self.headers.get("Authorization", "").removeprefix("Bearer ")
                    row = {"account": owner.account_names.get(key), "case": case, "request_id": identity,
                           "model": body.get("model"), "path": self.path, "status": status or 200, "disconnected": False}
                    with owner.lock:
                        owner.observations.append(row)
                    if status:
                        return self.reply(status, {"error": {"message": "Synthetic provider rejection", "type": "invalid_request_error", "code": "model_not_found" if status == 404 else str(status)}})
                    self.send_response(200)
                    self.send_header("Content-Type", "text/event-stream")
                    self.send_header("Connection", "close")
                    self.end_headers()
                    self.wfile.write(b'data: {"id":"truncated","object":"chat.completion.chunk","choices":[{"index":0,"delta":{"content":"partial"},"finish_reason":null}]}\n\n')
                    self.wfile.flush()
                    self.close_connection = True
                    return
                # The shared fixture reads once from rfile; no external socket or
                # provider URL is introduced by this replay of synthetic bytes.
                original = self.rfile
                self.rfile = io.BytesIO(payload)
                self.close_connection = True
                try:
                    return super().do_POST()
                finally:
                    self.rfile = original

        return Handler


class ProviderRun(Run):
    def __init__(self, core, exporter, dotnet, provider_id, model):
        fake_provider.MODEL = model
        super().__init__(core, provider=Provider(model))
        try:
            directory = self.root / "host-export"
            result = subprocess.run([dotnet, str(exporter), "--export", str(directory), "--provider", provider_id,
                                     "--provider-url", self.provider.url, "--proxy-port", str(self.port)],
                                    capture_output=True, text=True, timeout=45)
            expect(result.returncode == 0, "Host export failed: " + result.stdout + result.stderr)
            self.manifest = json.loads((directory / "fixture.json").read_text())
            expect(self.manifest["syntheticOnly"] and self.manifest["providerUrl"] == self.provider.url, "Export not bound to fixture")
            expect(self.manifest["providerId"] == provider_id and self.manifest["model"] == model, "Wrong provider export")
            for name in ("config", "bindings", "journal", "ownedAuthDir", "managementTokenFile", "inferenceTokenFile"):
                expect(Path(self.manifest[name]).is_relative_to(directory), "Export path outside fixture")
            self.journal = Path(self.manifest["journal"])
            self.management_key = Path(self.manifest["managementTokenFile"]).read_text().strip()
            self.client_key = Path(self.manifest["inferenceTokenFile"]).read_text().strip()
        except Exception:
            self.__exit__()
            raise

    def command(self):
        m = self.manifest
        return [self.core, "--config", m["config"], "--bindings", m["bindings"], "--journal", m["journal"],
                "--owned-auth-dir", m["ownedAuthDir"], "--management-token-file", m["managementTokenFile"]]

    def get(self, path):
        with urlopen(Request(self.url + path, headers={"Authorization": "Bearer " + self.management_key}), timeout=5) as response:
            return json.load(response)

    def body(self, case, *, stream=False, messages=None, alias="fixture-a"):
        body = {"model": alias, "messages": messages or [{"role": "user", "content": marker(case, case)}],
                "max_tokens": 64, "stream": stream}
        if stream:
            body["stream_options"] = {"include_usage": True}
        if case == "tool":
            body["tools"] = [{"type": "function", "function": {"name": "fixture_echo", "parameters": {"type": "object", "properties": {"value": {"type": "string"}}}}}]
        return body

    def send(self, body):
        return urlopen(Request(self.url + "/v1/chat/completions", json.dumps(body).encode(),
                       {"Authorization": "Bearer " + self.client_key, "Content-Type": "application/json"}), timeout=15)


def check(core, exporter, dotnet, provider_id, model):
    with ProviderRun(core, exporter, dotnet, provider_id, model) as run:
        with run.send(run.body("text")) as response:
            expect(json.load(response)["usage"]["total_tokens"] == 18, "Bounded Chat usage lost")
        with run.send(run.body("stream", stream=True)) as response:
            raw = response.read().decode()
        expect("[DONE]" in raw and "synthetic response" in raw, "Stream text or completion lost")
        messages = [{"role": "user", "content": marker("tool", "tool")}]
        with run.send(run.body("tool", stream=True, messages=messages)) as response:
            chunks = [json.loads(line[6:]) for line in response.read().decode().splitlines() if line.startswith("data: {")]
        calls = [chunk["choices"][0]["delta"]["tool_calls"][0] for chunk in chunks
                 if chunk.get("choices") and chunk["choices"][0].get("delta", {}).get("tool_calls")]
        expect(bool(calls), "Tool stream lost")
        call = {key: value for key, value in calls[0].items() if key != "index"}
        expect(json.loads(call["function"]["arguments"]) == {"value": "safe"}, "Tool arguments altered")
        messages.extend([{"role": "assistant", "content": None, "tool_calls": [call]}, {"role": "tool", "tool_call_id": call["id"], "content": "safe"}])
        with run.send(run.body("tool", stream=True, messages=messages)) as response:
            expect("synthetic tool result accepted" in response.read().decode(), "Tool continuation lost")
        starts, usages = assert_complete(run.events()["events"], 4)
        expect(all(row["accountId"] == run.manifest["accountA"] and row["credentialId"] == run.manifest["accounts"][run.manifest["accountA"]] for row in starts), "Account attribution changed")
        expect(all(row["model"] == model and row["totalTokens"] == 18 for row in usages), "Pinned model/usage history changed")
        for case, expected_status in (("unauthorized", 401), ("forbidden", 403), ("limited", 429), ("missing_model", 404)):
            before = run.events()["events"]
            try:
                with run.send(run.body(case)) as response:
                    response.read()
                raise AssertionError("Provider error unexpectedly succeeded")
            except HTTPError as error:
                expect(error.code == expected_status, f"{case}: wrong HTTP status {error.code}")
                raw = error.read().decode()
                expect("acct-fixture-a-key" not in raw and "acct-fixture-b-key" not in raw, "Error exposed credential")
            after = run.events()["events"][len(before):]
            failed_starts, failed_usage = assert_complete(after, 1)
            expect(all(row["accountId"] == run.manifest["accountA"] for row in failed_starts), "Error borrowed another account")
            expect(all(row["statusCode"] == expected_status and row["totalTokens"] is None for row in failed_usage), "Failure invented usage or lost status")
            # Authentication failures and 429 can legitimately cool down a key.
            # Restart the owned Core before the next independent error fixture.
            run.stop()
            run.start()
        count = len(run.provider.snapshot())
        try:
            with run.send(run.body("unknown", alias="nonexistent-model")) as response:
                response.read()
            raise AssertionError("Unknown public model unexpectedly succeeded")
        except HTTPError:
            expect(len(run.provider.snapshot()) == count, "Unknown model reached upstream")
        before = len(run.events()["events"])
        with run.send(run.body("truncated", stream=True)) as response:
            response.read()
        _, unknown_usage = assert_complete(run.events()["events"][before:], 1)
        expect(all(row["totalTokens"] is None for row in unknown_usage), "Incomplete stream invented provider usage")
        before = len(run.events()["events"])
        connection = http.client.HTTPConnection("127.0.0.1", run.port, timeout=10)
        connection.request("POST", "/v1/chat/completions", json.dumps(run.body("cancel", stream=True)),
                           {"Authorization": "Bearer " + run.client_key, "Content-Type": "application/json"})
        response = connection.getresponse()
        expect(response.status == 200 and b"data:" in response.readline(), "Cancel stream did not start")
        response.close()
        connection.close()
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline:
            events = run.events()["events"][before:]
            if any(row["kind"] == "usage" for row in events) and run.provider.snapshot()[-1]["disconnected"]:
                break
            time.sleep(.03)
        cancel_starts, cancel_usage = assert_complete(events, 1)
        expect(cancel_usage[0]["outcome"] == "canceled" and cancel_usage[0]["totalTokens"] is None, "Cancellation lost or invented usage")
        expect(cancel_starts[0]["accountId"] == run.manifest["accountA"] and run.provider.snapshot()[-1]["disconnected"], "Cancellation attribution/propagation lost")
        events = run.events()
        run.stop()
        run.start()
        expect(run.events() == events, "Restart changed durable provider attribution")
        return {"model": model, "chat": True, "stream": True, "tools": True, "errors": [401, 403, 429, 404],
                "unknownModelRejectedBeforeDispatch": True, "incompleteUsageUnknown": True,
                "cancellationAttributed": True, "restartReplayEqual": True, "liveVerification": "not_tested"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--core", required=True)
    parser.add_argument("--exporter", required=True, help="Already built SambaUpstage.Tests.dll")
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--report", required=True)
    args = parser.parse_args()
    core = Path(args.core).resolve(strict=True)
    exporter = Path(args.exporter).resolve(strict=True)
    report = {"syntheticOnly": True, "coreSha256": hashlib.sha256(core.read_bytes()).hexdigest(),
              "exporterSha256": hashlib.sha256(exporter.read_bytes()).hexdigest(), "checks": []}
    with tempfile.TemporaryDirectory(prefix="lain-samba-upstage-core-") as temporary:
        frozen = Path(temporary) / "lain-proxy"
        frozen.write_bytes(core.read_bytes())
        frozen.chmod(0o700)
        for provider_id, model in (("sambanova", "Meta-Llama-3.3-70B-Instruct"), ("upstage", "solar-mini4-260922")):
            try:
                evidence = check(frozen, exporter, args.dotnet, provider_id, model)
                report["checks"].append({"providerId": provider_id, "passed": True, "evidence": evidence})
            except Exception as error:
                report["checks"].append({"providerId": provider_id, "passed": False, "error": str(error)})
            print(f"{'PASS' if report['checks'][-1]['passed'] else 'FAIL'} {provider_id}", flush=True)
    report["passed"] = all(row["passed"] for row in report["checks"])
    Path(args.report).write_text(json.dumps(report, indent=2) + "\n")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
