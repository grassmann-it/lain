"""Offline maintenance fixtures. No real VS Code, provider, or user state."""
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import stat
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("vscode_groups", ROOT / "scripts/clients/vscode_groups.py")
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


class MaintenanceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="lain-vscode-offline-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / "source"
        self.extensions = self.root / "extensions"
        self.user_data = self.root / "user-data"
        self.bundle = self.root / "bundle"
        self.pins = self.root / "input.json"
        self.code = Path("/qualified/code")
        for path in (self.source, self.extensions, self.user_data):
            path.mkdir()
        package = {"publisher": "openai", "name": "chatgpt", "version": m.ORIGINAL_VERSION,
                   "displayName": "Codex", "engines": {"vscode": "^1.96.2"},
                   "main": "out/extension.js", "extensionPack": ["unrelated.pack"],
                   "__metadata": {"targetPlatform": m.PLATFORM, "installedTimestamp": 1}}
        files = {"package.json": m.encoded(package), "out/extension.js": b"original extension;\n",
                 "webview/assets/model-picker-options-2e5c9d5f09f6.js": b"original picker;\n",
                 "webview/assets/access-row-56c64d268a49.js": b"original rows;\n",
                 m.RUNTIME: b"never execute this fixture\n", "LICENSE.md": b"original terms\n",
                 "resources/icon.png": b"fixture\n", "bin/linux-x86_64/codex-code-mode-host": b"helper\n",
                 ".vsixmanifest": (f'<PackageManifest xmlns="{m.NS}" Version="2.0.0">'
                    '<Metadata><Identity Language="en-US" Id="chatgpt" Publisher="openai" '
                    f'Version="{m.ORIGINAL_VERSION}" TargetPlatform="{m.PLATFORM}"/>'
                    '<DisplayName>Codex</DisplayName></Metadata><Installation>'
                    '<InstallationTarget Id="Microsoft.VisualStudio.Code"/></Installation>'
                    '<Dependencies/><Assets><Asset Type="Microsoft.VisualStudio.Code.Manifest" '
                    'Path="extension/package.json" Addressable="true"/></Assets></PackageManifest>').encode()}
        for rel, data in files.items():
            target = self.source / rel
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
            target.chmod(0o755 if rel.startswith("bin/") else 0o644)
        (self.source / "empty-directory").mkdir()
        actual = {rel: info["sha256"] for rel, info in m.inventory(self.source).items() if info["kind"] == "file"}
        self.pins.write_bytes(m.encoded({"schemaVersion": 1, "extensionId": m.EXTENSION_ID,
            "version": m.ORIGINAL_VERSION, "targetPlatform": m.PLATFORM, "files": actual}))
        pins_patch = patch.object(m, "PINS", {rel: actual[rel] for rel in m.PINS})
        pins_patch.start()
        self.addCleanup(pins_patch.stop)
        self.original = self.extensions / f"{m.EXTENSION_ID}-{m.ORIGINAL_VERSION}-{m.PLATFORM}"
        shutil.copytree(self.source, self.original)
        self.record = self.make_record(self.original, m.ORIGINAL_VERSION)
        self.other = {"identifier": {"id": "unrelated.extension"}, "version": "1.0.0", "opaque": {"keep": True}}
        self.write_registry([self.other, self.record])
        self.before = m.inventory(self.source)

    def make_record(self, location, version, local=False):
        return {"identifier": {"id": m.EXTENSION_ID}, "version": version,
                "relativeLocation": location.name,
                "location": {"scheme": "file", "path": str(location), "fsPath": str(location)},
                "metadata": {"targetPlatform": "undefined" if local else m.PLATFORM, "source": "vsix" if local else "gallery",
                             "pinned": local, "id": "original-uuid", "publisherId": "original-publisher",
                             "isApplicationScoped": False, "isMachineScoped": True, "isBuiltin": False}}

    def write_registry(self, entries):
        (self.extensions / "extensions.json").write_bytes(m.encoded(entries))

    def transform(self, source, dest):
        shutil.copytree(source, dest)
        (dest / "out/extension.js").write_bytes(b"modified extension;\n")
        for rel in ("out/lain-groups-host.cjs", "webview/assets/lain-groups-query.js", "webview/assets/lain-groups-ui.js"):
            (dest / rel).write_bytes(b"local fixture;\n")
        package = json.loads((dest / "package.json").read_text())
        package["displayName"] += " (Lain local groups)"
        package["lainLocalModelGroups"] = {"revision": 1, "upstreamVersion": m.ORIGINAL_VERSION, "official": False}
        (dest / "package.json").write_bytes(m.encoded(package))
        return {"files": {"out/extension.js": m.file_hash(dest / "out/extension.js")}}

    def prepare(self, transform=None):
        self.prepared = m.prepare(self.source, self.bundle, self.pins, transform or self.transform)
        return self.prepared

    def fake_cli(self, command):
        self.assertEqual(command[1:5], ["--user-data-dir", str(self.user_data), "--extensions-dir", str(self.extensions)])
        self.assertIn("--force", command)
        self.assertIn("--do-not-include-pack-dependencies", command)
        self.assertIn("--do-not-sync", command)
        self.assertNotIn("--uninstall-extension", command)
        package_path = Path(command[command.index("--install-extension") + 1])
        with zipfile.ZipFile(package_path) as archive:
            package = json.loads(archive.read("extension/package.json"))
            version = package["version"]
            dest = self.extensions / f"{m.EXTENSION_ID}-{version}"
            if dest.exists():
                shutil.rmtree(dest)
            dest.mkdir()
            for entry in archive.infolist():
                if entry.filename.startswith("extension/"):
                    rel = entry.filename[len("extension/"):]
                else:
                    continue
                target = dest / rel
                if entry.is_dir():
                    target.mkdir(parents=True, exist_ok=True)
                else:
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(archive.read(entry))
                target.chmod(stat.S_IMODE(entry.external_attr >> 16))
            package["__metadata"] = {"installedTimestamp": 99, "targetPlatform": m.PLATFORM}
            (dest / "package.json").write_bytes(m.encoded(package))
        self.write_registry([self.other, self.make_record(dest, version, True)])

    def maintain(self, **kwargs):
        return m.maintain(self.bundle, self.extensions, self.user_data, self.code,
                          self.prepared["manifestSha256"], runner=self.fake_cli, isolation_root=self.root, **kwargs)

    def test_prepare_preserves_full_original_and_runtime(self):
        result = self.prepare()
        self.assertEqual(result["code"], "prepared")
        self.assertFalse(result["activationQualified"])
        self.assertEqual(m.inventory(self.bundle / "original"), self.before)
        self.assertEqual(m.inventory(self.source), self.before)
        manifest, _ = m.load_bundle(self.bundle)
        self.assertEqual(manifest["original"][m.RUNTIME], manifest["candidate"][m.RUNTIME])
        self.assertEqual(manifest["original"]["LICENSE.md"], manifest["candidate"]["LICENSE.md"])
        package = m.read_json(self.bundle / "candidate/package.json")
        self.assertEqual(package["version"], m.LOCAL_VERSION)
        self.assertEqual(package["displayName"], "Codex (Lain local groups)")
        self.assertEqual(package["name"], "chatgpt")
        self.assertEqual(package["publisher"], "openai")
        self.assertNotIn("__metadata", package)
        with zipfile.ZipFile(self.bundle / "candidate.vsix") as archive:
            self.assertIn("extension/empty-directory/", archive.namelist())
            self.assertEqual(stat.S_IMODE(archive.getinfo("extension/" + m.RUNTIME).external_attr >> 16), 0o755)
            self.assertIn(m.LOCAL_VERSION.encode(), archive.read("extension.vsixmanifest"))

    def test_install_rollback_standard_cli_and_state_preserved(self):
        self.prepare()
        (self.user_data / "opaque-state").write_bytes(b"preserve state\n")
        result = self.maintain(apply=True)
        self.assertEqual(result["code"], "reload-needed")
        self.assertFalse(result["reloadPerformed"])
        self.assertEqual(m.inventory(self.original), self.before)
        status = m.status(self.bundle, self.extensions)
        self.assertEqual(status["code"], "reload-needed")
        self.assertFalse(status["readyForGroups"])
        self.assertEqual(status["runtimeActivation"], "not-observed")
        result = self.maintain(apply=True, rollback=True)
        self.assertEqual(result["selectedVariant"], "original")
        self.assertEqual(m.inventory(self.original), self.before)
        self.assertTrue((self.extensions / f"{m.EXTENSION_ID}-{m.LOCAL_VERSION}").is_dir())
        self.assertEqual((self.user_data / "opaque-state").read_bytes(), b"preserve state\n")
        self.assertEqual(m.status(self.bundle, self.extensions)["code"], "original-selected")
        self.assertEqual(m.registry(self.extensions)[0][0], self.other)

    def test_dry_run_creates_no_lock_or_transaction_and_runs_no_cli(self):
        self.prepare()
        before = m.inventory(self.bundle)
        result = self.maintain()
        self.assertEqual(result["code"], "install-planned")
        self.assertEqual(m.inventory(self.bundle), before)

    def test_status_is_read_only(self):
        self.prepare()
        before = m.inventory(self.bundle)
        self.assertEqual(m.status(self.bundle, self.extensions)["code"], "original-selected")
        self.assertEqual(m.inventory(self.bundle), before)

    def test_requires_exact_complete_input_pins(self):
        pins = m.read_json(self.pins)
        del pins["files"]["resources/icon.png"]
        self.pins.write_bytes(m.encoded(pins))
        with self.assertRaisesRegex(m.MaintenanceError, "complete original"):
            self.prepare()
        self.assertFalse(self.bundle.exists())

    def test_rejects_manifest_forging_known_input_pin(self):
        (self.source / m.RUNTIME).write_bytes(b"different runtime")
        pins = m.read_json(self.pins)
        pins["files"][m.RUNTIME] = m.file_hash(self.source / m.RUNTIME)
        self.pins.write_bytes(m.encoded(pins))
        with self.assertRaisesRegex(m.MaintenanceError, "qualified original"):
            self.prepare()

    def test_rejects_changed_runtime_in_transform(self):
        def change(source, dest):
            report = self.transform(source, dest)
            (dest / m.RUNTIME).write_bytes(b"different runtime")
            return report
        with self.assertRaisesRegex(m.MaintenanceError, "protected resource"):
            self.prepare(change)

    def test_rejects_transform_dropping_resource(self):
        def change(source, dest):
            report = self.transform(source, dest)
            (dest / "resources/icon.png").unlink()
            return report
        with self.assertRaisesRegex(m.MaintenanceError, "removed original"):
            self.prepare(change)

    def test_rejects_transform_modifying_original(self):
        def change(source, dest):
            report = self.transform(source, dest)
            (source / "resources/icon.png").write_bytes(b"mutated source")
            return report
        with self.assertRaisesRegex(m.MaintenanceError, "original backup"):
            self.prepare(change)
        self.assertEqual(m.inventory(self.source), self.before)

    def test_rejects_transform_changing_commands(self):
        def change(source, dest):
            report = self.transform(source, dest)
            p = m.read_json(dest / "package.json")
            p["main"] = "unqualified.js"
            (dest / "package.json").write_bytes(m.encoded(p))
            return report
        with self.assertRaisesRegex(m.MaintenanceError, "protected package"):
            self.prepare(change)

    def test_existing_destination_never_overwritten(self):
        self.bundle.mkdir()
        sentinel = self.bundle / "preserve"
        sentinel.write_text("keep")
        with self.assertRaisesRegex(m.MaintenanceError, "already exists"):
            self.prepare()
        self.assertEqual(sentinel.read_text(), "keep")

    def test_atomic_publish_rejects_racing_destination(self):
        source = self.root / "publish-source"
        dest = self.root / "publish-dest"
        source.mkdir()
        dest.mkdir()
        with self.assertRaises(FileExistsError):
            m.publish(source, dest)
        self.assertTrue(source.is_dir())
        self.assertTrue(dest.is_dir())

    def test_rejects_symlink_and_hardlink_source(self):
        target = self.source / "resources/icon.png"
        target.unlink()
        target.symlink_to(self.source / "LICENSE.md")
        with self.assertRaises(m.MaintenanceError):
            self.prepare()
        target.unlink()
        os.link(self.source / "LICENSE.md", target)
        with self.assertRaises(m.MaintenanceError):
            self.prepare()

    def test_rejects_writable_source_and_symlink_ancestor(self):
        (self.source / "resources/icon.png").chmod(0o666)
        with self.assertRaisesRegex(m.MaintenanceError, "permissions"):
            self.prepare()
        alias = self.root / "alias"
        alias.symlink_to(self.source, target_is_directory=True)
        with self.assertRaisesRegex(m.MaintenanceError, "real directory"):
            m.inventory(alias)

    def test_stale_candidate_pin_aborts_before_mutation(self):
        self.prepare()
        self.prepared["manifestSha256"] = "0" * 64
        with self.assertRaisesRegex(m.MaintenanceError, "stale"):
            self.maintain(apply=True)
        self.assertFalse((self.bundle / ".maintenance.lock").exists())

    def test_missing_apply_pin_is_refused(self):
        self.prepare()
        with self.assertRaisesRegex(m.MaintenanceError, "requires --expect"):
            m.maintain(self.bundle, self.extensions, self.user_data, self.code, None, apply=True)

    def test_unknown_update_not_overwritten_or_reported_ready(self):
        self.prepare()
        record = dict(self.record, version="26.1022.10000")
        self.write_registry([self.other, record])
        self.assertIn("unknown extension update", m.status(self.bundle, self.extensions)["issue"])
        with self.assertRaisesRegex(m.MaintenanceError, "unknown extension update"):
            self.maintain(apply=True)

    def test_duplicate_selected_variants_refused(self):
        self.prepare()
        self.write_registry([self.record, self.record])
        with self.assertRaisesRegex(m.MaintenanceError, "exactly one"):
            self.maintain()

    def test_unrelated_or_stale_location_refused(self):
        self.prepare()
        self.record["location"]["path"] = "/other/location"
        self.write_registry([self.record])
        with self.assertRaisesRegex(m.MaintenanceError, "location mismatch"):
            self.maintain()

    def test_other_profile_refused(self):
        self.prepare()
        profile = self.user_data / "User/profiles/other"
        profile.mkdir(parents=True)
        (profile / "extensions.json").write_bytes(m.encoded([self.record]))
        with self.assertRaisesRegex(m.MaintenanceError, "another VS Code profile"):
            self.maintain()

    def test_changed_candidate_package_or_backup_refused(self):
        self.prepare()
        (self.bundle / "candidate.vsix").write_bytes(b"changed package")
        with self.assertRaisesRegex(m.MaintenanceError, "changed package"):
            self.maintain()
        self.assertEqual(m.status(self.bundle, self.extensions)["code"], "maintenance-required")

    def test_changed_installed_runtime_refused(self):
        self.prepare()
        self.maintain(apply=True)
        _, _, location = m.registry(self.extensions)
        (location / m.RUNTIME).write_bytes(b"changed installed runtime")
        with self.assertRaisesRegex(m.MaintenanceError, "resource differs"):
            self.maintain(rollback=True)

    def test_repeated_install_refused_without_replacing_anything(self):
        self.prepare()
        self.maintain(apply=True)
        with self.assertRaisesRegex(m.MaintenanceError, "already selected"):
            self.maintain(apply=True)

    def test_unknown_existing_destination_refused(self):
        self.prepare()
        dest = self.extensions / f"{m.EXTENSION_ID}-{m.LOCAL_VERSION}"
        dest.mkdir()
        with self.assertRaisesRegex(m.MaintenanceError, "destination already exists"):
            self.maintain(apply=True)

    def test_shared_lock_rejects_concurrent_mutation(self):
        self.prepare()
        with m.mutation_lock(self.bundle):
            with self.assertRaisesRegex(m.MaintenanceError, "another maintenance"):
                self.maintain(apply=True)

    def test_symlink_lock_is_not_followed(self):
        self.prepare()
        victim = self.root / "victim"
        victim.write_text("preserve")
        (self.bundle / ".maintenance.lock").symlink_to(victim)
        with self.assertRaises(OSError):
            self.maintain(apply=True)
        self.assertEqual(victim.read_text(), "preserve")

    def test_cli_failure_retains_intent_and_original(self):
        self.prepare()
        def fail(command):
            raise m.MaintenanceError("synthetic CLI failure")
        with self.assertRaisesRegex(m.MaintenanceError, "CLI failure"):
            m.maintain(self.bundle, self.extensions, self.user_data, self.code,
                       self.prepared["manifestSha256"], apply=True, runner=fail, isolation_root=self.root)
        result = m.status(self.bundle, self.extensions)
        self.assertEqual(result["code"], "maintenance-required")
        self.assertEqual(len(result["incompleteTransactions"]), 1)
        self.assertEqual(m.inventory(self.original), self.before)

    def test_cli_original_mutation_detected(self):
        self.prepare()
        def bad(command):
            self.fake_cli(command)
            (self.original / "LICENSE.md").write_bytes(b"bad mutation")
        with self.assertRaisesRegex(m.MaintenanceError, "running Original resources"):
            m.maintain(self.bundle, self.extensions, self.user_data, self.code,
                       self.prepared["manifestSha256"], apply=True, runner=bad, isolation_root=self.root)
        self.assertEqual(m.status(self.bundle, self.extensions)["code"], "maintenance-required")

    def evidence(self):
        manifest, sha = m.load_bundle(self.bundle)
        return {"schemaVersion": 1, "kind": "lain-vscode-groups-native", "result": "passed",
                "manifestSha256": sha, "candidateVsixSha256": manifest["packages"]["candidate.vsix"],
                "candidateTreeSha256": m.digest(manifest["candidate"]),
                "checks": {key: True for key in m.NATIVE_CHECKS}}

    def test_production_apply_needs_native_qualification(self):
        self.prepare()
        with self.assertRaisesRegex(m.MaintenanceError, "requires qualification"):
            m.maintain(self.bundle, self.extensions, self.user_data, self.code,
                       self.prepared["manifestSha256"], apply=True, runner=self.fake_cli)
        self.assertFalse((self.bundle / ".maintenance.lock").exists())

    def test_qualification_keeps_manifest_pin_and_enables_disk_readiness(self):
        self.prepare()
        manifest_pin = self.prepared["manifestSha256"]
        evidence = self.root / "native.json"
        evidence.write_bytes(m.encoded(self.evidence()))
        self.assertEqual(m.qualify(self.bundle, evidence, manifest_pin)["code"], "qualified")
        self.assertEqual(m.file_hash(self.bundle / "manifest.json"), manifest_pin)
        m.maintain(self.bundle, self.extensions, self.user_data, self.code,
                   manifest_pin, apply=True, runner=self.fake_cli)
        result = m.status(self.bundle, self.extensions)
        self.assertTrue(result["readyForGroups"])
        self.assertTrue(result["activationQualified"])
        self.assertEqual(result["runtimeActivation"], "not-observed")
        with self.assertRaises(FileExistsError):
            m.qualify(self.bundle, evidence, manifest_pin)

    def test_qualification_rejects_wrong_bytes_and_missing_runtime_assertion(self):
        self.prepare()
        path = self.root / "evidence.json"
        evidence = self.evidence()
        evidence["candidateVsixSha256"] = "0" * 64
        path.write_bytes(m.encoded(evidence))
        with self.assertRaisesRegex(m.MaintenanceError, "different candidate bytes"):
            m.qualify(self.bundle, path, self.prepared["manifestSha256"])
        evidence = self.evidence()
        evidence["checks"]["noAutomaticReload"] = False
        path.write_bytes(m.encoded(evidence))
        with self.assertRaisesRegex(m.MaintenanceError, "runtime assertions"):
            m.qualify(self.bundle, path, self.prepared["manifestSha256"])
        self.assertFalse((self.bundle / "qualification.json").exists())

    def test_transform_report_must_describe_actual_bytes(self):
        def bad(source, dest):
            report = self.transform(source, dest)
            report["files"]["out/extension.js"] = "0" * 64
            return report
        with self.assertRaisesRegex(m.MaintenanceError, "report differs"):
            self.prepare(bad)

    def test_adapter_source_race_is_detected(self):
        def bad(source, dest):
            report = self.transform(source, dest)
            report["sourcePins"] = {"lain-groups-host.cjs": "0" * 64}
            return report
        with self.assertRaisesRegex(m.MaintenanceError, "source changed"):
            self.prepare(bad)

    def test_malformed_registry_fails_closed(self):
        self.prepare()
        self.write_registry(["malformed"])
        self.assertEqual(m.status(self.bundle, self.extensions)["code"], "maintenance-required")
        with self.assertRaisesRegex(m.MaintenanceError, "malformed extension record"):
            self.maintain()

    def test_bad_transaction_never_preserves_true_readiness(self):
        self.prepare()
        path = self.root / "native.json"
        path.write_bytes(m.encoded(self.evidence()))
        m.qualify(self.bundle, path, self.prepared["manifestSha256"])
        self.maintain(apply=True)
        intent = next(self.bundle.glob("transaction-*/intent.json"))
        intent.write_text("invalid json")
        result = m.status(self.bundle, self.extensions)
        self.assertFalse(result["readyForGroups"])
        self.assertEqual(result["code"], "maintenance-required")

    def test_rollback_does_not_depend_on_valid_qualification(self):
        self.prepare()
        self.maintain(apply=True)
        (self.bundle / "qualification.json").write_text("invalid json")
        result = self.maintain(apply=True, rollback=True)
        self.assertEqual(result["selectedVariant"], "original")

    def test_isolation_designation_requires_both_directories_inside_root(self):
        self.prepare()
        with self.assertRaisesRegex(m.MaintenanceError, "children of --isolation-root"):
            m.maintain(self.bundle, self.extensions, self.user_data, self.code,
                       self.prepared["manifestSha256"], apply=True, isolation_root=self.user_data)

    def cli_args(self, command):
        return [command, "--bundle", str(self.bundle), "--extensions-dir", str(self.extensions)]

    def test_cli_status_emits_operational_json(self):
        self.prepare()
        output = io.StringIO()
        with patch("sys.stdout", output):
            self.assertEqual(m.main(self.cli_args("status")), 0)
        self.assertEqual(json.loads(output.getvalue())["code"], "original-selected")

    def test_cli_apply_isolation_and_receipt(self):
        self.prepare()
        output = io.StringIO()
        args = self.cli_args("install") + ["--user-data-dir", str(self.user_data), "--code", str(self.code),
                    "--expect-manifest-sha256", self.prepared["manifestSha256"],
                    "--isolation-root", str(self.root), "--apply"]
        with patch("sys.stdout", output), patch.object(m, "run_cli", self.fake_cli):
            self.assertEqual(m.main(args), 0)
        self.assertEqual(json.loads(output.getvalue())["code"], "reload-needed")

    def test_cli_stale_pin_emits_refusal_json(self):
        self.prepare()
        output = io.StringIO()
        args = self.cli_args("install") + ["--user-data-dir", str(self.user_data), "--code", str(self.code),
                    "--expect-manifest-sha256", "0" * 64]
        with patch("sys.stderr", output):
            self.assertEqual(m.main(args), 1)
        self.assertEqual(json.loads(output.getvalue())["code"], "refused")

    def test_mapped_root_ancestors_and_sticky_directory_are_accepted(self):
        real_stat = os.stat
        root_uid = real_stat("/").st_uid
        def mapped_root(path, *args, **kwargs):
            result = real_stat(path, *args, **kwargs)
            if result.st_uid == root_uid:
                fields = list(result)
                fields[4] = 65534
                result = os.stat_result(fields)
            return result
        with patch.object(m.os, "stat", mapped_root), patch.object(m.os, "lstat", mapped_root):
            self.assertEqual(m.safe_dir(self.source), self.source)

    def test_mapped_root_does_not_allow_nonsticky_writable_ancestor(self):
        real_stat = os.stat
        root_uid = real_stat("/").st_uid
        def mapped_root(path, *args, **kwargs):
            result = real_stat(path, *args, **kwargs)
            fields = list(result)
            if result.st_uid == root_uid:
                fields[4] = 65534
            if Path(path) == Path("/tmp"):
                fields[0] &= ~stat.S_ISVTX
            return os.stat_result(fields)
        with patch.object(m.os, "stat", mapped_root), patch.object(m.os, "lstat", mapped_root):
            with self.assertRaisesRegex(m.MaintenanceError, "writable directory"):
                m.safe_dir(self.source)


if __name__ == "__main__":
    unittest.main()
