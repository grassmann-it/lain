#!/usr/bin/env python3
"""Synthetic frozen expansion staging and refusal checks; no app/build execution."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import tempfile

root = Path(__file__).resolve().parents[1]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def snapshot(path):
    return {str(p.relative_to(path)): digest(p) for p in path.rglob("*") if p.is_file()}


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value))


with tempfile.TemporaryDirectory(prefix="lain-expansion-staging-") as temp:
    work = Path(temp)
    repo = work / "repo"
    (repo / "scripts").mkdir(parents=True)
    (repo / ".gitignore").write_text("**/obj/\n")
    for name in ["stage-expansion-release.py", "stage-component-refresh.py", "collect-notices.py"]:
        shutil.copyfile(root / "scripts" / name, repo / "scripts" / name)
    (repo / "scripts/collect-go-notices.py").write_text(
        "from pathlib import Path\nimport sys,json,hashlib\n"
        "p=Path(sys.argv[2]);p.mkdir(parents=True)\n"
        "(p/'inventory.json').write_text(json.dumps({'binarySha256':hashlib.sha256(Path(sys.argv[1]).read_bytes()).hexdigest()}))\n")
    projects = {"cli": "Lain.Cli", "host": "Lain.Host", "desktop": "Lain.Desktop"}
    for project in [*projects.values(), "Lain.Core", "Lain.Client"]:
        write(repo / "src" / project / "packages.lock.json", {"version": 1, "dependencies": {"net10.0": {}}})
        (repo / "src" / project / (project + ".csproj")).write_text('<Project Sdk="Microsoft.NET.Sdk"/>')
    for project in projects.values():
        write(repo / "src" / project / "obj/project.assets.json",
              {"libraries": {}, "packageFolders": {}, "project": {"frameworks": {}}})
    catalog = b'{"id":"fixture-provider","adapterState":"not_live_tested"}'
    catalog_path = repo / "src/Lain.Host/Providers/Catalog/fixture.json"
    catalog_path.parent.mkdir(parents=True)
    catalog_path.write_bytes(catalog)
    (catalog_path.parent.parent / "FixtureAdapter.cs").write_text("// source provenance fixture")
    note = repo / "docs/providers/fixture.md"
    note.parent.mkdir(parents=True)
    note.write_text("Official source fixture; no inference qualification.")
    write(repo / "global.json", {"sdk": {"version": "10.0.111"}})
    (repo / "Directory.Build.props").write_text('<Project/>')
    for path in ["packaging/lain", "packaging/lain.service.in", "scripts/install-linux.sh", "skills/lain-usage/SKILL.md"]:
        dest = repo / path
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_text("fixture " + path)
    write(repo / "packaging/licenses/sources.json", {"files": []})
    core_source = repo / "proxy/core"
    core_source.mkdir(parents=True)
    (core_source / "LICENSE").write_text("MIT fixture copyright")
    (core_source / "main.go").write_text("// instrumented source fixture")
    manifest = {"lainPatchVersion": 2, "snapshotVersion": "fixture-instrumented", "licenseSha256": digest(core_source / "LICENSE"),
                "files": snapshot(core_source)}
    write(repo / "proxy/source-manifest.json", manifest)
    write(repo / "proxy/toolchain.json", {"goVersion": "go1.27.2"})
    for name in ["baseline.json", "codex-model-catalog.json"]:
        write(repo / "proxy" / name, {"fixture": name})
    subprocess.run(["git", "init", "-q", str(repo)], check=True)
    subprocess.run(["git", "add", "."], cwd=repo, check=True)
    subprocess.run(["git", "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid",
                    "commit", "-qm", "frozen source"], cwd=repo, check=True)
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=repo, text=True).strip()
    version = "1.0.0+" + commit[:7]
    binary = work / "new-core"
    binary.write_bytes(b"new qualified Core fixture")
    binary.chmod(0o700)
    build = {"binarySha256": digest(binary), "sourceManifestSha256": digest(repo / "proxy/source-manifest.json"),
             "lainPatchVersion": 2, "snapshotVersion": manifest["snapshotVersion"], "goVersion": "go version go1.27.2 linux/amd64"}
    sidecar = work / "new-core.manifest.json"
    write(sidecar, build)
    qualification = work / "independent.json"
    write(qualification, {"passed": True, "sha256": digest(binary), "scope": "Independent fixture-only scope"})
    command = [sys.executable, str(repo / "scripts/stage-expansion-release.py"), "OUTPUT", "--source-commit", commit,
               "--core-source-commit", commit, "--informational-version", version, "--core", str(binary),
               "--core-build-manifest", str(sidecar), "--qualification", str(qualification)]
    for name, project in projects.items():
        publish = work / name
        publish.mkdir()
        (publish / project).write_bytes(b"synthetic executable")
        (publish / project).chmod(0o700)
        (publish / (project + ".dll")).write_bytes(
            b"\x01\x00" + bytes([len(version)]) + version.encode() + b"\x00\x00" + (catalog if name == "host" else b""))
        write(publish / (project + ".deps.json"), {"runtimeTarget": {"name": ".NETCoreApp,Version=v10.0/linux-x64"}})
        write(publish / (project + ".runtimeconfig.json"), {"runtimeOptions": {"includedFrameworks": [{"name": "Microsoft.NETCore.App", "version": "10.0.11"}]}})
        command.extend(["--" + name, str(publish)])

    def run(name, success, expected=""):
        cmd = command.copy()
        cmd[2] = str(work / name)
        result = subprocess.run(cmd, text=True, capture_output=True,
                                env={**os.environ, "PYTHONDONTWRITEBYTECODE": "1"})
        assert (result.returncode == 0) == success, result.stdout + result.stderr
        assert expected in result.stderr, result.stderr
        return work / name

    before = {name: snapshot(work / name) for name in projects}
    out = run("success", True)
    for name in projects:
        assert snapshot(out / name) == before[name] == snapshot(work / name)
    assert digest(out / "proxy/lain-proxy") == digest(binary)
    assert (out / "proxy/qualification.json").read_bytes() == qualification.read_bytes()
    gate = json.loads((out / "gate-status.json").read_text())
    assert gate["backendQualified"] and not gate["releaseQualified"] and not gate["expansionQualified"]
    assert gate["qualification"]["scope"] == "Independent fixture-only scope"
    assert len(list((out / "source-provenance/src").glob("*/packages.lock.json"))) == 5
    assert (out / "source-provenance/src/Lain.Host/Providers/Catalog/fixture.json").read_bytes() == catalog
    assert (out / "source-provenance/docs/providers/fixture.md").read_bytes() == note.read_bytes()
    with tarfile.open(str(out) + ".tar.gz") as archive:
        assert archive.getmember("success/proxy/lain-proxy").uid == 0
    run("success", False, "must not exist")
    write(qualification, {"passed": True, "sha256": "0" * 64})
    run("stale-qualification", False, "exact new Core sha256")
    write(qualification, {"passed": False, "sha256": digest(binary)})
    run("failed-qualification", False, "exact new Core sha256")
    write(qualification, {"passed": True, "sha256": digest(binary)})
    write(qualification, {"passed": True, "sha256": digest(binary), "sourceManifestSha256": "0" * 64})
    run("wrong-qualification-source", False, "qualification source-manifest hash disagrees")
    write(qualification, {"passed": True, "sha256": digest(binary)})
    write(sidecar, {**build, "sourceManifestSha256": "0" * 64})
    run("wrong-source-sidecar", False, "does not bind")
    write(sidecar, build)
    core_text = (core_source / "main.go").read_text()
    (core_source / "main.go").write_text("// changed after Core freeze")
    run("changed-core-source", False, "Core source files do not match")
    (core_source / "main.go").write_text(core_text)
    dll = work / "host/Lain.Host.dll"
    contents = dll.read_bytes()
    dll.write_bytes(contents.replace(catalog, b"missing provider resource"))
    run("missing-catalog-resource", False, "not embedded unchanged")
    dll.write_bytes(contents)
    write(repo / "src/Lain.Host/obj/project.assets.json", {"libraries": {"Unexpected/1.0": {"type": "package"}}})
    run("wrong-restore-graph", False, "restored graph differs from frozen lockfile")
    write(repo / "src/Lain.Host/obj/project.assets.json",
          {"libraries": {}, "packageFolders": {}, "project": {"frameworks": {}}})
    write(repo / "proxy/source-manifest.json", {**manifest, "lainPatchVersion": 1})
    subprocess.run(["git", "add", "proxy/source-manifest.json"], cwd=repo, check=True)
    subprocess.run(["git", "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid",
                    "commit", "-qm", "wrong patch fixture"], cwd=repo, check=True)
    wrong_commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=repo, text=True).strip()
    command[command.index("--source-commit") + 1] = wrong_commit
    command[command.index("--core-source-commit") + 1] = wrong_commit
    command[command.index("--informational-version") + 1] = "1.0.0+" + wrong_commit[:7]
    run("old-patch-source", False, "requires regenerated lainPatchVersion=2")
    print("Expansion fixtures passed: frozen component/Core provenance, exact qualification, source lock/provider provenance, original evidence scope, false release gates, archive and stale/failed/mismatched input refusals.")
