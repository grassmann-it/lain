"""Offline reasoning-repair fixtures; every writable path is explicitly temporary."""
import contextlib
import copy
import importlib.util
import io
import json
import os
from pathlib import Path
import stat
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/clients/repair_codex_reasoning.py"
FIXTURE = ROOT / "tests/model-compatibility/fixtures/codex-0.164.0-reasoning.json"
spec = importlib.util.spec_from_file_location("repair_codex_reasoning", SCRIPT)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


class ReasoningRepairTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="lain-reasoning-fixture-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.artifacts = self.root / ".lain-artifacts"
        self.old = self.artifacts / ("a" * 32)
        self.old.mkdir(parents=True)
        self.output = self.artifacts / ("b" * 32)
        self.config = self.root / "config.toml"
        self.catalog = self.old / "lain.models.json"
        self.models = self.root / "models.json"
        self.receipt = self.root / "receipt.json"
        self.fixture = FIXTURE
        self.astra = {"slug": m.MODEL, "display_name": "Astra · Codex", "priority": 7,
                      "description": "Model: gpt-6-astra. Service: Codex.",
                      "default_reasoning_level": "none", "supported_reasoning_levels": [],
                      "visibility": "list", "supported_in_api": True,
                      "opaque": {"unicode": "☃", "quoted": '"default_reasoning_level": "none"'}}
        self.catalog_doc = {"models": [
            {"slug": "before", "default_reasoning_level": "none", "supported_reasoning_levels": []},
            self.astra,
            {"slug": "lain-auto-router", "default_reasoning_level": "none", "supported_reasoning_levels": []},
            {"slug": "other-provider-astra", "upstream": m.MODEL, "default_reasoning_level": "none"}],
            "lain_model_picker": {"schema_version": 1, "groups": [
                {"id": "provider:codex", "name": "Codex", "models": [m.MODEL, "before"],
                 "router_model": "lain-auto-router"},
                {"id": "custom", "name": "IT Sec 🛡", "models": [m.MODEL]}]},
            "unknown_extension": {"anything": [None, 1.25, "retain spacing"]}}
        self.catalog.write_bytes((" \n" + json.dumps(self.catalog_doc, ensure_ascii=False, indent=4) + "\n\t").encode())
        self.config.write_bytes(self.config_bytes())
        self.models_doc = {"contractVersion": 1, "revision": 55, "models": [
            {"id": m.MODEL, "provider": "codex", "source": "configured", "accountIds": ["public-account-id"]},
            {"id": "before", "provider": "other", "source": "configured"}]}
        self.models.write_bytes(m.encoded(self.models_doc))
        (self.old / "lain.config.toml").write_bytes(self.config_bytes())
        (self.old / "manifest.json").write_bytes(m.encoded({"generationId": self.old.name, "profileId": "lain", "revision": 54,
                                                          "catalogHash": "legacy historical metadata"}))
        (self.old / "lain.models.json.bak-order-fixture").write_bytes(b"complete historical catalog\n")
        self.before = self.config.read_bytes()
        self.old_files = self.snapshot(self.old)

    def config_bytes(self, newline="\n"):
        return newline.join([
            "# retain comment and quoting", "model = 'gpt-6-astra' # selected",
            "model_provider = \"lain\"", "  model_reasoning_effort\t= 'none'  # user-approved repair",
            "model_catalog_json = " + json.dumps(str(self.catalog)) + " # retain catalog comment",
            "opaque = { nested = 'unchanged' }", "", "[model_providers.lain]",
            "wire_api = 'responses'", "base_url = 'http://127.0.0.1:18417/v1'",
            "[model_providers.lain.auth]", "command = '/never-run/auth-helper'", "args = ['auth', 'token']",
            "", "[profiles.optional]", "model_reasoning_effort = 'none' # preserve optional profile",
            "model_catalog_json = '/leave/profile/catalog.json'", "", "[projects.'/example/project']",
            "trust_level = 'trusted'", ""]).encode()

    @staticmethod
    def snapshot(path):
        return {item.name: item.read_bytes() for item in path.iterdir() if item.is_file()}

    def prepare(self):
        return m.prepare(self.config, self.catalog, self.output, self.fixture, self.models)

    def apply(self, prepared=None):
        prepared = prepared or self.prepare()
        return m.apply(Path(prepared["plan"]), prepared["planSha256"], prepared["configSha256"],
                       prepared["catalogSha256"], self.receipt)

    def rollback(self, applied):
        return m.rollback(self.receipt, applied["receiptSha256"], applied["configSha256"])

    def rewrite_catalog(self):
        self.catalog.write_bytes(m.encoded(self.catalog_doc))

    def rewrite_models(self):
        self.models.write_bytes(m.encoded(self.models_doc))

    def tamper(self, name):
        path = self.output / name
        path.chmod(0o600)
        path.write_bytes(path.read_bytes() + b"\n")
        path.chmod(0o400)

    def test_prepare_changes_only_astra_two_value_spans_and_no_source_files(self):
        result = self.prepare()
        self.assertEqual(self.config.read_bytes(), self.before)
        self.assertEqual(self.snapshot(self.old), self.old_files)
        new = (self.output / self.catalog.name).read_bytes()
        doc = json.loads(new)
        self.assertEqual([x["effort"] for x in doc["models"][1]["supported_reasoning_levels"]], list(m.EFFORTS))
        self.assertEqual(doc["models"][1]["default_reasoning_level"], "low")
        self.assertNotIn("ultra", [x["effort"] for x in doc["models"][1]["supported_reasoning_levels"]])
        restored = new.decode().replace(json.dumps(doc["models"][1]["supported_reasoning_levels"], ensure_ascii=False), "[]", 1)
        restored = restored.replace('"default_reasoning_level": "low"', '"default_reasoning_level": "none"', 1)
        self.assertEqual(restored.encode(), self.old_files[self.catalog.name])
        self.assertEqual(doc["lain_model_picker"], self.catalog_doc["lain_model_picker"])
        for name, data in self.old_files.items():
            if name != self.catalog.name:
                self.assertEqual((self.output / name).read_bytes(), data)
        self.assertEqual((self.output / m.BEFORE).read_bytes(), self.before)
        self.assertEqual((self.output / m.OLD_CATALOG).read_bytes(), self.old_files[self.catalog.name])
        self.assertEqual(stat.S_IMODE(self.output.stat().st_mode), 0o500)
        self.assertTrue(all(stat.S_IMODE(p.stat().st_mode) == 0o400 for p in self.output.iterdir()))
        plan_data = (self.output / m.PLAN).read_bytes()
        self.assertEqual(m.sha(plan_data), result["planSha256"])
        self.assertNotIn(b"public-account-id", plan_data)
        self.assertEqual(json.loads(plan_data)["sourceSha256"], m.SOURCE_SHA256)

    def test_apply_and_conditional_rollback_restore_exact_original(self):
        prepared = self.prepare()
        applied = self.apply(prepared)
        expected = self.before.replace(b"'none'  # user-approved", b'"low"  # user-approved', 1)
        expected = expected.replace(json.dumps(str(self.catalog)).encode(), json.dumps(str(self.output / self.catalog.name)).encode(), 1)
        self.assertEqual(self.config.read_bytes(), expected)
        self.assertEqual(self.snapshot(self.old), self.old_files)
        self.assertEqual(stat.S_IMODE(self.receipt.stat().st_mode), 0o400)
        self.assertEqual(json.loads(self.receipt.read_bytes())["kind"], "conditional-astra-rollback")
        self.rollback(applied)
        self.assertEqual(self.config.read_bytes(), self.before)
        self.assertEqual(self.snapshot(self.old), self.old_files)
        self.assertTrue(self.output.exists())

    def test_crlf_comments_quotes_and_optional_profile_preserved(self):
        data = self.config_bytes("\r\n")
        result = m.edit_config(data, self.catalog, self.output / self.catalog.name)
        self.assertEqual(result.count(b"\r\n"), data.count(b"\r\n"))
        self.assertEqual(result.split(b"[model_providers.lain]", 1)[1], data.split(b"[model_providers.lain]", 1)[1])
        self.assertEqual(m.toml(result)["profiles"], m.toml(data)["profiles"])

    def test_native_fixture_is_pinned_by_content_not_claimed_sha_field(self):
        fake = self.root / "fixture.json"
        fake.write_bytes(FIXTURE.read_bytes() + b"\n")
        self.fixture = fake
        with self.assertRaisesRegex(m.RepairError, "fixture SHA256"):
            self.prepare()
        self.assertFalse(self.output.exists())

    def test_reject_other_or_ambiguous_route_providers(self):
        for provider in ("commandcode", "requesty", "openrouter", "cpa", "Codex"):
            with self.subTest(provider=provider):
                self.models_doc["models"][0]["provider"] = provider
                self.rewrite_models()
                with self.assertRaisesRegex(m.RepairError, "route"):
                    self.prepare()
        self.models_doc["models"][0]["provider"] = "codex"
        self.models_doc["models"].append(dict(self.models_doc["models"][0]))
        self.rewrite_models()
        with self.assertRaisesRegex(m.RepairError, "route"):
            self.prepare()

    def test_reject_non_configured_or_invalid_public_models_snapshot(self):
        for value in ({"models": []}, {"contractVersion": 1, "revision": True, "models": []},
                      {"contractVersion": 1, "revision": -1, "models": []},
                      dict(self.models_doc, models=[dict(self.models_doc["models"][0], source="discovered")])):
            with self.subTest(value=value):
                self.models.write_bytes(m.encoded(value))
                with self.assertRaises(m.RepairError):
                    self.prepare()

    def test_reject_already_repaired_or_unrecognized_catalog_entries(self):
        variants = [{"slug": "aliased-astra"}, {"default_reasoning_level": "low"},
                    {"supported_reasoning_levels": [{"effort": "none"}]}, {"visibility": "hide"},
                    {"supported_in_api": False}, {"description": "Model: gpt-6-astra. Service: Requesty."}]
        original = copy.deepcopy(self.astra)
        for changes in variants:
            with self.subTest(changes=changes):
                self.astra.clear()
                self.astra.update(original, **changes)
                self.rewrite_catalog()
                with self.assertRaises(m.RepairError):
                    self.prepare()
        self.assertFalse(self.output.exists())

    def test_duplicate_catalog_keys_or_slugs_are_rejected(self):
        original = self.catalog.read_bytes()
        self.catalog.write_bytes(original.replace(b'"models": [', b'"models": [], "models": [', 1))
        with self.assertRaisesRegex(m.RepairError, "duplicate JSON"):
            self.prepare()
        self.catalog_doc["models"].append(dict(self.astra))
        self.rewrite_catalog()
        with self.assertRaisesRegex(m.RepairError, "duplicate catalog"):
            self.prepare()

    def test_reject_root_overrides_but_leave_optional_profiles_alone(self):
        for data in (self.before.replace(b'model_provider = "lain"', b'model_provider = "cpa"'),
                     self.before.replace(b"'none'  # user-approved", b"'high'  # user-approved"),
                     b"profile = 'lain'\n" + self.before,
                     self.before.replace(b"model = 'gpt-6-astra'", b"model = 'lain-auto-router'")):
            with self.subTest(data=data[:70]):
                self.config.write_bytes(data)
                with self.assertRaises(m.RepairError):
                    self.prepare()

    def test_reject_multiline_dotted_or_duplicate_root_controls(self):
        for data in (b"message = '''\nmodel_reasoning_effort = 'none'\n'''\n" + self.before,
                     self.before.replace(b"model_catalog_json =", b"'model_catalog_json' ="),
                     b"model_reasoning_effort = 'none'\n" + self.before,
                     self.before.replace(b"wire_api = 'responses'", b"wire_api = 'chat'")):
            with self.subTest(data=data[:70]):
                with self.assertRaises(m.RepairError):
                    m.edit_config(data, self.catalog, self.output / self.catalog.name)

    def test_output_must_be_new_sibling_generation(self):
        for output in (self.old, self.artifacts / "not-a-generation", self.root / ("c" * 32)):
            with self.subTest(output=output):
                with self.assertRaises(m.RepairError):
                    m.prepare(self.config, self.catalog, output, self.fixture, self.models)
        self.output.mkdir()
        sentinel = self.output / "keep"
        sentinel.write_bytes(b"unchanged")
        with self.assertRaises(m.RepairError):
            self.prepare()
        self.assertEqual(sentinel.read_bytes(), b"unchanged")

    def test_no_auth_or_unrecognized_artifact_is_ever_opened(self):
        (self.old / "auth.json").write_bytes(b"CREDENTIAL-CANARY")
        real_read = m.checked_read

        def guarded(fd, name):
            self.assertNotEqual(name, "auth.json", "source auth must never be opened")
            return real_read(fd, name)

        with patch.object(m, "checked_read", side_effect=guarded):
            with self.assertRaisesRegex(m.RepairError, "potential auth"):
                self.prepare()

    def test_symlink_inputs_and_ancestors_are_rejected(self):
        real = self.root / "real-config"
        self.config.rename(real)
        self.config.symlink_to(real)
        with self.assertRaises(OSError):
            self.prepare()
        self.config.unlink()
        real.rename(self.config)
        linked = self.root / "linked"
        linked.symlink_to(self.old, target_is_directory=True)
        with self.assertRaises(OSError):
            m.read(linked / self.catalog.name)
        self.catalog.unlink()
        self.catalog.symlink_to(self.models)
        with self.assertRaises(OSError):
            self.prepare()

    def test_hardlinks_fifos_and_unsafe_permissions_are_rejected(self):
        os.link(self.config, self.root / "hardlink")
        with self.assertRaisesRegex(m.RepairError, "single-link"):
            self.prepare()
        (self.root / "hardlink").unlink()
        self.config.chmod(0o666)
        with self.assertRaisesRegex(m.RepairError, "permissions"):
            self.prepare()
        self.config.chmod(0o600)
        self.catalog.unlink()
        os.mkfifo(self.catalog)
        with self.assertRaisesRegex(m.RepairError, "single-link"):
            self.prepare()

    def test_foreign_file_ownership_and_writable_ancestors_are_rejected(self):
        self.root.chmod(0o777)
        with self.assertRaisesRegex(m.RepairError, "writable directory"):
            self.prepare()
        self.root.chmod(0o700)
        with patch.object(m.os, "getuid", return_value=os.getuid() + 1000):
            with self.assertRaises(m.RepairError):
                self.prepare()

    def test_config_and_artifacts_must_not_change_after_preparation(self):
        prepared = self.prepare()
        self.config.write_bytes(self.before + b"# later user edit\n")
        changed = self.config.read_bytes()
        with self.assertRaisesRegex(m.RepairError, "config changed"):
            self.apply(prepared)
        self.assertEqual(self.config.read_bytes(), changed)
        self.assertFalse(self.receipt.exists())
        self.config.write_bytes(self.before)
        self.catalog.write_bytes(self.catalog.read_bytes() + b"\n")
        with self.assertRaisesRegex(m.RepairError, "source artifacts changed"):
            self.apply(prepared)
        self.assertEqual(self.config.read_bytes(), self.before)

    def test_every_source_artifact_is_bound_not_only_catalog(self):
        prepared = self.prepare()
        (self.old / "lain.config.toml").write_bytes(b"changed paired config")
        with self.assertRaisesRegex(m.RepairError, "source artifacts changed"):
            self.apply(prepared)
        self.assertEqual(self.config.read_bytes(), self.before)

    def test_explicit_apply_hashes_and_plan_hash_are_required_and_checked(self):
        prepared = self.prepare()
        values = [Path(prepared["plan"]), prepared["planSha256"], prepared["configSha256"], prepared["catalogSha256"], self.receipt]
        for index in (1, 2, 3):
            args = values.copy()
            args[index] = "0" * 64
            with self.subTest(index=index), self.assertRaises(m.RepairError):
                m.apply(*args)
        self.assertFalse(self.receipt.exists())
        self.assertEqual(self.config.read_bytes(), self.before)

    def test_tampered_prepared_payload_or_extra_files_block_apply(self):
        prepared = self.prepare()
        self.tamper(m.AFTER)
        with self.assertRaisesRegex(m.RepairError, "prepared file changed"):
            self.apply(prepared)
        self.assertEqual(self.config.read_bytes(), self.before)

    def test_extra_file_or_writable_prepared_directory_block_apply(self):
        prepared = self.prepare()
        self.output.chmod(0o700)
        with self.assertRaisesRegex(m.RepairError, "private and immutable"):
            self.apply(prepared)
        (self.output / "extra").write_bytes(b"unplanned")
        self.output.chmod(0o500)
        with self.assertRaisesRegex(m.RepairError, "inventory"):
            self.apply(prepared)

    def test_apply_cas_detects_edit_during_durable_temp_write(self):
        prepared = self.prepare()
        real_write = m.write_new
        later = self.before + b"# concurrent edit\n"

        def concurrent(fd, name, data, mode=0o400):
            real_write(fd, name, data, mode)
            if name.startswith(".reasoning-config-"):
                self.config.write_bytes(later)

        with patch.object(m, "write_new", side_effect=concurrent):
            with self.assertRaisesRegex(m.RepairError, "config changed before commit"):
                self.apply(prepared)
        self.assertEqual(self.config.read_bytes(), later)
        self.assertFalse(list(self.root.glob(".reasoning-config-*")))
        self.assertTrue(self.receipt.exists())  # conditional, not an apply-success claim

    def test_preparation_cas_discards_candidate_when_source_changes(self):
        real_write = m.write_new

        def concurrent(fd, name, data, mode=0o400):
            real_write(fd, name, data, mode)
            if name == m.PLAN:
                self.config.write_bytes(self.before + b"# concurrent prepare edit\n")

        with patch.object(m, "write_new", side_effect=concurrent):
            with self.assertRaisesRegex(m.RepairError, "inputs changed"):
                self.prepare()
        self.assertFalse(self.output.exists())
        self.assertFalse(list(self.artifacts.glob(".reasoning-*")))

    def test_receipt_exists_and_backups_are_complete_before_atomic_write(self):
        prepared = self.prepare()

        def crash(*args, **kwargs):
            self.assertEqual((self.output / m.BEFORE).read_bytes(), self.before)
            self.assertEqual((self.output / m.OLD_CATALOG).read_bytes(), self.old_files[self.catalog.name])
            receipt = json.loads(self.receipt.read_bytes())
            self.assertEqual(receipt["beforeConfigSha256"], m.sha(self.before))
            raise OSError("simulated crash before rename")

        with patch.object(m.os, "replace", side_effect=crash):
            with self.assertRaises(OSError):
                self.apply(prepared)
        self.assertEqual(self.config.read_bytes(), self.before)
        self.assertFalse(list(self.root.glob(".reasoning-config-*")))

    def test_parent_fsync_failure_after_publication_keeps_complete_candidate(self):
        real_sync = m.os.fsync

        def failed_parent_sync(fd):
            if self.output.exists():
                raise OSError("simulated publication fsync failure")
            real_sync(fd)

        with patch.object(m.os, "fsync", side_effect=failed_parent_sync), self.assertRaises(OSError):
            self.prepare()
        self.assertEqual(self.config.read_bytes(), self.before)
        self.assertEqual(stat.S_IMODE(self.output.stat().st_mode), 0o500)
        self.assertEqual((self.output / m.BEFORE).read_bytes(), self.before)
        self.assertEqual((self.output / m.OLD_CATALOG).read_bytes(), self.old_files[self.catalog.name])
        plan = self.output / m.PLAN
        m.load_plan(plan, m.sha(plan.read_bytes()))

    def test_commit_fsync_failure_retains_receipt_for_conditional_recovery(self):
        prepared = self.prepare()
        real_sync = m.os.fsync

        def failed_commit_sync(fd):
            if m.sha(self.config.read_bytes()) == prepared["afterConfigSha256"]:
                raise OSError("simulated post-commit fsync failure")
            real_sync(fd)

        with patch.object(m.os, "fsync", side_effect=failed_commit_sync), self.assertRaises(OSError):
            self.apply(prepared)
        self.assertEqual(m.sha(self.config.read_bytes()), prepared["afterConfigSha256"])
        m.rollback(self.receipt, m.sha(self.receipt.read_bytes()), prepared["afterConfigSha256"])
        self.assertEqual(self.config.read_bytes(), self.before)

    def test_oversize_input_is_rejected_before_reading_contents(self):
        with self.config.open("wb") as stream:
            stream.truncate(m.LIMIT + 1)
        with self.assertRaisesRegex(m.RepairError, "size limit"):
            self.prepare()
        self.assertFalse(self.output.exists())

    def test_receipt_collision_or_symlink_never_overwrites_anything(self):
        prepared = self.prepare()
        self.receipt.symlink_to(self.config)
        with self.assertRaises(OSError):
            self.apply(prepared)
        self.assertEqual(self.config.read_bytes(), self.before)
        self.assertTrue(self.receipt.is_symlink())

    def test_apply_refuses_second_application(self):
        prepared = self.prepare()
        self.apply(prepared)
        changed = self.config.read_bytes()
        with self.assertRaises(m.RepairError):
            self.apply(prepared)
        self.assertEqual(self.config.read_bytes(), changed)

    def test_rollback_only_when_after_hash_matches(self):
        applied = self.apply()
        self.config.write_bytes(self.config.read_bytes() + b"# subsequent user edit\n")
        later = self.config.read_bytes()
        with self.assertRaisesRegex(m.RepairError, "refusing rollback"):
            self.rollback(applied)
        self.assertEqual(self.config.read_bytes(), later)

    def test_rollback_checks_expected_receipt_and_config_hash(self):
        applied = self.apply()
        after = self.config.read_bytes()
        for receipt_hash, config_hash in (("0" * 64, applied["configSha256"]), (applied["receiptSha256"], "0" * 64)):
            with self.subTest(receipt=receipt_hash), self.assertRaises(m.RepairError):
                m.rollback(self.receipt, receipt_hash, config_hash)
        self.assertEqual(self.config.read_bytes(), after)

    def test_rollback_refuses_dangling_old_catalog_but_keeps_complete_backup(self):
        applied = self.apply()
        after = self.config.read_bytes()
        self.catalog.unlink()
        with self.assertRaises(OSError):
            self.rollback(applied)
        self.assertEqual(self.config.read_bytes(), after)
        self.assertEqual((self.output / m.OLD_CATALOG).read_bytes(), self.old_files[self.catalog.name])

    def test_receipt_cannot_mutate_old_or_prepared_generation(self):
        prepared = self.prepare()
        for receipt in (self.old / "new-receipt.json", self.output / "new-receipt.json"):
            with self.subTest(receipt=receipt), self.assertRaisesRegex(m.RepairError, "receipt must be outside"):
                m.apply(prepared["plan"], prepared["planSha256"], prepared["configSha256"], prepared["catalogSha256"], receipt)
            self.assertFalse(receipt.exists())
        self.assertEqual(self.snapshot(self.old), self.old_files)

    def test_atomic_publication_does_not_clobber_concurrent_output_creator(self):
        publish = m.publish

        def concurrent(fd, source, target):
            self.output.mkdir()
            (self.output / "concurrent").write_bytes(b"keep")
            publish(fd, source, target)

        with patch.object(m, "publish", side_effect=concurrent), self.assertRaises(m.RepairError):
            self.prepare()
        self.assertEqual((self.output / "concurrent").read_bytes(), b"keep")
        self.assertEqual(self.config.read_bytes(), self.before)
        self.assertFalse(list(self.artifacts.glob(".reasoning-*")))

    def test_late_catalog_change_prevents_config_commit(self):
        prepared = self.prepare()
        real_write = m.write_new

        def concurrent(fd, name, data, mode=0o400):
            real_write(fd, name, data, mode)
            if name.startswith(".reasoning-config-"):
                self.catalog.write_bytes(self.catalog.read_bytes() + b"\n")

        with patch.object(m, "write_new", side_effect=concurrent), self.assertRaisesRegex(m.RepairError, "source artifacts changed"):
            self.apply(prepared)
        self.assertEqual(self.config.read_bytes(), self.before)

    def test_identical_byte_inode_swap_during_apply_fails_identity_cas(self):
        prepared = self.prepare()
        real_write = m.write_new

        def concurrent(fd, name, data, mode=0o400):
            real_write(fd, name, data, mode)
            if name.startswith(".reasoning-config-"):
                swap = self.root / "config-swap"
                swap.write_bytes(self.before)
                os.replace(swap, self.config)

        with patch.object(m, "write_new", side_effect=concurrent), self.assertRaisesRegex(m.RepairError, "config changed before commit"):
            self.apply(prepared)
        self.assertEqual(self.config.read_bytes(), self.before)

    def test_catalog_repair_preserves_arbitrary_astra_position(self):
        for index in (0, 1, 3):
            with self.subTest(index=index):
                entries = [x for x in self.catalog_doc["models"] if x["slug"] != m.MODEL]
                entries.insert(index, self.astra)
                original = m.encoded(dict(self.catalog_doc, models=entries))
                result, _ = m.repair_catalog(original, FIXTURE.read_bytes(), self.models.read_bytes())
                self.assertEqual([x["slug"] for x in json.loads(result)["models"]], [x["slug"] for x in entries])
                self.assertEqual(json.loads(result)["models"][index]["default_reasoning_level"], "low")

    def test_rollback_cas_detects_late_edit(self):
        applied = self.apply()
        later = self.config.read_bytes() + b"# concurrent rollback edit\n"
        real_write = m.write_new

        def concurrent(fd, name, data, mode=0o400):
            real_write(fd, name, data, mode)
            if name.startswith(".reasoning-config-"):
                self.config.write_bytes(later)

        with patch.object(m, "write_new", side_effect=concurrent):
            with self.assertRaisesRegex(m.RepairError, "config changed before commit"):
                self.rollback(applied)
        self.assertEqual(self.config.read_bytes(), later)

    def test_changed_backup_blocks_rollback(self):
        applied = self.apply()
        after = self.config.read_bytes()
        self.tamper(m.BEFORE)
        with self.assertRaises(m.RepairError):
            self.rollback(applied)
        self.assertEqual(self.config.read_bytes(), after)

    def test_shared_directory_lock_serializes_repairs(self):
        prepared = self.prepare()
        with m.directory(self.root) as fd:
            m.fcntl.flock(fd, m.fcntl.LOCK_EX | m.fcntl.LOCK_NB)
            with self.assertRaises(BlockingIOError):
                self.apply(prepared)
        self.assertEqual(self.config.read_bytes(), self.before)

    def test_cli_requires_explicit_inputs_and_hashes_without_home_inference(self):
        for args in (["--prepare"], ["--apply"], ["--rollback"]):
            with self.subTest(args=args), contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as error:
                m.main(args)
            self.assertEqual(error.exception.code, 2)
        self.assertEqual(self.config.read_bytes(), self.before)

    def test_cli_prepare_apply_and_rollback_roundtrip_no_provider_or_auth_helper(self):
        def invoke(args):
            completed = subprocess.run([sys.executable, "-B", str(SCRIPT), *args], capture_output=True, check=True)
            self.assertEqual(completed.stderr, b"")
            return json.loads(completed.stdout)

        prepared = invoke(["--prepare", "--config", str(self.config), "--catalog", str(self.catalog),
                           "--output", str(self.output), "--source-fixture", str(self.fixture), "--models", str(self.models)])
        applied = invoke(["--apply", "--plan", prepared["plan"], "--expect-plan-sha256", prepared["planSha256"],
                          "--expect-config-sha256", prepared["configSha256"], "--expect-catalog-sha256", prepared["catalogSha256"],
                          "--receipt", str(self.receipt)])
        invoke(["--rollback", "--receipt", str(self.receipt), "--expect-receipt-sha256", applied["receiptSha256"],
                "--expect-config-sha256", applied["configSha256"]])
        self.assertEqual(self.config.read_bytes(), self.before)
        self.assertEqual(self.snapshot(self.old), self.old_files)

    def test_cli_errors_never_echo_config_or_exception_source_lines(self):
        self.config.write_bytes(b"super_secret = 'CREDENTIAL-CANARY'\ninvalid TOML CREDENTIAL-CANARY")
        stdout, stderr = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
            code = m.main(["--prepare", "--config", str(self.config), "--catalog", str(self.catalog),
                           "--output", str(self.output), "--source-fixture", str(self.fixture), "--models", str(self.models)])
        self.assertEqual(code, 1)
        self.assertNotIn("CREDENTIAL-CANARY", stdout.getvalue() + stderr.getvalue())
        self.assertFalse(self.output.exists())


if __name__ == "__main__":
    unittest.main()
