#!/usr/bin/env python3
"""Offline harness checks only: no native client/helper, server, namespace or build."""
import ast
import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
from types import SimpleNamespace
import sys
import tempfile
import unittest
from unittest import mock

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("native_code_mode_host", HERE / "native_code_mode_host.py")
harness = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(harness)


class HarnessFixtures(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.base = Path(self.temporary.name)
        self.source = self.base / "source"
        self.source.mkdir()
        harness.save(self.source / "lain-picker-source.json", {"commit": harness.COMMIT})
        examples = ['pub const PUBLIC_TOOL_NAME: &str = "exec";\npub const WAIT_TOOL_NAME: &str = "wait";',
                    'pub const DEFAULT_FUNCTION_NAMESPACE: &str = "functions";',
                    'ToolSpec::Freeform', '"custom_tool_call"', 'tools.exec_command', '{"models":[{}]}']
        for name, text in zip(harness.SOURCE_FILES, examples):
            target = self.source / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(text)
        self.args = SimpleNamespace(source=self.source, output=self.base / "evidence",
                                    binary=self.base / "not-built-codex", helper=self.base / "not-built-helper",
                                    binary_sha256=None, helper_sha256=None)

    def test_default_preparation_never_opens_or_starts_candidates(self):
        before = dict(os.environ)
        with mock.patch.object(harness, "freeze") as freeze, mock.patch.object(harness.subprocess, "run") as run, \
                mock.patch.object(harness.subprocess, "Popen") as popen, mock.patch.object(harness, "ThreadingHTTPServer") as server:
            plan = harness.prepare(self.args)
        for operation in (freeze, run, popen, server):
            operation.assert_not_called()
        self.assertEqual(dict(os.environ), before)
        self.assertEqual(list((self.args.output / "runtime").iterdir()), [])
        self.assertFalse(self.args.binary.exists())
        self.assertFalse(self.args.helper.exists())
        prepared = json.loads((self.args.output / "prepared.json").read_text())
        self.assertEqual(prepared["status"], "prepared-not-executed")
        self.assertFalse(prepared["nativeExecuted"])
        self.assertEqual(plan["source"]["toolNames"]["PUBLIC_TOOL_NAME"], "exec")
        script = ast.parse((self.args.output / "shell_proof.py").read_text())
        suffixes = [node.right.value for node in ast.walk(script) if isinstance(node, ast.BinOp)
                    and isinstance(node.op, ast.Add) and isinstance(node.right, ast.Constant)]
        self.assertIn("\n", suffixes, "shell-proof JSON must end in a real newline")

    def test_original_environment_paths_remain_unchanged_in_private_mount_plan(self):
        environ = {**os.environ, "CODEX_HOME": "/tmp/ap1-original-codex-path", "OPENAI_API_KEY": "never-inherit-fixture"}
        before = dict(environ)
        plan = harness.prepare(self.args, environ)
        self.assertEqual(environ, before)
        self.assertEqual(plan["privateCodex"], "private-codex")
        command = harness.bubblewrap_command(self.args.output, plan)
        self.assertIn(os.environ["HOME"], command)
        self.assertIn(environ["CODEX_HOME"], command)
        self.assertNotIn("--setenv", command)
        self.assertNotIn(["--ro-bind", "/", "/"], [command[index:index + 3] for index in range(len(command))])
        child = harness.child_environment(environ)
        self.assertEqual(child["HOME"], environ["HOME"])
        self.assertEqual(child["CODEX_HOME"], environ["CODEX_HOME"])
        self.assertNotIn("OPENAI_API_KEY", child)
        unset = {name: value for name, value in environ.items() if name != "CODEX_HOME"}
        self.assertNotIn("CODEX_HOME", harness.child_environment(unset))

    def test_unknown_source_protocol_is_rejected_before_creating_output(self):
        declaration = self.source / harness.SOURCE_FILES[0]
        declaration.write_text(declaration.read_text().replace('"exec"', '"future_exec"'))
        with self.assertRaisesRegex(ValueError, "unreviewed source tool names"):
            harness.prepare(self.args)
        self.assertFalse(self.args.output.exists())

    def test_native_run_requires_both_reviewed_pins_before_preparation(self):
        argv = ["native_code_mode_host.py", "--source", str(self.source), "--output", str(self.args.output),
                "--binary", str(self.args.binary), "--helper", str(self.args.helper), "--run"]
        with mock.patch.object(sys, "argv", argv), contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit) as error:
                harness.main()
        self.assertEqual(error.exception.code, 2)
        self.assertFalse(self.args.output.exists())

    def scenario(self):
        plan = harness.prepare(self.args)
        plan["helperSha256"] = "a" * 64
        observe = mock.Mock(return_value={"sha256": plan["helperSha256"], "pid": 123})
        return plan, harness.Scenario(plan, self.args.output, observe), observe

    @staticmethod
    def first_request():
        return {"model": harness.MODEL, "input": [{"role": "user", "content": "fixture only"},
                {"type": "additional_tools", "role": "developer", "tools": [{"type": "namespace", "name": "functions", "tools": [
                    {"type": "custom", "name": "exec"}, {"type": "function", "name": "wait"}]}]}]}

    def continuation(self, plan, exit_code=0, output_type="custom_tool_call_output"):
        proof = {"nonce": plan["nonce"], "HOME": plan["environment"]["HOME"], "CODEX_HOME": plan["environment"]["CODEX_HOME"],
                 "cwd": "/mnt/work", "networkNamespace": "net:[private-fixture]", "pidNamespace": "pid:[private-fixture]"}
        harness.save(self.args.output / "work/shell-proof.json", proof)
        result = json.dumps({"exit_code": exit_code, "output": "AP1_SHELL_PROOF:" + plan["nonce"]})
        return {"model": harness.MODEL, "input": [{"type": output_type, "call_id": harness.CALL_ID,
                "output": [{"type": "input_text", "text": "Script completed"}, {"type": "input_text", "text": result}]}]}

    def test_protocol_requests_namespaced_custom_exec_and_requires_real_result_continuation(self):
        plan, scenario, observe = self.scenario()
        tool = scenario.respond(self.first_request())[1]["item"]
        self.assertEqual((tool["type"], tool["namespace"], tool["name"]), ("custom_tool_call", "functions", "exec"))
        self.assertIn("await tools.exec_command(", tool["input"])
        self.assertNotIn(plan["nonce"], tool["input"], "provider code must not fabricate shell stdout by echoing the nonce")
        final = scenario.respond(self.continuation(plan))
        observe.assert_called_once_with(plan["helperSha256"])
        self.assertEqual(final[1]["item"]["content"][0]["text"], harness.FINAL)
        self.assertEqual(scenario.continuation["proof"]["nonce"], plan["nonce"])

    def test_ordinary_function_result_cannot_pass_as_code_mode_continuation(self):
        plan, scenario, observe = self.scenario()
        scenario.respond(self.first_request())
        with self.assertRaisesRegex(ValueError, "custom_tool_call_output"):
            scenario.respond(self.continuation(plan, output_type="function_call_output"))
        observe.assert_not_called()

    def test_failed_shell_result_or_missing_side_effect_cannot_pass(self):
        plan, scenario, observe = self.scenario()
        scenario.respond(self.first_request())
        with self.assertRaisesRegex(ValueError, "successful real shell stdout"):
            scenario.respond(self.continuation(plan, exit_code=1))
        observe.assert_not_called()

    def test_side_effect_is_required_even_with_matching_success_stdout(self):
        plan, scenario, observe = self.scenario()
        scenario.respond(self.first_request())
        body = self.continuation(plan)
        (self.args.output / "work/shell-proof.json").unlink()
        with self.assertRaises(FileNotFoundError):
            scenario.respond(body)
        observe.assert_not_called()

    def test_direct_shell_surface_is_rejected(self):
        _, scenario, _ = self.scenario()
        body = self.first_request()
        body["tools"] = [{"type": "function", "name": "exec_command"}]
        with self.assertRaisesRegex(ValueError, "direct shell tools"):
            scenario.respond(body)

    def test_timeout_cleanup_kills_and_reaps_only_owned_process_group(self):
        process = mock.Mock(pid=123456)
        with mock.patch.object(harness.os, "killpg") as kill:
            harness.kill_owned_group(process)
        kill.assert_called_once_with(process.pid, harness.signal.SIGKILL)
        process.wait.assert_called_once_with(timeout=5)

    def test_missing_owned_group_still_reaps_child(self):
        process = mock.Mock(pid=123456)
        process.poll.return_value = None
        with mock.patch.object(harness.os, "killpg", side_effect=ProcessLookupError):
            harness.kill_owned_group(process)
        process.kill.assert_called_once_with()
        process.wait.assert_called_once_with(timeout=5)


if __name__ == "__main__":
    unittest.main()
