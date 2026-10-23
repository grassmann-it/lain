import contextlib
import datetime as dt
import hashlib
import io
import json
import pathlib
import tempfile
import unittest
from unittest import mock

import reference_probe as probe


def stream(output):
    response = {"status": "completed", "output": output, "usage": {"input_tokens": 2, "output_tokens": 3, "total_tokens": 5}}
    result = io.BytesIO(("data: " + json.dumps({"type": "response.completed", "response": response}) + "\n\ndata: [DONE]\n\n").encode())
    result.headers = {"Content-Type": "text/event-stream"}
    return result


class ProbeTests(unittest.TestCase):
    def test_default_plan_never_opens_network(self):
        with mock.patch("sys.argv", ["probe"]), mock.patch.object(probe, "open_request", side_effect=AssertionError("network")), contextlib.redirect_stdout(io.StringIO()) as output:
            self.assertEqual(0, probe.main())
        self.assertEqual(4, len(json.loads(output.getvalue())["plan"]))

    def test_gate_requires_exact_qualified_hash(self):
        with tempfile.TemporaryDirectory() as directory:
            binary = pathlib.Path(directory) / "lain-proxy"
            binary.write_bytes(b"synthetic")
            sha = hashlib.sha256(b"synthetic").hexdigest()
            binary.with_name("binary-manifest.json").write_text(json.dumps({"sha256": sha}))
            binary.with_name("qualification.json").write_text(json.dumps({"sha256": sha, "passed": False}))
            with self.assertRaises(probe.ProbeError):
                probe.validate_gate(binary)
            binary.with_name("qualification.json").write_text(json.dumps({"sha256": sha, "passed": True}))
            self.assertEqual(sha, probe.validate_gate(binary))
            binary.with_name("qualification.json").write_text(json.dumps({"sha256": sha, "passed": False, "syntheticPassed": True}))
            self.assertEqual(sha, probe.validate_gate(binary))
            binary.write_bytes(b"changed")
            with self.assertRaises(probe.ProbeError):
                probe.validate_gate(binary)

    def test_missing_gate_cannot_reach_network(self):
        with tempfile.TemporaryDirectory() as directory:
            binary = pathlib.Path(directory) / "lain-proxy"
            binary.write_bytes(b"synthetic")
            with mock.patch("sys.argv", ["probe", "--execute", "--lain-home", directory, "--proxy-binary", str(binary)]), mock.patch.object(probe, "open_request", side_effect=AssertionError("network")), contextlib.redirect_stdout(io.StringIO()) as output:
                self.assertEqual(1, probe.main())
            self.assertEqual([], json.loads(output.getvalue())["calls"])

    def test_pair_is_two_calls_and_literal_tool_output_only(self):
        tool = {"type": "function_call", "name": "echo_probe", "call_id": "synthetic-call", "arguments": '{"text":"ok"}'}
        message = {"type": "message", "content": [{"type": "output_text", "text": "ok"}]}
        report = {"calls": []}
        with mock.patch.object(probe, "open_request", side_effect=[stream([tool]), stream([message])]) as request:
            probe.run_pair("route", "account", "not-a-real-token", report)
        self.assertEqual(2, request.call_count)
        continuation = request.call_args.args[2]
        self.assertEqual({"type": "function_call_output", "call_id": "synthetic-call", "output": "ok"}, continuation["input"][-1])
        self.assertEqual("none", continuation["tool_choice"])
        self.assertEqual(["passed", "passed"], [c["state"] for c in report["calls"]])
        self.assertNotIn("not-a-real-token", json.dumps(report))
        self.assertNotIn("synthetic-call", json.dumps(report))

    def test_unapproved_tool_never_continues(self):
        output = [{"type": "function_call", "name": "shell", "call_id": "call", "arguments": '{"command":"never run"}'}]
        report = {"calls": []}
        with mock.patch.object(probe, "open_request", return_value=stream(output)) as request:
            with self.assertRaises(probe.ProbeError):
                probe.run_pair("route", "account", "token", report)
        self.assertEqual(1, request.call_count)

    def test_expired_or_pooled_accounts_are_refused(self):
        config = {"routes": [{"alias": "route", "accountIds": ["a"], "upstreamModel": probe.MODEL, "enabled": True}],
                  "accounts": [{"id": "a", "provider": "codex", "credentialKind": "codex-access-snapshot", "enabled": True,
                                "sharedWithLegacy": True, "expiresAt": "2020-01-01T00:00:00Z"}]}
        with self.assertRaises(probe.ProbeError):
            probe.route_account(config, "route", True)
        config["accounts"][0]["expiresAt"] = (dt.datetime.now(dt.timezone.utc) + dt.timedelta(days=1)).isoformat()
        self.assertEqual("a", probe.route_account(config, "route", True))
        config["routes"][0]["pooled"] = True
        with self.assertRaises(probe.ProbeError):
            probe.route_account(config, "route", True)

    def test_raw_provider_error_is_not_propagated(self):
        response = io.BytesIO(b'data: {"type":"error","message":"sensitive provider content"}\n\n')
        with self.assertRaises(probe.ProbeError) as error:
            probe.parse_sse(response)
        self.assertNotIn("sensitive", str(error.exception))

    def test_truthful_client_user_agent_is_emitted(self):
        with mock.patch.object(probe.OPENER, "open", return_value=io.BytesIO()) as opened:
            probe.open_request(probe.PROXY, "synthetic-token", {"model": "synthetic"}, "synthetic-account")
        request = opened.call_args.args[0]
        self.assertEqual("lain-reference-probe/0.1.0", request.get_header("User-agent"))
        self.assertEqual("Bearer synthetic-token", request.get_header("Authorization"))


if __name__ == "__main__":
    unittest.main()
