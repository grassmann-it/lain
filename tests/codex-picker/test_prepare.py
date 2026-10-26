#!/usr/bin/env python3
"""Offline source pins, release-lock checks and native fixture contracts; no client runs."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
from types import SimpleNamespace
import unittest

spec = importlib.util.spec_from_file_location("prepare_codex", Path(__file__).resolve().parents[2] / "scripts" / "prepare-codex-picker.py")
prepare = importlib.util.module_from_spec(spec)
spec.loader.exec_module(prepare)

native_spec = importlib.util.spec_from_file_location("native_picker", Path(__file__).with_name("native_model_picker.py"))
native = importlib.util.module_from_spec(native_spec)
native_spec.loader.exec_module(native)


class BundleVerification(unittest.TestCase):
    def test_every_source_patch_matches_its_manifest_pin(self):
        bundle = Path(__file__).resolve().parents[2] / "clients/codex"
        manifest = json.loads((bundle / "upstream.json").read_text())
        self.assertEqual(set(manifest["patches"]), set(manifest["patchSha256"]))
        for relative in manifest["patches"]:
            with self.subTest(patch=relative):
                patch = bundle / relative
                self.assertEqual(hashlib.sha256(patch.read_bytes()).hexdigest(), manifest["patchSha256"][relative])
                result = subprocess.run(["git", "apply", "--numstat", str(patch)], capture_output=True, text=True)
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertTrue(result.stdout.strip(), "source patch has no changes")


class NativeFixtureContracts(unittest.TestCase):
    def setUp(self):
        self.scratch = tempfile.TemporaryDirectory(prefix="lain-picker-offline-")
        self.addCleanup(self.scratch.cleanup)
        self.root = Path(self.scratch.name)
        source = self.root / "source"
        seed = source / "codex-rs/models-manager/models.json"
        seed.parent.mkdir(parents=True)
        seed.write_text(json.dumps({"models": [{}]}))
        binary = self.root / "inert-binary"
        binary.write_bytes(b"offline fixture; never executed")
        self.output = self.root / "fixtures"
        self.cases = native.prepare(SimpleNamespace(source=source, binary=binary, output=self.output))

    def plan(self, name):
        return json.loads((self.output / name / "plan.json").read_text())

    def codex_dir(self, name):
        return self.output / name / "private-home/.codex"

    def persist(self, name, filename):
        path = self.codex_dir(name) / filename
        path.write_text(path.read_text().replace('model = "synthetic/free-a"', 'model = "synthetic/shared"'))

    def test_default_and_lain_are_grouped_other_profiles_and_providers_stay_upstream(self):
        for name in ("lain-groups", "normal-provider-lain", "default-native-flags", "lain-native-flags"):
            with self.subTest(case=name):
                self.assertTrue(self.plan(name)["grouped"])
        for name in ("other-profile-provider-lain", "lain-profile-other-provider", "normal-other-provider",
                     "normal-other-provider-invalid-extension", "normal-without-extension",
                     "other-profile-invalid-extension", "other-profile-duplicate-extension"):
            with self.subTest(case=name):
                self.assertEqual(self.plan(name)["expected"], "upstream")

    def test_flagless_default_and_explicit_profile_commands_are_separate_from_embedded_cases(self):
        self.assertEqual(native.native_command(self.plan("default-native-flags")), ["/mnt/codex"])
        self.assertEqual(native.native_command(self.plan("lain-native-flags")), ["/mnt/codex", "--profile", "lain"])
        self.assertIn("--no-daemon", native.native_command(self.plan("normal-provider-lain")))
        packaged = dict(self.plan("default-native-flags"), packageRoot="/explicit/cache/package")
        self.assertEqual(native.native_command(packaged), ["/mnt/package/bin/codex"])
        with self.assertRaisesRegex(AssertionError, "private daemon socket"):
            native.daemon_evidence(self.codex_dir("default-native-flags").parent, self.plan("default-native-flags"))

    def test_malformed_and_duplicate_metadata_require_bounded_catalog_refusal(self):
        for name in ("normal-invalid-extension", "lain-profile-invalid-extension", "normal-duplicate-extension"):
            with self.subTest(case=name):
                plan = self.plan(name)
                self.assertEqual(plan["expected"], "catalog-error")
                self.assertFalse(plan["grouped"])
                self.assertTrue(plan["catalogErrorContains"])
        contents = (self.codex_dir("normal-duplicate-extension") / "models.json").read_text()
        self.assertEqual(contents.count('"lain_model_picker":'), 2)
        without = json.loads((self.codex_dir("normal-without-extension") / "models.json").read_text())
        self.assertNotIn("lain_model_picker", without)

    def test_default_selection_changes_only_the_main_wire_model(self):
        name = "normal-provider-lain"
        self.persist(name, "config.toml")
        checks = native.configuration_checks(self.codex_dir(name), self.plan(name))
        self.assertTrue(checks["configWritesValid"])
        self.assertFalse(checks["mainSentinelUnchanged"])
        path = self.codex_dir(name) / "config.toml"
        path.write_text(path.read_text().replace('model_provider = "lain"', 'model_provider = "fixture"'))
        with self.assertRaisesRegex(AssertionError, "exactly the selected wire model"):
            native.configuration_checks(self.codex_dir(name), self.plan(name))

    def test_named_profile_selection_preserves_main_and_other_profile_bytes(self):
        name = "lain-groups"
        self.persist(name, "lain.config.toml")
        checks = native.configuration_checks(self.codex_dir(name), self.plan(name))
        self.assertTrue(checks["mainSentinelUnchanged"])
        self.persist(name, "other.config.toml")
        with self.assertRaisesRegex(AssertionError, "unselected or read-only configuration"):
            native.configuration_checks(self.codex_dir(name), self.plan(name))

    def test_catalog_refusal_and_upstream_cases_cannot_write_any_configuration(self):
        for name in ("normal-invalid-extension", "other-profile-provider-lain"):
            with self.subTest(case=name):
                self.assertTrue(native.configuration_checks(self.codex_dir(name), self.plan(name))["mainSentinelUnchanged"])
                self.persist(name, "config.toml")
                with self.assertRaisesRegex(AssertionError, "unselected or read-only configuration"):
                    native.configuration_checks(self.codex_dir(name), self.plan(name))

    def test_preparation_records_unexecuted_fixtures_and_preserves_home(self):
        receipt = json.loads((self.output / "prepared.json").read_text())
        self.assertEqual(receipt["status"], "prepared-not-executed")
        self.assertEqual(len(receipt["cases"]), len(self.cases))
        self.assertEqual(self.plan("default-native-flags")["homePath"], str(Path.home()))
        self.assertFalse(list(self.output.glob("*/result.json")))

    def test_explicit_package_preparation_pins_resources_without_running_or_changing_them(self):
        package = self.root / "explicit-package"
        for relative in ("codex-package.json", "bin/codex-code-mode-host", "codex-path/rg", "codex-resources/bwrap"):
            path = package / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("inert package fixture")
        before = {str(path.relative_to(package)): native.digest(path) for path in package.rglob("*") if path.is_file()}
        output = self.root / "packaged-fixtures"
        native.prepare(SimpleNamespace(source=self.root / "source", binary=self.root / "inert-binary", package=package, output=output))
        plan = json.loads((output / "default-native-flags/plan.json").read_text())
        self.assertEqual(plan["packageRoot"], str(package))
        self.assertEqual(plan["packageFileSha256"], before)
        self.assertEqual(native.native_command(plan), ["/mnt/package/bin/codex"])
        self.assertEqual(plan["binarySha256"], native.digest(self.root / "inert-binary"))
        self.assertEqual({str(path.relative_to(package)): native.digest(path) for path in package.rglob("*") if path.is_file()}, before)

    def test_installed_launcher_smoke_uses_known_lain_catalog_and_exact_wrapper(self):
        state = self.root / "installed-state"
        state.mkdir()
        launcher = state / "dispatch.py"
        launcher.write_text("inert launcher; never executed")
        output = self.root / "launcher-fixtures"
        native.prepare(SimpleNamespace(source=self.root / "source", binary=self.root / "inert-binary",
                                       dispatcher_state=state, output=output))
        plan = json.loads((output / "default-native-flags/plan.json").read_text())
        self.assertEqual(native.native_command(plan), [str(launcher)])
        self.assertEqual(plan["launcherSha256"], native.digest(launcher))
        codex = output / "default-native-flags/private-home/.codex"
        self.assertTrue((codex / "lain.models.json").is_file())
        self.assertIn(".codex/lain.models.json", (codex / "config.toml").read_text())
        self.assertFalse((codex / "models.json").exists())


class LockVerification(unittest.TestCase):
    def setUp(self):
        self.original = {"version": 4, "package": [
            {"name": "local", "version": "0.0.0", "dependencies": ["external"]},
            {"name": "external", "version": "1.2.3", "source": "registry+synthetic", "checksum": "unchanged", "dependencies": ["transitive"]},
        ]}
        self.normalized = copy.deepcopy(self.original)
        self.normalized["package"][0]["version"] = "0.164.0"
        self.policy = {"oldLocalVersion": "0.0.0", "newLocalVersion": "0.164.0", "changedLocalPackages": 1, "externalPackages": 1}

    def test_only_local_workspace_version_correction_is_accepted(self):
        checks = prepare.verify_workspace_lock(self.original, self.normalized, self.policy)
        self.assertTrue(checks["externalPackageRecordsUnchanged"])
        self.assertEqual(checks["localVersionChanges"], 1)

    def test_external_record_including_dependency_list_cannot_change(self):
        self.normalized["package"][1]["dependencies"].append("new-dependency")
        with self.assertRaisesRegex(ValueError, "external"):
            prepare.verify_workspace_lock(self.original, self.normalized, self.policy)

    def test_local_dependency_record_cannot_change(self):
        self.normalized["package"][0]["dependencies"].append("new-dependency")
        with self.assertRaisesRegex(ValueError, "local"):
            prepare.verify_workspace_lock(self.original, self.normalized, self.policy)

    def test_package_count_and_lock_metadata_are_pinned(self):
        self.normalized["version"] = 5
        with self.assertRaisesRegex(ValueError, "metadata"):
            prepare.verify_workspace_lock(self.original, self.normalized, self.policy)


if __name__ == "__main__":
    unittest.main()
