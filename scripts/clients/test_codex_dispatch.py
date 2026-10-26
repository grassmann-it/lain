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


def default_lain_fixture(home):
    """Synthetic generated catalog/receipt; never reads the real user config."""
    generation = "a" * 32
    artifact = home / ".lain-artifacts" / generation
    artifact.mkdir(parents=True, exist_ok=True, mode=0o700)
    catalog = artifact / "lain.models.json"
    document = {"models": [{"slug": "base-model"}, {"slug": "other-model"}],
                "lain_model_picker": {"schema_version": 1, "groups": [
                    {"id": "provider:test", "name": "Provider · Test", "models": ["base-model", "other-model"]}]}}
    catalog.write_text(json.dumps(document), encoding="utf-8")
    receipt = artifact / "manifest.json"
    receipt.write_text(json.dumps({"generationId": generation, "revision": 7,
                                   "catalogHash": hashlib.sha256(catalog.read_bytes()).hexdigest()}))
    (home / "config.toml").write_text('model_provider = "lain"\nmodel = "base-model"\n'
        'model_reasoning_effort = "ultra"\nmodel_catalog_json = ' + json.dumps(str(catalog)) + '\n'
        '[projects."/user/project"]\ntrust_level = "trusted"\n', encoding="utf-8")
    # Deliberately different settings: dispatch must not inject this profile.
    (home / "lain.config.toml").write_text('model = "other-model"\nmodel_reasoning_effort = "low"\n')
    for path in (catalog, receipt, home / "config.toml", home / "lain.config.toml"):
        path.chmod(0o600)
    return catalog


