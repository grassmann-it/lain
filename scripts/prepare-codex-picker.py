#!/usr/bin/env python3
"""Reconstruct the pinned opt-in Codex client without building or installing it."""
from __future__ import annotations
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tarfile
import tempfile
import tomllib
import urllib.request


def verify_workspace_lock(before: dict, after: dict, policy: dict) -> dict:
    """Allow only the pinned local workspace release-version correction."""
    original_external = [package for package in before["package"] if "source" in package]
    updated_external = [package for package in after["package"] if "source" in package]
    if original_external != updated_external or len(original_external) != policy["externalPackages"]:
        raise ValueError("external Cargo.lock package records changed")
    original_local = [package for package in before["package"] if "source" not in package]
    updated_local = [package for package in after["package"] if "source" not in package]
    expected_local = []
    changed = 0
    for package in original_local:
        package = dict(package)
        if package["version"] == policy["oldLocalVersion"]:
            package["version"] = policy["newLocalVersion"]
            changed += 1
        expected_local.append(package)
    if expected_local != updated_local or changed != policy["changedLocalPackages"]:
        raise ValueError("local Cargo.lock changes exceed the pinned workspace version correction")
    if {k: v for k, v in before.items() if k != "package"} != {k: v for k, v in after.items() if k != "package"}:
        raise ValueError("Cargo.lock metadata changed")
    return {"externalPackageRecordsUnchanged": True, "externalPackages": len(original_external),
            "localVersionChanges": changed}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--archive", type=Path, help="already downloaded public source archive")
    parser.add_argument("--fetch", action="store_true", help="download the pinned public archive")
    parser.add_argument("--output", type=Path, required=True, help="new, empty destination (must not exist)")
    args = parser.parse_args()
    if bool(args.archive) == bool(args.fetch):
        parser.error("choose exactly one of --archive or --fetch")
    output = args.output.absolute()
    if output.exists() or output.is_symlink():
        parser.error("--output must not exist; existing source or builds are never overwritten")
    bundle = Path(__file__).resolve().parents[1] / "clients" / "codex"
    manifest = json.loads((bundle / "upstream.json").read_text())
    scratch_parent = Path(os.environ.get("TMPDIR") or Path.home() / ".cache" / "lain-build-tmp")
    scratch_parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="lain-codex-prepare-", dir=scratch_parent) as scratch:
        scratch = Path(scratch)
        if args.fetch:
            archive = scratch / "upstream.tar.gz"
            with urllib.request.urlopen(manifest["archiveUrl"], timeout=60) as source, archive.open("wb") as dest:
                shutil.copyfileobj(source, dest)
        else:
            archive = args.archive.resolve(strict=True)
        with archive.open("rb") as source:
            digest = hashlib.file_digest(source, "sha256").hexdigest()
        if digest != manifest["archiveSha256"]:
            raise SystemExit("public archive SHA-256 does not match the pinned upstream manifest")
        with tarfile.open(archive, "r:gz") as source:
            root = manifest["archiveRoot"]
            if any(not member.name.startswith(root + "/") and member.name != root for member in source.getmembers()):
                raise SystemExit("source archive has an unexpected root")
            source.extractall(scratch / "extract", filter="data")
        checkout = scratch / "extract" / root
        lock_path = checkout / "codex-rs" / "Cargo.lock"
        original_lock = tomllib.loads(lock_path.read_text())
        for relative in manifest["patches"]:
            patch = (bundle / relative).resolve(strict=True)
            if not patch.is_relative_to(bundle):
                raise SystemExit("patch path escapes bundle")
            if hashlib.sha256(patch.read_bytes()).hexdigest() != manifest["patchSha256"][relative]:
                raise SystemExit("patch SHA-256 does not match the pinned manifest")
            subprocess.run(["git", "apply", "--check", str(patch)], cwd=checkout, check=True)
            subprocess.run(["git", "apply", str(patch)], cwd=checkout, check=True)
        policy = manifest["workspaceLockNormalization"]
        checks = verify_workspace_lock(original_lock, tomllib.loads(lock_path.read_text()), policy)
        if hashlib.sha256(lock_path.read_bytes()).hexdigest() != policy["normalizedLockSha256"]:
            raise SystemExit("normalized Cargo.lock SHA-256 does not match the pinned manifest")
        provenance = dict(manifest, preparerChecks=checks)
        (checkout / "lain-picker-source.json").write_text(json.dumps(provenance, indent=2) + "\n")
        output.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(str(checkout), str(output))
    print(output)


if __name__ == "__main__":
    main()
