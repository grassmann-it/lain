"""Fixture self-checks only. These do not qualify the CLIProxyAPI backend."""
import concurrent.futures
import http.client
import json
import time
import unittest
from urllib.error import HTTPError
from urllib.parse import urlsplit
from urllib.request import Request, urlopen

from fake_provider import FakeProvider, KEYS, MODEL, USAGE, marker


def request(url, key, case, request_id, *, stream=False, messages=None):
    body = {"model": MODEL, "messages": messages or [{"role": "user", "content": marker(case, request_id)}], "stream": stream}
    if stream:
        body["stream_options"] = {"include_usage": True}
    return urlopen(Request(url + "/chat/completions", json.dumps(body).encode(),
                           {"Authorization": "Bearer " + key, "Content-Type": "application/json"}), timeout=10)


class ProviderFixtureTests(unittest.TestCase):
    def setUp(self):
        self.provider = FakeProvider().__enter__()

    def tearDown(self):
        self.provider.__exit__()

    def test_two_distinct_credentials_same_alias_100_each(self):
        def one(item):
            key, i = item
            with request(self.provider.url, key, "text", f"{KEYS[key]}-{i}") as response:
                body = json.load(response)
                self.assertEqual(body["usage"], USAGE)
                self.assertEqual(body["model"], MODEL)
        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as executor:
            list(executor.map(one, ((key, i) for key in KEYS for i in range(100))))
        rows = self.provider.snapshot()
        self.assertEqual(len(rows), 200)
        for account in KEYS.values():
            self.assertEqual(sum(row["account"] == account for row in rows), 100)
        serialized = json.dumps(rows)
        for key in KEYS:
            self.assertNotIn(key, serialized)

    def test_native_responses_tool_continuation_and_endpoint_observation(self):
        def send(body):
            return urlopen(Request(self.provider.url + "/responses", json.dumps(body).encode(),
                                   {"Authorization": "Bearer lain-synthetic-a", "Content-Type": "application/json"}), timeout=10)
        inputs = [{"role": "user", "content": [{"type": "input_text", "text": marker("tool", "native-tool")}]}]
        with send({"model": MODEL, "input": inputs, "stream": True}) as response:
            events = [json.loads(line[6:]) for line in response.read().decode().splitlines() if line.startswith("data: {")]
        self.assertEqual([event["sequence_number"] for event in events], list(range(len(events))))
        self.assertEqual(events[-1]["type"], "response.completed")
        call = events[-1]["response"]["output"][0]
        self.assertEqual(call["type"], "function_call")
        self.assertEqual(json.loads(call["arguments"]), {"value": "safe"})
        inputs.extend([call, {"type": "function_call_output", "call_id": call["call_id"], "output": "safe"}])
        with send({"model": MODEL, "input": inputs}) as response:
            completed = json.load(response)
        self.assertEqual(completed["output"][0]["content"][0]["text"], "synthetic tool result accepted")
        self.assertEqual(completed["usage"]["total_tokens"], 18)
        rows = self.provider.snapshot()
        self.assertTrue(rows[-1]["tool_result"])
        self.assertTrue(all(row["path"] == "/v1/responses" for row in rows))

    def test_native_gemini_reports_header_presence_without_recording_key(self):
        with FakeProvider(keys={"synthetic-gemini": "gemini-account"}) as provider:
            body = {"contents": [{"role": "user", "parts": [{"text": marker("text", "native-gemini")}]}]}
            endpoint = provider.url.removesuffix("/v1") + "/v1beta/models/fixture-gemini:generateContent"
            with urlopen(Request(endpoint, json.dumps(body).encode(),
                                 {"Authorization": "Bearer synthetic-gemini", "Content-Type": "application/json"}), timeout=10) as response:
                result = json.load(response)
            self.assertEqual(result["usageMetadata"]["totalTokenCount"], 18)
            row = provider.snapshot()[0]
            self.assertTrue(row["bearerAuthorization"])
            self.assertFalse(row["googleApiKeyPresent"])
            self.assertEqual(row["request_id"], "native-gemini")
            self.assertNotIn("synthetic-gemini", json.dumps(row))

    def test_sse_usage_tool_continuation(self):
        messages = [{"role": "user", "content": marker("tool", "tool-1")}]
        with request(self.provider.url, "lain-synthetic-a", "tool", "tool-1", stream=True, messages=messages) as response:
            events = [json.loads(line[6:]) for line in response.read().decode().splitlines() if line.startswith("data: {")]
        call = next(event["choices"][0]["delta"]["tool_calls"][0] for event in events if event["choices"] and "tool_calls" in event["choices"][0]["delta"])
        self.assertEqual(call["function"]["name"], "fixture_echo")
        self.assertEqual(events[-1]["usage"], USAGE)
        del call["index"]
        messages.extend([{"role": "assistant", "content": None, "tool_calls": [call]},
                         {"role": "tool", "tool_call_id": call["id"], "content": "safe"}])
        with request(self.provider.url, "lain-synthetic-a", "tool", "tool-1-continued", messages=messages) as response:
            body = json.load(response)
        self.assertEqual(body["choices"][0]["message"]["content"], "synthetic tool result accepted")
        self.assertTrue(self.provider.snapshot()[-1]["tool_result"])

    def test_rate_limit_and_retry_once_are_deterministic(self):
        for case in ("rate_limit", "retry_once"):
            with self.assertRaises(HTTPError) as error:
                request(self.provider.url, "lain-synthetic-a", case, case)
            self.assertEqual(error.exception.code, 429)
            error.exception.close()
        with request(self.provider.url, "lain-synthetic-a", "retry_once", "retry_once") as response:
            self.assertEqual(response.status, 200)
        with request(self.provider.url, "lain-synthetic-b", "rate_limit", "rate_limit") as response:
            self.assertEqual(response.status, 200)

    def test_cancel_disconnect_is_observable(self):
        parsed = urlsplit(self.provider.url)
        connection = http.client.HTTPConnection(parsed.hostname, parsed.port, timeout=5)
        body = json.dumps({"model": MODEL, "stream": True, "messages": [{"role": "user", "content": marker("cancel", "cancel-1")}]})
        connection.request("POST", "/v1/chat/completions", body, {"Authorization": "Bearer lain-synthetic-a", "Content-Type": "application/json"})
        response = connection.getresponse()
        self.assertEqual(response.status, 200)
        self.assertIn(b"data:", response.readline())
        response.close()
        connection.close()
        deadline = time.monotonic() + 3
        while time.monotonic() < deadline and not self.provider.snapshot()[0]["disconnected"]:
            time.sleep(.02)
        self.assertTrue(self.provider.snapshot()[0]["disconnected"])


if __name__ == "__main__":
    unittest.main()
