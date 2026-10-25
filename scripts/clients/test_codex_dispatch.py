#!/usr/bin/python3
"""Small, serial, offline tests. All installations target temporary fixtures."""
import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock


HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("codex_dispatch", HERE / "codex_dispatch.py")
dispatch = importlib.util.module_from_spec(spec)
spec.loader.exec_module(dispatch)
spec_install = importlib.util.spec_from_file_location("installer", HERE / "install_codex_dispatch.py")
installer = importlib.util.module_from_spec(spec_install)
spec_install.loader.exec_module(installer)


class ProfileParsing(unittest.TestCase):
    def test_positive(self):
        cases = [
            ["--profile", "lain"], ["--profile=lain"], ["-p", "lain"],
            ["-p=lain"], ["-plain"],
            ["--profile", "lain", "--no-daemon"],
            ["--model", "gpt-6", "--profile=lain", "hello"],
            ["hello", "-plain", "--no-alt-screen"],
            ["-c", 'model="--profile=other"', "--profile=lain"],
            ["--profile=lain", "--", "--profile=other"],
            ["--profile=lain", "--", "exec"],
            ["--image", "first.png", "second.png", "--profile=lain"],
        ]
        for args in cases:
            with self.subTest(args=args):
                self.assertTrue(dispatch.uses_lain(args))

    def test_negative_and_ambiguous(self):
        cases = [
            [], ["--profile", "other"], ["--profile=lainish"],
            ["--", "--profile=lain"], ["--", "--profile", "lain"],
            ["--config", "--profile=lain"], ["-c", "profile=lain"],
            ["--config=--profile=lain"], ["-m", "--profile=lain"],
            ["--model=--profile=lain"], ["-m--profile=lain"],
            ["--profile lain"], ["hello --profile=lain"],
            ["--profile", "lain", "--profile", "other"],
            ["--profile=lain", "-plain"], ["--profile=lain", "--unknown"],
            ["--profile=lain", "--help"], ["--profile=lain", "-V"],
            ["--profile=lain", "--model"], ["--profile=lain", "--model="],
            ["--profile=lain", "--sandbox=invalid"],
            ["--profile=lain", "--ask-for-approval=invalid"],
            ["--profile=lain", "--ask-for-approval=untrusted"],
            ["--profile=lain", "--config=missing_equals"],
            ["--profile=lain", "--no-daemon=true"],
            ["--profile=lain", "--no-daemon", "--no-daemon"],
            ["--profile=lain", "one", "two"],
            ["--profile=lain", "--yolo", "-a", "never"],
            ["--profile=lain", "--approve-for-me", "--sandbox=read-only"],
            ["--profile=lain", "--image", "exec"],
        ]
        for args in cases:
            with self.subTest(args=args):
                self.assertFalse(dispatch.uses_lain(args))

    def test_all_subcommands_defer(self):
        for command in dispatch.SUBCOMMANDS:
            for args in ([command, "--profile=lain"], ["--profile=lain", command]):
                with self.subTest(args=args):
                    self.assertFalse(dispatch.uses_lain(args))

    def test_selected_binary_receives_identical_argv(self):
        with tempfile.TemporaryDirectory() as tmp:
            binary = Path(tmp) / "patch"
            binary.touch(mode=0o700)
            for args, expected in [(["--profile=lain"], str(binary)), (["exec", "--profile=lain"], "/original")]:
                argv = ["/invoked/codex", *args]
                with self.subTest(args=args), mock.patch.object(dispatch, "ORIGINAL", "/original"), mock.patch.object(dispatch, "PATCHED", str(binary)), mock.patch.object(sys, "argv", argv), mock.patch.object(dispatch, "restore_initial_environment"), mock.patch.object(os, "execv", side_effect=RuntimeError("executed")) as execute:
                    with self.assertRaisesRegex(RuntimeError, "executed"):
                        dispatch.main()
                    execute.assert_called_once_with(expected, argv)
                    self.assertIs(execute.call_args.args[1], argv)


