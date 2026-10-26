#!/usr/bin/env python3
"""Offline protocol/containment checks; no native receiver, client or helper."""
import importlib.util
import json
from pathlib import Path
from types import SimpleNamespace
import tempfile
import unittest
from unittest import mock

SPEC = importlib.util.spec_from_file_location("native_runner", Path(__file__).with_name("native_runner.py"))
runner = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runner)


class NativeRunnerFixtures(unittest.TestCase):
    def test_tool_surface_distinguishes_namespaces_from_members(self):
        tools = [{"type": "function", "name": "exec_command"},
                 {"type": "namespace", "name": "mcp__fixture",
                  "tools": [{"type": "function", "name": f"tool_{i}"} for i in range(160)]}]
        surface = runner.tool_surface(tools)
        self.assertEqual(surface["topLevelTools"], 2)
        self.assertEqual(surface["leafTools"], 161)
        self.assertEqual(surface["toolBytes"], len(json.dumps(tools).encode()))

    def test_base_config_sentinel_rejects_trust_or_approval_changes(self):
        before = {"web_search": "disabled", "projects": {"/mnt/project": {"trust_level": "trusted"}}}
        self.assertTrue(runner.check_base_config(before, before)["unchanged"])
        for changed in ({**before, "sandbox_mode": "danger-full-access"},
                        {**before, "approval_policy": "never"},
                        {**before, "projects": {"/mnt/project": {"trust_level": "untrusted"}}},
                        {"web_search": "disabled"}):
            with self.assertRaisesRegex(ValueError, "unexpected synthetic"):
                runner.check_base_config(before, changed)

    def test_prepare_does_not_open_or_execute_candidates(self):
        with tempfile.TemporaryDirectory() as tmp:
            args = SimpleNamespace(output=Path(tmp) / "evidence", binary=Path(tmp) / "missing-client",
                                   helper=Path(tmp) / "missing-helper", assembly=Path(tmp) / "missing-assembly",
                                   binary_sha256=None, helper_sha256=None, assembly_sha256=None)
            with mock.patch.object(runner.common, "freeze") as freeze, \
                    mock.patch.object(runner.subprocess, "Popen") as popen:
                plan = runner.prepare(args)
            freeze.assert_not_called()
            popen.assert_not_called()
            self.assertEqual(plan["baseConfig"]["projects"], {"/mnt/project": {"trust_level": "trusted"}})
            self.assertEqual(list((args.output / "runtime").iterdir()), [])
            command = runner.bubblewrap_command(args.output, plan)
            self.assertNotIn("--setenv", command)
            self.assertIn(plan["environment"]["HOME"], command)
            self.assertNotIn(["--ro-bind", str(args.output / "project"), "/mnt/project"],
                             [command[i:i + 3] for i in range(len(command))])

    def test_selects_only_an_advertised_native_tool(self):
        custom = {"type": "namespace", "name": "functions", "tools": [{"type": "custom", "name": "exec"}]}
        self.assertEqual(runner.select_tool({"input": [{"type": "additional_tools", "tools": [custom]}]}),
                         ("custom_tool_call", "functions", "exec"))
        direct = {"tools": [{"type": "function", "name": "exec_command"}]}
        self.assertEqual(runner.select_tool(direct), ("function_call", None, "exec_command"))
        with self.assertRaisesRegex(ValueError, "advertised neither"):
            runner.select_tool({"tools": [{"type": "function", "name": "unreviewed_shell"}]})

    def test_native_failure_cannot_pass_as_successful_shell(self):
        custom = {"output": [{"type": "input_text", "text": json.dumps({"exit_code": 1, "output": "DENIED"})}]}
        with self.assertRaisesRegex(ValueError, "successful real shell"):
            runner.shell_output(custom, "custom_tool_call")
        with self.assertRaisesRegex(ValueError, "exit zero"):
            runner.shell_output({"output": "Process exited with code 81\nOutput:\nDENIED"}, "function_call")
        self.assertEqual(runner.shell_output({"output": "Process exited with code 0\nOutput:\nDENIED\n"},
                                            "function_call"), "DENIED\n")

    def test_read_sandbox_bypass_cannot_receive_success_continuation(self):
        with tempfile.TemporaryDirectory() as tmp:
            scenario = runner.Scenario(Path(tmp), {"environment": {"HOME": "/home/fixture", "CODEX_HOME": None}})
            body = {"model": "fixture-read", "tools": [{"type": "function", "name": "exec_command"}]}
            item = scenario.respond(body)[1]["item"]
            self.assertNotIn("workdir", json.loads(item["arguments"]))
            output = "Process exited with code 0\nOutput:\nREAD_ONLY_WRITE_ALLOWED\nNATIVE_CWD=/mnt/project\nNATIVE_HOME=/home/fixture\nNATIVE_CODEX_HOME=__UNSET__\n"
            with self.assertRaisesRegex(ValueError, "write was not denied"):
                scenario.respond({"model": "fixture-read", "input": [{"type": "function_call_output",
                                  "call_id": item["call_id"], "output": output}]})
            self.assertEqual(scenario.proofs, {})


if __name__ == "__main__":
    unittest.main()
