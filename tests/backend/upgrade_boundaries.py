#!/usr/bin/env python3
"""Additional synthetic upgrade boundaries; operates only private child processes.

This deliberately keeps the existing protocol/durability qualification unchanged.
No successful Codex dispatch or OAuth exchange is attempted.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
from urllib.error import HTTPError

from qualify import Run, assert_complete, expect
from fake_provider import FakeProvider, MODEL


def rejected_snapshot(core):
    cases = {
        "refresh-token": {"type": "codex", "access_token": "synthetic-access", "refresh_token": "synthetic-never-refresh"},
        "empty-refresh-token": {"type": "codex", "access_token": "synthetic-access", "refresh_token": ""},
        "null-refresh-token": {"type": "codex", "access_token": "synthetic-access", "refresh_token": None},
        "other-oauth-provider": {"type": "claude", "access_token": "synthetic-access"},
    }
    observed = []
    for name, data in cases.items():
        run = Run(core)
        try:
            auth = run.root / "auth"
            auth.mkdir(mode=0o700)
            credential = auth / "external.json"
            original = (json.dumps(data) + "\n").encode()
            credential.write_bytes(original)
            credential.chmod(0o600)
            result = subprocess.run(run.command(), cwd=run.root, env=run.env, capture_output=True, timeout=10)
            expect(result.returncode != 0, f"{name}: unsafe snapshot accepted")
            expect(credential.read_bytes() == original, f"{name}: rejected snapshot was rewritten")
            expect(not run.provider.snapshot(), f"{name}: upstream called before snapshot rejection")
            expect(not run.journal.exists() or not run.journal.read_bytes(), f"{name}: rejected snapshot dispatched work")
            observed.append(name)
        finally:
            run.__exit__()
    run = Run(core)
    try:
        auth = run.root / "auth"
        auth.mkdir(mode=0o700)
        target = run.root / "synthetic-external.json"
        original = b'{"type":"codex","access_token":"synthetic-access"}\n'
        target.write_bytes(original)
        (auth / "external.json").symlink_to(target)
        result = subprocess.run(run.command(), cwd=run.root, env=run.env, capture_output=True, timeout=10)
        expect(result.returncode != 0, "symlink snapshot accepted")
        expect(target.read_bytes() == original and (auth / "external.json").is_symlink(), "symlink rejection changed credential")
        expect(not run.provider.snapshot(), "symlink snapshot dispatched upstream")
        observed.append("symlink")
    finally:
        run.__exit__()
    return {"rejectedCases": observed, "credentialsUnchanged": True, "upstreamAttempts": 0,
            "scope": "startup rejection only; no OAuth exchange or live refresh"}


def copied_owned_credential(core):
    run = Run(core)
    try:
        owned = run.root / "auth-owned"
        owned.mkdir(mode=0o700)
        credential = owned / "foreign.json"
        original = b'{"type":"codex","access_token":"synthetic-borrowed-access","refresh_token":"synthetic-never-refresh","account_id":"synthetic-foreign","expired":"2099-01-01T00:00:00Z"}\n'
        credential.write_bytes(original)
        credential.chmod(0o600)
        # A route reference alone must not promote an arbitrary copied file.
        run.bindings["accounts"]["borrowed-account"] = "owned/foreign.json"
        run.bindings["routes"]["fixture-borrowed"] = {"id": "borrowed-route", "model": MODEL,
            "accounts": ["borrowed-account"], "selection": "strict"}
        run.write_bindings()
        run.start(allow_unready=True)
        accounts = run.get("/lain/accounts")
        expect("owned/foreign.json" not in json.dumps(accounts), "copied credential registered without ownership")
        try:
            with run.post("text", "must-not-borrow", route="fixture-borrowed") as response:
                response.read()
            raise AssertionError("unregistered owned credential dispatched")
        except HTTPError as error:
            expect(error.code == 503, f"unregistered owned credential returned {error.code}, expected 503")
            error.close()
        expect(not run.provider.snapshot() and not run.events()["events"], "borrowed route escaped to another account")
        with run.post("text", "known-account-still-works", route="fixture-a") as response:
            response.read()
        starts, _ = assert_complete(run.events()["events"], 1)
        expect(starts[0]["accountId"] == "account-a", "known account lost stable identity")
        run.stop()
        expect(credential.read_bytes() == original, "unregistered copied credential was refreshed or rewritten")
        expect(not (owned / "ownership").exists(), "copied credential was silently promoted into ownership")
        return {"copiedFileNotRegistered": True, "routeReferenceDoesNotGrantOwnership": True,
                "rejectedStatus": 503, "borrowedUpstreamAttempts": 0, "knownAccountStillWorks": True,
                "copiedCredentialUnchanged": True}
    finally:
        run.__exit__()


def unqualified_free_only(core):
    observed = []
    for name, fields in [
        ("unknown-price", {"pricingClass": "unknown", "freeOnlyEnforcement": "openrouter_zero_cap_v1"}),
        ("missing-enforcement", {"pricingClass": "free"}),
        ("forged-enforcement", {"pricingClass": "free", "freeOnlyEnforcement": "claimed-model-pin"}),
    ]:
        run = Run(core)
        try:
            # Keep the route's model otherwise qualified so model rejection cannot
            # conceal a lost pricing/enforcement check.
            run.bindings["routes"]["fixture-a"].update({"provider": "openrouter", "model": "qwen/qwen3-coder:free",
                                                       "costPolicy": "free_only", **fields})
            run.write_bindings()
            result = subprocess.run(run.command(), cwd=run.root, env=run.env, capture_output=True, timeout=10)
            expect(result.returncode != 0, f"{name}: unqualified free-only route started")
            expect(not run.provider.snapshot(), f"{name}: rejected route dispatched upstream")
            expect(not run.journal.exists() or not run.journal.read_bytes(), f"{name}: rejected route recorded dispatch")
            observed.append(name)
        finally:
            run.__exit__()
    return {"rejectedCases": observed, "upstreamAttempts": 0,
            "scope": "startup fail-closed; final translated request and fresh zero-cap proof require transport unit gate"}


def gemini_auth_header(core):
    evidence = []
    for mode in ("bearer", "default-api-key"):
        run = Run(core, provider=FakeProvider(keys={"synthetic-gemini-key": "gemini-account"}))
        try:
            base = run.provider.url.removesuffix("/v1")
            kind = "gemini:apikey"
            credential = kind + ":" + hashlib.sha256("\0".join([kind, "synthetic-gemini-key", base, "", "", ""]).encode()).hexdigest()[:12]
            run.bindings["accounts"] = {"gemini-account": credential}
            run.bindings["routes"] = {"fixture-gemini-route": {"id": "gemini-route", "model": "fixture-gemini", "accounts": ["gemini-account"], "selection": "strict"}}
            run.write_bindings()
            config = run.root / "core.yaml"
            text = config.read_text() + f'''gemini-api-key:
  - api-key: synthetic-gemini-key
    base-url: "{base}"
    models:
      - name: fixture-gemini
        alias: fixture-gemini
'''
            if mode == "bearer":
                text += "    auth-header: bearer\n"
            config.write_text(text)
            run.start()
            with run.post("text", "gemini-" + mode, route="fixture-gemini-route") as response:
                expect("synthetic response" in response.read().decode(), "Gemini translation lost text")
            rows = run.provider.snapshot()
            expect(len(rows) == 1 and rows[0]["path"] == "/v1beta/models/fixture-gemini:generateContent", "Gemini did not use its native endpoint")
            expect(rows[0]["bearerAuthorization"] == (mode == "bearer"), "Gemini bearer override was lost")
            expect(rows[0]["googleApiKeyPresent"] == (mode != "bearer"), "Gemini sent a conflicting or missing API-key header")
            starts, _ = assert_complete(run.events()["events"], 1)
            expect(starts[0]["credentialId"] == credential and starts[0]["accountId"] == "gemini-account", "Gemini attempt identity changed")
            evidence.append({"mode": mode, "nativeEndpoint": rows[0]["path"], "bearerAuthorization": rows[0]["bearerAuthorization"],
                             "googleApiKeyPresent": rows[0]["googleApiKeyPresent"], "durableAttempt": True})
        finally:
            run.__exit__()
    return {"cases": evidence, "scope": "synthetic native Gemini gateway auth headers only; no live provider claim"}


CHECKS = [("snapshot-no-refresh-import-boundary", rejected_snapshot),
          ("copied-owned-file-never-borrowed", copied_owned_credential),
          ("unqualified-free-only-fails-closed", unqualified_free_only),
          ("gemini-native-bearer-and-default-key", gemini_auth_header)]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--core", required=True)
    parser.add_argument("--report", required=True)
    args = parser.parse_args()
    data = Path(args.core).resolve(strict=True).read_bytes()
    report = {"schemaVersion": 1, "syntheticOnly": True, "coreSha256": hashlib.sha256(data).hexdigest(), "checks": []}
    with tempfile.TemporaryDirectory(prefix="lain-upgrade-boundaries-") as temporary:
        frozen = Path(temporary) / "lain-proxy"
        frozen.write_bytes(data)
        frozen.chmod(0o700)
        for name, check in CHECKS:
            try:
                report["checks"].append({"name": name, "passed": True, "evidence": check(frozen)})
                print(f"PASS {name}", flush=True)
            except Exception as error:
                report["checks"].append({"name": name, "passed": False, "error": str(error)})
                print(f"FAIL {name}: {error}", flush=True)
    report["passed"] = all(check["passed"] for check in report["checks"])
    report["pending"] = ["owned OAuth refresh lifecycle unit gate", "final translated free-only transport gate",
                         "inspector bounded memory and performance gate", "real provider and login qualification"]
    Path(args.report).write_text(json.dumps(report, indent=2) + "\n")
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
