#!/usr/bin/env python3
"""Stage a desktop-only package refresh; never modify inputs or install anything."""
import argparse
import gzip
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def files(path):
    return {str(p.relative_to(path)): digest(p) for p in sorted(path.rglob("*")) if p.is_file()}


def backend_graph(inventory):
    return sorted((p["id"].lower(), p["version"], project)
                  for p in inventory["packages"] for project in p["projects"]
                  if project != "Lain.Desktop")


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("base", type=Path, help="qualified baseline package (read only)")
parser.add_argument("desktop", type=Path, help="fresh self-contained desktop publish (read only)")
parser.add_argument("output", type=Path, help="absolute, nonexistent staging package directory")
parser.add_argument("--desktop-source-commit", required=True)
parser.add_argument("--desktop-informational-version", required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
base, desktop = args.base.resolve(), args.desktop.resolve()
out = args.output
if not out.is_absolute():
    parser.error("output must be absolute")
out = out.resolve()
archive = Path(str(out) + ".tar.gz")
archive_hash = Path(str(archive) + ".sha256")
if any(p.exists() for p in (out, archive, archive_hash)):
    parser.error("output, archive and archive checksum must not exist")
if any(out == p or p in out.parents for p in (base, desktop, root)):
    parser.error("output must be outside source checkout and both input directories")
for path in (base / "build-info.json", base / "gate-status.json", base / "SHA256SUMS",
             base / "notices/inventory.json", desktop / "Lain.Desktop", desktop / "Lain.Desktop.deps.json"):
    if not path.is_file():
        parser.error("missing required input: " + str(path))
for project in ("Lain.Cli", "Lain.Host", "Lain.Desktop"):
    if not (root / "src" / project / "obj/project.assets.json").is_file():
        parser.error("restore all application projects before staging: " + project)
if not os.access(desktop / "Lain.Desktop", os.X_OK):
    parser.error("desktop launcher must be executable")
source_commit = subprocess.check_output(
    ["git", "rev-parse", args.desktop_source_commit + "^{commit}"], cwd=root, text=True).strip()
packaging_commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
# Validate the read-only baseline before trusting its files or inherited evidence.
subprocess.run(["sha256sum", "--check", "--quiet", "SHA256SUMS"], cwd=base, check=True)
protected = {name: files(base / name) for name in ("cli", "host", "proxy")}
if any(not values for values in protected.values()):
    parser.error("baseline cli, host and proxy directories must be populated")
old_inventory = json.loads((base / "notices/inventory.json").read_text())
shutil.copytree(base, out)
shutil.rmtree(out / "desktop")
shutil.copytree(desktop, out / "desktop")
# A fresh inventory prevents old dependency notices being mistaken for current ones.
shutil.rmtree(out / "notices")
subprocess.run([sys.executable, str(root / "scripts/collect-notices.py"), str(out / "notices")], check=True)
new_inventory = json.loads((out / "notices/inventory.json").read_text())
if backend_graph(old_inventory) != backend_graph(new_inventory):
    raise SystemExit("Non-desktop NuGet graph changed; incomplete staging output must not be installed")
for name, expected in protected.items():
    if files(out / name) != expected or files(base / name) != expected:
        raise SystemExit("Protected component changed: " + name)
info = json.loads((out / "build-info.json").read_text())
info.setdefault("componentSourceCommits", {})["desktop"] = source_commit
info.setdefault("componentInformationalVersions", {})["desktop"] = args.desktop_informational_version
info["packagingCommit"] = packaging_commit
info["desktopRefresh"] = {
    "baseManifestSha256": digest(base / "SHA256SUMS"),
    "sourceCommit": source_commit,
    "packagingSourceDirty": bool(subprocess.check_output(
        ["git", "status", "--porcelain"], cwd=root, text=True).strip()),
    "protectedComponentFilesUnchanged": True,
}
(out / "build-info.json").write_text(json.dumps(info, indent=2) + "\n")
gate = json.loads((out / "gate-status.json").read_text())
gate.update(releaseQualified=False, status="awaiting-redesigned-desktop-visual-acceptance",
            uiBaselineSuperseded=True, previousInstalledAcceptancePreserved=True,
            currentRequirement="Redesigned desktop requires visual and installed desktop acceptance; inherited desktop evidence describes the previous design.")
(out / "gate-status.json").write_text(json.dumps(gate, indent=2) + "\n")
manifest = "".join(f"{digest(path)}  ./{path.relative_to(out)}\n"
                   for path in sorted(out.rglob("*")) if path.is_file() and path != out / "SHA256SUMS")
(out / "SHA256SUMS").write_text(manifest)
subprocess.run(["sha256sum", "--check", "--quiet", "SHA256SUMS"], cwd=out, check=True)
epoch = int(os.environ.get("SOURCE_DATE_EPOCH") or subprocess.check_output(
    ["git", "log", "-1", "--format=%ct"], cwd=root, text=True).strip())


def normalized(member):
    member.uid = member.gid = 0
    member.uname = member.gname = ""
    member.mtime = epoch
    return member


with archive.open("xb") as raw:
    with gzip.GzipFile(filename="", fileobj=raw, mode="wb", mtime=0) as compressed:
        with tarfile.open(fileobj=compressed, mode="w|") as tar:
            tar.add(out, arcname=out.name, filter=normalized)
archive_hash.write_text(f"{digest(archive)}  {archive.name}\n")
print(f"Staged {out}; backend files unchanged, releaseQualified=false. Nothing installed.")
