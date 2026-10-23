#!/usr/bin/env python3
"""Stage explicit CLI/Host/Desktop publishes while preserving the qualified proxy."""
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

QUALIFIED_CORE = "8edf8abf666e3c15f1a04c20c841a969d35229f09e7de5265755ab704cf9dfc0"
PROJECTS = {"cli": "Lain.Cli", "host": "Lain.Host", "desktop": "Lain.Desktop"}


def digest(path):
    sha = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            sha.update(block)
    return sha.hexdigest()


def files(path):
    return {str(p.relative_to(path)): digest(p) for p in sorted(path.rglob("*")) if p.is_file()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("base", type=Path)
    parser.add_argument("output", type=Path, help="absolute, nonexistent staging directory")
    for component in PROJECTS:
        parser.add_argument("--" + component, required=True, nargs=3,
                            metavar=("PUBLISH", "SOURCE_COMMIT", "INFORMATIONAL_VERSION"))
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    base = args.base.resolve()
    if not args.output.is_absolute():
        parser.error("output must be absolute")
    out = args.output.resolve()
    archive = Path(str(out) + ".tar.gz")
    checksum = Path(str(archive) + ".sha256")
    if any(p.exists() or p.is_symlink() for p in (out, archive, checksum)):
        parser.error("output/archive/checksum must not exist")
    publishes = {name: Path(getattr(args, name)[0]).resolve() for name in PROJECTS}
    if any(out == p or p in out.parents or out in p.parents for p in [base, root, *publishes.values()]):
        parser.error("output must not overlap source checkout or input directories")
    for path in [base / name for name in ("build-info.json", "gate-status.json", "SHA256SUMS", "proxy/lain-proxy")]:
        if not path.is_file():
            parser.error("missing baseline input: " + str(path))
    subprocess.run(["sha256sum", "--check", "--quiet", "SHA256SUMS"], cwd=base, check=True)
    if digest(base / "proxy/lain-proxy") != QUALIFIED_CORE:
        parser.error("baseline Core is not the exact qualified SHA-256")
    gate = json.loads((base / "gate-status.json").read_text())
    if gate.get("backendQualified") is not True:
        parser.error("baseline must preserve backendQualified=true evidence")
    if gate.get("proxySha256", QUALIFIED_CORE) != QUALIFIED_CORE:
        parser.error("baseline gate records another Core SHA-256")
    protected = files(base / "proxy")
    provenance = {}
    publish_hashes = {}
    for component, project in PROJECTS.items():
        publish = publishes[component]
        _, commit, version = getattr(args, component)
        commit = subprocess.check_output(["git", "rev-parse", commit + "^{commit}"], cwd=root, text=True).strip()
        suffix = version.partition("+")[2]
        if len(suffix) < 7 or not commit.startswith(suffix):
            parser.error(component + " informational version must end in its source commit (at least 7 hex characters)")
        for name in (project, project + ".dll", project + ".deps.json", project + ".runtimeconfig.json"):
            if not (publish / name).is_file():
                parser.error("incomplete publish: " + str(publish / name))
        if not os.access(publish / project, os.X_OK):
            parser.error("launcher must be executable: " + component)
        if version.encode() not in (publish / (project + ".dll")).read_bytes():
            parser.error("claimed informational version missing from published assembly: " + component)
        if not (root / "src" / project / "obj/project.assets.json").is_file():
            parser.error("restore the current locked graph first: " + project)
        lock_path = "src/" + project + "/packages.lock.json"
        source_lock = json.loads(subprocess.check_output(
            ["git", "show", commit + ":" + lock_path], cwd=root, text=True))
        if source_lock != json.loads((root / lock_path).read_text()):
            parser.error("source commit and restored checkout have different lockfiles: " + component)
        publish_hashes[component] = files(publish)
        provenance[component] = {"sourceCommit": commit, "informationalVersion": version,
                                 "assemblySha256": digest(publish / (project + ".dll")),
                                 "lockfileSha256": digest(root / lock_path)}
    fonts = root / "src/Lain.Desktop/Assets/Fonts"
    # Each bundled family requires its original, unmodified OFL in the actual publish.
    for family in sorted(fonts.iterdir()) if fonts.is_dir() else []:
        if not family.is_dir() or not list(family.glob("*.ttf")):
            continue
        license_path = family / "OFL.txt"
        published = publishes["desktop"] / "Assets/Fonts" / family.name / "OFL.txt"
        if not license_path.is_file() or not published.is_file() or digest(license_path) != digest(published):
            parser.error("missing or changed published font OFL: " + family.name)
    shutil.copytree(base, out)
    for component, publish in publishes.items():
        shutil.rmtree(out / component)
        shutil.copytree(publish, out / component)
    shutil.rmtree(out / "notices")
    subprocess.run([sys.executable, str(root / "scripts/collect-notices.py"), str(out / "notices")], check=True)
    for component, publish in publishes.items():
        if files(publish) != publish_hashes[component] or files(out / component) != publish_hashes[component]:
            raise SystemExit("Publish input changed during staging: " + component)
    if files(base / "proxy") != protected or files(out / "proxy") != protected:
        raise SystemExit("Qualified proxy directory changed during staging")
    info = json.loads((out / "build-info.json").read_text())
    for component, record in provenance.items():
        info.setdefault("componentSourceCommits", {})[component] = record["sourceCommit"]
        info.setdefault("componentInformationalVersions", {})[component] = record["informationalVersion"]
    info["packagingCommit"] = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
    info["componentRefresh"] = {"baseManifestSha256": digest(base / "SHA256SUMS"),
                                "proxySha256": QUALIFIED_CORE, "components": provenance,
                                "packagingSourceDirty": bool(subprocess.check_output(
                                    ["git", "status", "--porcelain"], cwd=root, text=True).strip())}
    (out / "build-info.json").write_text(json.dumps(info, indent=2) + "\n")
    gate.update(releaseQualified=False, status="awaiting-current-ui-and-component-acceptance",
                uiBaselineSuperseded=True, proxySha256=QUALIFIED_CORE,
                currentRequirement="Current UI and changed application components require acceptance; inherited application evidence describes its recorded prior versions.")
    (out / "gate-status.json").write_text(json.dumps(gate, indent=2) + "\n")
    (out / "SHA256SUMS").write_text("".join(
        f"{digest(p)}  ./{p.relative_to(out)}\n" for p in sorted(out.rglob("*"))
        if p.is_file() and p != out / "SHA256SUMS"))
    subprocess.run(["sha256sum", "--check", "--quiet", "SHA256SUMS"], cwd=out, check=True)
    epoch = int(os.environ.get("SOURCE_DATE_EPOCH") or subprocess.check_output(
        ["git", "log", "-1", "--format=%ct"], cwd=root, text=True).strip())

    def normalized(member):
        member.uid = member.gid = 0
        member.uname = member.gname = ""
        member.mtime = epoch
        return member

    with archive.open("xb") as raw:
        with gzip.GzipFile(filename="", fileobj=raw, mode="wb", mtime=0, compresslevel=3) as compressed:
            with tarfile.open(fileobj=compressed, mode="w|") as tar:
                tar.add(out, arcname=out.name, filter=normalized)
    checksum.write_text(f"{digest(archive)}  {archive.name}\n")
    print(f"Staged {out}; qualified Core preserved, releaseQualified=false. Nothing installed.")


if __name__ == "__main__":
    main()
