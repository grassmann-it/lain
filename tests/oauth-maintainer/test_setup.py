#!/usr/bin/python3
"""Serial offline file fixtures; no provider, browser, Host or .NET process."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import stat
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("oauth_setup", ROOT / "scripts/prepare-oauth-maintainer.py")
setup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(setup)


def catalog():
    return {"schemaVersion": 1, "sourceUrl": setup.CF_SCOPES_URL, "capturedAt": "2026-10-25T20:00:00Z",
            "pages": [{"success": True, "errors": [], "result": [
                {"id": "fixture-account.read", "name": "Synthetic account read"},
                {"id": "fixture-analytics.read", "name": "Synthetic analytics read"}],
                "result_info": {"page": 1, "per_page": 2, "count": 2, "total_count": 2}}]}


def fixture():
    return {"schemaVersion": 1, "google": {"clientId": "fixture-owned.apps.googleusercontent.com"},
            "poe": {"clientId": "fixture-owned-poe", "callbackUri": "http://127.0.0.1:18419/lain/poe/callback"},
            "cloudflare": {"clientId": "fixture-owned-cloudflare", "accountId": "a" * 32,
                           "clientName": "Synthetic lain", "clientUri": "https://fixture.example/lain",
                           "logoUri": "https://fixture.example/lain.png", "callbackUri": "http://localhost:18421/lain/cloudflare/callback",
                           "scopes": ["offline_access", "fixture-account.read", "fixture-analytics.read"],
                           "scopeCatalogSha256": "b" * 64}}


class Setup(unittest.TestCase):
    def validate(self, value, cat=None):
        setup.validate(value, catalog() if cat is None else cat, "b" * 64)

    def test_registration_plans_accept_missing_ids_but_cannot_stage(self):
        value = fixture()
        for key in ("google", "poe", "cloudflare"):
            value[key]["clientId"] = None
        self.validate(value)
        plan = setup.plan(value)
        self.assertFalse(plan["externalRequestsMade"])
        self.assertFalse(plan["availabilityChanged"])
        self.assertTrue(all(not p["clientIdPresent"] for p in plan["providers"].values()))
        with self.assertRaisesRegex(ValueError, "provider-issued"):
            setup.outputs(value)

    def test_cloudflare_request_uses_public_pkce_and_catalog_ids(self):
        value = fixture(); self.validate(value)
        request = setup.plan(value)["providers"]["cloudflare"]["registrationRequest"]
        self.assertEqual(request["method"], "POST")
        self.assertTrue(request["notSent"])
        self.assertEqual(request["body"]["token_endpoint_auth_method"], "none")
        self.assertEqual(request["body"]["grant_types"], ["authorization_code", "refresh_token"])
        self.assertEqual(request["body"]["scopes"], ["fixture-account.read", "fixture-analytics.read"])
        self.assertNotIn("visibility", request["body"])
        self.assertNotIn("client_secret", request["body"])

    def test_host_output_contract_and_all_qualification_gates_stay_false(self):
        value = fixture(); self.validate(value)
        files = setup.outputs(value)
        cloud = files["maintainer/cloud-oauth.json"]
        self.assertFalse(cloud["cloudflareNeuronCandidateQualified"])
        self.assertEqual([c["provider"] for c in cloud["clients"]], ["gemini-api", "cloudflare-workers-ai"])
        for client in cloud["clients"]:
            self.assertFalse(client["qualified"])
        self.assertEqual(cloud["clients"][0]["scopes"], [setup.GOOGLE_SCOPE])
        self.assertNotIn("registeredCallback", cloud["clients"][0])
        self.assertEqual(cloud["clients"][1]["registeredCallback"], value["cloudflare"]["callbackUri"])
        self.assertEqual(set(files["maintainer/poe-login.json"]), {"clientId", "callbackUri", "registrationQualified"})
        self.assertFalse(files["maintainer/poe-login.json"]["registrationQualified"])

    def test_public_config_rejects_secrets_unknown_fields_and_qualification_switches(self):
        paths = [(None, "token"), ("google", "clientSecret"), ("cloudflare", "accessToken"),
                 ("google", "qualified"), ("poe", "registrationQualified"), (None, "cloudflareNeuronCandidateQualified")]
        for provider, key in paths:
            with self.subTest(provider=provider, key=key):
                value = fixture(); target = value[provider] if provider else value; target[key] = "PRIVATE_SENTINEL"
                with self.assertRaises(ValueError): self.validate(value)

    def test_schema_version_is_explicit_integer(self):
        for version in [True, "1", 2, None]:
            value = fixture(); value["schemaVersion"] = version
            with self.subTest(version=version), self.assertRaises(ValueError): self.validate(value)

    def test_google_desktop_registration_shape(self):
        for client in ["borrowed-example", "spaces.apps.googleusercontent.com ", "id@host.apps.googleusercontent.com"]:
            value = fixture(); value["google"]["clientId"] = client
            with self.subTest(client=client), self.assertRaises(ValueError): self.validate(value)

    def test_callbacks_reject_remote_ambiguous_and_privileged_targets(self):
        for callback in ["https://localhost:18419/cb", "http://localhost.evil:18419/cb", "http://127.0.0.2:18419/cb",
                         "http://localhost/cb", "http://localhost:80/cb", "http://localhost:18419/", "http://localhost:18419/cb?q=token",
                         "http://localhost:18419/cb#fragment", "http://user@localhost:18419/cb", "http://localhost:18419/%2e%2e/cb",
                         "http://localhost:18419/a/../cb", "http://localhost:invalid/cb"]:
            with self.subTest(callback=callback), self.assertRaises(ValueError): setup.callback(callback)

    def test_publisher_metadata_rejects_credentials_local_and_non_https_urls(self):
        for url in ["http://fixture.example", "https://user:secret@fixture.example", "https://localhost", "https://127.0.0.1/p",
                    "https://192.0.2.1/p", "https://fixture.example:444/p", "https://fixture.example/p#fragment"]:
            with self.subTest(url=url), self.assertRaises(ValueError): setup.public_url(url)

    def test_scope_ids_must_match_pinned_catalog(self):
        value = fixture(); value["cloudflare"]["scopeCatalogSha256"] = "c" * 64
        with self.assertRaisesRegex(ValueError, "changed"): self.validate(value)
        value = fixture(); value["cloudflare"]["scopes"].append("invented.read")
        with self.assertRaisesRegex(ValueError, "absent"): self.validate(value)
        value = fixture(); value["cloudflare"]["scopes"].append("workers:read")
        with self.assertRaisesRegex(ValueError, "scope IDs"): self.validate(value)

    def test_offline_access_duplicate_and_scope_boundaries(self):
        for scopes in [["offline_access"], ["fixture-account.read", "fixture-analytics.read"],
                       ["offline_access", "fixture-account.read", "fixture-account.read"], ["offline_access", 7]]:
            value = fixture(); value["cloudflare"]["scopes"] = scopes
            with self.subTest(scopes=scopes), self.assertRaises(ValueError): self.validate(value)

    def test_catalog_rejects_wrong_source_errors_duplicate_and_incomplete_pages(self):
        cases = []
        value = catalog(); value["sourceUrl"] = "https://untrusted.example/scopes"; cases.append(value)
        value = catalog(); value["pages"][0]["errors"] = [{"message": "PRIVATE_SENTINEL"}]; cases.append(value)
        value = catalog(); value["pages"][0]["result_info"]["total_count"] = 3; cases.append(value)
        value = catalog(); value["pages"][0]["result"][1]["id"] = "fixture-account.read"; cases.append(value)
        value = catalog(); value["pages"][0]["result_info"]["page"] = 2; cases.append(value)
        for value in cases:
            with self.subTest(value=value), self.assertRaises(ValueError): setup.catalog_ids(value)

    def test_complete_multi_page_catalog(self):
        value = catalog(); rows = value["pages"][0]["result"]
        value["pages"] = [{"success": True, "result": [row], "result_info": {"page": index, "per_page": 1, "count": 1, "total_count": 2}}
                          for index, row in enumerate(rows, 1)]
        self.assertEqual(setup.catalog_ids(value), {"fixture-account.read", "fixture-analytics.read"})

    def test_catalog_rejects_invalid_timestamp_and_uneven_page_distribution(self):
        value = catalog(); value["capturedAt"] = "2026-99-99T20:00:00Z"
        with self.assertRaisesRegex(ValueError, "timestamp"): setup.catalog_ids(value)
        value = catalog(); value["pages"][0]["result_info"]["total_pages"] = True
        with self.assertRaisesRegex(ValueError, "pagination"): setup.catalog_ids(value)
        value = catalog(); rows = value["pages"][0]["result"]
        value["pages"] = [{"success": True, "result": [row], "result_info": {"page": index, "per_page": 2, "count": 1, "total_count": 2}}
                          for index, row in enumerate(rows, 1)]
        with self.assertRaisesRegex(ValueError, "pagination"): setup.catalog_ids(value)

    def test_safe_input_refuses_symlink_fifo_oversize_duplicate_and_invalid_json(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); regular = root / "input.json"; regular.write_text('{}')
            link = root / "link"; link.symlink_to(regular)
            with self.assertRaises(OSError): setup.read_json(link)
            fifo = root / "fifo"; os.mkfifo(fifo)
            with self.assertRaisesRegex(ValueError, "regular"): setup.read_json(fifo)
            for raw in ['{"schemaVersion":1,"schemaVersion":2}', '{invalid', '{"a":NaN}', 'x' * 100]:
                regular.write_text(raw)
                with self.subTest(raw=raw), self.assertRaises(ValueError): setup.read_json(regular, 64)

    def test_staging_new_private_files_and_pinned_receipt(self):
        with tempfile.TemporaryDirectory() as tmp:
            output = Path(tmp) / "stage"; value = fixture(); self.validate(value)
            receipt = setup.stage(output, setup.outputs(value), setup.plan(value), "c" * 64, "b" * 64)
            self.assertFalse(receipt["installed"])
            self.assertFalse(receipt["availabilityChanged"])
            self.assertEqual(stat.S_IMODE(output.stat().st_mode), 0o700)
            for name, sha in receipt["files"].items():
                self.assertEqual(hashlib.sha256((output / name).read_bytes()).hexdigest(), sha)
                self.assertEqual(stat.S_IMODE((output / name).stat().st_mode), 0o600)

    def test_staging_refuses_existing_destination_unprotected_parent_and_symlink(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); value = fixture(); output = root / "exists"; output.mkdir()
            sentinel = output / "sentinel"; sentinel.write_text('unchanged')
            with self.assertRaises(FileExistsError): setup.stage(output, setup.outputs(value), setup.plan(value), "c" * 64, "b" * 64)
            self.assertEqual(sentinel.read_text(), 'unchanged')
            link = root / "link"; link.symlink_to(output, target_is_directory=True)
            with self.assertRaisesRegex(ValueError, "symlinks"): setup.stage(link / "child", {}, {}, "c" * 64, None)
            insecure = root / "insecure"; insecure.mkdir(); insecure.chmod(0o777)
            with self.assertRaisesRegex(ValueError, "protected"): setup.stage(insecure / "child", {}, {}, "c" * 64, None)

    def test_cli_rejects_secret_fields_without_echoing_values_and_writes_nothing(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp); path = root / "spec.json"
            path.write_text(json.dumps({"schemaVersion": 1, "google": {"clientId": "fixture.apps.googleusercontent.com", "clientSecret": "PRIVATE_SENTINEL"}}))
            result = subprocess.run([sys.executable, str(ROOT / "scripts/prepare-oauth-maintainer.py"), "stage", "--spec", str(path), "--output", str(root / "stage")], capture_output=True, text=True)
            self.assertEqual(result.returncode, 2)
            self.assertNotIn("PRIVATE_SENTINEL", result.stdout + result.stderr)
            self.assertFalse((root / "stage").exists())


if __name__ == "__main__":
    unittest.main()
