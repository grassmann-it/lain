#!/usr/bin/env python3
"""Reconstruct the pinned opt-in Codex client without building or installing it."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tarfile
import tempfile
import urllib.request


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
    with tempfile.TemporaryDirectory(prefix="lain-codex-prepare-") as scratch:
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
        for relative in manifest["patches"]:
            patch = (bundle / relative).resolve(strict=True)
            if not patch.is_relative_to(bundle):
                raise SystemExit("patch path escapes bundle")
            subprocess.run(["git", "apply", "--check", str(patch)], cwd=checkout, check=True)
            subprocess.run(["git", "apply", str(patch)], cwd=checkout, check=True)
        (checkout / "lain-picker-source.json").write_text(json.dumps(manifest, indent=2) + "\n")
        output.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(str(checkout), str(output))
    print(output)


if __name__ == "__main__":
    main()
