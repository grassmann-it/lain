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

    def test_cli_api_comparison_uses_exact_window_timezone_and_private_home(self):
        import pathlib
        window = {"from": "2026-10-23T10:00:00+00:00", "to": "2026-10-23T11:00:00+00:00", "timeZone": "UTC"}
        summary = {"totals": {"attempts": 4, "tokens": {"total": None}}, "daily": [], "accounts": [], "models": [], "timeZone": "UTC"}
        host = types.SimpleNamespace(token="local-token", state=pathlib.Path("/tmp/synthetic-lain-home"))
        cli = types.SimpleNamespace(returncode=0, stdout=json.dumps(summary), stderr="")
        with mock.patch.object(orchestration, "api", return_value=summary) as api, mock.patch.object(orchestration.subprocess, "run", return_value=cli) as run:
            result = orchestration.compare_cli_usage(host, pathlib.Path("/tmp/Lain.Cli.dll"), window)
        self.assertTrue(result["passed"])
        self.assertEqual("/usage?" + orchestration.usage_query(window), api.call_args.args[0])
        self.assertEqual(["dotnet", "/tmp/Lain.Cli.dll", "usage", "--from", window["from"], "--to", window["to"], "--timezone", "UTC", "--json"], run.call_args.args[0])
        self.assertEqual(str(host.state), run.call_args.kwargs["env"]["LAIN_HOME"])
        mismatched = dict(summary, totals={"attempts": 5})
        with mock.patch.object(orchestration, "api", return_value=mismatched), mock.patch.object(orchestration.subprocess, "run", return_value=cli):
            with self.assertRaises(orchestration.probe.ProbeError):
                orchestration.compare_cli_usage(host, pathlib.Path("/tmp/Lain.Cli.dll"), window)


if __name__ == "__main__":
    unittest.main()