class ProfileParsing(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        environment = mock.patch.dict(os.environ, {"CODEX_HOME": self.tmp.name})
        environment.start()
        self.addCleanup(environment.stop)

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


class DefaultLainParsing(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.base = Path(self.tmp.name)
        self.home = self.base / "codex-home"
        self.catalog = default_lain_fixture(self.home)
        self.config = self.home / "config.toml"
        self.receipt = self.catalog.parent / "manifest.json"
        environment = mock.patch.dict(os.environ, {"CODEX_HOME": str(self.home)})
        environment.start()
        self.addCleanup(environment.stop)

    def catalog_bytes(self, raw):
        self.catalog.write_bytes(raw)
        receipt = json.loads(self.receipt.read_bytes())
        receipt["catalogHash"] = hashlib.sha256(raw).hexdigest()
        self.receipt.write_text(json.dumps(receipt))

    def test_root_default_and_user_options(self):
        for args in ([], ["hello"], ["--no-daemon"], ["-m", "user-model"],
                     ["-c", 'model="user-model"', "-c", 'model_reasoning_effort="high"'],
                     ["-C", "/user/project", "--no-alt-screen"], ["--", "exec"],
                     ["--", "--profile=other"], ["--image", "one.png", "two.png"]):
            with self.subTest(args=args):
                self.assertTrue(dispatch.uses_lain(args))

    def test_other_profiles_subcommands_and_parser_boundaries_keep_original(self):
        cases = [["--profile=other"], ["--profile=lainish"], ["--profile=old-cpa"],
                 ["--help"], ["-h"], ["--version"], ["-V"], ["--unknown"],
                 ["--model"], ["--model=one", "-m", "two"], ["one", "two"],
                 ["--config=missing_equals"], ["--sandbox=invalid"],
                 ["--profile=lain", "--help"], ["--profile=lain", "--unknown"]]
        for command in dispatch.SUBCOMMANDS:
            cases.extend(([command], [command, "--profile=lain"], ["--profile=lain", command]))
        for args in cases:
            with self.subTest(args=args), mock.patch.object(dispatch, "configured_lain_default", side_effect=AssertionError("must not read config")):
                self.assertFalse(dispatch.uses_lain(args))

    def test_routing_overrides_are_conservatively_delegated(self):
        for args in (["--oss"], ["--local-provider=ollama"], ["--remote=unix:/tmp/remote"],
                     ["--remote-auth-token-env=TOKEN"], ["-c", 'model_provider="other"'],
                     ["-c", '"model_provider"="lain"'], ["-c", 'model_catalog_json="/other.json"'],
                     ["-c", 'model_providers.lain.base_url="http://localhost:1234"'],
                     ["-c", 'profile="lain"'], ["-c", 'oss_provider="ollama"'],
                     ["-c", 'model=unquoted'], ["-c", 'model="a"\nmodel_provider="other"']):
            with self.subTest(args=args):
                self.assertFalse(dispatch.uses_lain(args))
        # Preserve the pre-existing explicit-profile classifier even for config
        # strings whose complete semantics are left to native Codex.
        self.assertTrue(dispatch.uses_lain(["--profile=lain", "-c", "model=unquoted"]))

    def test_base_provider_and_strict_toml_are_required(self):
        original = self.config.read_bytes()
        for raw in (original.replace(b'"lain"', b'"other"', 1), b'provider="lain"',
                    b'model_provider="lain"\nmodel_provider="lain"', b'\xff',
                    original + b'\n[bad', b'model_provider=true',
                    b'profile="lain"\n' + original,
                    original.replace(b'model_catalog_json = ', b'other_catalog = ')):
            with self.subTest(raw=raw[:40]):
                self.config.write_bytes(raw)
                self.assertFalse(dispatch.uses_lain([]))

    def test_codex_home_precedence_and_home_default(self):
        with mock.patch.dict(os.environ, {"CODEX_HOME": str(self.base / "missing")}):
            self.assertFalse(dispatch.uses_lain([]))
            self.assertTrue(dispatch.uses_lain(["--profile=lain"]))
        with mock.patch.dict(os.environ, {"CODEX_HOME": "relative"}):
            self.assertFalse(dispatch.uses_lain([]))
        default_lain_fixture(self.base / ".codex")
        with mock.patch.dict(os.environ, {"HOME": str(self.base)}, clear=True):
            self.assertTrue(dispatch.uses_lain([]))

    def test_immutable_generation_does_not_overlay_profile_or_require_current(self):
        (self.home / "lain.config.toml").write_bytes(b"invalid profile; must not read")
        (self.home / ".lain-artifacts" / "current.json").write_bytes(b"a newer profile generation")
        before = self.config.read_bytes()
        self.assertTrue(dispatch.uses_lain([]))
        self.assertEqual(self.config.read_bytes(), before)

    def test_only_known_lain_catalog_paths_are_accepted(self):
        original = self.config.read_text()
        for path in (self.home / "other.models.json", self.base / "lain.models.json",
                     Path("relative.json"), self.home / ".lain-artifacts" / "bad" / "lain.models.json"):
            path.parent.mkdir(exist_ok=True, parents=True)
            if path.is_absolute():
                path.write_bytes(self.catalog.read_bytes())
            self.config.write_text(original.replace(json.dumps(str(self.catalog)), json.dumps(str(path))))
            self.assertFalse(dispatch.uses_lain([]))
        self.config.write_text(original)

    def test_legacy_generated_catalog_needs_no_receipt_or_profile_overlay(self):
        legacy = self.home / "lain.models.json"
        legacy.write_bytes(self.catalog.read_bytes())
        self.config.write_text(self.config.read_text().replace(json.dumps(str(self.catalog)), json.dumps(str(legacy))))
        (self.home / "lain.config.toml").unlink()
        self.receipt.unlink()
        self.assertTrue(dispatch.uses_lain([]))
        document = json.loads(legacy.read_bytes())
        del document["lain_model_picker"]
        legacy.write_text(json.dumps(document))
        self.assertFalse(dispatch.uses_lain([]))

    def test_valid_grouped_catalog_does_not_require_current_artifact_receipt(self):
        original = json.loads(self.receipt.read_bytes())
        for change in ({"generationId": "b" * 32}, {"catalogHash": "0" * 64},
                       {"revision": -1}, {"revision": True}, {"catalogHash": None}):
            with self.subTest(change=change):
                self.receipt.write_text(json.dumps(original | change))
                self.assertTrue(dispatch.uses_lain([]))
        self.receipt.write_bytes(b"old invalid receipt")
        self.assertTrue(dispatch.uses_lain([]))
        self.receipt.unlink()
        self.assertTrue(dispatch.uses_lain([]))
        self.catalog.write_bytes(self.catalog.read_bytes() + b" ")
        self.assertTrue(dispatch.uses_lain([]))

    def test_strict_json_rejects_duplicate_keys_nonfinite_and_malformed_documents(self):
        original = self.catalog.read_bytes()
        for raw in (b"{", b"[]", b"null", b"\xff", original + b" trailing",
                    b'{"models":[],"models":[]}', original.replace(b'"schema_version": 1', b'"schema_version": NaN'),
                    original[:-1] + b',"unused":1e999}',
                    original.replace(b'"schema_version": 1', b'"schema_version": 1, "schema_version": 1')):
            with self.subTest(raw=raw[:40]):
                self.catalog_bytes(raw)
                self.assertFalse(dispatch.uses_lain([]))
        self.catalog_bytes(original)
        self.assertTrue(dispatch.uses_lain([]))

    def test_invalid_extensions_defer_to_original(self):
        original = json.loads(self.catalog.read_bytes())
        group = original["lain_model_picker"]["groups"][0]
        pickers = [None, {}, {"schema_version": True, "groups": [group]},
                   {"schema_version": 2, "groups": [group]}, {"schema_version": 1, "groups": []},
                   {"schema_version": 1, "groups": [group] * 129},
                   {"schema_version": 1, "groups": [group, group]},
                   {"schema_version": 1, "groups": [group], "unknown": True}]
        for change in ({"id": ""}, {"name": "bad\nlabel"}, {"id": "x" * 257},
                       {"name": "🧪" * 257}, {"models": ["missing"]}, {"models": [False]},
                       {"models": ["base-model", "base-model"]}, {"router_model": "missing"},
                       {"router_model": []}, {"unknown": True}):
            pickers.append({"schema_version": 1, "groups": [group | change]})
        for picker in pickers:
            with self.subTest(picker=picker):
                self.catalog_bytes(json.dumps(original | {"lain_model_picker": picker}).encode())
                self.assertFalse(dispatch.uses_lain([]))
        for models in ([], [None], [{"slug": 5}], [{"slug": "base-model"}] * 2):
            self.catalog_bytes(json.dumps(original | {"models": models}).encode())
            self.assertFalse(dispatch.uses_lain([]))

    def test_valid_optional_router_and_overlapping_groups(self):
        document = json.loads(self.catalog.read_bytes())
        groups = document["lain_model_picker"]["groups"]
        groups[0]["router_model"] = "base-model"
        groups.append(groups[0] | {"id": "all", "name": "Alle", "router_model": None})
        self.catalog_bytes(json.dumps(document).encode())
        self.assertTrue(dispatch.uses_lain([]))

    def test_symlinks_unsafe_permissions_and_hardlinks_defer(self):
        paths = (self.home, self.catalog.parent.parent, self.catalog.parent, self.config, self.catalog)
        for path in paths:
            with self.subTest(path=path.name):
                mode = path.stat().st_mode & 0o777
                path.chmod(mode | 0o022)
                self.assertFalse(dispatch.uses_lain([]))
                path.chmod(mode)
                moved = path.with_name(path.name + ".moved")
                path.rename(moved)
                path.symlink_to(moved)
                self.assertFalse(dispatch.uses_lain([]))
                path.unlink()
                moved.rename(path)
        for path in (self.config, self.catalog):
            linked = self.base / "hardlink"
            os.link(path, linked)
            self.assertFalse(dispatch.uses_lain([]))
            linked.unlink()
        with mock.patch.object(os, "getuid", return_value=os.getuid() + 1):
            self.assertFalse(dispatch.uses_lain([]))
        self.assertTrue(dispatch.uses_lain([]))

    def test_bounded_reads_and_nonregular_files_defer(self):
        for setting, path in (("CONFIG_LIMIT", self.config), ("CATALOG_LIMIT", self.catalog)):
            with self.subTest(setting=setting), mock.patch.object(dispatch, setting, path.stat().st_size - 1):
                self.assertFalse(dispatch.uses_lain([]))
            raw = path.read_bytes()
            path.unlink()
            os.mkfifo(path, 0o600)
            self.assertFalse(dispatch.uses_lain([]))
            path.unlink()
            path.write_bytes(raw)

    def test_default_main_preserves_argv_object_and_calls_environment_restoration(self):
        argv = ["/invoked/codex", "-m", "user-model", "-c", 'model_reasoning_effort="high"']
        with mock.patch.object(sys, "argv", argv), mock.patch.object(dispatch, "check_target") as check, mock.patch.object(dispatch, "restore_initial_environment") as restore, mock.patch.object(os, "execv", side_effect=RuntimeError("executed")) as execute:
            with self.assertRaisesRegex(RuntimeError, "executed"):
                dispatch.main()
            check.assert_called_once_with(dispatch.PATCHED, dispatch.PATCHED_SHA256)
            restore.assert_called_once_with()
            execute.assert_called_once_with(dispatch.PATCHED, argv)
            self.assertIs(execute.call_args.args[1], argv)


class Installation(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.base = Path(self.tmp.name)
        self.entry = self.base / "codex"
        self.original = self.base / "original"
        self.patched = self.base / "patched"
        self.helper = self.base / installer.CODE_MODE_HOST
        self.state = self.base / "client"
        self.codex_home = self.base / "codex-home"
        environment = mock.patch.dict(os.environ, {"CODEX_HOME": str(self.codex_home)})
        environment.start()
        self.addCleanup(environment.stop)
        self.original.write_bytes(Path("/usr/bin/python3").read_bytes())
        self.original.chmod(0o700)
        self.patched.write_bytes(self.original.read_bytes())
        self.patched.chmod(0o700)
        self.helper.write_bytes(Path("/usr/bin/true").read_bytes())
        self.helper.chmod(0o700)
        self.helper_sha = installer.sha256(self.helper)
        self.entry.symlink_to("original")
        self.args = type("Args", (), dict(entry=self.entry, state=self.state, patched=self.patched,
                         code_mode_host=self.helper, code_mode_host_sha256=self.helper_sha,
                         original_sha256=installer.sha256(self.original),
                         patched_sha256=installer.sha256(self.patched), apply=True))()
        self.catalog_patch = mock.patch.object(installer, "qualifications", side_effect=self.fixture_catalog)
        self.catalog_patch.start()
        self.addCleanup(self.catalog_patch.stop)

    def fixture_catalog(self):
        # Synthetic byte pins test the contract, never actual Codex qualification.
        catalog = {"schemaVersion": 1, "supportedGrammars": ["codex-0.164.0"],
                   "originals": {self.args.original_sha256: {"version": "0.164.0", "grammarVersion": "codex-0.164.0"}},
                   "patched": {self.args.patched_sha256: {"version": "0.164.0", "variant": "lain-model-picker", "grammarVersion": "codex-0.164.0",
                       "upstreamCommit": "6aaac694803004a63c6f393d153c96d22521a6b9",
                       "runtimeDependencies": {installer.CODE_MODE_HOST: {
                           "sha256": self.helper_sha, "version": "0.164.0",
                           "upstreamCommit": "6aaac694803004a63c6f393d153c96d22521a6b9",
                           "provenance": "same-source-build", "evidence": "synthetic fixture only"}}}}}
        if getattr(self, "package_record", None) is not None:
            catalog["patched"][self.args.patched_sha256]["runtimePackage"] = self.package_record
        catalog["patched"].update(getattr(self, "extra_patch_records", {}))
        return catalog, installer.json_bytes(catalog)

    def package_fixture(self, patched=None, bind_record=True):
        package = self.base / "package"
        package.mkdir(mode=0o700)
        for relative in installer.RUNTIME_FILES:
            path = package / relative
            path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
            path.write_bytes((relative + " fixture\n").encode())
            if relative in installer.RUNTIME_EXECUTABLES:
                path.write_bytes(Path("/usr/bin/true").read_bytes())
            path.chmod(0o700 if relative in installer.RUNTIME_EXECUTABLES else 0o600)
        (package / "codex-package.json").write_text(json.dumps({"layoutVersion": 1,
            "version": "0.164.0", "target": installer.RUNTIME_TARGET,
            "entrypoint": "bin/codex", "resourcesDir": "codex-resources", "pathDir": "codex-path"}))
        (package / "bin").mkdir(mode=0o700)
        for name, source in (("codex", patched or self.patched), (installer.CODE_MODE_HOST, self.helper)):
            (package / "bin" / name).write_bytes(source.read_bytes())
            (package / "bin" / name).chmod(0o700)
        record = {"target": installer.RUNTIME_TARGET,
                  "files": {name: installer.sha256(package / name) for name in sorted(installer.RUNTIME_FILES)}}
        if bind_record:
            self.args.runtime_package = package
            self.package_record = record
        return package, record

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

    def test_installed_root_default_selects_patch_and_preserves_invocation_and_settings(self):
        catalog = default_lain_fixture(self.codex_home)
        self.install()
        before = self.files(self.codex_home)
        code = "import json,os,pathlib;print(json.dumps({'executable':os.readlink('/proc/self/exe'), 'argv':pathlib.Path('/proc/self/cmdline').read_bytes().decode().split(chr(0))[:-1], 'env':dict(os.environ), 'cwd':os.getcwd()}))"
        prompt = self.base / "prompt.py"
        prompt.write_text(code)
        env = {"CODEX_HOME": str(self.codex_home), "HOME": str(self.base), "LANG": "C",
               "MARKER": "exact user value", "PATH": "/usr/bin:/bin", "PYTHONCOERCECLOCALE": "0"}
        for args in ([], ["--", str(prompt)]):
            with self.subTest(args=args):
                result = subprocess.run([str(self.entry), *args], input=code, env=env, cwd=self.base,
                                        capture_output=True, text=True, timeout=10)
                self.assertEqual(result.returncode, 0, result.stderr)
                actual = json.loads(result.stdout)
                self.assertEqual(actual["executable"], str(self.state / "codex-lain"))
                self.assertEqual(actual["argv"], [str(self.entry), *args])
                self.assertEqual(actual["env"], env)
                self.assertEqual(actual["cwd"], str(self.base))
        self.assertEqual(self.files(self.codex_home), before)
        self.assertTrue(catalog.exists())

    def test_installed_lain_default_keeps_original_parser_for_excluded_invocations(self):
        default_lain_fixture(self.codex_home)
        self.install()
        for args in (["exec"], ["--profile=other"], ["--profile=old-cpa"], ["--profile=lain", "resume"],
                     ["--unknown"], ["--help"], ["--version"], ["-c", 'model_provider="other"']):
            with self.subTest(args=args):
                invocation = [str(self.entry), *args]
                actual = subprocess.run(invocation, capture_output=True, timeout=10)
                direct = subprocess.run(invocation, executable=str(self.original), capture_output=True, timeout=10)
                self.assertEqual((actual.returncode, actual.stdout, actual.stderr),
                                 (direct.returncode, direct.stdout, direct.stderr))

    def test_installed_root_default_accepts_legacy_catalog_without_receipt(self):
        catalog = default_lain_fixture(self.codex_home)
        legacy = self.codex_home / "lain.models.json"
        legacy.write_bytes(catalog.read_bytes())
        config = self.codex_home / "config.toml"
        config.write_text(config.read_text().replace(json.dumps(str(catalog)), json.dumps(str(legacy))))
        (catalog.parent / "manifest.json").unlink()
        self.install()
        before = self.files(self.codex_home)
        result = subprocess.run([str(self.entry)], input="import os;print(os.readlink('/proc/self/exe'))",
                                capture_output=True, text=True, timeout=10)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout.strip(), str(self.state / "codex-lain"))
        self.assertEqual(self.files(self.codex_home), before)

    def test_installed_root_default_restores_environment_after_python_locale_coercion(self):
        default_lain_fixture(self.codex_home)
        self.patched.write_bytes(Path("/usr/bin/env").read_bytes())
        self.args.patched_sha256 = installer.sha256(self.patched)
        self.install()
        env = {"CODEX_HOME": str(self.codex_home), "LANG": "C", "MARKER": "still exact"}
        result = subprocess.run([str(self.entry)], env=env, capture_output=True, timeout=10)
        self.assertEqual(result.returncode, 0, result.stderr)
        actual = dict(entry.split(b"=", 1) for entry in result.stdout.splitlines())
        self.assertEqual(actual, {key.encode(): value.encode() for key, value in env.items()})

    def test_installed_default_evidence_failure_uses_original_but_bound_patch_failure_errors(self):
        catalog = default_lain_fixture(self.codex_home)
        self.install()
        code = "import os;print(os.readlink('/proc/self/exe'))"
        valid = catalog.read_bytes()
        document = json.loads(valid)
        del document["lain_model_picker"]
        catalog.write_text(json.dumps(document))
        fallback = subprocess.run([str(self.entry)], input=code, capture_output=True, text=True, timeout=10)
        self.assertEqual(fallback.returncode, 0, fallback.stderr)
        self.assertEqual(fallback.stdout.strip(), str(self.original))
        catalog.write_bytes(valid)
        helper = self.state / installer.CODE_MODE_HOST
        helper.unlink()
        broken = subprocess.run([str(self.entry)], input=code, capture_output=True, text=True, timeout=10)
        self.assertEqual(broken.returncode, 126)
        self.assertIn("runtime dependency codex-code-mode-host is missing", broken.stderr)
        self.assertEqual(broken.stdout, "")

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


    def test_helper_is_a_private_pinned_copy_in_manifest_and_receipt(self):
        self.install()
        installed = self.state / installer.CODE_MODE_HOST
        self.assertFalse(installed.is_symlink())
        self.assertNotEqual(installer.file_identity(installed), installer.file_identity(self.helper))
        self.assertEqual(installed.stat().st_mode & 0o777, 0o700)
        manifest = json.loads((self.state / "manifest.json").read_text())
        stored_receipt = json.loads((self.state / "receipt.json").read_text())
        self.assertEqual(manifest["runtimeDependencies"], stored_receipt["runtimeDependencies"])
        self.assertEqual(stored_receipt["runtimeDependencies"][installer.CODE_MODE_HOST]["sha256"], self.helper_sha)
        self.helper.unlink()
        self.assertEqual(installer.sha256(installed), self.helper_sha)
        self.assertTrue(installer.check_status(self.state)["readyForGroups"])

    def test_cli_helper_install_dry_run_and_installed_checker(self):
        harness = self.base / "installer-source"
        harness.mkdir()
        for name in ("install_codex_dispatch.py", "codex_dispatch.py"):
            (harness / name).write_bytes((HERE / name).read_bytes())
        (harness / "qualified-releases.json").write_bytes(self.fixture_catalog()[1])
        prefix = [sys.executable, str(harness / "install_codex_dispatch.py"), "--state", str(self.state)]
        command = ["install", "--entry", str(self.entry), "--patched", str(self.patched),
                   "--original-sha256", self.args.original_sha256, "--patched-sha256", self.args.patched_sha256,
                   "--code-mode-host", str(self.helper), "--code-mode-host-sha256", self.helper_sha]
        before = self.files()
        dry = subprocess.run([*prefix, *command], capture_output=True, text=True)
        self.assertEqual(dry.returncode, 0, dry.stderr)
        self.assertEqual(self.files(), before)
        self.assertFalse(json.loads(dry.stdout)["apply"])
        applied = subprocess.run([*prefix, "--apply", *command], capture_output=True, text=True)
        self.assertEqual(applied.returncode, 0, applied.stderr)
        checked = subprocess.run([sys.executable, str(self.state / "maintenance.py"), "--state", str(self.state),
                                  "status", "--json"], capture_output=True, text=True)
        self.assertEqual(checked.returncode, 0, checked.stderr)
        self.assertTrue(json.loads(checked.stdout)["readyForGroups"])
        self.assertEqual(installer.sha256(self.state / installer.CODE_MODE_HOST), self.helper_sha)

    def test_missing_helper_input_fails_before_install_or_dry_run(self):
        self.helper.unlink()
        before = self.files()
        for apply in (False, True):
            self.args.apply = apply
            with self.subTest(apply=apply), self.assertRaises(FileNotFoundError):
                self.install()
            self.assertEqual(self.files(), before)
            self.assertEqual(os.readlink(self.entry), "original")
            self.assertFalse(self.state.exists())

    def test_helper_explicit_sha_does_not_qualify_arbitrary_system_bytes(self):
        # A matching version claim must never be executed or accepted as provenance.
        marker = self.base / "unqualified-helper-ran"
        self.helper.write_text(f"#!/usr/bin/python3\nfrom pathlib import Path\nPath({str(marker)!r}).touch()\nprint('0.164.0')\n")
        self.args.code_mode_host_sha256 = installer.sha256(self.helper)
        before = self.files()
        with self.assertRaisesRegex(ValueError, "qualified release pin"):
            self.install()
        self.assertEqual(self.files(), before)
        self.assertFalse(marker.exists())
        self.assertFalse(self.state.exists())

    def test_helper_qualification_requires_paired_source_and_evidence(self):
        catalog, _ = self.fixture_catalog()
        qualified_patch = catalog["patched"][self.args.patched_sha256]
        for field, value in (("sha256", "0" * 63), ("sha256", "z" * 64),
                             ("version", "0.165.0"), ("upstreamCommit", "0" * 40),
                             ("provenance", "system-path"), ("evidence", "")):
            changed = json.loads(json.dumps(catalog))
            changed["patched"][self.args.patched_sha256]["runtimeDependencies"][installer.CODE_MODE_HOST][field] = value
            with self.subTest(field=field, value=value), mock.patch.object(installer, "qualifications", return_value=(changed, installer.json_bytes(changed))):
                with self.assertRaisesRegex(ValueError, "qualification"):
                    self.install()
            self.assertFalse(self.state.exists())
        qualified_patch.pop("runtimeDependencies")
        with mock.patch.object(installer, "qualifications", return_value=(catalog, installer.json_bytes(catalog))):
            with self.assertRaisesRegex(ValueError, "qualification is missing"):
                self.install()
        self.assertEqual(os.readlink(self.entry), "original")

    def test_missing_helper_qualification_is_not_ready_and_rebind_does_not_mutate(self):
        self.install()
        catalog, _ = self.fixture_catalog()
        catalog["patched"][self.args.patched_sha256].pop("runtimeDependencies")
        before = self.files()
        with mock.patch.object(installer, "qualifications", return_value=(catalog, installer.json_bytes(catalog))):
            status = installer.check_status(self.state)
            self.assertFalse(status["readyForGroups"])
            self.assertIn("helper-qualification-missing", [item["code"] for item in status["issues"]])
            with self.assertRaisesRegex(ValueError, "qualification is missing"):
                self.bind(self.binding_args(self.original))
        self.assertEqual(self.files(), before)

    def test_rebind_requires_paired_explicit_helper_arguments(self):
        self.install()
        for source, digest in ((self.helper, None), (None, self.helper_sha), (self.helper, "0" * 64)):
            args = self.binding_args(self.original)
            args.code_mode_host = source
            args.code_mode_host_sha256 = digest
            before = self.files()
            with self.subTest(source=source, digest=digest), self.assertRaisesRegex(ValueError, "helper SHA256|requires --code-mode-host"):
                self.bind(args)
            self.assertEqual(self.files(), before)

    def test_changed_helper_during_staging_never_publishes_install(self):
        copy = installer.shutil.copyfileobj

        def corrupt_copy(source, destination):
            copy(source, destination)
            destination.write(b"corrupted during staging")

        with mock.patch.object(installer.shutil, "copyfileobj", side_effect=corrupt_copy):
            with self.assertRaisesRegex(ValueError, "SHA256"):
                self.install()
        self.assertFalse(self.state.exists())
        self.assertEqual(os.readlink(self.entry), "original")
        self.assertFalse(list(self.base.glob(".codex-stage-*")))

    def test_helper_faults_are_not_ready_and_fail_before_patched_exec(self):
        marker = self.base / "patched-ran"
        self.patched.write_text(f"#!/usr/bin/python3\nfrom pathlib import Path\nPath({str(marker)!r}).touch()\n")
        self.args.patched_sha256 = installer.sha256(self.patched)
        self.install()
        helper = self.state / installer.CODE_MODE_HOST
        valid = helper.read_bytes()
        successful = subprocess.run([str(self.entry), "--profile=lain"], capture_output=True, text=True)
        self.assertEqual(successful.returncode, 0, successful.stderr)
        self.assertTrue(marker.exists())
        marker.unlink()
        for fault in ("missing", "changed", "non-executable", "symlink", "hardlink", "writable", "directory"):
            with self.subTest(fault=fault):
                helper.unlink()
                if fault == "symlink":
                    helper.symlink_to(self.helper)
                elif fault == "hardlink":
                    helper.hardlink_to(self.helper)
                elif fault == "directory":
                    helper.mkdir()
                elif fault != "missing":
                    helper.write_bytes(valid if fault in ("non-executable", "writable") else b"foreign helper")
                    helper.chmod(0o600 if fault == "non-executable" else 0o722 if fault == "writable" else 0o700)
                status = installer.check_status(self.state)
                self.assertFalse(status["readyForGroups"])
                self.assertIn("helper-missing-or-changed", [item["code"] for item in status["issues"]])
                result = subprocess.run([str(self.entry), "--profile=lain"], capture_output=True, text=True)
                self.assertEqual(result.returncode, 126, result.stderr)
                self.assertIn("runtime dependency codex-code-mode-host", result.stderr)
                self.assertFalse(marker.exists())
                ordinary = subprocess.run([str(self.entry), "-c", "print('original-ok')"], capture_output=True, text=True)
                self.assertEqual(ordinary.returncode, 0, ordinary.stderr)
                self.assertEqual(ordinary.stdout.strip(), "original-ok")
                if helper.is_dir() and not helper.is_symlink():
                    helper.rmdir()
                else:
                    helper.unlink(missing_ok=True)
                helper.write_bytes(valid)
                helper.chmod(0o700)

    def legacy_without_helper(self, schema=3):
        self.install()
        wrapper = self.state / "dispatch.py"
        legacy = wrapper.read_text().split("\n    if target == PATCHED:\n", 1)[0]
        legacy = "\n".join(line for line in legacy.splitlines() if not line.startswith("PATCHED_RUNTIME_DEPENDENCIES ="))
        legacy += '\n\nif __name__ == "__main__":\n    sys.exit(main())\n'
        installer.atomic_write(wrapper, legacy.encode(), 0o700)
        (self.state / installer.CODE_MODE_HOST).unlink()
        manifest = json.loads((self.state / "manifest.json").read_text())
        manifest.pop("runtimeDependencies")
        manifest.update(schema=schema, dispatcherSha256=installer.sha256(wrapper), dispatcherIdentity=installer.file_identity(wrapper))
        installer.atomic_write(self.state / "manifest.json", installer.json_bytes(manifest))
        installer.atomic_write(self.state / "receipt.json", installer.json_bytes(installer.receipt(manifest, self.state)))
        return wrapper.read_bytes()

    def with_helper(self, args):
        args.code_mode_host = self.helper
        args.code_mode_host_sha256 = self.helper_sha
        return args

    def test_legacy_helper_migration_and_rollback_restore_exact_prior_layout(self):
        for schema in (2, 3):
            with self.subTest(schema=schema):
                if self.state.exists():
                    # Each fixture has its own state; retain all prior transaction files.
                    self.entry.unlink()
                    self.entry.symlink_to("original")
                    self.state = self.base / f"client-{schema}"
                    self.args.state = self.state
                legacy_wrapper = self.legacy_without_helper(schema)
                status = installer.check_status(self.state)
                self.assertFalse(status["readyForGroups"])
                self.assertIn("helper-missing-or-changed", [item["code"] for item in status["issues"]])
                before = self.files()
                with self.assertRaises(FileNotFoundError):
                    self.bind(self.binding_args(self.original))
                self.assertEqual(self.files(), before)
                self.bind(self.with_helper(self.binding_args(self.original, False)))
                self.assertEqual(self.files(), before)
                self.bind(self.with_helper(self.binding_args(self.original)))
                self.assertTrue(installer.check_status(self.state)["readyForGroups"])
                transaction = next((self.state / "maintenance-history").iterdir()).name
                self.rollback(transaction)
                self.assertFalse((self.state / installer.CODE_MODE_HOST).exists())
                self.assertEqual((self.state / "dispatch.py").read_bytes(), legacy_wrapper)
                self.assertFalse(installer.check_status(self.state)["readyForGroups"])

    def test_manual_helper_symlink_is_reported_and_never_overwritten(self):
        self.legacy_without_helper()
        installed = self.state / installer.CODE_MODE_HOST
        installed.symlink_to(self.helper)
        before = self.files()
        identity = installer.file_identity(installed)
        status = installer.check_status(self.state)
        self.assertIn("helper-missing-or-changed", [item["code"] for item in status["issues"]])
        with self.assertRaisesRegex(ValueError, "symlinks"):
            self.bind(self.with_helper(self.binding_args(self.original)))
        self.assertEqual(self.files(), before)
        self.assertEqual(installer.file_identity(installed), identity)
        self.assertEqual(installer.sha256(self.helper), self.helper_sha)
        self.assertFalse((self.state / "maintenance-history").exists())

    def test_migration_interrupted_before_helper_copy_can_restore_legacy_wrapper(self):
        legacy_wrapper = self.legacy_without_helper()
        with mock.patch.object(installer, "atomic_binary_copy", side_effect=OSError("helper copy interrupted")):
            with self.assertRaisesRegex(OSError, "helper copy interrupted"):
                self.bind(self.with_helper(self.binding_args(self.original)))
        self.assertFalse(installer.check_status(self.state)["readyForGroups"])
        transaction = next((self.state / "maintenance-history").iterdir()).name
        self.rollback(transaction)
        self.assertEqual((self.state / "dispatch.py").read_bytes(), legacy_wrapper)
        self.assertFalse((self.state / installer.CODE_MODE_HOST).exists())

    def test_missing_managed_helper_repair_cannot_roll_back_to_broken_helper_binding(self):
        self.install()
        (self.state / installer.CODE_MODE_HOST).unlink()
        self.bind(self.with_helper(self.binding_args(self.original)))
        self.assertTrue(installer.check_status(self.state)["readyForGroups"])
        transaction = next((self.state / "maintenance-history").iterdir()).name
        before = self.files()
        with self.assertRaises(FileNotFoundError):
            self.rollback(transaction)
        self.assertEqual(self.files(), before)

    def prepare_helper_update(self):
        self.install()
        old_sha = self.helper_sha
        self.helper.write_bytes(Path("/usr/bin/env").read_bytes())
        self.helper_sha = installer.sha256(self.helper)
        return old_sha

    def test_qualified_helper_update_and_rollback_preserve_user_files(self):
        user_files = {".codex/config.toml": b"user config", ".codex/lain.config.toml": b"profile config",
                      ".codex/auth.json": b"private auth fixture", ".codex/sessions/history": b"user history"}
        for name, data in user_files.items():
            target = self.base / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
        old_sha = self.prepare_helper_update()
        original = self.original.read_bytes()
        patched = (self.state / "codex-lain").read_bytes()
        entry = installer.link_snapshot(self.entry)
        self.bind(self.with_helper(self.binding_args(self.original)))
        self.assertTrue(installer.check_status(self.state)["readyForGroups"])
        self.assertEqual(installer.sha256(self.state / installer.CODE_MODE_HOST), self.helper_sha)
        transaction = next((self.state / "maintenance-history").iterdir()).name
        backup = self.state / "maintenance-history" / transaction / "previous" / installer.CODE_MODE_HOST
        self.assertEqual(installer.sha256(backup), old_sha)
        self.rollback(transaction)
        self.assertEqual(installer.sha256(self.state / installer.CODE_MODE_HOST), old_sha)
        self.helper_sha = old_sha
        self.assertTrue(installer.check_status(self.state)["readyForGroups"])
        self.assertEqual(installer.link_snapshot(self.entry), entry)
        self.assertEqual(self.original.read_bytes(), original)
        self.assertEqual((self.state / "codex-lain").read_bytes(), patched)
        for name, data in user_files.items():
            self.assertEqual((self.base / name).read_bytes(), data)

    def test_interrupted_helper_update_rolls_back_verified_previous_helper(self):
        old_sha = self.prepare_helper_update()
        write = installer.atomic_write

        def fail_receipt(path, *args, **kwargs):
            if path == self.state / "receipt.json":
                raise OSError("interruption after helper update")
            return write(path, *args, **kwargs)

        with mock.patch.object(installer, "atomic_write", side_effect=fail_receipt):
            with self.assertRaisesRegex(OSError, "after helper update"):
                self.bind(self.with_helper(self.binding_args(self.original)))
        self.assertFalse(installer.check_status(self.state)["readyForGroups"])
        self.assertEqual(installer.sha256(self.state / installer.CODE_MODE_HOST), self.helper_sha)
        transaction = next((self.state / "maintenance-history").iterdir()).name
        self.rollback(transaction)
        self.helper_sha = old_sha
        self.assertTrue(installer.check_status(self.state)["readyForGroups"])

    def test_rollback_refuses_changed_current_or_backup_helper(self):
        self.install()
        self.bind(self.binding_args(self.original))
        transaction = next((self.state / "maintenance-history").iterdir()).name
        for root in (self.state, self.state / "maintenance-history" / transaction / "previous"):
            with self.subTest(root=root):
                helper = root / installer.CODE_MODE_HOST
                valid = helper.read_bytes()
                helper.write_bytes(b"unknown helper replacement")
                before = self.files()
                with self.assertRaisesRegex(ValueError, "changed"):
                    self.rollback(transaction)
                self.assertEqual(self.files(), before)
                helper.write_bytes(valid)

    def test_uninstall_can_restore_original_even_when_patch_helper_is_missing(self):
        self.install()
        (self.state / installer.CODE_MODE_HOST).unlink()
        self.uninstall()
        self.assertEqual(os.readlink(self.entry), "original")

    def test_runtime_package_complete_layout_and_default_exec_preserve_user_settings(self):
        package, record = self.package_fixture()
        default_lain_fixture(self.codex_home)
        before = self.files(self.codex_home)
        self.install()
        runtime = self.state / "runtime"
        manifest = json.loads((self.state / "manifest.json").read_text())
        self.assertEqual(manifest["patchedRelativePath"], "runtime/bin/codex")
        self.assertEqual(manifest["runtimePackage"], record)
        self.assertEqual(len([p for p in runtime.rglob("*") if p.is_file()]), 43)
        self.assertFalse((self.state / "codex-lain").exists())
        self.assertFalse((self.state / installer.CODE_MODE_HOST).exists())
        for name in (*record["files"], "bin/codex", "bin/" + installer.CODE_MODE_HOST):
            copied = runtime / name
            self.assertEqual(installer.sha256(copied), installer.sha256(package / name))
            self.assertNotEqual(installer.file_identity(copied), installer.file_identity(package / name))
            self.assertEqual(copied.stat().st_uid, os.getuid())
            self.assertEqual(copied.stat().st_nlink, 1)
            self.assertFalse(copied.stat().st_mode & 0o022)
        status = installer.check_status(self.state)
        self.assertTrue(status["readyForGroups"], status)
        self.assertEqual(status["receipt"]["runtimePackage"], record)
        env = {"CODEX_HOME": str(self.codex_home), "HOME": str(self.base), "PATH": "/usr/bin:/bin",
               "LANG": "C", "MARKER": "exact package environment", "PYTHONCOERCECLOCALE": "0"}
        code = "import os,json;print(json.dumps([os.readlink('/proc/self/exe'),dict(os.environ),os.getcwd()]))"
        result = subprocess.run([str(self.entry)], input=code, env=env, cwd=self.base,
                                capture_output=True, text=True, timeout=10)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout), [str(runtime / "bin/codex"), env, str(self.base)])
        self.assertEqual(self.files(self.codex_home), before)
        self.uninstall()
        self.assertEqual(os.readlink(self.entry), "original")

    def test_runtime_package_required_and_unqualified_input_refused_without_writes(self):
        package, _ = self.package_fixture()
        self.args.runtime_package = None
        for apply in (False, True):
            self.args.apply = apply
            before = self.files()
            with self.assertRaisesRegex(ValueError, "--runtime-package is required"):
                self.install()
            self.assertEqual(self.files(), before)
        self.package_record = None
        self.args.runtime_package = package
        with self.assertRaisesRegex(ValueError, "requires a qualified"):
            self.install()
        self.assertFalse(self.state.exists())

    def test_runtime_package_qualification_has_finite_paths_target_and_sha_pins(self):
        _, record = self.package_fixture()
        for change in ({"target": "x86_64-unknown-linux-musl"}, {"files": {}},
                       {"files": {**record["files"], "../escape": "0" * 64}},
                       {"files": {**record["files"], "/absolute": "0" * 64}},
                       {"files": {**record["files"], "bin/codex": "0" * 64}},
                       {"files": {**record["files"], "codex-path/rg": "z" * 64}}):
            with self.subTest(change=change.keys()):
                self.package_record = {**record, **change}
                with self.assertRaisesRegex(ValueError, "package qualification"):
                    self.install()
                self.assertFalse(self.state.exists())

    def test_runtime_package_source_rejects_missing_tampered_extra_and_unsafe_files(self):
        package, _ = self.package_fixture()
        path = package / "codex-resources/voice/NOTICE.md"
        valid = path.read_bytes()
        for fault in ("missing", "tampered", "symlink", "fifo", "hardlink", "writable", "extra", "directory-link"):
            with self.subTest(fault=fault):
                path.unlink()
                if fault == "tampered":
                    path.write_bytes(b"changed")
                elif fault == "symlink":
                    path.symlink_to(self.helper)
                elif fault == "fifo":
                    os.mkfifo(path, 0o600)
                elif fault == "hardlink":
                    os.link(self.helper, path)
                elif fault == "writable":
                    path.write_bytes(valid)
                    path.chmod(0o666)
                elif fault == "extra":
                    path.write_bytes(valid)
                    (package / "extra").touch()
                elif fault == "directory-link":
                    path.write_bytes(valid)
                    (package / "codex-path").rename(package / "saved-path")
                    (package / "codex-path").symlink_to(package / "saved-path", target_is_directory=True)
                with self.assertRaises((ValueError, OSError)):
                    self.install()
                self.assertFalse(self.state.exists())
                self.assertEqual(os.readlink(self.entry), "original")
                path.unlink(missing_ok=True)
                path.write_bytes(valid)
                path.chmod(0o600)
                (package / "extra").unlink(missing_ok=True)
                if fault == "directory-link":
                    (package / "codex-path").unlink()
                    (package / "saved-path").rename(package / "codex-path")

    def test_runtime_package_copy_corruption_never_publishes(self):
        self.package_fixture()
        copy = installer.atomic_binary_copy
        def corrupt(source, destination, *args, **kwargs):
            copy(source, destination, *args, **kwargs)
            if destination.name == "NOTICE.md":
                destination.write_bytes(b"corrupt staged package")
        with mock.patch.object(installer, "atomic_binary_copy", side_effect=corrupt):
            with self.assertRaisesRegex(ValueError, "SHA256"):
                self.install()
        self.assertFalse(self.state.exists())
        self.assertEqual(os.readlink(self.entry), "original")

    def test_runtime_package_tampering_blocks_only_patched_exec_and_maintenance(self):
        self.package_fixture()
        default_lain_fixture(self.codex_home)
        self.install()
        path = self.state / "runtime/codex-path/rg"
        valid = path.read_bytes()
        for fault in ("tampered", "missing", "symlink", "fifo", "writable"):
            with self.subTest(fault=fault):
                path.unlink()
                if fault == "tampered":
                    path.write_bytes(b"changed")
                elif fault == "symlink":
                    path.symlink_to(self.helper)
                elif fault == "fifo":
                    os.mkfifo(path, 0o600)
                elif fault == "writable":
                    path.write_bytes(valid)
                    path.chmod(0o777)
                status = installer.check_status(self.state)
                self.assertFalse(status["readyForGroups"], status)
                self.assertIn("runtime-package-missing-or-changed", [issue["code"] for issue in status["issues"]])
                result = subprocess.run([str(self.entry)], input="raise Exception('must not run')",
                                        capture_output=True, text=True, timeout=10)
                self.assertEqual(result.returncode, 126, result.stderr)
                self.assertIn("runtime package", result.stderr)
                ordinary = subprocess.run([str(self.entry), "-c", "print('original-ok')"],
                                          capture_output=True, text=True, timeout=10)
                self.assertEqual(ordinary.stdout.strip(), "original-ok")
                with self.assertRaises((ValueError, OSError)):
                    self.bind(self.binding_args(self.original))
                path.unlink(missing_ok=True)
                path.write_bytes(valid)
                path.chmod(0o700)

    def test_runtime_package_rebind_and_rollback_use_nested_helper_and_verify_package(self):
        self.package_fixture()
        self.install()
        before = self.files(self.state / "runtime")
        self.bind(self.binding_args(self.original))
        transaction = next((self.state / "maintenance-history").iterdir()).name
        previous = self.state / "maintenance-history" / transaction / "previous"
        self.assertTrue((previous / "runtime/bin" / installer.CODE_MODE_HOST).is_file())
        self.assertFalse((previous / installer.CODE_MODE_HOST).exists())
        path = self.state / "runtime/codex-resources/voice/NOTICE.md"
        valid = path.read_bytes()
        path.write_bytes(b"changed asset")
        with self.assertRaisesRegex(ValueError, "SHA256"):
            self.rollback(transaction)
        path.write_bytes(valid)
        self.rollback(transaction)
        self.assertTrue(installer.check_status(self.state)["readyForGroups"])
        self.assertEqual(self.files(self.state / "runtime"), before)

    def package_upgrade_fixture(self):
        old_wrapper = self.legacy_without_helper()
        legacy_helper = self.state / installer.CODE_MODE_HOST
        legacy_helper.symlink_to(self.helper)
        old_link = installer.link_snapshot(legacy_helper)
        old_patch = installer.file_identity(self.state / "codex-lain")
        new_patch = self.base / "new-patched"
        new_patch.write_bytes(self.patched.read_bytes() + b"new package fixture")
        new_patch.chmod(0o700)
        package, record = self.package_fixture(new_patch, False)
        patch_record = json.loads(json.dumps(self.fixture_catalog()[0]["patched"][self.args.patched_sha256]))
        patch_record["runtimePackage"] = record
        new_sha = installer.sha256(new_patch)
        self.extra_patch_records = {new_sha: patch_record}
        args = self.with_helper(self.binding_args(self.original))
        args.patched, args.patched_sha256, args.runtime_package = new_patch, new_sha, package
        return args, old_wrapper, old_link, old_patch

    def test_legacy_package_upgrade_preserves_symlink_and_supports_subsequent_rebind(self):
        args, _, old_link, old_patch = self.package_upgrade_fixture()
        args.apply = False
        before = self.files()
        self.bind(args)
        self.assertEqual(self.files(), before)
        args.apply = True
        self.bind(args)
        self.assertTrue(installer.check_status(self.state)["readyForGroups"])
        self.assertEqual(installer.link_snapshot(self.state / installer.CODE_MODE_HOST), old_link)
        self.assertEqual(installer.file_identity(self.state / "codex-lain"), old_patch)
        manifest = json.loads((self.state / "manifest.json").read_text())
        receipt = json.loads((self.state / "receipt.json").read_text())
        self.assertEqual(manifest["preservedLegacyRuntime"], receipt["preservedLegacyRuntime"])
        self.bind(self.binding_args(self.original))
        self.assertTrue(installer.check_status(self.state)["readyForGroups"])
        self.assertEqual(installer.link_snapshot(self.state / installer.CODE_MODE_HOST), old_link)

    def test_legacy_package_upgrade_rollback_retains_both_runtimes_and_unqualified_link(self):
        args, old_wrapper, old_link, old_patch = self.package_upgrade_fixture()
        self.bind(args)
        runtime = self.files(self.state / "runtime")
        transaction = next((self.state / "maintenance-history").iterdir()).name
        self.rollback(transaction, False)
        self.rollback(transaction)
        self.assertEqual((self.state / "dispatch.py").read_bytes(), old_wrapper)
        self.assertEqual(installer.link_snapshot(self.state / installer.CODE_MODE_HOST), old_link)
        self.assertEqual(installer.file_identity(self.state / "codex-lain"), old_patch)
        self.assertEqual(self.files(self.state / "runtime"), runtime)
        self.assertFalse(installer.check_status(self.state)["readyForGroups"])
        # Ordinary helper maintenance still refuses the preserved legacy symlink.
        with self.assertRaisesRegex(ValueError, "symlinks"):
            self.bind(self.with_helper(self.binding_args(self.original)))

    def test_legacy_package_upgrade_interruption_can_restore_verified_old_wrapper(self):
        args, old_wrapper, old_link, _ = self.package_upgrade_fixture()
        write = installer.atomic_write
        def interrupt(path, *values, **kwargs):
            if path == self.state / "receipt.json":
                raise OSError("package upgrade interruption")
            return write(path, *values, **kwargs)
        with mock.patch.object(installer, "atomic_write", side_effect=interrupt):
            with self.assertRaisesRegex(OSError, "upgrade interruption"):
                self.bind(args)
        transaction = next((self.state / "maintenance-history").iterdir()).name
        self.rollback(transaction)
        self.assertEqual((self.state / "dispatch.py").read_bytes(), old_wrapper)
        self.assertEqual(installer.link_snapshot(self.state / installer.CODE_MODE_HOST), old_link)

    def test_legacy_package_upgrade_refuses_changed_legacy_link_or_package_on_rollback(self):
        args, _, old_link, _ = self.package_upgrade_fixture()
        self.bind(args)
        transaction = next((self.state / "maintenance-history").iterdir()).name
        link = self.state / installer.CODE_MODE_HOST
        link.unlink()
        link.symlink_to(self.original)
        with self.assertRaisesRegex(ValueError, "preserved legacy runtime changed"):
            self.rollback(transaction)
        self.assertEqual(os.readlink(link), str(self.original))

    def test_runtime_package_old_template_compatibility_and_package_placeholder_required(self):
        dependencies = installer.qualified_dependencies(self.fixture_catalog()[0]["patched"][self.args.patched_sha256])
        old_template = (HERE / "codex_dispatch.py").read_text().replace(
            'PATCHED_RUNTIME_PACKAGE = "@LAIN_PATCHED_RUNTIME_PACKAGE@"\n', '')
        legacy = installer.render_dispatch(self.original, self.state / "codex-lain", self.args.original_sha256,
                                           self.args.patched_sha256, old_template, dependencies)
        self.assertNotIn(b"@LAIN_PATCHED", legacy)
        _, record = self.package_fixture()
        with self.assertRaisesRegex(ValueError, "lacks runtime package checks"):
            installer.render_dispatch(self.original, self.state / "runtime/bin/codex", self.args.original_sha256,
                                      self.args.patched_sha256, old_template, dependencies, record)

    def remount_fixture(self):
        manifest_path = self.state / "manifest.json"
        manifest = json.loads(manifest_path.read_text())
        for identity in (manifest["originalIdentity"], manifest["dispatcherIdentity"], manifest["installedEntry"]["identity"]):
            identity["device"] += 100
        installer.atomic_write(manifest_path, installer.json_bytes(manifest))
        installer.atomic_write(self.state / "receipt.json", installer.json_bytes(installer.receipt(manifest, self.state)))
        return type("Accept", (), {"state": self.state, "expect_manifest_sha256": installer.sha256(manifest_path), "apply": True})()

    def accept_identities(self, args):
        with contextlib.redirect_stdout(io.StringIO()):
            installer.reaccept_identities(args)

    def test_explicit_device_only_reacceptance_is_read_only_until_apply_and_preserves_payloads(self):
        self.install()
        args = self.remount_fixture()
        before = self.files()
        args.apply = False
        self.accept_identities(args)
        self.assertEqual(self.files(), before)
        self.assertFalse(installer.check_status(self.state)["readyForGroups"])
        args.apply = True
        self.accept_identities(args)
        self.assertTrue(installer.check_status(self.state)["readyForGroups"])
        after = self.files()
        changed = {name for name in before if before[name] != after[name]}
        self.assertEqual(changed, {"client/manifest.json", "client/receipt.json"})
        transaction = next((self.state / "maintenance-history").iterdir())
        self.assertEqual((transaction / "previous/manifest.json").read_bytes(), before["client/manifest.json"])
        self.assertEqual((transaction / "previous/receipt.json").read_bytes(), before["client/receipt.json"])

    def test_reaccepted_legacy_baseline_supports_package_upgrade_and_verified_rollback(self):
        upgrade_args, old_wrapper, old_link, _ = self.package_upgrade_fixture()
        self.accept_identities(self.remount_fixture())
        accepted = json.loads((self.state / "manifest.json").read_text())
        self.assertEqual(accepted["originalIdentity"], installer.file_identity(self.original))
        upgrade_args.expect_manifest_sha256 = installer.sha256(self.state / "manifest.json")
        self.bind(upgrade_args)
        transactions = list((self.state / "maintenance-history").iterdir())
        transaction = next(p.name for p in transactions if "packageUpgrade" in json.loads((p / "transaction.json").read_text()))
        self.rollback(transaction)
        self.assertEqual((self.state / "dispatch.py").read_bytes(), old_wrapper)
        self.assertEqual(installer.link_snapshot(self.state / installer.CODE_MODE_HOST), old_link)
        self.assertFalse(installer.check_status(self.state)["readyForGroups"])

    def test_identity_reacceptance_refuses_stale_manifest_inode_or_inconsistent_device_changes(self):
        self.install()
        args = self.remount_fixture()
        baseline = json.loads((self.state / "manifest.json").read_text())
        for fault in ("stale", "inode", "mixed-device", "no-device-change"):
            manifest = json.loads(json.dumps(baseline))
            if fault == "inode":
                manifest["dispatcherIdentity"]["inode"] += 1
            elif fault == "mixed-device":
                manifest["originalIdentity"]["device"] += 1
            elif fault == "no-device-change":
                for identity in (manifest["originalIdentity"], manifest["dispatcherIdentity"], manifest["installedEntry"]["identity"]):
                    identity["device"] -= 100
            installer.atomic_write(self.state / "manifest.json", installer.json_bytes(manifest))
            installer.atomic_write(self.state / "receipt.json", installer.json_bytes(installer.receipt(manifest, self.state)))
            args.expect_manifest_sha256 = "0" * 64 if fault == "stale" else installer.sha256(self.state / "manifest.json")
            before = self.files()
            with self.subTest(fault=fault), self.assertRaises(ValueError):
                self.accept_identities(args)
            self.assertEqual(self.files(), before)

    def test_identity_reacceptance_refuses_changed_wrapper_bytes_or_entry_target(self):
        self.install()
        args = self.remount_fixture()
        wrapper = self.state / "dispatch.py"
        valid = wrapper.read_bytes()
        wrapper.write_bytes(valid + b"# unknown edit\n")
        before = self.files()
        with self.assertRaisesRegex(ValueError, "SHA256"):
            self.accept_identities(args)
        self.assertEqual(self.files(), before)
        wrapper.write_bytes(valid)
        self.entry.unlink()
        self.entry.symlink_to(self.original)
        with self.assertRaisesRegex(ValueError, "target changed"):
            self.accept_identities(args)


if __name__ == "__main__":
    unittest.main()
