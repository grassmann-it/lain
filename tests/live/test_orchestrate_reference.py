import contextlib
import io
import json
import types
import unittest
from unittest import mock

import orchestrate_reference as orchestration


class OrchestrationTests(unittest.TestCase):
    def test_default_has_no_process_or_network(self):
        with mock.patch("sys.argv", ["orchestration"]), mock.patch.object(orchestration, "api", side_effect=AssertionError("network")), mock.patch.object(orchestration.subprocess, "Popen", side_effect=AssertionError("process")), contextlib.redirect_stdout(io.StringIO()) as output:
            self.assertEqual(0, orchestration.main())
        self.assertEqual(4, len(json.loads(output.getvalue())["plan"]))

    def test_execution_requires_explicit_approved_sha(self):
        with mock.patch("sys.argv", ["orchestration", "--execute"]), mock.patch.object(orchestration.subprocess, "Popen", side_effect=AssertionError("process")), contextlib.redirect_stdout(io.StringIO()) as output:
            self.assertEqual(1, orchestration.main())
        self.assertEqual([], json.loads(output.getvalue())["calls"])

    def test_independent_usage_identity_and_counts(self):
        expected = {"codex": "route-codex", "command": "route-command"}
        attempts = [{"id": str(index), "requestId": "request-" + str(index), "accountId": account, "routeId": route,
                     "provider": account, "model": "gpt-6-luna", "statusCode": 200, "state": "succeeded", "finishedAt": "now", "tokens": {"total": None}}
                    for index, (account, route) in enumerate([(a, r) for a, r in expected.items() for _ in range(2)])]
        totals = {"attempts": 4, "requests": 4, "errors": 0, "tokens": {"total": None}, "unknownUsageAttempts": 4}
        summary = {"totals": totals, "complete": True, "cursor": 8, "accounts": [], "models": []}
        with mock.patch.object(orchestration, "api", side_effect=[{"total": 4, "items": attempts}, summary]):
            result = orchestration.capture_usage(types.SimpleNamespace(token="synthetic"), expected)
        self.assertEqual(totals, result["totals"])
        self.assertEqual(4, len(result["attempts"]))
        attempts[0]["routeId"] = "wrong-route"
        with mock.patch.object(orchestration, "api", return_value={"total": 4, "items": attempts}):
            with self.assertRaises(orchestration.probe.ProbeError):
                orchestration.capture_usage(types.SimpleNamespace(token="synthetic"), expected)

    def test_usage_waits_for_terminal_attempts(self):
        with mock.patch.object(orchestration, "api", return_value={"total": 3, "items": []}):
            self.assertIsNone(orchestration.capture_usage(types.SimpleNamespace(token="synthetic"), {}))


if __name__ == "__main__":
    unittest.main()
