#!/usr/bin/env python3
"""Stage frozen expansion publishes and independently qualified patch-2 Core.

No builds, installation, process/service operations or positive release gates.
The old-Core stage-component-refresh.py guard is deliberately separate.
"""
import argparse
import gzip
import json
import os
from pathlib import Path
import runpy
import shutil
import subprocess
import sys
import tarfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path, help="absolute nonexistent package directory")
    parser.add_argument("--source-commit", required=True, help="one frozen source for all three .NET publishes")
    parser.add_argument("--informational-version", required=True, help="complete exact .NET assembly version")
    for name in ("cli", "host", "desktop"):
        parser.add_argument("--" + name, required=True, type=Path)
    parser.add_argument("--core", required=True, type=Path)
    parser.add_argument("--core-build-manifest", required=True, type=Path)
    parser.add_argument("--core-source-commit", required=True)
    parser.add_argument("--qualification", required=True, type=Path,
                        help="independent passed=true report for exact new Core sha256")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    common = runpy.run_path(str(root / "scripts/stage-component-refresh.py"))
    digest, files, serialized_version = (common[k] for k in ("digest", "files", "serialized_version"))
    projects = common["PROJECTS"]

    def git(*command):
        return subprocess.check_output(["git", *command], cwd=root)

    def source(path, commit=None):
        return git("show", (commit or revision) + ":" + path)

    def record(path, value):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(value, indent=2) + "\n")

    revision = git("rev-parse", args.source_commit + "^{commit}").decode().strip()
    core_revision = git("rev-parse", args.core_source_commit + "^{commit}").decode().strip()
    suffixes = args.informational_version.partition("+")[2].split(".")
    if not (len(suffixes) in (1, 2) and all(7 <= len(s) <= 40 and revision.startswith(s) for s in suffixes)
            and (len(suffixes) == 1 or suffixes[1] == revision)):
        parser.error("informational version must identify the exact frozen .NET source commit")
    if not args.output.is_absolute():
        parser.error("output must be absolute")
    out = args.output.resolve()
    archive = Path(str(out) + ".tar.gz")
    checksum = Path(str(archive) + ".sha256")
    if any(p.exists() or p.is_symlink() for p in (out, archive, checksum)):
        parser.error("output/archive/checksum must not exist")
    publishes = {name: getattr(args, name).resolve() for name in projects}
    external = [args.core.resolve(), args.core_build_manifest.resolve(), args.qualification.resolve()]
    if any(out == p or p in out.parents or out in p.parents for p in [root, *publishes.values(), *external]):
        parser.error("output must not overlap checkout or input paths")
    # Reject tracked drift or untracked compile inputs relative to the frozen .NET source.
    subprocess.run(["git", "diff", "--exit-code", revision, "--", "src", "Directory.Build.props", "global.json"],
                   cwd=root, check=True, stdout=subprocess.DEVNULL)
    if git("ls-files", "--others", "--exclude-standard", "src").strip():
        parser.error("untracked application source files must be resolved before packaging")
    manifest_bytes = source("proxy/source-manifest.json", core_revision)
    if source("proxy/source-manifest.json") != manifest_bytes or (root / "proxy/source-manifest.json").read_bytes() != manifest_bytes:
        parser.error("Core/final/working source manifests must be byte-identical")
    manifest = json.loads(manifest_bytes)
    if manifest.get("lainPatchVersion") != 2:
        parser.error("expansion Core requires regenerated lainPatchVersion=2 source manifest")
    actual_core_files = files(root / "proxy/core")
    if actual_core_files != manifest["files"]:
        parser.error("vendored Core source files do not match the frozen source manifest")
    import hashlib
    source_hash = hashlib.sha256(manifest_bytes).hexdigest()
    binary_hash = digest(args.core)
    build_bytes = args.core_build_manifest.read_bytes()
    build = json.loads(build_bytes)
    if (build.get("binarySha256") != binary_hash or build.get("sourceManifestSha256") != source_hash
            or build.get("lainPatchVersion") != 2 or build.get("snapshotVersion") != manifest["snapshotVersion"]):
        parser.error("Core build sidecar does not bind the exact binary and patch-2 source manifest")
    if not os.access(args.core, os.X_OK):
        parser.error("Core binary must be executable")
    qualification_bytes = args.qualification.read_bytes()
    qualification = json.loads(qualification_bytes)
    if qualification.get("passed") is not True or qualification.get("sha256") != binary_hash:
        parser.error("independent qualification must have passed=true for the exact new Core sha256")
    if qualification.get("sourceManifestSha256", source_hash) != source_hash:
        parser.error("qualification source-manifest hash disagrees with the frozen Core")
    # Preserve original license text and verify its source-manifest binding.
    license_bytes = source("proxy/core/LICENSE", core_revision)
    if hashlib.sha256(license_bytes).hexdigest() != manifest["licenseSha256"]:
        parser.error("Core license does not match source-manifest license hash")
    source_paths = git("ls-tree", "-r", "--name-only", revision).decode().splitlines()
    locks = [p for p in source_paths if p.startswith("src/") and p.endswith("/packages.lock.json")]
    required_locks = {"src/" + project + "/packages.lock.json" for project in [*projects.values(), "Lain.Core", "Lain.Client"]}
    if not required_locks.issubset(locks):
        parser.error("frozen source is missing required application/Core/Client lockfiles")
    for path in locks:
        if (root / path).read_bytes() != source(path):
            parser.error("working lockfile differs from frozen source: " + path)
    provider_paths = [p for p in source_paths if p.startswith("src/Lain.Host/Providers/Catalog/") and p.endswith(".json")]
    if not provider_paths:
        parser.error("frozen source contains no provider adapter catalog manifests")
    publish_hashes = {}
    asset_hashes = {}
    for name, project in projects.items():
        publish = publishes[name]
        for file in [project, project + ".dll", project + ".deps.json", project + ".runtimeconfig.json"]:
            if not (publish / file).is_file():
                parser.error("incomplete publish: " + str(publish / file))
        if not os.access(publish / project, os.X_OK):
            parser.error("published launcher must be executable: " + name)
        assembly = (publish / (project + ".dll")).read_bytes()
        if serialized_version(args.informational_version) not in assembly:
            parser.error("complete informational version missing from published assembly: " + name)
        deps = json.loads((publish / (project + ".deps.json")).read_text())
        if not deps.get("runtimeTarget", {}).get("name", "").endswith("/linux-x64"):
            parser.error("publish must target linux-x64: " + name)
        if not json.loads((publish / (project + ".runtimeconfig.json")).read_text()).get("runtimeOptions", {}).get("includedFrameworks"):
            parser.error("publish must be self-contained: " + name)
        assets_path = root / "src" / project / "obj/project.assets.json"
        assets = json.loads(assets_path.read_text())
        asset_hashes[assets_path] = digest(assets_path)
        lock = json.loads(source("src/" + project + "/packages.lock.json"))
        locked = {(key.lower(), entry["resolved"]) for target in lock["dependencies"].values()
                  for key, entry in target.items() if entry["type"].lower() != "project"}
        restored = {(key.rsplit("/", 1)[0].lower(), key.rsplit("/", 1)[1]) for key, entry in assets["libraries"].items()
                    if entry["type"] == "package"}
        if locked != restored:
            parser.error("restored graph differs from frozen lockfile: " + name)
        if name == "host":
            for path in provider_paths:
                json.loads(source(path))
                if source(path) not in assembly:
                    parser.error("frozen provider catalog is not embedded unchanged in Host: " + path)
        publish_hashes[name] = files(publish)
    fonts = [p for p in source_paths if p.startswith("src/Lain.Desktop/Assets/Fonts/") and p.endswith("/OFL.txt")]
    for path in fonts:
        published = publishes["desktop"] / path.removeprefix("src/Lain.Desktop/")
        if not published.is_file() or published.read_bytes() != source(path):
            parser.error("published font OFL differs from frozen source: " + path)
    # Everything copied below comes from explicit frozen inputs, never a previous UI package.
    out.mkdir(parents=True)
    for name, publish in publishes.items():
        shutil.copytree(publish, out / name)
    proxy = out / "proxy"
    proxy.mkdir()
    shutil.copy2(args.core, proxy / "lain-proxy")
    (proxy / "build-manifest.json").write_bytes(build_bytes)
    (proxy / "qualification.json").write_bytes(qualification_bytes)
    (proxy / "source-manifest.json").write_bytes(manifest_bytes)
    (proxy / "LICENSE").write_bytes(license_bytes)
    for path in ["proxy/baseline.json", "proxy/codex-model-catalog.json", "proxy/toolchain.json"]:
        (proxy / Path(path).name).write_bytes(source(path, core_revision))
    record(proxy / "binary-manifest.json", {"format": 1, "binary": "lain-proxy", "sha256": binary_hash,
                                           "sourceManifestSha256": source_hash, "lainPatchVersion": 2})
    toolchain = json.loads((proxy / "toolchain.json").read_text())
    if toolchain["goVersion"] not in build.get("goVersion", "").split():
        raise SystemExit("Core build toolchain differs from frozen pinned toolchain")
    for path in ["packaging/lain", "packaging/lain.service.in", "scripts/install-linux.sh"]:
        dest = out / ("install-linux.sh" if path.startswith("scripts/") else path)
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(source(path))
        if path in ["packaging/lain", "scripts/install-linux.sh"]:
            dest.chmod(0o700)
    # Ship the requested CLI skill from the same source commit, without installing it.
    for path in [p for p in source_paths if p.startswith("skills/lain-usage/")]:
        dest = out / path
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(source(path))
    provenance = []
    provenance_paths = sorted(set(locks + provider_paths + [p for p in source_paths if
        (p.startswith("src/") and p.endswith(".csproj")) or p in ["global.json", "Directory.Build.props"] or
        p.startswith("docs/providers/") or p.startswith("src/Lain.Host/Providers/")]))
    for path in provenance_paths:
        dest = out / "source-provenance" / path
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(source(path))
        provenance.append({"sourcePath": path, "sourceCommit": revision,
                           "path": str(dest.relative_to(out)), "sha256": digest(dest)})
    record(out / "source-provenance/inventory.json", {"format": 1, "sourceCommit": revision, "files": provenance,
        "scope": "Source locks/project definitions, provider manifests/adapters and official-source research notes; not evidence of live provider support"})
    # Collector uses restored exact graphs and original cached/pinned notice texts.
    for path in [p for p in source_paths if p.startswith("packaging/licenses/")]:
        if not (root / path).is_file() or (root / path).read_bytes() != source(path):
            raise SystemExit("Notice source differs from frozen checkout: " + path)
    subprocess.run([sys.executable, str(root / "scripts/collect-notices.py"), str(out / "notices")], check=True)
    go_notices = json.loads((out / "notices/go/inventory.json").read_text())
    if go_notices.get("binarySha256") != binary_hash:
        raise SystemExit("Go notice inventory does not describe the shipped Core")
    components = {name: {"sourceCommit": revision, "informationalVersion": args.informational_version,
        "assemblySha256": digest(out / name / (project + ".dll"))} for name, project in projects.items()}
    record(out / "build-info.json", {"format": 1, "runtime": "linux-x64", "sdk": json.loads(source("global.json"))["sdk"]["version"],
        "packagingCommit": git("rev-parse", "HEAD").decode().strip(), "componentSourceCommits": {name: revision for name in projects},
        "packagingSourceDirty": bool(git("status", "--porcelain").strip()),
        "packagingToolSha256": {name: digest(root / "scripts" / name) for name in
            ["stage-expansion-release.py", "stage-component-refresh.py", "collect-notices.py", "collect-go-notices.py"]},
        "componentInformationalVersions": {name: args.informational_version for name in projects},
        "components": components, "coreSourceCommit": core_revision, "proxySha256": binary_hash,
        "coreBuildManifestSha256": digest(proxy / "build-manifest.json"), "sourceManifestSha256": source_hash,
        "qualificationSha256": digest(proxy / "qualification.json"), "sourceProvenanceInventorySha256": digest(out / "source-provenance/inventory.json")})
    record(out / "gate-status.json", {"format": 1, "runtime": "linux-x64", "backendQualified": True,
        "releaseQualified": False, "expansionQualified": False, "status": "awaiting-independent-full-package-and-installed-expansion-acceptance",
        "proxySha256": binary_hash, "sourceManifestSha256": source_hash,
        "qualification": {"path": "proxy/qualification.json", "sha256": digest(proxy / "qualification.json"),
                          "scope": qualification.get("scope", "Scope is defined only by the independent qualification report")},
        "limits": ["Exact Core qualification does not qualify package, GUI, migration or installed workflow.",
                   "Provider manifests and research do not establish live credentials, inference or free-only eligibility."]})
    for name, expected in publish_hashes.items():
        if files(publishes[name]) != expected or files(out / name) != expected:
            raise SystemExit("Publish input changed during staging: " + name)
    if digest(args.core) != binary_hash or digest(proxy / "lain-proxy") != binary_hash:
        raise SystemExit("Core input changed during staging")
    if args.core_build_manifest.read_bytes() != build_bytes or args.qualification.read_bytes() != qualification_bytes:
        raise SystemExit("Core build/qualification evidence changed during staging")
    if any(digest(path) != expected for path, expected in asset_hashes.items()):
        raise SystemExit("Restored application graph changed during notice collection")
    subprocess.run(["git", "diff", "--exit-code", revision, "--", "src", "Directory.Build.props", "global.json"],
                   cwd=root, check=True, stdout=subprocess.DEVNULL)
    (out / "SHA256SUMS").write_text("".join(f"{digest(p)}  ./{p.relative_to(out)}\n" for p in sorted(out.rglob("*"))
        if p.is_file() and p != out / "SHA256SUMS"))
    subprocess.run(["sha256sum", "--check", "--quiet", "SHA256SUMS"], cwd=out, check=True)
    epoch = int(os.environ.get("SOURCE_DATE_EPOCH") or git("show", "-s", "--format=%ct", revision).decode().strip())

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
    print(f"Staged {out}; exact patch-2 Core qualification retained, release/expansion gates false. Nothing installed.")


if __name__ == "__main__":
    main()
