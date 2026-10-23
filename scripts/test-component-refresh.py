#!/usr/bin/env python3
"""Exercise packaging guards with synthetic publishes; no application execution."""
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


def version_blob(value):
    # ECMA-335 custom attribute prolog, SerString byte length, value, zero named args.
    return b"fixture DLL\x01\x00" + bytes([len(value)]) + value.encode() + b"\x00\x00end"


with tempfile.TemporaryDirectory(prefix="lain-component-refresh-") as temp:
    work = Path(temp)
    repo = work / "repo"
    (repo / "scripts").mkdir(parents=True)
    for script in ("stage-component-refresh.py", "collect-notices.py"):
        shutil.copyfile(root / "scripts" / script, repo / "scripts" / script)
    assert "8edf8abf666e3c15f1a04c20c841a969d35229f09e7de5265755ab704cf9dfc0" in (
        repo / "scripts/stage-component-refresh.py").read_text()
    # Production Core guard has no override. This isolated import changes only the
    # fixture module constant, allowing successful-path testing with a tiny binary.
    (repo / "invoke.py").write_text(
        "import sys\nfrom scripts import stage_component_fixture as stage\n"
        "stage.QUALIFIED_CORE=sys.argv.pop(1)\nstage.main()\n")
    shutil.copyfile(repo / "scripts/stage-component-refresh.py", repo / "scripts/stage_component_fixture.py")
    (repo / "scripts/collect-go-notices.py").write_text(
        "import pathlib,sys\np=pathlib.Path(sys.argv[2]);p.mkdir(parents=True)\n"
        "(p/'inventory.json').write_text('{}')\n")
    licenses = repo / "packaging/licenses"
    licenses.mkdir(parents=True)
    (licenses / "sources.json").write_text('{"files":[]}')
    projects = {"cli": "Lain.Cli", "host": "Lain.Host", "desktop": "Lain.Desktop"}
    for project in projects.values():
        obj = repo / "src" / project / "obj"
        obj.mkdir(parents=True)
        (obj / "project.assets.json").write_text(json.dumps(
            {"libraries": {}, "packageFolders": {}, "project": {"frameworks": {}}}))
        (obj.parent / "packages.lock.json").write_text('{"version":1,"dependencies":{}}')
    font = repo / "src/Lain.Desktop/Assets/Fonts/FixtureFamily"
    font.mkdir(parents=True)
    (font / "Fixture.ttf").write_bytes(b"synthetic embedded font")
    (font / "OFL.txt").write_text("Fixture copyright\nSIL Open Font License 1.1\n")
    subprocess.run(["git", "init", "-q", str(repo)], check=True)
    subprocess.run(["git", "add", "."], cwd=repo, check=True)
    subprocess.run(["git", "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid",
                    "commit", "-qm", "fixture"], cwd=repo, check=True)
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=repo, text=True).strip()
    short_version = "1.0.0+" + commit[:7]
    versions = {"cli": short_version + "." + commit, "host": short_version,
                "desktop": "1.0.0+" + commit}
    base = work / "base"
    for name in (*projects, "proxy", "notices"):
        (base / name).mkdir(parents=True)
        (base / name / "old.txt").write_text("old " + name)
    (base / "proxy/lain-proxy").write_bytes(b"synthetic qualified Core")
    core = digest(base / "proxy/lain-proxy")
    (base / "build-info.json").write_text('{"backendQualificationCommit":"preserve-me"}')
    (base / "gate-status.json").write_text(json.dumps(
        {"backendQualified": True, "releaseQualified": True, "proxySha256": core}))

    def manifest():
        (base / "SHA256SUMS").write_text("".join(
            f"{value}  ./{key}\n" for key, value in sorted(snapshot(base).items()) if key != "SHA256SUMS"))

    manifest()
    command = [sys.executable, str(repo / "invoke.py"), core, str(base), "OUTPUT"]
    for component, project in projects.items():
        version = versions[component]
        publish = work / (component + "-publish")
        publish.mkdir()
        for name, content in ((project, b"synthetic launcher"), (project + ".dll", version_blob(version)),
                              (project + ".deps.json", b"{}"), (project + ".runtimeconfig.json", b"{}")):
            (publish / name).write_bytes(content)
        (publish / project).chmod(0o700)
        if component == "desktop":
            dest = publish / "Assets/Fonts/FixtureFamily"
            dest.mkdir(parents=True)
            shutil.copyfile(font / "OFL.txt", dest / "OFL.txt")
        command.extend(["--" + component, str(publish), commit, version])

    def run(name, success, expected="", override=None):
        cmd = (override or command).copy()
        cmd[4] = str(work / name)
        result = subprocess.run(cmd, capture_output=True, text=True,
                                env={**os.environ, "PYTHONDONTWRITEBYTECODE": "1"})
        assert (result.returncode == 0) == success, result.stdout + result.stderr
        assert expected in result.stderr, result.stderr
        return work / name

    before = snapshot(base)
    out = run("success", True)
    assert snapshot(base) == before
    assert snapshot(base / "proxy") == snapshot(out / "proxy")
    for component in projects:
        assert snapshot(out / component) == snapshot(work / (component + "-publish"))
        assert not (out / component / "old.txt").exists()
    gate = json.loads((out / "gate-status.json").read_text())
    assert gate["backendQualified"] and not gate["releaseQualified"]
    info = json.loads((out / "build-info.json").read_text())
    assert info["backendQualificationCommit"] == "preserve-me"
    assert all(value == commit for value in info["componentSourceCommits"].values())
    assert info["componentInformationalVersions"] == versions
    notices = json.loads((out / "notices/inventory.json").read_text())
    assert len(notices["bundledFonts"]) == 1
    assert (out / "notices/fonts/FixtureFamily/OFL.txt").read_bytes() == (font / "OFL.txt").read_bytes()
    archive = Path(str(out) + ".tar.gz")
    with tarfile.open(archive) as tar:
        assert tar.getmember("success/proxy/lain-proxy").uid == 0
    assert Path(str(archive) + ".sha256").read_text().split()[0] == digest(archive)
    run("success", False, "must not exist")
    partial = command.copy()
    partial[partial.index("--cli") + 3] = short_version
    run("truncated-sdk-version", False, "claimed informational version missing", partial)
    mismatch = command.copy()
    mismatch[mismatch.index("--cli") + 3] = short_version + "." + ("0" if commit[0] != "0" else "1") + commit[1:]
    run("mismatched-sdk-revision", False, "same full SHA", mismatch)
    mismatch[mismatch.index("--cli") + 3] = "1.0.0+" + ("0" if commit[0] != "0" else "1") + commit[1:7] + "." + commit
    run("mismatched-short-revision", False, "same full SHA", mismatch)
    dll = work / "host-publish/Lain.Host.dll"
    dll.write_text("wrong provenance")
    run("wrong-version", False, "claimed informational version missing")
    dll.write_bytes(version_blob(versions["host"]))
    lock = repo / "src/Lain.Host/packages.lock.json"
    lock.write_text('{"version":1,"dependencies":{"changed":{}}}')
    run("wrong-lock", False, "different lockfiles")
    lock.write_text('{"version":1,"dependencies":{}}')
    published_ofl = work / "desktop-publish/Assets/Fonts/FixtureFamily/OFL.txt"
    published_ofl.write_text("wrong notice")
    run("wrong-ofl", False, "missing or changed published font OFL")
    shutil.copyfile(font / "OFL.txt", published_ofl)
    (base / "proxy/lain-proxy").write_bytes(b"different Core")
    manifest()
    run("wrong-core", False, "not the exact qualified SHA-256")
    print("Component staging fixtures passed: provenance, Core preservation/refusal, unchanged inputs, font notices, gate=false, archive, stale-output and missing-license guards.")
