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

    def test_selected_binary_receives_python_argv_without_mutation(self):
        with tempfile.TemporaryDirectory() as tmp:
            binary = Path(tmp) / "patch"
            binary.touch(mode=0o700)
            for args, expected in [(["--profile=lain"], str(binary)), (["exec", "--profile=lain"], "/original")]:
                argv = ["/invoked/codex", *args]
                with self.subTest(args=args), mock.patch.object(dispatch, "ORIGINAL", "/original"), mock.patch.object(dispatch, "PATCHED", str(binary)), mock.patch.object(sys, "argv", argv), mock.patch.object(dispatch, "check_target"), mock.patch.object(dispatch, "restore_initial_environment"), mock.patch.object(os, "execv", side_effect=RuntimeError("executed")) as execute:
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
        self.catalog_patch = mock.patch.object(installer, "qualifications", side_effect=self.fixture_catalog)
        self.catalog_patch.start()
        self.addCleanup(self.catalog_patch.stop)

    def fixture_catalog(self):
        # Synthetic byte pins test the contract, never actual Codex qualification.
        catalog = {"schemaVersion": 1, "supportedGrammars": ["codex-0.164.0"],
                   "originals": {self.args.original_sha256: {"version": "0.164.0", "grammarVersion": "codex-0.164.0"}},
                   "patched": {self.args.patched_sha256: {"version": "0.164.0", "variant": "lain-model-picker", "grammarVersion": "codex-0.164.0"}}}
        return catalog, installer.json_bytes(catalog)

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

    def test_uninstall_refuses_recreated_entry_with_same_target(self):
        self.install()
        replacement = self.base / "foreign-entry"
        replacement.symlink_to(os.readlink(self.entry))
        os.replace(replacement, self.entry)
        foreign_identity = installer.file_identity(self.entry)
        with self.assertRaisesRegex(ValueError, "changed"):
            self.uninstall()
        self.assertEqual(installer.file_identity(self.entry), foreign_identity)

    def test_uninstall_refuses_recreated_wrapper_with_same_hash(self):
        self.install()
        wrapper = self.state / "dispatch.py"
        replacement = self.state / "foreign-wrapper"
        replacement.write_bytes(wrapper.read_bytes())
        replacement.chmod(0o700)
        os.replace(replacement, wrapper)
        with self.assertRaisesRegex(ValueError, "changed"):
            self.uninstall()
        self.assertEqual(os.readlink(self.entry), str(wrapper))

    def test_uninstall_checks_original_fallback_hash_before_mutation(self):
        self.install()
        self.original.write_bytes(b"changed by a foreign updater")
        with self.assertRaisesRegex(ValueError, "SHA256"):
            self.uninstall()
        self.assertEqual(os.readlink(self.entry), str(self.state / "dispatch.py"))

    def test_install_detects_recreated_entry_and_keeps_recovery_state(self):
        rename = os.rename

        def recreate_after_staging(source, destination):
            rename(source, destination)
            replacement = self.base / "foreign-entry"
            replacement.symlink_to("original")
            os.replace(replacement, self.entry)

        with mock.patch.object(os, "rename", side_effect=recreate_after_staging):
            with self.assertRaisesRegex(ValueError, "changed"):
                self.install()
        self.assertEqual(os.readlink(self.entry), "original")
        self.assertTrue((self.state / "manifest.json").exists())

    def test_replacement_detects_foreign_change_after_mutation_without_rollback(self):
        expected = installer.link_snapshot(self.entry)
        replace = os.replace

        def foreign_replace_after_ours(source, destination):
            replace(source, destination)
            replacement = self.base / "foreign-entry"
            replacement.symlink_to("foreign-codex")
            replace(replacement, self.entry)

        with mock.patch.object(os, "replace", side_effect=foreign_replace_after_ours):
            with self.assertRaisesRegex(ValueError, "changed after replacement"):
                installer.replace_link(self.entry, "patched", expected)
        self.assertEqual(os.readlink(self.entry), "foreign-codex")

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

    def test_absolute_entry_preserves_arguments_cwd_and_environment(self):
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

    def check_shebang_argv0_boundary(self, name, use_path):
        self.install()
        code = "import json,os,pathlib;print(json.dumps({'argv':pathlib.Path('/proc/self/cmdline').read_bytes().decode().split(chr(0))[:-1], 'env':dict(os.environ), 'cwd':os.getcwd()}))"
        env = {"LANG": "C", "MARKER": "exact value", "PATH": str(self.base) + ":/usr/bin:/bin", "PYTHONCOERCECLOCALE": "0"}
        args = [name, "-c", code, "literal --profile=lain"]
        baseline = subprocess.run(args, executable=str(self.original), env=env, cwd=self.base, capture_output=True, text=True)
        actual = subprocess.run(args, executable=None if use_path else str(self.entry), env=env, cwd=self.base, capture_output=True, text=True)
        self.assertEqual(baseline.returncode, 0, baseline.stderr)
        self.assertEqual(actual.returncode, 0, actual.stderr)
        before = json.loads(baseline.stdout)
        after = json.loads(actual.stdout)
        self.assertEqual(before["argv"][0], name)
        self.assertEqual(after["argv"][0], str(self.entry))
        self.assertNotEqual(after["argv"][0], before["argv"][0])
        self.assertEqual(after["argv"][1:], before["argv"][1:])
        self.assertEqual(after["env"], env)
        self.assertEqual(after["cwd"], str(self.base))

    def test_path_invocation_exhibits_shebang_argv0_limit(self):
        self.check_shebang_argv0_boundary("codex", use_path=True)

    def test_custom_argv0_exhibits_shebang_limit(self):
        # subprocess executable= sets a distinct argv[0], like shell exec -a.
        self.check_shebang_argv0_boundary("custom-codex-name", use_path=False)

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

    def binding_args(self, candidate, apply=True):
        return type("Bind", (), dict(state=self.state, original=candidate,
                    original_sha256=installer.sha256(candidate), original_version="0.164.0",
                    expect_manifest_sha256=installer.sha256(self.state / "manifest.json"), apply=apply))()

    def bind(self, args):
        with contextlib.redirect_stdout(io.StringIO()):
            installer.rebind(args)

    def rollback(self, transaction, apply=True):
        args = type("Rollback", (), dict(state=self.state, transaction=transaction,
                    expect_manifest_sha256=installer.sha256(self.state / "manifest.json"), apply=apply))()
        with contextlib.redirect_stdout(io.StringIO()):
            installer.rollback_rebind(args)

    def files(self, root=None):
        return {str(path.relative_to(root or self.base)): path.read_bytes()
                for path in (root or self.base).rglob("*") if path.is_file() and not path.is_symlink()}

    def test_versioned_receipt_and_installed_read_only_status(self):
        self.install()
        before = self.files()
        status = installer.check_status(self.state)
        self.assertTrue(status["readyForGroups"], status)
        self.assertEqual(status["receipt"]["entry"], str(self.entry))
        self.assertEqual(status["receipt"]["state"], str(self.state))
        self.assertEqual(status["receipt"]["grammarVersion"], "codex-0.164.0")
        result = subprocess.run([sys.executable, str(self.state / "maintenance.py"), "--state", str(self.state), "status", "--json"], capture_output=True, text=True,
                                env={**os.environ, "PYTHONDONTWRITEBYTECODE": "1"})
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout), status)
        self.assertEqual(self.files(), before)

    def test_unknown_grammar_arguments_defer_to_selected_original(self):
        self.install()
        args = [str(self.entry), "--future-grammar", "--profile=lain"]
        via_dispatch = subprocess.run(args, capture_output=True)
        direct = subprocess.run(args, executable=str(self.original), capture_output=True)
        self.assertEqual((via_dispatch.returncode, via_dispatch.stdout, via_dispatch.stderr),
                         (direct.returncode, direct.stdout, direct.stderr))

    def test_changed_upstream_release_is_reported_without_changing_fallback(self):
        observer = self.base / "current"
        observer.symlink_to(self.original)
        self.entry.unlink()
        self.entry.symlink_to("current")
        self.install()
        other = self.base / "unknown-new-release"
        other.write_bytes(b"not a qualified client")
        other.chmod(0o700)
        observer.unlink()
        observer.symlink_to(other)
        status = installer.check_status(self.state)
        self.assertFalse(status["readyForGroups"])
        self.assertIn("upstream-release-changed", [item["code"] for item in status["issues"]])
        result = subprocess.run([str(self.entry), "-c", "import os;print(os.readlink('/proc/self/exe'))"], capture_output=True, text=True)
        self.assertEqual(result.stdout.strip(), str(self.original))
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_missing_or_changed_fallback_has_clear_status_and_runtime_error(self):
        self.install()
        self.original.write_bytes(b"unknown replacement bytes")
        status = installer.check_status(self.state)
        self.assertIn("original-missing-or-changed", [item["code"] for item in status["issues"]])
        result = subprocess.run([str(self.entry), "-c", "print('must not run')"], capture_output=True, text=True)
        self.assertEqual(result.returncode, 126)
        self.assertIn("original fallback bytes changed", result.stderr)
        self.original.unlink()
        result = subprocess.run([str(self.entry)], capture_output=True, text=True)
        self.assertEqual(result.returncode, 126)
        self.assertIn("original fallback is missing", result.stderr)

    def test_rebind_is_dry_run_by_default_and_rejects_unqualified_bytes(self):
        self.install()
        candidate = self.base / "candidate"
        candidate.write_bytes(self.original.read_bytes())
        candidate.chmod(0o700)
        before = self.files()
        self.bind(self.binding_args(candidate, False))
        self.assertEqual(self.files(), before)
        candidate.write_bytes(b"unknown release")
        with self.assertRaisesRegex(ValueError, "unqualified"):
            self.bind(self.binding_args(candidate))
        self.assertEqual((self.state / "dispatch.py").read_bytes(), before["client/dispatch.py"])

    def test_rebind_requires_matching_manifest_and_qualified_version(self):
        self.install()
        args = self.binding_args(self.original)
        args.expect_manifest_sha256 = "0" * 64
        with self.assertRaisesRegex(ValueError, "manifest changed"):
            self.bind(args)
        args = self.binding_args(self.original)
        args.original_version = "0.999.0"
        with self.assertRaisesRegex(ValueError, "version does not match"):
            self.bind(args)

    def test_rebind_rollback_and_uninstall_preserve_selected_original_contract(self):
        self.install()
        entry_before = installer.link_snapshot(self.entry)
        original_wrapper = (self.state / "dispatch.py").read_bytes()
        ordinary_config = self.base / "ordinary-config.toml"
        ordinary_config.write_bytes(b"user settings must stay unchanged")
        candidate = self.base / "qualified-relocated-release"
        candidate.write_bytes(self.original.read_bytes())
        candidate.chmod(0o700)
        self.bind(self.binding_args(candidate))
        self.assertEqual(installer.link_snapshot(self.entry), entry_before)
        status = installer.check_status(self.state)
        self.assertTrue(status["readyForGroups"], status)
        result = subprocess.run([str(self.entry), "-c", "import os;print(os.readlink('/proc/self/exe'))", "exec", "--profile=lain"], capture_output=True, text=True)
        self.assertEqual(result.stdout.strip(), str(candidate))
        transaction = next((self.state / "maintenance-history").iterdir()).name
        before_rollback = self.files()
        self.rollback(transaction, False)
        self.assertEqual(self.files(), before_rollback)
        self.rollback(transaction)
        self.assertEqual((self.state / "dispatch.py").read_bytes(), original_wrapper)
        self.assertTrue(installer.check_status(self.state)["readyForGroups"])
        self.assertEqual(ordinary_config.read_bytes(), b"user settings must stay unchanged")
        self.uninstall()
        self.assertEqual(os.readlink(self.entry), "original")

    def test_rebind_can_recover_missing_fallback_with_qualified_existing_copy(self):
        candidate = self.base / "safe-existing-copy"
        candidate.write_bytes(self.original.read_bytes())
        candidate.chmod(0o700)
        self.install()
        self.original.unlink()
        self.bind(self.binding_args(candidate))
        self.assertTrue(installer.check_status(self.state)["readyForGroups"])
        self.uninstall()
        self.assertEqual(os.readlink(self.entry), str(candidate))

    def test_rebind_refuses_foreign_entry_and_rollback_refuses_unknown_edits(self):
        self.install()
        self.bind(self.binding_args(self.original))
        transaction = next((self.state / "maintenance-history").iterdir()).name
        (self.state / "receipt.json").write_text("foreign modification")
        with self.assertRaisesRegex(ValueError, "files changed"):
            self.rollback(transaction)
        self.entry.unlink()
        self.entry.symlink_to("patched")
        with self.assertRaisesRegex(ValueError, "foreign launcher"):
            self.bind(self.binding_args(self.original))

    def test_legacy_state_is_diagnosed_and_explicitly_upgraded(self):
        self.install()
        manifest = json.loads((self.state / "manifest.json").read_text())
        manifest["schema"] = 2
        installer.atomic_write(self.state / "manifest.json", installer.json_bytes(manifest))
        status = installer.check_status(self.state)
        self.assertFalse(status["readyForGroups"])
        self.assertIn("receipt-upgrade-required", [item["code"] for item in status["issues"]])
        self.bind(self.binding_args(self.original))
        self.assertTrue(installer.check_status(self.state)["readyForGroups"])

    def test_changed_receipt_or_maintenance_code_is_not_ready(self):
        self.install()
        (self.state / "maintenance.py").write_text("changed")
        status = installer.check_status(self.state)
        self.assertFalse(status["readyForGroups"])
        self.assertIn("maintenance-files-changed", [item["code"] for item in status["issues"]])

    def test_non_executable_wrapper_is_not_ready_and_rebind_refuses(self):
        self.install()
        wrapper = self.state / "dispatch.py"
        wrapper.chmod(0o600)
        before = self.files()
        status = installer.check_status(self.state)
        self.assertFalse(status["readyForGroups"])
        self.assertEqual(status["code"], "maintenance-required")
        self.assertIn("dispatcher-not-executable", [item["code"] for item in status["issues"]])
        with self.assertRaisesRegex(ValueError, "regular executable"):
            self.bind(self.binding_args(self.original))
        self.assertEqual(self.files(), before)
        self.assertEqual(wrapper.stat().st_mode & 0o777, 0o600)

    def test_interrupted_rebind_has_failed_receipt_and_explicit_verified_rollback(self):
        self.install()
        before_wrapper = (self.state / "dispatch.py").read_bytes()
        write = installer.atomic_write
        interrupted = False

        def fail_receipt(path, *args, **kwargs):
            nonlocal interrupted
            if path == self.state / "receipt.json" and not interrupted:
                interrupted = True
                raise OSError("synthetic receipt interruption")
            return write(path, *args, **kwargs)

        with mock.patch.object(installer, "atomic_write", side_effect=fail_receipt):
            with self.assertRaisesRegex(OSError, "receipt interruption"):
                self.bind(self.binding_args(self.original))
        self.assertFalse(installer.check_status(self.state)["readyForGroups"])
        transaction = next((self.state / "maintenance-history").iterdir()).name
        self.rollback(transaction)
        self.assertEqual((self.state / "dispatch.py").read_bytes(), before_wrapper)
        self.assertTrue(installer.check_status(self.state)["readyForGroups"])

    def test_unsupported_manifest_grammar_is_not_reported_supported(self):
        self.install()
        manifest = json.loads((self.state / "manifest.json").read_text())
        manifest["grammarVersion"] = "codex-unknown"
        installer.atomic_write(self.state / "manifest.json", installer.json_bytes(manifest))
        status = installer.check_status(self.state)
        self.assertFalse(status["readyForGroups"])
        self.assertIn("unsupported-grammar", [item["code"] for item in status["issues"]])

    def test_status_missing_installation_and_foreign_entry_are_clear(self):
        self.assertEqual(installer.check_status(self.state)["code"], "not-installed")
        self.install()
        self.entry.unlink()
        self.entry.symlink_to("patched")
        status = installer.check_status(self.state)
        self.assertEqual(status["code"], "unrecognized")
        self.assertFalse(status["readyForGroups"])

    def test_maintenance_lock_refuses_symlink_without_mutating_target(self):
        target = self.base / "foreign-lock-target"
        target.write_bytes(b"untouched")
        (self.base / ".codex-profile-lain.lock").symlink_to(target)
        with self.assertRaises(OSError):
            with installer.maintenance_lock(self.state):
                self.fail("symlink lock accepted")
        self.assertEqual(target.read_bytes(), b"untouched")

    def test_registry_cannot_enable_an_unknown_parser_grammar(self):
        catalog, _ = self.fixture_catalog()
        catalog["supportedGrammars"] = ["codex-unreviewed"]
        for records in (catalog["originals"], catalog["patched"]):
            for record in records.values():
                record["grammarVersion"] = "codex-unreviewed"
        with mock.patch.object(installer, "qualifications", return_value=(catalog, installer.json_bytes(catalog))):
            with self.assertRaisesRegex(ValueError, "unqualified"):
                self.install()
        self.assertFalse(self.state.exists())

    def test_receipt_cannot_claim_a_different_wrapper_binding(self):
        self.install()
        candidate = self.base / "same-qualified-bytes-other-path"
        candidate.write_bytes(self.original.read_bytes())
        candidate.chmod(0o700)
        manifest = json.loads((self.state / "manifest.json").read_text())
        manifest.update(original=str(candidate), originalIdentity=installer.file_identity(candidate))
        installer.atomic_write(self.state / "manifest.json", installer.json_bytes(manifest))
        installer.atomic_write(self.state / "receipt.json", installer.json_bytes(installer.receipt(manifest, self.state)))
        status = installer.check_status(self.state)
        self.assertFalse(status["readyForGroups"])
        self.assertIn("dispatcher-binding-mismatch", [item["code"] for item in status["issues"]])


if __name__ == "__main__":
    unittest.main()
