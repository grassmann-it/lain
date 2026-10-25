"""Small preparation/isolation tests; never start GUI, dotnet or providers."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("n09_runner", HERE / "run_native.py")
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class Preparation(unittest.TestCase):
    def test_allowlist_preserves_home_and_excludes_credentials_hooks_and_desktop(self):
        value = runner.selected_environment({"HOME": "/unchanged/home", "PATH": "/usr/bin", "CODEX_HOME": "/secret",
            "DISPLAY": ":0", "DBUS_SESSION_BUS_ADDRESS": "private", "OPENAI_API_KEY": "secret",
            "HTTPS_PROXY": "external", "DOTNET_STARTUP_HOOKS": "foreign"})
        self.assertEqual(value, {"HOME": "/unchanged/home", "PATH": "/usr/bin"})

    def test_scale_is_bounded_to_reviewed_matrix(self):
        self.assertEqual(runner.parse_scale("1.25"), "1.25")
        for value in ("0", "2", "auto", "nan"):
            with self.assertRaises(ValueError):
                runner.parse_scale(value)

    def test_prepare_pins_artifacts_without_launch_and_refuses_reusing_evidence(self):
        cache = Path.home() / ".cache/lain-build-tmp"
        cache.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="n09-source-test-", dir=cache) as temporary:
            base = Path(temporary)
            artifacts = base / "build"
            artifacts.mkdir()
            assembly = artifacts / "Fixture.dll"
            assembly.write_bytes(b"synthetic-not-executable")
            args = argparse.Namespace(assembly=assembly, output=base / "evidence", source_commit="a" * 40,
                scale="1", argument=["--n01-display-smoke"], uxd_filter=None, native_editor_filter=None, timeout=60)
            with patch.object(runner.subprocess, "Popen", side_effect=AssertionError("must not launch")):
                plan = runner.prepare(args)
            self.assertEqual(plan["state"], "prepared-not-executed")
            self.assertEqual(plan["homePath"], os.environ["HOME"])
            self.assertEqual(plan["artifacts"], {"Fixture.dll": runner.sha(assembly)})
            self.assertFalse((args.output / "run-result.json").exists())
            self.assertEqual(json.loads((args.output / "plan.json").read_text()), plan)
            with self.assertRaises(FileExistsError):
                runner.prepare(args)

    def test_artifact_symlink_is_rejected_before_execution(self):
        cache = Path.home() / ".cache/lain-build-tmp"
        cache.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="n09-source-test-", dir=cache) as temporary:
            root = Path(temporary)
            (root / "foreign.dll").symlink_to("/does-not-exist")
            with self.assertRaises(ValueError):
                runner.artifact_inventory(root)

    def test_catalog_is_only_a_plan_and_preserves_live_gate_separation(self):
        catalog = json.loads((HERE / "n09-cases.json").read_text())
        self.assertEqual(catalog["globalState"], "source-preparation")
        ids = [case["id"] for case in catalog["cases"]]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertTrue(all(case["state"] not in ("passed", "PASS", "completed") for case in catalog["cases"]))
        self.assertTrue(all(case["owner"].startswith("root") for case in catalog["cases"] if case["id"].startswith("N09-R")))
        self.assertEqual(catalog["matrix"]["monitorViewports"][-1], [640, 420])


if __name__ == "__main__":
    unittest.main()