class Installation(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.base = Path(self.tmp.name)
        self.entry = self.base / "codex"
        self.original = self.base / "original"
        self.patched = self.base / "patched"
        self.state = self.base / "client"
        self.original.write_bytes(Path("/usr/bin/python3").read_bytes())
        self.original.chmod(0o700)
        self.patched.write_bytes(self.original.read_bytes())
        self.patched.chmod(0o700)
        self.entry.symlink_to("original")
        self.args = type("Args", (), dict(entry=self.entry, state=self.state, patched=self.patched,
                         original_sha256=installer.sha256(self.original),
                         patched_sha256=installer.sha256(self.patched), apply=True))()

    def tearDown(self):
        self.tmp.cleanup()

    def install(self):
        with contextlib.redirect_stdout(io.StringIO()):
            installer.install(self.args)

    def uninstall(self):
        with contextlib.redirect_stdout(io.StringIO()):
            installer.uninstall(self.args)

    def test_dry_run_is_read_only(self):
        self.args.apply = False
        self.install()
        self.assertEqual(os.readlink(self.entry), "original")
        self.assertFalse(self.state.exists())

    def test_round_trip_restores_exact_symlink(self):
        before = self.original.read_bytes()
        self.install()
        self.assertEqual(os.readlink(self.entry), str(self.state / "dispatch.py"))
        manifest = json.loads((self.state / "manifest.json").read_text())
        self.assertEqual(manifest["original"], str(self.original))
        self.assertEqual(manifest["dispatcherSha256"], installer.sha256(self.state / "dispatch.py"))
        self.uninstall()
        self.assertEqual(os.readlink(self.entry), "original")
        self.assertEqual(self.original.read_bytes(), before)
        self.assertTrue((self.state / "previous-symlink.json").exists())

    def test_sha_mismatch_does_not_mutate(self):
        self.args.patched_sha256 = "0" * 64
        with self.assertRaisesRegex(ValueError, "SHA256"):
            self.install()
        self.assertEqual(os.readlink(self.entry), "original")
        self.assertFalse(self.state.exists())

    def test_non_symlink_does_not_mutate(self):
        self.entry.unlink()
        self.entry.write_text("owned by another installer")
        with self.assertRaisesRegex(ValueError, "non-symlink"):
            self.install()
        self.assertEqual(self.entry.read_text(), "owned by another installer")

    def test_uninstall_preserves_new_owner(self):
        self.install()
        self.entry.unlink()
        self.entry.symlink_to("patched")
        with self.assertRaisesRegex(ValueError, "changed"):
            self.uninstall()
        self.assertEqual(os.readlink(self.entry), "patched")

    def test_uninstall_preserves_modified_wrapper(self):
        self.install()
        with (self.state / "dispatch.py").open("a") as stream:
            stream.write("# changed\n")
        with self.assertRaisesRegex(ValueError, "changed"):
            self.uninstall()
        self.assertEqual(os.readlink(self.entry), str(self.state / "dispatch.py"))

    def test_missing_patch_only_breaks_lain(self):
        self.install()
        (self.state / "codex-lain").unlink()
        (self.state / "manifest.json").unlink()
        # Original test binary is Python; a normal argv must reach its parser.
        ordinary = subprocess.run([str(self.entry), "-c", "print('original-ok')"], text=True, capture_output=True)
        self.assertEqual(ordinary.returncode, 0, ordinary.stderr)
        self.assertEqual(ordinary.stdout.strip(), "original-ok")
        special = subprocess.run([str(self.entry), "--profile=lain"], text=True, capture_output=True)
        self.assertEqual(special.returncode, 126)
        self.assertIn("patched binary is missing", special.stderr)

    def test_exec_preserves_argv_cwd_and_environment(self):
        self.install()
        # /proc exposes the actual argv after exec, including argv[0]. Use -c as
        # Python's argument here: it is intentionally invalid as a Codex override.
        code = "import json,os,pathlib;print(json.dumps({'argv':pathlib.Path('/proc/self/cmdline').read_bytes().decode().split(chr(0))[:-1], 'env':dict(os.environ), 'cwd':os.getcwd()}))"
        env = {"LANG": "C", "MARKER": "exact value", "PATH": "/usr/bin:/bin", "PYTHONCOERCECLOCALE": "0"}
        result = subprocess.run([str(self.entry), "-c", code, "tail"], env=env, cwd=self.base, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        actual = json.loads(result.stdout)
        self.assertEqual(actual["argv"], [str(self.entry), "-c", code, "tail"])
        self.assertEqual(actual["env"], env)
        self.assertEqual(actual["cwd"], str(self.base))

    def test_dispatcher_refused_as_original(self):
        self.install()
        self.args.state = self.base / "second"
        self.args.original_sha256 = installer.sha256(self.entry)
        with self.assertRaisesRegex(ValueError, "recursive"):
            self.install()

    def test_python_locale_coercion_does_not_change_original_environment(self):
        self.original.write_bytes(Path("/usr/bin/env").read_bytes())
        self.args.original_sha256 = installer.sha256(self.original)
        self.install()
        env = {"LANG": "C", "MARKER": "still exact"}
        result = subprocess.run([str(self.entry), "-0"], env=env, capture_output=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        actual = dict(entry.split(b"=", 1) for entry in result.stdout.split(b"\0") if entry)
        self.assertEqual(actual, {key.encode(): value.encode() for key, value in env.items()})


if __name__ == "__main__":
    unittest.main()
