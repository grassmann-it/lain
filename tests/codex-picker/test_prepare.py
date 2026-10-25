#!/usr/bin/env python3
"""Offline checks for the strict release-lock correction verifier."""
import copy
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("prepare_codex", Path(__file__).resolve().parents[2] / "scripts" / "prepare-codex-picker.py")
prepare = importlib.util.module_from_spec(spec)
spec.loader.exec_module(prepare)


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
