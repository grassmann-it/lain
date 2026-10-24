#!/usr/bin/env python3
"""Verify and assemble the pinned external core plus the independent lain adapter.

Never modifies the Go module cache or the checkout. The destination contains a
private upstream tree with explicitly versioned patches and a separate adapter.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def verify():
    manifest = json.loads((ROOT / "proxy/source-manifest.json").read_text())
    if manifest.get("schemaVersion") != 2:
        raise ValueError("external core source manifest schema 2 required")
    expected = manifest["buildInputs"]
    actual = {str(p.relative_to(ROOT)): digest(p) for base in ("proxy/adapter", "proxy/patches") for p in (ROOT / base).rglob("*") if p.is_file()}
    for name in ("proxy/upstream.json", "proxy/toolchain.json", "proxy/LICENSE.upstream", "scripts/build-core.sh", "scripts/build-core-verify.py", "scripts/prepare-core.py"):
        actual[name] = digest(ROOT / name)
    if actual != expected:
        changed = sorted(k for k in actual.keys() | expected.keys() if actual.get(k) != expected.get(k))
        raise ValueError("core input manifest mismatch: " + ", ".join(changed))
    lock = json.loads((ROOT / "proxy/upstream.json").read_text())
    for patch in lock["patches"]:
        if digest(ROOT / "proxy" / patch["path"]) != patch["sha256"]:
            raise ValueError("upstream patch checksum mismatch: " + patch["path"])
    return lock

def prepare(destination, archive=None):
    lock = verify()
    destination = destination.resolve()
    destination.mkdir(parents=True, exist_ok=False, mode=0o700)
    if archive is None:
        cache = Path(os.environ.get("XDG_CACHE_HOME", Path.home() / ".cache")) / "lain/core-sources"
        cache.mkdir(parents=True, exist_ok=True, mode=0o700)
        archive = cache / (lock["archiveSha256"] + ".zip")
        if not archive.exists():
            with tempfile.NamedTemporaryFile(dir=cache, prefix="download-", delete=False) as output:
                temporary = Path(output.name)
                try:
                    with urllib.request.urlopen(lock["archiveUrl"], timeout=60) as response:
                        shutil.copyfileobj(response, output)
                    output.flush()
                    os.fsync(output.fileno())
                    if digest(temporary) != lock["archiveSha256"]:
                        raise ValueError("official upstream archive checksum mismatch")
                    temporary.replace(archive)
                finally:
                    temporary.unlink(missing_ok=True)
    if digest(archive) != lock["archiveSha256"]:
        raise ValueError("official upstream archive checksum mismatch")
    upstream = destination / "upstream"
    upstream.mkdir(mode=0o700)
    prefix = lock["module"] + "@" + lock["version"] + "/"
    with zipfile.ZipFile(archive) as bundle:
        for entry in bundle.infolist():
            if not entry.filename.startswith(prefix):
                raise ValueError("unexpected module archive prefix")
            relative = Path(entry.filename[len(prefix):])
            if relative.is_absolute() or ".." in relative.parts or (entry.external_attr >> 16) & 0o170000 == 0o120000:
                raise ValueError("unsafe source archive member")
            target = upstream / relative
            if entry.is_dir():
                target.mkdir(parents=True, exist_ok=True)
                continue
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(bundle.read(entry))
    for patch in lock["patches"]:
        path = ROOT / "proxy" / patch["path"]
        subprocess.run(["git", "apply", "--check", str(path)], cwd=upstream, check=True)
        subprocess.run(["git", "apply", str(path)], cwd=upstream, check=True)
    adapter = destination / "adapter"
    shutil.copytree(ROOT / "proxy/adapter", adapter)
    module_path = adapter / "go.mod"
    module_path.write_text(module_path.read_text().rstrip() + "\n\nreplace " + lock["module"] + " => ../upstream\n")
    provenance = {
        "schemaVersion": 1,
        "sourceManifestSha256": digest(ROOT / "proxy/source-manifest.json"),
        "upstreamVersion": lock["version"], "upstreamCommit": lock["commit"],
        "upstreamArchiveSha256": lock["archiveSha256"],
        "patchSetSha256": hashlib.sha256(json.dumps(lock["patches"], sort_keys=True).encode()).hexdigest(),
        "files": {str(p.relative_to(destination)): digest(p) for base in (upstream, adapter) for p in sorted(base.rglob("*")) if p.is_file()},
    }
    prepared_manifest = destination / "prepared-manifest.json"
    prepared_manifest.write_text(json.dumps(provenance, indent=2) + "\n")
    return {"upstream": str(upstream), "adapter": str(adapter), "preparedManifest": str(prepared_manifest), "sourceManifestSha256": provenance["sourceManifestSha256"], "upstreamVersion": lock["version"], "upstreamCommit": lock["commit"]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--destination", type=Path)
    parser.add_argument("--archive", type=Path, help="optional already downloaded official Go module zip")
    parser.add_argument("--verify-only", action="store_true")
    parser.add_argument("--verify-prepared", type=Path, help="verify an existing prepared-manifest.json against this source lock")
    args = parser.parse_args()
    if args.verify_prepared:
        verify()
        provenance = json.loads(args.verify_prepared.read_text())
        if provenance["sourceManifestSha256"] != digest(ROOT / "proxy/source-manifest.json"):
            raise ValueError("prepared source manifest does not match this checkout")
        destination = args.verify_prepared.resolve().parent
        actual = {str(p.relative_to(destination)): digest(p) for name in ("upstream", "adapter") for p in (destination / name).rglob("*") if p.is_file()}
        if actual != provenance["files"]:
            raise ValueError("prepared source tree was modified")
        print("Verified prepared source tree " + str(destination))
        return
    if args.verify_only:
        lock = verify()
        print("Verified independent adapter and pinned upstream " + lock["version"])
        return
    if args.destination is None:
        parser.error("--destination is required unless --verify-only is selected")
    print(json.dumps(prepare(args.destination, args.archive)))

if __name__ == "__main__":
    main()
